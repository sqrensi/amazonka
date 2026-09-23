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
        Apply();
    }

    void Apply()
    {
        Vector3 pos = _cam != null ? _cam.transform.position : Vector3.zero;
        float waterY = 0f;
        bool water = BoatWater.TryHeight(pos, out waterY);
        float height = water ? pos.y - waterY : 18f;
        float low = 1f - Mathf.SmoothStep(0f, Mathf.Max(6f, hazeHeight), Mathf.Max(0f, height));

        Color fog = Color.Lerp(air, nearWater, low * 0.55f);
        fog = Color.Lerp(fog, Color.white, 0.12f);
        if (_sun != null && _cam != null)
        {
            float intoSun = Mathf.Clamp01(Vector3.Dot(_cam.transform.forward, -_sun.transform.forward));
            fog = Color.Lerp(fog, Color.Lerp(fog, _sun.color, 0.22f), intoSun * 0.22f);
        }

        RenderSettings.fog = false;
    }
}
