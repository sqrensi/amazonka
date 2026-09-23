#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

static class RiverWaterFiller
{
    const string PrefabPath = "Assets/MAIN/Prefabs/Boat/BoatWater.prefab";

    [MenuItem("Horror/Restore Water Materials")]
    static void FillMenu()
    {
        RestoreWaterMaterials();
    }

    static bool RestoreWaterMaterials()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/IgniteCoders/Simple Water Shader/Resources/Water_mat_01.mat");
        if (mat == null)
            return false;
        bool changed = false;
        var waters = Object.FindObjectsByType<BoatWater>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < waters.Length; i++)
        {
            var water = waters[i];
            if (water == null)
                continue;
            var rends = water.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < rends.Length; r++)
            {
                if (rends[r] == null || rends[r].sharedMaterial == mat)
                    continue;
                rends[r].sharedMaterial = mat;
                changed = true;
            }
            if (changed)
                EditorUtility.SetDirty(water);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            return changed;
        var prefabRends = prefab.GetComponentsInChildren<MeshRenderer>(true);
        bool prefabDirty = false;
        for (int i = 0; i < prefabRends.Length; i++)
        {
            if (prefabRends[i] == null || prefabRends[i].sharedMaterial == mat)
                continue;
            prefabRends[i].sharedMaterial = mat;
            prefabDirty = true;
        }
        if (prefabDirty)
        {
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            changed = true;
        }
        return changed;
    }
}
#endif
