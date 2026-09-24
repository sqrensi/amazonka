using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Деталь лодки в мире: доска, бревно или бочка. Физика, гвозди, распил, плавучесть, посадка.
/// </summary>
public class BoatPiece : MonoBehaviour, IInteractable
{
    [SerializeField] BoatPieceKind kind = BoatPieceKind.Plank;
    [SerializeField] Vector3 pieceSize = new Vector3(0.22f, 0.045f, 1.15f);

    Rigidbody _rb;
    BoxCollider _box;
    Vector3 _colCenter;
    BoatPiece _oarLatchHull;
    bool _weldSlave;
    readonly List<BoatNail> _nails = new List<BoatNail>();
    static bool _spawnPending;
    static BoatPieceKind _spawnKind;
    static Vector3 _spawnSize;
    static bool _spawnBark;
    static float _spawnUv0;
    static float _spawnUv1;
    static float _spawnBarkAcross;
    static float _spawnBarkAlong;
    bool _barkMapped;
    float _uv0 = 0f;
    float _uv1 = 1f;
    float _barkAcross = 1.4f;
    float _barkAlong = 4f;
    static readonly List<BoatPiece> IslandQueue = new List<BoatPiece>(32);
    static readonly List<BoatPiece> IslandTmp = new List<BoatPiece>(32);
    static readonly HashSet<BoatPiece> IslandSeen = new HashSet<BoatPiece>();
    static readonly Collider[] OverlapScratch = new Collider[48];
    Renderer[] _lookRends;
    float _wetShown = -1f;

    public float Strain { get; internal set; }
    public float HullLift { get; internal set; } = 1f;
    public float HullSink { get; internal set; }
    public float HullStrength { get; internal set; } = 1f;
    public float HullFlood { get; internal set; }
    public bool HullIsCraft { get; internal set; }
    public bool HullCracked { get; internal set; }
    public bool ThrownByPlayer;

    public BoatPieceKind Kind => kind;
    public Rigidbody Body => _rb;
    public Vector3 PieceSize => pieceSize;
    public Vector3 ColliderCenter => _box != null ? _box.center : _colCenter;
    public Renderer[] LookRenderers
    {
        get
        {
            if (_lookRends == null || _lookRends.Length == 0)
                _lookRends = GetComponentsInChildren<Renderer>(false);
            return _lookRends;
        }
    }
    public float WetShown
    {
        get => _wetShown;
        set => _wetShown = value;
    }
    public IReadOnlyList<BoatNail> Nails => _nails;

    public string GetPrompt()
    {
        if (BoatOarStation.Active != null)
            return "";
        if (kind == BoatPieceKind.Oar && !OarIsMounted())
            return CanBeCarried() ? $"Pick up {KindName(kind)}" : "";
        if (CanRow() || IslandRowOar() != null)
            return "Row";
        if (IsAssembled())
        {
            if (!WaterSimLive() || IslandAfloat() || IslandSliding())
                return "";
            return "Push boat";
        }
        if (!CanBeCarried())
            return "";
        return $"Pick up {KindName(kind)}";
    }

    public string GetInteractKey() =>
        (kind != BoatPieceKind.Oar || OarIsMounted()) && (CanRow() || IslandRowOar() != null) ? "R" : "F";

    public Transform GetAnchor()
    {
        if (kind == BoatPieceKind.Oar && !OarIsMounted())
            return BoatVisuals.EnsurePromptAnchor(transform, new Vector3(0f, 0.05f, 1.02f));
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

    public static BoatPiece IslandLeaderFrom(List<BoatPiece> island)
    {
        BoatPiece lead = null;
        int best = int.MaxValue;
        BoatPiece oar = null;
        int oarBest = int.MaxValue;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            int id = p.GetInstanceID();
            if (p.kind == BoatPieceKind.Oar)
            {
                if (id < oarBest)
                {
                    oarBest = id;
                    oar = p;
                }
                continue;
            }
            if (id < best)
            {
                best = id;
                lead = p;
            }
        }
        return lead != null ? lead : (oar != null ? oar : island[0]);
    }

    public Vector3 CraftForward()
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.Cross(Vector3.up, transform.right);
        fwd.y = 0f;
        return fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
    }

    Transform _promptAnchor;
    int _promptCount = -1;

    Transform PromptAnchor(List<BoatPiece> island)
    {
        if (_promptAnchor == null)
        {
            var go = new GameObject("IslandPrompt");
            _promptAnchor = go.transform;
            _promptAnchor.SetParent(transform, false);
        }

        int n = 0;
        Vector3 acc = Vector3.zero;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            acc += transform.InverseTransformPoint(p.transform.position);
            n++;
        }
        Vector3 local = n > 0 ? acc / n : Vector3.zero;
        local.y += 0.22f;
        if (n != _promptCount || (_promptAnchor.localPosition - local).sqrMagnitude > 0.25f)
        {
            _promptCount = n;
            _promptAnchor.localPosition = local;
        }
        return _promptAnchor;
    }

    public bool CanInteract(GameObject interactor)
    {
        if (BoatOarStation.Active != null)
            return false;
        if (interactor == null || !isActiveAndEnabled)
            return false;
        if (kind == BoatPieceKind.Oar && !OarIsMounted())
        {
            var bag = interactor.GetComponent<PlayerInventory>();
            return bag != null && bag.HasFreeSlot() && CanBeCarried();
        }
        if (CanRow(interactor) || IslandRowOar(interactor) != null)
            return true;
        if (IsAssembled())
            return CanPush(interactor);
        if (!CanBeCarried())
            return false;
        var inv = interactor.GetComponent<PlayerInventory>();
        return inv != null && inv.HasFreeSlot();
    }

    public void Interact(GameObject interactor)
    {
        if (kind == BoatPieceKind.Oar && !OarIsMounted())
        {
            var bag = interactor.GetComponent<PlayerInventory>();
            if (bag != null && CanBeCarried())
                CarrySameObject(bag);
            return;
        }
        if (CanRow(interactor) || IslandRowOar(interactor) != null)
            return;
        if (IsAssembled())
        {
            TryPush(interactor);
            return;
        }
        var inv = interactor.GetComponent<PlayerInventory>();
        if (inv == null || !CanBeCarried())
            return;
        CarrySameObject(inv);
    }

    public bool TryRow(GameObject player)
    {
        if (kind == BoatPieceKind.Oar)
        {
            if (!CanRow(player) && !BoatOarStation.IsUsing(this))
                return false;
            BoatOarStation.Toggle(player, this);
            return true;
        }
        CollectIsland(IslandTmp);
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var oar = IslandTmp[i];
            if (oar == null || oar == this || oar.kind != BoatPieceKind.Oar)
                continue;
            if (oar.TryRow(player))
                return true;
        }
        return false;
    }

    public bool CanRow() => CanRow(null);

    public bool CanRow(GameObject actor)
    {
        if (kind != BoatPieceKind.Oar || !OarIsMounted())
            return false;
        if (BoatOarStation.IsUsing(this))
            return true;
        if (!IslandAfloat())
            return false;
        if (actor == null)
        {
            var move = Object.FindFirstObjectByType<HorrorFirstPersonController>();
            actor = move != null ? move.gameObject : null;
        }
        return PlayerOnIsland(actor);
    }

    public BoatPiece IslandRowOar(GameObject actor = null)
    {
        if (CanRow(actor))
            return this;
        CollectIsland(IslandTmp);
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var oar = IslandTmp[i];
            if (oar != null && oar != this && oar.CanRow(actor))
                return oar;
        }
        return null;
    }

    bool PlayerOnIsland(GameObject actor)
    {
        if (actor == null)
            return false;
        var move = actor.GetComponent<HorrorFirstPersonController>();
        return move != null && move.RidesIsland(this);
    }

    bool IslandSliding()
    {
        Rigidbody rb = IslandRootBody();
        if (rb == null)
            return false;
        Vector3 v = rb.linearVelocity;
        v.y = 0f;
        return v.sqrMagnitude > 0.16f;
    }

    public bool IsAfloat() => IslandAfloat();

    static float LaunchSettleUntil;

    public static void BeginLaunchSettle(float seconds)
    {
        LaunchSettleUntil = Time.time + Mathf.Max(0.15f, seconds);
    }

    public static bool LaunchSettling => Time.time < LaunchSettleUntil;

    static bool RaceWaterLive()
    {
        var race = BoatRaceMode.Current;
        if (race == null)
            return true;
        var p = race.CurrentPhase;
        return p == BoatRaceMode.Phase.Race || p == BoatRaceMode.Phase.Results;
    }

    float _rideWetUntil;

    public bool CanRide()
    {
        if (!RaceWaterLive())
        {
            _rideWetUntil = 0f;
            return false;
        }
        if (IslandBeached())
        {
            _rideWetUntil = 0f;
            return false;
        }
        if (DeckSubmerged())
        {
            _rideWetUntil = 0f;
            return false;
        }
        if (IslandAfloat())
        {
            _rideWetUntil = Time.time + 0.35f;
            return true;
        }
        return Time.time < _rideWetUntil;
    }

    public bool DeckSubmerged()
    {
        return DeckSubmerged(0.18f);
    }

    public bool DeckSubmerged(float underMeters)
    {
        CollectIsland(IslandTmp);
        bool anyHull = false;
        float top = float.NegativeInfinity;
        Vector3 sample = transform.position;
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var p = IslandTmp[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            anyHull = true;
            sample = p.transform.position;
            if (p.TrySolidBounds(out Bounds b))
                top = Mathf.Max(top, b.max.y);
            else
                top = Mathf.Max(top, p.transform.position.y);
        }
        if (!anyHull)
            return true;
        if (!BoatWater.TryHeight(sample, out float waterY))
            return false;
        return top < waterY - Mathf.Max(0.05f, underMeters);
    }

    bool IslandAfloat()
    {
        CollectIsland(IslandTmp);
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var p = IslandTmp[i];
            if (p == null)
                continue;
            Vector3 pos = p.transform.position;
            if (!BoatWater.TryHeight(pos, out float waterY))
                continue;
            if (pos.y < waterY + 0.5f && pos.y > waterY - 3.5f)
                return true;
        }
        return false;
    }

    bool IslandBeached()
    {
        CollectIsland(IslandTmp);
        int hull = 0;
        int grounded = 0;
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var p = IslandTmp[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            hull++;
            Vector3 origin = p.transform.position + Vector3.up * 0.28f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 1.35f, ~0, QueryTriggerInteraction.Ignore);
            bool land = false;
            for (int h = 0; h < hits.Length; h++)
            {
                var hit = hits[h];
                if (hit.collider == null)
                    continue;
                if (hit.collider.GetComponentInParent<BoatWater>() != null)
                    continue;
                var other = hit.collider.GetComponentInParent<BoatPiece>();
                if (other != null)
                {
                    bool self = false;
                    for (int k = 0; k < IslandTmp.Count; k++)
                    {
                        if (IslandTmp[k] == other)
                        {
                            self = true;
                            break;
                        }
                    }
                    if (self)
                        continue;
                }
                if (hit.normal.y < 0.4f)
                    continue;
                if (!BoatWater.TryHeight(origin, out float waterY) || hit.point.y > waterY - 0.12f)
                {
                    land = true;
                    break;
                }
            }
            if (land)
                grounded++;
        }
        return hull > 0 && grounded >= Mathf.Max(1, (hull + 1) / 2);
    }

    public bool SharesIslandWith(BoatPiece other)
    {
        if (other == null)
            return false;
        if (other == this)
            return true;
        CollectIsland(IslandTmp);
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            if (IslandTmp[i] == other)
                return true;
        }
        return false;
    }

    bool IsAssembled()
    {
        if (_weldSlave)
            return true;
        CollectIsland(IslandTmp);
        return IslandTmp.Count > 1;
    }

    bool CanPush(GameObject interactor)
    {
        if (interactor == null || !WaterSimLive() || !IsAssembled() || IslandAfloat())
            return false;
        return IslandClearance(interactor.transform.position) <= 1f;
    }

    float IslandClearance(Vector3 from)
    {
        CollectIsland(IslandTmp);
        float best = float.MaxValue;
        Vector3 probe = from + Vector3.up * 0.35f;
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var p = IslandTmp[i];
            if (p == null)
                continue;
            var cols = p.GetComponentsInChildren<Collider>();
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] == null || !cols[c].enabled || cols[c].isTrigger)
                    continue;
                float d = Vector3.Distance(probe, BoatBuildUtil.ClosestPoint(cols[c], probe));
                if (d < best)
                    best = d;
            }
        }
        return best;
    }

    void TryPush(GameObject interactor)
    {
        if (interactor == null || !CanPush(interactor))
            return;
        Vector3 dir = interactor.transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;
        dir.Normalize();

        CollectIsland(IslandTmp);
        BoatHull.GuardPush(2.2f);
        BoatBuildUtil.IgnoreActorsBriefly(IslandTmp, 1.6f);
        Rigidbody rb = IslandRootBody();
        if (rb == null)
            rb = Body;
        if (rb == null || rb.isKinematic)
            return;
        StartCoroutine(SmoothPush(rb, dir * 6f, 0.28f));
    }

    IEnumerator SmoothPush(Rigidbody rb, Vector3 target, float duration)
    {
        if (rb == null)
            yield break;
        RigidbodyConstraints saved = rb.constraints;
        rb.constraints = saved | RigidbodyConstraints.FreezeRotation;
        rb.angularVelocity = Vector3.zero;
        Vector3 from = rb.linearVelocity;
        from.y = 0f;
        target.y = 0f;
        float t = 0f;
        while (t < duration && rb != null)
        {
            t += Time.fixedDeltaTime;
            float k = t / duration;
            k = k * k * (3f - 2f * k);
            Vector3 v = Vector3.Lerp(from, target, k);
            v.y = Mathf.Min(rb.linearVelocity.y, 0.05f);
            rb.linearVelocity = v;
            rb.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }
        if (rb != null)
        {
            Vector3 v = target;
            v.y = Mathf.Min(rb.linearVelocity.y, 0.05f);
            rb.linearVelocity = v;
            rb.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.35f);
            if (rb != null)
                rb.constraints = saved;
        }
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
        _barkMapped = false;
        transform.localScale = Vector3.one;
        if (kind == BoatPieceKind.Oar)
            BoatVisuals.PlaceBox(kind, pieceSize, out _colCenter, out _);
        else
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
            if (kind == BoatPieceKind.Oar)
                BoatVisuals.PlaceBox(kind, pieceSize, out _colCenter, out _);
            else
                _colCenter = Vector3.zero;
            if (_spawnBark)
            {
                _barkMapped = true;
                _uv0 = _spawnUv0;
                _uv1 = _spawnUv1;
                _barkAcross = _spawnBarkAcross;
                _barkAlong = _spawnBarkAlong;
            }
            _spawnPending = false;
            _spawnBark = false;
        }
        _rb = GetComponent<Rigidbody>();
        ApplyVisual();
        ApplyPhysics();
        EnsureBoatPart();
        BindCraft(this, false);
    }

    void EnsureBoatPart()
    {
        var part = GetComponent<BoatPart>();
        if (part == null)
            part = gameObject.AddComponent<BoatPart>();
        part.Piece = this;
        if (part.Craft == null)
            part.Craft = this;
        Craft = part.Craft;
    }

    void Start()
    {
        if (HasDrivenNail() || BoatRope.IsTied(this))
            BoatIsland.Refresh(this);
    }

    public bool IsWeldSlave => _weldSlave;

    public static bool WaterSimLive() => RaceWaterLive();

    public BoatPiece Craft { get; private set; }

    public void RestOnShore()
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            return;
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.isKinematic = true;
        _rb.detectCollisions = true;
        _rb.useGravity = false;
    }

    public void WakeForWater()
    {
        if (_weldSlave)
            return;
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            return;
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.detectCollisions = true;
        if (LaunchSettling)
        {
            _rb.maxDepenetrationVelocity = 0.15f;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    public static void SyncCraft(List<BoatPiece> island, BoatPiece lead)
    {
        BoatLayers.Ensure();
        bool assembled = island != null && lead != null && island.Count > 1;
        if (island == null)
            return;
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] != null)
                island[i].BindCraft(assembled ? lead : island[i], assembled);
        }
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] != null && island[i] != lead)
                island[i].RemoveInteractVolume();
        }
        if (assembled)
            lead.FitInteractVolume(island);
        else if (lead != null)
            lead.FitOwnInteractVolume();
    }

    void BindCraft(BoatPiece lead, bool assembled)
    {
        Craft = lead != null ? lead : this;
        var part = GetComponent<BoatPart>();
        if (part == null)
            part = gameObject.AddComponent<BoatPart>();
        part.Piece = this;
        part.Craft = Craft;
        SetPhysicsLayer(BoatLayers.PhysicsLayer(assembled));
        StampSolidParts();
        if (!assembled)
            FitOwnInteractVolume();
    }

    void StampSolidParts()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null)
                continue;
            if (cols[i].gameObject.name == BoatLayers.InteractVolumeName)
                continue;
            BoatLayers.StampBoatPart(cols[i].gameObject, this, Craft);
        }
    }

    void SetPhysicsLayer(int layer)
    {
        BoatLayers.SetSolidLayer(gameObject, layer);
    }

    void FitInteractVolume(List<BoatPiece> island)
    {
        if (BoatLayers.Interact < 0)
            return;
        Transform t = transform.Find(BoatLayers.InteractVolumeName);
        BoxCollider box;
        if (t == null)
        {
            var go = new GameObject(BoatLayers.InteractVolumeName);
            t = go.transform;
            t.SetParent(transform, false);
            box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var part = go.AddComponent<BoatPart>();
            part.Piece = this;
            part.Craft = this;
        }
        else
        {
            box = t.GetComponent<BoxCollider>();
            if (box == null)
                box = t.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var part = t.GetComponent<BoatPart>();
            if (part == null)
                part = t.gameObject.AddComponent<BoatPart>();
            part.Piece = this;
            part.Craft = this;
            var link = t.GetComponent<InteractLink>();
            if (link != null)
                Destroy(link);
        }
        t.gameObject.layer = BoatLayers.Interact;
        t.gameObject.SetActive(true);
        Bounds local = default;
        bool any = false;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            if (!p.TrySolidBounds(out Bounds wb))
                continue;
            Vector3 min = wb.min;
            Vector3 max = wb.max;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? min.x : max.x,
                    (c & 2) == 0 ? min.y : max.y,
                    (c & 4) == 0 ? min.z : max.z);
                Vector3 lp = transform.InverseTransformPoint(corner);
                if (!any)
                {
                    local = new Bounds(lp, Vector3.zero);
                    any = true;
                }
                else
                    local.Encapsulate(lp);
            }
        }
        if (!any)
            return;
        if (kind == BoatPieceKind.Oar && island != null && island.Count <= 1)
        {
            BoatVisuals.OarGhostBounds(out Vector3 oc, out Vector3 os);
            box.center = oc;
            box.size = os;
            box.enabled = true;
            return;
        }
        box.center = local.center;
        box.size = local.size + Vector3.one * 0.08f;
        box.enabled = true;
    }

    static readonly System.Collections.Generic.List<BoatPiece> OwnIsland = new System.Collections.Generic.List<BoatPiece>(1);

    void FitOwnInteractVolume()
    {
        OwnIsland.Clear();
        OwnIsland.Add(this);
        FitInteractVolume(OwnIsland);
    }

    void RemoveInteractVolume()
    {
        Transform t = transform.Find(BoatLayers.InteractVolumeName);
        if (t != null)
            Destroy(t.gameObject);
    }

    void FixedUpdate()
    {
        if (_weldSlave)
            return;

        CollectIsland(IslandTmp);
        BoatPiece lead = IslandLeaderFrom(IslandTmp);
        if (lead != this && IslandTmp.Count > 1)
        {
            BoatIsland.Refresh(this);
            return;
        }

        if (!RaceWaterLive())
        {
            if (_rb != null && IslandTmp.Count > 1 && lead == this)
                RestOnShore();
            return;
        }

        if (_rb == null)
            return;
        if (_rb.isKinematic)
            WakeForWater();
        if (_rb.isKinematic)
            return;

        BoatHull.Tick(this, Time.fixedDeltaTime);
        if (IslandBeached())
        {
            _rb.angularDamping = 2.6f;
            Vector3 v = _rb.linearVelocity;
            v.x *= 0.86f;
            v.z *= 0.86f;
            if (v.y > 0f)
                v.y *= 0.35f;
            _rb.linearVelocity = v;
            _rb.angularVelocity *= 0.8f;
            return;
        }

        float fracSum = 0f;
        int n = 0;
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var p = IslandTmp[i];
            if (p == null)
                continue;
            Vector3 size = p._box != null ? p._box.size : p.pieceSize;
            Vector3 colCenter = p._box != null ? p._box.center : p._colCenter;
            float buoyancy = BoatVisuals.Buoyancy(p.kind) * Mathf.Max(0.08f, p.HullLift);
            if (LaunchSettling)
                buoyancy *= 0.22f;
            fracSum += BoatWater.ApplyBuoyancy(_rb, p.transform, colCenter, size, buoyancy, p.OwnMass(), false, IslandTmp.Count > 1 ? 0.12f : 1f);
            n++;
        }
        float frac = n > 0 ? fracSum / n : 0f;
        if (frac <= 0.001f)
            return;
        BoatWater.DampBuoyancy(_rb, transform, frac, _rb.mass, IslandTmp.Count > 1 ? 1f : BoatVisuals.CurrentScale(kind));
        if (LaunchSettling)
        {
            Vector3 v = _rb.linearVelocity;
            v.y = Mathf.Clamp(v.y, -0.4f, 0.28f);
            _rb.linearVelocity = v;
            _rb.angularVelocity *= 0.5f;
            _rb.maxDepenetrationVelocity = 0.18f;
        }
        if (IslandTmp.Count > 1)
            _rb.angularVelocity = Vector3.ClampMagnitude(_rb.angularVelocity, 0.42f);
        if (HullSink > 0.01f)
            _rb.AddForce(Vector3.down * (HullSink * _rb.mass * Mathf.Clamp01(frac + 0.15f)), ForceMode.Force);
        if (HullFlood > 0.2f)
        {
            Vector3 v = _rb.linearVelocity;
            float floor = Mathf.Lerp(-0.28f, -0.55f, Mathf.SmoothStep(0f, 1f, HullFlood));
            if (v.y < floor)
            {
                v.y = Mathf.Lerp(v.y, floor, SimTime.Blend(0.35f, Time.fixedDeltaTime));
                _rb.linearVelocity = v;
            }
        }
        ApplyIslandCurrent(frac);
    }

    void ApplyIslandCurrent(float wetFrac)
    {
        CollectIsland(IslandTmp);
        if (IslandLeaderFrom(IslandTmp) != this)
            return;
        Vector3 current = BoatWater.CurrentAt(transform.position);
        current.y = 0f;
        if (current.sqrMagnitude < 0.01f || wetFrac < 0.04f)
            return;
        float mass = 0f;
        Vector3 momentum = Vector3.zero;
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            Rigidbody body = IslandTmp[i] != null ? IslandTmp[i].Body : null;
            if (body == null || body.isKinematic)
                continue;
            mass += body.mass;
            momentum += body.linearVelocity * body.mass;
        }
        if (mass < 0.01f)
            return;
        Vector3 planar = momentum / mass;
        planar.y = 0f;
        Vector3 along = current.normalized;
        Vector3 haveAlong = along * Vector3.Dot(planar, along);
        Vector3 delta = current - haveAlong;
        delta.y = 0f;
        Vector3 accel = IslandTmp.Count > 1
            ? Vector3.ClampMagnitude(delta * 2.4f, 6.5f)
            : Vector3.ClampMagnitude(delta * BoatVisuals.CurrentScale(kind), BoatVisuals.CurrentCap(kind));
        if (accel.sqrMagnitude < 0.0001f)
            return;
        if (_rb != null && !_rb.isKinematic)
            _rb.AddForce(accel, ForceMode.Acceleration);
    }

    void LateUpdate()
    {
        BoatWetLook.Apply(this);
        if (kind == BoatPieceKind.Oar || _rb == null)
            return;
        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        float speed = v.magnitude;
        if (speed < 1.1f)
            return;
        Vector3 at = transform.position;
        if (BoatWater.TryHeight(at, out float y))
            at.y = y;
        BoatWaterFx.Wake(at, -CraftForward(), speed);
    }

    public float OwnMass()
    {
        Vector3 d = BoatVisuals.DefaultSize(kind);
        float vol = Mathf.Max(0.0001f, d.x * d.y * d.z);
        return Mathf.Max(0.4f, BoatVisuals.Mass(kind) * (pieceSize.x * pieceSize.y * pieceSize.z) / vol);
    }

    public void SetWeldMass(float mass)
    {
        if (_rb == null)
            return;
        _rb.mass = Mathf.Max(0.4f, mass);
        _rb.automaticCenterOfMass = true;
        _rb.automaticInertiaTensor = true;
        _rb.ResetInertiaTensor();
        _rb.angularDamping = 4.2f;
        _rb.linearDamping = 0.22f;
    }

    public void BreakNailJoints()
    {
        var joints = GetComponents<FixedJoint>();
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                Object.DestroyImmediate(joints[i]);
        }
    }

    public void MakeFreeBody()
    {
        _weldSlave = false;
        if (transform.parent != null)
            transform.SetParent(null, true);
        ApplyPhysics();
        if (kind == BoatPieceKind.Oar)
            SetOarSolids(true);
        BindCraft(this, false);
    }

    public void AttachAsWeldChild(BoatPiece lead)
    {
        if (lead == null || lead == this)
            return;
        BreakNailJoints();
        StripJoints();
        if (_rb != null)
            BoatRope.DropJointsOn(_rb);
        if (transform.parent != lead.transform)
            transform.SetParent(lead.transform, true);
        _weldSlave = true;
        if (_rb != null)
        {
            Object.DestroyImmediate(_rb);
            _rb = null;
        }
        if (kind == BoatPieceKind.Oar)
        {
            _oarLatchHull = lead;
            SetOarSolids(false);
        }
    }

    void StripJoints()
    {
        var joints = GetComponents<Joint>();
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                Object.DestroyImmediate(joints[i]);
        }
    }

    public void LatchOarTo(BoatPiece hull)
    {
        if (kind != BoatPieceKind.Oar || hull == null || hull == this)
            return;
        _oarLatchHull = hull;
        BoatIsland.Refresh(hull);
    }

    void EnsureOarJoint(BoatPiece hull)
    {
        if (hull == null || _rb == null || hull.Body == null)
            return;
        var joints = hull.GetComponents<FixedJoint>();
        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null && joints[i].connectedBody == _rb)
                return;
        }
        var own = GetComponents<FixedJoint>();
        for (int i = 0; i < own.Length; i++)
        {
            if (own[i] != null && own[i].connectedBody == hull.Body)
                return;
        }
        var j = hull.gameObject.AddComponent<FixedJoint>();
        j.connectedBody = _rb;
        j.breakForce = Mathf.Infinity;
        j.breakTorque = Mathf.Infinity;
        j.enableCollision = false;
        j.enablePreprocessing = true;
    }

    public void ReleaseOarLatch()
    {
        if (_oarLatchHull == null && transform.parent == null)
            return;
        SetOarIgnoreIsland(false);
        SetShaftCol(true);
        _oarLatchHull = null;
        if (_weldSlave || transform.parent != null)
            MakeFreeBody();
        else if (_rb != null && kind == BoatPieceKind.Oar)
        {
            _rb.isKinematic = false;
            _rb.detectCollisions = true;
        }
    }

    void SetOarSolids(bool solid)
    {
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null)
                continue;
            cols[i].enabled = true;
            cols[i].isTrigger = !solid;
        }
    }

    void SetShaftCol(bool on)
    {
        Transform t = transform.Find("ShaftCol");
        if (t == null)
            return;
        var col = t.GetComponent<Collider>();
        if (col != null)
            col.enabled = on;
    }

    void SetOarIgnoreIsland(bool ignore)
    {
        CollectIsland(IslandTmp);
        var oarCols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < IslandTmp.Count; i++)
        {
            var p = IslandTmp[i];
            if (p == null || p == this)
                continue;
            var cols = p.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] == null)
                    continue;
                for (int o = 0; o < oarCols.Length; o++)
                {
                    if (oarCols[o] == null)
                        continue;
                    Physics.IgnoreCollision(oarCols[o], cols[c], ignore);
                }
            }
        }
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
        if (kind == BoatPieceKind.Oar && !OarIsMounted())
            ReleaseOarLatch();
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
        var nested = GetComponentsInChildren<BoatNail>(true);
        for (int i = 0; i < nested.Length; i++)
        {
            var n = nested[i];
            if (n == null || n.Driven)
                continue;
            n.DropLoose();
        }
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
            n.DropLoose();
        }
    }

    public bool HoldsOar()
    {
        if (kind == BoatPieceKind.Oar)
            return true;
        for (int i = 0; i < _nails.Count; i++)
        {
            var n = _nails[i];
            if (n == null || !n.Driven)
                continue;
            var other = n.A == this ? n.B : n.A;
            if (other != null && other.Kind == BoatPieceKind.Oar)
                return true;
        }
        for (int c = 0; c < transform.childCount; c++)
        {
            var child = transform.GetChild(c).GetComponent<BoatPiece>();
            if (child != null && child.Kind == BoatPieceKind.Oar)
                return true;
        }
        return false;
    }

    public bool HasDrivenNail()
    {
        for (int i = 0; i < _nails.Count; i++)
            if (_nails[i] != null && _nails[i].Driven)
                return true;
        return false;
    }

    public bool OarIsMounted()
    {
        if (kind != BoatPieceKind.Oar)
            return false;
        for (int i = 0; i < _nails.Count; i++)
        {
            var n = _nails[i];
            if (n == null || !n.Driven)
                continue;
            var other = n.A == this ? n.B : n.A;
            if (other != null && other != this && other.kind != BoatPieceKind.Oar)
                return true;
        }
        return BoatRope.IsTied(this);
    }

    public bool IsLockedInBoat() => HasDrivenNail() || BoatRope.IsTied(this) || _weldSlave || (kind == BoatPieceKind.Oar && OarIsMounted());

    public bool TrySolidBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        if (_box != null && _box.enabled && !_box.isTrigger)
        {
            bounds = _box.bounds;
            any = true;
        }
        var cols = GetComponentsInChildren<Collider>(false);
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            if (col.GetComponentInParent<BoatPiece>() != this)
                continue;
            if (!any)
            {
                bounds = col.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(col.bounds);
        }
        return any;
    }

    static readonly List<BoatPiece> DeckScratch = new List<BoatPiece>(32);
    static readonly RaycastHit[] DeckProbe = new RaycastHit[16];

    public static bool BestDeckStand(BoatPiece lead, out Vector3 feet, out BoatPiece hull)
    {
        feet = default;
        hull = null;
        if (lead == null)
            return false;
        lead.CollectIsland(DeckScratch);
        BoatPiece best = null;
        float bestScore = float.NegativeInfinity;
        Bounds bestB = default;
        for (int i = 0; i < DeckScratch.Count; i++)
        {
            var p = DeckScratch[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            if (!p.TrySolidBounds(out Bounds b))
                continue;
            float area = b.size.x * b.size.z;
            if (area < 0.01f)
                continue;
            float score = area;
            if (p.Kind == BoatPieceKind.Plank)
                score *= 1.3f;
            else if (p.Kind == BoatPieceKind.Log)
                score *= 1.12f;
            score += b.max.y * 0.2f;
            float tall = b.size.y;
            float wide = Mathf.Max(b.size.x, b.size.z);
            if (tall > wide * 0.9f)
                score *= 0.5f;
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
                bestB = b;
            }
        }
        if (best == null)
            return false;

        Vector3 right = best.transform.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();
        Vector3 fwd = best.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.forward;
        else
            fwd.Normalize();
        float inset = Mathf.Clamp(Mathf.Min(bestB.size.x, bestB.size.z) * 0.22f, 0.05f, 0.2f);
        Vector3 c = new Vector3(bestB.center.x, bestB.max.y + 0.1f, bestB.center.z);
        Vector3[] samples =
        {
            c,
            c + right * inset,
            c - right * inset,
            c + fwd * inset,
            c - fwd * inset
        };
        for (int s = 0; s < samples.Length; s++)
        {
            if (ProbeDeck(samples[s], DeckScratch, out feet, out hull))
                return true;
        }

        feet = new Vector3(bestB.center.x, bestB.max.y + 0.06f, bestB.center.z);
        hull = best;
        return true;
    }

    static bool ProbeDeck(Vector3 above, List<BoatPiece> island, out Vector3 feet, out BoatPiece hull)
    {
        feet = default;
        hull = null;
        int n = Physics.SphereCastNonAlloc(above + Vector3.up * 1.7f, 0.12f, Vector3.down, DeckProbe, 3f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.NegativeInfinity;
        Vector3 pt = default;
        for (int i = 0; i < n; i++)
        {
            var hit = DeckProbe[i];
            if (hit.collider == null || hit.collider.isTrigger)
                continue;
            if (hit.collider.GetComponentInParent<BoatWater>() != null)
                continue;
            var piece = hit.collider.GetComponentInParent<BoatPiece>();
            if (piece == null || piece.Kind == BoatPieceKind.Oar)
                continue;
            bool onIsland = false;
            for (int k = 0; k < island.Count; k++)
            {
                if (island[k] == piece)
                {
                    onIsland = true;
                    break;
                }
            }
            if (!onIsland)
                continue;
            if (hit.normal.y < 0.5f)
                continue;
            if (hit.point.y > bestY)
            {
                bestY = hit.point.y;
                pt = hit.point;
                hull = piece;
            }
        }
        if (hull == null)
            return false;
        feet = pt + Vector3.up * 0.05f;
        return true;
    }

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
            if (p.kind == BoatPieceKind.Oar)
            {
                if (p.OarIsMounted() && p._oarLatchHull != null)
                    TryAddIsland(p._oarLatchHull);
            }
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
        center = _colCenter[axis];
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

        Vector3 size = pieceSize;
        Vector3 leftSize = size;
        Vector3 rightSize = size;
        leftSize[axis] = left;
        rightSize[axis] = right;

        Vector3 origin = _colCenter;
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
        Vector3 vel = Vector3.zero;
        Rigidbody root = IslandRootBody();
        if (root != null)
            vel = root.linearVelocity;
        else if (_rb != null)
            vel = _rb.linearVelocity;

        EnsureBarkMap();
        float span = Mathf.Max(0.001f, max - min);
        float leftT = left / span;
        float rightT = 1f - right / span;
        float keepUv0 = _uv0;
        float keepUv1 = _uv1;
        float offUv0 = _uv0;
        float offUv1 = _uv1;
        if (keepLeft)
        {
            keepUv1 = Mathf.Lerp(_uv0, _uv1, leftT);
            offUv0 = Mathf.Lerp(_uv0, _uv1, rightT);
        }
        else
        {
            keepUv0 = Mathf.Lerp(_uv0, _uv1, rightT);
            offUv1 = Mathf.Lerp(_uv0, _uv1, leftT);
        }
        _uv0 = keepUv0;
        _uv1 = keepUv1;

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
        var other = SpawnTwin(offWorld, rot, offSize, offUv0, offUv1);
        RelocateFasteners(other, axis, cut, keepLeft);

        Vector3 along = transform.TransformDirection(AxisUnit(axis));
        float away = keepLeft ? 1f : -1f;
        if (other.Body != null)
        {
            other.Body.maxDepenetrationVelocity = 0.4f;
            BoatBuildUtil.SetMotion(other.Body, vel * 0.25f + along * away * 0.45f, Vector3.zero);
        }

        IgnoreCollidersBrief(BoatBuildUtil.SolidCollider(this), BoatBuildUtil.SolidCollider(other), 0.55f);
        BoatIsland.Refresh(this);
        if (other != null)
            BoatIsland.Refresh(other);
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

    BoatPiece SpawnTwin(Vector3 pos, Quaternion rot, Vector3 size, float uv0, float uv1)
    {
        _spawnPending = true;
        _spawnKind = kind;
        _spawnSize = size;
        _spawnBark = true;
        _spawnUv0 = uv0;
        _spawnUv1 = uv1;
        _spawnBarkAcross = _barkAcross;
        _spawnBarkAlong = _barkAlong;
        var go = new GameObject(kind + "Piece");
        go.transform.SetPositionAndRotation(pos, rot);
        return go.AddComponent<BoatPiece>();
    }

    public static bool TryFindPair(Vector3 point, float radius, out BoatPiece a, out BoatPiece b)
    {
        return TryFindPair(point, radius, null, out a, out b);
    }

    public static bool TryFindPair(Vector3 point, float radius, BoatPiece preferred, out BoatPiece a, out BoatPiece b)
    {
        a = null;
        b = null;
        BoatMaterialItem.PromoteAround(point, radius + 0.2f);
        int n = Physics.OverlapSphereNonAlloc(point, radius, OverlapScratch, ~0, QueryTriggerInteraction.Ignore);
        BoatPiece first = preferred != null && preferred.isActiveAndEnabled ? preferred : null;
        float d1 = first != null ? 0f : float.PositiveInfinity;
        BoatPiece second = null;
        float d2 = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            var col = OverlapScratch[i];
            var p = col != null ? col.GetComponentInParent<BoatPiece>() : null;
            if (p == null || !p.isActiveAndEnabled)
                continue;
            if (p.GetComponentInParent<BoatClusterItem>() != null)
                continue;
            Vector3 closest = BoatBuildUtil.ClosestPoint(col, point);
            float d = (closest - point).sqrMagnitude;
            if (p == first)
                continue;
            if (first == null || (preferred == null && d < d1))
            {
                if (first != null)
                {
                    second = first;
                    d2 = d1;
                }
                first = p;
                d1 = d;
                continue;
            }
            if (d < d2)
            {
                d2 = d;
                second = p;
            }
        }
        if (first == null || second == null)
            return false;
        if (d2 > 0.18f * 0.18f)
            return false;
        a = first;
        b = second;
        return true;
    }

    public static BoatPiece Ray(Ray ray, float dist, out RaycastHit hit)
    {
        if (!Physics.Raycast(ray, out hit, dist, ~0, QueryTriggerInteraction.Ignore))
            return null;
        return hit.collider.GetComponentInParent<BoatPiece>();
    }

    void EnsureBarkMap()
    {
        if (_barkMapped || kind == BoatPieceKind.Oar || kind == BoatPieceKind.Barrel)
            return;
        BoatVisuals.BarkSpanForSize(kind, pieceSize, out _barkAcross, out _barkAlong);
        _uv0 = 0f;
        _uv1 = 1f;
        _barkMapped = true;
    }

    void ApplyVisual()
    {
        if (kind == BoatPieceKind.Oar)
        {
            BoatVisuals.BuildMountOar(transform);
            return;
        }
        EnsureBarkMap();
        var vis = BoatVisuals.Attach(
            transform,
            BoatVisuals.Shape(kind),
            BoatVisuals.VisualScale(kind, pieceSize),
            BoatVisuals.VisualRotation(kind),
            BoatVisuals.MaterialFor(kind));
        vis.transform.localPosition = _colCenter;
        var filter = vis.GetComponent<MeshFilter>();
        var rend = vis.GetComponent<Renderer>();
        if (kind == BoatPieceKind.Plank)
        {
            BoatVisuals.MapPlankLengthUv(filter, _uv0, _uv1);
            BoatVisuals.ApplyBarkUv(rend, _barkAcross, _barkAlong, 0f, 1f);
        }
        else
            BoatVisuals.ApplyBarkUv(rend, _barkAcross, _barkAlong, _uv0, _uv1);
        _lookRends = vis != null ? vis.GetComponentsInChildren<Renderer>(false) : null;
        _wetShown = -1f;
    }

    void ApplyPhysics()
    {
        _box = GetComponent<BoxCollider>();
        if (_box == null)
            _box = gameObject.AddComponent<BoxCollider>();
        _box.size = pieceSize;
        if (kind == BoatPieceKind.Oar)
        {
            BoatVisuals.PlaceBox(kind, pieceSize, out Vector3 oarCenter, out Vector3 oarBox);
            _colCenter = oarCenter;
            _box.center = oarCenter;
            _box.size = oarBox;
        }
        if (kind == BoatPieceKind.Plank)
        {
            Vector3 s = _box.size;
            s.y = Mathf.Max(s.y, 0.07f);
            _box.size = s;
        }
        _box.center = _colCenter;
        _box.isTrigger = false;
        if (kind == BoatPieceKind.Log)
        {
            float d = Mathf.Max(pieceSize.x, pieceSize.y);
            _box.size = new Vector3(d, d, pieceSize.z);
            _box.enabled = true;
            var cap = GetComponent<CapsuleCollider>();
            if (cap != null)
                cap.enabled = false;
        }
        else
        {
            var cap = GetComponent<CapsuleCollider>();
            if (cap != null)
                cap.enabled = false;
            _box.enabled = true;
        }
        if (kind == BoatPieceKind.Oar)
        {
            var childCols = GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < childCols.Length; i++)
            {
                if (childCols[i] == null || childCols[i] == _box)
                    continue;
                childCols[i].isTrigger = false;
                childCols[i].enabled = true;
                BoatBuildUtil.EnsureCollideWithActors(childCols[i]);
            }
        }

        if (_weldSlave)
        {
            _rb = null;
            if (kind == BoatPieceKind.Oar)
                SetOarSolids(false);
            return;
        }

        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = gameObject.AddComponent<Rigidbody>();
        _rb.mass = Mathf.Max(0.4f, BoatVisuals.Mass(kind) * (pieceSize.x * pieceSize.y * pieceSize.z) /
            (BoatVisuals.DefaultSize(kind).x * BoatVisuals.DefaultSize(kind).y * BoatVisuals.DefaultSize(kind).z));
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = kind == BoatPieceKind.Plank || kind == BoatPieceKind.Log
            ? CollisionDetectionMode.ContinuousDynamic
            : CollisionDetectionMode.ContinuousSpeculative;
        _rb.linearDamping = kind == BoatPieceKind.Plank || kind == BoatPieceKind.Log ? 0.32f : 0.28f;
        _rb.angularDamping = kind == BoatPieceKind.Oar ? 2.8f : 2.6f;
        _rb.maxDepenetrationVelocity = 0.45f;
        _rb.automaticCenterOfMass = false;
        if (kind == BoatPieceKind.Oar)
            _rb.centerOfMass = new Vector3(0f, -0.12f, 0.22f);
        else
            _rb.centerOfMass = _box.center;
        _rb.automaticInertiaTensor = true;
        _rb.ResetInertiaTensor();
        if (kind == BoatPieceKind.Log)
        {
            Vector3 it = _rb.inertiaTensor;
            it.z *= 5.5f;
            it.x *= 1.6f;
            it.y *= 1.6f;
            _rb.automaticInertiaTensor = false;
            _rb.inertiaTensor = it;
        }
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
            dynamicFriction = 0.95f,
            staticFriction = 1f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Maximum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        return _woodPhys;
    }

    void OnCollisionEnter(Collision collision)
    {
        BoatHull.Impact(this, collision);
        if (ThrownByPlayer)
        {
            var shark = collision.collider != null
                ? collision.collider.GetComponentInParent<RiverShark>()
                : null;
            shark?.HitByThrown(KindName(kind), collision);
        }
    }

    void OnJointBreak(float _)
    {
        BoatHull.JointBroke(this);
    }

    public static string KindName(BoatPieceKind k)
    {
        switch (k)
        {
            case BoatPieceKind.Log: return "Log";
            case BoatPieceKind.Barrel: return "Barrel";
            case BoatPieceKind.Oar: return "Oar";
            default: return "Plank";
        }
    }
}
