using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Boat-round HUD in How to Fish language: pop-in type, no paper, no plates.
/// </summary>
[DefaultExecutionOrder(-35)]
public class BoatRaceHud : MonoBehaviour
{
    public static BoatRaceHud Active { get; private set; }

    static readonly Color Cream = new Color(1f, 0.98f, 0.88f, 1f);
    static readonly Color Gold = new Color(1f, 0.92f, 0.45f, 1f);
    static readonly Color Mint = new Color(0.65f, 1f, 0.78f, 1f);
    static readonly Color White = new Color(1f, 1f, 1f, 0.92f);
    static readonly Color Danger = new Color(1f, 0.42f, 0.36f, 1f);

    Canvas _canvas;
    RectTransform _root;
    CanvasGroup _playGroup;
    CanvasGroup _titleGroup;
    CanvasGroup _resultGroup;
    Image _fade;
    Image _hurt;
    CanvasGroup _sharkGroup;
    CanvasGroup _sharkRowA;
    CanvasGroup _sharkRowB;
    Image _sharkFill;
    Image _sharkFillB;
    Text _sharkLabel;
    Text _sharkLabelB;
    Text _phaseText;
    Text _timerText;
    Text _statusText;
    Text _titleText;
    Text _subText;
    Text _resultKicker;
    Text _resultTitle;
    Text _resultMark;
    Text _resultBody;
    Text _resultHint;
    float _hurtAmt;
    bool _timerUrgent;
    string _timerShown;
    bool _busy;
    bool _canRestart;
    Coroutine _titleCo;
    Font _font;

    public bool CanRestart => _canRestart;

    void Awake()
    {
        Active = this;
        EnsureBuilt();
    }

    public void EnsureBuilt()
    {
        Active = this;
        if (_canvas == null)
            Build();
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        _hurtAmt = Mathf.MoveTowards(_hurtAmt, 0f, dt * 0.55f);
        if (_hurt != null)
        {
            var c = _hurt.color;
            c.a = _hurtAmt;
            _hurt.color = c;
        }
        if (_titleGroup != null && _titleGroup.alpha > 0.01f)
        {
            var rt = _titleGroup.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0f, 12f + Mathf.Sin(Time.unscaledTime * 1.55f) * 3.5f);
        }
    }

    void Build()
    {
        _font = HorrorPaperUI.Font();
        var go = new GameObject("BoatRaceHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 72;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _root = go.GetComponent<RectTransform>();

        _fade = Solid(_root, "Fade", new Color(0f, 0f, 0f, 0f));
        Stretch(_fade.rectTransform);
        _fade.gameObject.SetActive(false);

        _hurt = Solid(_root, "Hurt", new Color(0.55f, 0.05f, 0.04f, 0f));
        _hurt.sprite = Vignette();
        _hurt.type = Image.Type.Simple;
        Stretch(_hurt.rectTransform);

        _playGroup = Group(_root, "Play");
        Stretch(_playGroup.GetComponent<RectTransform>());
        _playGroup.alpha = 0f;

        _phaseText = FishText(_playGroup.transform, "Phase", 20, Gold, FontStyle.Bold, TextAnchor.UpperCenter);
        Pin(_phaseText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(720f, 28f));

        _timerText = FishText(_playGroup.transform, "Timer", 56, Cream, FontStyle.Bold, TextAnchor.UpperCenter);
        Pin(_timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(520f, 68f));

        _statusText = FishText(_playGroup.transform, "Status", 18, White, FontStyle.Normal, TextAnchor.UpperCenter);
        Pin(_statusText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(980f, 36f));

        _sharkGroup = Group(_playGroup.transform, "Shark");
        Pin(_sharkGroup.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -154f), new Vector2(380f, 72f));
        _sharkGroup.alpha = 0f;
        BuildSharkMeter(_sharkGroup.transform, "A", new Vector2(0f, 0f), new Vector2(1f, 1f), Cream, new Color(0.82f, 0.22f, 0.28f, 0.95f), out _sharkLabel, out _sharkFill, out _sharkRowA);
        BuildSharkMeter(_sharkGroup.transform, "B", new Vector2(0f, 0f), new Vector2(1f, 0.46f), new Color(0.92f, 0.86f, 0.72f, 1f), new Color(0.78f, 0.32f, 0.22f, 0.95f), out _sharkLabelB, out _sharkFillB, out _sharkRowB);
        if (_sharkRowB != null)
            _sharkRowB.alpha = 0f;

        _titleGroup = Group(_root, "Title");
        Pin(_titleGroup.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(1200f, 180f));
        _titleGroup.alpha = 0f;

        _titleText = FishText(_titleGroup.transform, "Title", 42, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
        Pin(_titleText.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        _subText = FishText(_titleGroup.transform, "Sub", 20, Gold, FontStyle.Italic, TextAnchor.UpperCenter);
        Pin(_subText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);

        _resultGroup = Group(_root, "Result");
        Stretch(_resultGroup.GetComponent<RectTransform>());
        _resultGroup.alpha = 0f;
        _resultGroup.gameObject.SetActive(false);

        var stack = new GameObject("Stack", typeof(RectTransform)).GetComponent<RectTransform>();
        stack.SetParent(_resultGroup.transform, false);
        Pin(stack, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 780f));
        var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.padding = new RectOffset(24, 24, 16, 28);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        _resultKicker = FishText(stack, "Kicker", 22, Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
        Row(_resultKicker, 30f);

        _resultTitle = FishText(stack, "Title", 48, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);
        Row(_resultTitle, 58f);

        _resultMark = FishText(stack, "Mark", 24, Mint, FontStyle.Italic, TextAnchor.MiddleCenter);
        _resultMark.horizontalOverflow = HorizontalWrapMode.Wrap;
        Row(_resultMark, 56f);

        _resultBody = FishText(stack, "Body", 20, White, FontStyle.Normal, TextAnchor.UpperCenter);
        _resultBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        _resultBody.verticalOverflow = VerticalWrapMode.Overflow;
        _resultBody.lineSpacing = 1.12f;
        var bodyRow = _resultBody.gameObject.AddComponent<LayoutElement>();
        bodyRow.minHeight = 80f;
        bodyRow.preferredHeight = 220f;
        bodyRow.flexibleHeight = 0f;

        _resultHint = FishText(stack, "Hint", 18, Gold, FontStyle.Normal, TextAnchor.MiddleCenter);
        Row(_resultHint, 40f);
    }

    public IEnumerator PlayIntro()
    {
        yield return PlayIntro("THE RIVER", "Build what you can. Then hold the water.");
    }

    public IEnumerator PlayIntro(string title, string sub)
    {
        yield return PlayIntro(title, sub, true);
    }

    public IEnumerator PlayIntro(string title, string sub, bool blackout)
    {
        _canRestart = false;
        _busy = true;
        SetFade(blackout ? 1f : 0f);
        _playGroup.alpha = 0f;
        _titleGroup.gameObject.SetActive(true);
        HideResult();
        yield return Title(title, sub, blackout ? 2.2f : 1.1f);
        if (blackout)
            yield return FadeTo(0f, 1.15f);
        else
            SetFade(0f);
        _busy = false;
    }

    public void ClearFade()
    {
        SetFade(0f);
        if (_playGroup != null)
            _playGroup.alpha = 1f;
    }

    public void PrepareNewRound()
    {
        _canRestart = false;
        _busy = false;
        _hurtAmt = 0f;
        HideResult();
        ClearFade();
        if (_titleGroup != null)
        {
            _titleGroup.alpha = 0f;
            _titleGroup.gameObject.SetActive(true);
        }
    }

    public IEnumerator PlayHoldOpen()
    {
        _phaseText.text = "HOLD";
        _statusText.text = "Keep the house standing";
        _statusText.color = White;
        yield return FadePlay(1f, 0.4f);
        yield return Title("HOLD", "If it loses its strength, it falls.", 1.7f);
    }

    public void SetStatus(string line, bool danger = false)
    {
        if (_statusText == null)
            return;
        _statusText.color = danger ? Danger : White;
        _statusText.text = line ?? "";
    }

    public void FlashWarn(string title, string sub)
    {
        EnsureBuilt();
        if (!isActiveAndEnabled)
            return;
        StartCoroutine(Title(title, sub, 1.55f));
    }

    public IEnumerator PlayBuildOpen()
    {
        _phaseText.text = "BUILD";
        _statusText.text = "Nail a hull before the clock dies";
        _statusText.color = White;
        yield return FadePlay(1f, 0.4f);
        yield return Title("BUILD", "Whatever is standing when the clock ends, goes in.", 1.7f);
    }

    public IEnumerator PlayLaunchOut()
    {
        _busy = true;
        yield return Title("LAUNCH", "Hold on.", 1.05f);
        yield return FadeTo(1f, 0.7f);
        _playGroup.alpha = 0f;
    }

    public IEnumerator PlayLaunchIn()
    {
        _phaseText.text = "RACE";
        _statusText.text = "Stay on the hull";
        _statusText.color = White;
        yield return FadeTo(0f, 0.85f);
        yield return FadePlay(1f, 0.35f);
        yield return Title("MAKE THE SHORE", "Do not leave the boat.", 1.7f);
        _busy = false;
    }

    public IEnumerator PlayEnd(bool win, string title, int score, string[] lines, KeyCode restart)
    {
        _busy = true;
        _canRestart = false;
        if (_titleCo != null)
            StopCoroutine(_titleCo);

        yield return Title(win ? "CATCH" : "LOST", title, win ? 1.35f : 1.7f);
        _titleGroup.alpha = 0f;
        _titleGroup.gameObject.SetActive(false);
        yield return FadePlay(0f, 0.3f);
        yield return FadeTo(win ? 0.12f : 0.22f, 0.7f);

        _resultKicker.text = win ? "NICE" : "DOWN";
        _resultKicker.color = win ? Gold : Danger;
        _resultTitle.text = win ? "ARRIVED" : "LOST";
        _resultTitle.color = Cream;
        _resultMark.text = win ? $"SCORE  {score}" : title;
        _resultMark.color = win ? Mint : Danger;
        string body = "";
        if (lines != null)
        {
            for (int i = 0; i < lines.Length; i++)
                body += lines[i] + "\n";
        }
        _resultBody.text = body.TrimEnd();
        var bodyFit = _resultBody.GetComponent<LayoutElement>();
        if (bodyFit != null)
        {
            int n = 1;
            if (lines != null)
                n = Mathf.Max(1, lines.Length);
            bodyFit.preferredHeight = Mathf.Clamp(28f * n + 12f, 80f, 420f);
            bodyFit.minHeight = bodyFit.preferredHeight;
        }
        _resultHint.text = $"Press  {restart}  to run it again";

        _resultGroup.gameObject.SetActive(true);
        _resultGroup.alpha = 0f;
        var rt = _resultGroup.GetComponent<RectTransform>();
        rt.localScale = Vector3.one * 0.72f;
        SetAlpha(_resultKicker, 0f);
        SetAlpha(_resultTitle, 0f);
        SetAlpha(_resultMark, 0f);
        SetAlpha(_resultBody, 0f);
        SetAlpha(_resultHint, 0f);

        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / 0.3f);
            rt.localScale = Vector3.one * Overshoot(u);
            _resultGroup.alpha = u;
            yield return null;
        }
        rt.localScale = Vector3.one;
        _resultGroup.alpha = 1f;
        yield return FadeIn(_resultKicker, 0.12f);
        yield return FadeIn(_resultTitle, 0.14f);
        yield return FadeIn(_resultMark, 0.12f);
        yield return FadeIn(_resultBody, 0.14f);
        yield return FadeIn(_resultHint, 0.14f);
        _canRestart = true;
        _busy = false;
    }

    public void SetTimer(float seconds, bool urgent)
    {
        seconds = Mathf.Max(0f, seconds);
        int m = Mathf.FloorToInt(seconds / 60f);
        int rest = Mathf.FloorToInt(seconds - m * 60f);
        string shown = $"{m:00}:{rest:00}";
        if (_timerShown != shown)
        {
            _timerShown = shown;
            _timerText.text = shown;
        }
        if (_timerUrgent == urgent)
            return;
        _timerUrgent = urgent;
        _timerText.color = urgent ? Danger : Cream;
        _phaseText.color = urgent ? Danger : Gold;
        _timerText.rectTransform.localScale = Vector3.one;
    }

    public void SetBuildStatus(BoatPiece craft)
    {
        if (_busy && _playGroup.alpha < 0.2f)
            return;
        if (craft == null)
        {
            _statusText.text = "No hull yet";
            _statusText.color = White;
            return;
        }
        int n = BoatHull.HullPieceCount(craft);
        int pct = Mathf.RoundToInt(Mathf.Clamp01(craft.HullStrength) * 100f);
        _statusText.color = White;
        _statusText.text = craft.HullIsCraft
            ? $"{n} pieces   ·   hull {pct}%"
            : $"{n} pieces   ·   not nailed";
    }

    public void SetRaceStatus(float dist, float hull, float flood, bool onBoat)
    {
        _statusText.color = onBoat ? White : Danger;
        _statusText.text = onBoat
            ? $"{dist:0} m   ·   hull {Mathf.RoundToInt(hull * 100f)}%   ·   flood {Mathf.RoundToInt(flood * 100f)}%"
            : "OFF THE HULL";
    }

    public void SetShark(RiverShark a, RiverShark b = null)
    {
        if (_sharkGroup == null)
            return;
        bool show = a != null && !a.IsDead;
        _sharkGroup.alpha = show ? 1f : 0f;
        if (!show)
        {
            if (_sharkRowB != null)
                _sharkRowB.alpha = 0f;
            return;
        }
        bool two = b != null && !b.IsDead;
        LayoutSharkRows(two);
        PaintBar(_sharkFill, _sharkLabel, a, BarName(a, two, 1));
        if (_sharkRowB != null)
            _sharkRowB.alpha = two ? 1f : 0f;
        if (two)
            PaintBar(_sharkFillB, _sharkLabelB, b, BarName(b, true, 2));
    }

    void LayoutSharkRows(bool two)
    {
        var g = _sharkGroup != null ? _sharkGroup.GetComponent<RectTransform>() : null;
        if (g != null)
            g.sizeDelta = new Vector2(380f, two ? 70f : 34f);
        if (_sharkRowA != null)
        {
            var rt = _sharkRowA.GetComponent<RectTransform>();
            rt.anchorMin = two ? new Vector2(0f, 0.54f) : Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    static string BarName(RiverShark shark, bool two, int index)
    {
        string n = shark != null ? shark.DisplayName.ToUpperInvariant() : "SHARK";
        return two ? n + "  " + index : n;
    }

    static void PaintBar(Image fill, Text label, RiverShark shark, string name)
    {
        if (shark == null)
            return;
        float hp = shark.Hp01;
        if (fill != null)
        {
            fill.fillAmount = hp;
            fill.color = Color.Lerp(new Color(0.72f, 0.16f, 0.18f, 0.95f), new Color(0.95f, 0.55f, 0.28f, 0.95f), hp);
        }
        if (label != null)
            label.text = $"{name}   {Mathf.CeilToInt(hp * shark.HealthMax)}";
    }

    void BuildSharkMeter(Transform parent, string id, Vector2 amin, Vector2 amax, Color labelColor, Color fillColor, out Text label, out Image fill, out CanvasGroup row)
    {
        row = Group(parent, "Meter" + id);
        Pin(row.GetComponent<RectTransform>(), amin, amax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        label = FishText(row.transform, "Name", 15, labelColor, FontStyle.Bold, TextAnchor.MiddleLeft);
        Pin(label.rectTransform, new Vector2(0.06f, 0.52f), new Vector2(0.94f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        var track = Solid(row.transform, "Track", new Color(0.08f, 0.09f, 0.07f, 0.5f));
        Pin(track.rectTransform, new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.48f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        fill = Solid(track.transform, "Fill", fillColor);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(-2f, -2f);
        fillRt.pivot = new Vector2(0f, 0.5f);
    }

    public void PulseHurt(float amount)
    {
        _hurtAmt = Mathf.Max(_hurtAmt, Mathf.Clamp(amount, 0f, 0.42f));
    }

    public void ClearHurt()
    {
        _hurtAmt = 0f;
    }

    IEnumerator Title(string title, string sub, float hold)
    {
        if (_titleCo != null)
            StopCoroutine(_titleCo);
        bool done = false;
        _titleCo = StartCoroutine(TitleAnim(title, sub, hold, () => done = true));
        while (!done)
            yield return null;
        _titleCo = null;
    }

    IEnumerator TitleAnim(string title, string sub, float hold, System.Action done)
    {
        _titleText.text = title;
        _subText.text = sub;
        SetAlpha(_titleText, 0f);
        SetAlpha(_subText, 0f);
        var rt = _titleGroup.GetComponent<RectTransform>();
        rt.localScale = Vector3.one * 0.7f;
        rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(-1.6f, 1.6f));
        _titleGroup.alpha = 0f;
        float t = 0f;
        while (t < 0.28f)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / 0.28f);
            rt.localScale = Vector3.one * Overshoot(u);
            _titleGroup.alpha = u;
            yield return null;
        }
        rt.localScale = Vector3.one;
        _titleGroup.alpha = 1f;
        yield return FadeIn(_titleText, 0.1f);
        yield return FadeIn(_subText, 0.12f);
        float wait = 0f;
        while (wait < hold)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }
        t = 0f;
        Vector2 start = rt.anchoredPosition;
        while (t < 0.4f)
        {
            t += Time.unscaledDeltaTime;
            float u = t / 0.4f;
            _titleGroup.alpha = 1f - u;
            rt.anchoredPosition = start + Vector2.up * (u * 28f);
            rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.94f, u);
            yield return null;
        }
        _titleGroup.alpha = 0f;
        rt.anchoredPosition = new Vector2(0f, 12f);
        done?.Invoke();
    }

    IEnumerator FadeTo(float a, float dur)
    {
        float from = _fade.color.a;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.05f, dur);
            SetFade(Mathf.Lerp(from, a, Smooth(Mathf.Clamp01(t))));
            yield return null;
        }
        SetFade(a);
    }

    IEnumerator FadePlay(float a, float dur)
    {
        float from = _playGroup.alpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.05f, dur);
            _playGroup.alpha = Mathf.Lerp(from, a, Smooth(Mathf.Clamp01(t)));
            yield return null;
        }
        _playGroup.alpha = a;
    }

    void HideResult()
    {
        _resultGroup.gameObject.SetActive(false);
        _resultGroup.alpha = 0f;
    }

    void SetFade(float a)
    {
        var c = _fade.color;
        c.a = a;
        _fade.color = c;
        _fade.gameObject.SetActive(a > 0.001f);
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);

    static float Overshoot(float u)
    {
        u = Mathf.Clamp01(u);
        return 1f + 1.6f * Mathf.Pow(u - 1f, 3f) + 0.6f * Mathf.Pow(u - 1f, 2f);
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
        float to = c.a > 0.01f ? c.a : 1f;
        c.a = 0f;
        text.color = c;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Clamp01(t / dur) * to;
            text.color = c;
            yield return null;
        }
        c.a = to;
        text.color = c;
    }

    static CanvasGroup Group(Transform parent, string name)
    {
        var g = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<CanvasGroup>();
        g.transform.SetParent(parent, false);
        g.blocksRaycasts = false;
        g.interactable = false;
        return g;
    }

    static Image Solid(Transform parent, string name, Color color)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = HorrorPaperUI.WhiteSprite();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text FishText(Transform parent, string name, int size, Color color, FontStyle style, TextAnchor align)
    {
        var t = new GameObject(name, typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = _font != null ? _font : HorrorPaperUI.Font();
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var ol = t.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.05f, 0.08f, 0.04f, 0.55f);
        ol.effectDistance = new Vector2(1.4f, -1.4f);
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.35f);
        sh.effectDistance = new Vector2(0f, -3f);
        return t;
    }

    static Sprite Vignette()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx * 0.72f + dy * dy);
                float a = Mathf.SmoothStep(0.42f, 1f, d);
                a = Mathf.Pow(a, 1.65f) * 0.9f;
                byte b = (byte)(Mathf.Clamp01(a) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, b);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Pin(RectTransform rt, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = amin;
        rt.anchorMax = amax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Row(Text text, float height)
    {
        var le = text.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleHeight = 0f;
        text.horizontalOverflow = text.horizontalOverflow == HorizontalWrapMode.Wrap
            ? HorizontalWrapMode.Wrap
            : HorizontalWrapMode.Overflow;
    }
}
