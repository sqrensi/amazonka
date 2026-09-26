using UnityEngine;

/// <summary>
/// Камень со склона: катится в дом и ломает швы при ударе.
/// </summary>
public class HouseRock : MonoBehaviour
{
    float _born;
    bool _spent;
    Vector3 _home;
    Rigidbody _rb;

    public void SteerTo(Vector3 house)
    {
        _home = house;
    }

    public static void NotifyHouseHit(BoatPiece piece, Collision collision)
    {
        if (piece == null || collision == null || !HouseBuildMode.Holding)
            return;
        if (collision.collider == null || collision.collider.GetComponentInParent<HouseRock>() == null)
            return;
        float speed = collision.relativeVelocity.magnitude;
        if (speed < 6.5f)
            return;
        float hit = Mathf.InverseLerp(6.5f, 22f, speed);
        if (HouseBuildMode.Current != null)
            HouseBuildMode.Current.Hurt(0.0025f + hit * 0.008f);
        Vector3 at = collision.contactCount > 0 ? collision.GetContact(0).point : piece.transform.position;
        HouseQuakeDust.Burst(at);
    }

    public static HouseRock Spawn(Vector3 pos, Vector3 vel, float size)
    {
        size = Mathf.Clamp(size, 0.28f, 0.72f);
        var go = new GameObject("HillRock");
        go.transform.position = pos;
        BuildRockMesh(go.transform, size);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = Mathf.Lerp(4.5f, 14f, Mathf.InverseLerp(0.28f, 0.72f, size));
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.linearDamping = 0.55f;
        rb.angularDamping = 0.45f;
        rb.linearVelocity = vel;
        rb.angularVelocity = Random.insideUnitSphere * 2.2f;
        var rock = go.AddComponent<HouseRock>();
        rock._born = Time.time;
        rock._home = pos + vel.normalized * 20f;
        rock._rb = rb;
        return rock;
    }

    static void BuildRockMesh(Transform root, float size)
    {
        Material mat = RockMat();
        int kind = Random.Range(0, 4);
        int bits = kind == 3 ? Random.Range(3, 5) : 1;
        for (int i = 0; i < bits; i++)
        {
            PrimitiveType prim = kind == 0 ? PrimitiveType.Sphere
                : kind == 1 ? PrimitiveType.Cube
                : kind == 2 ? PrimitiveType.Capsule
                : (i == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
            var part = GameObject.CreatePrimitive(prim);
            part.name = "RockBit";
            part.transform.SetParent(root, false);
            part.transform.localRotation = Random.rotation;
            if (kind == 0)
                part.transform.localScale = new Vector3(
                    size * Random.Range(0.75f, 1.2f),
                    size * Random.Range(0.55f, 0.95f),
                    size * Random.Range(0.7f, 1.15f));
            else if (kind == 1)
                part.transform.localScale = new Vector3(
                    size * Random.Range(0.55f, 0.95f),
                    size * Random.Range(0.4f, 0.75f),
                    size * Random.Range(0.6f, 1.05f));
            else if (kind == 2)
            {
                part.transform.localScale = new Vector3(
                    size * Random.Range(0.45f, 0.7f),
                    size * Random.Range(0.55f, 0.95f),
                    size * Random.Range(0.45f, 0.7f));
                part.transform.localRotation = Quaternion.Euler(Random.Range(60f, 120f), Random.Range(0f, 360f), 0f);
            }
            else
            {
                part.transform.localPosition = i == 0 ? Vector3.zero : Random.insideUnitSphere * (size * 0.28f);
                part.transform.localScale = Vector3.one * (size * Random.Range(0.35f, 0.7f));
            }
            var rend = part.GetComponent<Renderer>();
            if (rend != null)
                rend.sharedMaterial = mat;
            var col = part.GetComponent<Collider>();
            if (col != null)
                col.material = Phys();
        }
    }

    static Material RockMat()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        Texture tex = RockTexture();
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
        }
        Color[] tints =
        {
            new Color(0.62f, 0.55f, 0.45f),
            new Color(0.45f, 0.4f, 0.34f),
            new Color(0.38f, 0.32f, 0.26f),
            new Color(0.52f, 0.42f, 0.3f),
            new Color(0.33f, 0.34f, 0.3f),
            new Color(0.48f, 0.36f, 0.28f)
        };
        Color tint = tints[Random.Range(0, tints.Length)] * Random.Range(0.85f, 1.12f);
        tint.a = 1f;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", tint);
        mat.color = tint;
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.08f);
        return mat;
    }

    static Texture RockTexture()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            var layers = terrain.terrainData.terrainLayers;
            Texture best = null;
            int score = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null || layers[i].diffuseTexture == null)
                    continue;
                string n = layers[i].name.ToLowerInvariant();
                int s = 0;
                if (n.Contains("rock") || n.Contains("stone") || n.Contains("gravel"))
                    s = 3;
                else if (n.Contains("dirt") || n.Contains("soil") || n.Contains("clay") || n.Contains("dry"))
                    s = 2;
                else if (n.Contains("ground") || n.Contains("sand"))
                    s = 1;
                if (s > score)
                {
                    score = s;
                    best = layers[i].diffuseTexture;
                }
            }
            if (best != null)
                return best;
            if (layers.Length > 0 && layers[0] != null)
                return layers[0].diffuseTexture;
        }
#if UNITY_EDITOR
        string[] paths =
        {
            "Assets/MAIN/TerrainLayers/AdgRock.terrainlayer",
            "Assets/MAIN/TerrainLayers/GroundStones01.terrainlayer",
            "Assets/MAIN/TerrainLayers/PaintDirt.terrainlayer"
        };
        var layer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(paths[Random.Range(0, paths.Length)]);
        if (layer != null)
            return layer.diffuseTexture;
#endif
        return null;
    }

    static PhysicsMaterial _phys;

    static PhysicsMaterial Phys()
    {
        if (_phys != null)
            return _phys;
        _phys = new PhysicsMaterial("HillRock");
        _phys.dynamicFriction = 0.62f;
        _phys.staticFriction = 0.7f;
        _phys.bounciness = 0.04f;
        _phys.frictionCombine = PhysicsMaterialCombine.Average;
        _phys.bounceCombine = PhysicsMaterialCombine.Minimum;
        return _phys;
    }

    void FixedUpdate()
    {
        if (Time.time - _born > 22f || transform.position.y < -30f)
        {
            Destroy(gameObject);
            return;
        }
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        if (_rb == null || Time.time - _born > 6f)
            return;
        Vector3 home = HouseBuildMode.Current != null ? HouseBuildMode.Current.HoldOrigin : _home;
        Vector3 to = home - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1f)
            return;
        _rb.AddForce(to.normalized * 7f + Vector3.down * 6f, ForceMode.Acceleration);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_spent || collision == null)
            return;
        var piece = collision.collider != null
            ? collision.collider.GetComponentInParent<BoatPiece>()
            : null;
        if (piece == null)
            return;
        _spent = true;
        NotifyHouseHit(piece, collision);
        if (_rb != null)
        {
            _rb.linearVelocity *= 0.22f;
            _rb.angularVelocity *= 0.3f;
        }
    }
}

public class HouseRockSpawner : MonoBehaviour
{
    public Vector3 Aim = Vector3.forward;

    public void Drop(Vector3 house)
    {
        Vector3 to = house - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f)
            to = Aim.sqrMagnitude > 0.01f ? Aim : Vector3.forward;
        to.Normalize();
        Vector3 pos = transform.position + Vector3.up * 0.7f + Random.insideUnitSphere * 0.6f;
        Vector3 vel = to * Random.Range(3.2f, 6.4f) + Vector3.down * Random.Range(1.2f, 3.2f);
        vel += Vector3.Cross(Vector3.up, to) * Random.Range(-0.6f, 0.6f);
        var rock = HouseRock.Spawn(pos, vel, Random.Range(0.32f, 0.62f));
        if (rock != null)
            rock.SteerTo(house);
    }
}
