#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

static class TerrainLayerBuilder
{
    const string TexDir = "Assets/MAIN/textures";
    const string LayerDir = "Assets/MAIN/TerrainLayers";

    [MenuItem("Horror/Create Terrain Layers")]
    static void Create()
    {
        if (!AssetDatabase.IsValidFolder(LayerDir))
            AssetDatabase.CreateFolder("Assets/MAIN", "TerrainLayers");

        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.EndsWith("_d.tga", System.StringComparison.OrdinalIgnoreCase))
                continue;
            string stem = path.Substring(0, path.Length - 6);
            string name = Path.GetFileName(stem);
            if (name.StartsWith("T_YFGM_"))
                name = name.Substring(7);

            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (diffuse == null)
                continue;
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + "_n.tga");
            var layer = new TerrainLayer();
            layer.diffuseTexture = diffuse;
            layer.normalMapTexture = normal;
            layer.tileSize = TileFor(name);
            layer.smoothness = 0.12f;
            layer.metallic = 0f;
            string outPath = LayerDir + "/" + name + ".terrainlayer";
            AssetDatabase.CreateAsset(layer, outPath);
            n++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Terrain Layers", "Created " + n + " layers in " + LayerDir, "OK");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(LayerDir));
    }

    static Vector2 TileFor(string name)
    {
        if (name.StartsWith("GrassLeafs"))
            return new Vector2(9f, 9f);
        if (name.StartsWith("Grass"))
            return new Vector2(12f, 12f);
        if (name.Contains("Stones") || name.Contains("MossStones"))
            return new Vector2(16f, 16f);
        if (name.StartsWith("SoilGravel"))
            return new Vector2(7f, 7f);
        if (name.Contains("Sandy") || name == "Clay")
            return new Vector2(7f, 7f);
        if (name == "SoilMoss")
            return new Vector2(12f, 12f);
        return new Vector2(10f, 10f);
    }
}
#endif
