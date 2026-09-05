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
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryCreateIfMissing;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            BuildPrefab(false);
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

        var flashlightObject = new GameObject("Flashlight");
        flashlightObject.transform.SetParent(cameraObject.transform, false);
        flashlightObject.transform.localPosition = new Vector3(0.12f, -0.08f, 0.08f);
        flashlightObject.transform.localRotation = Quaternion.Euler(4f, -2f, 0f);

        var light = flashlightObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = 16f;
        light.spotAngle = 58f;
        light.innerSpotAngle = 28f;
        light.color = new Color(1f, 0.93f, 0.78f);
        light.intensity = 220f;
        light.shadows = LightShadows.Soft;
        flashlightObject.AddComponent<UniversalAdditionalLightData>();

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
        serialized.FindProperty("flashlight").objectReferenceValue = light;
        serialized.FindProperty("footstepSource").objectReferenceValue = audio;
        serialized.ApplyModifiedPropertiesWithoutUndo();

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
