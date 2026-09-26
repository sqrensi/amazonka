using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Набросок режима дома: стройка по таймеру, затем удержание. Без реки и акул.
/// </summary>
[DefaultExecutionOrder(-39)]
public class HouseBuildMode : MonoBehaviour
{
    public enum Phase
    {
        Intro,
        Build,
        Hold,
        Results
    }

    public static HouseBuildMode Current { get; private set; }
    public static bool Quaking { get; private set; }
    public static float MoveScale { get; private set; } = 1f;
    public static Vector3 GroundLurch { get; private set; }

    [SerializeField] float buildSeconds = 150f;
    [SerializeField] float holdSeconds = 90f;
    [SerializeField] KeyCode restartKey = KeyCode.R;

    Phase _phase = Phase.Intro;
    float _endsAt;
    bool _ended;
    bool _closing;
    bool _won;
    int _score;
    float _hp = 1f;
    int _startParts;
    int _startNails;
    float _startVolume;
    BoatPiece _house;
    HorrorFirstPersonController _player;
    BoatRaceHud _hud;
    readonly List<GameObject> _loot = new List<GameObject>(64);
    readonly List<BoatPiece> _buf = new List<BoatPiece>(32);
    Vector3 _plot;
    Vector3 _plotFwd = Vector3.forward;
    float _scanAt;
    readonly List<float> _quakeAt = new List<float>(4);
    readonly List<float> _quakePow = new List<float>(4);
    bool _shaking;
    int _quakePhase;
    HouseQuakeCam _shake;
    readonly List<HouseRockSpawner> _rockSpawners = new List<HouseRockSpawner>(6);
    float _rockUntil;
    float _outsideSince = -1f;
    bool _hadRoof;

    public bool InHold => _phase == Phase.Hold && !_ended && !_closing;
    public static bool Holding => Current != null && Current.InHold;
    public Vector3 HoldOrigin => _house != null ? _house.transform.position : _plot;
    public bool HandsLocked => _closing || _ended || _phase == Phase.Intro || _phase == Phase.Results;

    void Start()
    {
        if (!PlaySession.IsHouseScene)
            return;
        if (PlaySession.Active == PlaySession.Mode.None)
            PlaySession.Choose(PlaySession.Mode.HouseHold);
        if (PlaySession.Active == PlaySession.Mode.HouseHold)
            BeginRound();
    }

    BoatRaceHud BindHud()
    {
        var hud = GetComponent<BoatRaceHud>();
        if (hud == null)
            hud = Object.FindFirstObjectByType<BoatRaceHud>(FindObjectsInactive.Include);
        if (hud == null)
            hud = gameObject.AddComponent<BoatRaceHud>();
        hud.enabled = true;
        if (!hud.gameObject.activeSelf)
            hud.gameObject.SetActive(true);
        hud.EnsureBuilt();
        return hud;
    }

    public void BeginRound()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        Current = this;
        _ended = false;
        _closing = false;
        _won = false;
        _score = 0;
        _hp = 1f;
        _house = null;
        _shaking = false;
        _quakePhase = 0;
        Quaking = false;
        MoveScale = 1f;
        _outsideSince = -1f;
        _hadRoof = false;
        _phase = Phase.Intro;
        _endsAt = Time.unscaledTime + 10000f;
        _quakeAt.Clear();
        _quakePow.Clear();
        if (_shake != null)
            _shake.StopShake();
        BoatWater.CurrentEnabled = false;
        HideRiver(true);
        ClearRocks();
        _hud = BindHud();
        if (_hud == null)
        {
            Debug.LogError("[House] HUD missing");
            return;
        }
        _hud.PrepareNewRound();
        RaceSim.BeginRound(unchecked(31 * 397 + (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff)));
        try
        {
            RaceMood.ApplyRound(RaceSim.RoundSeed);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[House] mood failed: " + e.Message);
        }
        _player = Object.FindFirstObjectByType<HorrorFirstPersonController>();
        RaceActor.BindLocal(_player);
        if (_player != null)
        {
            _player.MovementLocked = true;
            var inv = _player.GetComponent<PlayerInventory>();
            if (inv != null)
                inv.Holster();
        }
        PickPlot();
        PlacePlayer();
        ClearGround(null);
        StartCoroutine(BootLootAndIntro());
    }

    IEnumerator BootLootAndIntro()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        ScatterLoot();
        PlacePlayer();
        yield return RunIntro();
    }

    void OnDestroy()
    {
        if (Current == this)
            Current = null;
        if (_shake != null)
            _shake.StopShake();
        ClearRocks();
        HideRiver(false);
        Time.timeScale = 1f;
    }

    void Update()
    {
        if (_ended || _closing)
        {
            if (_hud != null && _hud.CanRestart && RestartPressed())
                BeginRound();
            return;
        }
        if (_phase == Phase.Build)
            TickBuild();
        else if (_phase == Phase.Hold)
            TickHold();
    }

    IEnumerator RunIntro()
    {
        _phase = Phase.Intro;
        yield return _hud.PlayIntro("THE HOUSE", "Build on land. Then keep it standing.", false);
        _phase = Phase.Build;
        if (_player != null)
            _player.MovementLocked = false;
        _endsAt = Time.unscaledTime + buildSeconds;
        _hud.SetTimer(buildSeconds, false);
        yield return _hud.PlayBuildOpen();
        _hud.SetStatus("Nail a house before the clock dies");
    }

    void TickBuild()
    {
        float left = Mathf.Max(0f, _endsAt - Time.unscaledTime);
        _hud.SetTimer(left, left < 20f);
        if (Time.unscaledTime >= _scanAt)
        {
            _scanAt = Time.unscaledTime + 0.25f;
            _house = FindHouse();
        }
        if (_house != null)
        {
            int n = BoatHull.HullBodyCount(_house);
            _hud.SetStatus($"{n} pieces   ·   nailed house");
        }
        else
            _hud.SetStatus("No house yet");
        if (left <= 0f)
            StartCoroutine(EnterHold());
    }

    IEnumerator EnterHold()
    {
        _house = FindHouse();
        if (_house == null || BoatHull.HullBodyCount(_house) < 3)
        {
            Fail("No house standing");
            yield break;
        }
        _phase = Phase.Hold;
        ClearGround(_house);
        DropHouse();
        Snapshot();
        PlanQuakes();
        CalmHouse();
        PlantRockSpawners();
        _rockUntil = Time.unscaledTime + 14f;
        _endsAt = Time.unscaledTime + holdSeconds;
        _hud.SetTimer(holdSeconds, false);
        if (_hud != null)
            _hud.FlashWarn("ROCKFALL", "Stay inside. The slope is coming down.");
        StartCoroutine(RockStorm());
        StartCoroutine(BirdRaids());
        yield return _hud.PlayHoldOpen();
    }

    void TickHold()
    {
        float left = Mathf.Max(0f, _endsAt - Time.unscaledTime);
        _hud.SetTimer(left, left < 15f);
        _house = FindHouse();
        if (_house == null)
        {
            Fail("Nothing left fastened");
            return;
        }
        KeepPlayerInHouse();
        MaybeQuake();
        RefreshHp();
        if (HouseHasRoof())
            _hadRoof = true;
        else if (_hadRoof)
        {
            Fail("The roof collapsed");
            return;
        }
        int pct = Mathf.RoundToInt(_hp * 100f);
        bool inside = PlayerInHouse();
        string feel = Time.unscaledTime < _rockUntil
            ? "ROCKFALL"
            : !_shaking ? "HOLD" : _quakePhase == 1 ? "EARTHQUAKE · peak" : _quakePhase == 2 ? "EARTHQUAKE · settling" : "EARTHQUAKE";
        if (!inside)
            _hud.SetStatus($"OUTSIDE   ·   get back in   ·   integrity {pct}%", true);
        else
            _hud.SetStatus($"{feel}   ·   integrity {pct}%");
        if (!inside && FailIfOutsideTooLong())
            return;
        if (left <= 0f)
            Win();
    }

    void PlanQuakes()
    {
        _quakeAt.Clear();
        _quakePow.Clear();
        var rng = new System.Random(unchecked(RaceSim.RoundSeed * 224682251 + 19));
        float hold = Mathf.Max(24f, holdSeconds);
        float first = Time.unscaledTime + Mathf.Lerp(10f, hold * 0.38f, (float)rng.NextDouble());
        _quakeAt.Add(first);
        _quakePow.Add(Mathf.Lerp(0.72f, 1f, (float)rng.NextDouble()));
        if (rng.Next(0, 100) < 42)
        {
            _quakeAt.Add(first + Mathf.Lerp(14f, 22f, (float)rng.NextDouble()));
            _quakePow.Add(Mathf.Lerp(0.4f, 0.62f, (float)rng.NextDouble()));
        }
    }

    IEnumerator RunQuake(float power)
    {
        _shaking = true;
        Quaking = true;
        _quakePhase = 0;
        power = Mathf.Clamp01(power);
        float dur = Mathf.Lerp(13.5f, 19f, power);
        if (_hud != null)
        {
            _hud.PulseHurt(0.12f + power * 0.1f);
            _hud.FlashWarn("EARTHQUAKE", "The ground is waking. Stay on your feet.");
        }
        EnsureShakeCam();
        HouseQuakeGround.Reset();
        int nailsLeft = Mathf.Clamp(Mathf.RoundToInt(2f + power * 3f), 2, 5);
        float nextNail = dur * 0.28f;
        float t = 0f;
        while (t < dur && !_closing && _phase == Phase.Hold)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float env;
            if (u < 0.22f)
            {
                _quakePhase = 0;
                env = Mathf.SmoothStep(0f, 0.38f, u / 0.22f);
            }
            else if (u < 0.62f)
            {
                _quakePhase = 1;
                float p = (u - 0.22f) / 0.4f;
                env = 0.28f + 0.72f * Mathf.Sin(p * Mathf.PI);
            }
            else
            {
                _quakePhase = 2;
                env = Mathf.SmoothStep(0.28f, 0f, (u - 0.62f) / 0.38f);
            }
            HouseQuakeGround.SetDrive(env, power);
            MoveScale = env < 0.34f
                ? 1f
                : Mathf.Lerp(1f, 0.52f, Mathf.InverseLerp(0.34f, 1f, env));
            if (_shake != null)
                _shake.SetLevel(env * (0.75f + power * 0.4f));
            if (nailsLeft > 0 && t >= nextNail && _quakePhase == 1)
            {
                TearHouseBit();
                nailsLeft--;
                nextNail += Mathf.Lerp(1.5f, 2.4f, (float)Random.value);
            }
            if (FindHouse() == null)
            {
                Fail("Nothing left fastened");
                yield break;
            }
            yield return null;
        }
        _quakePhase = 0;
        _shaking = false;
        Quaking = false;
        MoveScale = 1f;
        GroundLurch = Vector3.zero;
        HouseQuakeGround.Reset();
        if (_shake != null)
            _shake.StopShake();
        CalmHouse();
    }

    void FixedUpdate()
    {
        if (!_shaking || _closing)
        {
            GroundLurch = Vector3.zero;
            return;
        }
        HouseQuakeGround.Tick(Time.fixedDeltaTime);
        GroundLurch = HouseQuakeGround.Delta;
        if (_player != null)
            _player.ApplyGroundLurch(HouseQuakeGround.Delta);
        DriveFromGround(HouseQuakeGround.Acc);
    }

    void DriveFromGround(Vector3 acc)
    {
        if (acc.sqrMagnitude < 0.01f)
            return;
        acc = Vector3.ClampMagnitude(acc, 13f);
        var pieces = Object.FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var seen = new HashSet<Rigidbody>();
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p == null || p.IsWeldSlave)
                continue;
            if (p.GetComponentInParent<HorrorFirstPersonController>() != null)
                continue;
            var rb = p.IslandRootBody() ?? p.Body;
            if (rb == null || rb.isKinematic || !seen.Add(rb))
                continue;
            bool locked = p.HasDrivenNail() || p.IsLockedInBoat();
            float couple = locked ? 0.7f : 0.95f;
            rb.WakeUp();
            rb.AddForce(acc * couple, ForceMode.Acceleration);
            rb.AddTorque(new Vector3(acc.z, 0f, -acc.x) * (couple * 0.07f), ForceMode.Acceleration);
        }
    }

    void EnsureShakeCam()
    {
        if (_player == null || _player.PlayerCam == null)
            return;
        if (_shake == null)
            _shake = _player.PlayerCam.GetComponent<HouseQuakeCam>();
        if (_shake == null)
            _shake = _player.PlayerCam.gameObject.AddComponent<HouseQuakeCam>();
        _shake.Play();
    }

    void TearHouseBit()
    {
        _house = FindHouse();
        if (_house == null)
            return;
        _house.CollectIsland(_buf);
        var nails = new List<BoatNail>(16);
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null)
                continue;
            var list = p.Nails;
            for (int n = 0; n < list.Count; n++)
            {
                var nail = list[n];
                if (nail != null && nail.Driven && !nails.Contains(nail))
                    nails.Add(nail);
            }
        }
        if (nails.Count <= 1)
            return;

        BoatNail pick = nails[0];
        float bestY = float.NegativeInfinity;
        for (int i = 0; i < nails.Count; i++)
        {
            float y = nails[i].transform.position.y + Random.Range(0f, 0.35f);
            if (y > bestY)
            {
                bestY = y;
                pick = nails[i];
            }
        }

        BoatPiece a = pick.A;
        BoatPiece b = pick.B;
        Vector3 seam = pick.transform.position;
        GameObject nailGo = pick.gameObject;
        pick.DropLoose();
        var nrb = nailGo != null ? nailGo.GetComponent<Rigidbody>() : null;
        if (nrb != null)
        {
            nrb.AddForce(Random.onUnitSphere * 3.4f + Vector3.up * 2.2f, ForceMode.VelocityChange);
            nrb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
        }
        if (a != null)
            BoatIsland.Refresh(a);
        if (b != null && b != a)
            BoatIsland.Refresh(b);
        HouseQuakeDust.Burst(seam);
        ShoveTorn(a, seam);
        ShoveTorn(b, seam);
        Hurt(0.055f);
    }

    public void BlastHouse(Vector3 at, float power)
    {
        power = Mathf.Clamp01(power);
        Hurt(0.045f + power * 0.07f);
        TearHouseBit();
        if (power > 0.55f)
            TearHouseBit();
        _house = FindHouse();
        if (_house == null)
            return;
        _house.CollectIsland(_buf);
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null)
                continue;
            p.WakeForWater();
            var rb = p.IslandRootBody() ?? p.Body;
            if (rb == null || rb.isKinematic)
                continue;
            Vector3 d = rb.worldCenterOfMass - at;
            float dist = Mathf.Max(0.4f, d.magnitude);
            if (dist > 7.5f)
                continue;
            float fall = 1f - dist / 7.5f;
            rb.WakeUp();
            rb.AddForce(d.normalized * (3.1f * fall * power) + Vector3.up * (1.6f * fall * power), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * (1.8f * fall * power), ForceMode.VelocityChange);
        }
    }

    IEnumerator BirdRaids()
    {
        yield return new WaitForSeconds(9f);
        while (InHold)
        {
            HouseBombBird.SpawnPass(HoldOrigin);
            if (_hud != null)
                _hud.FlashWarn("BIRD", "Eggs inbound.");
            yield return new WaitForSeconds(Random.Range(16f, 26f));
        }
    }

    void ShoveTorn(BoatPiece p, Vector3 from)
    {
        if (p == null)
            return;
        p.WakeForWater();
        p.CollectIsland(_buf);
        if (_buf.Count >= 5)
            return;
        var rb = p.IslandRootBody() ?? p.Body;
        if (rb == null || rb.isKinematic)
            return;
        Vector3 away = rb.worldCenterOfMass - from;
        away.y = Mathf.Max(0.15f, away.y);
        if (away.sqrMagnitude < 0.04f)
            away = Vector3.up + Random.insideUnitSphere * 0.4f;
        away.Normalize();
        rb.WakeUp();
        rb.AddForce(away * (2.8f + _buf.Count * 0.15f) + Vector3.up * 1.35f, ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * (2.2f / Mathf.Max(1, _buf.Count)), ForceMode.VelocityChange);
    }

    void MaybeQuake()
    {
        if (_shaking || _quakeAt.Count == 0)
            return;
        if (Time.unscaledTime < _quakeAt[0])
            return;
        float pow = _quakePow[0];
        _quakeAt.RemoveAt(0);
        _quakePow.RemoveAt(0);
        StartCoroutine(RunQuake(pow));
    }

    void Snapshot()
    {
        _house.CollectIsland(_buf);
        _startParts = CountParts(_buf);
        _startNails = CountNails(_buf);
        _startVolume = VolumeOf(_buf);
        _hp = 1f;
    }

    void RefreshHp()
    {
        if (_house == null)
        {
            _hp = 0f;
            return;
        }
        _house.CollectIsland(_buf);
        float vol = VolumeOf(_buf);
        float structure = vol / Mathf.Max(0.001f, _startVolume);
        float nails = _startNails <= 0 ? 1f : CountNails(_buf) / (float)_startNails;
        float now = Mathf.Clamp01(structure * 0.8f + nails * 0.2f);
        _hp = Mathf.Min(_hp, now);
    }

    public void Hurt(float amount)
    {
        _hp = Mathf.Clamp01(_hp - Mathf.Max(0f, amount));
    }

    void Collapse()
    {
        if (_house == null)
            return;
        _house.CollectIsland(_buf);
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null)
                continue;
            p.ResumePhysics(new Vector3(Random.Range(-1.2f, 1.2f), 1.4f, Random.Range(-1.2f, 1.2f)));
        }
    }

    void Win()
    {
        _house = FindHouse();
        if (_house == null)
        {
            Fail("Nothing left fastened");
            return;
        }
        RefreshHp();
        _house.CollectIsland(_buf);
        int parts = CountParts(_buf);
        int nails = CountNails(_buf);
        float tall = HouseHeight(_buf);
        _score = parts * 40 + nails * 8 + Mathf.RoundToInt(tall * 25f) + Mathf.RoundToInt(_hp * 180f);
        _won = true;
        End(true, "It held", new[]
        {
            $"{parts} pieces still fastened",
            $"{nails} nails",
            $"Height {tall:0.0} m",
            $"Integrity {Mathf.RoundToInt(_hp * 100f)}%"
        });
    }

    void Fail(string reason)
    {
        _score = 0;
        _won = false;
        End(false, reason, new[] { "Keep at least two pieces nailed together." });
    }

    void End(bool win, string title, string[] lines)
    {
        if (_closing || _ended)
            return;
        _closing = true;
        _shaking = false;
        Quaking = false;
        MoveScale = 1f;
        GroundLurch = Vector3.zero;
        HouseQuakeGround.Reset();
        ClearRocks();
        if (_shake != null)
            _shake.StopShake();
        _phase = Phase.Results;
        if (_player != null)
        {
            _player.MovementLocked = true;
            var inv = _player.GetComponent<PlayerInventory>();
            if (inv != null)
                inv.Holster();
        }
        StartCoroutine(Close(win, title, lines));
    }

    IEnumerator Close(bool win, string title, string[] lines)
    {
        yield return _hud.PlayEnd(win, title, _score, lines, restartKey);
        _ended = true;
    }

    bool RestartPressed()
    {
        var kb = Keyboard.current;
        return kb != null && kb.rKey.wasPressedThisFrame;
    }

    BoatPiece FindHouse()
    {
        var pieces = Object.FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        BoatPiece best = null;
        int bestN = 1;
        var seen = new HashSet<BoatPiece>();
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p == null || p.GetComponentInParent<HorrorFirstPersonController>() != null)
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

    static int CountParts(List<BoatPiece> island)
    {
        int n = 0;
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] != null && island[i].Kind != BoatPieceKind.Oar)
                n++;
        }
        return n;
    }

    static int CountNails(List<BoatPiece> island)
    {
        int n = 0;
        for (int i = 0; i < island.Count; i++)
        {
            if (island[i] == null)
                continue;
            n += island[i].GetComponentsInChildren<BoatNail>(true).Length;
        }
        return n;
    }

    static float VolumeOf(List<BoatPiece> island)
    {
        float v = 0f;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            Vector3 s = p.PieceSize;
            v += Mathf.Max(0.0001f, Mathf.Abs(s.x * s.y * s.z));
        }
        return v;
    }

    static float HouseHeight(List<BoatPiece> island)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < island.Count; i++)
        {
            var p = island[i];
            if (p == null)
                continue;
            Vector3 pos = p.transform.position;
            min = Mathf.Min(min, pos.y);
            max = Mathf.Max(max, pos.y);
        }
        return max > min ? max - min : 0f;
    }

    void DropHouse()
    {
        if (_house == null)
            return;
        _house.CollectIsland(_buf);
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null)
                continue;
            p.WakeForWater();
            var rb = p.Body;
            if (rb == null || rb.isKinematic)
                continue;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        CalmHouse();
    }

    void CalmHouse()
    {
        if (_house == null)
            return;
        _house.CollectIsland(_buf);
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null)
                continue;
            var rb = p.Body;
            if (rb == null || rb.isKinematic)
                continue;
            rb.interpolation = RigidbodyInterpolation.None;
        }
    }

    void ClearGround(BoatPiece keep)
    {
        var keepSet = new HashSet<BoatPiece>();
        if (keep != null)
        {
            keep.CollectIsland(_buf);
            for (int i = 0; i < _buf.Count; i++)
            {
                if (_buf[i] != null)
                    keepSet.Add(_buf[i]);
            }
            var frozen = new List<BoatPiece>(keepSet);
            for (int i = 0; i < frozen.Length; i++)
            {
                var p = frozen[i];
                if (p == null)
                    continue;
                var parts = p.GetComponentsInChildren<BoatPiece>(true);
                for (int k = 0; k < parts.Length; k++)
                {
                    if (parts[k] != null)
                        keepSet.Add(parts[k]);
                }
                var root = p.GetComponentInParent<BoatPiece>();
                if (root != null)
                    keepSet.Add(root);
            }
        }

        var allPieces = Object.FindObjectsByType<BoatPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (keep == null)
        {
            for (int i = 0; i < allPieces.Length; i++)
            {
                var p = allPieces[i];
                if (p == null || keepSet.Contains(p))
                    continue;
                if (Owned(p.transform))
                    continue;
                p.CollectIsland(_buf);
                bool fastened = CountParts(_buf) >= 2 || p.HasAnyNail();
                if (!fastened)
                    continue;
                for (int k = 0; k < _buf.Count; k++)
                {
                    if (_buf[k] != null)
                        keepSet.Add(_buf[k]);
                }
            }
        }

        Vector3 origin = _plot;
        bool wipeAll = keep == null;
        const float r = 28f;
        var boat = GetComponent<BoatRaceMode>();
        var pieces = allPieces;
        for (int i = 0; i < pieces.Length; i++)
        {
            var p = pieces[i];
            if (p == null || keepSet.Contains(p))
                continue;
            if (Owned(p.transform))
                continue;
            var hosted = p.GetComponentInParent<BoatPiece>();
            if (hosted != null && keepSet.Contains(hosted))
                continue;
            if (boat != null && boat.IsLootSource(p.gameObject))
                continue;
            if (!wipeAll && !Near(p.transform.position, origin, r))
                continue;
            DestroyImmediate(p.gameObject);
        }

        var items = Object.FindObjectsByType<HeldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item == null || item.IsCarried)
                continue;
            if (Owned(item.transform))
                continue;
            var piece = item.GetComponent<BoatPiece>() ?? item.GetComponentInParent<BoatPiece>();
            if (piece != null && keepSet.Contains(piece))
                continue;
            if (boat != null && boat.IsLootSource(item.gameObject))
                continue;
            if (!wipeAll && !Near(item.transform.position, origin, r))
                continue;
            DestroyImmediate(item.gameObject);
        }

        var nails = Object.FindObjectsByType<BoatNail>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < nails.Length; i++)
        {
            var n = nails[i];
            if (n == null)
                continue;
            if (keepSet.Contains(n.A) || keepSet.Contains(n.B))
                continue;
            if (Owned(n.transform))
                continue;
            if (!wipeAll && !Near(n.transform.position, origin, r))
                continue;
            DestroyImmediate(n.gameObject);
        }

        for (int i = 0; i < _loot.Count; i++)
        {
            var go = _loot[i];
            if (go == null)
                continue;
            if (boat != null && boat.IsLootSource(go))
                continue;
            var p = go.GetComponent<BoatPiece>();
            if (p != null && keepSet.Contains(p))
                continue;
            if (Owned(go.transform))
                continue;
            DestroyImmediate(go);
        }
        _loot.Clear();
    }

    static bool Near(Vector3 pos, Vector3 origin, float r)
    {
        Vector3 d = pos - origin;
        d.y = 0f;
        return d.sqrMagnitude <= r * r;
    }

    static bool Owned(Transform t)
    {
        return t != null && (t.GetComponentInParent<HorrorFirstPersonController>() != null
            || t.GetComponentInParent<PlayerInventory>() != null);
    }

    void PickPlot()
    {
        var terrain = Terrain.activeTerrain;
        var rng = new System.Random(unchecked(RaceSim.RoundSeed * 1103515245 + 12345));
        Vector3 picked = Vector3.zero;
        Vector3 fwd = Vector3.forward;
        bool ok = false;
        if (terrain != null && terrain.terrainData != null)
        {
            var data = terrain.terrainData;
            Vector3 size = data.size;
            Vector3 origin = terrain.transform.position;
            float margin = Mathf.Min(48f, size.x * 0.12f, size.z * 0.12f);
            float best = float.NegativeInfinity;
            for (int i = 0; i < 28; i++)
            {
                float nx = margin + (float)rng.NextDouble() * (size.x - margin * 2f);
                float nz = margin + (float)rng.NextDouble() * (size.z - margin * 2f);
                Vector3 p = origin + new Vector3(nx, 0f, nz);
                p.y = terrain.SampleHeight(p) + origin.y;
                float u = Mathf.Clamp01(nx / size.x);
                float v = Mathf.Clamp01(nz / size.z);
                float steep = data.GetSteepness(u, v);
                if (steep > 26f)
                    continue;
                if (TooCloseToWater(p))
                    continue;
                float score = (26f - steep) + (float)rng.NextDouble() * 4f;
                if (score > best)
                {
                    best = score;
                    picked = p;
                    ok = true;
                }
            }
        }
        if (!ok)
        {
            var boat = GetComponent<BoatRaceMode>() ?? Object.FindFirstObjectByType<BoatRaceMode>();
            Transform spawn = boat != null ? boat.PlayerSpawn : null;
            if (spawn != null)
                picked = spawn.position;
            else if (_player != null)
                picked = _player.transform.position;
        }
        float yaw = (float)rng.NextDouble() * 360f;
        fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        _plotFwd = fwd;
        _plot = GroundAt(picked);

        var host = GetComponent<BoatRaceMode>() ?? Object.FindFirstObjectByType<BoatRaceMode>();
        if (host != null && host.PlayerSpawn != null)
            host.PlayerSpawn.SetPositionAndRotation(_plot + Vector3.up * 0.2f, Quaternion.LookRotation(_plotFwd, Vector3.up));
    }

    static bool TooCloseToWater(Vector3 pos)
    {
        var waters = Object.FindObjectsByType<BoatWater>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < waters.Length; i++)
        {
            var w = waters[i];
            if (w == null)
                continue;
            float y = w.SurfaceY;
            if (pos.y <= y + 1.4f)
            {
                Vector3 d = pos - w.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < 70f * 70f)
                    return true;
            }
        }
        return false;
    }

    void PlacePlayer()
    {
        if (_player == null)
            return;
        Vector3 pos = GroundAt(_plot) + Vector3.up * 1.15f;
        _player.WarpTo(pos, Quaternion.LookRotation(_plotFwd, Vector3.up));
        if (_player.PlayerCam != null)
        {
            _player.PlayerCam.enabled = true;
            _player.PlayerCam.gameObject.SetActive(true);
        }
    }

    bool PlayerInHouse()
    {
        if (_player == null || _house == null)
            return false;
        _house.CollectIsland(_buf);
        if (!InsideFootprint(_player.transform.position))
            return false;
        return HasCoverOver(_player.transform.position);
    }

    bool InsideFootprint(Vector3 feet)
    {
        Bounds b = default;
        bool any = false;
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null || p.Kind == BoatPieceKind.Oar)
                continue;
            if (!p.TrySolidBounds(out Bounds pb))
                continue;
            if (!any)
            {
                b = pb;
                any = true;
            }
            else
                b.Encapsulate(pb);
        }
        if (!any)
            return false;
        b.Expand(new Vector3(1.15f, 0f, 1.15f));
        return feet.x >= b.min.x && feet.x <= b.max.x && feet.z >= b.min.z && feet.z <= b.max.z;
    }

    bool HouseHasRoof()
    {
        if (_house == null)
            return false;
        _house.CollectIsland(_buf);
        float floor = float.PositiveInfinity;
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null || p.Kind == BoatPieceKind.Oar || !p.TrySolidBounds(out Bounds pb))
                continue;
            floor = Mathf.Min(floor, pb.min.y);
        }
        if (float.IsInfinity(floor))
            return false;
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null || p.Kind == BoatPieceKind.Oar || !p.TrySolidBounds(out Bounds pb))
                continue;
            if (IsRoofPiece(pb, floor))
                return true;
        }
        return false;
    }

    static bool IsRoofPiece(Bounds pb, float floor)
    {
        if (pb.max.y < floor + 1.35f)
            return false;
        float span = Mathf.Max(pb.size.x, pb.size.z);
        if (span < 0.28f)
            return false;
        return pb.min.y >= floor + 0.85f || pb.size.y < 0.7f;
    }

    bool HasCoverOver(Vector3 feet)
    {
        if (_house == null)
            return false;
        if (_buf.Count == 0)
            _house.CollectIsland(_buf);
        float need = feet.y + 1.05f;
        for (int i = 0; i < _buf.Count; i++)
        {
            var p = _buf[i];
            if (p == null || p.Kind == BoatPieceKind.Oar || !p.TrySolidBounds(out Bounds pb))
                continue;
            if (pb.max.y < need)
                continue;
            if (feet.x < pb.min.x - 0.55f || feet.x > pb.max.x + 0.55f)
                continue;
            if (feet.z < pb.min.z - 0.55f || feet.z > pb.max.z + 0.55f)
                continue;
            return true;
        }
        Vector3 origin = feet + Vector3.up * 1.55f;
        var hits = Physics.RaycastAll(origin, Vector3.up, 4.8f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i].collider;
            if (col == null)
                continue;
            if (col.GetComponentInParent<HorrorFirstPersonController>() != null)
                continue;
            var piece = BoatPart.FromCollider(col);
            if (piece == null || !piece.SharesIslandWith(_house))
                continue;
            if (hits[i].point.y >= need)
                return true;
        }
        return false;
    }

    bool FailIfOutsideTooLong()
    {
        bool jumpGrace = _player != null && _player.AirborneNow;
        if (jumpGrace)
        {
            _outsideSince = -1f;
            return false;
        }
        if (_outsideSince < 0f)
        {
            _outsideSince = Time.unscaledTime;
            if (_hud != null)
                _hud.FlashWarn("OUTSIDE", "Get back in the house.");
        }
        if (Time.unscaledTime - _outsideSince >= 1.8f)
        {
            Fail("You left the house");
            return true;
        }
        return false;
    }

    void KeepPlayerInHouse()
    {
        if (PlayerInHouse())
            _outsideSince = -1f;
    }

    void PlantRockSpawners()
    {
        ClearRocks();
        var terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
            return;
        var data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;
        var rng = new System.Random(unchecked(RaceSim.RoundSeed * 7919 + 44));
        var picks = new List<Vector3>(8);
        var aims = new List<Vector3>(8);
        var scores = new List<float>(8);
        for (int i = 0; i < 56; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = Mathf.Lerp(18f, 62f, (float)rng.NextDouble());
            Vector3 p = _plot + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
            p.y = terrain.SampleHeight(p) + origin.y;
            float nx = Mathf.Clamp01((p.x - origin.x) / size.x);
            float nz = Mathf.Clamp01((p.z - origin.z) / size.z);
            float steep = data.GetSteepness(nx, nz);
            float rise = p.y - _plot.y;
            if (rise < 3.5f || steep < 7f || steep > 55f)
                continue;
            Vector3 toHouse = _plot - p;
            toHouse.y = 0f;
            if (toHouse.sqrMagnitude < 1f)
                continue;
            toHouse.Normalize();
            Vector3 nrm = data.GetInterpolatedNormal(nx, nz);
            Vector3 slope = Vector3.ProjectOnPlane(Vector3.down, nrm);
            slope.y = 0f;
            if (slope.sqrMagnitude > 0.01f)
                slope.Normalize();
            else
                slope = toHouse;
            float face = Mathf.Max(0f, Vector3.Dot(slope, toHouse));
            float score = rise + steep * 0.25f + face * 12f;
            picks.Add(p);
            aims.Add(toHouse);
            scores.Add(score);
        }
        int want = 4;
        while (_rockSpawners.Count < want && picks.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < scores.Count; i++)
            {
                if (scores[i] > scores[best])
                    best = i;
            }
            var go = new GameObject("RockSpawner");
            go.transform.position = picks[best] + Vector3.up * 1.1f;
            var sp = go.AddComponent<HouseRockSpawner>();
            sp.Aim = aims[best];
            _rockSpawners.Add(sp);
            picks.RemoveAt(best);
            aims.RemoveAt(best);
            scores.RemoveAt(best);
        }
        if (_rockSpawners.Count == 0)
        {
            var go = new GameObject("RockSpawner");
            Vector3 fall = _plot + Vector3.up * 14f;
            if (Terrain.activeTerrain != null)
            {
                fall.x = _plot.x;
                fall.z = _plot.z - 28f;
                fall.y = Terrain.activeTerrain.SampleHeight(fall) + Terrain.activeTerrain.transform.position.y + 2.5f;
            }
            go.transform.position = fall;
            var sp = go.AddComponent<HouseRockSpawner>();
            Vector3 aim = _plot - fall;
            aim.y = 0f;
            sp.Aim = aim.sqrMagnitude > 0.01f ? aim.normalized : Vector3.forward;
            _rockSpawners.Add(sp);
        }
    }

    IEnumerator RockStorm()
    {
        float t = 0f;
        while (InHold && _rockSpawners.Count > 0)
        {
            float wait = t < 13f ? Random.Range(0.85f, 1.7f) : Random.Range(2.4f, 4.2f);
            yield return new WaitForSeconds(wait);
            t += wait;
            if (!InHold || _rockSpawners.Count == 0)
                break;
            int i = Random.Range(0, _rockSpawners.Count);
            if (i < 0 || i >= _rockSpawners.Count)
                break;
            var sp = _rockSpawners[i];
            Vector3 target = _house != null ? _house.transform.position : _plot;
            if (sp != null)
                sp.Drop(target);
        }
    }

    void ClearRocks()
    {
        for (int i = 0; i < _rockSpawners.Count; i++)
        {
            if (_rockSpawners[i] != null)
                Object.Destroy(_rockSpawners[i].gameObject);
        }
        _rockSpawners.Clear();
        var rocks = Object.FindObjectsByType<HouseRock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < rocks.Length; i++)
        {
            if (rocks[i] != null)
                Object.Destroy(rocks[i].gameObject);
        }
        var birds = Object.FindObjectsByType<HouseBombBird>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] != null)
                Object.Destroy(birds[i].gameObject);
        }
        var eggs = Object.FindObjectsByType<HouseBombEgg>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < eggs.Length; i++)
        {
            if (eggs[i] != null)
                Object.Destroy(eggs[i].gameObject);
        }
    }

    static Vector3 GroundAt(Vector3 pos)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
            return pos;
        }
        if (Physics.Raycast(pos + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 160f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.GetComponentInParent<BoatWater>() == null)
                pos.y = hit.point.y;
        }
        return pos;
    }

    static void HideRiver(bool hide)
    {
        BoatWater.CurrentEnabled = !hide;
        var waters = Object.FindObjectsByType<BoatWater>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < waters.Length; i++)
        {
            if (waters[i] == null)
                continue;
            var rend = waters[i].GetComponent<Renderer>();
            if (rend != null)
                rend.enabled = !hide;
            var cols = waters[i].GetComponents<Collider>();
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] != null)
                    cols[c].enabled = !hide;
            }
        }
        var sharks = Object.FindObjectsByType<RiverShark>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sharks.Length; i++)
        {
            if (sharks[i] != null)
                Object.Destroy(sharks[i].gameObject);
        }
        var dir = Object.FindFirstObjectByType<SharkDirector>();
        if (dir != null)
            dir.Clear();
    }

    void ScatterLoot()
    {
        for (int i = 0; i < _loot.Count; i++)
        {
            if (_loot[i] != null)
                Object.Destroy(_loot[i]);
        }
        _loot.Clear();
        var boat = GetComponent<BoatRaceMode>();
        if (boat == null)
            boat = Object.FindFirstObjectByType<BoatRaceMode>(FindObjectsInactive.Include);
        if (boat == null)
        {
            Debug.LogError("[House] BoatRaceMode missing, no build pieces");
            return;
        }
        boat.ScatterSupplies(_plot, _plotFwd, true, 22);
        var spawned = boat.SpawnedLoot;
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
                _loot.Add(spawned[i]);
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
        var go = Object.Instantiate(prefab, pos, rot);
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
        _loot.Add(go);
    }

    static float GroundY(Vector3 from, float fallback)
    {
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.GetComponentInParent<BoatWater>() == null)
                return hit.point.y;
        }
        return fallback;
    }
}

static class HouseQuakeGround
{
    public static Vector3 Delta { get; private set; }
    public static Vector3 Acc { get; private set; }
    public static float Envelope { get; private set; }

    static float _env;
    static float _power;
    static float _clock;
    static Vector3 _prev;
    static Vector3 _vel;
    static bool _primed;

    public static void Reset()
    {
        _env = 0f;
        _power = 0f;
        _clock = 0f;
        _primed = false;
        _prev = Vector3.zero;
        _vel = Vector3.zero;
        Delta = Vector3.zero;
        Acc = Vector3.zero;
        Envelope = 0f;
    }

    public static void SetDrive(float env, float power)
    {
        _env = Mathf.Clamp01(env);
        _power = Mathf.Clamp01(power);
        Envelope = _env;
    }

    public static void Tick(float dt)
    {
        if (dt < 0.0001f)
            return;
        _clock += dt;
        float t = _clock;
        float s = Mathf.Pow(_env, 1.25f) * _power;
        float sx = (Mathf.PerlinNoise(t * 0.27f, 2.7f) - 0.5f) * 2f;
        float sz = (Mathf.PerlinNoise(8.1f, t * 0.23f) - 0.5f) * 2f;
        sx += (Mathf.PerlinNoise(t * 0.08f, 1.2f) - 0.5f) * 1.15f;
        sz += (Mathf.PerlinNoise(4.4f, t * 0.09f) - 0.5f) * 1.15f;
        float horiz = s * 0.22f;
        float y = s * 0.055f * Mathf.Sin(t * 2.05f + sx);
        Vector3 disp = new Vector3(sx * horiz, y, sz * horiz);
        if (!_primed)
        {
            _prev = disp;
            _vel = Vector3.zero;
            _primed = true;
            Delta = Vector3.zero;
            Acc = Vector3.zero;
            return;
        }
        Vector3 vel = (disp - _prev) / dt;
        vel = Vector3.Lerp(_vel, vel, 0.28f);
        Acc = Vector3.ClampMagnitude((vel - _vel) / dt, 16f);
        Delta = vel * dt;
        _prev = disp;
        _vel = vel;
    }
}

static class HouseQuakeDust
{
    static ParticleSystem _dust;

    public static void Burst(Vector3 pos)
    {
        Ensure();
        if (_dust == null)
            return;
        var emit = new ParticleSystem.EmitParams
        {
            position = pos,
            applyShapeToPosition = true
        };
        _dust.Emit(emit, 18);
        emit.position = pos + Vector3.up * 0.12f;
        _dust.Emit(emit, 10);
    }

    static void Ensure()
    {
        if (_dust != null)
            return;
        var go = new GameObject("HouseQuakeDust");
        Object.DontDestroyOnLoad(go);
        _dust = go.AddComponent<ParticleSystem>();
        _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _dust.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.42f, 0.32f, 0.18f, 0.85f),
            new Color(0.28f, 0.2f, 0.1f, 0.55f));
        main.gravityModifier = 0.85f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        var em = _dust.emission;
        em.enabled = false;
        var sh = _dust.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.18f;
        var lim = _dust.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.25f;
        var col = _dust.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        rend.sharedMaterial = mat;
        _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}

[DefaultExecutionOrder(220)]
public class HouseQuakeCam : MonoBehaviour
{
    float _level;
    bool _on;
    Vector3 _pos;
    Vector3 _posVel;
    Vector3 _tilt;
    Vector3 _tiltVel;
    float _jolt;
    Vector3 _joltDir = Vector3.up;
    float _nextJolt;

    public void Play()
    {
        _on = true;
        enabled = true;
    }

    public void SetLevel(float level)
    {
        _level = Mathf.Clamp(level, 0f, 1.35f);
        if (_level > 0.001f)
            Play();
    }

    public void StopShake()
    {
        _on = false;
        _level = 0f;
        _pos = Vector3.zero;
        _posVel = Vector3.zero;
        _tilt = Vector3.zero;
        _tiltVel = Vector3.zero;
        _jolt = 0f;
        enabled = false;
    }

    void LateUpdate()
    {
        if (!_on || _level < 0.001f)
            return;
        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;
        float e = Mathf.Clamp(_level, 0f, 1.35f);
        if (t >= _nextJolt)
        {
            _jolt = Random.Range(0.55f, 1f) * e;
            _joltDir = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.35f, 0.85f), Random.Range(-1f, 1f)).normalized;
            _nextJolt = t + Random.Range(0.55f, 1.45f) / Mathf.Max(0.35f, e);
        }
        _jolt = Mathf.MoveTowards(_jolt, 0f, dt * (1.8f + e * 1.4f));

        float sway = Mathf.Sin(t * 1.05f) * 0.55f + Mathf.Sin(t * 0.31f + 1.7f) * 0.35f;
        float heave = Mathf.Sin(t * 2.35f) * 0.4f + Mathf.Sin(t * 6.8f) * 0.12f;
        float tremor = (Mathf.PerlinNoise(t * 11.5f, 0.4f) - 0.5f);
        float tremorZ = (Mathf.PerlinNoise(1.8f, t * 13.2f) - 0.5f);
        Vector3 wantPos = new Vector3(
            sway * 0.018f * e + tremor * 0.006f * e + _joltDir.x * _jolt * 0.028f,
            heave * 0.022f * e + _joltDir.y * _jolt * 0.04f,
            tremorZ * 0.01f * e + _joltDir.z * _jolt * 0.02f);
        _pos = Vector3.SmoothDamp(_pos, wantPos, ref _posVel, 0.045f, 12f, dt);
        Vector3 wantTilt = new Vector3(
            heave * 1.35f * e + _joltDir.z * _jolt * 2.8f,
            tremor * 0.45f * e,
            sway * 2.4f * e + _joltDir.x * _jolt * 3.4f);
        _tilt = Vector3.SmoothDamp(_tilt, wantTilt, ref _tiltVel, 0.07f, 28f, dt);
        transform.localPosition += _pos;
        transform.localRotation *= Quaternion.Euler(_tilt);
    }
}
