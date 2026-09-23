using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Обычный подводный вид. На выходе эффект держится, пока near-clip
/// режет поверхность — кадр закрыт цветом воды, дно без тумана не мелькает.
/// </summary>
[DefaultExecutionOrder(80)]
public class UnderwaterFx : MonoBehaviour
{
    [SerializeField] Color waterFog = new Color(0.07f, 0.2f, 0.24f, 1f);
    [SerializeField] Color tint = new Color(0.14f, 0.4f, 0.44f, 0.38f);
    [SerializeField] float fogDensity = 0.07f;

    public static bool Covering { get; private set; }

    Camera _cam;
    bool _latched;
    float _fogStart;
    float _fogEnd;
    bool _fogWas;
    FogMode _fogMode;
    Color _fogColor;
    float _fogDensity;
    Color _ambient;
    Color _background;
    float _nearClip = 0.08f;
    bool _saved;

    Image _veil;
    Volume _volume;
    ColorAdjustments _color;
    Vignette _vignette;
    ChromaticAberration _chroma;
    ParticleSystem _bubbles;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = Camera.main;
        if (_cam != null)
            _nearClip = _cam.nearClipPlane;
        var urp = GetComponent<UniversalAdditionalCameraData>();
        if (urp == null && _cam != null)
            urp = _cam.GetComponent<UniversalAdditionalCameraData>();
        if (urp != null)
        {
            urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            urp.antialiasingQuality = AntialiasingQuality.High;
        }
        BuildUi();
        BuildVolume();
        BuildBubbles();
    }

    void OnDisable()
    {
        RestoreAir();
        _latched = false;
        Covering = false;
        ApplyVisuals(false, 0f, 0f);
    }

    void LateUpdate()
    {
        if (_cam == null)
            return;

        Vector3 pos = _cam.transform.position;
        bool overWater = BoatWater.TryHeight(pos, out float surfaceY);
        float signed = overWater ? surfaceY - pos.y : -999f;
        float slab = Mathf.Max(_nearClip, 0.08f);

        if (overWater && signed > 0.02f)
            _latched = true;
        else if (!overWater || signed < -slab)
            _latched = false;

        bool under = _latched || (overWater && signed > 0.02f);
        Covering = under;
        ApplyVisuals(under, signed, slab);
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("UnderwaterVeil");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _cam;
        canvas.planeDistance = 0.18f;
        canvas.sortingOrder = 40;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        var panel = new GameObject("Tint");
        panel.transform.SetParent(canvasGo.transform, false);
        _veil = panel.AddComponent<Image>();
        _veil.raycastTarget = false;
        _veil.color = Color.clear;
        var rt = _veil.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void BuildVolume()
    {
        var go = new GameObject("UnderwaterVolume");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 50f;
        _volume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volume.profile = profile;

        _color = profile.Add<ColorAdjustments>(true);
        _color.colorFilter.Override(new Color(0.55f, 0.82f, 0.86f));
        _color.postExposure.Override(0f);
        _color.contrast.Override(6f);
        _color.saturation.Override(-10f);

        _vignette = profile.Add<Vignette>(true);
        _vignette.intensity.Override(0.28f);
        _vignette.smoothness.Override(0.55f);
        _vignette.color.Override(new Color(0.04f, 0.12f, 0.14f));

        _chroma = profile.Add<ChromaticAberration>(true);
        _chroma.intensity.Override(0.08f);
    }

    void BuildBubbles()
    {
        var go = new GameObject("Bubbles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -0.25f, 0.7f);
        _bubbles = go.AddComponent<ParticleSystem>();
        var main = _bubbles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 2.4f;
        main.startSpeed = 0.22f;
        main.startSize = 0.025f;
        main.startColor = new Color(0.75f, 0.92f, 1f, 0.4f);
        main.gravityModifier = -0.12f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 16;
        var emission = _bubbles.emission;
        emission.rateOverTime = 0f;
        var shape = _bubbles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.4f, 0.5f, 1.4f);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = new Color(0.8f, 0.95f, 1f, 0.35f);
                renderer.sharedMaterial = mat;
            }
        }
    }

    void ApplyVisuals(bool under, float signedDepth, float slab)
    {
        if (under)
            CaptureAir();
        else if (_saved)
            RestoreAir();

        // Выход: глаз уже над водой, меш клипается — закрываем дыру цветом поверхности.
        float crossing = 0f;
        if (under && signedDepth < slab)
            crossing = 1f - Mathf.Clamp01((signedDepth + slab) / (slab + 0.08f));

        if (_veil != null)
        {
            Color c = tint;
            c.a = under ? Mathf.Lerp(tint.a, Mathf.Max(tint.a, 0.72f), crossing) : 0f;
            _veil.color = c;
        }

        if (_volume != null)
            _volume.weight = under ? 1f : 0f;

        if (_bubbles != null)
        {
            var emission = _bubbles.emission;
            emission.rateOverTime = under && signedDepth > 0.08f ? 5f : 0f;
            if (under && signedDepth > 0.08f && !_bubbles.isPlaying)
                _bubbles.Play();
            if ((!under || signedDepth <= 0.08f) && _bubbles.isPlaying)
                _bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        if (!under || !_saved)
            return;

        float depth01 = Mathf.Clamp01(Mathf.Max(0f, signedDepth) / 3.5f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.Lerp(_fogColor, waterFog, 1f);
        RenderSettings.fogDensity = Mathf.Lerp(_fogDensity < 0.0001f ? 0.003f : _fogDensity, fogDensity + depth01 * 0.03f, 1f);
        RenderSettings.ambientLight = Color.Lerp(_ambient, Color.Lerp(_ambient, waterFog * 1.8f, 0.65f), 1f);
        if (_cam != null)
            _cam.backgroundColor = waterFog;
    }

    void CaptureAir()
    {
        if (_saved)
            return;
        _fogWas = RenderSettings.fog;
        _fogMode = RenderSettings.fogMode;
        _fogColor = RenderSettings.fogColor;
        _fogDensity = RenderSettings.fogDensity;
        _fogStart = RenderSettings.fogStartDistance;
        _fogEnd = RenderSettings.fogEndDistance;
        _ambient = RenderSettings.ambientLight;
        _background = _cam != null ? _cam.backgroundColor : Color.black;
        if (_cam != null)
            _nearClip = _cam.nearClipPlane;
        _saved = true;
    }

    void RestoreAir()
    {
        if (!_saved)
            return;
        RenderSettings.fog = _fogWas;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogColor = _fogColor;
        RenderSettings.fogDensity = _fogDensity;
        RenderSettings.fogStartDistance = _fogStart;
        RenderSettings.fogEndDistance = _fogEnd;
        RenderSettings.ambientLight = _ambient;
        if (_cam != null)
            _cam.backgroundColor = _background;
        _saved = false;
        if (_volume != null)
            _volume.weight = 0f;
        if (_veil != null)
            _veil.color = Color.clear;
    }
}
