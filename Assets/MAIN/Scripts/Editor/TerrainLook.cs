#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Слои с разным tile size + автопокрас всего террейна (берег / трава / склон / скала).
/// Horror/Paint Terrain Look — повторить. Один раз после компиляции, пока нет ключа в EditorPrefs.
/// </summary>
[InitializeOnLoad]
static class TerrainLook
{
    const string PrefKey = "Horror.TerrainPaint.v1";
    const string TerrainPath = "Assets/New Terrain.asset";
    const string LayerDir = "Assets/MAIN/TerrainLayers";
    const string AdgRoot = "Assets/ADG_Textures/ground_vol1/";

    static TerrainLook()
    {
        EditorApplication.delayCall += TryAuto;
        EditorApplication.delayCall += EnsureTriplanar;
        EditorApplication.delayCall += RestoreLayerRefsIfBroken;
    }

    static void EnsureTriplanar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null && terrains[i].GetComponent<TerrainTriplanar>() == null)
                terrains[i].gameObject.AddComponent<TerrainTriplanar>();
        }
    }

    static void TryAuto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAuto;
            return;
        }
        if (EditorPrefs.GetInt(PrefKey, 0) >= 1)
            return;
        if (PaintAll())
            EditorPrefs.SetInt(PrefKey, 1);
    }

    [MenuItem("Horror/Paint Terrain Look")]
    public static void MenuPaint()
    {
        if (PaintAll())
            EditorPrefs.SetInt(PrefKey, 1);
    }

    [MenuItem("Horror/Restore Terrain Layers")]
    public static void MenuRestoreLayers()
    {
        RestoreLayerRefs();
    }

    static void RestoreLayerRefsIfBroken()
    {
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        if (data == null)
            return;
        if (!LayersBroken(data))
            return;
        RestoreLayerRefs();
    }

    static bool LayersBroken(TerrainData data)
    {
        var layers = data.terrainLayers;
        if (layers == null || layers.Length < 8)
            return true;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null || layers[i].diffuseTexture == null)
                return true;
        }
        return false;
    }

    public static void RestoreLayerRefs()
    {
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        if (data == null)
            return;
        TerrainLayer[] layers = BuildLayers();
        if (layers == null || layers.Length < 8)
            return;
        data.terrainLayers = layers;
        EditorUtility.SetDirty(data);
        var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null && terrains[i].terrainData == data)
                terrains[i].terrainData.terrainLayers = layers;
        }
        if (!Application.isPlaying)
            AssetDatabase.SaveAssets();
        Debug.Log("[Terrain] Restored layer textures");
    }

    public static bool PaintAll()
    {
        FixSharedLayerTiles();
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        if (data == null)
        {
            Debug.LogWarning("[Terrain] No data at " + TerrainPath);
            return false;
        }

        TerrainLayer[] layers = BuildLayers();
        if (layers == null || layers.Length < 8)
            return false;

        Undo.RegisterCompleteObjectUndo(data, "Paint Terrain Look");
        data.terrainLayers = layers;

        float waterY = FindWaterY();
        Vector2[] river = CollectRiver();
        float riverWidth = 36f;

        int w = data.alphamapWidth;
        int h = data.alphamapHeight;
        int n = layers.Length;
        float[,,] map = new float[w, h, n];
        Vector3 size = data.size;

        try
        {
            for (int y = 0; y < h; y++)
            {
                if ((y & 15) == 0)
                    EditorUtility.DisplayProgressBar("Terrain", "Painting " + y + "/" + h, y / (float)h);
                float v = h <= 1 ? 0f : y / (h - 1f);
                for (int x = 0; x < w; x++)
                {
                    float u = w <= 1 ? 0f : x / (w - 1f);
                    float height = data.GetInterpolatedHeight(u, v);
                    Vector3 nrm = data.GetInterpolatedNormal(u, v);
                    float slope = Vector3.Angle(nrm, Vector3.up);
                    float above = height - waterY;
                    float wx = u * size.x;
                    float wz = v * size.z;
                    float rd = RiverDist(wx, wz, river);

                    float macro = Noise(u * 3.4f, v * 3.4f, 2.1f);
                    float mid = Noise(u * 14f, v * 14f, 8.7f);
                    float fine = Noise(u * 41f, v * 41f, 19.4f);
                    float patch = Noise(u * 7.2f, v * 7.2f, 4.4f);

                    float wet = Smooth(2.6f, 0.22f, above);
                    float channel = Smooth(riverWidth * 0.55f, 5f, rd) * Smooth(1.9f, 0.05f, above);
                    wet = Mathf.Max(wet, channel * 0.85f);

                    float cliff = Smooth(38f, 58f, slope);
                    float steep = Smooth(18f, 42f, slope) * (1f - cliff);
                    float gravelBand = Smooth(10f, 26f, slope) * (1f - cliff) * (1f - wet * 0.7f);
                    float alpine = Smooth(22f, 48f, height);
                    float flat = (1f - wet) * (1f - steep) * (1f - cliff);

                    float bank = wet * (0.55f + 0.45f * mid);
                    float moss = flat * (0.25f + 0.55f * (1f - macro)) * (1f - alpine * 0.4f) + wet * 0.12f * (1f - cliff);
                    float grassA = flat * (0.45f + 0.55f * macro) * (0.4f + 0.6f * patch);
                    float grassB = flat * (0.35f + 0.65f * (1f - patch)) * (0.5f + 0.5f * fine);
                    float gravel = gravelBand * (0.4f + 0.6f * mid) + alpine * 0.15f * (1f - cliff);
                    float stones = steep * (0.45f + 0.55f * fine) + alpine * 0.25f;
                    float rock = cliff * (0.65f + 0.35f * mid) + alpine * steep * 0.35f;
                    float dirt = flat * 0.18f * (1f - macro) * (0.3f + 0.7f * fine) + wet * 0.08f;

                    float[] wgt =
                    {
                        bank, moss, grassA, grassB, gravel, stones, rock, dirt
                    };
                    float sum = 0.0001f;
                    for (int i = 0; i < 8; i++)
                    {
                        wgt[i] = Mathf.Max(0f, wgt[i]);
                        sum += wgt[i];
                    }
                    for (int i = 0; i < n; i++)
                        map[x, y, i] = i < 8 ? wgt[i] / sum : 0f;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        data.SetAlphamaps(0, 0, map);
        data.SetBaseMapDirty();
        EditorUtility.SetDirty(data);

        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || t.terrainData != data)
                continue;
            t.basemapDistance = 20000f;
            t.heightmapPixelError = 4f;
            t.Flush();
            EditorUtility.SetDirty(t);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Terrain] Painted " + w + "x" + h + " waterY=" + waterY.ToString("0.00") + " riverPts=" + river.Length);
        return true;
    }

    static TerrainLayer[] BuildLayers()
    {
        var bank = AdgLayer("AdgBank", "ground1", new Vector2(6.5f, 6.5f), new Vector2(0f, 0f), 0.16f);
        var moss = CopyLayer("SoilMoss.terrainlayer", "PaintMoss", new Vector2(12f, 12f), new Vector2(3.1f, 1.4f), 0.1f);
        var grassA = CopyLayer("Grass01.terrainlayer", "PaintGrassA", new Vector2(14f, 14f), new Vector2(0.6f, 2.8f), 0.11f);
        var grassB = CopyLayer("Grass03.terrainlayer", "PaintGrassB", new Vector2(9f, 9f), new Vector2(5.4f, 1.1f), 0.11f);
        if (grassB == null)
            grassB = CopyLayer("Grass02.terrainlayer", "PaintGrassB", new Vector2(9f, 9f), new Vector2(5.4f, 1.1f), 0.11f);
        var gravel = AdgLayer("AdgGravel", "ground4", new Vector2(7.2f, 7.2f), new Vector2(2.2f, 6.7f), 0.14f);
        if (gravel == null)
            gravel = CopyLayer("SoilGravel01.terrainlayer", "PaintGravel", new Vector2(7.2f, 7.2f), new Vector2(2.2f, 6.7f), 0.14f);
        var stones = CopyLayer("GroundStones01.terrainlayer", "PaintStones", new Vector2(16f, 16f), new Vector2(1.8f, 4.9f), 0.08f);
        var rock = AdgLayer("AdgRock", "ground10", new Vector2(18f, 18f), new Vector2(7.5f, 0.9f), 0.07f);
        if (rock == null)
            rock = CopyLayer("MossStones.terrainlayer", "PaintRock", new Vector2(18f, 18f), new Vector2(7.5f, 0.9f), 0.07f);
        var dirt = CopyLayer("Ground02.terrainlayer", "PaintDirt", new Vector2(11f, 11f), new Vector2(4.2f, 8.1f), 0.1f);
        if (dirt == null)
            dirt = CopyLayer("SandySoil.terrainlayer", "PaintDirt", new Vector2(11f, 11f), new Vector2(4.2f, 8.1f), 0.1f);

        if (bank == null)
            bank = CopyLayer("SandySoil.terrainlayer", "PaintBank", new Vector2(6.5f, 6.5f), Vector2.zero, 0.16f);
        if (moss == null || grassA == null || grassB == null || gravel == null || stones == null || rock == null || dirt == null)
        {
            Debug.LogWarning("[Terrain] Missing layers");
            return null;
        }
        return new[] { bank, moss, grassA, grassB, gravel, stones, rock, dirt };
    }

    static TerrainLayer CopyLayer(string file, string name, Vector2 tile, Vector2 offset, float smooth)
    {
        var src = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + "/" + file);
        if (src == null)
            return null;
        string path = LayerDir + "/" + name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = Object.Instantiate(src);
            layer.name = name;
            AssetDatabase.CreateAsset(layer, path);
        }
        else
        {
            layer.diffuseTexture = src.diffuseTexture;
            layer.normalMapTexture = src.normalMapTexture;
            layer.maskMapTexture = src.maskMapTexture;
        }
        layer.tileSize = tile;
        layer.tileOffset = offset;
        layer.smoothness = smooth;
        layer.metallic = 0f;
        layer.normalScale = 1.15f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static TerrainLayer AdgLayer(string name, string folder, Vector2 tile, Vector2 offset, float smooth)
    {
        string root = AdgRoot + folder + "/" + folder;
        var diff = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "_Diffuse.tga");
        if (diff == null)
            return null;
        string path = LayerDir + "/" + name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = diff;
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "_Normal.tga");
        layer.tileSize = tile;
        layer.tileOffset = offset;
        layer.smoothness = smooth;
        layer.metallic = 0f;
        layer.normalScale = 1.2f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static void FixSharedLayerTiles()
    {
        SetTile("Grass01.terrainlayer", 14f);
        SetTile("Grass02.terrainlayer", 12f);
        SetTile("Grass03.terrainlayer", 11f);
        SetTile("Grass04.terrainlayer", 13f);
        SetTile("Grass05.terrainlayer", 10f);
        SetTile("Grass06.terrainlayer", 12f);
        SetTile("GrassLeafs01.terrainlayer", 8.5f);
        SetTile("GrassLeafs02.terrainlayer", 9.5f);
        SetTile("GroundStones01.terrainlayer", 16f);
        SetTile("GroundStones02.terrainlayer", 17f);
        SetTile("MossStones.terrainlayer", 18f);
        SetTile("SoilGravel01.terrainlayer", 7f);
        SetTile("SoilGravel02.terrainlayer", 8f);
        SetTile("SoilGravel03.terrainlayer", 6.5f);
        SetTile("SoilGravel04.terrainlayer", 7.5f);
        SetTile("SoilMoss.terrainlayer", 12f);
        SetTile("SandySoil.terrainlayer", 6.5f);
        SetTile("Ground01.terrainlayer", 9f);
        SetTile("Ground02.terrainlayer", 11f);
        SetTile("Dry01.terrainlayer", 10f);
        SetTile("Dry02.terrainlayer", 11f);
        SetTile("Dry03.terrainlayer", 10f);
        SetTile("Clay.terrainlayer", 8f);
    }

    static void SetTile(string file, float size)
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + "/" + file);
        if (layer == null)
            return;
        layer.tileSize = new Vector2(size, size);
        EditorUtility.SetDirty(layer);
    }

    static float FindWaterY()
    {
        var waters = Object.FindObjectsByType<BoatWater>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (waters != null && waters.Length > 0 && waters[0] != null)
            return waters[0].transform.position.y;
        return 0f;
    }

    static Vector2[] CollectRiver()
    {
        var list = new List<Vector2>(32);
        var paths = Object.FindObjectsByType<BoatCurrentPath>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (paths == null)
            return list.ToArray();
        for (int p = 0; p < paths.Length; p++)
        {
            var path = paths[p];
            if (path == null)
                continue;
            for (int i = 0; i < path.transform.childCount; i++)
            {
                var c = path.transform.GetChild(i);
                if (c == null)
                    continue;
                Vector3 pos = c.position;
                list.Add(new Vector2(pos.x, pos.z));
            }
        }
        return list.ToArray();
    }

    static float RiverDist(float x, float z, Vector2[] pts)
    {
        if (pts == null || pts.Length < 2)
            return 9999f;
        Vector2 p = new Vector2(x, z);
        float best = 9999f;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[i + 1];
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.01f)
                continue;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            float d = Vector2.Distance(p, a + ab * t);
            if (d < best)
                best = d;
        }
        return best;
    }

    static float Noise(float x, float y, float seed)
    {
        return Mathf.PerlinNoise(x + seed, y + seed * 0.73f);
    }

    static float Smooth(float from, float to, float v)
    {
        float t = Mathf.InverseLerp(from, to, v);
        return t * t * (3f - 2f * t);
    }
}
#endif
