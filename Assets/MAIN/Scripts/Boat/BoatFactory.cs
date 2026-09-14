using UnityEngine;

/// <summary>
/// Создаёт стройпредметы из CreatePrimitive (Cube / Cylinder / Capsule).
/// </summary>
public static class BoatFactory
{
    public static BoatMaterialItem CreateMaterial(BoatPieceKind kind)
    {
        var go = new GameObject(kind + "Item");
        if (kind == BoatPieceKind.Oar)
            BoatVisuals.BuildMountOar(go.transform);
        else
            BoatVisuals.Attach(
                go.transform,
                BoatVisuals.Shape(kind),
                BoatVisuals.VisualScale(kind, BoatVisuals.DefaultSize(kind)),
                BoatVisuals.VisualRotation(kind),
                BoatVisuals.MaterialFor(kind));
        var item = go.AddComponent<BoatMaterialItem>();
        item.SetKind(kind);
        item.SetWorldSize(BoatVisuals.DefaultSize(kind));
        return item;
    }

    public static NailItem CreateNail()
    {
        var go = new GameObject("NailItem");
        BoatVisuals.Attach(go.transform, PrimitiveType.Cylinder, new Vector3(0.03f, 0.08f, 0.03f), BoatVisuals.Metal);
        return go.AddComponent<NailItem>();
    }

    public static RopeItem CreateRope()
    {
        var go = new GameObject("RopeItem");
        BoatVisuals.Attach(go.transform, PrimitiveType.Cylinder, new Vector3(0.07f, 0.12f, 0.07f), BoatVisuals.Rope);
        return go.AddComponent<RopeItem>();
    }

    public static HammerItem CreateHammer()
    {
        var go = new GameObject("HammerItem");
        BoatVisuals.BuildHammer(go.transform);
        return go.AddComponent<HammerItem>();
    }

    public static SawItem CreateSaw()
    {
        var go = new GameObject("SawItem");
        BoatVisuals.BuildSaw(go.transform);
        return go.AddComponent<SawItem>();
    }

    public static HeldItem Create(string id)
    {
        switch (id)
        {
            case "Log": return CreateMaterial(BoatPieceKind.Log);
            case "Barrel": return CreateMaterial(BoatPieceKind.Barrel);
            case "Nail": return CreateNail();
            case "Rope": return CreateRope();
            case "Hammer": return CreateHammer();
            case "Saw": return CreateSaw();
            case "Oar": return CreateMaterial(BoatPieceKind.Oar);
            default: return CreateMaterial(BoatPieceKind.Plank);
        }
    }
}
