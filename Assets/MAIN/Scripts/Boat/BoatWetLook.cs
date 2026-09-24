using UnityEngine;

/// <summary>
/// Мокрая кромка на дереве по высоте воды. MaterialPropertyBlock, без клонов материалов.
/// </summary>
public static class BoatWetLook
{
    static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int Smooth = Shader.PropertyToID("_Smoothness");

    public static void Apply(BoatPiece piece)
    {
        if (piece == null || piece.Kind == BoatPieceKind.Oar)
            return;
        var rends = piece.LookRenderers;
        if (rends == null || rends.Length == 0)
            return;
        if (!BoatWater.TryHeight(piece.transform.position, out float waterY))
            return;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            if (rends[i] != null)
                b.Encapsulate(rends[i].bounds);
        }
        float wet = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b.max.y + 0.06f, b.min.y - 0.02f, waterY));
        if (piece.HullFlood > 0.2f)
            wet = Mathf.Max(wet, piece.HullFlood * 0.85f);
        if (wet < 0.02f)
            wet = 0f;
        if (Mathf.Abs(wet - piece.WetShown) < 0.035f)
            return;
        piece.WetShown = wet;

        Color tint = Color.Lerp(Color.white, new Color(0.58f, 0.66f, 0.72f, 1f), wet * 0.62f);

        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null)
                continue;
            r.GetPropertyBlock(Block);
            var mat = r.sharedMaterial;
            Color baseC = Color.white;
            if (mat != null)
            {
                if (mat.HasProperty(BaseColor))
                    baseC = mat.GetColor(BaseColor);
                else if (mat.HasProperty(ColorId))
                    baseC = mat.GetColor(ColorId);
            }
            Color wetC = baseC * tint;
            wetC.a = baseC.a;
            if (mat != null && mat.HasProperty(BaseColor))
                Block.SetColor(BaseColor, wetC);
            if (mat != null && mat.HasProperty(ColorId))
                Block.SetColor(ColorId, wetC);
            if (mat != null && mat.HasProperty(Smooth))
            {
                float s = mat.GetFloat(Smooth);
                Block.SetFloat(Smooth, Mathf.Lerp(s, Mathf.Max(s, 0.68f), wet));
            }
            r.SetPropertyBlock(Block);
        }
    }
}
