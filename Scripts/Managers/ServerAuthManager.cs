using UnityEngine;
using Mirror;
using RPG.Data;
using RPG.Managers;
using System.Collections.Generic;
using System.Collections;

namespace RPG.Network
{
    /// <summary>
    /// Lado servidor da autenticação.
    /// Mantém sessões por connectionId e processa requisições de login,
    /// criação de conta, listagem e seleção de personagem.
    ///
    /// Limpa sessões ociosas (Unauthenticated/Authenticated) por TTL.
    /// Sessões InGame só são removidas em OnServerDisconnect.
    /// </summary>
    public class ServerAuthManager : MonoBehaviour
    {
        public static ServerAuthManager Instance { get; private set; }

        private const int   LOGIN_MAX_ATTEMPTS  = 5;
        private const float SESSION_TTL_SECONDS = 300f; // 5 min sem atividade
        private const float CLEANUP_INTERVAL    = 60f;

        [Header("Debug")]
        [Tooltip("Logs detalhados do fluxo de auth. DESATIVE em produção.")]
        [SerializeField] private bool debugAuth = false;

        private enum ConnState { Unauthenticated, Authenticated, InGame }

        private class ConnData
        {
            public ConnState   State           = ConnState.Unauthenticated;
            public string      Username        = "";
            public string      CharacterId     = "";
            public AccountData CachedAccount;
            public int         LoginAttempts;
            public string      SessionNonce    = "";
            public float       LastActivityTime;

            public ConnData() => LastActivityTime = Time.time;
        }

        private readonly Dictionary<int, ConnData> _sessions = new();
        private Coroutine _cleanupCoroutine;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_cleanupCoroutine != null) StopCoroutine(_cleanupCoroutine);
        }

        public void RegisterHandlers()
        {
            NetworkServer.RegisterHandler<MsgLoginRequest>          (OnLoginRequest,           false);
            NetworkServer.RegisterHandler<MsgCreateAccountRequest>  (OnCreateAccountRequest,   false);
            NetworkServer.RegisterHandler<MsgRequestCharacterList>  (OnRequestCharacterList,   false);
            NetworkServer.RegisterHandler<MsgCreateCharacterRequest>(OnCreateCharacterRequest, false);
            NetworkServer.RegisterHandler<MsgSelectCharacter>       (OnSelectCharacter,        false);

            _cleanupCoroutine = StartCoroutine(CleanupExpiredSessions());
            Debug.Log("[ServerAuthManager] Handlers registrados.");
        }

        public void OnServerConnect(NetworkConnectionToClient conn)
        {
            var session = new ConnData { SessionNonce = GameManager.GenerateNonce() };
            _sessions[conn.connectionId] = session;

            conn.Send(new MsgAuthChallenge { Nonce = session.SessionNonce });
            LogAuth($"Nova conexão: {conn.connectionId} | nonce enviado.");
        }

        public void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            _sessions.Remove(conn.connectionId);
        }

        // ══════════════════════════════════════════════════════════════════
        // Login
        // ══════════════════════════════════════════════════════════════════

        private void OnLoginRequest(NetworkConnectionToClient conn, MsgLoginRequest msg)
        {
            if (!_sessions.TryGetValue(conn.connectionId, out var session))
            {
                conn.Send(new MsgLoginResponse { Success = false, Error = "Sessão inválida." });
                return;
            }

            if (session.State != ConnState.Unauthenticated)
            {
                conn.Send(new MsgLoginResponse { Success = false, Error = "Já autenticado." });
                return;
            }

            session.LoginAttempts++;
            if (session.LoginAttempts > LOGIN_MAX_ATTEMPTS)
            {
                Debug.LogWarning($"[ServerAuth] SECURITY: conn:{conn.connectionId} excedeu tentativas.");
                conn.Send(new MsgLoginResponse { Success = false, Error = "Muitas tentativas. Tente mais tarde." });
                conn.Disconnect();
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.Username) || string.IsNullOrWhiteSpace(msg.SignedHash))
            {
                conn.Send(new MsgLoginResponse { Success = false, Error = "Dados de login inválidos." });
                return;
            }

            if (string.IsNullOrWhiteSpace(session.SessionNonce))
            {
                Debug.LogError($"[ServerAuth] SessionNonce vazio para conn:{conn.connectionId}.");
                conn.Send(new MsgLoginResponse { Success = false, Error = "Erro de sessão. Reconecte." });
                return;
            }

            var account = DatabaseManager.Instance?.TryLoginWithSignedHash(
                msg.Username, msg.SignedHash, session.SessionNonce);

            if (account == null)
            {
                string attempts = $"({session.LoginAttempts}/{LOGIN_MAX_ATTEMPTS})";
                conn.Send(new MsgLoginResponse
                {
                    Success = false,
                    Error   = $"Usuário ou senha incorretos. {attempts}"
                });
                return;
            }

            session.State            = ConnState.Authenticated;
            session.Username         = account.Username;
            session.CachedAccount    = account;
            session.LoginAttempts    = 0;
            session.LastActivityTime = Time.time;

            conn.Send(new MsgLoginResponse { Success = true, Username = account.Username });
            SendCharacterList(conn, account);

            Debug.Log($"[ServerAuth] Login OK: {account.Username}");
        }

        // ══════════════════════════════════════════════════════════════════
        // Criar conta
        // ══════════════════════════════════════════════════════════════════

        private void OnCreateAccountRequest(NetworkConnectionToClient conn, MsgCreateAccountRequest msg)
        {
            if (string.IsNullOrWhiteSpace(msg.Username))
            {
                conn.Send(new MsgCreateAccountResponse { Success = false, Error = "Username inválido." });
                return;
            }
            if (string.IsNullOrWhiteSpace(msg.PasswordHash))
            {
                conn.Send(new MsgCreateAccountResponse { Success = false, Error = "Senha inválida." });
                return;
            }

            var error = DatabaseManager.Instance?.TryCreateAccount(msg.Username, msg.PasswordHash);
            if (error != null)
            {
                conn.Send(new MsgCreateAccountResponse { Success = false, Error = error });
                return;
            }
            conn.Send(new MsgCreateAccountResponse { Success = true });
            Debug.Log($"[ServerAuth] Conta criada: {msg.Username}");
        }

        // ══════════════════════════════════════════════════════════════════
        // Lista / criar / selecionar personagens
        // ══════════════════════════════════════════════════════════════════

        private void OnRequestCharacterList(NetworkConnectionToClient conn, MsgRequestCharacterList msg)
        {
            if (!RequireAuth(conn, out var session)) return;
            UpdateActivity(session);

            var chars = DatabaseManager.Instance?.LoadCharacters(session.Username)
                        ?? new List<CharacterData>();
            SendCharacterList(conn, session.Username, chars);
        }

        private void SendCharacterList(NetworkConnectionToClient conn, AccountData account)
            => SendCharacterList(conn, account.Username, account.Characters ?? new List<CharacterData>());

        private void SendCharacterList(NetworkConnectionToClient conn, string username, List<CharacterData> chars)
        {
            var list = new List<CharacterSummary>();
            foreach (var ch in chars)
                list.Add(new CharacterSummary
                {
                    CharacterId   = ch.CharacterId,
                    CharacterName = ch.CharacterName,
                    Race          = ch.Race.ToString(),
                    Level         = ch.Level
                });
            conn.Send(new MsgCharacterListResponse { Characters = list });
        }

        private void OnCreateCharacterRequest(NetworkConnectionToClient conn, MsgCreateCharacterRequest msg)
        {
            if (!RequireAuth(conn, out var session)) return;
            UpdateActivity(session);

            var error = DatabaseManager.Instance?.TryCreateCharacter(
                session.Username, msg.Name, (CharacterRace)msg.RaceIndex);

            if (error != null)
            {
                conn.Send(new MsgCreateCharacterResponse { Success = false, Error = error });
                return;
            }

            var chars = DatabaseManager.Instance?.LoadCharacters(session.Username)
                        ?? new List<CharacterData>();
            var list = new List<CharacterSummary>();
            foreach (var ch in chars)
                list.Add(new CharacterSummary
                {
                    CharacterId   = ch.CharacterId,
                    CharacterName = ch.CharacterName,
                    Race          = ch.Race.ToString(),
                    Level         = ch.Level
                });

            conn.Send(new MsgCreateCharacterResponse { Success = true, UpdatedList = list });
            Debug.Log($"[ServerAuth] Personagem criado: {msg.Name} (conta:{session.Username})");
        }

        private void OnSelectCharacter(NetworkConnectionToClient conn, MsgSelectCharacter msg)
        {
            if (!RequireAuth(conn, out var session)) return;

            if (session.State == ConnState.InGame)
            {
                conn.Send(new MsgSelectCharacterResponse { Success = false, Error = "Já está em jogo." });
                return;
            }

            var charData = DatabaseManager.Instance?.LoadCharacterForAccount(
                msg.CharacterId, session.Username);

            if (charData == null)
            {
                conn.Send(new MsgSelectCharacterResponse
                {
                    Success = false,
                    Error   = "Personagem não encontrado ou não pertence a esta conta."
                });
                Debug.LogWarning($"[ServerAuth] SECURITY: {session.Username} tentou selecionar {msg.CharacterId}");
                return;
            }

            session.State        = ConnState.InGame;
            session.CharacterId  = msg.CharacterId;
            UpdateActivity(session);

            RPGNetworkManager.singleton?.SpawnPlayerForConnection(conn, charData, session.Username);
            Debug.Log($"[ServerAuth] {charData.CharacterName} ({charData.Race}) entrando | conn:{conn.connectionId}");
        }

        // ══════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════

        private bool RequireAuth(NetworkConnectionToClient conn, out ConnData session)
        {
            if (!_sessions.TryGetValue(conn.connectionId, out session))
            {
                conn.Send(new MsgErrorResponse { Error = "Sessão inválida." });
                return false;
            }
            if (session.State == ConnState.Unauthenticated)
            {
                conn.Send(new MsgErrorResponse { Error = "Não autenticado." });
                return false;
            }
            return true;
        }

        private static void UpdateActivity(ConnData session)
            => session.LastActivityTime = Time.time;

        private void LogAuth(string msg)
        {
            if (debugAuth) Debug.Log($"[ServerAuth-DEBUG] {msg}");
        }

        // ══════════════════════════════════════════════════════════════════
        // Limpeza de sessões expiradas
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Remove sessões ociosas há mais de SESSION_TTL_SECONDS.
        /// Estados:
        ///   Unauthenticated → limpa por timer
        ///   Authenticated   → limpa por timer (idle, sem entrar no jogo)
        ///   InGame          → NUNCA limpa por timer (apenas via disconnect)
        /// </summary>
        private IEnumerator CleanupExpiredSessions()
        {
            var wait = new WaitForSeconds(CLEANUP_INTERVAL);
            var expired = new List<int>();

            while (true)
            {
                yield return wait;

                expired.Clear();
                foreach (var kv in _sessions)
                {
                    if (kv.Value.State == ConnState.InGame) continue;
                    if (Time.time - kv.Value.LastActivityTime > SESSION_TTL_SECONDS)
                        expired.Add(kv.Key);
                }

                foreach (var id in expired)
                {
                    var state = _sessions[id].State;
                    _sessions.Remove(id);
                    Debug.Log($"[ServerAuthManager] Sessão expirada removida: connId={id} estado={state}");
                }
            }
        }
    }
}
