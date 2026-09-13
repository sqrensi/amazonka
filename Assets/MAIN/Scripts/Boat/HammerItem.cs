using UnityEngine;

/// <summary>
/// Молоток: забивает гвоздь. Если гвоздя нет, но в инвентаре есть гвоздь — ставит и сразу забивает.
/// </summary>
public class HammerItem : HeldItem
{
    public override void OnEquip()
    {
        base.OnEquip();
        BoatBuildHud.Hint("LMB drive nails", 3f);
    }

    public override void OnUseStart()
    {
        if (IsUseBlocked)
            return;
        if (!BoatBuildUtil.Aim(Owner, 4.5f, out RaycastHit hit, preferPieces: true))
            return;

        var nail = hit.collider.GetComponentInParent<BoatNail>();
        if (nail == null)
            nail = FindNail(hit.point, 0.18f);

        if (nail != null && !nail.Driven)
        {
            nail.Drive();
            AddKick(new Vector3(0f, 0.015f, -0.04f), new Vector3(-12f, 0f, 2f));
            BoatBuildHud.Hint("Nailed");
            return;
        }

        if (!BoatPiece.TryFindPair(hit.point, 0.48f, out BoatPiece a, out BoatPiece b))
        {
            BoatBuildHud.Hint("No nail here");
            return;
        }

        if (Inventory == null || !Inventory.ConsumeFirst<NailItem>())
        {
            BoatBuildHud.Hint("Need a nail");
            return;
        }

        Vector3 dir = Vector3.up;
        nail = BoatBuildUtil.SpawnNail(a, b, hit.point, dir);
        nail.Drive();
        AddKick(new Vector3(0f, 0.015f, -0.04f), new Vector3(-12f, 0f, 2f));
        BoatBuildHud.Hint("Nailed");
    }

    static BoatNail FindNail(Vector3 p, float r)
    {
        var cols = Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Collide);
        BoatNail best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < cols.Length; i++)
        {
            var n = cols[i].GetComponentInParent<BoatNail>();
            if (n == null || n.Driven)
                continue;
            float d = (n.transform.position - p).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = n;
            }
        }
        return best;
    }
}
