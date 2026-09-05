using System;
using UnityEngine;

/// <summary>
/// Центральная система шума. Хранит общий уровень шума игрока (0..100),
/// последнее "услышанное" событие с позицией и рассылает события подписчикам (монстру).
///
/// Модель:
///  - Разовые звуки (шаги, приземление, в будущем выстрел/дверь) добавляют шум через Report().
///  - Уровень шума экспоненциально затухает со временем.
///  - По порогам вычисляется "уровень тревоги" ночи (Tier), от которого зависит агрессия монстра.
/// </summary>
[DisallowMultipleComponent]
public class NoiseManager : MonoBehaviour
{
    public enum Tier
    {
        Calm = 0,     // 0-20   : монстр почти не активен
        Explore = 1,  // 20-40  : начинает бродить по деревне
        Patrol = 2,   // 40-70  : патрулирует
        Hunt = 3       // 70-100 : охотится
    }

    public static NoiseManager Instance { get; private set; }

    [Header("Meter")]
    [SerializeField, Range(0f, 100f)] float noise;
    [Tooltip("Экспоненциальное затухание (доля в секунду).")]
    [SerializeField] float decayPerSecond = 0.85f;
    [Tooltip("Дополнительное линейное затухание, чтобы шкала гарантированно доходила до нуля.")]
    [SerializeField] float linearFloorDecay = 5f;

    [Header("Aggression thresholds")]
    [SerializeField] float exploreThreshold = 20f;
    [SerializeField] float patrolThreshold = 40f;
    [SerializeField] float huntThreshold = 70f;

    /// <summary>Текущий шум 0..100.</summary>
    public float Noise => noise;
    public float NoiseNormalized => noise * 0.01f;
    public Tier CurrentTier { get; private set; }

    public Vector3 LastNoisePosition { get; private set; }
    public float LastNoiseAmount { get; private set; }
    public float LastNoiseTime { get; private set; } = -999f;

    /// <summary>Вызывается при каждом заметном звуке: (позиция, громкость).</summary>
    public event Action<Vector3, float> OnNoise;
    /// <summary>Вызывается при смене уровня тревоги.</summary>
    public event Action<Tier> OnTierChanged;

    /// <summary>Достаёт существующий менеджер или создаёт новый (чтобы сцену не нужно было настраивать вручную).</summary>
    public static NoiseManager GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        Instance = FindFirstObjectByType<NoiseManager>();
        if (Instance == null)
        {
            var go = new GameObject("NoiseManager");
            Instance = go.AddComponent<NoiseManager>();
        }
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        noise -= noise * decayPerSecond * dt;
        noise = Mathf.MoveTowards(noise, 0f, linearFloorDecay * dt);
        noise = Mathf.Clamp(noise, 0f, 100f);
        UpdateTier();
    }

    void UpdateTier()
    {
        Tier newTier;
        if (noise >= huntThreshold) newTier = Tier.Hunt;
        else if (noise >= patrolThreshold) newTier = Tier.Patrol;
        else if (noise >= exploreThreshold) newTier = Tier.Explore;
        else newTier = Tier.Calm;

        if (newTier != CurrentTier)
        {
            CurrentTier = newTier;
            OnTierChanged?.Invoke(newTier);
        }
    }

    /// <summary>Сообщить о звуке в точке world-пространства с заданной громкостью (в единицах шкалы).</summary>
    public void Report(Vector3 position, float amount)
    {
        if (amount <= 0f)
            return;

        noise = Mathf.Clamp(noise + amount, 0f, 100f);
        LastNoisePosition = position;
        LastNoiseAmount = amount;
        LastNoiseTime = Time.time;
        UpdateTier();

        OnNoise?.Invoke(position, amount);
    }

    /// <summary>Принудительно задать уровень (для отладки / катаклизмов).</summary>
    public void SetNoise(float value) => noise = Mathf.Clamp(value, 0f, 100f);
}
