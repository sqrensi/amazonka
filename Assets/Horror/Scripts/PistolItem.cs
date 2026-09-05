using UnityEngine;

/// <summary>
/// Пистолет: полуавтомат, стреляет лучом из центра камеры. Урон наносится всему,
/// что реализует <see cref="IDamageable"/> (в т.ч. монстру -> его смерть).
/// Пока без звуков и визуальных эффектов — только выстрел, попадание и шум для монстра.
/// </summary>
public class PistolItem : HeldItem
{
    [Header("Ballistics")]
    [SerializeField] float damage = 50f;
    [SerializeField] float range = 120f;
    [Tooltip("Мин. интервал между выстрелами (полуавтомат).")]
    [SerializeField] float fireInterval = 0.28f;
    [Tooltip("Слои, по которым бьёт луч (стены + враги). Триггеры игнорируются.")]
    [SerializeField] LayerMask hitMask = ~0;

    [Header("Noise")]
    [Tooltip("Громкость выстрела для системы шума (монстр слышит издалека).")]
    [SerializeField] float shotNoise = 65f;

    [Header("Tracer (визуальная траектория пули)")]
    [Tooltip("Точка вылета пули (дуло). Если пусто — позиция модели пистолета.")]
    [SerializeField] Transform muzzle;
    [SerializeField] float tracerSpeed = 260f;
    [SerializeField] float tracerStreakLength = 4f;
    [SerializeField] float tracerWidth = 0.03f;
    [SerializeField] Color tracerColor = new Color(1f, 0.85f, 0.4f, 1f);

    Camera _camera;
    PlayerNoise _playerNoise;
    float _nextFireTime;

    public override void Initialize(PlayerInventory inventory, GameObject owner)
    {
        base.Initialize(inventory, owner);
        if (owner != null)
        {
            _camera = owner.GetComponentInChildren<Camera>();
            _playerNoise = owner.GetComponent<PlayerNoise>();
        }
        if (_camera == null)
            _camera = Camera.main;
    }

    public override void OnUseStart()
    {
        Fire();
    }

    void Fire()
    {
        if (Time.time < _nextFireTime || IsUseBlocked)
            return;
        _nextFireTime = Time.time + fireInterval;

        // Выстрел слышно (монстр реагирует), даже если промахнулись.
        if (_playerNoise != null)
            _playerNoise.EmitNoise(shotNoise);
        else if (NoiseManager.Instance != null)
            NoiseManager.Instance.Report(transform.position, shotNoise);

        if (_camera == null)
            return;

        // 1) Куда целимся: точка "в центре экрана" (луч из центра камеры).
        Ray centerRay = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint = Physics.Raycast(centerRay, out RaycastHit aimHit, range, hitMask, QueryTriggerInteraction.Ignore)
            ? aimHit.point
            : centerRay.origin + centerRay.direction * range;

        // 2) Реальный луч выстрела идёт из дула (Muzzle внутри PistolItem) в центр экрана.
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 dir = (aimPoint - origin).normalized;
        Vector3 endPoint = origin + dir * range;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && !target.IsDead)
                target.TakeDamage(damage, hit.point, dir);
        }

        // Визуальная траектория пули — от дула к точке попадания.
        BulletTracer.Spawn(origin, endPoint, tracerSpeed, tracerStreakLength, tracerColor, tracerWidth);
    }
}
