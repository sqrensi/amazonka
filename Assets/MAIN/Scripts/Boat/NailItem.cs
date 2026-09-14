using UnityEngine;

/// <summary>
/// Гвоздь: ЛКМ ставит гвоздь между двумя соприкасающимися деталями. Молоток потом забивает.
/// </summary>
public class NailItem : HeldItem
{
    public override void OnEquip()
    {
        base.OnEquip();
        BoatBuildHud.Hint("LMB plant nail where two pieces meet", 3.5f);
    }

    public override void OnUseStart()
    {
        if (IsUseBlocked)
            return;
        if (!BoatBuildUtil.Aim(Owner, 4.5f, out RaycastHit hit, preferPieces: true))
        {
            BoatBuildHud.Hint("Aim at a joint");
            return;
        }
        var aimed = hit.collider.GetComponentInParent<BoatPiece>();
        if (!BoatPiece.TryFindPair(hit.point, 0.28f, aimed, out BoatPiece a, out BoatPiece b))
        {
            BoatBuildHud.Hint("Need two pieces touching");
            return;
        }
        Vector3 dir = hit.normal;
        BoatBuildUtil.SpawnNail(a, b, hit.point, dir);
        Inventory?.DestroyEquipped();
        BoatBuildHud.Hint("Hammer the nail");
        AddKick(new Vector3(0f, 0.01f, -0.02f), new Vector3(-6f, 0f, 0f));
    }
}
