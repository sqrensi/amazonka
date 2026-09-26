using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Меню в сцене game. Дальше грузится main (лодка) или house (дом).
/// </summary>
[DefaultExecutionOrder(-80)]
public class GameModeSelect : MonoBehaviour
{
    Canvas _canvas;
    bool _picked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (!PlaySession.IsMenuScene)
            return;
        if (Object.FindFirstObjectByType<GameModeSelect>() != null)
            return;
        var select = new GameObject("GameModeSelect");
        select.AddComponent<GameModeSelect>();
    }

    void Awake()
    {
        PlaySession.ResetPick();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        EnsureEventSystem();
        EnsureMenuCamera();
        Build();
    }

    void Update()
    {
        if (_picked)
            return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void PickBoat()
    {
        if (_picked)
            return;
        _picked = true;
        PlaySession.Choose(PlaySession.Mode.BoatRace);
        SceneManager.LoadScene(PlaySession.BoatScene);
    }

    void PickHouse()
    {
        if (_picked)
            return;
        _picked = true;
        PlaySession.Choose(PlaySession.Mode.HouseHold);
        SceneManager.LoadScene(PlaySession.HouseScene);
    }

    static void EnsureMenuCamera()
    {
        if (Camera.main != null)
            return;
        var go = new GameObject("MenuCamera");
        var cam = go.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.04f, 0.05f, 1f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 2000f;
        go.transform.SetPositionAndRotation(new Vector3(220f, 48f, 180f), Quaternion.Euler(18f, -40f, 0f));
        if (go.GetComponent<AudioListener>() == null)
            go.AddComponent<AudioListener>();
    }

    void Build()
    {
        var go = new GameObject("ModeSelectHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = go.GetComponent<RectTransform>();

        var fade = Image(root, new Color(0.02f, 0.03f, 0.04f, 0.82f));
        Stretch(fade.rectTransform);

        var title = Label(root, "ModeTitle", 46, FontStyle.Bold, new Color(1f, 0.98f, 0.88f));
        Pin(title.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(920f, 64f), new Vector2(0f, 40f));
        title.text = "CHOOSE A MODE";

        var sub = Label(root, "ModeSub", 20, FontStyle.Italic, new Color(1f, 0.92f, 0.45f));
        Pin(sub.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(920f, 36f), new Vector2(0f, -18f));
        sub.text = "Same land. Two games.";

        MakeButton(root, "River", "THE RIVER", "Build a boat. Ride the current.", new Vector2(-220f, -40f), PickBoat);
        MakeButton(root, "House", "THE HOUSE", "Build a house. Keep it standing.", new Vector2(220f, -40f), PickHouse);
    }

    void MakeButton(RectTransform parent, string name, string title, string hint, Vector2 pos, UnityEngine.Events.UnityAction click)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Pin(rt, new Vector2(0.5f, 0.42f), new Vector2(380f, 160f), pos);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);
        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.18f, 0.22f, 0.2f, 1f);
        colors.pressedColor = new Color(0.12f, 0.14f, 0.12f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(click);

        var t = Label(rt, "T", 28, FontStyle.Bold, new Color(1f, 0.98f, 0.88f));
        Pin(t.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(340f, 40f), Vector2.zero);
        t.text = title;

        var h = Label(rt, "H", 16, FontStyle.Normal, new Color(0.85f, 0.88f, 0.82f, 0.9f));
        Pin(h.rectTransform, new Vector2(0.5f, 0.28f), new Vector2(340f, 48f));
        h.text = hint;
        h.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
            return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.hideFlags = HideFlags.DontSave;
    }

    static Image Image(Transform parent, Color c)
    {
        var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = true;
        return img;
    }

    static Text Label(Transform parent, string name, int size, FontStyle style, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = HorrorPaperUI.Font();
        t.fontSize = size;
        t.fontStyle = style;
        t.color = c;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Pin(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 pos = default)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }
}
