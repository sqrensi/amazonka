using UnityEngine;

/// <summary>
/// Фонарь как предмет в руке. Свет исходит из самой модели фонаря (а не из камеры).
/// Основное действие (ЛКМ) — включить/выключить. Мягкое мерцание опционально.
/// </summary>
public class FlashlightItem : HeldItem
{
    [Header("Flashlight")]
    [SerializeField] Light spot;
    [SerializeField] bool startsOn = true;
    [SerializeField] float baseIntensity = 220f;
    [SerializeField] bool flicker;
    [SerializeField] float flickerAmount = 18f;

    bool _on;

    public override void Initialize(PlayerInventory inventory, GameObject owner)
    {
        base.Initialize(inventory, owner);
        if (spot == null)
            spot = GetComponentInChildren<Light>(true);
        _on = startsOn;
    }

    public override void OnEquip()
    {
        ApplyLightState();
    }

    public override void OnUnequip()
    {
        // Свет уходит вместе со скрытым объектом; состояние вкл/выкл запоминаем.
        if (spot != null)
            spot.enabled = false;
    }

    public override void OnUseStart()
    {
        _on = !_on;
        ApplyLightState();
    }

    void Update()
    {
        if (spot == null)
            return;

        // По мере убирания к стене свет плавно гаснет; полностью убран — выключен.
        float lightFactor = 1f - RetractAmount;
        bool lit = _on && RetractAmount < 0.95f;
        spot.enabled = lit;
        if (!lit)
            return;

        float intensity = baseIntensity;
        if (flicker)
            intensity += (Mathf.PerlinNoise(Time.time * 9.1f, 0.37f) - 0.5f) * 2f * flickerAmount;
        spot.intensity = intensity * lightFactor;
    }

    void ApplyLightState()
    {
        if (spot != null)
        {
            spot.enabled = _on && RetractAmount < 0.95f;
            spot.intensity = baseIntensity * (1f - RetractAmount);
        }
    }
}
