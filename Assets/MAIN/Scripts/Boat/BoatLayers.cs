using UnityEngine;

/// <summary>
/// Слои лодки и россыпи: физика не видит прицел.
/// Сваренная лодка (BoatPhysics) не сталкивается сама с собой.
/// Россыпь (BoatLoose) сталкивается друг с другом и с лодкой.
/// </summary>
public static class BoatLayers
{
    public const string PhysicsName = "BoatPhysics";
    public const string InteractName = "BoatInteract";
    public const string LooseName = "BoatLoose";
    public const string InteractVolumeName = "InteractVolume";

    static bool _ready;

    public static int Physics { get; private set; } = -1;
    public static int Interact { get; private set; } = -1;
    public static int Loose { get; private set; } = -1;

    public static void Ensure()
    {
        if (_ready)
            return;
        Physics = LayerMask.NameToLayer(PhysicsName);
        Interact = LayerMask.NameToLayer(InteractName);
        if (Interact < 0)
            Interact = LayerMask.NameToLayer(InteractName + " ");
        Loose = LayerMask.NameToLayer(LooseName);
        if (Physics >= 0)
            UnityEngine.Physics.IgnoreLayerCollision(Physics, Physics, true);
        if (Interact >= 0)
        {
            UnityEngine.Physics.IgnoreLayerCollision(Interact, Interact, true);
            if (Physics >= 0)
                UnityEngine.Physics.IgnoreLayerCollision(Interact, Physics, true);
            if (Loose >= 0)
                UnityEngine.Physics.IgnoreLayerCollision(Interact, Loose, true);
        }
        _ready = true;
    }

    public static int PhysicsLayer(bool assembled)
    {
        Ensure();
        if (assembled && Physics >= 0)
            return Physics;
        if (Loose >= 0)
            return Loose;
        return 0;
    }

    public static int InteractorMask()
    {
        Ensure();
        int mask = ~0;
        if (Physics >= 0)
            mask &= ~(1 << Physics);
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (ignore >= 0)
            mask &= ~(1 << ignore);
        int water = LayerMask.NameToLayer("Water");
        if (water >= 0)
            mask &= ~(1 << water);
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0)
            mask &= ~(1 << ui);
        return mask;
    }

    public static void StampBoatPart(GameObject go, BoatPiece piece, BoatPiece craft)
    {
        if (go == null || piece == null)
            return;
        var part = go.GetComponent<BoatPart>();
        if (part == null)
            part = go.AddComponent<BoatPart>();
        part.Piece = piece;
        part.Craft = craft != null ? craft : piece;
    }

    public static void SetSolidLayer(GameObject root, int layer)
    {
        if (root == null)
            return;
        root.layer = layer;
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null)
                continue;
            if (cols[i].gameObject.name == InteractVolumeName)
                continue;
            cols[i].gameObject.layer = layer;
        }
    }

    public static void BindPickup(Component host, bool inWorld)
    {
        if (host == null)
            return;
        Ensure();
        Transform root = host.transform;
        Transform vol = root.Find(InteractVolumeName);
        if (!inWorld)
        {
            if (vol != null)
                vol.gameObject.SetActive(false);
            return;
        }

        SetSolidLayer(root.gameObject, PhysicsLayer(false));
        if (Interact < 0)
            return;

        BoxCollider box;
        if (vol == null)
        {
            var go = new GameObject(InteractVolumeName);
            vol = go.transform;
            vol.SetParent(root, false);
            box = go.AddComponent<BoxCollider>();
        }
        else
        {
            box = vol.GetComponent<BoxCollider>();
            if (box == null)
                box = vol.gameObject.AddComponent<BoxCollider>();
        }

        box.isTrigger = true;
        vol.gameObject.layer = Interact;
        vol.gameObject.SetActive(true);
        var link = vol.GetComponent<InteractLink>();
        if (link == null)
            link = vol.gameObject.AddComponent<InteractLink>();
        link.Host = host;

        Bounds local = default;
        bool any = false;
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var col = cols[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            if (col.gameObject.name == InteractVolumeName)
                continue;
            Bounds wb = col.bounds;
            Vector3 min = wb.min;
            Vector3 max = wb.max;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? min.x : max.x,
                    (c & 2) == 0 ? min.y : max.y,
                    (c & 4) == 0 ? min.z : max.z);
                Vector3 lp = root.InverseTransformPoint(corner);
                if (!any)
                {
                    local = new Bounds(lp, Vector3.zero);
                    any = true;
                }
                else
                    local.Encapsulate(lp);
            }
        }
        if (!any)
            local = new Bounds(Vector3.zero, Vector3.one * 0.2f);
        box.center = local.center;
        box.size = local.size + Vector3.one * 0.06f;
        box.enabled = true;
    }
}
