using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
static class HorrorPlayerPrefabBuilder
{
    const string PrefabPath = "Assets/Horror/Prefabs/HorrorFirstPersonPlayer.prefab";
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
        controller.slopeLimit = 45f;
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
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Инвентарь (5 слотов, клавиши 1..5). Предметы вешаются в HeldSocket.
        var inventory = root.AddComponent<PlayerInventory>();
        var invSo = new SerializedObject(inventory);
        invSo.FindProperty("heldSocket").objectReferenceValue = heldSocket.transform;
        invSo.FindProperty("playerCamera").objectReferenceValue = cam;
        invSo.FindProperty("slotCount").intValue = 5;
        invSo.ApplyModifiedPropertiesWithoutUndo();

        // Взаимодействие по близости (подбор предметов на F, когда игрок рядом).
        var interactor = root.AddComponent<PlayerInteractor>();

        // HUD: подсказка подбора, прицел и инвентарь (Tab).
        var interactionHud = root.AddComponent<InteractionHUD>();
        var iHudSo = new SerializedObject(interactionHud);
        iHudSo.FindProperty("interactor").objectReferenceValue = interactor;
        iHudSo.FindProperty("inventory").objectReferenceValue = inventory;
        iHudSo.FindProperty("playerCamera").objectReferenceValue = cam;
        iHudSo.FindProperty("controller").objectReferenceValue = horror;
        iHudSo.ApplyModifiedPropertiesWithoutUndo();

        // Здоровье игрока: при смерти отключаем управление и ввод.
        var playerHealth = root.AddComponent<PlayerHealth>();
        var healthSo = new SerializedObject(playerHealth);
        var disableArray = healthSo.FindProperty("disableOnDeath");
        disableArray.arraySize = 2;
        disableArray.GetArrayElementAtIndex(0).objectReferenceValue = horror;
        disableArray.GetArrayElementAtIndex(1).objectReferenceValue = playerInput;
        healthSo.ApplyModifiedPropertiesWithoutUndo();

        // Шум игрока -> NoiseManager -> монстр.
        var playerNoise = root.AddComponent<PlayerNoise>();
        var noiseSo = new SerializedObject(playerNoise);
        noiseSo.FindProperty("controller").objectReferenceValue = horror;
        noiseSo.ApplyModifiedPropertiesWithoutUndo();

        // HUD (шкалы шума и HP, экран смерти) — строится из кода в рантайме.
        var hud = root.AddComponent<PlayerHUD>();
        var hudSo = new SerializedObject(hud);
        hudSo.FindProperty("health").objectReferenceValue = playerHealth;
        hudSo.FindProperty("noise").objectReferenceValue = playerNoise;
        hudSo.FindProperty("controller").objectReferenceValue = horror;
        hudSo.ApplyModifiedPropertiesWithoutUndo();

        // Возрождение после смерти.
        root.AddComponent<PlayerRespawn>();

        // Защита: если по какой-то причине Awake HUD успел создать Canvas (напр. в Play-режиме),
        // вырезаем их — HUD строится в рантайме сам, в префабе Canvas быть не должно.
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            if (canvas != null)
                Object.DestroyImmediate(canvas.gameObject);

        if (!AssetDatabase.IsValidFolder("Assets/Horror"))
            AssetDatabase.CreateFolder("Assets", "Horror");
        if (!AssetDatabase.IsValidFolder("Assets/Horror/Prefabs"))
            AssetDatabase.CreateFolder("Assets/Horror", "Prefabs");

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Horror first-person player prefab saved to " + PrefabPath);
    }
}
