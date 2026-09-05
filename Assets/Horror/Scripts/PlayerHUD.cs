using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Игровой HUD, собираемый из кода (uGUI), чтобы не требовать ручной настройки Canvas:
///  - шкала здоровья (снизу слева) с числом;
///  - шкала выносливости (над HP);
///  - индикатор шума — тонкая полоска вверху по центру, расходится от центра к краям
///    экрана по мере роста шума (без числового значения);
///  - экран смерти ("ВЫ ПОГИБЛИ").
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerNoise noise;
    [SerializeField] HorrorFirstPersonController controller;

    [Header("Colors")]
    [SerializeField] Color healthColor = new Color(0.75f, 0.12f, 0.12f, 1f);
    [SerializeField] Color staminaColor = new Color(0.25f, 0.7f, 0.85f, 1f);
    [SerializeField] Color noiseLowColor = new Color(0.9f, 0.85f, 0.2f, 1f);
    [SerializeField] Color noiseHighColor = new Color(0.9f, 0.15f, 0.1f, 1f);

    Font _font;
    RectTransform _healthFill;
    RectTransform _staminaFill;
    RectTransform _noiseFill;
    Image _noiseFillImage;
    Text _healthLabel;
    GameObject _deathScreen;

    void Awake()
    {
        if (health == null)
            health = GetComponent<PlayerHealth>();
        if (noise == null)
            noise = GetComponent<PlayerNoise>();
        if (controller == null)
            controller = GetComponent<HorrorFirstPersonController>();

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null)
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        BuildUI();
    }

    void OnEnable()
    {
        if (health != null)
        {
            health.OnDeath += ShowDeathScreen;
            health.OnRespawn += HideDeathScreen;
        }
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnDeath -= ShowDeathScreen;
            health.OnRespawn -= HideDeathScreen;
        }
    }

    void Update()
    {
        if (health != null && _healthFill != null)
        {
            float f = Mathf.Clamp01(health.HealthNormalized);
            _healthFill.anchorMax = new Vector2(f, 1f);
            if (_healthLabel != null)
                _healthLabel.text = $"HP  {Mathf.CeilToInt(health.CurrentHealth)}/{Mathf.CeilToInt(health.MaxHealth)}";
        }

        if (controller != null && _staminaFill != null)
        {
            float s = Mathf.Clamp01(controller.StaminaNormalized);
            _staminaFill.anchorMax = new Vector2(s, 1f);
        }

        if (noise != null && _noiseFill != null)
        {
            float n = Mathf.Clamp01(noise.CurrentNoiseNormalized);
            // Полоска расходится симметрично от центра к краям экрана.
            _noiseFill.anchorMin = new Vector2(0.5f - n * 0.5f, 0f);
            _noiseFill.anchorMax = new Vector2(0.5f + n * 0.5f, 1f);
            if (_noiseFillImage != null)
                _noiseFillImage.color = Color.Lerp(noiseLowColor, noiseHighColor, n);
        }
    }

    // ------------------------------------------------------------- UI build

    void BuildUI()
    {
        var canvasGO = new GameObject("PlayerHUD_Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = canvasGO.GetComponent<RectTransform>();

        // --- Health bar (снизу слева) ---
        var healthBar = CreateBar(root, "HealthBar",
            anchoredPos: new Vector2(40f, 50f),
            size: new Vector2(460f, 32f),
            fillColor: healthColor,
            out _healthFill, out _);
        _healthLabel = CreateLabel(healthBar, "HP", 18, TextAnchor.MiddleCenter);

        // --- Stamina bar (над HP) ---
        CreateBar(root, "StaminaBar",
            anchoredPos: new Vector2(40f, 90f),
            size: new Vector2(460f, 18f),
            fillColor: staminaColor,
            out _staminaFill, out _);

        // --- Noise indicator (тонкая полоска вверху по центру) ---
        BuildNoiseIndicator(root);

        BuildDeathScreen(root);
    }

    void BuildNoiseIndicator(RectTransform root)
    {
        // Тонкая дорожка на всю ширину сверху (едва заметный фон).
        var track = new GameObject("NoiseTrack", typeof(Image)).GetComponent<Image>();
        track.transform.SetParent(root, false);
        track.color = new Color(1f, 1f, 1f, 0.06f);
        var trackRt = track.rectTransform;
        trackRt.anchorMin = new Vector2(0f, 1f);
        trackRt.anchorMax = new Vector2(1f, 1f);
        trackRt.pivot = new Vector2(0.5f, 1f);
        trackRt.anchoredPosition = new Vector2(0f, -10f);
        trackRt.sizeDelta = new Vector2(0f, 5f); // высота 5px, ширина = весь экран

        // Заполнение, растущее от центра к краям через anchorMin/Max.x.
        var fill = new GameObject("NoiseFill", typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(trackRt, false);
        fill.color = noiseLowColor;
        _noiseFillImage = fill;
        _noiseFill = fill.rectTransform;
        _noiseFill.anchorMin = new Vector2(0.5f, 0f);
        _noiseFill.anchorMax = new Vector2(0.5f, 1f);
        _noiseFill.offsetMin = Vector2.zero;
        _noiseFill.offsetMax = Vector2.zero;
    }

    RectTransform CreateBar(RectTransform parent, string name, Vector2 anchoredPos, Vector2 size,
        Color fillColor, out RectTransform fillRect, out Image fillImage)
    {
        var bar = new GameObject(name, typeof(Image)).GetComponent<Image>();
        bar.transform.SetParent(parent, false);
        bar.color = new Color(0f, 0f, 0f, 0.55f);
        var barRt = bar.rectTransform;
        barRt.anchorMin = new Vector2(0f, 0f);
        barRt.anchorMax = new Vector2(0f, 0f);
        barRt.pivot = new Vector2(0f, 0f);
        barRt.anchoredPosition = anchoredPos;
        barRt.sizeDelta = size;

        var fill = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(barRt, false);
        fill.color = fillColor;
        fillImage = fill;
        fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        return barRt;
    }

    Text CreateLabel(RectTransform parent, string text, int fontSize, TextAnchor anchor)
    {
        var go = new GameObject("Label", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.text = text;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8f, 0f);
        rt.offsetMax = new Vector2(-8f, 0f);
        return t;
    }

    void BuildDeathScreen(RectTransform root)
    {
        _deathScreen = new GameObject("DeathScreen", typeof(Image));
        _deathScreen.transform.SetParent(root, false);
        var bg = _deathScreen.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        var rt = bg.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var title = new GameObject("Title", typeof(Text)).GetComponent<Text>();
        title.transform.SetParent(rt, false);
        title.font = _font;
        title.text = "ВЫ ПОГИБЛИ";
        title.fontSize = 72;
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.8f, 0.1f, 0.1f, 1f);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 0.5f);
        trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(0f, 30f);
        trt.sizeDelta = new Vector2(900f, 120f);

        var hint = new GameObject("Hint", typeof(Text)).GetComponent<Text>();
        hint.transform.SetParent(rt, false);
        hint.font = _font;
        hint.text = "Возрождение...";
        hint.fontSize = 28;
        hint.alignment = TextAnchor.MiddleCenter;
        hint.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        hint.horizontalOverflow = HorizontalWrapMode.Overflow;
        hint.verticalOverflow = VerticalWrapMode.Overflow;
        var hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0.5f);
        hrt.anchorMax = new Vector2(0.5f, 0.5f);
        hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = new Vector2(0f, -60f);
        hrt.sizeDelta = new Vector2(900f, 60f);

        _deathScreen.SetActive(false);
    }

    void ShowDeathScreen()
    {
        if (_deathScreen != null)
            _deathScreen.SetActive(true);
    }

    void HideDeathScreen()
    {
        if (_deathScreen != null)
            _deathScreen.SetActive(false);
    }
}
