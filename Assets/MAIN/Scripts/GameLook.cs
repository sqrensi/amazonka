using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Светлее нейтральный тон, без тяжёлого ACES/виньетки.
/// </summary>
[DefaultExecutionOrder(85)]
public class GameLook : MonoBehaviour
{
    ColorAdjustments _color;
    WhiteBalance _wb;
    Volume _volume;
    float _exposure;
    float _contrast;
    float _sat;
    float _temp;
    float _tint;
    float _exposureTo;
    float _contrastTo;
    float _satTo;
    float _tempTo;
    float _tintTo;

    void Awake()
    {
        var go = new GameObject("GameLookVolume");
        go.transform.SetParent(transform, false);
        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 20f;
        _volume.weight = 1f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volume.profile = profile;

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.Neutral);

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.12f);
        bloom.threshold.Override(1.15f);
        bloom.scatter.Override(0.62f);
        bloom.clamp.Override(24f);

        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.05f);
        grain.response.Override(0.8f);

        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.04f);
        vig.smoothness.Override(0.4f);
        vig.color.Override(new Color(0.08f, 0.09f, 0.1f));

        var color = profile.Add<ColorAdjustments>(true);
        _color = color;
        color.postExposure.Override(0.62f);
        color.contrast.Override(10f);
        color.saturation.Override(18f);
        color.hueShift.Override(0f);
        _exposure = _exposureTo = 0.62f;
        _contrast = _contrastTo = 10f;
        _sat = _satTo = 18f;

        var wb = profile.Add<WhiteBalance>(true);
        _wb = wb;
        wb.temperature.Override(6f);
        wb.tint.Override(-1f);
        _temp = _tempTo = 6f;
        _tint = _tintTo = -1f;
    }

    public void SetRound(float exposure, float contrast, float sat, float temp, float tint, bool instant = false)
    {
        _exposureTo = exposure;
        _contrastTo = contrast;
        _satTo = sat;
        _tempTo = temp;
        _tintTo = tint;
        if (instant)
            SnapGrade();
    }

    void SnapGrade()
    {
        _exposure = _exposureTo;
        _contrast = _contrastTo;
        _sat = _satTo;
        _temp = _tempTo;
        _tint = _tintTo;
        ApplyGrade();
    }

    void ApplyGrade()
    {
        if (_color != null)
        {
            _color.postExposure.Override(_exposure);
            _color.contrast.Override(_contrast);
            _color.saturation.Override(_sat);
        }
        if (_wb != null)
        {
            _wb.temperature.Override(_temp);
            _wb.tint.Override(_tint);
        }
    }

    void LateUpdate()
    {
        float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 0.55f);
        _exposure = Mathf.Lerp(_exposure, _exposureTo, k);
        _contrast = Mathf.Lerp(_contrast, _contrastTo, k);
        _sat = Mathf.Lerp(_sat, _satTo, k);
        _temp = Mathf.Lerp(_temp, _tempTo, k);
        _tint = Mathf.Lerp(_tint, _tintTo, k);
        ApplyGrade();
        if (_volume != null)
            _volume.weight = UnderwaterFx.Covering ? 0.12f : 1f;
    }
}
