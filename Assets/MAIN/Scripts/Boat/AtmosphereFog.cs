using UnityEngine;

/// <summary>
/// Над водой дистанционный туман выключен: линейный/экспонента красили дальнюю воду
/// по взгляду камеры. Под водой туман ставит UnderwaterFx.
/// </summary>
[DefaultExecutionOrder(90)]
public class AtmosphereFog : MonoBehaviour
{
    [SerializeField] Color air = new Color(0.74f, 0.80f, 0.84f, 1f);
    [SerializeField] Color nearWater = new Color(0.68f, 0.76f, 0.78f, 1f);
    [SerializeField] float densityHigh = 0.0042f;
    [SerializeField] float densityRiver = 0.0074f;
    [SerializeField] float hazeHeight = 36f;

    Camera _cam;
    Light _sun;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = Camera.main;
        _sun = RenderSettings.sun;
        if (_sun == null)
            _sun = FindFirstObjectByType<Light>();
    }

    void LateUpdate()
    {
        if (UnderwaterFx.Covering)
            return;
        if (RaceMood.Active != null)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = RaceMood.FogColor;
            RenderSettings.fogDensity = Mathf.Max(0.00012f, RaceMood.FogDensity);
            return;
        }
        RenderSettings.fog = false;
    }
}
