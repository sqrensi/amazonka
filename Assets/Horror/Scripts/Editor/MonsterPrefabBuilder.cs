using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Собирает префаб монстра на основе modели monster.fbx (Mixamo, humanoid).
/// Заглушка-капсула из первой версии автоматически заменяется настоящей моделью
/// при загрузке редактора (если в префабе ещё нет SkinnedMeshRenderer).
/// Также доступно через меню Horror -> Rebuild Monster Prefab.
/// </summary>
[InitializeOnLoad]
static class MonsterPrefabBuilder
{
    const string PrefabPath = "Assets/Horror/Monster/monster.prefab";
    const string FbxPath = "Assets/Horror/Monster/monster.fbx";

    const float BaseModelHeight = 1.8f;
    const float MonsterScale = 1.25f; // насколько монстр крупнее человека
    static float ModelHeight => BaseModelHeight * MonsterScale;

    static MonsterPrefabBuilder()
    {
        EditorApplication.delayCall += EnsureBuilt;
    }

    [MenuItem("Horror/Rebuild Monster Prefab")]
    static void RebuildFromMenu() => Build();

    static void EnsureBuilt()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureBuilt;
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        // Нет префаба или он всё ещё с заглушкой-капсулой (без скелетного меша) -> пересобрать с FBX.
        if (prefab == null || prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
            Build();
    }

    static void Build()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
        {
            Debug.LogWarning($"[MonsterPrefabBuilder] Не найден {FbxPath} — префаб монстра не пересобран.");
            return;
        }

        var root = new GameObject("monster");

        float radius = 0.32f * MonsterScale;

        // Коллайдер тела.
        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1; // Y
        capsule.height = ModelHeight;
        capsule.radius = radius;
        capsule.center = new Vector3(0f, ModelHeight * 0.5f, 0f);

        // Навигация.
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = radius + 0.05f;
        agent.height = ModelHeight;
        agent.baseOffset = 0f;
        agent.speed = 3.5f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1f;
        agent.autoBraking = true;

        var health = root.AddComponent<MonsterHealth>();
        var ai = root.AddComponent<MonsterAI>();

        // Модель из FBX как дочерний объект (увеличенная).
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * MonsterScale;

        // "Глаза" для проверки зрения — на уровне головы, чуть вперёд.
        var eyes = new GameObject("Eyes");
        eyes.transform.SetParent(root.transform, false);
        eyes.transform.localPosition = new Vector3(0f, 1.6f * MonsterScale, 0.25f * MonsterScale);

        // Связываем ссылки и подгоняем параметры под крупного монстра.
        var aiSo = new SerializedObject(ai);
        aiSo.FindProperty("eyes").objectReferenceValue = eyes.transform;
        aiSo.FindProperty("health").objectReferenceValue = health;
        aiSo.FindProperty("eyeHeight").floatValue = 1.6f * MonsterScale;
        aiSo.FindProperty("attackRange").floatValue = 2.2f * MonsterScale;
        aiSo.ApplyModifiedPropertiesWithoutUndo();

        var healthSo = new SerializedObject(health);
        var disableBehaviours = healthSo.FindProperty("disableOnDeath");
        disableBehaviours.arraySize = 1;
        disableBehaviours.GetArrayElementAtIndex(0).objectReferenceValue = ai;
        var disableColliders = healthSo.FindProperty("disableCollidersOnDeath");
        disableColliders.arraySize = 1;
        disableColliders.GetArrayElementAtIndex(0).objectReferenceValue = capsule;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder("Assets/Horror/Monster"))
            AssetDatabase.CreateFolder("Assets/Horror", "Monster");

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[MonsterPrefabBuilder] Префаб монстра пересобран с моделью {FbxPath}");
    }
}
