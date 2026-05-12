using UnityEngine;
using UnityEngine.AI;
using Mirror;
using RPG.Character;
using RPG.UI;
using RPG.Network;

namespace RPG.Combat
{
    /// <summary>
    /// Auto-ataque básico (sem custo de mana, sem cooldown explícito — apenas ASPD).
    ///
    /// Disparado por duplo-clique em um monstro. Persegue até entrar no range,
    /// para, ataca em intervalos de 1/ASPD, e cancela se o alvo morrer ou mudar.
    ///
    /// Princípios:
    ///   - Tudo aqui é client-side prediction/UX. O dano real é decidido pelo
    ///     servidor via CmdBasicAttack.
    ///   - Movimento durante perseguição usa um destino a (range * DEST_FRACTION)
    ///     para o player parar dentro do range com folga, sem sobrepor o alvo.
    /// </summary>
    [RequireComponent(typeof(PlayerEntity))]
    [RequireComponent(typeof(NetworkIdentity))]
    public class BasicAttackSystem : NetworkBehaviour
    {
        [Header("Ataque")]
        [Tooltip("Distância máxima de ataque (m).")]
        [SerializeField] private float attackRange = 2.5f;

        [Tooltip("Intervalo fixo se useCharacterASPD = false.")]
        [SerializeField] private float attackInterval = 1.2f;

        [Tooltip("Se true, usa 1/ASPD do personagem como intervalo.")]
        [SerializeField] private bool useCharacterASPD = true;

        [Tooltip("Janela para reconhecer duplo-clique (s).")]
        [SerializeField] private float doubleClickTime = 0.35f;

        [Tooltip("Frequência máxima de envio de CmdMoveTo durante perseguição (s).")]
        [SerializeField] private float moveCommandInterval = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // Constantes de tuning — não mexer sem testar perseguição/kite
        private const float DEST_FRACTION      = 0.80f;  // destino a 80% do range
        private const float RANGE_CHECK_MARGIN = 1.05f;  // tolerância anti-jitter
        private const float CHASE_STOP_DIST    = 0.2f;   // stoppingDistance fixo
        private const float IDLE_STOP_DIST     = 0.5f;   // stoppingDistance quando parado
        private const float MIN_INTERVAL       = 0.3f;
        private const float MAX_INTERVAL       = 3f;

        // ── Componentes ────────────────────────────────────────────────────
        private PlayerEntity            _player;
        private NavMeshAgent            _agent;
        private Animator                _animator;
        private NetworkPlayerController _controller;
        private SkillSystem             _skillSystem;
        private NetworkIdentity         _identity;

        // ── Estado de auto-ataque ──────────────────────────────────────────
        private NetworkMonsterEntity _attackTarget;
        private bool                 _autoAttacking;
        private float                _attackTimer;
        private float                _lastMoveCmd;

        // ── Estado de duplo-clique ─────────────────────────────────────────
        private float                _lastClickTime = -999f;
        private NetworkMonsterEntity _lastClickTarget;

        public bool  IsAutoAttacking => _autoAttacking;
        public float AttackRange     => attackRange;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _player      = GetComponent<PlayerEntity>();
            _agent       = GetComponent<NavMeshAgent>();
            _animator    = GetComponentInChildren<Animator>();
            _controller  = GetComponent<NetworkPlayerController>();
            _skillSystem = GetComponent<SkillSystem>();
            _identity    = GetComponent<NetworkIdentity>();
        }

        private void Update()
        {
            if (!isLocalPlayer) return;
            if (!_player.IsInitialized || _player.IsDead) return;
            if (_autoAttacking) UpdateAutoAttack();
        }

        // ── API pública ────────────────────────────────────────────────────

        /// <summary>
        /// Registra um clique no monstro e inicia auto-ataque se for duplo-clique.
        /// Retorna true se um duplo-clique foi reconhecido.
        /// </summary>
        public bool TryRegisterClick(NetworkMonsterEntity monster)
        {
            if (IsTargetGone(monster)) return false;

            float now           = Time.time;
            bool  isDoubleClick = (now - _lastClickTime) <= doubleClickTime
                                  && _lastClickTarget == monster;

            _lastClickTime   = now;
            _lastClickTarget = monster;

            if (isDoubleClick)
            {
                StartAutoAttack(monster);
                return true;
            }
            return false;
        }

        public void CancelAutoAttack()
        {
            if (!_autoAttacking) return;

            _autoAttacking = false;
            _attackTarget  = null;

            StopAgentMovement();
            Log("Auto-ataque cancelado.");
        }

        // ── Início ─────────────────────────────────────────────────────────

        private void StartAutoAttack(NetworkMonsterEntity monster)
        {
            _skillSystem?.CancelPendingWalk();
            CancelAutoAttack();

            _attackTarget  = monster;
            _autoAttacking = true;
            _attackTimer   = GetAttackInterval();

            _player.SetTarget(monster);
            UIManager.Instance?.UpdateTargetPanel(monster);

            Log($"Auto-ataque iniciado → {monster.DisplayName}");
        }

        // ── Loop principal ─────────────────────────────────────────────────

        private void UpdateAutoAttack()
        {
            // 1) Alvo morreu ou foi destruído?
            if (IsTargetGone(_attackTarget))
            {
                Log("Alvo destruído ou morto — cancelando.");
                CancelAutoAttack();
                _player.ClearTarget();
                UIManager.Instance?.ClearTargetPanel();
                return;
            }

            // 2) Jogador trocou de alvo manualmente?
            if (!IsCurrentTargetStillSame())
            {
                CancelAutoAttack();
                return;
            }

            // 3) Dentro do range? Ataca. Fora? Persegue.
            float dist           = Vector3.Distance(transform.position, _attackTarget.Position);
            float effectiveRange = attackRange * RANGE_CHECK_MARGIN;

            if (dist > effectiveRange)
                ChaseTarget();
            else
                AttackTarget();
        }

        private void AttackTarget()
        {
            // Para o agent (no range)
            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath)
                StopAgentMovement();

            _attackTimer += Time.deltaTime;
            if (_attackTimer >= GetAttackInterval())
            {
                _attackTimer = 0f;
                ExecuteBasicAttack();
            }

            RotateTowardsTarget();
        }

        private void RotateTowardsTarget()
        {
            Vector3 dir = _attackTarget.Position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    10f * Time.deltaTime);
        }

        private void ChaseTarget()
        {
            if (_agent != null && _agent.isOnNavMesh)
            {
                Vector3 destination = CalculateChaseDestination(_attackTarget.Position);
                _agent.stoppingDistance = CHASE_STOP_DIST;
                _agent.SetDestination(destination);
            }

            // Throttle do CmdMoveTo (cliente faz prediction; servidor recebe periodicamente)
            if (Time.time - _lastMoveCmd >= moveCommandInterval)
            {
                _lastMoveCmd = Time.time;
                Vector3 serverDest = CalculateChaseDestination(_attackTarget.Position);
                _controller?.CmdMoveTo(serverDest);
            }
        }

        private Vector3 CalculateChaseDestination(Vector3 targetPos)
        {
            Vector3 toTarget = targetPos - transform.position;
            float   dist     = toTarget.magnitude;

            float safeStopDist = attackRange * DEST_FRACTION;
            if (dist <= safeStopDist * 0.95f)
                return transform.position;

            Vector3 direction   = toTarget.normalized;
            Vector3 destination = targetPos - direction * safeStopDist;

            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return hit.position;

            return destination;
        }

        // ── Execução do ataque ─────────────────────────────────────────────

        private void ExecuteBasicAttack()
        {
            if (IsTargetGone(_attackTarget)) return;

            _animator?.SetTrigger("Attack");

            if (_identity != null)
            {
                _attackTarget.CmdBasicAttack(_identity.netId, attackRange);
                Log($"CmdBasicAttack → {_attackTarget.DisplayName}");
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private float GetAttackInterval()
        {
            if (useCharacterASPD && _player.IsInitialized && _player.Stats != null)
                return Mathf.Clamp(1f / Mathf.Max(0.1f, _player.Stats.ASPD),
                                   MIN_INTERVAL, MAX_INTERVAL);
            return attackInterval;
        }

        private void StopAgentMovement()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.ResetPath();
            _agent.velocity         = Vector3.zero;
            _agent.stoppingDistance = IDLE_STOP_DIST;
        }

        /// <summary>
        /// Verifica se o PlayerEntity ainda tem como CurrentTarget o mesmo monstro.
        /// Compara por instância de Unity Object para evitar problemas de boxing.
        /// </summary>
        private bool IsCurrentTargetStillSame()
        {
            if (_player.CurrentTarget == null) return false;
            // Cast direto: se CurrentTarget é o monstro como ITargetable, isto compara
            // a referência da MonoBehaviour subjacente. UnityEngine.Object override de
            // operator== lida com objetos destruídos retornando true para null.
            var current = _player.CurrentTarget as NetworkMonsterEntity;
            return current == _attackTarget && current != null;
        }

        private static bool IsTargetGone(NetworkMonsterEntity target)
            => target == null || target.IsDead;

        private void Log(string msg)
        {
            if (debugLogs) Debug.Log($"[BasicAttackSystem] {msg}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, attackRange);

            Gizmos.color = new Color(0f, 1f, 0.5f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, attackRange * DEST_FRACTION);
        }
#endif
    }
}
