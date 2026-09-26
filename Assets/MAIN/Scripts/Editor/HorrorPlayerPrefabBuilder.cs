using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class HorrorPlayerPrefabBuilder
{
    const string PrefabPath = "Assets/MAIN/Prefabs/HorrorFirstPersonPlayer.prefab";
    const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    static HorrorPlayerPrefabBuilder()
    {
        EditorApplication.delayCall += TryCreateIfMissing;
    }

    [MenuItem("Horror/Rebuild First Person Player Prefab")]
    static void RebuildFromMenu()
    {
        BuildPrefab(true);
    }

    static void TryCreateIfMissing()
    {
        // В Play-режиме не трогаем: там Awake у HUD успевает создать Canvas на temp-объекте,
        // и он бы запёкся в префаб (причина дублирования интерфейса).
        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryCreateIfMissing;
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        // Нет префаба или он устаревший (без инвентаря, или ещё со старым числом слотов < 5)
        // -> пересобрать под актуальную систему предметов/инвентаря.
        bool outdated = prefab == null;
        if (!outdated)
        {
            var inv = prefab.GetComponent<PlayerInventory>();
            // Устарел, если нет инвентаря, слотов < 5, или в префаб случайно запёкся Canvas.
            outdated = inv == null || inv.SlotCount < 5 ||
                       prefab.GetComponentInChildren<Canvas>(true) != null;
        }
        if (outdated)
            BuildPrefab(prefab != null);
        else
            FixFootstepClipsOnPrefabs();
    }

    static void FixFootstepClipsOnPrefabs()
    {
        bool a = FillClipsOnPrefab(PrefabPath);
        bool b = FillClipsOnPrefab("Assets/MAIN/Prefabs/FPS.prefab");
        if (a || b)
            AssetDatabase.SaveAssets();
    }

    static bool FillClipsOnPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            return false;
        var horror = prefab.GetComponent<HorrorFirstPersonController>();
        if (horror == null)
            return false;
        var so = new SerializedObject(horror);
        bool changed = false;
        changed |= AssignClipArrayIfEmpty(so, "walkFootsteps",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_05.wav",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_06.wav",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_07.wav");
        changed |= AssignClipArrayIfEmpty(so, "sprintFootsteps",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_01.wav",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_02.wav");
        changed |= AssignClipArrayIfEmpty(so, "jumpClips",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_01.wav");
        changed |= AssignClipArrayIfEmpty(so, "landClips",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_02.wav",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_07.wav");
        if (!changed)
            return false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(horror);
        return true;
    }

    static bool AssignClipArrayIfEmpty(SerializedObject so, string property, params string[] paths)
    {
        var p = so.FindProperty(property);
        if (p == null || !p.isArray)
            return false;
        if (p.arraySize > 0)
            return false;
        return AssignClipArray(so, property, paths);
    }

    static bool AssignClipArray(SerializedObject so, string property, params string[] paths)
    {
        var p = so.FindProperty(property);
        if (p == null || !p.isArray)
            return false;
        var clips = new System.Collections.Generic.List<AudioClip>();
        for (int i = 0; i < paths.Length; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            if (clip != null)
                clips.Add(clip);
        }
        if (clips.Count == 0)
            return false;
        p.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        return true;
    }

    static void BuildPrefab(bool force)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null && !force)
            return;

        var root = new GameObject("HorrorFirstPersonPlayer");
        root.tag = "Player";

        var controller = root.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.32f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.slopeLimit = 68f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.08f;
        controller.minMoveDistance = 0.001f;

        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.rolloffMode = AudioRolloffMode.Logarithmic;
        audio.minDistance = 1f;
        audio.maxDistance = 18f;

        var pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 1.62f, 0f);

        var cameraObject = new GameObject("PlayerCamera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(pivot.transform, false);

        var cam = cameraObject.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.08f;
        cam.farClipPlane = 250f;
        cam.depth = -1;
        cameraObject.AddComponent<AudioListener>();
        var urpCamera = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        urpCamera.renderPostProcessing = true;
        urpCamera.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        // Сокет "руки": сюда экипируемые предметы (фонарь/пистолет) подвешиваются
        // перед камерой. Фонарь больше НЕ висит на камере — он теперь предмет в руке.
        var heldSocket = new GameObject("HeldSocket");
        heldSocket.transform.SetParent(cameraObject.transform, false);
        heldSocket.transform.localPosition = new Vector3(0.22f, -0.2f, 0.45f);
        heldSocket.transform.localRotation = Quaternion.identity;

        var playerInput = root.AddComponent<PlayerInput>();
        playerInput.notificationBehavior = PlayerNotifications.SendMessages;
        playerInput.defaultActionMap = "Player";
        playerInput.camera = cam;

        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (actions != null)
            playerInput.actions = actions;

        var horror = root.AddComponent<HorrorFirstPersonController>();
        var serialized = new SerializedObject(horror);
        serialized.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
        serialized.FindProperty("playerCamera").objectReferenceValue = cam;
        serialized.FindProperty("footstepSource").objectReferenceValue = audio;
        AssignClipArray(serialized, "walkFootsteps",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_05.wav",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_06.wav",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_07.wav");
        AssignClipArray(serialized, "sprintFootsteps",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_01.wav",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_02.wav");
        AssignClipArray(serialized, "jumpClips",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_01.wav");
        AssignClipArray(serialized, "landClips",
            "Assets/MAIN/Monster/sounds/Footstep_Soft_Surface_02.wav",
            "Assets/MAIN/Monster/sounds/Foostep_Grass_07.wav");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Инвентарь (5 слотов, клавиши 1..5). Предметы вешаются в HeldSocket.
        var inventory = root.AddComponent<PlayerInventory>();
        var invSo = new SerializedObject(inventory);
        invSo.FindProperty("heldSocket").objectReferenceValue = heldSocket.transform;
        invSo.FindProperty("playerCamera").objectReferenceValue = cam;
        invSo.FindProperty("slotCount").intValue = 5;
        invSo.ApplyModifiedPropertiesWithoutUndo();

        // Взаимодействие по близости (подбор предметов на F, когда игрок рядом).
        root.AddComponent<PlayerInteractor>();

        // Защита: если по какой-то причине Awake HUD успел создать Canvas (напр. в Play-режиме),
        // вырезаем их — HUD строится в рантайме сам, в префабе Canvas быть не должно.
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            if (canvas != null)
                Object.DestroyImmediate(canvas.gameObject);

        if (!AssetDatabase.IsValidFolder("Assets/MAIN/Prefabs"))
            AssetDatabase.CreateFolder("Assets/MAIN", "Prefabs");

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Horror first-person player prefab saved to " + PrefabPath);
    }
}
