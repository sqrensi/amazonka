using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Деталь лодки в мире: доска, бревно или бочка. Физика, гвозди, распил, плавучесть, посадка.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BoatPiece : MonoBehaviour, IInteractable
{
    [SerializeField] BoatPieceKind kind = BoatPieceKind.Plank;
    [SerializeField] Vector3 pieceSize = new Vector3(0.22f, 0.045f, 1.15f);

    Rigidbody _rb;
    BoxCollider _box;
    Vector3 _colCenter;
    readonly List<BoatNail> _nails = new List<BoatNail>();
    static bool _spawnPending;
    static BoatPieceKind _spawnKind;
    static Vector3 _spawnSize;
    static readonly List<BoatPiece> IslandQueue = new List<BoatPiece>(32);
    static readonly List<BoatPiece> IslandTmp = new List<BoatPiece>(32);
    static readonly HashSet<BoatPiece> IslandSeen = new HashSet<BoatPiece>();
    static readonly Collider[] OverlapScratch = new Collider[24];

    public BoatPieceKind Kind => kind;
    public Rigidbody Body => _rb;
    public Vector3 PieceSize => pieceSize;
    public Vector3 ColliderCenter => _box != null ? _box.center : _colCenter;
    public IReadOnlyList<BoatNail> Nails => _nails;

    public string GetPrompt()
    {
        if (IsLockedInBoat())
            return "";
        return $"Pick up {KindName(kind)}";
    }

    public string GetInteractKey() => "F";
    public Transform GetAnchor() => transform;

    public bool CanInteract(GameObject interactor)
    {
        if (interactor == null || !isActiveAndEnabled)
            return false;
        if (IsLockedInBoat())
            return false;
        var inv = interactor.GetComponent<PlayerInventory>();
        return inv != null && inv.HasFreeSlot();
    }

    public void Interact(GameObject interactor)
    {
        if (IsLockedInBoat())
            return;

        var inv = interactor.GetComponent<PlayerInventory>();
        if (inv == null)
            return;

        CarrySameObject(inv);
    }

    bool CarrySameObject(PlayerInventory inv)
    {
        BoatMaterialItem.BeginCarry(kind, pieceSize, KindName(kind), transform.rotation);
        var item = gameObject.GetComponent<BoatMaterialItem>();
        if (item == null)
            item = gameObject.AddComponent<BoatMaterialItem>();
        enabled = false;
        if (inv.PickupExisting(item))
        {
            Destroy(this);
            return true;
        }
        enabled = true;
        Destroy(item);
        return false;
    }

    public void Configure(BoatPieceKind k, Vector3 size)
    {
        kind = k;
        pieceSize = size;
        _colCenter = Vector3.zero;
        ApplyVisual();
        ApplyPhysics();
    }

    public static void PrepareSpawn(BoatPieceKind k, Vector3 size)
    {
        _spawnPending = true;
        _spawnKind = k;
        _spawnSize = size;
    }

    void Awake()
    {
        if (_spawnPending)
        {
            kind = _spawnKind;
            pieceSize = _spawnSize;
            _colCenter = Vector3.zero;
            _spawnPending = false;
        }
        _rb = GetComponent<Rigidbody>();
        ApplyVisual();
        ApplyPhysics();
    }

    void FixedUpdate()
    {
        if (_rb == null || _rb.isKinematic)
            return;
        if (!BoatWater.TryHeight(transform.position, out float waterY))
            return;

        Bounds b = _box != null ? _box.bounds : new Bounds(transform.position, pieceSize);
        float sub = waterY - b.min.y;
        if (sub <= 0f)
            return;
        float depth = Mathf.Clamp(sub / Mathf.Max(0.05f, b.size.y), 0f, 1.4f);
        float force = BoatVisuals.Buoyancy(kind) * depth;
        _rb.AddForce(Vector3.up * force, ForceMode.Acceleration);
        _rb.AddForce(-_rb.linearVelocity * (1.8f * depth), ForceMode.Acceleration);
        Vector3 av = _rb.angularVelocity;
        av *= 1f - 0.35f * depth * Time.fixedDeltaTime * 8f;
        _rb.angularVelocity = av;
    }

    public void RegisterNail(BoatNail nail)
    {
        if (nail != null && !_nails.Contains(nail))
            _nails.Add(nail);
    }

    public void UnregisterNail(BoatNail nail)
    {
        if (nail != null)
            _nails.Remove(nail);
    }

    public bool HasDrivenNail()
    {
        for (int i = 0; i < _nails.Count; i++)
            if (_nails[i] != null && _nails[i].Driven)
                return true;
        return false;
    }

    public bool IsLockedInBoat() => HasDrivenNail() || BoatRope.IsTied(this);

    public void CollectIsland(List<BoatPiece> into)
    {
        IslandQueue.Clear();
        IslandSeen.Clear();
        IslandQueue.Add(this);
        IslandSeen.Add(this);
        into.Clear();
        while (IslandQueue.Count > 0)
        {
            BoatPiece p = IslandQueue[IslandQueue.Count - 1];
            IslandQueue.RemoveAt(IslandQueue.Count - 1);
            into.Add(p);
            for (int i = 0; i < p._nails.Count; i++)
            {
                BoatNail n = p._nails[i];
                if (n == null || !n.Driven)
                    continue;
                TryAddIsland(n.A);
                TryAddIsland(n.B);
            }
            BoatRope.ForEachLinked(p.transform, TryAddIsland);
        }

        void TryAddIsland(BoatPiece x)
        {
            if (x == null || !IslandSeen.Add(x))
                return;
            IslandQueue.Add(x);
        }
    }

    public Rigidbody IslandRootBody()
    {
        CollectIsland(IslandTmp);
        Rigidbody best = _rb;
        float bestMass = _rb != null ? _rb.mass : 0f;
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var body = IslandTmp[i].Body;
            if (body != null && body.mass > bestMass)
            {
                best = body;
                bestMass = body.mass;
            }
        }
        return best;
    }

    /// <summary>Точка реза на длинной оси — там, куда смотришь, без подтягивания к центру.</summary>
    public bool TryGetCut(Vector3 worldPoint, out Vector3 cutWorld)
    {
        cutWorld = default;
        if (!CutRange(out int axis, out float min, out float max, out float center))
            return false;
        float cut = transform.InverseTransformPoint(worldPoint)[axis];
        const float minRemain = 0.16f;
        if (cut < min + minRemain || cut > max - minRemain)
            return false;
        Vector3 local = Vector3.zero;
        if (_box != null)
            local = _box.center;
        local[axis] = cut;
        cutWorld = transform.TransformPoint(local);
        return true;
    }

    bool CutRange(out int axis, out float min, out float max, out float center)
    {
        axis = BoatVisuals.LengthAxis(kind);
        Vector3 size = pieceSize;
        center = 0f;
        if (_box != null)
        {
            size = _box.size;
            center = _box.center[axis];
        }
        float half = size[axis] * 0.5f;
        min = center - half;
        max = center + half;
        return size[axis] >= 0.36f;
    }

    /// <summary>Поперечный распил ровно в точке прицела.</summary>
    public bool TrySaw(Vector3 worldPoint)
    {
        if (!CutRange(out int axis, out float min, out float max, out _))
            return false;

        float cut = transform.InverseTransformPoint(worldPoint)[axis];
        const float minRemain = 0.16f;
        const float kerf = 0.05f;
        if (cut < min + minRemain || cut > max - minRemain)
            return false;

        float left = cut - min - kerf * 0.5f;
        float right = max - cut - kerf * 0.5f;
        if (left < minRemain || right < minRemain)
            return false;

        Vector3 size = _box != null ? _box.size : pieceSize;
        Vector3 leftSize = size;
        Vector3 rightSize = size;
        leftSize[axis] = left;
        rightSize[axis] = right;

        Vector3 origin = _box != null ? _box.center : _colCenter;
        Vector3 leftLocal = origin;
        Vector3 rightLocal = origin;
        leftLocal[axis] = min + left * 0.5f;
        rightLocal[axis] = max - right * 0.5f;

        bool locked = IsLockedInBoat();
        bool keepLeft = ChooseKeepLeft(axis, cut, left, right, locked);

        Vector3 keepSize = keepLeft ? leftSize : rightSize;
        Vector3 offSize = keepLeft ? rightSize : leftSize;
        Vector3 keepLocal = keepLeft ? leftLocal : rightLocal;
        Vector3 offLocal = keepLeft ? rightLocal : leftLocal;
        Vector3 offWorld = transform.TransformPoint(offLocal);
        Quaternion rot = transform.rotation;
        Vector3 vel = _rb != null ? _rb.linearVelocity : Vector3.zero;

        pieceSize = keepSize;
        _colCenter = locked ? keepLocal : Vector3.zero;
        ApplyPhysics();
        ApplyVisual();

        if (!locked)
        {
            transform.position = transform.TransformPoint(keepLocal);
            _colCenter = Vector3.zero;
            ApplyPhysics();
            ApplyVisual();
        }

        if (_rb != null)
        {
            _rb.linearVelocity = locked ? vel : vel * 0.85f;
            _rb.angularVelocity = Vector3.zero;
        }

        Physics.SyncTransforms();
        var other = SpawnTwin(offWorld, rot, offSize);
        RelocateFasteners(other, axis, cut, keepLeft);

        Vector3 along = transform.TransformDirection(AxisUnit(axis));
        float away = keepLeft ? 1f : -1f;
        if (other.Body != null)
        {
            other.Body.maxDepenetrationVelocity = 0.4f;
            other.Body.linearVelocity = vel * 0.25f + along * away * 0.45f;
            other.Body.angularVelocity = Vector3.zero;
        }

        IgnoreCollidersBrief(_box, other._box, 0.55f);
        return true;
    }

    bool ChooseKeepLeft(int axis, float cut, float leftLen, float rightLen, bool locked)
    {
        if (!locked)
            return leftLen >= rightLen;

        int leftScore = 0;
        int rightScore = 0;
        for (int i = 0; i < _nails.Count; i++)
        {
            var n = _nails[i];
            if (n == null || !n.Driven)
                continue;
            float x = transform.InverseTransformPoint(n.transform.position)[axis];
            if (x < cut)
                leftScore++;
            else
                rightScore++;
        }
        ScoreRopes(axis, cut, ref leftScore, ref rightScore);
        if (leftScore == 0 && rightScore == 0)
            return leftLen >= rightLen;
        return leftScore >= rightScore;
    }

    void ScoreRopes(int axis, float cut, ref int leftScore, ref int rightScore)
    {
        BoatRope.ScoreCut(this, axis, cut, ref leftScore, ref rightScore);
    }

    void RelocateFasteners(BoatPiece offcut, int axis, float cut, bool keepLeft)
    {
        if (offcut == null)
            return;

        for (int i = _nails.Count - 1; i >= 0; i--)
        {
            var n = _nails[i];
            if (n == null)
            {
                _nails.RemoveAt(i);
                continue;
            }
            float x = transform.InverseTransformPoint(n.transform.position)[axis];
            bool onKeep = keepLeft ? x < cut : x > cut;
            if (onKeep)
                continue;

            n.transform.SetParent(offcut.transform, true);
            UnregisterNail(n);
            offcut.RegisterNail(n);
            BoatPiece other = n.A == this ? n.B : n.A;
            if (n.A == this)
                n.Reconnect(offcut, n.B);
            else
                n.Reconnect(n.A, offcut);
            if (other != null)
                other.RegisterNail(n);
        }

        BoatRope.SplitForCut(this, offcut, axis, cut, keepLeft);
    }

    static Vector3 AxisUnit(int axis)
    {
        var v = Vector3.zero;
        v[axis] = 1f;
        return v;
    }

    void IgnoreCollidersBrief(Collider a, Collider b, float seconds)
    {
        if (a == null || b == null)
            return;
        StartCoroutine(IgnoreRoutine(a, b, seconds));
    }

    static IEnumerator IgnoreRoutine(Collider a, Collider b, float seconds)
    {
        Physics.IgnoreCollision(a, b, true);
        yield return new WaitForSeconds(seconds);
        if (a != null && b != null)
            Physics.IgnoreCollision(a, b, false);
    }

    BoatPiece SpawnTwin(Vector3 pos, Quaternion rot, Vector3 size)
    {
        _spawnPending = true;
        _spawnKind = kind;
        _spawnSize = size;
        var go = new GameObject(kind + "Piece");
        go.transform.SetPositionAndRotation(pos, rot);
        return go.AddComponent<BoatPiece>();
    }

    public static bool TryFindPair(Vector3 point, float radius, out BoatPiece a, out BoatPiece b)
    {
        a = null;
        b = null;
        int n = Physics.OverlapSphereNonAlloc(point, radius, OverlapScratch, ~0, QueryTriggerInteraction.Ignore);
        BoatPiece first = null;
        for (int i = 0; i < n; i++)
        {
            var p = OverlapScratch[i] != null ? OverlapScratch[i].GetComponentInParent<BoatPiece>() : null;
            if (p == null)
                continue;
            if (first == null)
                first = p;
            else if (p != first)
            {
                a = first;
                b = p;
                return true;
            }
        }
        return false;
    }

    public static BoatPiece Ray(Ray ray, float dist, out RaycastHit hit)
    {
        if (!Physics.Raycast(ray, out hit, dist, ~0, QueryTriggerInteraction.Ignore))
            return null;
        return hit.collider.GetComponentInParent<BoatPiece>();
    }

    void ApplyVisual()
    {
        var vis = BoatVisuals.Attach(
            transform,
            BoatVisuals.Shape(kind),
            BoatVisuals.VisualScale(kind, pieceSize),
            BoatVisuals.VisualRotation(kind),
            BoatVisuals.MaterialFor(kind));
        vis.transform.localPosition = _colCenter;
    }

    void ApplyPhysics()
    {
        _box = GetComponent<BoxCollider>();
        if (_box == null)
            _box = gameObject.AddComponent<BoxCollider>();
        _box.size = pieceSize;
        _box.center = _colCenter;
        _box.isTrigger = false;
        _box.enabled = true;

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();
        _rb.mass = Mathf.Max(0.4f, BoatVisuals.Mass(kind) * (pieceSize.x * pieceSize.y * pieceSize.z) /
            (BoatVisuals.DefaultSize(kind).x * BoatVisuals.DefaultSize(kind).y * BoatVisuals.DefaultSize(kind).z));
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.linearDamping = 0.35f;
        _rb.angularDamping = 0.55f;
        _rb.maxDepenetrationVelocity = 1.2f;
        _rb.detectCollisions = true;
        _rb.isKinematic = false;
        _rb.useGravity = true;
    }

    public static string KindName(BoatPieceKind k)
    {
        switch (k)
        {
            case BoatPieceKind.Log: return "Log";
            case BoatPieceKind.Barrel: return "Barrel";
            default: return "Plank";
        }
    }
}
