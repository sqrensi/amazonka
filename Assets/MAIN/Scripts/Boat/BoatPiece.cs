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
    static readonly Collider[] OverlapScratch = new Collider[48];

    public BoatPieceKind Kind => kind;
    public Rigidbody Body => _rb;
    public Vector3 PieceSize => pieceSize;
    public Vector3 ColliderCenter => _box != null ? _box.center : _colCenter;
    public IReadOnlyList<BoatNail> Nails => _nails;

    public string GetPrompt()
    {
        if (!CanBeCarried())
            return "";
        CollectIsland(IslandTmp);
        if (IslandTmp.Count > 1)
            return IslandTmp.Count > 2 ? "Pick up boat" : "Pick up assembly";
        return $"Pick up {KindName(kind)}";
    }

    public string GetInteractKey() => "F";

    public Transform GetAnchor()
    {
        CollectIsland(IslandTmp);
        if (IslandTmp.Count <= 1)
            return transform;
        BoatPiece lead = IslandLeaderFrom(IslandTmp);
        return lead.PromptAnchor(IslandTmp);
    }

    public BoatPiece IslandLeader()
    {
        CollectIsland(IslandTmp);
        return IslandLeaderFrom(IslandTmp);
    }

    static BoatPiece IslandLeaderFrom(List<BoatPiece> island)
    {
        BoatPiece lead = island[0];
        int best = lead.GetInstanceID();
        for (int i = 1; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            int id = p.GetInstanceID();
            if (id < best)
            {
                best = id;
                lead = p;
            }
        }
        return lead;
    }

    Transform _promptAnchor;

    Transform PromptAnchor(List<BoatPiece> island)
    {
        if (_promptAnchor == null)
        {
            var go = new GameObject("IslandPrompt");
            _promptAnchor = go.transform;
            _promptAnchor.SetParent(transform, false);
        }

        bool any = false;
        Bounds b = default;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            var box = p.GetComponent<BoxCollider>();
            Bounds pb = box != null ? box.bounds : new Bounds(p.transform.position, p.PieceSize);
            if (!any)
            {
                b = pb;
                any = true;
            }
            else
                b.Encapsulate(pb);
        }
        _promptAnchor.position = any ? b.center : transform.position;
        return _promptAnchor;
    }

    public bool CanInteract(GameObject interactor)
    {
        if (interactor == null || !isActiveAndEnabled)
            return false;
        if (!CanBeCarried())
            return false;
        var inv = interactor.GetComponent<PlayerInventory>();
        return inv != null && inv.HasFreeSlot();
    }

    public void Interact(GameObject interactor)
    {
        var inv = interactor.GetComponent<PlayerInventory>();
        if (inv == null || !CanBeCarried())
            return;

        CollectIsland(IslandTmp);
        if (IslandTmp.Count > 1)
        {
            BoatClusterItem.TryPickup(IslandTmp, inv);
            return;
        }
        CarrySameObject(inv);
    }

    bool CanBeCarried()
    {
        if (GetComponentInParent<BoatClusterItem>() != null)
            return false;
        var held = GetComponentInParent<HeldItem>();
        return held == null || !held.IsCarried;
    }

    bool CarrySameObject(PlayerInventory inv)
    {
        DetachLooseNails();
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
        {
            _rb.angularDamping = 0.7f;
            return;
        }

        Vector3 size = _box != null ? _box.size : pieceSize;
        Vector3 colCenter = _box != null ? _box.center : _colCenter;
        float hx = Mathf.Max(0.04f, size.x * 0.45f);
        float hy = Mathf.Max(0.03f, size.y * 0.5f);
        float hz = Mathf.Max(0.04f, size.z * 0.45f);
        float[] xs = { -hx * 0.55f, hx * 0.55f };
        float[] zs = { -hz * 0.4f, hz * 0.4f };
        float buoyancy = BoatVisuals.Buoyancy(kind);
        float submerged = 0f;
        int wet = 0;
        const int sampleCount = 4;
        Vector3 liftSum = Vector3.zero;
        for (int ix = 0; ix < xs.Length; ix++)
        {
            for (int iz = 0; iz < zs.Length; iz++)
            {
                Vector3 local = colCenter + new Vector3(xs[ix], 0f, zs[iz]);
                Vector3 world = transform.TransformPoint(local);
                float depth = waterY - (world.y - hy);
                if (depth <= 0f)
                    continue;
                float d = Mathf.Clamp01(depth / Mathf.Max(0.08f, size.y));
                submerged += d;
                wet++;
                liftSum += Vector3.up * (_rb.mass * buoyancy * d / sampleCount);
            }
        }
        if (wet <= 0)
            return;
        float frac = submerged / sampleCount;
        if (frac <= 0.001f)
            return;

        _rb.AddForce(liftSum, ForceMode.Force);

        Vector3 vel = _rb.linearVelocity;
        Vector3 drag = vel;
        drag.y *= 0.45f;
        _rb.AddForce(-drag * (2.2f * frac * _rb.mass), ForceMode.Force);
        _rb.AddTorque(-_rb.angularVelocity * (5.5f * frac), ForceMode.Acceleration);
        Vector3 straighten = Vector3.Cross(transform.up, Vector3.up);
        _rb.AddTorque(straighten * (6f * frac), ForceMode.Acceleration);
        _rb.angularDamping = Mathf.Lerp(0.7f, 3.2f, Mathf.Clamp01(frac));
        if (vel.y > 2.2f)
            _rb.AddForce(Vector3.down * ((vel.y - 2.2f) * _rb.mass * 3f), ForceMode.Force);
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

    public void SleepForCarry()
    {
        if (_rb != null)
        {
            BoatBuildUtil.StopMotion(_rb);
            _rb.isKinematic = true;
            _rb.detectCollisions = false;
        }
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null)
                cols[i].enabled = false;
        for (int i = 0; i < _nails.Count; i++)
            _nails[i]?.DisconnectJointKeepState();
        enabled = false;
    }

    public void WakeInWorld(bool physicsLive = true)
    {
        enabled = true;
        var rs = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null)
                continue;
            var nail = rs[i].GetComponentInParent<BoatNail>();
            if (nail != null && nail.Driven)
                continue;
            rs[i].enabled = true;
        }
        ApplyPhysics();
        if (!physicsLive && _rb != null)
        {
            BoatBuildUtil.StopMotion(_rb);
            _rb.isKinematic = true;
            _rb.detectCollisions = false;
        }
        var nails = GetComponentsInChildren<BoatNail>(true);
        for (int i = 0; i < nails.Length; i++)
        {
            if (nails[i] == null || nails[i].Driven)
                continue;
            var cols = nails[i].GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
                if (cols[c] != null)
                    cols[c].enabled = true;
        }
    }

    public void ResumePhysics(Vector3 velocity)
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            return;
        _rb.isKinematic = false;
        _rb.detectCollisions = true;
        _rb.useGravity = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.maxDepenetrationVelocity = IsLockedInBoat() ? 1.2f : 6f;
        BoatBuildUtil.SetMotion(_rb, velocity, Vector3.zero);
    }

    void DetachLooseNails()
    {
        for (int i = _nails.Count - 1; i >= 0; i--)
        {
            var n = _nails[i];
            if (n == null)
            {
                _nails.RemoveAt(i);
                continue;
            }
            if (n.Driven)
                continue;
            if (n.A != null)
                n.A.UnregisterNail(n);
            if (n.B != null)
                n.B.UnregisterNail(n);
            n.A = null;
            n.B = null;
            n.transform.SetParent(null, true);
        }
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
            BoatBuildUtil.SetMotion(_rb, locked ? vel : vel * 0.85f, Vector3.zero);

        Physics.SyncTransforms();
        var other = SpawnTwin(offWorld, rot, offSize);
        RelocateFasteners(other, axis, cut, keepLeft);

        Vector3 along = transform.TransformDirection(AxisUnit(axis));
        float away = keepLeft ? 1f : -1f;
        if (other.Body != null)
        {
            other.Body.maxDepenetrationVelocity = 0.4f;
            BoatBuildUtil.SetMotion(other.Body, vel * 0.25f + along * away * 0.45f, Vector3.zero);
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
        BoatMaterialItem.PromoteAround(point, radius + 0.2f);
        int n = Physics.OverlapSphereNonAlloc(point, radius, OverlapScratch, ~0, QueryTriggerInteraction.Ignore);
        BoatPiece best1 = null;
        BoatPiece best2 = null;
        float d1 = float.PositiveInfinity;
        float d2 = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var col = OverlapScratch[i];
            var p = col != null ? col.GetComponentInParent<BoatPiece>() : null;
            if (p == null || !p.isActiveAndEnabled || p == best1)
                continue;
            if (p.GetComponentInParent<BoatClusterItem>() != null)
                continue;
            Vector3 closest = col.ClosestPoint(point);
            float d = (closest - point).sqrMagnitude;
            if (d < d1)
            {
                d2 = d1;
                best2 = best1;
                d1 = d;
                best1 = p;
            }
            else if (p != best1 && d < d2)
            {
                d2 = d;
                best2 = p;
            }
        }
        if (best1 == null || best2 == null)
            return false;
        a = best1;
        b = best2;
        return true;
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
        if (kind == BoatPieceKind.Plank)
        {
            Vector3 s = _box.size;
            s.y = Mathf.Max(s.y, 0.07f);
            _box.size = s;
        }
        else if (kind == BoatPieceKind.Log)
        {
            Vector3 s = _box.size;
            s.x = Mathf.Max(s.x, 0.4f);
            s.y = Mathf.Max(s.y, 0.4f);
            _box.size = s;
        }
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
        _rb.linearDamping = 0.45f;
        _rb.angularDamping = 0.7f;
        _rb.maxDepenetrationVelocity = 1.8f;
        _rb.detectCollisions = true;
        _rb.isKinematic = false;
        _rb.useGravity = true;
        if (_box.sharedMaterial == null)
            _box.sharedMaterial = WoodPhysMat();
        BoatBuildUtil.EnsureCollideWithActors(_box);
    }

    static PhysicsMaterial _woodPhys;
    static PhysicsMaterial WoodPhysMat()
    {
        if (_woodPhys != null)
            return _woodPhys;
        _woodPhys = new PhysicsMaterial("BoatWood")
        {
            dynamicFriction = 0.72f,
            staticFriction = 0.85f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Maximum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        return _woodPhys;
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
