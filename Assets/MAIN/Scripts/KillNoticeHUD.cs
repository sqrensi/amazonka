using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Лёгкая справка об убийстве в духе How to Fish: без панели, pop-in, stagger.
/// </summary>
public class KillNoticeHUD : MonoBehaviour
{
    public struct Report
    {
        public int killIndex;
        public string target;
        public string weapon;
        public float distanceMeters;
        public KillStyle.Tag[] tags;
        public int points;
    }

    static int _kills;
    static KillNoticeHUD _instance;

    RectTransform _stack;
    Font _font;

    public static int NextKillIndex() => ++_kills;
    public static int KillCount => _kills;

    public static void ResetKills()
    {
        _kills = 0;
    }

    public static void Show(Report report)
    {
        var hud = Instance();
        if (hud != null)
            hud.Spawn(report);
    }

    static KillNoticeHUD Instance()
    {
        if (_instance != null)
            return _instance;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return null;
        _instance = player.GetComponent<KillNoticeHUD>();
        if (_instance == null)
            _instance = player.AddComponent<KillNoticeHUD>();
        return _instance;
    }

    void Awake()
    {
        _instance = this;
        _font = HorrorPaperUI.Font();
        Build();
    }

    void Build()
    {
        var canvasGo = new GameObject("KillNotice_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var stackGo = new GameObject("Stack", typeof(RectTransform));
        stackGo.transform.SetParent(canvasGo.transform, false);
        _stack = stackGo.GetComponent<RectTransform>();
        _stack.anchorMin = new Vector2(0.5f, 0.78f);
        _stack.anchorMax = new Vector2(0.5f, 0.78f);
        _stack.pivot = new Vector2(0.5f, 1f);
        _stack.anchoredPosition = Vector2.zero;
        _stack.sizeDelta = new Vector2(720, 200);
    }

    void Spawn(Report report)
    {
        if (_stack == null)
            Build();
        StartCoroutine(AnimateCard(report));
    }

    IEnumerator AnimateCard(Report report)
    {
        var root = new GameObject("Kill", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(_stack, false);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(700, report.tags != null && report.tags.Length > 0 ? 210 : 168);
        rt.anchoredPosition = new Vector2(0f, -8f);
        var cg = root.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        rt.localScale = Vector3.one * 0.72f;
        rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(-2.2f, 2.2f));

        string flavor = Flavor(report);
        var title = MakeText(root.transform, "Flavor", flavor, 22, new Color(1f, 0.92f, 0.45f, 1f), FontStyle.Bold, TextAnchor.MiddleCenter);
        Stretch(title.rectTransform, 0f, 8f, 0f, -4f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 70f);
        title.rectTransform.sizeDelta = new Vector2(680, 28);

        var num = MakeText(root.transform, "Index", $"#{report.killIndex:00}", 54, new Color(1f, 0.98f, 0.88f, 1f), FontStyle.Bold, TextAnchor.MiddleCenter);
        num.rectTransform.anchoredPosition = new Vector2(0f, 28f);
        num.rectTransform.sizeDelta = new Vector2(680, 58);

        string pts = report.points > 0 ? $"+{report.points}" : "";
        var score = MakeText(root.transform, "Score", pts, 30, new Color(1f, 0.86f, 0.28f, 1f), FontStyle.Bold, TextAnchor.MiddleCenter);
        score.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        score.rectTransform.sizeDelta = new Vector2(680, 32);

        var target = MakeText(root.transform, "Target", report.target.ToUpperInvariant(), 24, new Color(0.65f, 1f, 0.78f, 1f), FontStyle.Italic, TextAnchor.MiddleCenter);
        target.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        target.rectTransform.sizeDelta = new Vector2(680, 28);

        string stats = $"{report.weapon}   ·   {report.distanceMeters:0.0} m";
        if (report.tags != null && report.tags.Length > 0)
        {
            var names = new string[report.tags.Length];
            for (int i = 0; i < report.tags.Length; i++)
                names[i] = report.tags[i].name;
            stats = string.Join("  ·  ", names) + "\n" + stats;
        }
        var line = MakeText(root.transform, "Stats", stats, 18, new Color(1f, 1f, 1f, 0.92f), FontStyle.Normal, TextAnchor.MiddleCenter);
        line.rectTransform.anchoredPosition = new Vector2(0f, -78f);
        line.rectTransform.sizeDelta = new Vector2(680, 48);

        SetAlpha(title, 0f);
        SetAlpha(num, 0f);
        SetAlpha(score, 0f);
        SetAlpha(target, 0f);
        SetAlpha(line, 0f);

        float t = 0f;
        while (t < 0.28f)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / 0.28f);
            float s = Overshoot(u);
            rt.localScale = Vector3.one * s;
            cg.alpha = u;
            yield return null;
        }
        rt.localScale = Vector3.one;
        cg.alpha = 1f;

        yield return FadeIn(title, 0.1f);
        yield return FadeIn(num, 0.12f);
        yield return FadeIn(score, 0.1f);
        yield return FadeIn(target, 0.1f);
        yield return FadeIn(line, 0.12f);

        float hold = 2.6f;
        float bob = 0f;
        while (bob < hold)
        {
            bob += Time.unscaledDeltaTime;
            rt.anchoredPosition = new Vector2(0f, -8f + Mathf.Sin(bob * 1.6f) * 4f);
            yield return null;
        }

        t = 0f;
        Vector2 start = rt.anchoredPosition;
        while (t < 0.45f)
        {
            t += Time.unscaledDeltaTime;
            float u = t / 0.45f;
            cg.alpha = 1f - u;
            rt.anchoredPosition = start + Vector2.up * (u * 36f);
            rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.94f, u);
            yield return null;
        }

        Destroy(root);
    }

    static string Flavor(Report report)
    {
        if (report.tags != null)
        {
            for (int i = 0; i < report.tags.Length; i++)
            {
                if (report.tags[i].name == "360")
                    return "AROUND THE WORLD";
                if (report.tags[i].name == "180")
                    return "TURN AND BURN";
                if (report.tags[i].name == "FLICK")
                    return "FLICKED";
                if (report.tags[i].name == "AIR")
                    return "MID-AIR";
                if (report.tags[i].name == "DOUBLE")
                    return "BACK TO BACK";
            }
        }
        string[] words = { "NICE", "GOT 'EM", "CLEAN", "DOWN", "POP", "CATCH" };
        int n = Mathf.Max(1, report.killIndex);
        return words[(n - 1) % words.Length];
    }

    static float Overshoot(float u)
    {
        u = Mathf.Clamp01(u);
        return 1f + 1.6f * Mathf.Pow(u - 1f, 3f) + 0.6f * Mathf.Pow(u - 1f, 2f);
    }

    Text MakeText(Transform parent, string name, string text, int size, Color color, FontStyle style, TextAnchor align)
    {
        var t = new GameObject(name, typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = _font != null ? _font : HorrorPaperUI.Font();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        var outline = t.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.05f, 0.08f, 0.04f, 0.55f);
        outline.effectDistance = new Vector2(1.4f, -1.4f);
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.35f);
        sh.effectDistance = new Vector2(0f, -3f);
        return t;
    }

    static void Stretch(RectTransform rt, float l, float r, float t, float b)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
    }

    static void SetAlpha(Text text, float a)
    {
        var c = text.color;
        c.a = a;
        text.color = c;
    }

    static IEnumerator FadeIn(Text text, float dur)
    {
        float t = 0f;
        Color c = text.color;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Clamp01(t / dur);
            text.color = c;
            yield return null;
        }
        c.a = 1f;
        text.color = c;
    }
}
