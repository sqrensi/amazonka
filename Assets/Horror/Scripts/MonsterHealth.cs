using System;
using UnityEngine;

/// <summary>
/// Здоровье монстра. Монстра можно убить (альтернативная победа "KILL THE MONSTER").
/// HP намеренно высокое — убийство должно быть трудным и требовать хорошего оружия.
/// </summary>
public class MonsterHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField] float maxHealth = 300f;
    [SerializeField] float currentHealth = 300f;

    [Header("On death")]
    [SerializeField] Behaviour[] disableOnDeath;
    [SerializeField] Collider[] disableCollidersOnDeath;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthNormalized => maxHealth <= 0f ? 0f : currentHealth / maxHealth;
    public bool IsDead { get; private set; }

    public event Action<float, float> OnHealthChanged;
    public event Action<float> OnDamaged;
    public event Action OnDeath;

    void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    public void TakeDamage(float amount) => TakeDamage(amount, transform.position, Vector3.zero);

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (IsDead || amount <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        OnDamaged?.Invoke(amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
            Die();
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

        if (disableCollidersOnDeath != null)
        {
            foreach (var c in disableCollidersOnDeath)
                if (c != null)
                    c.enabled = false;
        }

        OnDeath?.Invoke();
        Debug.Log("[MonsterHealth] Монстр убит.");
    }
}
