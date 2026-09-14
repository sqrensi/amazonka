using UnityEngine;

/// <summary>
/// Каталог префабов стройматериалов (заполняет BoatPrefabBuilder).
/// </summary>
public static class BoatCatalog
{
    public static BoatMaterialItem Plank;
    public static BoatMaterialItem Log;
    public static BoatMaterialItem Barrel;
    public static NailItem Nail;
    public static RopeItem Rope;
    public static HammerItem Hammer;
    public static SawItem Saw;
    public static BoatMaterialItem Oar;

    public static BoatMaterialItem ItemPrefab(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Log: return Log;
            case BoatPieceKind.Barrel: return Barrel;
            case BoatPieceKind.Oar: return Oar;
            default: return Plank;
        }
    }
}
