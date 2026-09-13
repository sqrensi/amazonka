using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Примитивы Unity с непрозрачным URP Lit.
/// </summary>
public static class BoatVisuals
{
    static Material _wood;
    static Material _woodDark;
    static Material _barrel;
    static Material _metal;
    static Material _rope;
    static Material _ghost;

    public static Material Wood => _wood != null ? _wood : (_wood = Opaque(new Color(0.72f, 0.52f, 0.28f)));
    public static Material WoodDark => _woodDark != null ? _woodDark : (_woodDark = Opaque(new Color(0.42f, 0.28f, 0.14f)));
    public static Material Barrel => _barrel != null ? _barrel : (_barrel = Opaque(new Color(0.55f, 0.28f, 0.14f)));
    public static Material Metal => _metal != null ? _metal : (_metal = Opaque(new Color(0.62f, 0.64f, 0.68f), 0.65f, 0.4f));
    public static Material Rope => _rope != null ? _rope : (_rope = Opaque(new Color(0.7f, 0.58f, 0.32f)));
    public static Material Ghost => _ghost != null ? _ghost : (_ghost = Opaque(new Color(0.45f, 0.85f, 0.5f)));
    static Material _sawMark;
    public static Material SawMark
    {
        get
        {
            if (_sawMark != null)
                return _sawMark;
            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _sawMark = s != null ? new Material(s) : Opaque(new Color(0.15f, 1f, 0.25f));
            _sawMark.name = "BoatSawMark";
            var c = new Color(0.2f, 1f, 0.3f, 1f);
            _sawMark.color = c;
            if (_sawMark.HasProperty("_BaseColor"))
                _sawMark.SetColor("_BaseColor", c);
            if (_sawMark.HasProperty("_Color"))
                _sawMark.SetColor("_Color", c);
            return _sawMark;
        }
    }

    public static Vector3 DefaultSize(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Log: return new Vector3(0.28f, 0.28f, 1.45f);
            case BoatPieceKind.Barrel: return new Vector3(0.5f, 0.62f, 0.5f);
            default: return new Vector3(0.22f, 0.045f, 1.15f);
        }
    }

    public static float Mass(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Log: return 6.5f;
            case BoatPieceKind.Barrel: return 8f;
            default: return 2.2f;
        }
    }

    public static float Buoyancy(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Barrel: return 38f;
            case BoatPieceKind.Log: return 18f;
            default: return 9f;
        }
    }

    public static PrimitiveType Shape(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Log: return PrimitiveType.Cylinder;
            case BoatPieceKind.Barrel: return PrimitiveType.Cylinder;
            default: return PrimitiveType.Cube;
        }
    }

    public static Material MaterialFor(BoatPieceKind kind)
    {
        switch (kind)
        {
            case BoatPieceKind.Log: return WoodDark;
            case BoatPieceKind.Barrel: return Barrel;
            default: return Wood;
        }
    }

    public static Vector3 VisualScale(BoatPieceKind kind, Vector3 size)
    {
        switch (kind)
        {
            case BoatPieceKind.Log:
                return new Vector3(size.x, size.z * 0.5f, size.x);
            case BoatPieceKind.Barrel:
                return new Vector3(size.x, size.y * 0.5f, size.z);
            default:
                return size;
        }
    }

    public static Quaternion VisualRotation(BoatPieceKind kind)
    {
        return kind == BoatPieceKind.Log ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
    }

    /// <summary>Дочерний примитив Unity без своего коллайдера.</summary>
    public static GameObject Attach(Transform parent, PrimitiveType type, Vector3 localScale, Material mat, string name = "Vis")
    {
        return Attach(parent, type, localScale, Quaternion.identity, mat, name);
    }

    public static void ClearChild(Transform parent, string name)
    {
        if (parent == null)
            return;
        Transform old = parent.Find(name);
        while (old != null)
        {
            Object.DestroyImmediate(old.gameObject);
            old = parent.Find(name);
        }
    }

    public static GameObject Attach(Transform parent, PrimitiveType type, Vector3 localScale, Quaternion localRot, Material mat, string name = "Vis")
    {
        ClearChild(parent, name);

        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = MeshOf(type);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat != null ? mat : Wood;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        return go;
    }

    public static void StripPhysics(GameObject go)
    {
        if (go == null)
            return;
        var cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                Object.DestroyImmediate(cols[i]);
        }
        var rbs = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            if (rbs[i] != null)
                Object.DestroyImmediate(rbs[i]);
        }
    }

    public static void SetIgnoreRaycast(GameObject go)
    {
        if (go == null)
            return;
        int layer = LayerMask.NameToLayer("Ignore Raycast");
        if (layer < 0)
            layer = 2;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    public static Mesh CubeMesh() => MeshOf(PrimitiveType.Cube);
    public static Mesh CylinderMesh() => MeshOf(PrimitiveType.Cylinder);

    static Mesh _cubeMesh;
    static Mesh _cylMesh;
    static Mesh _capMesh;

    static Mesh MeshOf(PrimitiveType type)
    {
        if (type == PrimitiveType.Cylinder)
        {
            if (_cylMesh == null)
                _cylMesh = StealMesh(PrimitiveType.Cylinder);
            return _cylMesh;
        }
        if (type == PrimitiveType.Capsule)
        {
            if (_capMesh == null)
                _capMesh = StealMesh(PrimitiveType.Capsule);
            return _capMesh;
        }
        if (_cubeMesh == null)
            _cubeMesh = StealMesh(PrimitiveType.Cube);
        return _cubeMesh;
    }

    static Mesh StealMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        return mesh;
    }

    public static Shader FindUrpLit()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s != null)
            return s;
        s = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (s != null)
            return s;

        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null)
            rp = GraphicsSettings.defaultRenderPipeline;
        if (rp != null && rp.defaultMaterial != null && rp.defaultMaterial.shader != null)
            return rp.defaultMaterial.shader;

#if UNITY_EDITOR
        s = AssetDatabase.LoadAssetAtPath<Shader>(
            "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
        if (s != null)
            return s;
        string[] guids = AssetDatabase.FindAssets("t:Shader Lit");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.Replace('\\', '/').EndsWith("/Shaders/Lit.shader") &&
                path.Contains("render-pipelines.universal"))
                return AssetDatabase.LoadAssetAtPath<Shader>(path);
        }
#endif
        return Shader.Find("Sprites/Default");
    }

    public static void Tint(Material m, Color c, float metallic = 0f, float smoothness = 0.22f)
    {
        if (m == null)
            return;
        m.color = c;
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color"))
            m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Surface"))
            m.SetFloat("_Surface", 0f);
        m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (m.HasProperty("_ZWrite"))
            m.SetFloat("_ZWrite", 1f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }

    static Material Opaque(Color c, float metallic = 0f, float smoothness = 0.22f)
    {
        Shader shader = FindUrpLit();
        var m = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
        m.name = "BoatUrp";
        Tint(m, c, metallic, smoothness);
        return m;
    }

    public static int LengthAxis(BoatPieceKind kind)
    {
        return kind == BoatPieceKind.Barrel ? 1 : 2;
    }

    public static void BuildHammer(Transform root)
    {
        var handle = Attach(root, PrimitiveType.Cylinder, new Vector3(0.032f, 0.13f, 0.032f), WoodDark, "Handle");
        handle.transform.localPosition = new Vector3(0f, -0.02f, 0f);

        var head = Attach(root, PrimitiveType.Cube, new Vector3(0.18f, 0.07f, 0.07f), Metal, "Head");
        head.transform.localPosition = new Vector3(0f, 0.16f, 0f);

        var face = Attach(root, PrimitiveType.Cube, new Vector3(0.045f, 0.065f, 0.065f), Metal, "Face");
        face.transform.localPosition = new Vector3(0.11f, 0.16f, 0f);

        var peen = Attach(root, PrimitiveType.Cube, new Vector3(0.04f, 0.045f, 0.045f), Metal, "Peen");
        peen.transform.localPosition = new Vector3(-0.11f, 0.16f, 0f);
    }

    public static void BuildSaw(Transform root)
    {
        var blade = Attach(root, PrimitiveType.Cube, new Vector3(0.01f, 0.075f, 0.42f), Metal, "Blade");
        blade.transform.localPosition = new Vector3(0f, 0.03f, 0.12f);

        var spine = Attach(root, PrimitiveType.Cube, new Vector3(0.016f, 0.02f, 0.42f), Metal, "Spine");
        spine.transform.localPosition = new Vector3(0f, 0.068f, 0.12f);

        for (int i = 0; i < 12; i++)
        {
            float z = -0.02f + i * 0.032f;
            var tooth = Attach(root, PrimitiveType.Cube, new Vector3(0.008f, 0.028f, 0.018f), Metal, "Tooth" + i);
            tooth.transform.localPosition = new Vector3(0f, -0.02f, z);
            tooth.transform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? 28f : -28f);
        }

        var grip = Attach(root, PrimitiveType.Cube, new Vector3(0.038f, 0.12f, 0.038f), Wood, "Handle");
        grip.transform.localPosition = new Vector3(0f, -0.04f, -0.12f);
        grip.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

        var ferrule = Attach(root, PrimitiveType.Cube, new Vector3(0.042f, 0.028f, 0.04f), Metal, "Ferrule");
        ferrule.transform.localPosition = new Vector3(0f, 0.02f, -0.08f);
    }
}
