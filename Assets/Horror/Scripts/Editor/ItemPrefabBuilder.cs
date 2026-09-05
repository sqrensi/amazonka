using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Собирает из кода префабы предметов (как <see cref="HorrorPlayerPrefabBuilder"/>):
///  - предметы "в руке": FlashlightItem (модель фонаря + Spot Light), PistolItem (модель пистолета);
///  - подбираемые предметы для сцены: FlashlightPickup, PistolPickup (модель + коллайдер + <see cref="WorldItemPickup"/>).
///
/// Фонарь и пистолет НЕ выдаются игроку — их кладут в сцену как *Pickup, и игрок находит их сам.
/// Пересобрать вручную: меню Horror -> Rebuild Item Prefabs.
/// </summary>
[InitializeOnLoad]
static class ItemPrefabBuilder
{
    const string ItemsDir = "Assets/Horror/Prefabs/Items";
    const string PickupsDir = "Assets/Horror/Prefabs/Pickups";

    const string FlashlightModel = "Assets/Horror/Flashlight/FlashLight.prefab";
    const string PistolModel = "Assets/Horror/Gun/pistol4.prefab";

    const string FlashlightItemPath = ItemsDir + "/FlashlightItem.prefab";
    const string PistolItemPath = ItemsDir + "/PistolItem.prefab";
    const string FlashlightPickupPath = PickupsDir + "/FlashlightPickup.prefab";
    const string PistolPickupPath = PickupsDir + "/PistolPickup.prefab";

    static ItemPrefabBuilder()
    {
        EditorApplication.delayCall += EnsureBuilt;
    }

    [MenuItem("Horror/Rebuild Item Prefabs")]
    static void RebuildFromMenu()
    {
        bool ok = EditorUtility.DisplayDialog(
            "Пересобрать префабы предметов?",
            "Полная пересборка СБРОСИТ ручные настройки положения предметов в руке " +
            "(Held Local Position / Euler / Scale) к значениям по умолчанию.\n\n" +
            "Если нужно только починить ссылки (Muzzle, апгрейд) — используй " +
            "\"Horror/Fix Item References\", он не трогает позиции.",
            "Пересобрать (сбросит позиции)", "Отмена");
        if (ok)
            BuildAll();
    }

    [MenuItem("Horror/Fix Item References")]
    static void FixFromMenu() => FixItemReferences();

    static void EnsureBuilt()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureBuilt;
            return;
        }

        // Строим с нуля ТОЛЬКО если префаб вообще отсутствует (первое создание).
        // Существующие префабы не пересобираем автоматически, чтобы не сбрасывать
        // вручную настроенные положения предметов в руке.
        bool anyMissing =
            AssetDatabase.LoadAssetAtPath<GameObject>(PistolItemPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(FlashlightItemPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(FlashlightPickupPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(PistolPickupPath) == null;

        if (anyMissing)
            BuildAll();
        else
            FixItemReferences(); // безопасно: чиним только ссылки, позиции не трогаем
    }

    // ------------------------------------------------------------- Non-destructive fixes

    /// <summary>
    /// Чинит ссылки в существующих префабах, НЕ меняя положение/поворот/масштаб:
    ///  - у пистолета привязывает поле Muzzle (создаёт дочерний "Muzzle", если его нет);
    ///  - у фонаря проставляет результат апгрейда (пистолет), если он не задан.
    /// </summary>
    static void FixItemReferences()
    {
        bool a = FixPistolMuzzle();
        bool b = FixFlashlightUpgrade();
        if (a || b)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[ItemPrefabBuilder] Ссылки предметов починены (Muzzle/апгрейд), положения сохранены.");
        }
    }

    static bool FixPistolMuzzle()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PistolItemPath) == null)
            return false;

        var root = PrefabUtility.LoadPrefabContents(PistolItemPath);
        bool changed = false;
        try
        {
            var item = root.GetComponent<PistolItem>();
            if (item != null)
            {
                Transform muzzle = root.transform.Find("Muzzle");
                if (muzzle == null)
                {
                    var m = new GameObject("Muzzle");
                    m.transform.SetParent(root.transform, false);
                    m.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                    m.transform.localRotation = Quaternion.identity;
                    muzzle = m.transform;
                    changed = true;
                }

                var so = new SerializedObject(item);
                var p = so.FindProperty("muzzle");
                if (p != null && p.objectReferenceValue != (Object)muzzle)
                {
                    p.objectReferenceValue = muzzle;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, PistolItemPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return changed;
    }

    static bool FixFlashlightUpgrade()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FlashlightItemPath) == null)
            return false;

        var pistolAsset = AssetDatabase.LoadAssetAtPath<HeldItem>(PistolItemPath);
        if (pistolAsset == null)
            return false;

        var root = PrefabUtility.LoadPrefabContents(FlashlightItemPath);
        bool changed = false;
        try
        {
            var item = root.GetComponent<HeldItem>();
            if (item != null)
            {
                var so = new SerializedObject(item);
                var p = so.FindProperty("upgradeResultPrefab");
                if (p != null && p.objectReferenceValue == null)
                {
                    p.objectReferenceValue = pistolAsset;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, FlashlightItemPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return changed;
    }

    static void BuildAll()
    {
        EnsureFolders();

        // Пистолет строим первым — фонарь ссылается на него как на результат апгрейда.
        BuildPistolItem();
        var pistolItem = AssetDatabase.LoadAssetAtPath<HeldItem>(PistolItemPath);
        BuildFlashlightItem(pistolItem);

        var flashlightItem = AssetDatabase.LoadAssetAtPath<HeldItem>(FlashlightItemPath);

        BuildPickup(FlashlightPickupPath, FlashlightModel, flashlightItem, "Фонарь");
        BuildPickup(PistolPickupPath, PistolModel, pistolItem, "Пистолет");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ItemPrefabBuilder] Префабы предметов и подбираемых предметов пересобраны.");
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Horror/Prefabs"))
            AssetDatabase.CreateFolder("Assets/Horror", "Prefabs");
        if (!AssetDatabase.IsValidFolder(ItemsDir))
            AssetDatabase.CreateFolder("Assets/Horror/Prefabs", "Items");
        if (!AssetDatabase.IsValidFolder(PickupsDir))
            AssetDatabase.CreateFolder("Assets/Horror/Prefabs", "Pickups");
    }

    // ------------------------------------------------------------- Held items

    static void BuildFlashlightItem(HeldItem upgradeResult)
    {
        var root = new GameObject("FlashlightItem");
        var item = root.AddComponent<FlashlightItem>();

        GameObject model = InstantiateModel(FlashlightModel);
        if (model != null)
        {
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            StripColliders(model);
        }

        // Источник света исходит из модели фонаря, светит вперёд (по камере).
        var lightGO = new GameObject("Spot");
        lightGO.transform.SetParent(root.transform, false);
        lightGO.transform.localPosition = new Vector3(0f, 0f, 0.1f);
        lightGO.transform.localRotation = Quaternion.identity;

        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = 16f;
        light.spotAngle = 58f;
        light.innerSpotAngle = 28f;
        light.color = new Color(1f, 0.93f, 0.78f);
        light.intensity = 220f;
        light.shadows = LightShadows.Soft;
        lightGO.AddComponent<UniversalAdditionalLightData>();

        var so = new SerializedObject(item);
        so.FindProperty("displayName").stringValue = "Фонарь";
        so.FindProperty("preferredSlot").intValue = 0;
        // Фонарь можно улучшить до пистолета (пистолет > фонарь).
        so.FindProperty("upgradeResultPrefab").objectReferenceValue = upgradeResult;
        SetVector3(so, "heldLocalPosition", new Vector3(0f, 0f, 0f));
        SetVector3(so, "heldLocalEuler", new Vector3(0f, 0f, 0f));
        SetVector3(so, "heldLocalScale", Vector3.one);
        so.FindProperty("spot").objectReferenceValue = light;
        so.FindProperty("startsOn").boolValue = true;
        so.FindProperty("baseIntensity").floatValue = 220f;
        so.ApplyModifiedPropertiesWithoutUndo();

        Save(root, FlashlightItemPath);
    }

    static void BuildPistolItem()
    {
        var root = new GameObject("PistolItem");
        var item = root.AddComponent<PistolItem>();

        GameObject model = InstantiateModel(PistolModel);
        if (model != null)
        {
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            StripColliders(model);
        }

        // Дуло: точка вылета пули и старт трассера. Смещено вперёд от корня предмета;
        // при необходимости подвинуть под конкретную модель в префабе.
        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(root.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 0.3f);
        muzzle.transform.localRotation = Quaternion.identity;

        var so = new SerializedObject(item);
        so.FindProperty("displayName").stringValue = "Пистолет";
        so.FindProperty("preferredSlot").intValue = 1;
        SetVector3(so, "heldLocalPosition", new Vector3(0f, 0f, 0f));
        SetVector3(so, "heldLocalEuler", new Vector3(0f, 90f, 0f));
        SetVector3(so, "heldLocalScale", Vector3.one);
        so.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        Save(root, PistolItemPath);
    }

    // ------------------------------------------------------------- World pickups

    static void BuildPickup(string path, string modelPath, HeldItem itemPrefab, string displayName)
    {
        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));

        GameObject model = InstantiateModel(modelPath);
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 0.3f);
        if (model != null)
        {
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            StripColliders(model);
            bounds = CalculateLocalBounds(root);
        }

        // Коллайдер для наводки лучом (взгляд игрока) — по габаритам модели, но не крошечный.
        var box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = Vector3.Max(bounds.size, Vector3.one * 0.25f);

        var pickup = root.AddComponent<WorldItemPickup>();
        var so = new SerializedObject(pickup);
        so.FindProperty("itemPrefab").objectReferenceValue = itemPrefab;
        so.FindProperty("displayName").stringValue = displayName;
        so.ApplyModifiedPropertiesWithoutUndo();

        Save(root, path);
    }

    // ------------------------------------------------------------- Helpers

    static GameObject InstantiateModel(string modelPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ItemPrefabBuilder] Не найдена модель {modelPath}.");
            return null;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        return instance;
    }

    static void StripColliders(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);
    }

    static Bounds CalculateLocalBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one * 0.3f);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        // Переносим из мира в локальные координаты корня (корень в origin с identity — совпадает).
        b.center = root.transform.InverseTransformPoint(b.center);
        return b;
    }

    static void SetVector3(SerializedObject so, string prop, Vector3 value)
    {
        var p = so.FindProperty(prop);
        if (p != null)
            p.vector3Value = value;
    }

    static void Save(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}
