using UnityEngine;

/// <summary>
/// Физический камень: бьёт лодку и акул, тонет, потом удаляется.
/// </summary>
public class FallingRock : MonoBehaviour
{
    Rigidbody _rb;
    SphereCollider _col;
    bool _splashed;
    bool _settled;
    float _still;
    float _born;
    float _hitBoatAt;
    float _hitSharkAt;
    float _killAt = -1f;

    const int MaxLive = 22;
    static int Live;
    static float _actorsAt;
    static CharacterController[] _actors;

    public static bool CanSpawn => Live < MaxLive;

    public static FallingRock Spawn(Vector3 pos, Vector3 vel)
    {
        if (!CanSpawn)
            return null;
        var go = new GameObject("FallingRock");
        go.transform.SetPositionAndRotation(pos, Random.rotation);
        var rock = go.AddComponent<FallingRock>();
        rock.Build();
        rock._rb.linearVelocity = vel;
        rock._rb.angularVelocity = Random.insideUnitSphere * 7f;
        return rock;
    }

    void Build()
    {
        Live++;
        _born = Time.time;
        float size = Random.Range(0.95f, 1.75f);
        var vis = new GameObject("Vis");
        vis.transform.SetParent(transform, false);
        vis.transform.localScale = new Vector3(
            Random.Range(0.72f, 1.28f),
            Random.Range(0.55f, 1.15f),
            Random.Range(0.72f, 1.28f)) * size;
        var mf = vis.AddComponent<MeshFilter>();
        mf.sharedMesh = RockMesh.Pick();
        var mr = vis.AddComponent<MeshRenderer>();
        mr.sharedMaterial = RockLook.Material();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        _col = gameObject.AddComponent<SphereCollider>();
        _col.radius = size * 0.42f;
        _col.material = RockPhys();

        _rb = gameObject.AddComponent<Rigidbody>();
        _rb.mass = Mathf.Clamp(22f * size * size * size, 28f, 140f);
        _rb.linearDamping = 0f;
        _rb.angularDamping = 0.04f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _rb.useGravity = true;
        IgnoreActors();
    }

    void OnDestroy()
    {
        Live = Mathf.Max(0, Live - 1);
    }

    void IgnoreActors()
    {
        if (Time.time >= _actorsAt)
        {
            _actorsAt = Time.time + 1.2f;
            _actors = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
        if (_actors == null || _col == null)
            return;
        for (int i = 0; i < _actors.Length; i++)
        {
            if (_actors[i] != null)
                Physics.IgnoreCollision(_col, _actors[i], true);
        }
    }

    void FixedUpdate()
    {
        if (_rb == null || _settled)
            return;
        if (Time.time > _born + 45f)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 p = _rb.worldCenterOfMass;
        bool water = BoatWater.TryHeight(p, out float waterY);
        if (!water || p.y > waterY + 0.2f)
            _rb.AddForce(Vector3.down * 42f, ForceMode.Acceleration);
        else
        {
            if (!_splashed)
            {
                _splashed = true;
                Vector3 v = _rb.linearVelocity;
                v.y = 0f;
                BoatWaterFx.Splash(new Vector3(p.x, waterY, p.z), v.sqrMagnitude > 0.2f ? v : Vector3.forward, 1.1f);
            }
            float depth = Mathf.Clamp(waterY - p.y, 0f, 6f);
            _rb.linearDamping = Mathf.Lerp(0.2f, 1.6f, Mathf.Clamp01(depth * 0.4f));
            _rb.angularDamping = 1.1f;
            _rb.AddForce(Vector3.down * (18f + depth * 5f), ForceMode.Acceleration);
        }

        float speed = _rb.linearVelocity.magnitude;
        if (speed < 0.35f)
            _still += Time.fixedDeltaTime;
        else
            _still = 0f;

        if (_still > 1.4f && water && p.y < waterY - 0.25f)
            RestOnBed();
        else if (_still > 2.2f)
            RestOnBed();
    }

    void RestOnBed()
    {
        if (_settled)
            return;
        _settled = true;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
        _killAt = Time.time + Random.Range(3.2f, 5.5f);
    }

    void Update()
    {
        if (_settled && _killAt > 0f && Time.time >= _killAt)
            Destroy(gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;
        if (collision.collider.GetComponentInParent<BoatWater>() != null)
            return;

        float speed = collision.relativeVelocity.magnitude;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 dir = collision.relativeVelocity.sqrMagnitude > 0.01f
            ? collision.relativeVelocity.normalized
            : Vector3.down;

        var shark = collision.collider.GetComponentInParent<RiverShark>();
        if (shark != null && !shark.IsDead && speed > 3.5f && Time.time >= _hitSharkAt)
        {
            _hitSharkAt = Time.time + 0.25f;
            float dmg = Mathf.Clamp(10f + speed * 1.8f * (_rb != null ? _rb.mass * 0.04f : 1f), 12f, 55f);
            RaceSim.RequestDamage(0, shark, dmg, point, dir, "Rock");
        }

        var piece = collision.collider.GetComponentInParent<BoatPiece>();
        if (piece != null && speed > 7.5f && Time.time >= _hitBoatAt)
        {
            _hitBoatAt = Time.time + 0.35f;
            if (speed > 12f && Random.value < 0.35f)
                BoatHull.SharkBite(piece, point, 0.12f);
        }
    }

    static PhysicsMaterial _phys;
    static PhysicsMaterial RockPhys()
    {
        if (_phys != null)
            return _phys;
        _phys = new PhysicsMaterial("Rock")
        {
            dynamicFriction = 0.62f,
            staticFriction = 0.72f,
            bounciness = 0.08f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        return _phys;
    }
}

static class RockMesh
{
    static Mesh[] _pool;

    public static Mesh Pick()
    {
        if (_pool == null)
        {
            _pool = new Mesh[5];
            for (int i = 0; i < _pool.Length; i++)
                _pool[i] = Build(i * 17 + 3);
        }
        return _pool[Random.Range(0, _pool.Length)];
    }

    static Mesh Build(int seed)
    {
        var rng = new System.Random(seed);
        float t = 1.618034f;
        var raw = new Vector3[]
        {
            new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f), new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
            new Vector3(0f, -1f, t), new Vector3(0f, 1f, t), new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
            new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f)
        };
        for (int i = 0; i < raw.Length; i++)
        {
            Vector3 n = raw[i].normalized;
            float j = 0.62f + (float)rng.NextDouble() * 0.55f;
            raw[i] = Vector3.Scale(n * j, new Vector3(
                0.85f + (float)rng.NextDouble() * 0.4f,
                0.7f + (float)rng.NextDouble() * 0.45f,
                0.85f + (float)rng.NextDouble() * 0.4f));
        }
        int[] faces =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        int tri = faces.Length / 3;
        var verts = new Vector3[tri * 3];
        var norms = new Vector3[tri * 3];
        var uv = new Vector2[tri * 3];
        var tris = new int[tri * 3];
        for (int f = 0; f < tri; f++)
        {
            Vector3 a = raw[faces[f * 3]];
            Vector3 b = raw[faces[f * 3 + 1]];
            Vector3 c = raw[faces[f * 3 + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 0.0001f)
                n = Vector3.up;
            else
                n.Normalize();
            int i = f * 3;
            verts[i] = a;
            verts[i + 1] = b;
            verts[i + 2] = c;
            norms[i] = n;
            norms[i + 1] = n;
            norms[i + 2] = n;
            uv[i] = new Vector2(a.x, a.z);
            uv[i + 1] = new Vector2(b.x, b.z);
            uv[i + 2] = new Vector2(c.x, c.z);
            tris[i] = i;
            tris[i + 1] = i + 1;
            tris[i + 2] = i + 2;
        }
        var mesh = new Mesh { name = "RockFacet" };
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}

static class RockLook
{
    static Material _mat;

    public static Material Material()
    {
        if (_mat != null)
            return _mat;
        _mat = Resources.Load<Material>("Rockfall");
        if (_mat != null)
            return _mat;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        _mat = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
        _mat.name = "RockfallRuntime";
        if (_mat.HasProperty("_BaseColor"))
            _mat.SetColor("_BaseColor", new Color(0.42f, 0.38f, 0.34f, 1f));
        if (_mat.HasProperty("_Smoothness"))
            _mat.SetFloat("_Smoothness", 0.08f);
        if (_mat.HasProperty("_Metallic"))
            _mat.SetFloat("_Metallic", 0.04f);
#if UNITY_EDITOR
        var layer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/MAIN/TerrainLayers/AdgRock.terrainlayer");
        if (layer == null)
            layer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/MAIN/TerrainLayers/GroundStones01.terrainlayer");
        if (layer != null && layer.diffuseTexture != null)
        {
            if (_mat.HasProperty("_BaseMap"))
                _mat.SetTexture("_BaseMap", layer.diffuseTexture);
            if (layer.normalMapTexture != null && _mat.HasProperty("_BumpMap"))
            {
                _mat.SetTexture("_BumpMap", layer.normalMapTexture);
                _mat.EnableKeyword("_NORMALMAP");
                if (_mat.HasProperty("_BumpScale"))
                    _mat.SetFloat("_BumpScale", 1.15f);
            }
        }
#endif
        return _mat;
    }
}
