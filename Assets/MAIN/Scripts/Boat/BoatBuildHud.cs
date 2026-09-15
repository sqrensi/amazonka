using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Короткая подсказка строительства (без фона), как pickup HUD.
/// </summary>
public class BoatBuildHud : MonoBehaviour
{
    static BoatBuildHud _instance;
    Text _label;
    Text _hull;
    float _until;

    public static void Clear()
    {
        var hud = Instance();
        if (hud == null || hud._label == null)
            return;
        hud._label.text = "";
        hud._until = 0f;
    }

    public static void Hint(string text, float seconds = 2.4f)
    {
        if (BoatOarStation.Active != null)
            return;
        var hud = Instance();
        if (hud == null)
            return;
        hud.Show(text, seconds);
    }

    static BoatBuildHud Instance()
    {
        if (_instance != null)
            return _instance;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return null;
        _instance = player.GetComponent<BoatBuildHud>();
        if (_instance == null)
            _instance = player.AddComponent<BoatBuildHud>();
        return _instance;
    }

    void Awake()
    {
        _instance = this;
        Build();
    }

    void Build()
    {
        var canvasGo = new GameObject("BoatBuild_Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var go = new GameObject("Hint", typeof(RectTransform));
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.12f);
        rt.anchorMax = new Vector2(0.5f, 0.12f);
        rt.sizeDelta = new Vector2(900, 40);

        _label = go.AddComponent<Text>();
        _label.font = HorrorPaperUI.Font();
        _label.fontSize = 22;
        _label.fontStyle = FontStyle.Bold;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.color = new Color(1f, 0.98f, 0.86f, 1f);
        _label.raycastTarget = false;
        _label.supportRichText = true;
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(0.05f, 0.08f, 0.04f, 0.5f);
        ol.effectDistance = new Vector2(1.2f, -1.2f);
        _label.text = "";

        var hullGo = new GameObject("Hull", typeof(RectTransform));
        hullGo.transform.SetParent(canvasGo.transform, false);
        var hullRt = hullGo.GetComponent<RectTransform>();
        hullRt.anchorMin = new Vector2(0.5f, 0.055f);
        hullRt.anchorMax = new Vector2(0.5f, 0.055f);
        hullRt.sizeDelta = new Vector2(900, 32);
        _hull = hullGo.AddComponent<Text>();
        _hull.font = HorrorPaperUI.Font();
        _hull.fontSize = 18;
        _hull.fontStyle = FontStyle.Bold;
        _hull.alignment = TextAnchor.MiddleCenter;
        _hull.color = new Color(0.82f, 0.92f, 1f, 0.92f);
        _hull.raycastTarget = false;
        _hull.supportRichText = true;
        var hol = hullGo.AddComponent<Outline>();
        hol.effectColor = new Color(0.04f, 0.06f, 0.1f, 0.5f);
        hol.effectDistance = new Vector2(1.1f, -1.1f);
        _hull.text = "";
    }

    void Show(string text, float seconds)
    {
        if (_label == null)
            Build();
        _label.text = text ?? "";
        _until = Time.unscaledTime + seconds;
    }

    void Update()
    {
        if (_label == null)
            return;
        if (BoatOarStation.Active != null)
        {
            _label.text = "";
            _until = 0f;
        }
        else if (Time.unscaledTime > _until)
            _label.text = "";

        if (_hull != null)
            _hull.text = BoatHull.PlayerStatus(gameObject);
    }
}
