using System;
using UnityEngine;

/// <summary>
/// Здоровье игрока и обработка смерти.
/// При смерти отключает указанные компоненты (контроллер движения и т.п.),
/// разблокирует курсор и рассылает события для UI/менеджера забега.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float currentHealth = 100f;

    [Tooltip("Мин. интервал между получениями урона (защита от мгновенного добивания).")]
    [SerializeField] float invulnerabilityTime = 0.4f;

    [Header("On death")]
    [Tooltip("Компоненты, которые выключаются при смерти (например, контроллер игрока).")]
    [SerializeField] Behaviour[] disableOnDeath;
    [SerializeField] bool freeCursorOnDeath = true;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthNormalized => maxHealth <= 0f ? 0f : currentHealth / maxHealth;
    public bool IsDead { get; private set; }

    /// <summary>(current, max)</summary>
    public event Action<float, float> OnHealthChanged;
    public event Action<float> OnDamaged; // amount
    public event Action OnDeath;
    public event Action OnRespawn;

    float _lastHitTime = -999f;

    void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    void Start()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(float amount) => TakeDamage(amount, transform.position, Vector3.zero);

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (IsDead || amount <= 0f)
            return;
        if (Time.time - _lastHitTime < invulnerabilityTime)
            return;

        _lastHitTime = Time.time;
        currentHealth = Mathf.Max(0f, currentHealth - amount);

        OnDamaged?.Invoke(amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
            Die();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>Полное восстановление (для эффекта "бессмертие"/аптечка из рулетки и т.п.).</summary>
    public void SetMaxHealth(float value, bool refill)
    {
        maxHealth = Mathf.Max(1f, value);
        if (refill)
            currentHealth = maxHealth;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    void Die()
    {
        if (IsDead)
            return;
        IsDead = true;

        if (disableOnDeath != null)
        {
            foreach (var b in disableOnDeath)
                if (b != null)
                    b.enabled = false;
        }

        if (freeCursorOnDeath)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        OnDeath?.Invoke();
        Debug.Log("[PlayerHealth] Игрок погиб.");
    }

    /// <summary>
    /// Оживить игрока: снять флаг смерти, восстановить HP и заново включить
    /// компоненты, отключённые при смерти (управление, ввод). Вызывается системой респавна.
    /// </summary>
    public void Revive(bool refillHealth = true)
    {
        IsDead = false;
        _lastHitTime = -999f;

        if (refillHealth)
            currentHealth = maxHealth;
        currentHealth = Mathf.Clamp(currentHealth, 1f, maxHealth);

        if (disableOnDeath != null)
        {
            foreach (var b in disableOnDeath)
                if (b != null)
                    b.enabled = true;
        }

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnRespawn?.Invoke();
    }
}
