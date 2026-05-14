using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Mirror;
using RPG.Character;
using RPG.UI;
using RPG.Network;
using RPG.Data;

namespace RPG.Combat
{
    public enum SkillType   { Physical, Magical, Heal, Buff }
    public enum SkillTarget { Enemy, Self, Ally }

    [Serializable]
    public class SkillData
    {
        public string      Name          = "Skill";
        public SkillType   Type          = SkillType.Physical;
        public SkillTarget Target        = SkillTarget.Enemy;
        public float       Cooldown      = 3f;
        public float       ManaCost      = 10f;
        public float       Range         = 4f;
        public float       AtkMultiplier = 1.0f;
        public float       CastTime      = 0f;
        public string      AnimTrigger   = "Attack";
        public Sprite      Icon;
    }

    /// <summary>
    /// Gerencia a barra de skills do jogador local.
    ///
    /// === MUDANÇAS DESTA VERSÃO (lag/movimento) ===
    ///
    ///   1. CancelPendingWalkSoft(): NOVO. Cancela o estado de walk-to-skill
    ///      SEM tocar no NavMeshAgent. Usado pelo NetworkPlayerController
    ///      durante click-to-move para não interromper o movimento atual.
    ///
    ///   2. CancelPendingWalk() agora delega para Soft + StopAgent. Use
    ///      apenas quando realmente quiser parar.
    ///
    ///   3. StopAgent não zera mais velocity. ResetPath() + stoppingDistance
    ///      ajustada. O agent desacelera naturalmente.
    ///
    ///   4. WalkThenSendCmd: só chama SetDestination quando o destino mudou
    ///      significativamente (evita stutter de recálculo de path).
    /// </summary>
    [RequireComponent(typeof(PlayerEntity))]
    public class SkillSystem : NetworkBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public  const int   MAX_SKILLS         = 4;
        private const float CMD_MOVE_INTERVAL  = 0.18f;
        private const float WALK_TIMEOUT       = 15f;
        private const float WALK_DEST_FRACTION = 0.85f;
        private const float RANGE_CHECK_MARGIN = 1.05f;
        private const float WALK_STOP_DIST     = 0.15f;
        private const float IDLE_STOP_DIST     = 0.5f;
        private const float INSTANT_CAST_EPS   = 0.05f;
        private const float WALK_REDIRECT_THRESHOLD = 0.5f;

        // ── Componentes ────────────────────────────────────────────────────
        private PlayerEntity              _player;
        private Animator                  _animator;
        private NavMeshAgent              _agent;
        private NetworkPlayerController   _controller;
        private NetworkInventory          _inventory;
        private RPG.Network.NetworkPlayer _netPlayer;
        private NetworkIdentity           _identity;

        // ── Cooldown visual ────────────────────────────────────────────────
        private readonly float[] _uiCooldownTimers = new float[MAX_SKILLS];

        // ── Estado de walk-to-range e cast ────────────────────────────────
        private Coroutine   _walkCoroutine;
        private Coroutine   _castCoroutine;
        private bool        _hasPendingWalk;
        private bool        _isCasting;
        private ITargetable _pendingTarget;
        private float       _lastCmdMoveTime;
        private Vector3     _lastWalkDestination = Vector3.positiveInfinity;

        // ── Eventos para a UI ──────────────────────────────────────────────
        public event Action<int, float>    OnCooldownStarted;
        public event Action<int>           OnSkillFired;
        public event Action                OnSkillBarNeedsRefresh;
        public event Action<string, float> OnCastStarted;
        public event Action<float>         OnCastProgress;
        public event Action                OnCastFinished;

        public bool HasPendingAction => _hasPendingWalk || _isCasting;
        public bool IsCasting        => _isCasting;
        public int  SkillCount       => MAX_SKILLS;

        // ══════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _player     = GetComponent<PlayerEntity>();
            _animator   = GetComponentInChildren<Animator>();
            _agent      = GetComponent<NavMeshAgent>();
            _controller = GetComponent<NetworkPlayerController>();
            _inventory  = GetComponent<NetworkInventory>();
            _netPlayer  = GetComponent<RPG.Network.NetworkPlayer>();
            _identity   = GetComponent<NetworkIdentity>();
        }

        public override void OnStartLocalPlayer()
        {
            if (_inventory != null)
                _inventory.OnGemLoadoutChanged += OnGemLoadoutChanged;
        }

        public override void OnStopClient()
        {
            if (_inventory != null)
                _inventory.OnGemLoadoutChanged -= OnGemLoadoutChanged;

            CancelPendingWalkSoft();
            CancelCast();
        }

        private void OnDestroy()
        {
            if (_inventory != null)
                _inventory.OnGemLoadoutChanged -= OnGemLoadoutChanged;

            CancelPendingWalkSoft();
            CancelCast();
        }

        private void OnGemLoadoutChanged()
        {
            if (!isLocalPlayer) return;
            OnSkillBarNeedsRefresh?.Invoke();
        }

        private void Update()
        {
            if (!isLocalPlayer) return;

            for (int i = 0; i < MAX_SKILLS; i++)
                if (_uiCooldownTimers[i] > 0f)
                    _uiCooldownTimers[i] -= Time.deltaTime;

            if ((_hasPendingWalk || _isCasting) && _player.IsDead)
            {
                CancelPendingWalk();
                CancelCast();
                return;
            }

            if (_hasPendingWalk && !IsTargetValid(_pendingTarget))
                CancelPendingWalk();
        }

        // ══════════════════════════════════════════════════════════════════
        // API pública
        // ══════════════════════════════════════════════════════════════════

        public SkillData GetSkill(int index)
        {
            if (index < 0 || index >= MAX_SKILLS) return null;
            return _inventory?.GetEquippedSkill(index);
        }

        public float GetUICooldown(int i)
            => (i >= 0 && i < MAX_SKILLS) ? Mathf.Max(0f, _uiCooldownTimers[i]) : 0f;

        public bool IsOnUICooldown(int i) => GetUICooldown(i) > 0f;

        public void TryUseSkill(int index)
        {
            if (!isLocalPlayer) return;
            if (index < 0 || index >= MAX_SKILLS) return;
            if (!_player.IsInitialized || _player.IsDead) return;
            if (_isCasting) return;

            var skill = GetSkill(index);
            if (skill == null)
            {
                UIManager.Instance?.ShowMessage($"Nenhuma Joia equipada no slot {SkillSlotName(index)}!");
                return;
            }

            if (IsOnUICooldown(index))
            {
                UIManager.Instance?.ShowMessage($"{skill.Name}: aguarde {GetUICooldown(index):0.0}s");
                return;
            }

            CancelPendingWalk();

            if (skill.Target == SkillTarget.Self
                || skill.Type == SkillType.Heal
                || skill.Type == SkillType.Buff)
            {
                StartCastAndSend(index, skill, null, isSelf: true);
                return;
            }

            var target = _player.CurrentTarget;
            if (target == null)
            {
                UIManager.Instance?.ShowMessage("Selecione um alvo primeiro!");
                return;
            }
            if (!IsTargetValid(target))
            {
                UIManager.Instance?.ShowMessage("Alvo já está morto!");
                _player.ClearTarget();
                UIManager.Instance?.ClearTargetPanel();
                return;
            }

            float dist = Vector3.Distance(transform.position, target.Position);

            if (dist <= skill.Range * RANGE_CHECK_MARGIN)
            {
                StopAgent();
                StartCastAndSend(index, skill, target, isSelf: false);
            }
            else
            {
                Log($"Fora de range ({dist:0.1} > {skill.Range:0.1}). Caminhando...");
                _hasPendingWalk      = true;
                _pendingTarget       = target;
                _lastCmdMoveTime     = -CMD_MOVE_INTERVAL;
                _lastWalkDestination = Vector3.positiveInfinity;
                _walkCoroutine       = StartCoroutine(WalkThenSendCmd(index, skill, target));
            }
        }

        public void CancelCast()
        {
            if (_castCoroutine != null)
            {
                StopCoroutine(_castCoroutine);
                _castCoroutine = null;
            }
            if (_isCasting)
            {
                _isCasting = false;
                OnCastFinished?.Invoke();
            }
        }

        /// <summary>
        /// Cancela o walk-to-skill E para o agent. Use quando realmente quiser
        /// que o player pare (ex: morte, mudança de alvo manual).
        /// </summary>
        public void CancelPendingWalk()
        {
            CancelPendingWalkSoft();
            StopAgent();
        }

        /// <summary>
        /// NOVO: cancela apenas o ESTADO de walk-to-skill, sem mexer no agent.
        /// Use durante click-to-move para que o player continue se movendo
        /// para o novo destino sem parar.
        /// </summary>
        public void CancelPendingWalkSoft()
        {
            if (_walkCoroutine != null)
            {
                StopCoroutine(_walkCoroutine);
                _walkCoroutine = null;
            }
            _hasPendingWalk      = false;
            _pendingTarget       = null;
            _lastWalkDestination = Vector3.positiveInfinity;
        }

        public void OnServerSkillConfirmed(int skillIndex, float cooldownDuration)
        {
            if (skillIndex < 0 || skillIndex >= MAX_SKILLS) return;
            _uiCooldownTimers[skillIndex] = cooldownDuration;
            OnCooldownStarted?.Invoke(skillIndex, cooldownDuration);
            OnSkillFired?.Invoke(skillIndex);
            Log($"Skill {skillIndex} confirmada. Cooldown: {cooldownDuration:0.0}s");
        }

        public void OnServerSkillRejected(int skillIndex, string reason)
        {
            UIManager.Instance?.ShowMessage(reason);
            Log($"Skill {skillIndex} rejeitada: {reason}");
        }

        // ══════════════════════════════════════════════════════════════════
        // Cast
        // ══════════════════════════════════════════════════════════════════

        private void StartCastAndSend(int index, SkillData skill, ITargetable target, bool isSelf)
        {
            float effectiveCastTime = 0f;
            if (skill.CastTime > 0f && _player.Stats != null)
            {
                effectiveCastTime = StatsCalculator.CalculateEffectiveCastTime(
                    skill.CastTime, _player.Stats.CastSpeed);
            }

            if (effectiveCastTime <= INSTANT_CAST_EPS)
            {
                if (isSelf) SendSelfSkillCmd(index);
                else        SendSkillCmd(index, target, skill.Type == SkillType.Physical);
                return;
            }

            if (_castCoroutine != null) StopCoroutine(_castCoroutine);
            _castCoroutine = StartCoroutine(CastSequence(index, skill, target, isSelf, effectiveCastTime));
        }

        private IEnumerator CastSequence(int index, SkillData skill, ITargetable target,
                                         bool isSelf, float castTime)
        {
            _isCasting = true;
            OnCastStarted?.Invoke(skill.Name, castTime);

            StopAgent();

            if (!string.IsNullOrEmpty(skill.AnimTrigger))
                _animator?.SetTrigger("CastStart");

            float elapsed   = 0f;
            bool  cancelled = false;

            while (elapsed < castTime)
            {
                elapsed += Time.deltaTime;

                if (_player.IsDead) { Log("Cast cancelado: player morreu."); cancelled = true; break; }
                if (!isSelf && !IsTargetValid(target))
                {
                    Log("Cast cancelado: alvo morreu.");
                    UIManager.Instance?.ShowMessage("Alvo inválido — cast cancelado.");
                    cancelled = true;
                    break;
                }

                OnCastProgress?.Invoke(elapsed / castTime);
                yield return null;
            }

            _isCasting     = false;
            _castCoroutine = null;
            OnCastFinished?.Invoke();

            if (!cancelled)
            {
                if (isSelf) SendSelfSkillCmd(index);
                else        SendSkillCmd(index, target, skill.Type == SkillType.Physical);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // Walk-to-range
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator WalkThenSendCmd(int index, SkillData skill, ITargetable target)
        {
            if (_agent != null && _agent.isOnNavMesh)
                _agent.stoppingDistance = WALK_STOP_DIST;

            float timeout        = WALK_TIMEOUT;
            float effectiveRange = skill.Range * RANGE_CHECK_MARGIN;

            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;

                if (_player.IsDead) { Log("Walk: player morreu."); break; }

                if (!IsTargetValid(target))
                {
                    _player.ClearTarget();
                    UIManager.Instance?.ClearTargetPanel();
                    Log("Walk: alvo inválido.");
                    break;
                }

                if (_player.CurrentTarget != target) { Log("Walk: alvo mudou."); break; }

                float dist = Vector3.Distance(transform.position, target.Position);
                if (dist <= effectiveRange)
                {
                    StopAgent();
                    _hasPendingWalk = false;
                    _pendingTarget  = null;

                    yield return null;

                    if (!_player.IsDead && IsTargetValid(target) && _player.CurrentTarget == target)
                    {
                        Log($"Em range ({dist:0.2}/{skill.Range:0.1}). Executando skill {index}.");
                        StartCastAndSend(index, skill, target, isSelf: false);
                    }
                    yield break;
                }

                // FIX: só recalcula path se o destino mudou significativamente
                if (_agent != null && _agent.isOnNavMesh)
                {
                    Vector3 destination = CalculateWalkDestination(target.Position, skill.Range);
                    if (Vector3.Distance(destination, _lastWalkDestination) >= WALK_REDIRECT_THRESHOLD)
                    {
                        _agent.SetDestination(destination);
                        _lastWalkDestination = destination;
                    }
                }

                if (Time.time - _lastCmdMoveTime >= CMD_MOVE_INTERVAL)
                {
                    _lastCmdMoveTime = Time.time;
                    Vector3 serverDest = CalculateWalkDestination(target.Position, skill.Range);
                    _controller?.CmdMoveTo(serverDest);
                }

                yield return null;
            }

            if (timeout <= 0f)
                Log($"Walk: timeout após {WALK_TIMEOUT}s.");

            StopAgent();
            _hasPendingWalk = false;
            _pendingTarget  = null;
            _walkCoroutine  = null;
        }

        private Vector3 CalculateWalkDestination(Vector3 targetPos, float skillRange)
        {
            Vector3 toTarget = targetPos - transform.position;
            float   dist     = toTarget.magnitude;

            float safeStopDist = skillRange * WALK_DEST_FRACTION;
            if (dist <= safeStopDist * 0.95f)
                return transform.position;

            Vector3 direction   = toTarget.normalized;
            Vector3 destination = targetPos - direction * safeStopDist;

            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                return hit.position;

            return destination;
        }

        // ══════════════════════════════════════════════════════════════════
        // Envio de comandos
        // ══════════════════════════════════════════════════════════════════

        private void SendSkillCmd(int skillIndex, ITargetable target, bool isPhysical)
        {
            var skill = GetSkill(skillIndex);
            StopAgent();

            if (_animator != null && skill != null && !string.IsNullOrEmpty(skill.AnimTrigger))
                _animator.SetTrigger(skill.AnimTrigger);

            if (target != null)
            {
                Vector3 dir = target.Position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(dir);
            }

            if (target is not NetworkBehaviour targetNB)
            {
                Log("Alvo não é NetworkBehaviour — skill não enviada.");
                return;
            }

            if (_identity == null) return;
            uint attackerNetId = _identity.netId;

            if (targetNB is NetworkMonsterEntity monster)
            {
                monster.CmdRequestSkill(attackerNetId, skillIndex, isPhysical);
                Log($"CmdRequestSkill → {monster.DisplayName} skill:{skillIndex}");
            }
            else
            {
                if (debugLogs)
                    UIManager.Instance?.ShowMessage("PvP ainda não implementado.");
            }
        }

        private void SendSelfSkillCmd(int skillIndex)
        {
            _netPlayer?.CmdRequestSelfSkill(skillIndex);
            Log($"CmdRequestSelfSkill skill:{skillIndex}");
        }

        // ══════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Para o agent suavemente: limpa path e ajusta stoppingDistance.
        /// FIX: NÃO zera mais velocity — deixa o brake natural desacelerar.
        /// </summary>
        private void StopAgent()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.ResetPath();
            _agent.stoppingDistance = IDLE_STOP_DIST;
            _lastWalkDestination    = Vector3.positiveInfinity;
        }

        private static bool IsTargetValid(ITargetable target)
        {
            if (target == null) return false;
            if (target is UnityEngine.Object unityObj && unityObj == null) return false;
            return !target.IsDead;
        }

        private static string SkillSlotName(int index) => index switch
        {
            0 => "Q", 1 => "W", 2 => "E", 3 => "R", _ => index.ToString()
        };

        private void Log(string msg)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugLogs) Debug.Log($"[SkillSystem] {msg}");
#endif
        }
    }
}
