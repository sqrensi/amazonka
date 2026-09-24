using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Камнепад с отмеченных точек. Если точек нет — ставит их по берегам течения.
/// </summary>
[DefaultExecutionOrder(46)]
public class RockfallDirector : MonoBehaviour
{
    [SerializeField] bool autoSeed = true;
    [SerializeField] int seedEvery = 1;
    [SerializeField] bool active;

    readonly List<RockfallPoint> _points = new List<RockfallPoint>(32);
    readonly List<Vector3> _boats = new List<Vector3>(8);
    Vector3 _playerBoat;
    bool _hasPlayer;
    bool _seeded;
    float _scanAt;
    float _hailAt;

    void Awake()
    {
        Collect();
    }

    void Update()
    {
        if (!active)
            return;
        if (!RaceSim.HasAuthority)
            return;
        var race = BoatRaceMode.Current;
        if (race == null)
            return;
        if (race.CurrentPhase != BoatRaceMode.Phase.Race && race.CurrentPhase != BoatRaceMode.Phase.Launch)
            return;
        if (!_seeded)
        {
            _seeded = true;
            Collect();
            if (_points.Count == 0 && autoSeed)
                SeedAlongRiver();
        }
        ScanBoats();
        ExtraHail();
        for (int i = 0; i < _points.Count; i++)
        {
            var pt = _points[i];
            if (pt == null || !pt.isActiveAndEnabled || !pt.Ready)
                continue;
            float haste = BoatHaste(pt.transform.position, out Vector3 toward);
            if (haste >= 0.4f)
                continue;
            if (!FallingRock.CanSpawn)
            {
                pt.Arm(0.2f);
                continue;
            }
            int n = pt.RollBurst(haste);
            for (int k = 0; k < n; k++)
            {
                if (!FallingRock.CanSpawn)
                    break;
                Vector3 pos = pt.transform.position + Random.insideUnitSphere * 0.55f;
                FallingRock.Spawn(pos, pt.ThrowVelocity(toward));
            }
            pt.ScheduleNext(haste);
        }
    }

    void Collect()
    {
        _points.Clear();
        var found = Object.FindObjectsByType<RockfallPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
            {
                _points.Add(found[i]);
                if (found[i].Ready)
                    found[i].Arm(Random.Range(0.8f, 3.5f));
            }
        }
    }

    void SeedAlongRiver()
    {
        var path = Object.FindFirstObjectByType<BoatCurrentPath>();
        if (path == null)
            return;
        var root = new GameObject("RockfallPoints");
        root.transform.SetParent(transform, false);
        float half = Mathf.Max(8f, path.ChannelWidth * 0.48f);
        int step = Mathf.Max(1, seedEvery);
        int n = path.transform.childCount;
        int made = 0;
        for (int i = 0; i < n - 1; i += step)
        {
            Transform a = path.transform.GetChild(i);
            Transform b = path.transform.GetChild(Mathf.Min(i + 1, n - 1));
            if (a == null || b == null || !a.gameObject.activeInHierarchy)
                continue;
            Vector3 tan = b.position - a.position;
            tan.y = 0f;
            if (tan.sqrMagnitude < 0.01f)
                tan = Vector3.forward;
            else
                tan.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tan);
            if (side.sqrMagnitude < 0.01f)
                side = Vector3.right;
            else
                side.Normalize();
            Place(root.transform, a.position, side, half);
            Place(root.transform, a.position, -side, half);
            made += 2;
            if (made >= 28)
                break;
        }
        Collect();
    }

    static void Place(Transform parent, Vector3 along, Vector3 side, float half)
    {
        Vector3 xz = along + side * half;
        xz.y = 0f;
        float y = along.y + 12f;
        if (BoatWater.TryHeight(along, out float waterY))
            y = waterY + 14f;
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float ty = terrain.SampleHeight(xz) + terrain.transform.position.y;
            y = Mathf.Max(y, ty + 5.5f);
        }
        var go = new GameObject("RockfallPoint");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(xz.x, y, xz.z);
        Vector3 look = -side + Vector3.down * 0.35f;
        if (look.sqrMagnitude > 0.01f)
            go.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        var pt = go.AddComponent<RockfallPoint>();
        pt.Arm(Random.Range(0.6f, 4f));
    }

    void ScanBoats()
    {
        if (Time.time < _scanAt)
            return;
        _scanAt = Time.time + 0.3f;
        _boats.Clear();
        _hasPlayer = false;
        var local = RaceRoster.Local();
        if (local != null)
        {
            _playerBoat = local.Craft != null ? local.Craft.transform.position : local.transform.position;
            _hasPlayer = true;
            _boats.Add(_playerBoat);
        }
        var actors = RaceRoster.All;
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            if (a == null || a == local)
                continue;
            if (a.Craft != null)
                _boats.Add(a.Craft.transform.position);
            else
                _boats.Add(a.transform.position);
        }
    }

    void ExtraHail()
    {
        if (!_hasPlayer || Time.time < _hailAt)
            return;
        RockfallPoint a = null;
        RockfallPoint b = null;
        float bestA = 32f * 32f;
        float bestB = 32f * 32f;
        for (int i = 0; i < _points.Count; i++)
        {
            var pt = _points[i];
            if (pt == null || !pt.isActiveAndEnabled)
                continue;
            Vector3 d = pt.transform.position - _playerBoat;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < bestA)
            {
                bestB = bestA;
                b = a;
                bestA = sq;
                a = pt;
            }
            else if (sq < bestB)
            {
                bestB = sq;
                b = pt;
            }
        }
        if (a == null)
            return;
        _hailAt = Time.time + Random.Range(0.12f, 0.22f);
        int n = Random.Range(2, 4);
        Dump(a, _playerBoat - a.transform.position, n);
        if (b != null && bestB < 32f * 32f && FallingRock.CanSpawn && Random.value < 0.45f)
            Dump(b, _playerBoat - b.transform.position, Random.Range(2, 4));
    }

    static void Dump(RockfallPoint pt, Vector3 toward, int n)
    {
        for (int k = 0; k < n; k++)
        {
            if (!FallingRock.CanSpawn)
                return;
            Vector3 pos = pt.transform.position + Random.insideUnitSphere * 1.1f;
            FallingRock.Spawn(pos, pt.ThrowVelocity(toward));
        }
    }

    float BoatHaste(Vector3 from, out Vector3 toward)
    {
        toward = Vector3.zero;
        float best = 48f * 48f;
        for (int i = 0; i < _boats.Count; i++)
        {
            Vector3 d = _boats[i] - from;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < best)
            {
                best = sq;
                toward = d;
            }
        }
        float dist = Mathf.Sqrt(best);
        return Mathf.InverseLerp(38f, 11f, dist);
    }
}
