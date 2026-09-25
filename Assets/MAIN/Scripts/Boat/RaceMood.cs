using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Настроение заезда от RoundSeed: сезон, погода, течение, лёгкий tint воды и грунта.
/// </summary>
public class RaceMood : MonoBehaviour
{
    public enum Season { Spring, Summer, Autumn, Winter }
    public enum Sky { Clear, Overcast, Fog, Rain, Storm, Snow }

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
    Color _sky;
    bool _litSaved;
    ParticleSystem _precip;
    Color _waterDeep = new Color(0.15f, 0.21f, 0.34f, 0.96f);
    Color _waterShallow = new Color(0.1f, 0.28f, 0.46f, 0.11f);

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
        var host = Object.FindFirstObjectByType<BoatRaceMode>();
        RaceMood mood = host != null ? host.GetComponent<RaceMood>() : null;
        if (mood == null && host != null)
            mood = host.gameObject.AddComponent<RaceMood>();
        if (mood == null)
        {
            var go = new GameObject("RaceMood");
            mood = go.AddComponent<RaceMood>();
        }
        mood.Roll(seed);
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
        RoundSky = PickSky(RoundSeason, rng);
        CaptureLight();
        float current = RollCurrent(RoundSeason, rng);
        BoatCurrentPath.SetRoundSpeed(current);

        Color grade = Color.white;
        float snow = 0f;
        float rain = 0f;
        FogDensity = 0f;
        FogColor = new Color(0.74f, 0.80f, 0.84f);
        float exposure = 0.62f;
        float contrast = 10f;
        float sat = 18f;
        float temp = 6f;
        float tint = -1f;
        float sunMul = 1f;
        Color sunCol = new Color(1f, 0.91f, 0.65f);
        Vector3 sunEuler = new Vector3(38f, 160f, 0f);

        switch (RoundSeason)
        {
            case Season.Spring:
                grade = new Color(0.92f, 1.02f, 0.9f);
                sat = 20f;
                temp = 2f;
                current *= 1.02f;
                _waterDeep = new Color(0.14f, 0.24f, 0.34f, 0.96f);
                _waterShallow = new Color(0.12f, 0.32f, 0.44f, 0.12f);
                break;
            case Season.Summer:
                grade = new Color(1.04f, 1.01f, 0.9f);
                sat = 22f;
                temp = 10f;
                exposure = 0.7f;
                sunMul = 1.12f;
                sunEuler = new Vector3(52f, 148f, 0f);
                _waterDeep = new Color(0.13f, 0.22f, 0.36f, 0.96f);
                _waterShallow = new Color(0.1f, 0.3f, 0.48f, 0.11f);
                break;
            case Season.Autumn:
                grade = new Color(1.08f, 0.86f, 0.62f);
                sat = 16f;
                temp = 14f;
                contrast = 12f;
                sunCol = new Color(1f, 0.78f, 0.48f);
                sunMul = 0.88f;
                sunEuler = new Vector3(28f, 172f, 0f);
                _waterDeep = new Color(0.16f, 0.2f, 0.28f, 0.96f);
                _waterShallow = new Color(0.14f, 0.26f, 0.36f, 0.12f);
                break;
            default:
                grade = new Color(0.92f, 0.96f, 1.04f);
                snow = 0.72f;
                sat = 8f;
                temp = -6f;
                exposure = 0.78f;
                contrast = 8f;
                sunCol = new Color(0.82f, 0.9f, 1f);
                sunMul = 0.78f;
                sunEuler = new Vector3(24f, 155f, 0f);
                FogColor = new Color(0.82f, 0.88f, 0.92f);
                _waterDeep = new Color(0.16f, 0.22f, 0.3f, 0.96f);
                _waterShallow = new Color(0.18f, 0.28f, 0.36f, 0.14f);
                break;
        }

        switch (RoundSky)
        {
            case Sky.Clear:
                break;
            case Sky.Overcast:
                sunMul *= 0.55f;
                exposure -= 0.12f;
                sat -= 4f;
                FogDensity = 0.004f;
                FogColor = Color.Lerp(FogColor, new Color(0.62f, 0.66f, 0.7f), 0.45f);
                break;
            case Sky.Fog:
                sunMul *= 0.42f;
                FogDensity = 0.018f;
                FogColor = Color.Lerp(FogColor, new Color(0.7f, 0.76f, 0.78f), 0.5f);
                exposure -= 0.08f;
                sat -= 6f;
                break;
            case Sky.Rain:
                rain = 1f;
                sunMul *= 0.4f;
                FogDensity = 0.009f;
                FogColor = new Color(0.55f, 0.6f, 0.64f);
                exposure -= 0.16f;
                sat -= 8f;
                temp -= 4f;
                break;
            case Sky.Storm:
                rain = 1.35f;
                sunMul *= 0.28f;
                FogDensity = 0.014f;
                FogColor = new Color(0.42f, 0.46f, 0.5f);
                exposure -= 0.22f;
                contrast += 4f;
                sat -= 10f;
                sunCol = new Color(0.7f, 0.76f, 0.82f);
                break;
            case Sky.Snow:
                snow = Mathf.Max(snow, 0.82f);
                rain = 0f;
                FogDensity = Mathf.Max(FogDensity, 0.011f);
                FogColor = new Color(0.86f, 0.9f, 0.94f);
                sunMul *= 0.62f;
                break;
        }

        BoatCurrentPath.SetRoundSpeed(current);
        ApplySun(sunCol, sunMul, sunEuler);
        ApplyGrade(exposure, contrast, sat, temp, tint);
        BindTerrainLayers();
        TintTerrain(grade, snow);
        TintWater();
        BuildPrecip(RoundSky == Sky.Snow || RoundSeason == Season.Winter, rain, snow);
        RenderSettings.fog = FogOn;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = FogDensity;
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
        _sky = RenderSettings.skybox != null ? Color.white : RenderSettings.ambientSkyColor;
        _litSaved = true;
    }

    void ApplySun(Color col, float mul, Vector3 euler)
    {
        if (_sun == null)
            return;
        _sun.color = col;
        _sun.intensity = Mathf.Clamp(_sunInt * mul, 0.25f, 2.4f);
        _sun.transform.rotation = Quaternion.Euler(euler);
        RenderSettings.ambientLight = Color.Lerp(_ambient, col, 0.18f) * Mathf.Lerp(0.7f, 1.05f, mul);
    }

    void ApplyGrade(float exposure, float contrast, float sat, float temp, float tint)
    {
        var look = Object.FindFirstObjectByType<GameLook>();
        if (look != null)
            look.SetRound(exposure, contrast, sat, temp, tint);
    }

    void TintTerrain(Color mul, float snow)
    {
        Color c = Color.Lerp(mul, new Color(0.86f, 0.9f, 0.96f), snow);
        Shader.SetGlobalColor(TerrainTintId, new Color(
            Mathf.Clamp(c.r, 0.55f, 1.25f),
            Mathf.Clamp(c.g, 0.55f, 1.25f),
            Mathf.Clamp(c.b, 0.55f, 1.25f),
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

    void BuildPrecip(bool snow, float rain, float snowAmt)
    {
        if (_precip != null)
            Destroy(_precip.gameObject);
        if (rain < 0.05f && !snow)
            return;
        var cam = Camera.main;
        var go = new GameObject(snow ? "SnowFx" : "RainFx");
        if (cam != null)
            go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 4.5f, 3.5f);
        _precip = go.AddComponent<ParticleSystem>();
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
        em.rateOverTime = snow ? 28f + snowAmt * 18f : 55f * rain;
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
