using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Boat mode round: timed island build, launch into water, race to finish.
/// </summary>
[DefaultExecutionOrder(-40)]
public class BoatRaceMode : MonoBehaviour
{
    public enum Phase
    {
        Intro,
        Build,
        Launch,
        Race,
        Results
    }

    public static BoatRaceMode Current { get; private set; }

    [Header("Places")]
    [SerializeField] Transform playerSpawn;
    [SerializeField] Transform waterLaunch;
    [SerializeField] Transform finish;
    [SerializeField] float finishRadius = 6.5f;

    [Header("Timers")]
    [SerializeField] float buildSeconds = 180f;
    [SerializeField] float raceSeconds = 180f;
    [SerializeField] float launchHold = 1.35f;
    [SerializeField] float fallGrace = 1.6f;

    [Header("Pacing")]
    [SerializeField] float failSlow = 1.55f;

    [Header("Loot")]
    [SerializeField] bool scatterLoot = true;
    [SerializeField] GameObject[] lootPrefabs;
    [SerializeField] int[] lootCounts;
    [SerializeField] float scatterRadius = 18f;
    [SerializeField] int scatterSeed = 17;

    [Header("Play")]
    [SerializeField] bool autoStart = true;
    [SerializeField] KeyCode restartKey = KeyCode.R;

    Phase _phase = Phase.Intro;
    float _endsAt;
    float _raceStartedAt;
    float _offBoatSince = -1f;
    float _sunkSince = -1f;
    bool _ended;
    bool _closing;
    bool _won;
    int _score;
    string _failReason = "";
    BoatPiece _craft;
    HorrorFirstPersonController _player;
    BoatRaceHud _hud;
    SharkDirector _sharks;
    RaceActor _actor;
    readonly List<GameObject> _spawnedLoot = new List<GameObject>();

    float _craftScanAt = -1f;
    BoatPiece _scannedCraft;

    float _spawnProtectUntil;

    public Phase CurrentPhase => _phase;
    public BoatPiece PlayerCraft => _craft;
    public bool HandsLocked => _closing || _ended || _phase == Phase.Launch || _phase == Phase.Results || _phase == Phase.Intro;
    public Transform Finish => finish;
    public Transform WaterLaunch => waterLaunch;
    public Transform PlayerSpawn => playerSpawn;

    public bool IsLootSource(GameObject go)
    {
        if (go == null || lootPrefabs == null)
            return false;
        for (int i = 0; i < lootPrefabs.Length; i++)
        {
            if (lootPrefabs[i] == go)
                return true;
        }
        return false;
    }

    public IReadOnlyList<GameObject> SpawnedLoot => _spawnedLoot;

    void Awake()
    {
        EnsureFinishTrigger();
        BoatLayers.Ensure();
    }

    void OnDestroy()
    {
        if (Current == this)
            Current = null;
        BoatCurrentPath.ClearRaceWindow();
        BoatWater.CurrentEnabled = true;
        Time.timeScale = 1f;
    }

    void Start()
    {
        if (PlaySession.IsHouseScene)
            return;
        if (PlaySession.Active == PlaySession.Mode.HouseHold)
            return;
        if (PlaySession.Active == PlaySession.Mode.None)
            PlaySession.Choose(PlaySession.Mode.BoatRace);
        if (PlaySession.Active == PlaySession.Mode.BoatRace)
            BeginRound();
    }

    void Update()
    {
        if (_ended || _closing)
        {
            if (_hud != null && _hud.CanRestart && RestartPressed())
                RestartScene();
            return;
        }

        if (_phase == Phase.Build)
            TickBuild();
        else if (_phase == Phase.Race)
            TickRace();
    }

    public void BeginRound()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        _ended = false;
        _closing = false;
        _won = false;
        _score = 0;
        _failReason = "";
        _craft = null;
        Current = this;
        _hud = GetComponent<BoatRaceHud>();
        if (_hud == null)
            _hud = gameObject.AddComponent<BoatRaceHud>();
        _hud.enabled = true;
        _hud.EnsureBuilt();
        _offBoatSince = -1f;
        _sunkSince = -1f;
        BoatWater.CurrentEnabled = false;
        RaceSim.BeginRound(unchecked(scatterSeed * 397 + (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff)));
        BoatCurrentPath.PickRaceWindow(RaceSim.RoundSeed, 15, 20);
        try
        {
            RaceMood.ApplyRound(RaceSim.RoundSeed);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Race] mood failed: " + e.Message);
        }
        PlaceRaceOnPath();
        _player = FindFirstObjectByType<HorrorFirstPersonController>();
        _actor = RaceActor.BindLocal(_player);
        _sharks = GetComponent<SharkDirector>();
        if (_sharks == null)
            _sharks = gameObject.AddComponent<SharkDirector>();
        if (_sharks != null)
            _sharks.Clear();
        if (_player != null)
            _player.MovementLocked = true;
        PlacePlayerAtSpawn();
        ScatterLoot();
        StartCoroutine(RunIntro());
    }

    void PlaceRaceOnPath()
    {
        if (!BoatCurrentPath.TryRaceEnds(out Vector3 launchPos, out Vector3 finishPos, out Vector3 launchTan, out Vector3 finishTan))
            return;
        if (waterLaunch == null)
        {
            var go = new GameObject("Launch");
            go.transform.SetParent(transform, false);
            waterLaunch = go.transform;
        }
        if (finish == null)
        {
            var go = new GameObject("Finish");
            go.transform.SetParent(transform, false);
            finish = go.transform;
        }
        waterLaunch.SetPositionAndRotation(launchPos, Quaternion.LookRotation(launchTan, Vector3.up));
        finish.SetPositionAndRotation(finishPos, Quaternion.LookRotation(finishTan, Vector3.up));
        EnsureFinishTrigger();
    }

    IEnumerator RunIntro()
    {
        _phase = Phase.Intro;
        if (_player != null)
            _player.MovementLocked = true;
        yield return _hud.PlayIntro();
        EnterBuild();
        yield return _hud.PlayBuildOpen();
    }

    void EnterBuild()
    {
        _phase = Phase.Build;
        if (_player != null)
            _player.MovementLocked = false;
        _endsAt = Time.unscaledTime + buildSeconds;
        _hud.SetTimer(buildSeconds, false);
    }

    void TickBuild()
    {
        float left = Mathf.Max(0f, _endsAt - Time.unscaledTime);
        _hud.SetTimer(left, left < 20f);
        if (Time.unscaledTime >= _craftScanAt)
        {
            _craftScanAt = Time.unscaledTime + 0.25f;
            _scannedCraft = FindAssembledBoat() ?? FindLoneFloater();
        }
        _hud.SetBuildStatus(_scannedCraft);
        if (left <= 0f)
            StartCoroutine(RunLaunch());
    }

    IEnumerator RunLaunch()
    {
        _phase = Phase.Launch;
        _craft = FindAssembledBoat() ?? FindLoneFloater() ?? SpawnFloater();
        if (_craft == null)
        {
            Fail("No boat to launch");
            yield break;
        }

        if (_player != null)
        {
            _player.MovementLocked = true;
            _player.ReleaseBoatFollow();
            _player.ClearSwimState();
        }
        ClearStartIsland(_craft);
        yield return _hud.PlayLaunchOut();
        TeleportCraftToWater(_craft);
        BoatHull.GuardPush(3.2f);
        FreezeCraft(_craft);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        AlignCraftToWater(_craft);
        FreezeCraft(_craft);
        Physics.SyncTransforms();
        PlacePlayerOnCraft(_craft);
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForFixedUpdate();
            FreezeCraft(_craft);
            if (i == 1 || i == 5 || i == 9)
                PlacePlayerOnCraft(_craft);
        }
        BoatPiece.BeginLaunchSettle(1.45f);
        EnterRace();
        if (_player != null)
            _player.MovementLocked = true;
        BoatWater.CurrentEnabled = false;
        int hold = Mathf.Clamp(Mathf.RoundToInt(launchHold * 50f), 16, 36);
        for (int i = 0; i < hold; i++)
        {
            yield return new WaitForFixedUpdate();
            SoftCraft(_craft);
            PlacePlayerOnCraft(_craft);
        }
        BoatWater.CurrentEnabled = true;
        if (_player != null)
        {
            _player.MovementLocked = false;
            _player.ArmLaunchSeat(2.6f);
        }
        yield return _hud.PlayLaunchIn();
    }

    void EnterRace()
    {
        _phase = Phase.Race;
        BoatWater.CurrentEnabled = true;
        _raceStartedAt = Time.unscaledTime;
        _endsAt = _raceStartedAt + raceSeconds;
        _offBoatSince = -1f;
        _spawnProtectUntil = Time.unscaledTime + 2.4f;
        _hud.SetTimer(raceSeconds, false);
        if (_hud != null)
            _hud.ClearHurt();
        if (_actor != null)
            _actor.Craft = _craft;
        if (_sharks != null)
            _sharks.Arm(RaceSim.RoundSeed);
        StartCoroutine(BirdRaids());
    }

    IEnumerator BirdRaids()
    {
        yield return new WaitForSeconds(11f);
        while (_phase == Phase.Race && !_closing && !_ended)
        {
            FollowPlayerCraft();
            if (_craft != null)
            {
                HouseBombBird.SpawnOnBoat(_craft);
                if (_hud != null)
                    _hud.FlashWarn("BIRD", "Eggs inbound.");
            }
            yield return new WaitForSeconds(Random.Range(18f, 28f));
        }
    }

    void TickRace()
    {
        FollowPlayerCraft();

        float elapsed = Time.unscaledTime - _raceStartedAt;
        RaceSim.RaceElapsed = elapsed;
        if (_actor != null)
            _actor.Craft = _craft;
        if (_sharks != null)
            _sharks.Tick(elapsed);
        if (_hud != null)
        {
            if (_sharks != null)
            {
                _sharks.GetHudPair(out RiverShark a, out RiverShark b);
                _hud.SetShark(a, b);
            }
            else
                _hud.SetShark(null);
        }

        float left = Mathf.Max(0f, _endsAt - Time.unscaledTime);
        float dist = DistanceToFinish();
        float hull = _craft != null ? Mathf.Clamp01(_craft.HullStrength) : 0f;
        float flood = _craft != null ? Mathf.Clamp01(_craft.HullFlood) : 0f;
        _hud.SetTimer(left, left < 20f);
        bool onBoat = OnBoat();
        _hud.SetRaceStatus(dist, hull, flood, onBoat);

        if (ReachedFinish())
        {
            Win(left);
            return;
        }

        if (left <= 0f)
        {
            Fail("Time ran out");
            return;
        }

        bool inWater = PlayerInWater();
        bool sunk = CraftFullySunk(_craft);
        if (sunk && inWater && !onBoat)
        {
            if (_sunkSince < 0f)
                _sunkSince = Time.unscaledTime;
            bool atSurface = _player != null
                && BoatWater.TryHeight(_player.transform.position, out float waterY)
                && _player.transform.position.y > waterY - 0.7f;
            if (atSurface || Time.unscaledTime - _sunkSince >= 2.5f)
                Fail("The boat sank");
            return;
        }
        _sunkSince = -1f;

        if (onBoat)
        {
            _offBoatSince = -1f;
            return;
        }

        bool jumpGrace = _player != null && _player.AirborneNow && !inWater && !_player.IsSwimming;
        if (jumpGrace)
        {
            _offBoatSince = -1f;
            return;
        }

        if (Time.unscaledTime < _spawnProtectUntil)
        {
            _offBoatSince = -1f;
            return;
        }

        if (_offBoatSince < 0f)
            _offBoatSince = Time.unscaledTime;
        if (Time.unscaledTime - _offBoatSince >= fallGrace)
        {
            if (_hud != null)
                _hud.PulseHurt(0.4f);
            Fail(inWater ? "You fell from the boat" : "You left the boat");
        }
    }

    public void NotifyFinish()
    {
        if (_ended || _closing || _phase != Phase.Race)
            return;
        if (!OnBoat())
            return;
        Win(Mathf.Max(0f, _endsAt - Time.unscaledTime));
    }

    void Win(float timeLeft)
    {
        if (_ended || _closing)
            return;
        float elapsed = Time.unscaledTime - _raceStartedAt;
        int pieces = BoatHull.HullPieceCount(_craft);
        float hull = _craft != null ? Mathf.Clamp01(_craft.HullStrength) : 0f;
        float flood = _craft != null ? Mathf.Clamp01(_craft.HullFlood) : 1f;
        int timePts = Mathf.RoundToInt(timeLeft * 8f);
        int hullPts = Mathf.RoundToInt(hull * 420f);
        int dryPts = Mathf.RoundToInt((1f - flood) * 180f);
        int piecePts = pieces * 12;
        int arrival = 500;
        int stylePts = KillStyle.RoundPoints;
        _score = arrival + timePts + hullPts + dryPts + piecePts + stylePts;
        var lines = new List<string>
        {
            "Arrival                +500",
            $"Time left              +{timePts}",
            $"Hull                   +{hullPts}",
            $"Dry hold               +{dryPts}",
            $"Pieces                 +{piecePts}"
        };
        if (stylePts > 0)
        {
            lines.Add($"Shark style            +{stylePts}");
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < KillStyle.RoundTags.Count; i++)
            {
                var tag = KillStyle.RoundTags[i];
                if (!seen.ContainsKey(tag.name))
                    seen[tag.name] = 0;
                seen[tag.name]++;
            }
            foreach (var kv in seen)
                lines.Add($"  {kv.Key}  x{kv.Value}");
        }
        lines.Add("");
        lines.Add($"Water time             {Fmt(elapsed)}");
        _won = true;
        EndRound(true, "You made the mark", lines.ToArray());
    }

    void Fail(string reason)
    {
        if (_ended || _closing)
            return;
        _failReason = reason;
        _score = 0;
        _won = false;
        EndRound(false, reason, new[] { "The river keeps what it takes." });
    }

    void EndRound(bool win, string title, string[] lines)
    {
        if (_closing || _ended)
            return;
        _closing = true;
        _phase = Phase.Results;
        if (win && _sharks != null)
            _sharks.Clear();
        if (_player != null)
        {
            if (win)
                _player.MovementLocked = true;
            var inv = _player.GetComponent<PlayerInventory>();
            if (inv != null)
                inv.Holster();
        }
        StartCoroutine(CloseRound(win, title, lines));
    }

    IEnumerator CloseRound(bool win, string title, string[] lines)
    {
        if (win)
        {
            float from = Time.timeScale;
            float to = 0.35f;
            float t = 0f;
            float dur = 1.1f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / dur;
                Time.timeScale = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
                yield return null;
            }
        }
        yield return _hud.PlayEnd(win, title, _score, lines, restartKey);
        if (win)
            Time.timeScale = 0f;
        _ended = true;
    }

    bool RestartPressed()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return false;
        if (System.Enum.TryParse(restartKey.ToString(), true, out Key key) && key != Key.None)
            return kb[key].wasPressedThisFrame;
        return kb.rKey.wasPressedThisFrame;
    }

    void RestartScene()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void FollowPlayerCraft()
    {
        if (_player == null)
            return;
        var hull = _player.HullUnderFeet();
        if (hull == null)
            return;
        var lead = hull.IslandLeader() ?? hull;
        if (lead != null)
            _craft = lead;
    }

    public void BlastCraft(Vector3 at, float power)
    {
        if (_phase != Phase.Race || _craft == null || _closing || _ended)
            return;
        power = Mathf.Clamp01(power);
        var lead = _craft.IslandLeader() ?? _craft;
        lead.HullFlood = Mathf.Clamp01(lead.HullFlood + 0.045f + power * 0.07f);
        var buf = new List<BoatPiece>(24);
        lead.CollectIsland(buf);
        for (int i = 0; i < buf.Count; i++)
        {
            var p = buf[i];
            if (p == null)
                continue;
            p.WakeForWater();
            var rb = p.IslandRootBody() ?? p.Body;
            if (rb == null || rb.isKinematic)
                continue;
            Vector3 d = rb.worldCenterOfMass - at;
            float dist = Mathf.Max(0.35f, d.magnitude);
            if (dist > 6.5f)
                continue;
            float fall = 1f - dist / 6.5f;
            rb.WakeUp();
            rb.AddForce(d.normalized * (2.4f * fall * power) + Vector3.up * (1.15f * fall * power), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * (1.4f * fall * power), ForceMode.VelocityChange);
        }
        if (_hud != null)
            _hud.PulseHurt(0.1f + power * 0.08f);
    }

    bool OnBoat()
    {
        if (_player == null)
            return false;
        if (_player.IsOnCraft)
            return true;
        var hull = _player.HullUnderFeet();
        if (hull == null)
            return false;
        return !hull.DeckSubmerged();
    }

    bool PlayerInWater()
    {
        if (_player == null)
            return false;
        if (_player.IsSwimming)
            return true;
        Vector3 pos = _player.transform.position;
        return BoatWater.TryHeight(pos, out float waterY) && pos.y < waterY - 0.08f;
    }

    static bool CraftFullySunk(BoatPiece lead)
    {
        if (lead == null)
            return true;
        lead.CollectIsland(ScratchCraft);
        int hull = 0;
        int sunk = 0;
        for (int i = 0; i < ScratchCraft.Count; i++)
        {
            var p = ScratchCraft[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            hull++;
            Vector3 pos = p.transform.position;
            if (!BoatWater.TryHeight(pos, out float waterY) || pos.y < waterY - 0.9f)
                sunk++;
        }
        return hull == 0 || sunk >= hull;
    }

    static readonly List<BoatPiece> ScratchCraft = new List<BoatPiece>(32);

    bool ReachedFinish()
    {
        if (finish == null || _player == null)
            return false;
        if (!OnBoat())
            return false;
        return DistanceToFinish() <= finishRadius;
    }

    float DistanceToFinish()
    {
        if (finish == null)
            return 0f;
        Vector3 from = _player != null ? _player.transform.position : transform.position;
        Vector3 a = from;
        Vector3 b = finish.position;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    BoatPiece FindAssembledBoat()
    {
        var pieces = FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        BoatPiece best = null;
        int bestN = 1;
        var buf = new List<BoatPiece>(24);
        var seen = new HashSet<BoatPiece>();
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p == null || !p.isActiveAndEnabled)
                continue;
            if (p.GetComponentInParent<HorrorFirstPersonController>() != null)
                continue;
            var lead = p.IslandLeader() ?? p;
            if (!seen.Add(lead))
                continue;
            int n = BoatHull.HullBodyCount(lead);
            if (n > bestN)
            {
                bestN = n;
                best = lead;
            }
        }
        return best;
    }

    BoatPiece FindLoneFloater()
    {
        var pieces = FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        BoatPiece best = null;
        int bestScore = -1;
        float bestD = float.PositiveInfinity;
        Vector3 origin = _player != null ? _player.transform.position
            : (playerSpawn != null ? playerSpawn.position : transform.position);
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p == null || !p.isActiveAndEnabled)
                continue;
            if (p.GetComponentInParent<HorrorFirstPersonController>() != null)
                continue;
            if (p.Kind != BoatPieceKind.Plank && p.Kind != BoatPieceKind.Log && p.Kind != BoatPieceKind.Barrel)
                continue;
            if (BoatHull.HullBodyCount(p) != 1)
                continue;
            int score = p.Kind == BoatPieceKind.Log ? 3 : p.Kind == BoatPieceKind.Barrel ? 2 : 1;
            float d = (p.transform.position - origin).sqrMagnitude;
            if (score > bestScore || (score == bestScore && d < bestD))
            {
                bestScore = score;
                bestD = d;
                best = p;
            }
        }
        return best;
    }

    BoatPiece SpawnFloater()
    {
        GameObject prefab = LootPrefab("Log") ?? LootPrefab("Plank") ?? LootPrefab("Barrel");
        if (prefab == null)
            return null;
        Vector3 pos = waterLaunch != null ? waterLaunch.position
            : (playerSpawn != null ? playerSpawn.position : transform.position);
        if (BoatWater.TryHeight(pos, out float y))
            pos.y = y + 0.38f;
        else
            pos.y += 0.4f;
        Vector3 look = Flatten(waterLaunch != null ? waterLaunch.forward : Vector3.forward);
        var go = Instantiate(prefab, pos, Quaternion.LookRotation(look));
        go.name = prefab.name;
        var mat = go.GetComponent<BoatMaterialItem>();
        BoatPiece piece = mat != null ? mat.TryBecomeWorldPiece() : go.GetComponent<BoatPiece>();
        if (piece != null)
            piece.WakeInWorld(true);
        return piece;
    }

    GameObject LootPrefab(string part)
    {
        if (lootPrefabs == null)
            return null;
        for (int i = 0; i < lootPrefabs.Length; i++)
        {
            var p = lootPrefabs[i];
            if (p != null && p.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return p;
        }
        return null;
    }

    void ClearStartIsland(BoatPiece keep)
    {
        var keepSet = new HashSet<BoatPiece>();
        if (keep != null)
        {
            keep.CollectIsland(ScratchCraft);
            for (int i = 0; i < ScratchCraft.Count; i++)
            {
                if (ScratchCraft[i] != null)
                    keepSet.Add(ScratchCraft[i]);
            }
        }

        float r = Mathf.Max(22f, scatterRadius + 6f);
        Vector3 origin = playerSpawn != null ? playerSpawn.position : transform.position;

        var pieces = FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p == null || keepSet.Contains(p))
                continue;
            if (IsPlayerOwned(p.transform))
                continue;
            if (!NearSpawn(p.transform.position, origin, r))
                continue;
            Destroy(p.gameObject);
        }

        var items = FindObjectsByType<HeldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item == null || item.IsCarried)
                continue;
            if (IsPlayerOwned(item.transform))
                continue;
            if (item.GetComponent<BoatPiece>() != null && keepSet.Contains(item.GetComponent<BoatPiece>()))
                continue;
            if (!NearSpawn(item.transform.position, origin, r))
                continue;
            Destroy(item.gameObject);
        }

        var nails = FindObjectsByType<BoatNail>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < nails.Length; i++)
        {
            var n = nails[i];
            if (n == null)
                continue;
            if (keepSet.Contains(n.A) || keepSet.Contains(n.B))
                continue;
            if (IsPlayerOwned(n.transform))
                continue;
            if (!NearSpawn(n.transform.position, origin, r))
                continue;
            Destroy(n.gameObject);
        }

        for (int i = 0; i < _spawnedLoot.Count; i++)
        {
            var go = _spawnedLoot[i];
            if (go == null)
                continue;
            var p = go.GetComponent<BoatPiece>();
            if (p != null && keepSet.Contains(p))
                continue;
            if (IsPlayerOwned(go.transform))
                continue;
            Destroy(go);
        }
        _spawnedLoot.Clear();
        if (keep != null)
            _spawnedLoot.Add(keep.gameObject);
    }

    static bool NearSpawn(Vector3 pos, Vector3 origin, float r)
    {
        Vector3 d = pos - origin;
        d.y = 0f;
        return d.sqrMagnitude <= r * r;
    }

    static bool IsPlayerOwned(Transform t)
    {
        if (t == null)
            return false;
        return t.GetComponentInParent<HorrorFirstPersonController>() != null
            || t.GetComponentInParent<PlayerInventory>() != null;
    }

    void TeleportCraftToWater(BoatPiece lead)
    {
        if (lead == null || waterLaunch == null)
            return;
        var island = new List<BoatPiece>(24);
        lead.CollectIsland(island);
        Vector3 look = Flatten(waterLaunch.forward);
        Vector3 centroid = Vector3.zero;
        int n = 0;
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] == null)
                continue;
            centroid += island[i].transform.position;
            n++;
        }
        if (n == 0)
            return;
        centroid /= n;
        Vector3 target = waterLaunch.position;
        if (BoatWater.TryHeight(target, out float waterY))
            target.y = waterY + 0.32f;
        Vector3 offset = target - centroid;
        Quaternion rot = Quaternion.FromToRotation(Flatten(lead.transform.forward), look);
        var moved = new HashSet<Rigidbody>();
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            var body = p.Body;
            if (body != null)
            {
                if (!moved.Add(body))
                    continue;
                body.position += offset;
                body.rotation = rot * body.rotation;
                BoatBuildUtil.StopMotion(body);
            }
            else if (p.transform.parent == null)
            {
                p.transform.position += offset;
                p.transform.rotation = rot * p.transform.rotation;
            }
        }
        Physics.SyncTransforms();
        AlignCraftToWater(lead);
        lead.HullFlood = Mathf.Min(lead.HullFlood, 0.08f);
        BoatOarStation.Abort();
    }

    void AlignCraftToWater(BoatPiece lead)
    {
        if (lead == null)
            return;
        var island = new List<BoatPiece>(24);
        lead.CollectIsland(island);
        Bounds hull = default;
        bool any = false;
        Vector3 mid = Vector3.zero;
        int n = 0;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            if (!p.TrySolidBounds(out Bounds b))
                continue;
            hull = any ? Encapsulate(hull, b) : b;
            any = true;
            mid += b.center;
            n++;
        }
        if (!any)
            return;
        mid /= n;
        if (!BoatWater.TryHeight(mid, out float waterY))
            return;
        float keel = hull.min.y;
        float deck = hull.max.y;
        float thick = Mathf.Max(0.08f, deck - keel);
        float wantKeel = waterY - Mathf.Clamp(thick * 0.12f, 0.02f, 0.07f);
        float dy = wantKeel - keel;
        if (deck + dy < waterY + 0.34f)
            dy = waterY + 0.34f - deck;
        if (Mathf.Abs(dy) < 0.004f)
            return;
        ShiftCraft(island, new Vector3(0f, dy, 0f));
        Physics.SyncTransforms();
    }

    static Bounds Encapsulate(Bounds a, Bounds b)
    {
        a.Encapsulate(b.min);
        a.Encapsulate(b.max);
        return a;
    }

    void FreezeCraft(BoatPiece lead)
    {
        if (lead == null)
            return;
        var island = new List<BoatPiece>(24);
        lead.CollectIsland(island);
        for (int i = 0; i < island.Count; i++)
        {
            var rb = island[i] != null ? island[i].Body : null;
            if (rb == null)
                continue;
            BoatBuildUtil.StopMotion(rb);
            rb.isKinematic = true;
            rb.maxDepenetrationVelocity = 0.15f;
        }
    }

    void SoftCraft(BoatPiece lead)
    {
        if (lead == null)
            return;
        var island = new List<BoatPiece>(24);
        lead.CollectIsland(island);
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            p.WakeForWater();
            var rb = p.Body;
            if (rb == null || rb.isKinematic)
                continue;
            rb.maxDepenetrationVelocity = 0.18f;
            Vector3 v = rb.linearVelocity;
            v.y = Mathf.Clamp(v.y, -0.35f, 0.28f);
            v.x *= 0.72f;
            v.z *= 0.72f;
            rb.linearVelocity = v;
            rb.angularVelocity *= 0.35f;
        }
    }

    static void ShiftCraft(List<BoatPiece> island, Vector3 offset)
    {
        var moved = new HashSet<Rigidbody>();
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            var body = p.Body;
            if (body != null)
            {
                if (!moved.Add(body))
                    continue;
                bool kin = body.isKinematic;
                BoatBuildUtil.StopMotion(body);
                body.isKinematic = true;
                body.position += offset;
                body.isKinematic = kin;
            }
            else if (p.transform.parent == null)
                p.transform.position += offset;
        }
    }

    static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    void PlacePlayerOnCraft(BoatPiece lead)
    {
        if (_player == null || lead == null)
            return;
        _player.SnapOntoHull(lead);
        Vector3 look = Flatten(finish != null ? finish.position - _player.transform.position : _player.transform.forward);
        _player.transform.rotation = Quaternion.LookRotation(look);
        _player.SnapOntoHull(lead);
    }

    void PlacePlayerAtSpawn()
    {
        if (_player == null)
            return;
        Vector3 pos = playerSpawn != null ? playerSpawn.position : _player.transform.position;
        Quaternion rot = playerSpawn != null ? playerSpawn.rotation : _player.transform.rotation;
        var cc = _player.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;
        _player.transform.SetPositionAndRotation(pos, rot);
        if (cc != null)
            cc.enabled = true;
        _player.ReleaseBoatFollow();
    }

    void ScatterLoot()
    {
        ScatterSupplies(false, 0);
    }

    public void ScatterSupplies(bool skipOar, int minPlanks)
    {
        Vector3 origin = playerSpawn != null ? playerSpawn.position : Vector3.zero;
        Vector3 fwd = playerSpawn != null ? Flatten(playerSpawn.forward) : Vector3.forward;
        ScatterSupplies(origin, fwd, skipOar, minPlanks);
    }

    public void ScatterSupplies(Vector3 origin, Vector3 fwd, bool skipOar, int minPlanks)
    {
        for (int i = 0; i < _spawnedLoot.Count; i++)
        {
            var go = _spawnedLoot[i];
            if (go == null || IsLootSource(go))
                continue;
            DestroyImmediate(go);
        }
        _spawnedLoot.Clear();
        if (!scatterLoot || lootPrefabs == null || lootPrefabs.Length == 0)
            return;

        fwd = Flatten(fwd);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        right.Normalize();
        Vector3 pile = origin + fwd * 2.6f;
        pile.y = GroundY(pile + Vector3.up * 80f, origin.y);

        for (int i = 0; i < lootPrefabs.Length; i++)
        {
            var prefab = lootPrefabs[i];
            if (prefab == null)
                continue;
            string name = prefab.name;
            if (skipOar && name.IndexOf("Oar", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            int count = 1;
            if (lootCounts != null && i < lootCounts.Length)
                count = Mathf.Max(0, lootCounts[i]);
            if (name.IndexOf("Plank", System.StringComparison.OrdinalIgnoreCase) >= 0)
                count = Mathf.Max(count, minPlanks);
            if (count <= 0)
                continue;
            if (name.IndexOf("Plank", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayPlanks(prefab, count, pile, right, fwd);
            else if (name.IndexOf("Log", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayLogs(prefab, count, pile + right * 1.15f, right, fwd);
            else if (name.IndexOf("Barrel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayBarrels(prefab, count, pile + right * -1.35f, right, fwd);
            else if (name.IndexOf("Nail", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayGrid(prefab, count, pile + fwd * 1.15f + right * 0.15f, right, fwd, 6, 0.13f, 0.09f);
            else if (name.IndexOf("Rope", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayGrid(prefab, count, pile + fwd * 1.15f + right * -0.85f, right, fwd, 5, 0.22f, 0.13f);
            else if (name.IndexOf("Hammer", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayRow(prefab, count, pile + fwd * -0.85f + right * 0.35f, right, 0.35f, Quaternion.LookRotation(fwd));
            else if (name.IndexOf("Saw", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayRow(prefab, count, pile + fwd * -0.85f + right * -0.15f, right, 0.35f, Quaternion.LookRotation(fwd));
            else if (name.IndexOf("Pistol", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayRow(prefab, count, pile + fwd * -0.85f + right * 0.75f, right, 0.32f, Quaternion.LookRotation(fwd));
            else if (name.IndexOf("Oar", System.StringComparison.OrdinalIgnoreCase) >= 0)
                LayRow(prefab, count, pile + right * -2.05f, fwd, 0.28f, Quaternion.LookRotation(right));
            else
                LayGrid(prefab, count, pile + fwd * 1.6f, right, fwd, 4, 0.4f, 0.12f);
        }
    }

    void LayPlanks(GameObject prefab, int count, Vector3 origin, Vector3 right, Vector3 fwd)
    {
        Quaternion rot = Quaternion.LookRotation(fwd);
        int cols = 3;
        int per = Mathf.CeilToInt(count / (float)cols);
        for (int i = 0; i < count; i++)
        {
            int col = i / per;
            int layer = i % per;
            Vector3 p = origin + right * ((col - 1) * 0.3f) + Vector3.up * (0.048f * layer + 0.03f);
            SpawnSettled(prefab, p, rot);
        }
    }

    void LayLogs(GameObject prefab, int count, Vector3 origin, Vector3 right, Vector3 fwd)
    {
        Quaternion rot = Quaternion.LookRotation(fwd);
        int row = Mathf.CeilToInt(count * 0.5f);
        for (int i = 0; i < count; i++)
        {
            int layer = i / row;
            int slot = i % row;
            float along = (slot - (row - 1) * 0.5f) * 0.34f;
            Vector3 p = origin + right * along + Vector3.up * (0.29f * layer + 0.15f);
            SpawnSettled(prefab, p, rot);
        }
    }

    void LayBarrels(GameObject prefab, int count, Vector3 origin, Vector3 right, Vector3 fwd)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 p = origin + right * ((i - (count - 1) * 0.5f) * 0.62f) + Vector3.up * 0.32f;
            SpawnSettled(prefab, p, Quaternion.LookRotation(fwd));
        }
    }

    void LayGrid(GameObject prefab, int count, Vector3 origin, Vector3 right, Vector3 fwd, int cols, float space, float y)
    {
        for (int i = 0; i < count; i++)
        {
            int c = i % cols;
            int r = i / cols;
            Vector3 p = origin + right * ((c - (cols - 1) * 0.5f) * space) + fwd * (r * space) + Vector3.up * y;
            SpawnSettled(prefab, p, Quaternion.LookRotation(fwd));
        }
    }

    void LayRow(GameObject prefab, int count, Vector3 origin, Vector3 along, float space, Quaternion rot)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 p = origin + along * ((i - (count - 1) * 0.5f) * space) + Vector3.up * 0.08f;
            SpawnSettled(prefab, p, rot);
        }
    }

    void SpawnSettled(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        var go = Instantiate(prefab, pos, rot);
        go.name = prefab.name;
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            float ground = GroundY(pos + Vector3.up * 3f, pos.y);
            float bottom = col.bounds.min.y;
            if (bottom < ground + 0.01f)
                go.transform.position += Vector3.up * (ground + 0.01f - bottom);
        }
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
            BoatBuildUtil.StopMotion(rb);
        _spawnedLoot.Add(go);
    }

    static float GroundY(Vector3 from, float fallback)
    {
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.GetComponentInParent<BoatWater>() == null)
                return hit.point.y;
        }
        return fallback;
    }

    void EnsureFinishTrigger()
    {
        if (finish == null)
            return;
        var col = finish.GetComponent<SphereCollider>();
        if (col == null)
            col = finish.gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = finishRadius;
        if (finish.GetComponent<BoatRaceFinish>() == null)
            finish.gameObject.AddComponent<BoatRaceFinish>();
        var rb = finish.GetComponent<Rigidbody>();
        if (rb == null)
            rb = finish.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    static string Fmt(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m:00}:{s:00}";
    }

    void OnDrawGizmos()
    {
        if (playerSpawn != null)
        {
            Gizmos.color = new Color(0.85f, 0.75f, 0.45f, 0.8f);
            Gizmos.DrawWireSphere(playerSpawn.position, 0.45f);
        }
        if (waterLaunch != null)
        {
            Gizmos.color = new Color(0.25f, 0.45f, 0.75f, 0.85f);
            Gizmos.DrawWireCube(waterLaunch.position, new Vector3(4.2f, 0.4f, 6.5f));
        }
        if (finish != null)
        {
            Gizmos.color = new Color(0.75f, 0.15f, 0.12f, 0.7f);
            Gizmos.DrawWireSphere(finish.position, finishRadius);
        }
        if (waterLaunch != null && finish != null)
        {
            Gizmos.color = new Color(0.55f, 0.2f, 0.15f, 0.55f);
            Gizmos.DrawLine(waterLaunch.position, finish.position);
        }
    }
}
