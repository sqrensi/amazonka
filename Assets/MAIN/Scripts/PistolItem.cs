using UnityEngine;

/// <summary>
/// Пистолет: полуавтомат, луч из дула в центр экрана. Выстрел даёт звук и вспышку на Muzzle.
/// </summary>
public class PistolItem : HeldItem
{
    protected override bool SinksInWater => true;
    [Header("Ballistics")]
    [SerializeField] float damage = 50f;
    [SerializeField] float range = 120f;
    [Tooltip("Мин. интервал между выстрелами (полуавтомат).")]
    [SerializeField] float fireInterval = 0.28f;
    [Tooltip("Слои, по которым бьёт луч (стены + враги). Триггеры игнорируются.")]
    [SerializeField] LayerMask hitMask = ~0;
    [Tooltip("Импульс пули по предметам с Rigidbody (на земле и в полёте).")]
    [SerializeField] float bulletImpulse = 14f;
    [Tooltip("Толщина луча, чтобы попадать по летящим предметам.")]
    [SerializeField] float shotRadius = 0.11f;

    [Header("Tracer (визуальная траектория пули)")]
    [Tooltip("Точка вылета пули (дуло). Если пусто — позиция модели пистолета.")]
    [SerializeField] Transform muzzle;
    [SerializeField] float tracerSpeed = 260f;
    [SerializeField] float tracerStreakLength = 4f;
    [SerializeField] float tracerWidth = 0.03f;
    [SerializeField] Color tracerColor = new Color(1f, 0.85f, 0.4f, 1f);

    [Header("Shot FX")]
    [SerializeField] AudioClip shotClip;
    [SerializeField] GameObject muzzleFlashPrefab;
    [SerializeField, Range(0f, 1f)] float shotVolume = 0.85f;

    Camera _camera;
    AudioSource _shotSource;
    float _nextFireTime;
    readonly RaycastHit[] _hits = new RaycastHit[24];

    public override void Initialize(PlayerInventory inventory, GameObject owner)
    {
        base.Initialize(inventory, owner);
        if (owner != null)
            _camera = owner.GetComponentInChildren<Camera>();
        if (_camera == null)
            _camera = Camera.main;
        EnsureShotAudio();
    }

    public override void OnUnequip()
    {
        base.OnUnequip();
        _shotSource = null;
    }

    protected override void OnDropped()
    {
        base.OnDropped();
        _shotSource = null;
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
        AddKick(new Vector3(0.004f, 0.012f, -0.028f), new Vector3(-7.5f, UnityEngine.Random.Range(-1.8f, 1.8f), UnityEngine.Random.Range(-2f, 2f)));

        PlayShot();
        SpawnMuzzleFlash();

        if (_camera == null)
            return;

        Transform cam = _camera.transform;
        Vector3 lookOrigin = cam.position + cam.forward * 0.22f;
        Vector3 lookDir = cam.forward;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 endPoint = origin + lookDir * range;

        if (ShotCast(lookOrigin, lookDir, out RaycastHit hit))
            ApplyHit(hit, lookDir, origin, ref endPoint);

        BulletTracer.Spawn(origin, endPoint, tracerSpeed, tracerStreakLength, tracerColor, tracerWidth);
    }

    bool ShotCast(Vector3 origin, Vector3 dir, out RaycastHit hit)
    {
        int count = Physics.SphereCastNonAlloc(
            origin, shotRadius, dir, _hits, range, hitMask, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDist = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (IsOwnHit(_hits[i].collider))
                continue;
            if (_hits[i].distance < bestDist)
            {
                bestDist = _hits[i].distance;
                best = i;
            }
        }
        if (best >= 0)
        {
            hit = _hits[best];
            return true;
        }

        count = Physics.RaycastNonAlloc(origin, dir, _hits, range, hitMask, QueryTriggerInteraction.Ignore);
        best = -1;
        bestDist = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (IsOwnHit(_hits[i].collider))
                continue;
            if (_hits[i].distance < bestDist)
            {
                bestDist = _hits[i].distance;
                best = i;
            }
        }
        if (best >= 0)
        {
            hit = _hits[best];
            return true;
        }

        hit = default;
        return false;
    }

    void ApplyHit(RaycastHit hit, Vector3 dir, Vector3 tracerOrigin, ref Vector3 endPoint)
    {
        endPoint = hit.point;
        if ((endPoint - tracerOrigin).sqrMagnitude < 0.01f)
            endPoint = tracerOrigin + dir * 0.35f;

        var target = hit.collider.GetComponentInParent<IDamageable>();
        if (target != null && !target.IsDead)
        {
            var actor = RaceRoster.Local();
            if (actor != null)
                actor.WeaponName = DisplayName;
            ushort id = actor != null ? actor.Id : (ushort)0;
            RaceSim.RequestDamage(id, target, SharkDamage(target), hit.point, dir, DisplayName);
        }

        Rigidbody body = hit.rigidbody != null ? hit.rigidbody : hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic)
            return;
        if (IsOwnHit(hit.collider))
            return;

        Vector3 forceDir = dir.sqrMagnitude > 0.001f ? dir.normalized : hit.normal * -1f;
        body.AddForceAtPosition(forceDir * bulletImpulse, hit.point, ForceMode.Impulse);
        body.AddTorque(Vector3.Cross(forceDir, Random.onUnitSphere) * (bulletImpulse * 0.18f), ForceMode.Impulse);
    }

    float SharkDamage(IDamageable target)
    {
        if (target is RiverShark)
            return 64f;
        return damage;
    }

    bool IsOwnHit(Collider col)
    {
        if (col == null)
            return true;
        Transform t = col.transform;
        if (t == transform || t.IsChildOf(transform))
            return true;
        if (Owner != null && (t == Owner.transform || t.IsChildOf(Owner.transform)))
            return true;
        return false;
    }

    void PlayShot()
    {
        if (shotClip == null)
            return;
        EnsureShotAudio();
        if (_shotSource == null)
            return;
        _shotSource.pitch = Random.Range(0.96f, 1.04f);
        _shotSource.PlayOneShot(shotClip, shotVolume);
    }

    void SpawnMuzzleFlash()
    {
        if (muzzleFlashPrefab == null)
            return;
        Transform parent = muzzle != null ? muzzle : transform;
        var fx = Instantiate(muzzleFlashPrefab, parent, false);
        fx.transform.localPosition = Vector3.zero;
        Destroy(fx, 1.5f);
    }

    void EnsureShotAudio()
    {
        if (_shotSource != null)
            return;
        Transform parent = muzzle != null ? muzzle : transform;
        var go = new GameObject("ShotAudio");
        go.transform.SetParent(parent, false);
        _shotSource = go.AddComponent<AudioSource>();
        _shotSource.playOnAwake = false;
        _shotSource.spatialBlend = 0.28f;
        _shotSource.minDistance = 1.2f;
        _shotSource.maxDistance = 28f;
        _shotSource.rolloffMode = AudioRolloffMode.Linear;
        _shotSource.priority = 32;
    }
}
