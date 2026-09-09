using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class SlimeMonsterPrefabBuilder
{
    const string ModelPath = "Assets/RPG Monster DUO PBR Polyart/Prefabs/SlimePBR.prefab";
    const string OutPath = "Assets/MAIN/Prefabs/SlimeMonster.prefab";

    static SlimeMonsterPrefabBuilder()
    {
        EditorApplication.delayCall += Ensure;
    }

    [MenuItem("Horror/Rebuild Slime Monster Prefab")]
    static void Rebuild() => Build(true);

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Ensure;
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(OutPath) == null)
            Build(false);
        else
            AssignHitClip();
    }

    static void AssignHitClip()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutPath);
        if (prefab == null)
            return;
        var slime = prefab.GetComponent<SlimeMonster>();
        if (slime == null)
            return;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MAIN/Sounds/Hit.wav");
        if (clip == null)
            return;
        var so = new SerializedObject(slime);
        var p = so.FindProperty("hitClip");
        if (p == null || p.objectReferenceValue != null)
            return;
        p.objectReferenceValue = clip;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(slime);
        AssetDatabase.SaveAssets();
    }

    static void Build(bool force)
    {
        if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(OutPath) != null)
            return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogWarning("[SlimeMonster] Missing " + ModelPath);
            return;
        }

        var root = new GameObject("SlimeMonster");
        var cc = root.AddComponent<CharacterController>();
        cc.height = 0.95f;
        cc.radius = 0.42f;
        cc.center = new Vector3(0f, 0.48f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.25f;
        cc.skinWidth = 0.06f;
        cc.minMoveDistance = 0.001f;

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "SlimePBR";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        var anim = visual.GetComponent<Animator>();
        if (anim != null)
            anim.applyRootMotion = false;

        root.AddComponent<SlimeMonster>();
        var hit = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MAIN/Sounds/Hit.wav");
        if (hit != null)
        {
            var so = new SerializedObject(root.GetComponent<SlimeMonster>());
            so.FindProperty("hitClip").objectReferenceValue = hit;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!AssetDatabase.IsValidFolder("Assets/MAIN/Prefabs"))
            AssetDatabase.CreateFolder("Assets/MAIN", "Prefabs");

        PrefabUtility.SaveAsPrefabAsset(root, OutPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[SlimeMonster] Saved " + OutPath);
    }
}
