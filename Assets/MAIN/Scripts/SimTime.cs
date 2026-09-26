using UnityEngine;

/// <summary>
/// Смешивание «за тик физики» (50 Гц), чтобы симуляция не зависела от FPS.
/// </summary>
public static class SimTime
{
    public const float Tick = 0.02f;

    public static float Dt => Time.fixedDeltaTime;

    public static float Blend(float alphaAtTick, float dt)
    {
        alphaAtTick = Mathf.Clamp01(alphaAtTick);
        if (alphaAtTick <= 0.0001f)
            return 0f;
        if (alphaAtTick >= 0.999f)
            return 1f;
        float k = -Mathf.Log(1f - alphaAtTick) / Tick;
        return 1f - Mathf.Exp(-k * dt);
    }

    public static float VisualAlpha()
    {
        if (Time.timeScale < 0.5f)
            return 1f;
        float step = Time.fixedDeltaTime;
        if (step < 0.00001f)
            return 1f;
        return Mathf.Clamp01((Time.time - Time.fixedTime) / step);
    }
}
