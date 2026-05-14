using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using Mirror;
using RPG.Character;
using RPG.UI;
using RPG.Combat;

namespace RPG.Network
{
    /// <summary>
    /// Input do jogador local: mouse para mover/atacar/selecionar, teclado para
    /// skills, câmera orbital com anti-oclusão.
    ///
    /// === REESCRITA: MOVIMENTO ESTILO MMO (Ragnarok/PoE) ===
    ///
    ///   1. CLICK & HOLD: clique simples move até o ponto; SEGURAR o botão
    ///      esquerdo faz o player seguir continuamente para onde o mouse
    ///      aponta no chão (re-target a cada UPDATE_TARGET_INTERVAL).
    ///
    ///   2. REDIRECIONAMENTO SEM PARAR: clicar em outro lugar enquanto o
    ///      player se move NÃO faz ResetPath() nem zera velocity. Apenas
    ///      troca o destino — o agent muda de direção fluidamente.
    ///
    ///   3. CANCELAMENTOS INTELIGENTES: auto-ataque e walk-to-skill são
    ///      cancelados sem tocar no agent quando o player vai continuar
    ///      se movendo (apenas trocando destino).
    ///
    ///   4. THROTTLE DE Cmd: o CmdMoveTo é enviado em rate fixo, não a
    ///      cada movimento de mouse. Reduz tráfego sem perder responsividade.
    ///
    ///   5. PREDIÇÃO LOCAL LIMPA: o cliente local atualiza o agent
    ///      imediatamente; o servidor confirma de forma assíncrona via
    ///      NetworkTransform. Não há mais "fight" porque não cancelamos
    ///      o path corrente.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NetworkPlayerController : NetworkBehaviour
    {
        [Header("Layers")]
        [SerializeField] private LayerMask terrainLayer;
        [SerializeField] private LayerMask targetableLayer;
        [SerializeField] private LayerMask itemLayer;
        [Tooltip("Layers que bloqueiam a câmera. Normalmente o mesmo do terrain.")]
        [SerializeField] private LayerMask cameraOcclusionLayer;

        [Header("Câmera")]
        [SerializeField] private float orbitSensitivity = 3f;
        [SerializeField] private float zoomSensitivity  = 5f;
        [SerializeField] private float cameraSmoothTime = 0.05f;
        [SerializeField] private float cameraHeight     = 1.5f;

        [Header("Movimento (Click & Hold)")]
        [Tooltip("Intervalo entre atualizações de destino enquanto o mouse está pressionado.")]
        [SerializeField] private float updateTargetInterval = 0.12f;
        [Tooltip("Intervalo entre envios de CmdMoveTo para o servidor.")]
        [SerializeField] private float cmdMoveInterval = 0.18f;
        [Tooltip("Distância mínima entre o destino atual e o novo para emitir SetDestination.")]
        [SerializeField] private float redirectThreshold = 0.4f;

        [Header("Indicador de Movimento")]
        [SerializeField] private GameObject moveIndicatorPrefab;

        [Header("Debug")]
        [SerializeField] private bool debugMovement = false;

        // ── Componentes ────────────────────────────────────────────────────
        private NavMeshAgent       _agent;
        private PlayerEntity       _playerEntity;
        private SkillSystem        _skillSystem;
        private BasicAttackSystem  _basicAttack;
        private NetworkIdentity    _identity;
        private Camera             _cam;

        // ── Estado de movimento ────────────────────────────────────────────
        private bool    _holdMoving;           // mouse esquerdo pressionado movendo
        private Vector3 _lastSentDestination;  // último destino enviado ao servidor
        private float   _lastTargetUpdateTime;
        private float   _lastCmdMoveTime;

        // ── Câmera ─────────────────────────────────────────────────────────
        private float   _yaw         = 45f;
        private float   _pitch       = 45f;
        private float   _distance    = 12f;
        private bool    _orbiting;
        private Vector3 _camVelocity = Vector3.zero;

        private const float PITCH_MIN      = 10f;
        private const float PITCH_MAX      = 80f;
        private const float DIST_MIN       = 3f;
        private const float DIST_MAX       = 30f;
        private const float MAX_MOVE_DIST  = 120f;
        private const float CAM_SKIN_WIDTH = 0.3f;

        private float _lastSecurityWarnTime = -999f;
        private const float SECURITY_WARN_INTERVAL = 2f;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _agent       = GetComponent<NavMeshAgent>();
            _basicAttack = GetComponent<BasicAttackSystem>();
            _identity    = GetComponent<NetworkIdentity>();
        }

        private void OnEnable()
        {
            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnDisable()
        {
            _orbiting        = false;
            _holdMoving      = false;
            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public override void OnStartLocalPlayer()
        {
            _playerEntity = GetComponent<PlayerEntity>();
            _skillSystem  = GetComponent<SkillSystem>();
            _basicAttack  = GetComponent<BasicAttackSystem>();
            _cam          = Camera.main;

            if (_cam == null)
                Debug.LogWarning("[NetworkPlayerController] Camera.main não encontrada.");

            // FIX: configuração profissional do NavMeshAgent para movimento suave
            if (_agent != null)
            {
                _agent.acceleration     = 60f;     // alta para arrancar rápido sem patinar
                _agent.angularSpeed     = 720f;    // gira rápido sem stutter visual
                _agent.autoBraking      = false;   // SEM brake — evita desaceleração no fim do path
                _agent.stoppingDistance = 0.15f;

                if (_playerEntity != null && _playerEntity.Stats != null)
                    _agent.speed = Mathf.Clamp(_playerEntity.Stats.MoveSpeed, 3f, 7f);
            }

            Cursor.visible   = true;
            Cursor.lockState = CursorLockMode.None;

            if (terrainLayer    == 0) Debug.LogWarning("[NetworkPlayerController] terrainLayer não configurado.");
            if (targetableLayer == 0) Debug.LogWarning("[NetworkPlayerController] targetableLayer não configurado.");

            UIManager.Instance?.BindLocalPlayer(_playerEntity);
        }

        private void Update()
        {
            if (!isLocalPlayer) return;
            HandleMouseInput();
            HandleSkillInput();
            HandleCameraOrbit();
            HandleUIInput();
        }

        private void LateUpdate()
        {
            if (!isLocalPlayer) return;
            UpdateCameraPosition();
        }

        // ══════════════════════════════════════════════════════════════════
        // Mouse — click & hold estilo Ragnarok/PoE
        // ══════════════════════════════════════════════════════════════════

        private void HandleMouseInput()
        {
            if (_cam == null) return;

            // Bloqueia interação se estiver sobre UI
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            // ── Click inicial (down) ───────────────────────────────────────
            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);

                // Pickup e clique em monstro/alvo são prioritários e param o hold
                if (TryPickupItem(ray))         { _holdMoving = false; return; }
                if (TryHandleMonsterClick(ray)) { _holdMoving = false; return; }
                if (TrySelectTargetable(ray))   { _holdMoving = false; return; }

                // Caso contrário: começa movimento e habilita hold
                if (TryMoveToGround(ray, showIndicator: true))
                {
                    _holdMoving           = true;
                    _lastTargetUpdateTime = Time.time;
                }
            }

            // ── Hold (botão pressionado) ───────────────────────────────────
            // FIX: enquanto segurar, redireciona suavemente para onde o mouse aponta
            if (_holdMoving && Input.GetMouseButton(0) && !overUI)
            {
                if (Time.time - _lastTargetUpdateTime >= updateTargetInterval)
                {
                    _lastTargetUpdateTime = Time.time;
                    Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
                    TryMoveToGround(ray, showIndicator: false);
                }
            }

            // ── Solta o botão: para de seguir o cursor, mas mantém o destino atual ──
            if (Input.GetMouseButtonUp(0))
            {
                _holdMoving = false;
            }
        }

        private bool TryHandleMonsterClick(Ray ray)
        {
            if (targetableLayer == 0) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, targetableLayer)) return false;

            var monster = hit.collider.GetComponentInParent<NetworkMonsterEntity>();
            if (monster == null || monster.IsDead) return false;

            bool targetChanged = _playerEntity != null
                              && _playerEntity.CurrentTarget != (ITargetable)monster;

            if (targetChanged && _basicAttack != null && _basicAttack.IsAutoAttacking)
                _basicAttack.CancelAutoAttack();

            _skillSystem?.CancelPendingWalk();
            _playerEntity?.SetTarget(monster);
            UIManager.Instance?.UpdateTargetPanel(monster);
            _basicAttack?.TryRegisterClick(monster);

            return true;
        }

        private bool TryPickupItem(Ray ray)
        {
            if (itemLayer == 0) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, itemLayer)) return false;

            var worldItem = hit.collider.GetComponentInParent<WorldItem>();
            if (worldItem == null) return false;

            if (_identity != null)
                worldItem.CmdPickUp(_identity.netId);
            return true;
        }

        private bool TrySelectTargetable(Ray ray)
        {
            if (targetableLayer == 0) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, targetableLayer)) return false;

            var targetable = hit.collider.GetComponentInParent<ITargetable>();
            if (targetable == null || targetable.IsDead) return false;

            _skillSystem?.CancelPendingWalk();
            _basicAttack?.CancelAutoAttack();
            _playerEntity?.SetTarget(targetable);
            UIManager.Instance?.UpdateTargetPanel(targetable);
            return true;
        }

        /// <summary>
        /// Move para um ponto no chão. NÃO PARA o agent antes — apenas troca o destino,
        /// preservando velocity e fluidez.
        /// </summary>
        private bool TryMoveToGround(Ray ray, bool showIndicator)
        {
            int moveLayerMask = terrainLayer != 0
                ? (int)terrainLayer
                : ~(1 << LayerMask.NameToLayer("Targetable"));

            if (!Physics.Raycast(ray, out RaycastHit hit, 300f, moveLayerMask)) return false;

            // FIX: cancela ações pendentes SEM TOCAR no agent (sem ResetPath/velocity=0)
            // Os sistemas de skill/auto-ataque saem do estado deles, mas o agent
            // continua se movendo até receber o novo SetDestination logo abaixo.
            _skillSystem?.CancelPendingWalkSoft();
            _basicAttack?.CancelAutoAttackSoft();

            // Desselecionar alvo só faz sentido no clique inicial, não no hold contínuo
            if (showIndicator)
            {
                _playerEntity?.ClearTarget();
                UIManager.Instance?.ClearTargetPanel();
            }

            Vector3 dest = hit.point;
            if (NavMesh.SamplePosition(dest, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
                dest = navHit.position;

            // FIX: só redireciona se o novo destino é significativamente diferente.
            // Evita SetDestination spam que recalcula path desnecessariamente.
            float deltaToCurrent = Vector3.Distance(_lastSentDestination, dest);
            bool shouldRedirect  = deltaToCurrent >= redirectThreshold;

            if (shouldRedirect)
            {
                // Predição local — SEM ResetPath, SEM velocity=0
                if (_agent != null && _agent.isOnNavMesh)
                    _agent.SetDestination(dest);

                _lastSentDestination = dest;
            }

            // Throttle do envio ao servidor (independente de shouldRedirect, para
            // garantir que o servidor sempre tenha o destino mais recente)
            if (Time.time - _lastCmdMoveTime >= cmdMoveInterval)
            {
                _lastCmdMoveTime = Time.time;
                CmdMoveTo(dest);
            }

            if (showIndicator) SpawnMoveIndicator(hit.point);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // Teclado
        // ══════════════════════════════════════════════════════════════════

        private void HandleSkillInput()
        {
            if (_skillSystem == null) return;
            if (_playerEntity != null && _playerEntity.IsDead) return;
            if (IsTypingInField()) return;

            if (Input.GetKeyDown(KeyCode.Q)) { _holdMoving = false; _skillSystem.TryUseSkill(0); }
            if (Input.GetKeyDown(KeyCode.W)) { _holdMoving = false; _skillSystem.TryUseSkill(1); }
            if (Input.GetKeyDown(KeyCode.E)) { _holdMoving = false; _skillSystem.TryUseSkill(2); }
            if (Input.GetKeyDown(KeyCode.R)) { _holdMoving = false; _skillSystem.TryUseSkill(3); }
            if (Input.GetKeyDown(KeyCode.C)) AttributeWindowUI.Instance?.Toggle();
        }

        private void HandleUIInput()
        {
            if (IsTypingInField()) return;

            if (Input.GetKeyDown(KeyCode.I))
            {
                EnsureCursorVisible();
                InventoryUI.Instance?.Toggle();
            }
            if (Input.GetKeyDown(KeyCode.P))
            {
                EnsureCursorVisible();
                PowerGemUI.Instance?.Toggle();
            }
        }

        private static bool IsTypingInField()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null) return false;
            return selected.GetComponent<TMPro.TMP_InputField>() != null
                || selected.GetComponent<UnityEngine.UI.InputField>() != null;
        }

        private void EnsureCursorVisible()
        {
            if (!_orbiting)
            {
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Câmera
        // ══════════════════════════════════════════════════════════════════

        private void HandleCameraOrbit()
        {
            if (Input.GetMouseButtonDown(1))
            {
                _orbiting        = true;
                Cursor.visible   = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            if (Input.GetMouseButtonUp(1))
            {
                _orbiting        = false;
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }

            if (_orbiting)
            {
                _yaw   += Input.GetAxis("Mouse X") * orbitSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
                _pitch  = Mathf.Clamp(_pitch, PITCH_MIN, PITCH_MAX);

                if (_yaw > 360f)  _yaw -= 360f;
                if (_yaw < -360f) _yaw += 360f;
            }

            _distance -= Input.GetAxis("Mouse ScrollWheel") * zoomSensitivity;
            _distance  = Mathf.Clamp(_distance, DIST_MIN, DIST_MAX);
        }

        private void UpdateCameraPosition()
        {
            if (_cam == null) return;

            Quaternion rot   = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3    pivot = transform.position + Vector3.up * cameraHeight;
            Vector3    dir   = rot * new Vector3(0f, 0f, -1f);

            float effectiveDistance = _distance;
            int   occlusionMask = cameraOcclusionLayer != 0
                ? (int)cameraOcclusionLayer
                : (int)terrainLayer;

            if (occlusionMask != 0
                && Physics.SphereCast(pivot, CAM_SKIN_WIDTH, dir, out RaycastHit camHit,
                                      _distance, occlusionMask))
            {
                effectiveDistance = Mathf.Max(DIST_MIN, camHit.distance - CAM_SKIN_WIDTH);
            }

            Vector3 target = pivot + dir * effectiveDistance;

            if (target.y < transform.position.y + 0.5f)
                target.y = transform.position.y + 0.5f;

            _cam.transform.position = Vector3.SmoothDamp(
                _cam.transform.position, target, ref _camVelocity, cameraSmoothTime);
            _cam.transform.LookAt(pivot);
        }

        // ══════════════════════════════════════════════════════════════════
        // Commands
        // ══════════════════════════════════════════════════════════════════

        [Command]
        public void CmdMoveTo(Vector3 destination)
        {
            var netPlayer = GetComponent<NetworkPlayer>();
            if (netPlayer == null || netPlayer.Dead) return;

            float dist = Vector3.Distance(transform.position, destination);
            if (dist > MAX_MOVE_DIST)
            {
                if (Time.time - _lastSecurityWarnTime >= SECURITY_WARN_INTERVAL)
                {
                    _lastSecurityWarnTime = Time.time;
                    Debug.LogWarning($"[Security] CmdMoveTo suspeito: dist={dist:0.0} | {netPlayer.CharacterName}");
                }
                return;
            }

            if (_agent == null) return;

            Vector3 finalDest = destination;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas)
                || NavMesh.SamplePosition(destination, out hit, 6f, NavMesh.AllAreas))
            {
                finalDest = hit.position;
            }
            else if (debugMovement)
            {
                Debug.LogWarning($"[Server] CmdMoveTo: destino fora do NavMesh para {netPlayer.CharacterName}");
            }

            // FIX: SetDestination puro — sem ResetPath, sem velocity=0.
            // O agent transita suavemente do path atual para o novo.
            _agent.SetDestination(finalDest);
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        public void SetEnabled(bool value)
        {
            enabled = value;
            if (!value)
            {
                _holdMoving = false;
                _basicAttack?.CancelAutoAttack();
                _skillSystem?.CancelPendingWalk();

                _orbiting        = false;
                Cursor.visible   = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════

        private void SpawnMoveIndicator(Vector3 pos)
        {
            if (moveIndicatorPrefab == null) return;
            var go = Instantiate(moveIndicatorPrefab,
                pos + Vector3.up * 0.02f,
                Quaternion.Euler(90f, 0f, 0f));
            Destroy(go, 0.8f);
        }
    }
}
