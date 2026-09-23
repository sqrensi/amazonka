using UnityEngine;

/// <summary>
/// Кэш: коллайдер → деталь → лидер острова, без обхода иерархии.
/// </summary>
public class BoatPart : MonoBehaviour
{
    public BoatPiece Piece;
    public BoatPiece Craft;

    public static BoatPiece FromCollider(Collider col)
    {
        if (col == null)
            return null;
        var part = col.GetComponent<BoatPart>();
        if (part != null)
            return part.Piece;
        return col.GetComponentInParent<BoatPiece>();
    }
}
