using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Течение по точкам. Дочерние Empty — waypoints по порядку.
/// Horror/Boat Race/Create Current Path.
/// </summary>
[DefaultExecutionOrder(-50)]
public class BoatCurrentPath : MonoBehaviour
{
    static readonly List<BoatCurrentPath> All = new List<BoatCurrentPath>();
    static readonly List<Transform> Tmp = new List<Transform>(16);
    static readonly List<Transform> SpawnPts = new List<Transform>(32);

    [SerializeField] float speed = 4.5f;
    [SerializeField] float width = 42f;

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        if (RacePath == this)
            ClearRaceWindow();
    }

    static BoatCurrentPath RacePath;
    static int RaceFrom;
    static int RaceLen;

    public static bool HasRaceWindow => RacePath != null && RaceLen >= 2;
    public static int RacePointCount => RaceLen;

    public static void ClearRaceWindow()
    {
        RacePath = null;
        RaceFrom = 0;
        RaceLen = 0;
    }

    /// <summary>
    /// Each round: a consecutive 15–20 point slice. Launch = first, finish = last.
    /// Navigation (sharks, InChannel) uses that slice; flow still follows the whole river.
    /// </summary>
    public static bool PickRaceWindow(int seed, int minPoints = 15, int maxPoints = 20)
    {
        ClearRaceWindow();
        CollectActiveFull(SpawnPts);
        int n = SpawnPts.Count;
        if (n < 2)
            return false;
        var rng = new System.Random(unchecked(seed * 11003 + 17));
        int want = rng.Next(Mathf.Min(minPoints, maxPoints), Mathf.Max(minPoints, maxPoints) + 1);
        want = Mathf.Clamp(want, 2, n);
        int start = n <= want ? 0 : rng.Next(0, n - want + 1);
        for (int p = 0; p < All.Count; p++)
        {
            var path = All[p];
            if (path == null || !path.isActiveAndEnabled)
                continue;
            int count = path.ActiveChildCount();
            if (count < 2)
                continue;
            RacePath = path;
            RaceFrom = start;
            RaceLen = want;
            return true;
        }
        return false;
    }

    public static bool TryRaceEnds(out Vector3 launch, out Vector3 finish, out Vector3 launchTan, out Vector3 finishTan)
    {
        launch = finish = Vector3.zero;
        launchTan = finishTan = Vector3.forward;
        if (!HasRaceWindow)
            return false;
        RacePath.CollectRange(SpawnPts, RaceFrom, RaceLen);
        if (SpawnPts.Count < 2)
            return false;
        Transform a = SpawnPts[0];
        Transform b = SpawnPts[SpawnPts.Count - 1];
        Transform a2 = SpawnPts[1];
        Transform b2 = SpawnPts[SpawnPts.Count - 2];
        if (a == null || b == null || a2 == null || b2 == null)
            return false;
        launch = a.position;
        finish = b.position;
        launchTan = PlanarDir(a2.position - a.position);
        finishTan = PlanarDir(b.position - b2.position);
        return true;
    }

    int ActiveChildCount()
    {
        int n = 0;
        int c = transform.childCount;
        for (int i = 0; i < c; i++)
        {
            var t = transform.GetChild(i);
            if (t != null && t.gameObject.activeInHierarchy)
                n++;
        }
        return n;
    }

    void CollectRange(List<Transform> into, int from, int count)
    {
        into.Clear();
        int n = transform.childCount;
        int taken = 0;
        int skip = Mathf.Max(0, from);
        for (int i = 0; i < n; i++)
        {
            var c = transform.GetChild(i);
            if (c == null || !c.gameObject.activeInHierarchy)
                continue;
            if (skip > 0)
            {
                skip--;
                continue;
            }
            into.Add(c);
            taken++;
            if (taken >= count)
                return;
        }
    }

    static void CollectActiveFull(List<Transform> into)
    {
        into.Clear();
        for (int p = 0; p < All.Count; p++)
        {
            var path = All[p];
            if (path == null || !path.isActiveAndEnabled)
                continue;
            path.Collect(into);
            if (into.Count >= 2)
                return;
        }
    }

    public float ChannelWidth => width;

    public static bool TryCenter(Vector3 world, out Vector3 point, out Vector3 tangent)
    {
        return TryAhead(world, 0f, 0f, out point, out tangent);
    }

    public static float OffCenter(Vector3 world)
    {
        if (!TryCenter(world, out Vector3 point, out _))
            return 9999f;
        return PlanarDist(world, point);
    }

    public static bool InChannel(Vector3 world, float maxOff = 5.5f)
    {
        return OffCenter(world) <= maxOff;
    }

    public static bool TryAhead(Vector3 world, float aheadMeters, float lateral, out Vector3 point, out Vector3 tangent)
    {
        point = world;
        tangent = Vector3.forward;
        CollectActive(SpawnPts);
        if (SpawnPts.Count < 2)
            return false;
        if (!ClosestStation(world, SpawnPts, out int seg, out float t, out float dist))
            return false;
        float s = 0f;
        for (int i = 0; i < seg; i++)
            s += PlanarDist(SpawnPts[i].position, SpawnPts[i + 1].position);
        s += t * PlanarDist(SpawnPts[seg].position, SpawnPts[seg + 1].position);
        return PlaceAlong(SpawnPts, s + aheadMeters, lateral, out point, out tangent);
    }

    public static bool TryRandomAlong(Vector3 world, float minAhead, float maxAhead, float maxLateral, System.Random rng, out Vector3 point, out Vector3 tangent)
    {
        float u = rng != null ? (float)rng.NextDouble() : Random.value;
        float ahead = Mathf.Lerp(minAhead, maxAhead, u);
        float lat = ((rng != null ? (float)rng.NextDouble() : Random.value) - 0.5f) * 2f * maxLateral;
        return TryAhead(world, ahead, lat, out point, out tangent);
    }

    static void CollectActive(List<Transform> into)
    {
        into.Clear();
        if (HasRaceWindow && RacePath.isActiveAndEnabled)
        {
            RacePath.CollectRange(into, RaceFrom, RaceLen);
            if (into.Count >= 2)
                return;
        }
        CollectActiveFull(into);
    }

    static bool ClosestStation(Vector3 world, List<Transform> pts, out int seg, out float t, out float dist)
    {
        seg = 0;
        t = 0f;
        dist = float.PositiveInfinity;
        Vector3 planar = world;
        planar.y = 0f;
        bool any = false;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (pts[i] == null || pts[i + 1] == null)
                continue;
            Vector3 a = pts[i].position;
            Vector3 b = pts[i + 1].position;
            a.y = 0f;
            b.y = 0f;
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.05f)
                continue;
            float u = Mathf.Clamp01(Vector3.Dot(planar - a, ab) / (len * len));
            float d = Vector3.Distance(planar, a + ab * u);
            if (d >= dist)
                continue;
            dist = d;
            seg = i;
            t = u;
            any = true;
        }
        return any;
    }

    static bool PlaceAlong(List<Transform> pts, float s, float lateral, out Vector3 point, out Vector3 tangent)
    {
        point = pts[0].position;
        tangent = Vector3.forward;
        float remain = Mathf.Max(0f, s);
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (pts[i] == null || pts[i + 1] == null)
                continue;
            Vector3 a = pts[i].position;
            Vector3 b = pts[i + 1].position;
            Vector3 ab = b - a;
            ab.y = 0f;
            float len = ab.magnitude;
            if (len < 0.05f)
                continue;
            tangent = ab / len;
            if (remain <= len)
            {
                Vector3 p = Vector3.Lerp(a, b, remain / len);
                Vector3 side = Vector3.Cross(Vector3.up, tangent);
                if (side.sqrMagnitude > 0.01f)
                    side.Normalize();
                else
                    side = Vector3.right;
                p += side * lateral;
                p.y = Mathf.Lerp(a.y, b.y, remain / len);
                point = p;
                return true;
            }
            remain -= len;
        }
        Vector3 last = pts[pts.Count - 1].position;
        Vector3 prev = pts[pts.Count - 2].position;
        tangent = PlanarDir(last - prev);
        Vector3 lastSide = Vector3.Cross(Vector3.up, tangent);
        if (lastSide.sqrMagnitude > 0.01f)
            lastSide.Normalize();
        point = last + lastSide * lateral;
        return true;
    }

    static Vector3 PlanarDir(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    static float PlanarDist(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    public static Vector3 FlowAt(Vector3 world)
    {
        if (!BoatWater.CurrentEnabled)
            return Vector3.zero;
        Vector3 best = Vector3.zero;
        float bestDist = float.PositiveInfinity;
        for (int i = 0; i < All.Count; i++)
        {
            var path = All[i];
            if (path == null || !path.isActiveAndEnabled)
                continue;
            if (!path.Sample(world, out Vector3 flow, out float dist))
                continue;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = flow;
            }
        }
        return best;
    }

    bool Sample(Vector3 world, out Vector3 flow, out float dist)
    {
        flow = Vector3.zero;
        dist = float.PositiveInfinity;
        Collect(Tmp);
        if (Tmp.Count < 2)
            return false;
        float maxW = Mathf.Max(4f, width);
        Vector3 planar = world;
        planar.y = 0f;
        int bestI = -1;
        Vector3 bestDir = Vector3.forward;
        for (int i = 0; i < Tmp.Count - 1; i++)
        {
            if (Tmp[i] == null || Tmp[i + 1] == null)
                continue;
            Vector3 a = Tmp[i].position;
            Vector3 b = Tmp[i + 1].position;
            a.y = 0f;
            b.y = 0f;
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.05f)
                continue;
            float t = Mathf.Clamp01(Vector3.Dot(planar - a, ab) / (len * len));
            Vector3 proj = a + ab * t;
            float d = Vector3.Distance(planar, proj);
            if (d >= dist)
                continue;
            dist = d;
            bestI = i;
            bestDir = ab / len;
        }
        if (bestI < 0)
            return false;
        Vector3 dir = bestDir.sqrMagnitude > 0.01f ? bestDir.normalized : Vector3.forward;
        float outer = maxW * 1.15f;
        if (dist > outer)
            return false;
        flow = dir * speed;
        return true;
    }

    void Collect(List<Transform> into)
    {
        into.Clear();
        int n = transform.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = transform.GetChild(i);
            if (c != null && c.gameObject.activeInHierarchy)
                into.Add(c);
        }
    }

    [ContextMenu("Add Point")]
    public void AddPoint()
    {
        Collect(Tmp);
        var go = new GameObject($"Point_{transform.childCount}");
        go.transform.SetParent(transform, false);
        if (Tmp.Count >= 2)
        {
            Vector3 a = Tmp[Tmp.Count - 2].position;
            Vector3 b = Tmp[Tmp.Count - 1].position;
            go.transform.position = b + (b - a);
        }
        else if (Tmp.Count == 1)
            go.transform.position = Tmp[0].position + Vector3.forward * 12f;
        else
            go.transform.position = transform.position;
    }

    void OnDrawGizmos()
    {
        Collect(Tmp);
        bool race = HasRaceWindow && RacePath == this;
        for (int i = 0; i < Tmp.Count; i++)
        {
            if (Tmp[i] == null)
                continue;
            bool inRace = race && i >= RaceFrom && i < RaceFrom + RaceLen;
            Gizmos.color = inRace
                ? new Color(1f, 0.45f, 0.12f, 0.95f)
                : new Color(0.2f, 0.75f, 1f, 0.45f);
            bool end = inRace && (i == RaceFrom || i == RaceFrom + RaceLen - 1);
            Gizmos.DrawSphere(Tmp[i].position, end ? 0.7f : (inRace ? 0.42f : 0.28f));
            if (i < Tmp.Count - 1 && Tmp[i + 1] != null)
            {
                Gizmos.DrawLine(Tmp[i].position, Tmp[i + 1].position);
                Vector3 mid = (Tmp[i].position + Tmp[i + 1].position) * 0.5f;
                Vector3 dir = Tmp[i + 1].position - Tmp[i].position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    Gizmos.DrawRay(mid, dir.normalized * 2.2f);
            }
        }
    }
}
