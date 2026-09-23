#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

static class BoatRaceBuilder
{
    const string Items = "Assets/MAIN/Prefabs/Items/";

    [MenuItem("Horror/Boat Race/Create Round In Scene")]
    static void CreateRound()
    {
        var existing = Object.FindFirstObjectByType<BoatRaceMode>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("Boat race already in the scene. Move Spawn / Launch / Finish, then Play.");
            return;
        }

        var root = new GameObject("BoatRace");
        Undo.RegisterCreatedObjectUndo(root, "Create Boat Race");
        var mode = root.AddComponent<BoatRaceMode>();
        root.AddComponent<BoatRaceHud>();

        var spawn = new GameObject("Spawn").transform;
        spawn.SetParent(root.transform, false);
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            spawn.SetPositionAndRotation(player.transform.position, player.transform.rotation);
        else
            spawn.position = new Vector3(0f, 1.1f, 0f);

        var launch = new GameObject("Launch").transform;
        launch.SetParent(root.transform, false);
        var finish = new GameObject("Finish").transform;
        finish.SetParent(root.transform, false);

        var water = Object.FindFirstObjectByType<BoatWater>();
        if (water != null)
        {
            Vector3 flow = water.FlowWorld;
            if (flow.sqrMagnitude < 0.01f)
                flow = Vector3.forward;
            flow.y = 0f;
            flow.Normalize();
            Vector3 mid = water.transform.position;
            launch.position = new Vector3(mid.x, water.SurfaceY + 0.2f, mid.z) + flow * 8f;
            launch.rotation = Quaternion.LookRotation(flow);
            finish.position = launch.position + flow * 72f;
            finish.rotation = Quaternion.LookRotation(flow);
        }
        else
        {
            launch.position = new Vector3(0f, 0.4f, 24f);
            finish.position = new Vector3(0f, 0.4f, 96f);
        }

        CreateCurrentPath(root.transform, launch, finish);

        var so = new SerializedObject(mode);
        so.FindProperty("playerSpawn").objectReferenceValue = spawn;
        so.FindProperty("waterLaunch").objectReferenceValue = launch;
        so.FindProperty("finish").objectReferenceValue = finish;
        so.FindProperty("scatterLoot").boolValue = true;
        var prefabs = so.FindProperty("lootPrefabs");
        var counts = so.FindProperty("lootCounts");
        string[] names =
        {
            "PlankItem", "LogItem", "BarrelItem", "NailItem", "RopeItem", "HammerItem", "SawItem", "MountOarItem"
        };
        int[] n = { 18, 10, 2, 30, 5, 1, 1, 1 };
        prefabs.arraySize = names.Length;
        counts.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Items + names[i] + ".prefab");
            prefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
            counts.GetArrayElementAtIndex(i).intValue = n[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mode);
        Selection.activeGameObject = root;
        Debug.Log("Boat race created. Place Spawn on the island, Launch on water, Finish downriver. Add CurrentPath points along the river.");
    }

    [MenuItem("Horror/Boat Race/Create Current Path")]
    static void CreateCurrentPathMenu()
    {
        var race = Object.FindFirstObjectByType<BoatRaceMode>();
        var existing = Object.FindFirstObjectByType<BoatCurrentPath>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("CurrentPath already in the scene. Duplicate/move the child points along the river.");
            return;
        }
        Transform parent = race != null ? race.transform : null;
        Transform launch = race != null ? race.WaterLaunch : null;
        Transform finish = race != null ? race.Finish : null;
        var path = CreateCurrentPath(parent, launch, finish);
        Selection.activeGameObject = path;
        EditorGUIUtility.PingObject(path);
        Debug.Log("CurrentPath created. Move the child points, duplicate them along the river. Current follows that polyline.");
    }

    static GameObject CreateCurrentPath(Transform parent, Transform launch, Transform finish)
    {
        var go = new GameObject("CurrentPath");
        Undo.RegisterCreatedObjectUndo(go, "Create Current Path");
        if (parent != null)
            go.transform.SetParent(parent, false);
        go.AddComponent<BoatCurrentPath>();
        Vector3 a = launch != null ? launch.position : new Vector3(0f, 0.4f, 0f);
        Vector3 b = finish != null ? finish.position : a + Vector3.forward * 40f;
        MakePoint(go.transform, "Point_0", a);
        MakePoint(go.transform, "Point_1", Vector3.Lerp(a, b, 0.33f));
        MakePoint(go.transform, "Point_2", Vector3.Lerp(a, b, 0.66f));
        MakePoint(go.transform, "Point_3", b);
        return go;
    }

    static void MakePoint(Transform parent, string name, Vector3 pos)
    {
        var p = new GameObject(name);
        p.transform.SetParent(parent, false);
        p.transform.position = pos;
    }
}
#endif
