using UnityEngine;

/// <summary>
/// Верёвка: первый клик — якорь, второй — связать две детали.
/// </summary>
public class RopeItem : HeldItem
{
    public override float WaterLift() => 24f;
    public override float WaterCurrent() => 1.25f;
    BoatPiece _startPiece;
    Vector3 _startPoint;

    public override void OnEquip()
    {
        base.OnEquip();
        BoatBuildHud.Hint("LMB first piece, LMB second piece", 4f);
    }

    public override void OnUnequip()
    {
        _startPiece = null;
        base.OnUnequip();
    }

    public override void OnUseStart()
    {
        if (IsUseBlocked)
            return;
        if (!BoatBuildUtil.Aim(Owner, 4.5f, out RaycastHit hit))
            return;
        var piece = hit.collider.GetComponentInParent<BoatPiece>();
        if (piece == null)
        {
            BoatBuildHud.Hint("Aim at a piece");
            return;
        }

        if (_startPiece == null)
        {
            _startPiece = piece;
            _startPoint = hit.point;
            BoatBuildHud.Hint("Now the other piece");
            return;
        }

        if (piece == _startPiece)
        {
            BoatBuildHud.Hint("Pick a different piece");
            return;
        }

        var go = new GameObject("Rope");
        var rope = go.AddComponent<BoatRope>();
        rope.Bind(_startPiece, _startPoint, piece, hit.point);
        BoatIsland.Refresh(_startPiece);
        BoatIsland.Refresh(piece);
        _startPiece = null;
        Inventory?.DestroyEquipped();
        BoatBuildHud.Hint("Rope tied");
    }
}
