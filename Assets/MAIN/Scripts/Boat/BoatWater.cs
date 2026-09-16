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
        var cols = GetComponents<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].isTrigger = true;
        }
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

    public static bool HeightAt(Vector3 world, out float y)
    {
        if (!TryHeight(world, out y))
            return false;
        y += Wave(world);
        return true;
    }

    public static float Wave(Vector3 world)
    {
        float t = Time.time;
        float x = world.x;
        float z = world.z;
        return 0.07f * Mathf.Sin(x * 0.41f + t * 1.05f)
             + 0.05f * Mathf.Sin(z * 0.33f + t * 0.82f + 0.9f)
             + 0.03f * Mathf.Sin(x * 0.85f + z * 0.72f + t * 1.65f)
             + 0.016f * Mathf.Sin(x * 1.55f - z * 1.05f + t * 2.35f);
    }

    public static Vector3 WaveDrift(Vector3 world)
    {
        const float e = 0.35f;
        float hx = (Wave(world + Vector3.right * e) - Wave(world - Vector3.right * e)) / (2f * e);
        float hz = (Wave(world + Vector3.forward * e) - Wave(world - Vector3.forward * e)) / (2f * e);
        return new Vector3(-hx, 0f, -hz) * 7f;
    }

    public static float ApplyBuoyancy(Rigidbody rb, Transform t, Vector3 localCenter, Vector3 localSize, float buoyancy)
    {
        if (rb == null || t == null)
            return 0f;
        float hx = Mathf.Max(0.05f, localSize.x * 0.48f);
        float hy = Mathf.Max(0.03f, localSize.y * 0.5f);
        float hz = Mathf.Max(0.05f, localSize.z * 0.48f);
        Vector3[] locals =
        {
            localCenter + new Vector3(-hx, -hy * 0.2f, -hz),
            localCenter + new Vector3(hx, -hy * 0.2f, -hz),
            localCenter + new Vector3(-hx, -hy * 0.2f, hz),
            localCenter + new Vector3(hx, -hy * 0.2f, hz),
            localCenter + new Vector3(0f, -hy * 0.2f, 0f)
        };
        int n = locals.Length;
        int wet = 0;
        float submerged = 0f;
        float thick = Mathf.Max(0.1f, localSize.y);
        for (int i = 0; i < n; i++)
        {
            Vector3 world = t.TransformPoint(locals[i]);
            if (!HeightAt(world, out float waterY))
                continue;
            float depth = waterY - world.y;
            if (depth <= 0f)
                continue;
            float d = Mathf.Clamp01(depth / thick);
            wet++;
            submerged += d;
            float lift = rb.mass * buoyancy * d / n;
            rb.AddForceAtPosition(Vector3.up * lift + WaveDrift(world) * (rb.mass * 0.4f * d / n), world, ForceMode.Force);
        }
        if (wet <= 0)
            return 0f;
        float frac = submerged / n;
        Vector3 vel = rb.linearVelocity;
        Vector3 drag = vel;
        drag.y *= 0.45f;
        rb.AddForce(-drag * (0.72f * frac * rb.mass), ForceMode.Force);
        rb.AddTorque(-rb.angularVelocity * (2.6f * frac), ForceMode.Acceleration);
        Vector3 straighten = Vector3.Cross(t.up, Vector3.up);
        rb.AddTorque(straighten * (2.4f * frac), ForceMode.Acceleration);
        rb.angularDamping = Mathf.Lerp(0.8f, 2.35f, Mathf.Clamp01(frac));
        if (vel.y > 2.4f)
            rb.AddForce(Vector3.down * ((vel.y - 2.4f) * rb.mass * 2.4f), ForceMode.Force);
        return frac;
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
