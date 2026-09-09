using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Подсказка у ближайшего pickup: клавиша и текст рядом с предметом на экране.
/// </summary>
public class PickupPromptHUD : MonoBehaviour
{
    PlayerInteractor _interactor;
    Camera _camera;
    GameObject _root;
    RectTransform _panel;
    Text _label;

    void Awake()
    {
        _interactor = GetComponent<PlayerInteractor>();
        _camera = GetComponentInChildren<Camera>();
        Build();
    }

    void LateUpdate()
    {
        if (_root == null)
            return;

        bool show = _interactor != null && _interactor.HasTarget && _camera != null;
        if (!show)
        {
            _root.SetActive(false);
            return;
        }

        Transform anchor = _interactor.Current.GetAnchor();
        if (anchor == null)
        {
            _root.SetActive(false);
            return;
        }

        Vector3 world = anchor.position + Vector3.up * 0.22f;
        Vector3 screen = _camera.WorldToScreenPoint(world);
        if (screen.z <= 0.05f)
        {
            _root.SetActive(false);
            return;
        }

        _root.SetActive(true);
        _panel.position = screen;
        float bob = Mathf.Sin(Time.unscaledTime * 3.2f) * 5f;
        _panel.position = screen + new Vector3(0f, bob, 0f);
        string key = _interactor.Current.GetInteractKey();
        string prompt = _interactor.Current.GetPrompt();
        _label.text = $"<color=#8FFFB0>{key}</color>   {prompt}";
    }

    void Build()
    {
        var canvasGo = new GameObject("PickupPrompt_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _root = new GameObject("Prompt", typeof(RectTransform), typeof(CanvasGroup));
        _root.transform.SetParent(canvasGo.transform, false);
        _panel = _root.GetComponent<RectTransform>();
        _panel.sizeDelta = new Vector2(460, 40);
        _panel.pivot = new Vector2(0.5f, 0f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(_root.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        _label = textGo.AddComponent<Text>();
        _label.font = HorrorPaperUI.Font();
        _label.fontSize = 26;
        _label.fontStyle = FontStyle.Bold;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.color = new Color(1f, 0.98f, 0.86f, 1f);
        _label.raycastTarget = false;
        _label.horizontalOverflow = HorizontalWrapMode.Overflow;
        _label.supportRichText = true;

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.08f, 0.12f, 0.05f, 0.55f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.28f);
        shadow.effectDistance = new Vector2(0f, -2.5f);

        _root.SetActive(false);

        BuildCrosshair(canvasGo.transform);
    }

    void BuildCrosshair(Transform canvas)
    {
        var glow = MakeDot(canvas, "CrosshairGlow", 18f, new Color(0.25f, 1f, 0.4f, 0.28f));
        var core = MakeDot(canvas, "Crosshair", 7f, new Color(0.45f, 1f, 0.55f, 0.95f));
        glow.SetAsLastSibling();
        core.SetAsLastSibling();
    }

    static RectTransform MakeDot(Transform parent, string name, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.sprite = GreenDotSprite();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static Sprite _greenDot;

    static Sprite GreenDotSprite()
    {
        if (_greenDot != null)
            return _greenDot;

        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float c = (s - 1) * 0.5f;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        _greenDot = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 32f);
        return _greenDot;
    }
}
