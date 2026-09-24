#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

static class RockfallBuilder
{
    [MenuItem("Horror/Boat Race/Place Rockfall Points Along River")]
    static void Place()
    {
        var path = Object.FindFirstObjectByType<BoatCurrentPath>();
        if (path == null)
        {
            EditorUtility.DisplayDialog("Rockfall", "No BoatCurrentPath in the scene.", "OK");
            return;
        }

        var existingRoot = GameObject.Find("RockfallPoints");
        if (existingRoot != null)
            Undo.DestroyObjectImmediate(existingRoot);

        var root = new GameObject("RockfallPoints");
        Undo.RegisterCreatedObjectUndo(root, "Place Rockfall Points");
        var race = Object.FindFirstObjectByType<BoatRaceMode>();
        if (race != null)
            root.transform.SetParent(race.transform, true);

        float half = Mathf.Max(8f, path.ChannelWidth * 0.48f);
        int n = path.transform.childCount;
        int made = 0;
        for (int i = 0; i < n - 1; i += 2)
        {
            Transform a = path.transform.GetChild(i);
            Transform b = path.transform.GetChild(Mathf.Min(i + 1, n - 1));
            if (a == null || !a.gameObject.activeInHierarchy)
                continue;
            Vector3 tan = b.position - a.position;
            tan.y = 0f;
            if (tan.sqrMagnitude < 0.01f)
                tan = Vector3.forward;
            else
                tan.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, tan).normalized;
            Make(root.transform, a.position, side, half);
            Make(root.transform, a.position, -side, half);
            made += 2;
            if (made >= 28)
                break;
        }
        Selection.activeGameObject = root;
        Debug.Log("Rockfall: placed " + made + " points. Yellow gizmos show throw direction.");
    }

    static void Make(Transform parent, Vector3 along, Vector3 side, float half)
    {
        Vector3 xz = along + side * half;
        float y = along.y + 12f;
        if (BoatWater.TryHeight(along, out float waterY))
            y = waterY + 14f;
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            float ty = terrain.SampleHeight(xz) + terrain.transform.position.y;
            y = Mathf.Max(y, ty + 5.5f);
        }
        var go = new GameObject("RockfallPoint");
        Undo.RegisterCreatedObjectUndo(go, "Rockfall Point");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(xz.x, y, xz.z);
        Vector3 look = -side + Vector3.down * 0.35f;
        go.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        go.AddComponent<RockfallPoint>();
    }
}
#endif
