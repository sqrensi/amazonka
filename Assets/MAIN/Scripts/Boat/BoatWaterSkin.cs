using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Локальный волнистый слой у геометрии (лодка): не двигает физическую воду.
/// </summary>
public class BoatWaterSkin : MonoBehaviour
{
    static BoatWaterSkin _inst;
    MeshRenderer _rend;

    public static void TickAround(Vector3 world)
    {
        if (_inst == null)
            _inst = Create();
        if (_inst == null)
            return;
        if (!BoatWater.TryHeight(world, out float y))
        {
            _inst._rend.enabled = false;
            return;
        }
        _inst._rend.enabled = true;
        _inst.transform.SetPositionAndRotation(
            new Vector3(world.x, y + 0.02f, world.z),
            Quaternion.identity);
    }

    static BoatWaterSkin Create()
    {
        var go = new GameObject("BoatWaterSkin");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = Grid(16, 11f);
        var rend = go.AddComponent<MeshRenderer>();
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.lightProbeUsage = LightProbeUsage.Off;
        rend.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        var shader = Shader.Find("MAIN/BoatWaterSoft");
        if (shader != null)
            rend.sharedMaterial = new Material(shader);
        RaceMood.PaintWater(rend);
        var skin = go.AddComponent<BoatWaterSkin>();
        skin._rend = rend;
        return skin;
    }

    static Mesh Grid(int seg, float size)
    {
        int n = seg + 1;
        var verts = new Vector3[n * n];
        var uv = new Vector2[n * n];
        var tris = new int[seg * seg * 6];
        float h = size * 0.5f;
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)seg;
                float v = z / (float)seg;
                int i = z * n + x;
                verts[i] = new Vector3(Mathf.Lerp(-h, h, u), 0f, Mathf.Lerp(-h, h, v));
                uv[i] = new Vector2(u, v);
            }
        }
        int t = 0;
        for (int z = 0; z < seg; z++)
        {
            for (int x = 0; x < seg; x++)
            {
                int i = z * n + x;
                tris[t++] = i;
                tris[t++] = i + n;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + n;
                tris[t++] = i + n + 1;
            }
        }
        var mesh = new Mesh { name = "WaterSkinGrid" };
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
