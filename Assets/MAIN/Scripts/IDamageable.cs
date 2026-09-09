using UnityEngine;

/// <summary>
/// Общий интерфейс для всего, что можно ранить (игрок, монстр, разрушаемые объекты).
/// Оружие/атаки работают через него, не зная конкретный тип цели.
/// </summary>
public interface IDamageable
{
    bool IsDead { get; }
    void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection);
}
