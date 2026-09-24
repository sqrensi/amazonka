using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Светлее нейтральный тон, без тяжёлого ACES/виньетки.
/// </summary>
[DefaultExecutionOrder(85)]
public class GameLook : MonoBehaviour
{
    Volume _volume;

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
        color.postExposure.Override(0.62f);
        color.contrast.Override(10f);
        color.saturation.Override(18f);
        color.hueShift.Override(0f);

        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(6f);
        wb.tint.Override(-1f);
    }

    void LateUpdate()
    {
        if (_volume != null)
            _volume.weight = UnderwaterFx.Covering ? 0.12f : 1f;
    }
}
