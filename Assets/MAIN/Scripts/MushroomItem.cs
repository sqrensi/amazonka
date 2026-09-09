using UnityEngine;

/// <summary>
/// Мухомор: один префаб в мире и в руке. ЛКМ — съесть (предмет пропадает).
/// </summary>
public class MushroomItem : HeldItem
{
    public override void OnUseStart()
    {
        if (IsUseBlocked || Inventory == null)
            return;
        Inventory.DestroyEquipped();
    }

    void OnCollisionEnter(Collision collision)
    {
        TryKillSlime(collision.collider);
    }

    /// <summary>Убивает только бросок игрока, пока мухомор ещё летит — не лежащий на земле.</summary>
    public bool IsPlayerThrowHit
    {
        get
        {
            if (IsCarried || !WasDroppedByPlayer)
                return false;
            var rb = GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic)
                return false;
            return rb.linearVelocity.sqrMagnitude >= 1.15f * 1.15f;
        }
    }

    void TryKillSlime(Collider col)
    {
        if (col == null || !IsPlayerThrowHit)
            return;
        var slime = col.GetComponentInParent<SlimeMonster>();
        slime?.HitByMushroom(this);
    }
}
