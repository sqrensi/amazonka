using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class BoatPrefabBuilder
{
    const string ItemsDir = "Assets/MAIN/Prefabs/Items";
    const string BoatDir = "Assets/MAIN/Prefabs/Boat";
    const string MatsDir = "Assets/MAIN/Materials";

    static BoatPrefabBuilder()
    {
        EditorApplication.delayCall += Ensure;
    }

    [MenuItem("Horror/Rebuild Boat Prefabs")]
    static void Rebuild() => BuildAll();

    static void Ensure()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }
        BuildAll();
    }

    static void BuildAll()
    {
        EnsureFolders();
        SaveItem(BoatFactory.CreateMaterial(BoatPieceKind.Plank), "PlankItem", "Plank", 2);
        SaveItem(BoatFactory.CreateMaterial(BoatPieceKind.Log), "LogItem", "Log", 2);
        SaveItem(BoatFactory.CreateMaterial(BoatPieceKind.Barrel), "BarrelItem", "Barrel", 2);
        SaveItem(BoatFactory.CreateMaterial(BoatPieceKind.Oar), "MountOarItem", "Oar", 2);
        SaveItem(BoatFactory.CreateNail(), "NailItem", "Nail", 3);
        SaveItem(BoatFactory.CreateRope(), "RopeItem", "Rope", 3);
        SaveItem(BoatFactory.CreateHammer(), "HammerItem", "Hammer", 4);
        SaveItem(BoatFactory.CreateSaw(), "SawItem", "Saw", 4);
        BuildWater();
        BuildKit();
        WireCatalog();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Boat] Prefabs ready in " + BoatDir);
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/MAIN/Prefabs"))
            AssetDatabase.CreateFolder("Assets/MAIN", "Prefabs");
        if (!AssetDatabase.IsValidFolder(ItemsDir))
            AssetDatabase.CreateFolder("Assets/MAIN/Prefabs", "Items");
        if (!AssetDatabase.IsValidFolder(BoatDir))
            AssetDatabase.CreateFolder("Assets/MAIN/Prefabs", "Boat");
        if (!AssetDatabase.IsValidFolder(MatsDir))
            AssetDatabase.CreateFolder("Assets/MAIN", "Materials");
    }

    static Material UrpMat(string name, Color c, float metallic = 0f, float smooth = 0.22f)
    {
        string path = MatsDir + "/" + name + ".mat";
        Shader shader = BoatVisuals.FindUrpLit();
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (shader != null)
            mat.shader = shader;
        BoatVisuals.Tint(mat, c, metallic, smooth);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void StampUrp(GameObject go, Material vis)
    {
        var metal = BoatVisuals.Metal;
        var wood = BoatVisuals.Wood;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.gameObject.name;
            if (n == "Handle" || n == "Grip" || n == "Shaft" || n == "Neck")
                r.sharedMaterial = wood;
            else if (n == "Head" || n == "Face" || n == "Peen" || n == "Blade" || n == "Spine" || n == "Ferrule" || n.StartsWith("Tooth")
                || n == "Collar" || n.StartsWith("Oarlock") || n == "OarlockPin")
                r.sharedMaterial = metal;
            else
                r.sharedMaterial = vis;
        }
    }

    static void SaveItem(HeldItem item, string file, string display, int slot)
    {
        Material vis = BoatVisuals.Wood;
        if (item is BoatMaterialItem wood)
        {
            vis = BoatVisuals.MaterialFor(wood.Kind);
        }
        else if (item is NailItem)
            vis = BoatVisuals.Metal;
        else if (item is RopeItem)
            vis = BoatVisuals.Rope;
        else if (item is HammerItem)
            vis = BoatVisuals.WoodDark;
        else if (item is SawItem)
            vis = BoatVisuals.Metal;

        StampUrp(item.gameObject, vis);
        BoatVisuals.StripStaleVisuals(item.transform);
        if (item is BoatMaterialItem woodItem && woodItem.Kind == BoatPieceKind.Oar)
            BoatVisuals.ClearChild(item.transform, "Vis");

        var so = new SerializedObject(item);
        so.FindProperty("displayName").stringValue = display;
        so.FindProperty("preferredSlot").intValue = slot;
        var posProp = so.FindProperty("heldLocalPosition");
        var eulerProp = so.FindProperty("heldLocalEuler");
        if (posProp != null)
            posProp.vector3Value = Vector3.zero;
        if (eulerProp != null)
            eulerProp.vector3Value = Vector3.zero;
        var kindProp = so.FindProperty("kind");
        if (kindProp != null && item is BoatMaterialItem mat)
            kindProp.intValue = (int)mat.Kind;
        so.ApplyModifiedPropertiesWithoutUndo();
        string path = ItemsDir + "/" + file + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(item.gameObject, path);
        Object.DestroyImmediate(item.gameObject);
    }

    static Material WaterSurfaceMat()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/IgniteCoders/Simple Water Shader/Resources/Water_mat_01.mat");
        if (mat != null)
            return mat;
        return UrpMat("BoatWater", new Color(0.18f, 0.48f, 0.72f), 0.05f, 0.85f);
    }

    static void BuildWater()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/IgniteCoders/Simple Water Shader/Resources/WaterBlock_50m.mesh");
        var mat = WaterSurfaceMat();
        var go = new GameObject("BoatWater");
        go.SetActive(false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Bounds mb = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, new Vector3(50f, 1f, 50f));
        float depth = 14f;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(Mathf.Max(8f, mb.size.x), depth, Mathf.Max(8f, mb.size.z));
        box.center = new Vector3(mb.center.x, mb.max.y - depth * 0.5f, mb.center.z);
        var water = go.AddComponent<BoatWater>();
        var so = new SerializedObject(water);
        so.FindProperty("surfaceY").floatValue = mb.max.y;
        var speed = so.FindProperty("flowSpeed");
        if (speed != null)
            speed.floatValue = 4.5f;
        var flow = so.FindProperty("localFlow");
        if (flow != null)
            flow.vector3Value = Vector3.forward;
        so.ApplyModifiedPropertiesWithoutUndo();
        rend.sharedMaterial = mat;
        PrefabUtility.SaveAsPrefabAsset(go, BoatDir + "/BoatWater.prefab");
        Object.DestroyImmediate(go);
    }

    static void BuildKit()
    {
        var root = new GameObject("BoatKit");
        ScatterStack(root.transform, "PlankItem", 18, new Vector3(0f, 0.03f, 0f), 3, 0.3f, 0.05f);
        ScatterStack(root.transform, "LogItem", 10, new Vector3(1.2f, 0.16f, 0f), 5, 0.34f, 0.3f);
        ScatterStack(root.transform, "BarrelItem", 2, new Vector3(-1.4f, 0.32f, 0.2f), 2, 0.62f, 0.65f);
        ScatterStack(root.transform, "NailItem", 30, new Vector3(0.2f, 0.08f, 1.2f), 6, 0.13f, 0.1f);
        ScatterStack(root.transform, "RopeItem", 5, new Vector3(-0.9f, 0.12f, 1.15f), 5, 0.22f, 0.14f);
        Place(root.transform, "HammerItem", new Vector3(0.35f, 0.08f, -0.9f));
        Place(root.transform, "SawItem", new Vector3(-0.15f, 0.08f, -0.9f));
        Place(root.transform, "MountOarItem", new Vector3(-2.0f, 0.1f, 0f));
        PrefabUtility.SaveAsPrefabAsset(root, BoatDir + "/BoatKit.prefab");
        Object.DestroyImmediate(root);
    }

    static void ScatterStack(Transform parent, string itemFile, int count, Vector3 origin, int cols, float space, float lift)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemsDir + "/" + itemFile + ".prefab");
        if (prefab == null)
            return;
        int per = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));
        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int layer = i / cols;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(parent, false);
            inst.transform.localPosition = origin + new Vector3((col - (cols - 1) * 0.5f) * space, layer * lift, 0f);
            inst.transform.localRotation = Quaternion.identity;
        }
    }

    static void Scatter(Transform parent, string itemFile, int count, Vector3 origin, float spread)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemsDir + "/" + itemFile + ".prefab");
        if (prefab == null)
            return;
        for (int i = 0; i < count; i++)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(parent, false);
            inst.transform.localPosition = origin + new Vector3(
                Random.Range(-spread, spread), 0.05f * i, Random.Range(-spread, spread));
            inst.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
    }

    static void Place(Transform parent, string itemFile, Vector3 pos)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemsDir + "/" + itemFile + ".prefab");
        if (prefab == null)
            return;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.SetParent(parent, false);
        inst.transform.localPosition = pos;
    }

    static void WireCatalog()
    {
        BoatCatalog.Plank = Load<BoatMaterialItem>("PlankItem");
        BoatCatalog.Log = Load<BoatMaterialItem>("LogItem");
        BoatCatalog.Barrel = Load<BoatMaterialItem>("BarrelItem");
        BoatCatalog.Nail = Load<NailItem>("NailItem");
        BoatCatalog.Rope = Load<RopeItem>("RopeItem");
        BoatCatalog.Hammer = Load<HammerItem>("HammerItem");
        BoatCatalog.Saw = Load<SawItem>("SawItem");
        BoatCatalog.Oar = Load<BoatMaterialItem>("MountOarItem");
    }

    static T Load<T>(string file) where T : Component
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(ItemsDir + "/" + file + ".prefab");
        return go != null ? go.GetComponent<T>() : null;
    }
}
