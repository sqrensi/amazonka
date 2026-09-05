using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// ИИ главного монстра деревни. Реактивный, а не "увидел-побежал-убил".
///
/// Восприятие:
///  - СЛУХ: подписан на <see cref="NoiseManager"/>. Громкий звук слышно издалека,
///    тихий — только вблизи (радиус слышимости = громкость * коэффициент).
///  - ЗРЕНИЕ: конус обзора + проверка линии видимости лучом. Очень близко чувствует и вне конуса.
///  - ТРЕВОГА: общий уровень шума ночи (Tier) повышает скорость и заставляет патрулировать.
///
/// Состояния: Idle -> Patrol -> Investigate -> Search -> Chase -> Attack (-> Dead).
/// Движение — через NavMeshAgent (нужен запечённый NavMesh на локации).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MonsterAI : MonoBehaviour
{
    public enum State { Idle, Patrol, Investigate, Search, Chase, Attack, Dead }

    [Header("References")]
    [SerializeField] Transform eyes;
    [SerializeField] MonsterHealth health;
    [SerializeField] string playerTag = "Player";

    [Header("Speeds")]
    [SerializeField] float patrolSpeed = 1.4f;
    [SerializeField] float investigateSpeed = 2.6f;
    [SerializeField] float chaseSpeed = 4.2f;
    [Tooltip("Прибавка к скорости, когда ночь в режиме Hunt (шум 70+).")]
    [SerializeField] float huntSpeedBonus = 1.4f;
    [SerializeField] float turnSpeed = 8f;

    [Header("Vision")]
    [SerializeField] float viewDistance = 18f;
    [SerializeField, Range(0f, 180f)] float viewAngle = 70f;
    [Tooltip("Радиус, в котором монстр чувствует игрока даже вне конуса обзора.")]
    [SerializeField] float senseRadius = 3.5f;
    [SerializeField] float eyeHeight = 1.7f;
    [SerializeField] float targetHeight = 1.2f;
    [SerializeField] LayerMask visionMask = ~0;
    [Tooltip("Сколько секунд монстр помнит игрока, потеряв его из виду.")]
    [SerializeField] float loseSightTime = 4f;

    [Header("Hearing")]
    [Tooltip("Радиус слышимости = громкость звука * этот коэффициент.")]
    [SerializeField] float hearingDistancePerNoise = 0.9f;
    [SerializeField] float minHearableNoise = 4f;

    [Header("Crouch stealth")]
    [Tooltip("Во сколько раз падает дальность зрения, когда игрок сидит.")]
    [SerializeField, Range(0f, 1f)] float crouchVisionMultiplier = 0.3f;
    [Tooltip("Во сколько раз падает радиус чутья вблизи, когда игрок сидит.")]
    [SerializeField, Range(0f, 1f)] float crouchSenseMultiplier = 0.4f;

    [Header("Attack")]
    [SerializeField] float attackRange = 2.2f;
    [SerializeField] float attackDamage = 25f;
    [SerializeField] float attackCooldown = 1.3f;
    [Tooltip("Задержка перед нанесением урона (даёт шанс увернуться).")]
    [SerializeField] float attackWindup = 0.35f;

    [Header("Patrol / Search")]
    [SerializeField] float patrolPointRadius = 12f;
    [SerializeField] float searchDuration = 8f;
    [SerializeField] float arriveThreshold = 1.2f;
    [SerializeField] float idleRepathTime = 3f;

    [Header("Roaming (бродит всегда, даже в тишине)")]
    [Tooltip("Радиус выбора следующей точки блуждания от текущей позиции.")]
    [SerializeField] float wanderRadius = 24f;
    [Tooltip("Минимальная длина одного отрезка блуждания, чтобы реально перемещаться по карте.")]
    [SerializeField] float minWanderStep = 7f;
    [Tooltip("Шанс сделать паузу-осмотр по прибытии в точку блуждания.")]
    [SerializeField, Range(0f, 1f)] float pauseChance = 0.35f;
    [SerializeField] Vector2 pauseDuration = new Vector2(0.8f, 2.2f);
    [Tooltip("Скорость вращения при осмотре по сторонам (град/сек).")]
    [SerializeField] float scanTurnSpeed = 70f;

    [Header("Smart chase")]
    [Tooltip("На сколько секунд вперёд монстр упреждает движение игрока (срезает углы).")]
    [SerializeField] float predictLead = 0.6f;

    [Header("Debug")]
    [SerializeField] bool drawGizmos = true;

    NavMeshAgent _agent;
    NoiseManager _noise;
    Transform _player;
    PlayerHealth _playerHealth;
    HorrorFirstPersonController _playerController;

    State _state = State.Idle;
    Vector3 _investigatePos;
    Vector3 _lastSeenPos;
    Vector3 _lastSeenVelocity;
    float _lastSeenTime = -999f;
    float _searchTimer;
    float _searchRadius;
    float _attackTimer;
    float _idleTimer;
    bool _attacking;

    // Осмотр по сторонам.
    float _scanTimer;
    float _scanYaw;

    // Предсказание движения игрока.
    Vector3 _playerLastPos;
    Vector3 _playerVelocity;

    public State CurrentState => _state;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (health == null)
            health = GetComponent<MonsterHealth>();
        if (eyes == null)
            eyes = transform;
    }

    void Start()
    {
        _noise = NoiseManager.GetOrCreate();
        _noise.OnNoise += HandleNoise;

        AcquirePlayer();

        if (health != null)
            health.OnDeath += HandleDeath;

        _investigatePos = transform.position;
        SetState(State.Idle);
    }

    void OnDestroy()
    {
        if (_noise != null)
            _noise.OnNoise -= HandleNoise;
        if (health != null)
            health.OnDeath -= HandleDeath;
    }

    void Update()
    {
        if (_state == State.Dead)
            return;

        if (_player == null)
            AcquirePlayer();

        TrackPlayerVelocity();

        bool canSee = CanSeePlayer();
        if (canSee)
        {
            _lastSeenPos = _player.position;
            _lastSeenVelocity = _playerVelocity;
            _lastSeenTime = Time.time;
        }

        ApplySpeed();

        switch (_state)
        {
            case State.Idle: TickIdle(canSee); break;
            case State.Patrol: TickPatrol(canSee); break;
            case State.Investigate: TickInvestigate(canSee); break;
            case State.Search: TickSearch(canSee); break;
            case State.Chase: TickChase(canSee); break;
            case State.Attack: TickAttack(canSee); break;
        }
    }

    // ---------------------------------------------------------------- States

    void TickIdle(bool canSee)
    {
        if (canSee) { SetState(State.Chase); return; }

        // Стоим на месте и осматриваемся по сторонам.
        ScanAround();

        _idleTimer -= Time.deltaTime;
        if (_idleTimer <= 0f)
            SetState(State.Patrol); // после паузы всегда продолжаем бродить
    }

    void TickPatrol(bool canSee)
    {
        if (canSee) { SetState(State.Chase); return; }

        if (Arrived())
        {
            // Иногда останавливаемся осмотреться, иначе идём к новой точке карты.
            if (Random.value < pauseChance)
                SetState(State.Idle);
            else
                MoveTo(NextWanderPoint());
        }
    }

    void TickInvestigate(bool canSee)
    {
        if (canSee) { SetState(State.Chase); return; }
        if (Arrived())
            SetState(State.Search);
    }

    void TickSearch(bool canSee)
    {
        if (canSee) { SetState(State.Chase); return; }

        _searchTimer -= Time.deltaTime;
        if (_searchTimer <= 0f)
        {
            SetState(State.Patrol); // не нашли — возвращаемся к блужданию
            return;
        }

        if (Arrived())
        {
            ScanAround();
            // Расширяем зону поиска вокруг последней зацепки.
            _searchRadius = Mathf.Min(_searchRadius + 2.5f, patrolPointRadius);
            MoveTo(RandomNavPoint(_investigatePos, _searchRadius));
        }
    }

    void TickChase(bool canSee)
    {
        if (_player == null) { SetState(State.Search); return; }

        float dist = Vector3.Distance(transform.position, _player.position);
        if (dist <= attackRange) { SetState(State.Attack); return; }

        if (canSee)
        {
            // Упреждаем — идём туда, где игрок будет, а не где он был (срезаем углы).
            MoveTo(_player.position + _playerVelocity * predictLead);
        }
        else
        {
            if (Time.time - _lastSeenTime > loseSightTime)
            {
                _investigatePos = _lastSeenPos + _lastSeenVelocity * predictLead;
                SetState(State.Search);
                return;
            }
            // Продолжаем в сторону последнего замеченного направления движения.
            MoveTo(_lastSeenPos + _lastSeenVelocity * predictLead);
        }
    }

    void TickAttack(bool canSee)
    {
        StopAgent();
        FaceTarget(_player != null ? _player.position : transform.position + transform.forward);

        if (_player == null || (_playerHealth != null && _playerHealth.IsDead))
        {
            ResumeAgent();
            SetState(State.Idle);
            return;
        }

        float dist = Vector3.Distance(transform.position, _player.position);
        if (dist > attackRange * 1.15f)
        {
            ResumeAgent();
            SetState(State.Chase);
            return;
        }

        _attackTimer -= Time.deltaTime;
        if (_attackTimer <= 0f && !_attacking)
        {
            _attackTimer = attackCooldown;
            StartCoroutine(DoAttack());
        }
    }

    IEnumerator DoAttack()
    {
        _attacking = true;
        // "Замах" — урон наносится не мгновенно.
        yield return new WaitForSeconds(attackWindup);

        if (_state != State.Dead && _player != null && _playerHealth != null && !_playerHealth.IsDead)
        {
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist <= attackRange * 1.25f)
            {
                Vector3 dir = (_player.position - transform.position).normalized;
                _playerHealth.TakeDamage(attackDamage, _player.position, dir);
            }
        }

        _attacking = false;
    }

    // ---------------------------------------------------------------- Perception

    void HandleNoise(Vector3 position, float amount)
    {
        if (_state == State.Dead || amount < minHearableNoise)
            return;

        float audibleDistance = amount * hearingDistancePerNoise;
        if (Vector3.Distance(transform.position, position) > audibleDistance)
            return;

        _investigatePos = position;

        // Уже преследует/атакует — не сбиваем; иначе идём проверять источник.
        if (_state != State.Chase && _state != State.Attack)
            SetState(State.Investigate);
    }

    bool CanSeePlayer()
    {
        if (_player == null || (_playerHealth != null && _playerHealth.IsDead))
            return false;

        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * eyeHeight;
        Vector3 target = _player.position + Vector3.up * targetHeight;
        Vector3 to = target - origin;
        float dist = to.magnitude;

        // Присед делает игрока почти незаметным: режем дальность зрения и радиус чутья.
        bool crouching = _playerController != null && _playerController.IsCrouching;
        float effectiveViewDistance = crouching ? viewDistance * crouchVisionMultiplier : viewDistance;
        float effectiveSenseRadius = crouching ? senseRadius * crouchSenseMultiplier : senseRadius;

        if (dist > effectiveViewDistance)
            return false;

        Vector3 dir = to / Mathf.Max(dist, 0.0001f);

        // Вне конуса обзора видим только если игрок совсем рядом.
        if (dist > effectiveSenseRadius)
        {
            float angle = Vector3.Angle(transform.forward, dir);
            if (angle > viewAngle)
                return false;
        }

        // Линия видимости: если луч упёрся во что-то раньше игрока — обзор перекрыт.
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, visionMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform != _player && !hit.transform.IsChildOf(_player))
                return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- Helpers

    void AcquirePlayer()
    {
        var obj = GameObject.FindGameObjectWithTag(playerTag);
        if (obj == null)
            return;
        _player = obj.transform;
        _playerHealth = obj.GetComponent<PlayerHealth>();
        _playerController = obj.GetComponent<HorrorFirstPersonController>();
        _playerLastPos = _player.position;
    }

    void ApplySpeed()
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;

        float speed;
        switch (_state)
        {
            case State.Chase:
            case State.Attack: speed = chaseSpeed; break;
            case State.Investigate: speed = investigateSpeed; break;
            case State.Search: speed = investigateSpeed * 0.7f; break;
            default: speed = patrolSpeed; break;
        }

        if (_noise != null && _noise.CurrentTier == NoiseManager.Tier.Hunt)
            speed += huntSpeedBonus;

        _agent.speed = speed;
    }

    void SetState(State next)
    {
        _state = next;

        switch (next)
        {
            case State.Idle:
                // Пауза-осмотр на месте.
                StopAgent();
                _idleTimer = Random.Range(pauseDuration.x, pauseDuration.y);
                _scanTimer = 0f;
                break;

            case State.Patrol:
                ResumeAgent();
                MoveTo(NextWanderPoint());
                break;

            case State.Investigate:
                ResumeAgent();
                MoveTo(_investigatePos);
                break;

            case State.Search:
                ResumeAgent();
                _searchTimer = searchDuration;
                _searchRadius = Mathf.Max(3f, patrolPointRadius * 0.4f);
                MoveTo(RandomNavPoint(_investigatePos, _searchRadius));
                break;

            case State.Chase:
                ResumeAgent();
                break;

            case State.Attack:
                _attackTimer = 0f; // ударить как можно скорее
                break;

            case State.Dead:
                StopAgent();
                break;
        }
    }

    Vector3 RecentNoiseCenter()
    {
        if (_noise != null && Time.time - _noise.LastNoiseTime < 8f)
            return _noise.LastNoisePosition;
        return transform.position;
    }

    void TrackPlayerVelocity()
    {
        if (_player == null)
        {
            _playerVelocity = Vector3.zero;
            return;
        }

        Vector3 pos = _player.position;
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            Vector3 v = (pos - _playerLastPos) / dt;
            // Сглаживаем, чтобы предсказание не дёргалось.
            _playerVelocity = Vector3.Lerp(_playerVelocity, v, 1f - Mathf.Exp(-10f * dt));
        }
        _playerLastPos = pos;
    }

    // Вращаемся, периодически меняя направление взгляда — "осматриваемся".
    void ScanAround()
    {
        _scanTimer -= Time.deltaTime;
        if (_scanTimer <= 0f)
        {
            _scanTimer = Random.Range(0.7f, 1.6f);
            _scanYaw = transform.eulerAngles.y + Random.Range(-130f, 130f);
        }

        Quaternion target = Quaternion.Euler(0f, _scanYaw, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, scanTurnSpeed * Time.deltaTime);
    }

    // Следующая точка блуждания: достаточно далёкая от текущей, чтобы реально покрывать карту.
    // Если недавно был шум — с некоторым шансом дрейфуем в его сторону.
    Vector3 NextWanderPoint()
    {
        Vector3 basis = transform.position;
        if (_noise != null && Time.time - _noise.LastNoiseTime < 10f && Random.value < 0.5f)
            basis = Vector3.Lerp(basis, _noise.LastNoisePosition, 0.6f);

        for (int i = 0; i < 12; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(minWanderStep, wanderRadius);
            Vector3 candidate = basis + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                if (Vector3.Distance(hit.position, transform.position) >= minWanderStep)
                    return hit.position;
            }
        }
        return RandomNavPoint(transform.position, wanderRadius);
    }

    void MoveTo(Vector3 destination)
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return;
        _agent.isStopped = false;
        _agent.SetDestination(destination);
    }

    bool Arrived()
    {
        if (_agent == null || !_agent.isOnNavMesh)
            return true;
        if (_agent.pathPending)
            return false;
        return _agent.remainingDistance <= arriveThreshold;
    }

    void StopAgent()
    {
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = true;
    }

    void ResumeAgent()
    {
        if (_agent != null && _agent.isOnNavMesh)
            _agent.isStopped = false;
    }

    void FaceTarget(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            return;
        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }

    Vector3 RandomNavPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;
        }
        return center;
    }

    void HandleDeath()
    {
        SetState(State.Dead);
        StopAgent();
        if (_agent != null && _agent.isOnNavMesh)
            _agent.ResetPath();
        StopAllCoroutines();
        _attacking = false;
    }

    // ---------------------------------------------------------------- Gizmos

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * eyeHeight;

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Vector3 left = Quaternion.Euler(0f, -viewAngle, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, viewAngle, 0f) * transform.forward;
        Gizmos.DrawRay(origin, left * viewDistance);
        Gizmos.DrawRay(origin, right * viewDistance);

        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, senseRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
