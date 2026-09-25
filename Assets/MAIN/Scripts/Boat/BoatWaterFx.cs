using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Брызги весла: прозрачные капли, не «песок».
/// </summary>
public static class BoatWaterFx
{
    static readonly Color WaterDeep = new Color(0.18f, 0.32f, 0.46f, 1f);
    static readonly Color WaterLite = new Color(0.42f, 0.62f, 0.74f, 1f);

    static ParticleSystem _drops;
    static ParticleSystem _spray;
    static float _nextSplash;
    static float _nextWake;

    public static void Splash(Vector3 at, Vector3 along, float power)
    {
        EmitSplash(at, along, power, true);
    }

    public static void Impact(Vector3 at, Vector3 along, float power)
    {
        EmitSplash(at, along, power, false);
    }

    static void EmitSplash(Vector3 at, Vector3 along, float power, bool throttle)
    {
        Ensure();
        if (_drops == null)
            return;
        if (throttle && Time.time < _nextSplash)
            return;
        if (!BoatWater.TryHeight(at, out float y))
            return;
        at.y = y + 0.04f;
        along.y = 0f;
        if (along.sqrMagnitude < 0.01f)
            along = Vector3.forward;
        along.Normalize();
        power = Mathf.Clamp(power, 0.25f, 1.8f);
        if (throttle)
            _nextSplash = Time.time + 0.022f;
        Vector3 side = Vector3.Cross(Vector3.up, along);

        var emit = new ParticleSystem.EmitParams();
        int n = (throttle ? 14 : 24) + Mathf.RoundToInt(power * 10f);
        for (int i = 0; i < n; i++)
        {
            emit.position = at + side * Random.Range(-0.2f, 0.2f) + along * Random.Range(-0.08f, 0.12f);
            emit.velocity = along * Random.Range(0.5f, 2.8f) * power
                + side * Random.Range(-1.6f, 1.6f) * power
                + Vector3.up * Random.Range(1.6f, 4.6f) * power;
            emit.startSize = Random.Range(0.018f, 0.065f) * (throttle ? 1f : 1.3f);
            emit.startColor = DropColor();
            emit.startLifetime = Random.Range(0.26f, 0.55f);
            _drops.Emit(emit, 1);
        }

        if (_spray != null)
        {
            int mist = throttle ? 2 : 5;
            for (int i = 0; i < mist; i++)
            {
                emit.position = at + side * Random.Range(-0.12f, 0.12f);
                emit.velocity = along * (0.45f * power) + Vector3.up * (0.28f * power);
                emit.startSize = Random.Range(0.24f, 0.52f) * power;
                emit.startColor = SprayColor();
                emit.startLifetime = Random.Range(0.16f, 0.34f);
                _spray.Emit(emit, 1);
            }
        }
    }

    public static void Foam(Vector3 at, Vector3 along, float power)
    {
        Splash(at, along, Mathf.Max(0.2f, power * 0.45f));
    }

    public static void Wake(Vector3 at, Vector3 along, float speed)
    {
        if (speed < 1.2f || Time.time < _nextWake)
            return;
        Ensure();
        if (_spray == null || !BoatWater.TryHeight(at, out float y))
            return;
        _nextWake = Time.time + 0.14f;
        at.y = y + 0.02f;
        along.y = 0f;
        if (along.sqrMagnitude < 0.01f)
            along = Vector3.forward;
        along.Normalize();
        var emit = new ParticleSystem.EmitParams
        {
            position = at,
            velocity = along * 0.25f,
            startSize = 0.28f,
            startColor = SprayColor(),
            startLifetime = 0.28f
        };
        _spray.Emit(emit, 1);
    }

    static Color DropColor()
    {
        Color c = Color.Lerp(WaterLite, Color.white, Random.Range(0.15f, 0.45f));
        c.a = Random.Range(0.35f, 0.7f);
        return c;
    }

    static Color SprayColor()
    {
        Color c = Color.Lerp(WaterDeep, WaterLite, 0.65f);
        c.a = 0.16f;
        return c;
    }

    static void Ensure()
    {
        if (_drops != null && _spray != null)
            return;
        var root = new GameObject("BoatWaterFx");
        Object.DontDestroyOnLoad(root);
        root.hideFlags = HideFlags.HideAndDontSave;
        Texture2D dropTex = SoftDisc(48, 1.15f);
        Texture2D mistTex = SoftDisc(48, 2.4f);
        _drops = Make(root.transform, "Drops", 160, 0.35f, 2.6f, dropTex, false);
        _spray = Make(root.transform, "Spray", 36, 0.2f, 0.05f, mistTex, true);
    }

    static ParticleSystem Make(Transform parent, string name, int max, float life, float gravity, Texture2D tex, bool mist)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = mist ? 0.24f : 0.03f;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;
        var sizeLife = ps.sizeOverLifetime;
        sizeLife.enabled = true;
        sizeLife.size = new ParticleSystem.MinMaxCurve(1f, mist
            ? AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f)
            : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(WaterLite, 0f),
                new GradientColorKey(WaterDeep, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(mist ? 0.25f : 0.7f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = g;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.lightProbeUsage = LightProbeUsage.Off;
        rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sharedMaterial = ParticleMat(tex, additive: !mist);
        return ps;
    }

    static Material ParticleMat(Texture2D tex, bool additive)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.name = additive ? "WaterDropAdd" : "WaterMist";
        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        mat.color = Color.white;
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", additive ? 2f : 0f);
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 0f);
        if (additive)
        {
            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        else
        {
            if (mat.HasProperty("_SrcBlend"))
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    static Texture2D SoftDisc(int size, float falloff)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        tex.name = "WaterSoftDisc";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 0;
        float c = (size - 1) * 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                a = Mathf.Pow(a, falloff);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }
}
