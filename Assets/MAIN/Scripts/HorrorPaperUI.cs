using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Процедурные бумажные спрайты: тетрадь, обрывок, предсмертная записка, печать.
/// HUD-шкалы используют лёгкий <see cref="Paper"/>.
/// </summary>
public static class HorrorPaperUI
{
    public static readonly Color Ink = new Color(0.14f, 0.09f, 0.06f, 0.94f);
    public static readonly Color InkMuted = new Color(0.2f, 0.13f, 0.08f, 0.78f);
    public static readonly Color InkFaint = new Color(0.24f, 0.16f, 0.1f, 0.55f);
    public static readonly Color Blood = new Color(0.42f, 0.08f, 0.06f, 0.7f);
    public static readonly Color BloodDeep = new Color(0.3f, 0.05f, 0.04f, 0.9f);
    public static readonly Color Pencil = new Color(0.3f, 0.26f, 0.2f, 0.4f);
    public static readonly Color Stamina = new Color(0.42f, 0.34f, 0.24f, 0.92f);
    public static readonly Color PaperHud = new Color(0.83f, 0.76f, 0.64f, 0.18f);
    public static readonly Color PaperSheet = Color.white;
    public static readonly Color PaperSlot = new Color(1f, 1f, 1f, 0.02f);
    public static readonly Color PaperSlotFilled = new Color(1f, 1f, 1f, 0.04f);
    public static readonly Color PaperSlotSelected = new Color(0.55f, 0.22f, 0.14f, 0.12f);
    public static readonly Color Dim = new Color(0.04f, 0.03f, 0.02f, 0.52f);
    public static readonly Color Line = new Color(0.35f, 0.22f, 0.18f, 0.18f);
    public static readonly Color RuleBlue = new Color(0.42f, 0.5f, 0.62f, 0.22f);

    public enum Kind
    {
        Soft,
        Notebook,
        Scrap,
        Death,
        Tape,
        Stamp,
        Shadow
    }

    static Font _font;
    static readonly Sprite[] Cache = new Sprite[8];

    public static Font Font()
    {
        if (_font != null)
            return _font;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null)
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return _font;
    }

    static Sprite _white;
    static Sprite _dot;

    public static Sprite WhiteSprite()
    {
        if (_white != null)
            return _white;
        var tex = Texture2D.whiteTexture;
        _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 4f);
        return _white;
    }

    public static Sprite SoftDot()
    {
        if (_dot != null)
            return _dot;

        const int size = 64;
        var tex = NewTex(size, size);
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Exp(-d * d * 7.5f);
                a *= 0.85f;
                px[y * size + x] = To32(0.78f, 0.08f, 0.07f, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _dot = Simple(tex);
        return _dot;
    }

    public static Sprite Paper(bool soft = false) => Get(soft ? Kind.Soft : Kind.Notebook);

    public static Sprite Get(Kind kind)
    {
        int i = (int)kind;
        if (Cache[i] == null)
            Cache[i] = Bake(kind);
        return Cache[i];
    }

    public static Image PaperImage(Transform parent, string name, Color tint, bool raycast = false)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = Get(Kind.Soft);
        img.color = tint;
        img.type = Image.Type.Sliced;
        img.raycastTarget = raycast;
        return img;
    }

    public static Image Sheet(Transform parent, string name, Kind kind, bool raycast = false)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = Get(kind);
        img.color = Color.white;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
        img.raycastTarget = raycast;
        return img;
    }

    public static Text InkText(Transform parent, string name, int size, TextAnchor align, Color color)
    {
        var t = new GameObject(name, typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Font();
        t.fontSize = size;
        t.fontStyle = FontStyle.Italic;
        t.alignment = align;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static Sprite Bake(Kind kind)
    {
        switch (kind)
        {
            case Kind.Soft: return Slice(MakeSheet(192, 192, lined: false, holes: false, blood: false, torn: 0.04f, seed: 1.7f), 28);
            case Kind.Notebook: return Simple(MakeSheet(640, 880, lined: true, holes: true, blood: false, torn: 0.035f, seed: 4.1f));
            case Kind.Scrap: return Simple(MakeSheet(420, 160, lined: false, holes: false, blood: false, torn: 0.12f, seed: 9.4f, tape: true));
            case Kind.Death: return Simple(MakeSheet(560, 720, lined: false, holes: false, blood: true, torn: 0.07f, seed: 21.2f, crumple: 1.35f));
            case Kind.Tape: return Simple(MakeTape(256, 72));
            case Kind.Stamp: return Simple(MakeStamp(256));
            case Kind.Shadow: return Simple(MakeShadow(256, 256));
            default: return Simple(MakeSheet(256, 256, false, false, false, 0.05f, 0f));
        }
    }

    static Sprite Simple(Texture2D tex) =>
        Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);

    static Sprite Slice(Texture2D tex, int border) =>
        Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));

    static Texture2D MakeSheet(int w, int h, bool lined, bool holes, bool blood, float torn, float seed,
        bool tape = false, float crumple = 1f)
    {
        var tex = NewTex(w, h);
        var px = new Color32[w * h];

        Vector2 coffeeA = new Vector2(0.22f + Hash(seed) * 0.2f, 0.18f);
        Vector2 coffeeB = new Vector2(0.74f, 0.62f + Hash(seed + 2f) * 0.2f);
        Vector2 bloodA = new Vector2(0.78f, 0.22f);
        Vector2 bloodB = new Vector2(0.18f, 0.8f);

        for (int y = 0; y < h; y++)
        {
            float ny = y / (h - 1f);
            for (int x = 0; x < w; x++)
            {
                float nx = x / (w - 1f);
                float mask = EdgeMask(nx, ny, torn, seed);
                if (mask <= 0.001f)
                {
                    px[y * w + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float fiber = N(nx * 18f + seed, ny * 2.2f);
                float pulp = N(nx * 6f + seed * 0.3f, ny * 7f);
                float grain = N(nx * 42f, ny * 38f + seed);
                float fold = N(nx * 2.4f + 3f, ny * 3.1f) * crumple;
                float light = 0.78f + fiber * 0.1f + pulp * 0.08f + grain * 0.04f + (fold - 0.5f) * 0.16f;

                float fox = Mathf.Max(0f, N(nx * 11f + 8f, ny * 9f) - 0.72f) * 1.8f;
                float ring = Coffee(nx, ny, coffeeA, 0.16f) + Coffee(nx, ny, coffeeB, 0.11f) * 0.7f;

                float r = 0.78f * light - fox * 0.12f - ring * 0.1f;
                float g = 0.71f * light - fox * 0.16f - ring * 0.14f;
                float b = 0.56f * light - fox * 0.18f - ring * 0.12f;

                if (lined)
                {
                    float leftMargin = 0.16f;
                    if (nx > leftMargin + 0.01f && ny > 0.1f && ny < 0.92f)
                    {
                        float lineEvery = 28f / h;
                        float phase = (ny - 0.1f) / lineEvery;
                        float d = Mathf.Abs(phase - Mathf.Round(phase));
                        if (d * lineEvery * h < 1.15f)
                        {
                            r = Mathf.Lerp(r, 0.45f, 0.28f);
                            g = Mathf.Lerp(g, 0.52f, 0.28f);
                            b = Mathf.Lerp(b, 0.64f, 0.32f);
                        }
                    }
                    if (Mathf.Abs(nx - 0.155f) * w < 1.4f && ny > 0.08f && ny < 0.94f)
                    {
                        r = Mathf.Lerp(r, 0.62f, 0.45f);
                        g = Mathf.Lerp(g, 0.28f, 0.45f);
                        b = Mathf.Lerp(b, 0.28f, 0.45f);
                    }
                }

                if (holes)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float hy = 0.14f + i * 0.16f;
                        float d = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.07f, hy));
                        if (d < 0.018f)
                            mask = 0f;
                        else if (d < 0.026f)
                        {
                            r *= 0.55f;
                            g *= 0.55f;
                            b *= 0.55f;
                        }
                    }
                }

                if (blood)
                {
                    float splat = BloodSplat(nx, ny, bloodA, 0.2f, seed) + BloodSplat(nx, ny, bloodB, 0.12f, seed + 4f) * 0.65f;
                    splat += Mathf.Max(0f, 0.08f - Vector2.Distance(new Vector2(nx, ny), new Vector2(0.5f, 0.12f))) * 3f;
                    r = Mathf.Lerp(r, 0.38f, splat);
                    g = Mathf.Lerp(g, 0.05f, splat);
                    b = Mathf.Lerp(b, 0.04f, splat);
                }

                if (tape && ny > 0.78f && nx > 0.08f && nx < 0.42f)
                {
                    r = Mathf.Lerp(r, 0.86f, 0.35f);
                    g = Mathf.Lerp(g, 0.78f, 0.35f);
                    b = Mathf.Lerp(b, 0.52f, 0.28f);
                }

                float a = mask * (0.92f + grain * 0.06f);
                px[y * w + x] = To32(r, g, b, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static Texture2D MakeTape(int w, int h)
    {
        var tex = NewTex(w, h);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float ny = y / (h - 1f);
            for (int x = 0; x < w; x++)
            {
                float nx = x / (w - 1f);
                float jag = 0.08f * N(nx * 14f, 3.2f);
                if (ny < 0.12f + jag || ny > 0.88f - jag * 0.5f)
                {
                    px[y * w + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float g = 0.82f + N(nx * 20f, ny * 8f) * 0.08f;
                float a = 0.55f * (1f - Mathf.Abs(ny - 0.5f) * 0.4f);
                px[y * w + x] = To32(g, g * 0.94f, g * 0.7f, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static Texture2D MakeStamp(int size)
    {
        var tex = NewTex(size, size);
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float n = N(x * 0.05f, y * 0.05f);
                float ring = Mathf.Abs(d - 0.78f);
                float inner = Mathf.Abs(d - 0.62f);
                float fill = d < 0.78f ? 0.12f : 0f;
                float ink = 0f;
                if (ring < 0.045f + n * 0.02f) ink = 0.85f;
                if (inner < 0.03f) ink = Mathf.Max(ink, 0.55f);
                if (d > 0.92f) ink = 0f;
                float a = Mathf.Clamp01(ink + fill) * (0.55f + n * 0.2f);
                if (a < 0.02f)
                    px[y * size + x] = new Color32(0, 0, 0, 0);
                else
                    px[y * size + x] = To32(0.38f, 0.08f, 0.07f, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static Texture2D MakeShadow(int w, int h)
    {
        var tex = NewTex(w, h);
        var px = new Color32[w * h];
        Vector2 c = new Vector2(0.5f, 0.48f);
        for (int y = 0; y < h; y++)
        {
            float ny = y / (h - 1f);
            for (int x = 0; x < w; x++)
            {
                float nx = x / (w - 1f);
                float dx = (nx - c.x) / 0.42f;
                float dy = (ny - c.y) / 0.46f;
                float d = dx * dx + dy * dy;
                float a = Mathf.Exp(-d * 2.8f) * 0.45f;
                px[y * w + x] = To32(0.02f, 0.015f, 0.01f, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static float EdgeMask(float nx, float ny, float torn, float seed)
    {
        float jL = (N(ny * 14f + seed, seed) - 0.5f) * torn * 2.2f;
        float jR = (N(ny * 16f + seed * 1.3f, seed + 2f) - 0.5f) * torn * 2.2f;
        float jB = (N(nx * 15f + seed, seed + 4f) - 0.5f) * torn * 2f;
        float jT = (N(nx * 13f + seed * 0.7f, seed + 6f) - 0.5f) * torn * 1.6f;
        float d = Mathf.Min(nx - 0.02f - jL, 1f - nx - 0.02f - jR);
        d = Mathf.Min(d, ny - 0.02f - jB);
        d = Mathf.Min(d, 1f - ny - 0.02f - jT);
        return Mathf.Clamp01(d / 0.018f);
    }

    static float Coffee(float nx, float ny, Vector2 c, float rad)
    {
        float d = Vector2.Distance(new Vector2(nx, ny), c);
        return Mathf.Exp(-Mathf.Abs(d - rad) * 38f) * 0.55f;
    }

    static float BloodSplat(float nx, float ny, Vector2 c, float rad, float seed)
    {
        float a = Mathf.Atan2(ny - c.y, nx - c.x);
        float d = Vector2.Distance(new Vector2(nx, ny), c);
        float wobble = rad * (0.7f + 0.45f * N(Mathf.Cos(a) * 3f + seed, Mathf.Sin(a) * 3f));
        return Mathf.Clamp01((wobble - d) / (wobble * 0.55f));
    }

    static float N(float x, float y) => Mathf.PerlinNoise(x, y);
    static float Hash(float s) => Mathf.Abs(Mathf.Sin(s * 12.9898f) * 43758.5453f) % 1f;

    static Texture2D NewTex(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "HorrorPaper"
        };
        return tex;
    }

    static Color32 To32(float r, float g, float b, float a)
    {
        return new Color32(
            (byte)(Mathf.Clamp01(r) * 255f),
            (byte)(Mathf.Clamp01(g) * 255f),
            (byte)(Mathf.Clamp01(b) * 255f),
            (byte)(Mathf.Clamp01(a) * 255f));
    }
}
