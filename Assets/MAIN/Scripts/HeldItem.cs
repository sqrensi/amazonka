using System.Collections;
using UnityEngine;

/// <summary>
/// Один и тот же предмет живёт и в мире, и в инвентаре.
/// В мире: физика, подбор по F, фонарь продолжает светить если был включён.
/// В руке: без физики, поза перед камерой, sway/retract.
/// </summary>
public abstract class HeldItem : MonoBehaviour, IInteractable
{
    [Header("Item")]
    [Tooltip("Отображаемое имя (в подсказке подбора и слоте инвентаря).")]
    [SerializeField] string displayName = "Item";
    [Tooltip("Желаемый быстрый слот (0..4 -> клавиши 1..5). -1 = первый свободный.")]
    [SerializeField] int preferredSlot = -1;
    [Tooltip("Во что превращается при успешном апгрейде в рулетке. Пусто = улучшать нельзя.")]
    [SerializeField] HeldItem upgradeResultPrefab;

    [Header("Положение в руке (относительно сокета камеры)")]
    [SerializeField] Vector3 heldLocalPosition = Vector3.zero;
    [SerializeField] Vector3 heldLocalEuler = Vector3.zero;
    [SerializeField] Vector3 heldLocalScale = Vector3.one;

    [Header("Убирание у стены (retract)")]
    [Tooltip("Смещение к камере вплотную к стене (предмет остаётся видимым).")]
    [SerializeField] Vector3 retractedPositionOffset = new Vector3(0.015f, -0.035f, -0.2f);
    [Tooltip("Небольшой доворот, чтобы ствол/корпус не лезли в стену.")]
    [SerializeField] Vector3 retractedEulerOffset = new Vector3(-10f, 6f, -8f);
    [Tooltip("Время сглаживания убирания/доставания (сек). Больше — мягче.")]
    [SerializeField] float retractSmoothTime = 0.42f;
    [Tooltip("Порог убранности, при котором использование блокируется (0..1).")]
    [SerializeField, Range(0f, 1f)] float useBlockThreshold = 0.82f;

    protected PlayerInventory Inventory { get; private set; }
    protected GameObject Owner { get; private set; }

    Renderer[] _renderers;
    float _retract;
    float _retractTarget;
    float _retractVel;
    bool _hidden;

    HorrorFirstPersonController _ownerMove;
    float _equip;
    Vector3 _swayPos;
    Vector3 _swayPosVel;
    Vector3 _swayEuler;
    Vector3 _swayEulerVel;
    Vector3 _kickPos;
    Vector3 _kickPosVel;
    Vector3 _kickEuler;
    Vector3 _kickEulerVel;
    float _idleT;

    Rigidbody _rb;
    Collider _col;
    bool _carried;
    Coroutine _ignoreRoutine;
    float _droppedAt = -100f;
    bool _droppedByPlayer;

    public string DisplayName => displayName;
    public void SetDisplayName(string n)
    {
        if (!string.IsNullOrEmpty(n))
            displayName = n;
    }
    public int PreferredSlot => preferredSlot;
    public HeldItem UpgradeResultPrefab => upgradeResultPrefab;
    public bool CanUpgrade => upgradeResultPrefab != null;
    public bool IsCarried => _carried;
    /// <summary>Не выключать объект в инвентаре (верёвка / живая деталь лодки).</summary>
    public virtual bool KeepActiveInInventory => false;
    public bool WasDroppedByPlayer => _droppedByPlayer;
    public bool IsEquipped => _carried && Inventory != null && Inventory.Current == this;

    /// <summary>Текущая доля убранности предмета (0 — в руке, 1 — полностью убран).</summary>
    public float RetractAmount => _retract;

    /// <summary>Предмет достаточно убран у стены — использование заблокировано.</summary>
    public bool IsUseBlocked => !IsEquipped || _retract >= useBlockThreshold;

    public string GetPrompt() => $"Pickup {displayName}";
    public Transform GetAnchor() => transform;
    public string GetInteractKey() => "F";

    public bool CanInteract(GameObject interactor)
    {
        if (_carried || !isActiveAndEnabled || interactor == null)
            return false;
        if (GetComponent<BoatPiece>() != null)
            return false;
        if (!IsPickupReady())
            return false;
        var inventory = interactor.GetComponent<PlayerInventory>();
        return inventory != null && inventory.HasFreeSlot(preferredSlot);
    }

    bool IsPickupReady()
    {
        // Сразу после G предмет ещё в руках/в полёте — не показываем [F].
        if (Time.time < _droppedAt + 0.55f)
            return false;
        if (_rb != null && !_rb.isKinematic && _rb.linearVelocity.sqrMagnitude > 1.6f * 1.6f)
            return false;
        return true;
    }

    public void Interact(GameObject interactor)
    {
        var inventory = interactor != null ? interactor.GetComponent<PlayerInventory>() : null;
        inventory?.PickupExisting(this);
    }

    protected virtual void Awake()
    {
        EnsurePhysics();
        if (!_carried)
            SetPhysicsEnabled(true);
    }

    /// <summary>Применить позу в руке с учётом текущей убранности (retract).</summary>
    public void ApplyHeldPose()
    {
        float hide = 1f - _equip;
        Vector3 equipOffset = new Vector3(0.02f, -0.07f, -0.05f) * hide;
        float tuck = _retract * _retract * (3f - 2f * _retract);
        Vector3 retract = retractedPositionOffset * tuck;
        transform.localPosition = heldLocalPosition + retract + _swayPos + _kickPos + equipOffset;
        transform.localRotation = Quaternion.Euler(heldLocalEuler + retractedEulerOffset * tuck + _swayEuler + _kickEuler
            + new Vector3(12f, -4f, 0f) * hide);
        transform.localScale = heldLocalScale;
    }

    /// <summary>Вызывается при попадании в инвентарь. Состояние (свет фонаря) не сбрасывается.</summary>
    public virtual void Initialize(PlayerInventory inventory, GameObject owner)
    {
        Inventory = inventory;
        Owner = owner;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _ownerMove = owner != null ? owner.GetComponent<HorrorFirstPersonController>() : null;
        _equip = 0f;
    }

    public void ReleaseFromHands()
    {
        _carried = false;
        Inventory = null;
        Owner = null;
        _ownerMove = null;
        SetPhysicsEnabled(true);
    }

    public void AttachToInventory(PlayerInventory inventory, GameObject owner)
    {
        ReleasePlayingSounds();
        StopIgnoreRoutine();
        _carried = true;
        _droppedByPlayer = false;
        SetPhysicsEnabled(false);
        Transform socket = inventory != null ? inventory.HeldSocket : null;
        transform.SetParent(socket, false);
        Initialize(inventory, owner);
        SetRenderersHidden(false);
    }

    public void DropIntoWorld(Vector3 velocity, GameObject ignoreWith)
    {
        OnDropped();
        Inventory = null;
        Owner = null;
        _ownerMove = null;
        _carried = false;
        _droppedAt = Time.time;
        _droppedByPlayer = true;

        // worldPositionStays: предмет остаётся там, где был в руке. ApplyHeldPose здесь нельзя —
        // localPosition стал бы мировым и предмет улетал в (0,0,0) / в геометрию.
        transform.SetParent(null, true);
        gameObject.SetActive(true);
        _retract = 0f;
        _retractTarget = 0f;
        _retractVel = 0f;
        _swayPos = _swayPosVel = Vector3.zero;
        _swayEuler = _swayEulerVel = Vector3.zero;
        _kickPos = _kickPosVel = Vector3.zero;
        _kickEuler = _kickEulerVel = Vector3.zero;
        SetRenderersHidden(false);

        SetPhysicsEnabled(true);
        Physics.SyncTransforms();
        KeepAboveGround();

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.ClampMagnitude(velocity, 12f);
            _rb.angularVelocity = Random.insideUnitSphere * 2.2f;
        }

        if (ignoreWith != null)
        {
            StopIgnoreRoutine();
            _ignoreRoutine = StartCoroutine(IgnorePlayerUntilClear(ignoreWith));
        }
    }

    /// <summary>Выброс в мир (фонарь не гасим — состояние сохраняется).</summary>
    protected virtual void OnDropped()
    {
        ReleasePlayingSounds();
    }

    /// <summary>
    /// Играющие звуки отвязываются от предмета, чтобы SetActive(false) или смена родителя
    /// не обрывали выстрел / клик.
    /// </summary>
    protected void ReleasePlayingSounds()
    {
        var sources = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null || !source.isPlaying)
                continue;

            float remain = 3f;
            if (source.clip != null && Mathf.Abs(source.pitch) > 0.01f)
                remain = Mathf.Max(0.08f, (source.clip.length - source.time) / Mathf.Abs(source.pitch));

            if (source.gameObject == gameObject)
            {
                if (source.clip != null)
                    AudioSource.PlayClipAtPoint(source.clip, transform.position, source.volume);
                source.Stop();
                continue;
            }

            source.transform.SetParent(null, true);
            source.playOnAwake = false;
            Destroy(source.gameObject, remain + 0.2f);
        }
    }

    public void SetRetractTarget(float target01)
    {
        _retractTarget = Mathf.Clamp01(target01);
    }

    public void ResetRetract()
    {
        _retract = 0f;
        _retractTarget = 0f;
        _retractVel = 0f;
        _swayPos = _swayPosVel = Vector3.zero;
        _swayEuler = _swayEulerVel = Vector3.zero;
        _kickPos = _kickPosVel = Vector3.zero;
        _kickEuler = _kickEulerVel = Vector3.zero;
        ApplyHeldPose();
        SetRenderersHidden(false);
    }

    public void TickRetract(float dt)
    {
        _retract = Mathf.SmoothDamp(_retract, _retractTarget, ref _retractVel, retractSmoothTime, 1.35f, dt);
        _retract = Mathf.Clamp01(_retract);

        TickSway(dt);
        ApplyHeldPose();
        OnRetractChanged(_retract);
    }

    public void AddKick(Vector3 localPos, Vector3 localEuler)
    {
        _kickPos += localPos;
        _kickEuler += localEuler;
    }

    void TickSway(float dt)
    {
        _equip = Mathf.MoveTowards(_equip, 1f, dt / 0.22f);
        _idleT += dt;

        Vector3 posTarget = Vector3.zero;
        Vector3 eulerTarget = Vector3.zero;

        if (_ownerMove != null)
        {
            Vector2 look = _ownerMove.LookVelocity;
            Vector3 move = _ownerMove.LocalPlanarVelocity;
            float cycle = _ownerMove.StepCycle * Mathf.PI * 2f;
            float bobW = _ownerMove.BobWeight;

            eulerTarget.y = Mathf.Clamp(-look.x * 0.018f, -7f, 7f);
            eulerTarget.x = Mathf.Clamp(look.y * 0.014f, -5f, 5f);
            eulerTarget.z = Mathf.Clamp(look.x * 0.01f - move.x * 1.4f, -6f, 6f);

            posTarget.x = Mathf.Clamp(-look.x * 0.00035f - move.x * 0.012f, -0.045f, 0.045f);
            posTarget.y = Mathf.Clamp(-look.y * 0.00028f - move.z * 0.004f, -0.035f, 0.035f);
            posTarget.z = Mathf.Clamp(move.z * 0.006f, -0.03f, 0.03f);

            posTarget.x += Mathf.Sin(cycle * 0.5f) * 0.011f * bobW;
            posTarget.y += Mathf.Sin(cycle) * 0.009f * bobW + _ownerMove.StepPunch * 0.35f + _ownerMove.LandPunch * 0.45f;
            eulerTarget.z += Mathf.Sin(cycle * 0.5f) * 1.6f * bobW;
            eulerTarget.x += Mathf.Sin(cycle) * 1.1f * bobW;
        }

        posTarget.x += Mathf.Sin(_idleT * 0.9f) * 0.0022f;
        posTarget.y += Mathf.Cos(_idleT * 1.15f) * 0.0018f;
        eulerTarget.z += Mathf.Sin(_idleT * 0.7f) * 0.35f;

        _swayPos = Vector3.SmoothDamp(_swayPos, posTarget, ref _swayPosVel, 0.11f, 2f, dt);
        _swayEuler = Vector3.SmoothDamp(_swayEuler, eulerTarget, ref _swayEulerVel, 0.13f, 80f, dt);
        _kickPos = Vector3.SmoothDamp(_kickPos, Vector3.zero, ref _kickPosVel, 0.07f, 8f, dt);
        _kickEuler = Vector3.SmoothDamp(_kickEuler, Vector3.zero, ref _kickEulerVel, 0.08f, 120f, dt);
    }

    void SetRenderersHidden(bool hidden)
    {
        if (_hidden == hidden)
            return;
        _hidden = hidden;

        if (_renderers == null)
            _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers)
            if (r != null)
                r.enabled = !hidden;
    }

    void EnsurePhysics()
    {
        _col = GetComponent<Collider>();
        if (_col == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            if (GetComponent<BoatMaterialItem>() == null)
                FitBox(box);
            _col = box;
        }

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();

        _rb.mass = 1.35f;
        _rb.linearDamping = 0.55f;
        _rb.angularDamping = 0.85f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.useGravity = true;
        _rb.maxDepenetrationVelocity = 2.5f;

        if (_col != null && _col.sharedMaterial == null)
        {
            var mat = new PhysicsMaterial("HeldItemGround")
            {
                dynamicFriction = 0.55f,
                staticFriction = 0.65f,
                bounciness = 0.08f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            _col.sharedMaterial = mat;
        }
    }

    void SetPhysicsEnabled(bool on)
    {
        EnsurePhysics();
        if (_col != null)
            _col.enabled = on;
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = on;
        }
        if (_rb != null)
        {
            _rb.detectCollisions = on;
            if (on)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }
            else
            {
                BoatBuildUtil.StopMotion(_rb);
                _rb.useGravity = false;
                _rb.isKinematic = true;
            }
        }
    }

    static void FitBox(BoxCollider box)
    {
        Bounds b = LocalRendererBounds(box.transform);
        Vector3 size = b.size;
        size.x = Mathf.Clamp(size.x, 0.12f, 1.6f);
        size.y = Mathf.Clamp(size.y, 0.04f, 0.8f);
        size.z = Mathf.Clamp(size.z, 0.12f, 1.6f);
        box.center = b.center;
        box.size = size;
        box.isTrigger = false;
    }

    static Bounds LocalRendererBounds(Transform root)
    {
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var f in filters)
        {
            if (f.sharedMesh == null)
                continue;
            Bounds mb = f.sharedMesh.bounds;
            Vector3[] corners =
            {
                mb.min, mb.max,
                new Vector3(mb.min.x, mb.min.y, mb.max.z),
                new Vector3(mb.min.x, mb.max.y, mb.min.z),
                new Vector3(mb.max.x, mb.min.y, mb.min.z),
                new Vector3(mb.max.x, mb.max.y, mb.min.z),
                new Vector3(mb.max.x, mb.min.y, mb.max.z),
                new Vector3(mb.min.x, mb.max.y, mb.max.z)
            };
            foreach (var c in corners)
            {
                Vector3 local = root.InverseTransformPoint(f.transform.TransformPoint(c));
                if (!any)
                {
                    b = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                    b.Encapsulate(local);
            }
        }

        return any ? b : new Bounds(Vector3.zero, Vector3.one * 0.3f);
    }

    void StopIgnoreRoutine()
    {
        if (_ignoreRoutine != null)
        {
            StopCoroutine(_ignoreRoutine);
            _ignoreRoutine = null;
        }
    }

    IEnumerator IgnorePlayerUntilClear(GameObject other)
    {
        var others = other.GetComponentsInChildren<Collider>(true);
        var self = GetComponentsInChildren<Collider>(true);
        SetIgnore(self, others, true);

        float clearDist = 1.15f;
        float t = 0f;
        while (t < 1.25f && other != null)
        {
            Vector3 delta = transform.position - other.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude >= clearDist * clearDist)
                break;
            t += Time.deltaTime;
            yield return null;
        }

        SetIgnore(self, others, false);
        _ignoreRoutine = null;
    }

    static void SetIgnore(Collider[] a, Collider[] b, bool ignore)
    {
        foreach (var ca in a)
        {
            if (ca == null) continue;
            foreach (var cb in b)
            {
                if (cb == null) continue;
                Physics.IgnoreCollision(ca, cb, ignore);
            }
        }
    }

    void FixedUpdate()
    {
        if (_carried || _rb == null || _rb.isKinematic)
            return;
        const float maxSpeed = 13f;
        if (_rb.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            _rb.linearVelocity = Vector3.ClampMagnitude(_rb.linearVelocity, maxSpeed);
        if (_rb.angularVelocity.sqrMagnitude > 80f)
            _rb.angularVelocity = Vector3.ClampMagnitude(_rb.angularVelocity, 9f);
        KeepAboveGround();
    }

    void KeepAboveGround()
    {
        if (_col == null)
            return;
        float skin = 0.04f;
        Vector3 origin = transform.position + Vector3.up * 0.8f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 2.4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        Vector3 ground = Vector3.zero;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsOwnCollider(hits[i].collider))
                continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                ground = hits[i].point;
            }
        }
        if (best == float.PositiveInfinity)
            return;

        float bottom = _col.bounds.min.y;
        if (bottom >= ground.y - 0.002f)
            return;
        float lift = ground.y + skin - bottom;
        if (lift <= 0f)
            return;
        transform.position += Vector3.up * lift;
        if (_rb != null && !_rb.isKinematic && _rb.linearVelocity.y < 0f)
        {
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            _rb.linearVelocity = v;
        }
    }

    bool IsOwnCollider(Collider col)
    {
        return col != null && (col.transform == transform || col.transform.IsChildOf(transform));
    }

    protected virtual void OnRetractChanged(float retract) { }

    public virtual void OnEquip()
    {
        _equip = 0f;
    }

    /// <summary>Живая деталь с верёвкой: оставить в мире, слот не теряем.</summary>
    public void ParkInWorld(GameObject ignoreWith)
    {
        transform.SetParent(null, true);
        gameObject.SetActive(true);
        SetPhysicsEnabled(true);
        Physics.SyncTransforms();
        KeepAboveGround();
        BoatBuildUtil.StopMotion(_rb);
        if (ignoreWith != null)
        {
            StopIgnoreRoutine();
            _ignoreRoutine = StartCoroutine(IgnorePlayerUntilClear(ignoreWith));
        }
    }

    public virtual void OnUnequip()
    {
        ReleasePlayingSounds();
    }

    public virtual void OnUseStart() { }

    public virtual void OnUseStop() { }
}
