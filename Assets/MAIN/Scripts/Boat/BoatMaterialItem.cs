using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Доска / бревно / бочка: ЛКМ ставит деталь. Q/E — поворот, колесо — наклон, Shift — мелкий шаг.
/// </summary>
public class BoatMaterialItem : HeldItem
{
    [SerializeField] BoatPieceKind kind = BoatPieceKind.Plank;

    GameObject _ghost;
    Vector3 _ghostSize;
    Vector3 _ghostPos;
    Vector3 _ghostPosVel;
    Quaternion _ghostRot;
    bool _ghostFollow;
    float _yaw;
    float _pitch;
    float _roll;
    float _qHeld;
    float _eHeld;
    bool _fromWorld;
    Vector3 _worldSize;
    Quaternion _poseRot = Quaternion.identity;

    static bool _pendingCarry;
    static BoatPieceKind _pendingKind;
    static Vector3 _pendingSize;
    static string _pendingName;
    static Quaternion _pendingRot;

    public BoatPieceKind Kind => kind;
    public Vector3 WorldSize => _worldSize.sqrMagnitude > 0.01f ? _worldSize : BoatVisuals.DefaultSize(kind);

    public static void BeginCarry(BoatPieceKind k, Vector3 size, string displayName, Quaternion worldRot)
    {
        _pendingCarry = true;
        _pendingKind = k;
        _pendingSize = size;
        _pendingName = displayName;
        _pendingRot = worldRot;
    }

    public void SetKind(BoatPieceKind k) => kind = k;

    public void SetWorldSize(Vector3 size)
    {
        _worldSize = size;
        ApplyHeldVisual();
    }

    public BoatPiece TryBecomeWorldPiece()
    {
        if (IsCarried)
            return null;
        var existing = GetComponent<BoatPiece>();
        if (existing != null)
            return existing;
        enabled = false;
        BoatPiece.PrepareSpawn(kind, WorldSize);
        var piece = gameObject.AddComponent<BoatPiece>();
        Destroy(this);
        return piece;
    }

    public override bool KeepActiveInInventory => BoatRope.IsTied(transform);

    protected override void Awake()
    {
        if (_pendingCarry)
        {
            kind = _pendingKind;
            _worldSize = _pendingSize;
            _fromWorld = true;
            SetDisplayName(_pendingName);
            _poseRot = _pendingRot;
            _yaw = 0f;
            _pitch = 0f;
            _roll = 0f;
            _pendingCarry = false;
        }
        else if (_worldSize.sqrMagnitude < 0.01f)
            _worldSize = BoatVisuals.DefaultSize(kind);

        base.Awake();
        if (!_fromWorld)
            ApplyHeldVisual();
    }

    public override void OnEquip()
    {
        base.OnEquip();
        HideHeldMesh();
        _ghostFollow = false;
        BoatBuildHud.Hint("LMB place   Wheel turn   hold Q/E tilt   G drop", 4f);
    }

    public override void OnUnequip()
    {
        ClearGhost();
        BoatBuildHud.Clear();
        base.OnUnequip();
    }

    protected override void OnRetractChanged(float retract)
    {
        HideHeldMesh();
    }

    public override void OnUseStart()
    {
        if (IsUseBlocked)
            return;
        if (!TryPose(out Vector3 pos, out Quaternion rot))
        {
            BoatBuildHud.Hint("Aim at ground or a piece");
            return;
        }
        Place(pos, rot);
    }

    void Update()
    {
        if (!IsEquipped)
        {
            ClearGhost();
            return;
        }

        TickRotate(Time.deltaTime);

        if (!TryPose(out Vector3 pos, out Quaternion rot))
        {
            if (_ghost != null)
                _ghost.SetActive(false);
            _ghostFollow = false;
            HideHeldMesh();
            return;
        }
        EnsureGhost();
        _ghost.SetActive(true);
        if (!_ghostFollow)
        {
            _ghostPos = pos;
            _ghostRot = rot;
            _ghostPosVel = Vector3.zero;
            _ghostFollow = true;
        }
        else
        {
            _ghostPos = Vector3.SmoothDamp(_ghostPos, pos, ref _ghostPosVel, 0.055f, 28f, Time.deltaTime);
            _ghostRot = Quaternion.Slerp(_ghostRot, rot, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
        _ghost.transform.SetPositionAndRotation(_ghostPos, _ghostRot);
        HideHeldMesh();
    }

    void TickRotate(float dt)
    {
        var kb = Keyboard.current;
        const float tap = 5f;
        const float holdDelay = 0.16f;
        const float holdDeg = 120f;
        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        float tapStep = shift ? 2f : tap;
        float holdSpeed = shift ? holdDeg * 0.4f : holdDeg;

        if (kb != null && kb.qKey.wasPressedThisFrame)
        {
            _pitch -= tapStep;
            _qHeld = 0f;
        }
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            _pitch += tapStep;
            _eHeld = 0f;
        }
        if (kb != null && kb.qKey.isPressed)
        {
            _qHeld += dt;
            if (_qHeld > holdDelay)
                _pitch -= holdSpeed * dt;
        }
        else
            _qHeld = 0f;
        if (kb != null && kb.eKey.isPressed)
        {
            _eHeld += dt;
            if (_eHeld > holdDelay)
                _pitch += holdSpeed * dt;
        }
        else
            _eHeld = 0f;
        if (kb != null && kb.rKey.wasPressedThisFrame)
            _roll += 90f;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                _yaw += scroll > 0f ? tapStep : -tapStep;
        }
    }

    bool TryPose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = _poseRot * Quaternion.Euler(_pitch, _yaw, _roll);
        if (!BoatBuildUtil.Aim(Owner, 6f, out RaycastHit hit))
            return false;

        Vector3 size = WorldSize;
        rot = _poseRot * Quaternion.Euler(_pitch, _yaw, _roll);
        float lift = ProjectExtent(rot, size, Vector3.up) + 0.02f;
        float search = Mathf.Max(0.65f, Mathf.Max(size.x, size.z) * 0.5f);
        float y = hit.point.y;
        if (hit.collider != null)
        {
            var piece = hit.collider.GetComponentInParent<BoatPiece>();
            if (piece != null)
                y = hit.collider.bounds.max.y;
        }
        if (BoatBuildUtil.TrySupportTop(hit.point, search, Owner, out float supportY) && supportY > y - 0.02f)
            y = supportY;
        pos = new Vector3(hit.point.x, y + lift, hit.point.z);
        return true;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static float ProjectExtent(Quaternion rot, Vector3 size, Vector3 n)
    {
        Vector3 w = Abs(rot * size);
        return 0.5f * Vector3.Dot(w, Abs(n.normalized));
    }

    void Place(Vector3 pos, Quaternion rot)
    {
        if (_ghost != null && _ghost.activeInHierarchy)
        {
            pos = _ghost.transform.position;
            rot = _ghost.transform.rotation;
        }
        BoatBuildHud.Clear();
        ClearGhost();
        var support = BoatBuildUtil.Aim(Owner, 6f, out RaycastHit hit)
            ? hit.collider
            : null;

        if (_fromWorld)
        {
            enabled = false;
            Inventory?.ClearEquippedKeepObject();
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(pos, rot);
            gameObject.SetActive(true);
            var live = gameObject.AddComponent<BoatPiece>();
            live.Configure(kind, WorldSize);
            BoatBuildUtil.Settle(live, support);
            Destroy(this);
            return;
        }

        var go = new GameObject(kind + "Piece");
        go.transform.SetPositionAndRotation(pos, rot);
        var piece = go.AddComponent<BoatPiece>();
        piece.Configure(kind, WorldSize);
        BoatBuildUtil.Settle(piece, support);
        if (Inventory != null)
            Inventory.DestroyEquipped();
        else
            Destroy(gameObject);
    }

    protected override void OnDropped()
    {
        base.OnDropped();
        Invoke(nameof(BecomeWorldPiece), 0f);
    }

    void BecomeWorldPiece()
    {
        if (this == null || GetComponent<BoatPiece>() != null)
            return;
        var piece = gameObject.AddComponent<BoatPiece>();
        piece.Configure(kind, WorldSize);
        Destroy(this);
    }

    void ApplyHeldVisual()
    {
        BoatVisuals.Attach(
            transform,
            BoatVisuals.Shape(kind),
            BoatVisuals.VisualScale(kind, WorldSize),
            BoatVisuals.VisualRotation(kind),
            BoatVisuals.MaterialFor(kind));
    }

    void EnsureGhost()
    {
        Vector3 size = WorldSize;
        if (_ghost != null && (_ghostSize - size).sqrMagnitude > 0.0001f)
            ClearGhost();
        if (_ghost != null)
            return;
        _ghost = new GameObject("Ghost");
        _ghostSize = size;
        BoatVisuals.Attach(
            _ghost.transform,
            BoatVisuals.Shape(kind),
            BoatVisuals.VisualScale(kind, size),
            BoatVisuals.VisualRotation(kind),
            BoatVisuals.Ghost);
        BoatVisuals.SetIgnoreRaycast(_ghost);
        var col = _ghost.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);
        _ghost.SetActive(false);
    }

    void HideHeldMesh()
    {
        var rs = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null)
                continue;
            if (_ghost != null && rs[i].transform.IsChildOf(_ghost.transform))
                continue;
            rs[i].enabled = false;
        }
    }

    void ClearGhost()
    {
        if (_ghost != null)
            Destroy(_ghost);
        _ghost = null;
        _ghostFollow = false;
    }

    void OnDestroy()
    {
        ClearGhost();
    }
}
