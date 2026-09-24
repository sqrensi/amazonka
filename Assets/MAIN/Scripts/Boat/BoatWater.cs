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
    [SerializeField] Vector3 localFlow = Vector3.forward;
    [SerializeField] float flowSpeed = 4.5f;

    BoxCollider _volume;
    GameObject _underside;

    public float SurfaceY => surfaceY;
    public float FlowSpeed => flowSpeed;
    public static bool CurrentEnabled { get; set; } = true;

    public void EnsureSurface()
    {
        if (Mathf.Abs(surfaceY) < 0.0001f)
            surfaceY = transform.position.y + MeshTopLocal();
    }

    public float HeightWorld(Vector3 world)
    {
        Vector3 n = transform.up;
        Vector3 p = transform.TransformPoint(new Vector3(0f, MeshTopLocal(), 0f));
        if (Mathf.Abs(n.y) < 0.0001f)
            return p.y;
        return p.y - (n.x * (world.x - p.x) + n.z * (world.z - p.z)) / n.y;
    }

    public void TiltAlong(Vector3 from, Vector3 to, float degrees)
    {
        EnsureSurface();
        Vector3 along = to - from;
        along.y = 0f;
        if (along.sqrMagnitude < 0.01f)
            return;
        float keep = HeightWorld(from);
        transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up)
            * Quaternion.Euler(Mathf.Clamp(degrees, 0.2f, 6f), 0f, 0f);
        localFlow = Vector3.forward;
        transform.position += Vector3.up * (keep - HeightWorld(from));
        surfaceY = HeightWorld(transform.position);
    }

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
        _volume = GetComponent<BoxCollider>();
        var cols = GetComponents<Collider>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i] is not MeshCollider)
                cols[i].isTrigger = true;
        }
        EnsureSurface();
        var rend = GetComponent<MeshRenderer>();
        if (rend != null)
        {
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = true;
            rend.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            var ignite = Resources.Load<Material>("Water_mat_01");
            if (ignite != null)
                rend.sharedMaterial = ignite;
        }
    }

    void LateUpdate()
    {
        UpdateUnderside();
        var cam = Camera.main;
        if (cam != null)
            BoatWaterSkin.TickAround(cam.transform.position);
    }

    void UpdateUnderside()
    {
        var cam = Camera.main;
        bool under = cam != null && cam.transform.position.y < HeightWorld(cam.transform.position) - 0.04f;
        if (!under)
        {
            if (_underside != null)
                _underside.SetActive(false);
            return;
        }
        EnsureUnderside();
        if (_underside != null)
            _underside.SetActive(true);
    }

    void EnsureUnderside()
    {
        if (_underside != null)
            return;
        var filter = GetComponent<MeshFilter>();
        var rend = GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || rend == null)
            return;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        _underside = new GameObject("Underside");
        _underside.transform.SetParent(transform, false);
        _underside.transform.localPosition = new Vector3(0f, -0.1f, 0f);
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
        if (_volume != null && _volume.enabled)
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
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w == null || !w.isActiveAndEnabled || !w.ContainsXZ(world))
                continue;
            y = w.HeightWorld(world);
            return true;
        }
        y = 0f;
        return false;
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
        return ApplyBuoyancy(rb, t, localCenter, localSize, buoyancy, -1f, true, 1f);
    }

    public static float ApplyBuoyancy(Rigidbody rb, Transform t, Vector3 localCenter, Vector3 localSize, float buoyancy, float mass, bool damp)
    {
        return ApplyBuoyancy(rb, t, localCenter, localSize, buoyancy, mass, damp, 1f);
    }

    public static float ApplyBuoyancy(Rigidbody rb, Transform t, Vector3 localCenter, Vector3 localSize, float buoyancy, float mass, bool damp, float drift)
    {
        if (rb == null || t == null)
            return 0f;
        float m = mass > 0.01f ? mass : rb.mass;
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
            float lift = m * buoyancy * d / n;
            rb.AddForceAtPosition(Vector3.up * lift + WaveDrift(world) * (m * 0.4f * drift * d / n), world, ForceMode.Force);
        }
        if (wet <= 0)
            return 0f;
        float frac = submerged / n;
        if (damp)
            DampBuoyancy(rb, t, frac, m, drift);
        return frac;
    }

    public static void DampBuoyancy(Rigidbody rb, Transform t, float frac, float mass, float currentFollow = 1f)
    {
        if (rb == null || frac <= 0.001f)
            return;
        float m = mass > 0.01f ? mass : rb.mass;
        float follow = Mathf.Clamp01(currentFollow);
        Vector3 vel = rb.linearVelocity;
        Vector3 current = CurrentAt(rb.worldCenterOfMass) * follow;
        current.y = 0f;
        Vector3 planar = vel;
        planar.y = 0f;
        Vector3 along = current.sqrMagnitude > 0.01f ? current.normalized : Vector3.zero;
        Vector3 vAlong = along.sqrMagnitude > 0.01f ? along * Vector3.Dot(planar, along) : Vector3.zero;
        Vector3 vLat = planar - vAlong;
        Vector3 drag = (vAlong - current) + vLat * (follow < 0.25f ? 0.55f : 0.18f);
        drag.y = vel.y * (follow < 0.25f ? 0.18f : 0.45f);
        rb.AddForce(-drag * ((follow < 0.25f ? 0.38f : 0.62f) * frac * m), ForceMode.Force);
        rb.AddTorque(-rb.angularVelocity * ((follow < 0.25f ? 1.1f : 2.6f) * frac), ForceMode.Acceleration);
        if (follow > 0.35f)
        {
            Vector3 straighten = Vector3.Cross(t != null ? t.up : rb.transform.up, Vector3.up);
            rb.AddTorque(straighten * (1.35f * frac), ForceMode.Acceleration);
        }
        rb.angularDamping = Mathf.Lerp(0.8f, follow < 0.25f ? 1.2f : 2.35f, Mathf.Clamp01(frac));
        if (vel.y > 2.4f)
            rb.AddForce(Vector3.down * ((vel.y - 2.4f) * m * 2.4f), ForceMode.Force);
    }

    public static void DriftWithCurrent(Rigidbody rb, float frac, float scale = 1f)
    {
        if (rb == null || frac < 0.04f || scale < 0.01f)
            return;
        Vector3 current = CurrentAt(rb.worldCenterOfMass);
        current.y = 0f;
        if (current.sqrMagnitude < 0.01f)
            return;
        Vector3 planar = rb.linearVelocity;
        planar.y = 0f;
        Vector3 along = current.normalized;
        Vector3 haveAlong = along * Vector3.Dot(planar, along);
        Vector3 delta = current * scale - haveAlong;
        delta.y = 0f;
        float cap = Mathf.Lerp(0.8f, 3.2f, Mathf.Clamp01(scale));
        rb.AddForce(Vector3.ClampMagnitude(delta * (0.7f + 0.45f * scale), cap), ForceMode.Acceleration);
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
            Vector3 n = w.transform.up;
            float den = Vector3.Dot(n, ray.direction);
            if (Mathf.Abs(den) < 0.0001f)
                continue;
            Vector3 p0 = w.transform.TransformPoint(new Vector3(0f, w.MeshTopLocal(), 0f));
            float t = Vector3.Dot(p0 - ray.origin, n) / den;
            if (t < 0.05f || t > dist)
                continue;
            Vector3 p = ray.GetPoint(t);
            if (!w.ContainsXZ(p))
                continue;
            if (t < best)
            {
                best = t;
                point = p;
                surfaceY = p.y;
                any = true;
            }
        }
        return any;
    }

    static readonly RaycastHit[] BankHits = new RaycastHit[8];

    static Vector3 DeflectFromBanks(Vector3 world, Vector3 flow)
    {
        flow.y = 0f;
        if (flow.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        float speed = flow.magnitude;
        Vector3 dir = flow / speed;
        Vector3 origin = world + Vector3.up * 0.35f;
        int mask = ~0;
        int water = LayerMask.NameToLayer("Water");
        if (water >= 0)
            mask &= ~(1 << water);
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (ignore >= 0)
            mask &= ~(1 << ignore);
        float[] ang = { 0f, -22f, 22f, -48f, 48f };
        Vector3 acc = flow;
        for (int i = 0; i < ang.Length; i++)
        {
            Vector3 probe = Quaternion.AngleAxis(ang[i], Vector3.up) * dir;
            int n = Physics.SphereCastNonAlloc(origin, 0.42f, probe, BankHits, 3.4f, mask, QueryTriggerInteraction.Ignore);
            float best = 3.4f;
            Vector3 nrm = Vector3.zero;
            bool hit = false;
            for (int h = 0; h < n; h++)
            {
                var col = BankHits[h].collider;
                if (col == null || col.isTrigger)
                    continue;
                if (col.GetComponentInParent<BoatWater>() != null)
                    continue;
                if (col.GetComponentInParent<BoatPiece>() != null)
                    continue;
                if (col.GetComponentInParent<CharacterController>() != null)
                    continue;
                if (col.GetComponentInParent<RiverShark>() != null)
                    continue;
                if (BankHits[h].distance < 0.04f)
                    continue;
                Vector3 nn = BankHits[h].normal;
                nn.y = 0f;
                if (nn.sqrMagnitude < 0.05f)
                    continue;
                if (BankHits[h].distance < best)
                {
                    best = BankHits[h].distance;
                    nrm = nn;
                    hit = true;
                }
            }
            if (!hit)
                continue;
            nrm.Normalize();
            if (Vector3.Dot(acc, nrm) < 0f)
                acc = Vector3.ProjectOnPlane(acc, nrm);
        }
        acc.y = 0f;
        if (acc.sqrMagnitude < 0.04f)
            return dir * speed;
        Vector3 outDir = acc.normalized;
        if (Vector3.Dot(outDir, dir) < 0.28f)
            return dir * speed;
        return outDir * speed;
    }

    public Vector3 FlowWorld
    {
        get
        {
            if (!CurrentEnabled || flowSpeed <= 0.01f)
                return Vector3.zero;
            Vector3 d = transform.TransformDirection(localFlow);
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f)
                return Vector3.zero;
            return d.normalized * flowSpeed;
        }
    }

    public static Vector3 CurrentAt(Vector3 world)
    {
        if (!CurrentEnabled)
            return Vector3.zero;
        bool wet = false;
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w != null && w.isActiveAndEnabled && w.ContainsXZ(world))
            {
                wet = true;
                break;
            }
        }
        if (!wet)
            return Vector3.zero;
        Vector3 path = BoatCurrentPath.FlowAt(world);
        if (path.sqrMagnitude > 0.0001f)
            return DeflectFromBanks(world, path);
        for (int i = 0; i < All.Count; i++)
        {
            var w = All[i];
            if (w != null && w.isActiveAndEnabled && w.ContainsXZ(world))
                return w.FlowWorld;
        }
        return Vector3.zero;
    }
}
