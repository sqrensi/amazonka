using UnityEngine;

/// <summary>
/// Фонарь. Свет принадлежит самому предмету: в руке, в полёте и на полу
/// остаётся включённым, пока игрок его не выключил.
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
    bool _stateInited;

    protected override void Awake()
    {
        base.Awake();
        if (spot == null)
            spot = GetComponentInChildren<Light>(true);
        if (!_stateInited)
        {
            _on = startsOn;
            _stateInited = true;
        }
        ApplyLightState();
    }

    public override void Initialize(PlayerInventory inventory, GameObject owner)
    {
        base.Initialize(inventory, owner);
        if (spot == null)
            spot = GetComponentInChildren<Light>(true);
    }

    public override void OnEquip()
    {
        base.OnEquip();
        ApplyLightState();
    }

    public override void OnUnequip()
    {
        // Только убрали в инвентарь (объект станет неактивным). При выбросе OnUnequip не зовём.
        base.OnUnequip();
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
        ApplyLightState();
    }

    void ApplyLightState()
    {
        if (spot == null)
            return;

        bool inHand = IsEquipped;
        bool lit = _on;
        spot.enabled = lit;
        if (!lit)
            return;

        float intensity = baseIntensity;
        if (flicker)
            intensity += (Mathf.PerlinNoise(Time.time * 9.1f, 0.37f) - 0.5f) * 2f * flickerAmount;
        float close = inHand ? RetractAmount : 0f;
        spot.intensity = intensity * Mathf.Lerp(1f, 0.62f, close);
    }
}
