using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Случайные появления акул в заезде. Одновременно не больше двух.
/// Тайминги от RoundSeed — одинаковые у клиентов при общем seed лобби.
/// </summary>
[DefaultExecutionOrder(45)]
public class SharkDirector : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float appearChance = 1f;
    [SerializeField] Vector2 firstDelay = new Vector2(4f, 10f);
    [SerializeField] Vector2 nextDelay = new Vector2(7f, 15f);
    [SerializeField] int maxLive = 2;

    readonly List<RiverShark> _live = new List<RiverShark>(4);
    System.Random _rng;
    bool _armed;
    bool _willSpawn;
    float _nextAt;

    public int AliveCount
    {
        get
        {
            Prune();
            return _live.Count;
        }
    }

    public void GetHudPair(out RiverShark a, out RiverShark b)
    {
        Prune();
        a = null;
        b = null;
        int bestA = int.MaxValue;
        int bestB = int.MaxValue;
        for (int i = 0; i < _live.Count; i++)
        {
            var s = _live[i];
            if (s == null || !s.ShowHud)
                continue;
            int id = s.Id;
            if (id < bestA)
            {
                bestB = bestA;
                b = a;
                bestA = id;
                a = s;
            }
            else if (id < bestB)
            {
                bestB = id;
                b = s;
            }
        }
    }

    public RiverShark Live
    {
        get
        {
            GetHudPair(out RiverShark a, out _);
            return a;
        }
    }

    public void Arm(int seed)
    {
        Clear();
        _rng = new System.Random(seed * 7919 + 104729);
        _willSpawn = _rng.NextDouble() < appearChance;
        _nextAt = Roll(new Vector2(4f, 10f));
        _armed = true;
    }

    public void Tick(float raceElapsed)
    {
        RaceSim.RaceElapsed = raceElapsed;
        if (!_armed || !_willSpawn)
            return;
        if (!RaceSim.HasAuthority)
            return;
        Prune();
        if (raceElapsed < _nextAt)
            return;
        if (_live.Count >= Mathf.Max(1, maxLive))
        {
            _nextAt = raceElapsed + 4f;
            return;
        }
        if (!TrySpawn())
        {
            _nextAt = raceElapsed + 2.2f;
            return;
        }
        _nextAt = raceElapsed + Roll(new Vector2(7f, 15f));
    }

    public void Clear()
    {
        _armed = false;
        _willSpawn = false;
        _nextAt = 0f;
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i] != null)
                Destroy(_live[i].gameObject);
        }
        _live.Clear();
    }

    float Roll(Vector2 range)
    {
        float a = Mathf.Min(range.x, range.y);
        float b = Mathf.Max(range.x, range.y);
        if (_rng == null)
            return a;
        return Mathf.Lerp(a, b, (float)_rng.NextDouble());
    }

    void Prune()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            if (_live[i] == null || _live[i].IsDead)
                _live.RemoveAt(i);
        }
    }

    bool TrySpawn()
    {
        var prey = RaceRoster.Local() ?? RaceRoster.PreyNear(Vector3.zero);
        if (prey == null)
            return false;
        Vector3 origin = prey.Craft != null ? prey.Craft.transform.position : prey.transform.position;
        if (!PickSpawn(origin, prey, out Vector3 pos, out bool ambush))
            return false;
        var shark = RiverShark.Spawn(pos, RaceSim.AllocId(), RollKind());
        if (shark == null)
            return false;
        if (ambush)
            shark.HoldAmbush(pos);
        _live.Add(shark);
        return true;
    }

    bool PickSpawn(Vector3 origin, RaceActor prey, out Vector3 pos, out bool ambush)
    {
        pos = origin;
        ambush = false;
        float roll = Rand();
        if (roll < 0.55f && TryPathSpawn(origin, prey, 12f, 42f, 0f, 2.2f, true, out pos))
        {
            ambush = true;
            return true;
        }
        if (TryPathSpawn(origin, prey, 8f, 28f, 0f, 2.4f, false, out pos))
            return true;
        if (roll < 0.8f && TryPathSpawn(origin, prey, 28f, 70f, 0f, 2.0f, true, out pos))
        {
            ambush = true;
            return true;
        }
        if (TryCloseFlank(origin, prey, out pos))
            return true;
        return TryReachableSpawn(origin, prey, out pos);
    }

    bool TryPathSpawn(Vector3 origin, RaceActor prey, float minAhead, float maxAhead, float minLat, float maxLat, bool allowDeepInView, out Vector3 pos)
    {
        pos = origin;
        for (int i = 0; i < 14; i++)
        {
            float ahead = Mathf.Lerp(minAhead, maxAhead, Rand());
            float lat = Mathf.Lerp(0f, 1.6f, Rand()) * (Rand() < 0.5f ? -1f : 1f);
            if (!BoatCurrentPath.TryAhead(origin, ahead, lat, out Vector3 sample, out Vector3 tangent))
                continue;
            if (!TryRiverColumn(sample, allowDeepInView ? 2.2f : 1.65f, out sample))
                continue;
            if (!allowDeepInView && InPlayerView(prey, sample))
                continue;
            if (allowDeepInView && InPlayerView(prey, sample) && !DeepEnough(sample, 1.9f))
                continue;
            float near = Planar(origin, sample);
            if (near < 8f || near > 110f)
                continue;
            Vector3 gate = sample + tangent * 5f;
            if (!CanSwimTo(sample, gate) || !CanSwimTo(sample, origin))
                continue;
            pos = sample;
            return true;
        }
        return false;
    }

    bool TryCloseFlank(Vector3 origin, RaceActor prey, out Vector3 pos)
    {
        pos = origin;
        Vector3 flow = BoatWater.CurrentAt(origin);
        Vector3 along = flow.sqrMagnitude > 0.01f ? Flatten(flow) : Flatten(prey.transform.forward);
        Vector3 side = Vector3.Cross(Vector3.up, along);
        if (side.sqrMagnitude < 0.01f)
            side = Vector3.right;
        side.Normalize();
        for (int i = 0; i < 12; i++)
        {
            float sign = Rand() < 0.5f ? -1f : 1f;
            Vector3 sample = origin + side * sign * (8f + Rand() * 4f) + along * (Rand() * 6f);
            if (!TryRiverColumn(sample, 1.7f, out sample))
                continue;
            if (InPlayerView(prey, sample) && !DeepEnough(sample, 1.9f))
                continue;
            if (!CanSwimTo(sample, origin))
                continue;
            pos = sample;
            return true;
        }
        return false;
    }

    static float Planar(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static bool DeepEnough(Vector3 p, float under)
    {
        if (!BoatWater.TryHeight(p, out float wy))
            return false;
        return wy - p.y >= under;
    }

    static bool TryRiverColumn(Vector3 xz, float underWant, out Vector3 swim)
    {
        swim = xz;
        if (!BoatCurrentPath.TryCenter(xz, out Vector3 mid, out Vector3 tan))
            return false;
        xz = mid;
        if (!BoatCurrentPath.InChannel(xz, 3.6f))
            return false;
        Vector3 side = Vector3.Cross(Vector3.up, tan);
        if (side.sqrMagnitude < 0.01f)
            side = Vector3.right;
        else
            side.Normalize();
        Vector3[] ring =
        {
            xz,
            xz + tan * 1.35f,
            xz - tan * 1.35f,
            xz + side * 1.2f,
            xz - side * 1.2f
        };
        for (int i = 0; i < ring.Length; i++)
        {
            if (!OpenWater(ring[i], out float wy, out float floorY) || wy - floorY < 2.05f)
                return false;
            if (Buried(ring[i], wy, floorY))
                return false;
        }
        if (!OpenWater(xz, out float waterY, out float floor))
            return false;
        float under = Mathf.Clamp(underWant, 1.45f, waterY - floor - 0.7f);
        swim = xz;
        swim.y = waterY - under;
        if (swim.y < floor + 0.85f)
            swim.y = floor + 0.85f;
        if (Buried(swim, waterY, floor) || !LaneOpen(swim))
            return false;
        return waterY - swim.y >= 1.2f && swim.y - floor >= 0.7f;
    }

    static bool Buried(Vector3 p, float waterY, float floorY)
    {
        Vector3 origin = p + Vector3.up * 0.08f;
        float up = Mathf.Max(0.2f, waterY - origin.y);
        if (Physics.Raycast(origin, Vector3.up, out RaycastHit hit, up, ~0, QueryTriggerInteraction.Ignore)
            && BlocksSwim(hit.collider))
            return true;
        Vector3 downFrom = new Vector3(p.x, waterY - 0.05f, p.z);
        if (Physics.Raycast(downFrom, Vector3.down, out RaycastHit ground, 14f, ~0, QueryTriggerInteraction.Ignore)
            && BlocksSwim(ground.collider))
        {
            if (ground.point.y > p.y + 0.12f)
                return true;
        }
        return p.y < floorY + 0.45f;
    }

    static bool FinishWater(Vector3 xz, float underWant, out Vector3 swim)
    {
        return TryRiverColumn(xz, underWant, out swim);
    }

    bool TryReachableSpawn(Vector3 origin, RaceActor prey, out Vector3 pos)
    {
        pos = origin;
        Vector3 flow = BoatWater.CurrentAt(origin);
        flow.y = 0f;
        Vector3 along = flow.sqrMagnitude > 0.01f ? flow.normalized : Flatten(prey.transform.forward);
        Vector3 side = Vector3.Cross(Vector3.up, along);
        if (side.sqrMagnitude < 0.01f)
            side = Vector3.right;
        side.Normalize();
        for (int i = 0; i < 36; i++)
        {
            Camera cam = PreyCam(prey);
            Vector3 camFwd = cam != null ? Flatten(cam.transform.forward) : along;
            bool behind = i < 24 || Rand() < 0.75f;
            float yaw = Rand() * 100f - 50f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * (behind ? -camFwd : camFwd);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = behind ? -along : along;
            dir.Normalize();
            float dist = 10f + Rand() * 8f;
            float lat = (Rand() - 0.5f) * 8f;
            Vector3 sample = origin + dir * dist + side * lat;
            if (!TryRiverColumn(sample, 1.6f, out sample))
                continue;
            if (InPlayerView(prey, sample))
                continue;
            if (!CanSwimTo(sample, origin))
                continue;
            pos = sample;
            return true;
        }
        return false;
    }

    static Camera PreyCam(RaceActor prey)
    {
        if (prey == null)
            return Camera.main;
        var cam = prey.GetComponentInChildren<Camera>();
        return cam != null ? cam : Camera.main;
    }

    static bool InPlayerView(RaceActor prey, Vector3 world)
    {
        Camera cam = PreyCam(prey);
        if (cam == null)
            return false;
        Vector3 vp = cam.WorldToViewportPoint(world);
        if (vp.z < 1.2f)
            return false;
        const float pad = 0.18f;
        return vp.x > -pad && vp.x < 1f + pad && vp.y > -pad && vp.y < 1f + pad;
    }

    static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    public static bool LaneOpen(Vector3 p)
    {
        const float r = 0.52f;
        int n = Physics.OverlapSphereNonAlloc(p, r, LaneBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (BlocksSwim(LaneBuf[i]))
                return false;
        }
        Vector3[] dirs =
        {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.forward + Vector3.left).normalized,
            (Vector3.back + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized
        };
        int blocked = 0;
        for (int i = 0; i < dirs.Length; i++)
        {
            if (Physics.SphereCast(p, 0.4f, dirs[i], out RaycastHit hit, 2.6f, ~0, QueryTriggerInteraction.Ignore)
                && BlocksSwim(hit.collider) && hit.distance < 1.15f)
                blocked++;
        }
        return blocked <= 2;
    }

    static readonly Collider[] LaneBuf = new Collider[16];

    public static Vector3 StayInRiver(Vector3 pos)
    {
        return StayInRiver(pos, 99f);
    }

    public static Vector3 StayInRiver(Vector3 pos, float maxPlanar)
    {
        Vector3 start = pos;
        if (BoatCurrentPath.TryCenter(pos, out Vector3 mid, out Vector3 tan))
        {
            Vector3 planar = pos;
            planar.y = 0f;
            Vector3 midP = mid;
            midP.y = 0f;
            float off = Vector3.Distance(planar, midP);
            const float maxOff = 3.8f;
            if (off > maxOff)
            {
                Vector3 pull = midP - planar;
                pos += pull.normalized * Mathf.Min(off - maxOff + 0.2f, 0.55f);
            }
        }

        for (int i = 0; i < 3; i++)
        {
            if (LaneOpen(pos) && !ColumnBlocked(pos))
                break;
            if (!BoatCurrentPath.TryCenter(pos, out mid, out tan))
                break;
            Vector3 to = mid - pos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f)
                break;
            pos += to.normalized * 0.45f;
        }

        if (BoatWater.TryHeight(pos, out float waterY))
        {
            float floor = waterY - 8f;
            Vector3 probe = new Vector3(pos.x, waterY - 0.08f, pos.z);
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit ground, 12f, ~0, QueryTriggerInteraction.Ignore)
                && BlocksSwim(ground.collider))
                floor = ground.point.y;
            float swimY = waterY - 0.45f;
            if (swimY < floor + 0.7f)
                swimY = floor + 0.7f;
            if (swimY > waterY - 0.18f)
                swimY = waterY - 0.18f;
            pos.y = Mathf.MoveTowards(start.y, swimY, 0.35f);
        }

        Vector3 slide = pos - start;
        slide.y = 0f;
        float cap = Mathf.Max(0.04f, maxPlanar);
        if (slide.magnitude > cap)
        {
            pos.x = start.x + slide.normalized.x * cap;
            pos.z = start.z + slide.normalized.z * cap;
        }
        if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)
            || float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
            return start;
        return pos;
    }

    public static bool CanStrikeFrom(Vector3 pos)
    {
        return BoatCurrentPath.InChannel(pos, 4.2f) && LaneOpen(pos) && !ColumnBlocked(pos);
    }

    static bool ColumnBlocked(Vector3 p)
    {
        if (!BoatWater.TryHeight(p, out float wy))
            return true;
        return Buried(p, wy, wy - 8f) || !OpenWater(p, out _, out _);
    }

    static bool BlocksSwim(Collider col)
    {
        if (col == null || !col.enabled || col.isTrigger)
            return false;
        if (col.GetComponentInParent<BoatWater>() != null)
            return false;
        if (col.GetComponentInParent<RiverShark>() != null)
            return false;
        if (col.GetComponentInParent<CharacterController>() != null)
            return false;
        if (col.GetComponentInParent<BoatPiece>() != null)
            return false;
        return true;
    }

    static bool OpenWater(Vector3 xz, out float waterY, out float floorY)
    {
        waterY = 0f;
        floorY = 0f;
        if (!BoatWater.TryHeight(xz, out waterY))
            return false;
        Vector3 probe = new Vector3(xz.x, waterY - 0.15f, xz.z);
        floorY = waterY - 12f;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 14f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider != null
            && hit.collider.GetComponentInParent<BoatWater>() == null
            && hit.collider.GetComponentInParent<RiverShark>() == null)
            floorY = hit.point.y;
        return waterY - floorY >= 1.35f;
    }

    static bool CanSwimTo(Vector3 from, Vector3 to)
    {
        Vector3 a = from;
        Vector3 b = to;
        a.y = 0f;
        b.y = 0f;
        float len = Vector3.Distance(a, b);
        if (len < 0.5f)
            return true;
        int steps = Mathf.Clamp(Mathf.CeilToInt(len / 3.2f), 3, 14);
        Vector3 prev = default;
        bool havePrev = false;
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)steps);
            if (!OpenWater(p, out float wy, out float floorY))
                return false;
            Vector3 swim = p;
            swim.y = Mathf.Lerp(floorY + 0.8f, wy - 0.45f, 0.55f);
            if (havePrev)
            {
                Vector3 delta = swim - prev;
                float dist = delta.magnitude;
                if (dist > 0.05f
                    && Physics.SphereCast(prev, 0.42f, delta / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
                {
                    var col = hit.collider;
                    if (col != null
                        && !col.isTrigger
                        && col.GetComponentInParent<BoatWater>() == null
                        && col.GetComponentInParent<RiverShark>() == null
                        && col.GetComponentInParent<CharacterController>() == null
                        && col.GetComponentInParent<BoatPiece>() == null)
                        return false;
                }
            }
            prev = swim;
            havePrev = true;
        }
        return true;
    }

    RiverShark.Kind RollKind()
    {
        float u = Rand();
        if (u < 0.34f)
            return RiverShark.Kind.Bull;
        if (u < 0.67f)
            return RiverShark.Kind.Runner;
        return RiverShark.Kind.Stalker;
    }

    float Rand()
    {
        return _rng != null ? (float)_rng.NextDouble() : Random.value;
    }

    void OnDestroy()
    {
        Clear();
    }
}
