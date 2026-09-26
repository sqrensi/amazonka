using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Настроение заезда от RoundSeed. Сезон на раунд один; небо, туман, дождь и время суток
/// едут плавно внутри раунда по тому же сиду.
/// </summary>
[DefaultExecutionOrder(-20)]
public class RaceMood : MonoBehaviour
{
    public enum Season { Spring, Summer, Autumn, Winter }
    public enum Sky { Clear, Overcast, Fog, Rain, Storm, Snow }

    struct Look
    {
        public Season season;
        public Sky sky;
        public Color grade;
        public float snow;
        public float rain;
        public float fogDensity;
        public Color fogColor;
        public float exposure;
        public float contrast;
        public float sat;
        public float temp;
        public float tint;
        public float sunMul;
        public Color sunCol;
        public Vector3 sunEuler;
        public float current;
        public Color waterDeep;
        public Color waterShallow;
    }

    public static RaceMood Active { get; private set; }
    public static Season RoundSeason { get; private set; } = Season.Summer;
    public static Sky RoundSky { get; private set; } = Sky.Clear;
    public static Color FogColor { get; private set; } = new Color(0.72f, 0.78f, 0.82f);
    public static float FogDensity { get; private set; }
    public static bool FogOn => FogDensity > 0.0008f;

    Light _sun;
    Color _sunColor;
    float _sunInt;
    Quaternion _sunRot;
    Color _ambient;
    bool _litSaved;
    ParticleSystem _precip;
    bool _precipSnow;
    Color _waterDeep = new Color(0.15f, 0.21f, 0.34f, 0.96f);
    Color _waterShallow = new Color(0.1f, 0.28f, 0.46f, 0.11f);

    bool _live;
    float _day;
    float _dayVel;
    float _baseCurrent;
    Look _from;
    Look _to;
    float _blend;
    float _blendDur;
    float _hold;
    int _beat;
    System.Random _weatherRng;
    float _waterPaintAt;
    float _gradeAt;
    float _fogLive;
    Color _fogColLive;
    float _rainLive;
    float _snowLive;

    static readonly int WaterDeepId = Shader.PropertyToID("Color_36218622185947c6a5ae36366d8e21d8");
    static readonly int WaterShallowId = Shader.PropertyToID("Color_93e06cd551a5449091bcde90b46765a0");
    static readonly int SoftShallow = Shader.PropertyToID("_Shallow");
    static readonly int SoftDeep = Shader.PropertyToID("_Deep");
    static readonly int TerrainTintId = Shader.PropertyToID("_TerrainSeasonTint");
    static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void DefaultTerrainTint()
    {
        Shader.SetGlobalColor(TerrainTintId, Color.white);
    }

    public static void ApplyRound(int seed)
    {
        try
        {
            RaceMood mood = Active;
            if (mood == null)
                mood = Object.FindFirstObjectByType<RaceMood>();
            if (mood == null)
            {
                var go = new GameObject("RaceMood");
                mood = go.AddComponent<RaceMood>();
            }
            mood.Roll(seed);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[RaceMood] " + e.Message);
        }
    }

    void OnEnable()
    {
        Active = this;
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;
        Restore();
    }

    public void Roll(int seed)
    {
        var rng = new System.Random(unchecked(seed * 7919 + 31));
        RoundSeason = (Season)rng.Next(0, 4);
        CaptureLight();
        BindTerrainLayers();
        _baseCurrent = RollCurrent(RoundSeason, rng);
        _day = Mathf.Lerp(0.14f, 0.5f, (float)rng.NextDouble());
        _dayVel = Mathf.Lerp(0.07f, 0.15f, (float)rng.NextDouble()) / 60f;
        _weatherRng = new System.Random(unchecked(seed * 104729 + 17));
        _beat = 0;
        _from = BuildLook(RoundSeason, PickSky(RoundSeason, rng), _day, _baseCurrent);
        _to = _from;
        _blend = 1f;
        _blendDur = 1f;
        _hold = 0f;
        _fogLive = _from.fogDensity;
        _fogColLive = _from.fogColor;
        _rainLive = _from.rain;
        _snowLive = _from.snow;
        ApplyLook(_from, true);
        QueueHold();
        _to = NextLook();
        _live = true;
    }

    void Update()
    {
        if (!_live || _weatherRng == null)
            return;
        try
        {
        float dt = Time.unscaledDeltaTime;
        _day = Mathf.Min(0.92f, _day + _dayVel * dt);
        Look goal;
        if (_hold > 0f)
        {
            _hold -= dt;
            goal = WithDay(_from, _day);
        }
        else
        {
            _blend += dt;
            float u = _blendDur <= 1f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_blend / _blendDur));
            goal = LerpLook(_from, WithDay(_to, _day), u);
            goal.sunEuler = DaySun(_day);
            if (u >= 1f)
            {
                _from = WithDay(_to, _day);
                _to = NextLook();
                QueueHold();
                _blend = 0f;
            }
        }
        ApplyLook(goal, false);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[RaceMood] tick " + e.Message);
            _live = false;
        }
    }

    void QueueHold()
    {
        _hold = Mathf.Lerp(18f, 48f, (float)_weatherRng.NextDouble());
        _blendDur = Mathf.Lerp(48f, 95f, (float)_weatherRng.NextDouble());
    }

    Look NextLook()
    {
        _beat++;
        Sky sky = StepSky(_from.sky, RoundSeason, _weatherRng);
        return BuildLook(RoundSeason, sky, _day, _baseCurrent);
    }

    static Sky StepSky(Sky from, Season season, System.Random rng)
    {
        Sky[] chain = season == Season.Winter
            ? new[] { Sky.Clear, Sky.Overcast, Sky.Fog, Sky.Snow }
            : new[] { Sky.Clear, Sky.Overcast, Sky.Fog, Sky.Rain, Sky.Storm };
        int i = 0;
        for (int n = 0; n < chain.Length; n++)
        {
            if (chain[n] == from)
            {
                i = n;
                break;
            }
        }
        int step = rng.Next(0, 100) < 22 ? 0 : (rng.Next(0, 2) == 0 ? -1 : 1);
        if (step == 0)
            step = rng.Next(0, 2) == 0 ? -1 : 1;
        i = Mathf.Clamp(i + step, 0, chain.Length - 1);
        return chain[i];
    }

    static Look WithDay(Look look, float day)
    {
        look.sunEuler = DaySun(day);
        Color heat = DaySunColor(day);
        look.sunCol = Color.Lerp(look.sunCol, heat, 0.55f);
        look.temp += (day - 0.45f) * 10f;
        return look;
    }

    static Vector3 DaySun(float day)
    {
        float t = Mathf.Clamp01(day);
        float pitch = Mathf.Lerp(7f, 56f, Mathf.Sin(t * Mathf.PI));
        float yaw = Mathf.Lerp(98f, 238f, t);
        return new Vector3(pitch, yaw, 0f);
    }

    static Color DaySunColor(float day)
    {
        float t = Mathf.Clamp01(day);
        Color dawn = new Color(1f, 0.72f, 0.48f);
        Color noon = new Color(1f, 0.94f, 0.78f);
        Color dusk = new Color(1f, 0.52f, 0.28f);
        if (t < 0.5f)
            return Color.Lerp(dawn, noon, t * 2f);
        return Color.Lerp(noon, dusk, (t - 0.5f) * 2f);
    }

    static Look BuildLook(Season season, Sky sky, float day, float current)
    {
        var look = new Look
        {
            season = season,
            sky = sky,
            grade = Color.white,
            snow = 0f,
            rain = 0f,
            fogDensity = 0f,
            fogColor = new Color(0.74f, 0.80f, 0.84f),
            exposure = 0.62f,
            contrast = 10f,
            sat = 18f,
            temp = 6f,
            tint = -1f,
            sunMul = 1f,
            sunCol = new Color(1f, 0.91f, 0.65f),
            sunEuler = DaySun(day),
            current = current,
            waterDeep = new Color(0.15f, 0.21f, 0.34f, 0.96f),
            waterShallow = new Color(0.1f, 0.28f, 0.46f, 0.11f)
        };

        switch (season)
        {
            case Season.Spring:
                look.grade = new Color(0.92f, 1.02f, 0.9f);
                look.sat = 20f;
                look.temp = 2f;
                look.current *= 1.02f;
                look.waterDeep = new Color(0.14f, 0.24f, 0.34f, 0.96f);
                look.waterShallow = new Color(0.12f, 0.32f, 0.44f, 0.12f);
                break;
            case Season.Summer:
                look.grade = new Color(1.04f, 1.01f, 0.9f);
                look.sat = 22f;
                look.temp = 10f;
                look.exposure = 0.7f;
                look.sunMul = 1.12f;
                look.waterDeep = new Color(0.13f, 0.22f, 0.36f, 0.96f);
                look.waterShallow = new Color(0.1f, 0.3f, 0.48f, 0.11f);
                break;
            case Season.Autumn:
                look.grade = new Color(1.08f, 0.86f, 0.62f);
                look.sat = 16f;
                look.temp = 14f;
                look.contrast = 12f;
                look.sunCol = new Color(1f, 0.78f, 0.48f);
                look.sunMul = 0.88f;
                look.waterDeep = new Color(0.16f, 0.2f, 0.28f, 0.96f);
                look.waterShallow = new Color(0.14f, 0.26f, 0.36f, 0.12f);
                break;
            default:
                look.grade = new Color(0.92f, 0.96f, 1.04f);
                look.snow = 0.72f;
                look.sat = 8f;
                look.temp = -6f;
                look.exposure = 0.78f;
                look.contrast = 8f;
                look.sunCol = new Color(0.82f, 0.9f, 1f);
                look.sunMul = 0.78f;
                look.fogColor = new Color(0.82f, 0.88f, 0.92f);
                look.waterDeep = new Color(0.16f, 0.22f, 0.3f, 0.96f);
                look.waterShallow = new Color(0.18f, 0.28f, 0.36f, 0.14f);
                break;
        }

        switch (sky)
        {
            case Sky.Clear:
                break;
            case Sky.Overcast:
                look.sunMul *= 0.55f;
                look.exposure -= 0.12f;
                look.sat -= 4f;
                look.fogDensity = 0.004f;
                look.fogColor = Color.Lerp(look.fogColor, new Color(0.62f, 0.66f, 0.7f), 0.45f);
                break;
            case Sky.Fog:
                look.sunMul *= 0.42f;
                look.fogDensity = 0.018f;
                look.fogColor = Color.Lerp(look.fogColor, new Color(0.7f, 0.76f, 0.78f), 0.5f);
                look.exposure -= 0.08f;
                look.sat -= 6f;
                break;
            case Sky.Rain:
                look.rain = 1f;
                look.sunMul *= 0.4f;
                look.fogDensity = 0.009f;
                look.fogColor = new Color(0.55f, 0.6f, 0.64f);
                look.exposure -= 0.16f;
                look.sat -= 8f;
                look.temp -= 4f;
                look.current *= 1.06f;
                break;
            case Sky.Storm:
                look.rain = 1.35f;
                look.sunMul *= 0.28f;
                look.fogDensity = 0.014f;
                look.fogColor = new Color(0.42f, 0.46f, 0.5f);
                look.exposure -= 0.22f;
                look.contrast += 4f;
                look.sat -= 10f;
                look.sunCol = new Color(0.7f, 0.76f, 0.82f);
                look.current *= 1.1f;
                break;
            case Sky.Snow:
                look.snow = Mathf.Max(look.snow, 0.82f);
                look.rain = 0f;
                look.fogDensity = Mathf.Max(look.fogDensity, 0.011f);
                look.fogColor = new Color(0.86f, 0.9f, 0.94f);
                look.sunMul *= 0.62f;
                break;
        }

        look.sunEuler = DaySun(day);
        look.sunCol = Color.Lerp(look.sunCol, DaySunColor(day), 0.55f);
        return look;
    }

    static Look LerpLook(Look a, Look b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Look
        {
            season = t < 0.5f ? a.season : b.season,
            sky = t < 0.5f ? a.sky : b.sky,
            grade = Color.Lerp(a.grade, b.grade, t),
            snow = Mathf.Lerp(a.snow, b.snow, t),
            rain = Mathf.Lerp(a.rain, b.rain, t),
            fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
            fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
            exposure = Mathf.Lerp(a.exposure, b.exposure, t),
            contrast = Mathf.Lerp(a.contrast, b.contrast, t),
            sat = Mathf.Lerp(a.sat, b.sat, t),
            temp = Mathf.Lerp(a.temp, b.temp, t),
            tint = Mathf.Lerp(a.tint, b.tint, t),
            sunMul = Mathf.Lerp(a.sunMul, b.sunMul, t),
            sunCol = Color.Lerp(a.sunCol, b.sunCol, t),
            sunEuler = Quaternion.Slerp(Quaternion.Euler(a.sunEuler), Quaternion.Euler(b.sunEuler), t).eulerAngles,
            current = Mathf.Lerp(a.current, b.current, t),
            waterDeep = Color.Lerp(a.waterDeep, b.waterDeep, t),
            waterShallow = Color.Lerp(a.waterShallow, b.waterShallow, t)
        };
    }

    void ApplyLook(Look look, bool force)
    {
        RoundSeason = look.season;
        RoundSky = look.sky;
        float dt = Time.unscaledDeltaTime;
        if (force)
        {
            _fogLive = look.fogDensity;
            _fogColLive = look.fogColor;
            _rainLive = look.rain;
            _snowLive = look.snow;
        }
        else
        {
            float k = 1f - Mathf.Exp(-dt * 0.28f);
            _fogLive = Mathf.Lerp(_fogLive, look.fogDensity, k);
            _fogColLive = Color.Lerp(_fogColLive, look.fogColor, k);
            _rainLive = Mathf.Lerp(_rainLive, look.rain, k);
            _snowLive = Mathf.Lerp(_snowLive, look.snow, k);
        }
        FogDensity = _fogLive;
        FogColor = _fogColLive;
        float k = force ? 1f : 1f - Mathf.Exp(-dt * 0.38f);
        _waterDeep = Color.Lerp(_waterDeep, look.waterDeep, k);
        _waterShallow = Color.Lerp(_waterShallow, look.waterShallow, k);
        BoatCurrentPath.SetRoundSpeed(look.current);
        ApplySun(look.sunCol, look.sunMul, look.sunEuler, force);
        float now = Time.unscaledTime;
        if (force || now >= _gradeAt)
        {
            ApplyGrade(look.exposure, look.contrast, look.sat, look.temp, look.tint, force);
            TintTerrain(look.grade, look.snow, force);
            _gradeAt = now + 0.05f;
        }
        if (force || now >= _waterPaintAt)
        {
            TintWater();
            _waterPaintAt = now + 0.2f;
        }
        SetPrecip(_rainLive, _snowLive);
        if (!UnderwaterFx.Covering)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = Mathf.Max(0.00012f, FogDensity);
        }
        if (_sun != null)
            RenderSettings.sun = _sun;
    }

    static Sky PickSky(Season season, System.Random rng)
    {
        int roll = rng.Next(0, 100);
        if (season == Season.Winter)
        {
            if (roll < 28) return Sky.Snow;
            if (roll < 48) return Sky.Fog;
            if (roll < 70) return Sky.Overcast;
            return Sky.Clear;
        }
        if (season == Season.Autumn)
        {
            if (roll < 18) return Sky.Fog;
            if (roll < 38) return Sky.Rain;
            if (roll < 50) return Sky.Overcast;
            if (roll < 58) return Sky.Storm;
            return Sky.Clear;
        }
        if (season == Season.Spring)
        {
            if (roll < 22) return Sky.Rain;
            if (roll < 34) return Sky.Fog;
            if (roll < 52) return Sky.Overcast;
            return Sky.Clear;
        }
        if (roll < 10) return Sky.Storm;
        if (roll < 22) return Sky.Rain;
        if (roll < 32) return Sky.Fog;
        if (roll < 48) return Sky.Overcast;
        return Sky.Clear;
    }

    static float RollCurrent(Season season, System.Random rng)
    {
        float u = (float)rng.NextDouble();
        float baseSp = season == Season.Spring ? 5.1f : season == Season.Winter ? 3.4f : season == Season.Autumn ? 4.2f : 4.8f;
        return Mathf.Lerp(baseSp * 0.78f, baseSp * 1.28f, u);
    }

    void CaptureLight()
    {
        if (_litSaved)
            return;
        _sun = RenderSettings.sun;
        if (_sun == null)
            _sun = Object.FindFirstObjectByType<Light>();
        if (_sun != null)
        {
            _sunColor = _sun.color;
            _sunInt = _sun.intensity;
            _sunRot = _sun.transform.rotation;
        }
        _ambient = RenderSettings.ambientLight;
        _litSaved = true;
    }

    void ApplySun(Color col, float mul, Vector3 euler, bool force)
    {
        if (_sun == null)
            return;
        Quaternion want = Quaternion.Euler(euler);
        float wantInt = Mathf.Clamp(_sunInt * mul, 0.25f, 2.4f);
        float k = force ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 0.42f);
        _sun.color = Color.Lerp(_sun.color, col, k);
        _sun.intensity = Mathf.Lerp(_sun.intensity, wantInt, k);
        _sun.transform.rotation = Quaternion.Slerp(_sun.transform.rotation, want, k);
        RenderSettings.ambientLight = Color.Lerp(
            RenderSettings.ambientLight,
            Color.Lerp(_ambient, col, 0.18f) * Mathf.Lerp(0.7f, 1.05f, mul),
            k);
    }

    void ApplyGrade(float exposure, float contrast, float sat, float temp, float tint, bool force)
    {
        var look = Object.FindFirstObjectByType<GameLook>();
        if (look != null)
            look.SetRound(exposure, contrast, sat, temp, tint, force);
    }

    Color _gradeLive = Color.white;

    void TintTerrain(Color mul, float snow, bool force)
    {
        Color c = Color.Lerp(mul, new Color(0.86f, 0.9f, 0.96f), snow);
        if (force)
            _gradeLive = c;
        else
            _gradeLive = Color.Lerp(_gradeLive, c, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 0.35f));
        Shader.SetGlobalColor(TerrainTintId, new Color(
            Mathf.Clamp(_gradeLive.r, 0.55f, 1.25f),
            Mathf.Clamp(_gradeLive.g, 0.55f, 1.25f),
            Mathf.Clamp(_gradeLive.b, 0.55f, 1.25f),
            1f));
    }

    static readonly string[] LayerFiles =
    {
        "AdgBank", "PaintMoss", "PaintGrassA", "PaintGrassB",
        "AdgGravel", "PaintStones", "AdgRock", "PaintDirt"
    };

    static void BindTerrainLayers()
    {
#if UNITY_EDITOR
        var layers = new TerrainLayer[LayerFiles.Length];
        for (int i = 0; i < LayerFiles.Length; i++)
        {
            layers[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/MAIN/TerrainLayers/" + LayerFiles[i] + ".terrainlayer");
            if (layers[i] == null || layers[i].diffuseTexture == null)
                return;
        }
        var found = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int t = 0; t < found.Length; t++)
        {
            var terrain = found[t];
            if (terrain == null || terrain.terrainData == null)
                continue;
            terrain.terrainData.terrainLayers = layers;
        }
#endif
    }

    public static void PaintWater(Renderer r)
    {
        if (Active != null)
            Active.PaintOneWater(r);
    }

    void PaintOneWater(Renderer r)
    {
        if (r == null)
            return;
        r.GetPropertyBlock(Block);
        Block.SetColor(WaterDeepId, _waterDeep);
        Block.SetColor(WaterShallowId, _waterShallow);
        Block.SetColor(SoftDeep, _waterDeep);
        Block.SetColor(SoftShallow, new Color(_waterShallow.r, _waterShallow.g, _waterShallow.b, 0.55f));
        r.SetPropertyBlock(Block);
    }

    void TintWater()
    {
        var waters = Object.FindObjectsByType<BoatWater>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < waters.Length; i++)
        {
            if (waters[i] == null)
                continue;
            var rs = waters[i].GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < rs.Length; r++)
                PaintOneWater(rs[r]);
        }
    }

    void SetPrecip(float rain, float snow)
    {
        bool wantSnow = snow > 0.4f && rain < 0.35f;
        bool wantRain = rain > 0.06f && !wantSnow;
        if (!wantRain && !wantSnow)
        {
            if (_precip != null)
            {
                var emOff = _precip.emission;
                emOff.rateOverTime = 0f;
            }
            return;
        }
        if (_precip == null || _precipSnow != wantSnow)
            BuildPrecip(wantSnow);
        if (_precip == null)
            return;
        var em = _precip.emission;
        em.rateOverTime = wantSnow ? 28f + snow * 18f : 55f * rain;
        var cam = Camera.main;
        if (cam != null && _precip.transform.parent != cam.transform)
        {
            _precip.transform.SetParent(cam.transform, false);
            _precip.transform.localPosition = new Vector3(0f, 4.5f, 3.5f);
        }
    }

    void BuildPrecip(bool snow)
    {
        if (_precip != null)
            Destroy(_precip.gameObject);
        var cam = Camera.main;
        var go = new GameObject(snow ? "SnowFx" : "RainFx");
        if (cam != null)
            go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 4.5f, 3.5f);
        _precip = go.AddComponent<ParticleSystem>();
        _precipSnow = snow;
        var main = _precip.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = snow ? 90 : 110;
        main.startLifetime = snow ? 6.5f : 0.55f;
        main.startSpeed = snow ? 1.4f : 14f;
        main.startSize = snow ? 0.045f : 0.018f;
        main.startColor = snow ? new Color(0.92f, 0.95f, 1f, 0.7f) : new Color(0.65f, 0.72f, 0.8f, 0.35f);
        main.gravityModifier = snow ? 0.08f : 1.6f;
        var em = _precip.emission;
        em.rateOverTime = snow ? 28f : 55f;
        var sh = _precip.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(14f, 6f, 14f);
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
        if (!snow)
        {
            rend.velocityScale = 0.08f;
            rend.lengthScale = 2.4f;
        }
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.color = Color.white;
            rend.sharedMaterial = mat;
        }
        _precip.Play();
    }

    void Restore()
    {
        _live = false;
        Shader.SetGlobalColor(TerrainTintId, Color.white);
        if (_litSaved && _sun != null)
        {
            _sun.color = _sunColor;
            _sun.intensity = _sunInt;
            _sun.transform.rotation = _sunRot;
            RenderSettings.ambientLight = _ambient;
        }
        RenderSettings.fog = false;
        if (_precip != null)
            Destroy(_precip.gameObject);
    }
}
