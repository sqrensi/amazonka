using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Водная поверхность (шейдер IgniteCoders) + объём для плавучести и плавания.
/// </summary>
public class BoatWater : MonoBehaviour
{
    static readonly List<BoatWater> All = new List<BoatWater>();

    [SerializeField] float surfaceY;
    BoxCollider _volume;
    GameObject _underside;

    public float SurfaceY => surfaceY;

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
        _volume = GetComponent<BoxCollider>();
        if (Mathf.Abs(surfaceY) < 0.0001f)
            surfaceY = transform.position.y + MeshTopLocal();
        EnsureUnderside();
    }

    void EnsureUnderside()
    {
        if (_underside != null)
            return;
        var filter = GetComponent<MeshFilter>();
        var rend = GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || rend == null)
            return;
        _underside = new GameObject("Underside");
        _underside.transform.SetParent(transform, false);
        _underside.transform.localPosition = Vector3.zero;
        _underside.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        _underside.transform.localScale = Vector3.one;
        _underside.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var underRend = _underside.AddComponent<MeshRenderer>();
        underRend.sharedMaterial = rend.sharedMaterial;
        underRend.shadowCastingMode = ShadowCastingMode.Off;
        underRend.receiveShadows = false;
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    float MeshTopLocal()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
            return filter.sharedMesh.bounds.max.y;
        return 0f;
    }

    public bool ContainsXZ(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        if (_volume != null)
        {
            Vector3 half = _volume.size * 0.5f;
            Vector3 c = _volume.center;
            return Mathf.Abs(local.x - c.x) <= half.x && Mathf.Abs(local.z - c.z) <= half.z;
        }
        var rend = GetComponent<Renderer>();
        if (rend == null)
            return false;
        Bounds b = rend.bounds;
        return world.x >= b.min.x && world.x <= b.max.x && world.z >= b.min.z && world.z <= b.max.z;
    }

    public static bool TryHeight(Vector3 world, out float y)
    {
        BoatWater best = null;
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w != null && w.isActiveAndEnabled && w.ContainsXZ(world))
            {
                best = w;
                break;
            }
        }
        if (best == null)
        {
            y = 0f;
            return false;
        }
        y = best.surfaceY;
        return true;
    }

    public static bool IsUnder(Vector3 world, out float depth)
    {
        if (!TryHeight(world, out float y))
        {
            depth = 0f;
            return false;
        }
        depth = y - world.y;
        return depth > 0.02f;
    }

    public static bool RaycastSurface(Ray ray, float dist, out Vector3 point, out float surfaceY)
    {
        point = default;
        surfaceY = 0f;
        float best = dist + 1f;
        bool any = false;
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w == null || !w.isActiveAndEnabled)
                continue;
            if (Mathf.Abs(ray.direction.y) < 0.0001f)
                continue;
            float t = (w.surfaceY - ray.origin.y) / ray.direction.y;
            if (t < 0.05f || t > dist)
                continue;
            Vector3 p = ray.GetPoint(t);
            if (!w.ContainsXZ(p))
                continue;
            if (t < best)
            {
                best = t;
                point = p;
                surfaceY = w.surfaceY;
                any = true;
            }
        }
        return any;
    }
}
