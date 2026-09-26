using UnityEngine;

/// <summary>
/// Птица заходит издалека, пикирует на дом и сбрасывает яйца-гранаты.
/// Модель: Assets/MAIN/Bird.
/// </summary>
public class HouseBombBird : MonoBehaviour, IDamageable
{
    const float Hp = 12f;
    Vector3 _start;
    Vector3 _passDir;
    Vector3 _house;
    float _t;
    float _dur;
    bool _dropped;
    float _dropAt;
    Vector3 _dropPos;
    bool _dead;
    float _hp = Hp;
    Vector3 _pos;
    Vector3 _vel;
    Quaternion _faceFix = Quaternion.identity;
    Rigidbody _rb;
    CapsuleCollider _col;
    float _yaw;
    float _pitch;
    float _roll;
    int _dropN = 2;
    Renderer[] _rends;
    Color[] _baseColors;
    float _flashUntil;

    public bool IsDead => _dead;

    public static HouseBombBird SpawnPass(Vector3 house)
    {
        var go = new GameObject("BombBird");
        var bird = go.AddComponent<HouseBombBird>();
        bird.Begin(house);
        return bird;
    }

    void Begin(Vector3 house)
    {
        _house = house;
        Vector2 ring = Random.insideUnitCircle.normalized;
        if (ring.sqrMagnitude < 0.01f)
            ring = Vector2.right;
        _start = house + new Vector3(ring.x, 0f, ring.y) * Random.Range(78f, 110f);
        _start.y = house.y + Random.Range(20f, 28f);
        _passDir = (house - _start);
        _passDir.y = 0f;
        if (_passDir.sqrMagnitude < 0.01f)
            _passDir = Vector3.forward;
        _passDir.Normalize();
        _yaw = Mathf.Atan2(_passDir.x, _passDir.z) * Mathf.Rad2Deg;
        _dur = Random.Range(12f, 16f);
        _dropN = Random.Range(1, 5);
        _pos = _start;
        transform.position = _start;
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        BuildVisual();
        _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _col = gameObject.AddComponent<CapsuleCollider>();
        _col.direction = 2;
        _col.height = 1.6f;
        _col.radius = 0.38f;
        _col.center = Vector3.zero;
    }

    void BuildVisual()
    {
        var prefab = LoadBirdPrefab();
        Transform vis = null;
        if (prefab != null)
        {
            var model = Object.Instantiate(prefab, transform);
            model.name = "BirdModel";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            StripPhysics(model);
            vis = model.transform;
            Bounds b = Encapsulate(vis);
            float span = Mathf.Max(b.size.x, b.size.z, 0.2f);
            float want = 1.55f;
            vis.localScale = vis.localScale * (want / span);
            vis.localPosition = -b.center * (want / span);
            if (b.size.x > b.size.z * 1.15f)
                _faceFix = Quaternion.Euler(0f, 90f, 0f);
            else
                _faceFix = Quaternion.identity;
        }
        else
        {
            vis = MakeFallback().transform;
            vis.SetParent(transform, false);
        }
        var anim = GetComponentInChildren<Animator>();
        if (anim != null)
            anim.speed = 1.15f;
        _rends = GetComponentsInChildren<Renderer>();
        _baseColors = new Color[_rends.Length];
        for (int i = 0; i < _rends.Length; i++)
        {
            if (_rends[i] == null)
                continue;
            _baseColors[i] = _rends[i].material.HasProperty("_BaseColor")
                ? _rends[i].material.GetColor("_BaseColor")
                : _rends[i].material.color;
        }
    }

    static GameObject LoadBirdPrefab()
    {
        var loaded = Resources.Load<GameObject>("Bird");
        if (loaded != null)
            return loaded;
#if UNITY_EDITOR
        string[] folders = { "Assets/MAIN/Bird", "Assets/MAIN/Birds" };
        for (int f = 0; f < folders.Length; f++)
        {
            if (!UnityEditor.AssetDatabase.IsValidFolder(folders[f]))
                continue;
            string[] filters = { "t:Prefab", "t:Model", "t:GameObject" };
            for (int k = 0; k < filters.Length; k++)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets(filters[k], new[] { folders[f] });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path) || path.EndsWith(".meta"))
                        continue;
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null)
                        return go;
                }
            }
        }
#endif
        return null;
    }

    static GameObject MakeFallback()
    {
        var root = new GameObject("BirdFallback");
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(0.28f, 0.42f, 0.28f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Object.Destroy(body.GetComponent<Collider>());
        Tint(body, new Color(0.18f, 0.16f, 0.14f));
        for (int i = 0; i < 2; i++)
        {
            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = "Wing";
            wing.transform.SetParent(root.transform, false);
            wing.transform.localScale = new Vector3(0.85f, 0.04f, 0.28f);
            wing.transform.localPosition = new Vector3(i == 0 ? -0.45f : 0.45f, 0.05f, 0.05f);
            Object.Destroy(wing.GetComponent<Collider>());
            Tint(wing, new Color(0.22f, 0.2f, 0.17f));
        }
        return root;
    }

    static void Tint(GameObject go, Color c)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null)
            return;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", c);
        m.color = c;
        r.sharedMaterial = m;
    }

    static void StripPhysics(GameObject root)
    {
        var rbs = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
            Object.Destroy(rbs[i]);
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            Object.Destroy(cols[i]);
    }

    static Bounds Encapsulate(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(root.position, Vector3.zero);
        bool any = false;
        for (int i = 0; i < rs.Length; i++)
        {
            if (rs[i] == null)
                continue;
            if (!any)
            {
                b = rs[i].bounds;
                any = true;
            }
            else
                b.Encapsulate(rs[i].bounds);
        }
        if (!any)
            b = new Bounds(root.position, Vector3.one);
        Vector3 local = root.InverseTransformPoint(b.center);
        return new Bounds(local, b.size);
    }

    void Update()
    {
        if (_dead)
            return;
        if (Time.unscaledTime < _flashUntil)
            ApplyFlash(Mathf.InverseLerp(_flashUntil - 0.12f, _flashUntil, Time.unscaledTime));
        float dt = Time.deltaTime;
        _t += dt;
        float u = Mathf.Clamp01(_t / _dur);
        Vector3 house = HouseBuildMode.Current != null ? HouseBuildMode.Current.HoldOrigin : _house;
        Vector3 cruise = house + Vector3.up * 18f - _passDir * 14f;
        Vector3 over = house + Vector3.up * 7.4f;
        Vector3 exit = house + _passDir * 130f + Vector3.up * 36f;
        Vector3 want;
        Vector3 tangent;
        if (_dropped)
        {
            float s = Smooth(Mathf.Clamp01((_t - _dropAt) / 5.8f));
            want = Vector3.Lerp(_dropPos, exit, s);
            tangent = exit - _dropPos;
            if (tangent.sqrMagnitude < 0.01f)
                tangent = _passDir + Vector3.up * 0.18f;
        }
        else if (u < 0.5f)
        {
            float s = Smooth(u / 0.5f);
            want = Vector3.Lerp(_start, cruise, s);
            tangent = cruise - _start;
        }
        else if (u < 0.62f)
        {
            float s = Smooth((u - 0.5f) / 0.12f);
            want = Vector3.Lerp(cruise, over, s);
            tangent = over - cruise;
        }
        else
        {
            float s = Smooth((u - 0.62f) / 0.38f);
            want = Vector3.Lerp(over, exit, s);
            tangent = exit - over;
        }

        float floor = TerrainClearance(want, _dropped);
        want.y = Mathf.Max(want.y, floor);
        float chase = _dropped ? 5.4f : 3.1f;
        Vector3 next = Vector3.Lerp(_pos, want, 1f - Mathf.Exp(-chase * dt));
        next.y = Mathf.Max(next.y, TerrainClearance(next, _dropped));
        _vel = (next - _pos) / Mathf.Max(dt, 0.0001f);
        _pos = next;
        transform.position = _pos;

        if (tangent.sqrMagnitude < 0.01f)
            tangent = _passDir;
        Vector3 flat = new Vector3(tangent.x, 0f, tangent.z);
        if (flat.sqrMagnitude < 0.0001f)
            flat = _passDir;
        flat.Normalize();
        float wantYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        _yaw = Mathf.LerpAngle(_yaw, wantYaw, 1f - Mathf.Exp(-3.4f * dt));
        Vector3 tn = tangent.normalized;
        float wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(tn.y, -0.42f, 0.42f)) * Mathf.Rad2Deg, _dropped ? -26f : -20f, 14f);
        _pitch = Mathf.Lerp(_pitch, wantPitch, 1f - Mathf.Exp(-2.8f * dt));
        float turn = Mathf.DeltaAngle(_yaw, wantYaw);
        _roll = Mathf.Lerp(_roll, Mathf.Clamp(-turn * 0.28f, -18f, 18f), 1f - Mathf.Exp(-3.6f * dt));
        transform.rotation = Quaternion.Euler(_pitch, _yaw, _roll) * _faceFix;

        float planar = Vector2.Distance(new Vector2(_pos.x, _pos.z), new Vector2(house.x, house.z));
        if (!_dropped && planar < 5.2f && u > 0.42f)
            DropEggs();

        if (_dropped && _t - _dropAt > 7.5f)
            Destroy(gameObject);
        else if (!_dropped && u >= 1f)
            Destroy(gameObject);
    }

    static float TerrainClearance(Vector3 pos, bool departing)
    {
        float pad = departing ? 8.5f : 5.2f;
        float y = pos.y;
        var terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
            y = terrain.SampleHeight(pos) + terrain.transform.position.y + pad;
        if (departing)
            return y;
        if (Physics.Raycast(pos + Vector3.up * 12f, Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null
                && hit.collider.GetComponentInParent<HouseBombBird>() == null
                && hit.collider.GetComponentInParent<HouseBombEgg>() == null
                && hit.collider.GetComponentInParent<BoatPiece>() == null)
                y = Mathf.Max(y, hit.point.y + 4.6f);
        }
        return y;
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    void DropEggs()
    {
        _dropped = true;
        _dropAt = _t;
        _dropPos = _pos;
        Vector3 house = HouseBuildMode.Current != null ? HouseBuildMode.Current.HoldOrigin : _house;
        Vector3 right = Vector3.Cross(Vector3.up, _passDir);
        if (right.sqrMagnitude < 0.01f)
            right = Vector3.right;
        right.Normalize();
        int n = Mathf.Clamp(_dropN, 1, 4);
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (i / (float)(n - 1) - 0.5f);
            Vector3 at = new Vector3(house.x, _pos.y - 0.35f, house.z) + right * (t * 1.15f) + _passDir * Random.Range(-0.35f, 0.35f);
            Vector3 vel = Vector3.down * Random.Range(3.8f, 5.2f) + right * t * 0.35f;
            HouseBombEgg.Spawn(at, vel, this);
        }
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_dead)
            return;
        _hp -= amount;
        Flash();
        if (_hp > 0f)
            return;
        Die(hitDirection);
    }

    void Flash()
    {
        _flashUntil = Time.unscaledTime + 0.12f;
        ApplyFlash(0f);
    }

    void ApplyFlash(float recover)
    {
        if (_rends == null)
            return;
        for (int i = 0; i < _rends.Length; i++)
        {
            if (_rends[i] == null)
                continue;
            Color baseC = _baseColors != null && i < _baseColors.Length ? _baseColors[i] : Color.white;
            Color c = Color.Lerp(Color.Lerp(baseC, Color.white, 0.5f), baseC, recover);
            var m = _rends[i].material;
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", c);
            m.color = c;
        }
    }

    public void Die(Vector3 dir)
    {
        if (_dead)
            return;
        _dead = true;
        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 kick = dir.sqrMagnitude > 0.01f ? dir.normalized : -transform.forward;
            kick.y = Mathf.Clamp(kick.y, -0.15f, 0.45f);
            _rb.AddForce(kick * 5.5f + Vector3.up * 1.8f, ForceMode.VelocityChange);
            _rb.AddTorque(transform.right * 1.4f, ForceMode.VelocityChange);
        }
        Destroy(gameObject, 5f);
    }
}

public class HouseBombEgg : MonoBehaviour, IDamageable
{
    const float BlastRadius = 4.4f;
    Rigidbody _rb;
    HouseBombBird _owner;
    bool _boom;
    float _born;
    bool _playerShot;

    public bool IsDead => _boom;

    public static HouseBombEgg Spawn(Vector3 pos, Vector3 vel, HouseBombBird owner)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BombEgg";
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.2f, 0.26f, 0.2f);
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh);
            Color shell = new Color(0.93f, 0.86f, 0.68f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", shell);
            mat.color = shell;
            rend.sharedMaterial = mat;
        }
        var egg = go.AddComponent<HouseBombEgg>();
        egg._rb = go.GetComponent<Rigidbody>();
        if (egg._rb == null)
            egg._rb = go.AddComponent<Rigidbody>();
        egg._rb.mass = 1.1f;
        egg._rb.interpolation = RigidbodyInterpolation.Interpolate;
        egg._rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        egg._rb.linearVelocity = vel;
        egg._born = Time.time;
        egg._owner = owner;
        return egg;
    }

    void Update()
    {
        if (!_boom && Time.time - _born > 9f)
            Explode();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_boom || collision == null)
            return;
        if (collision.collider != null && collision.collider.GetComponentInParent<HouseBombBird>() != null)
            return;
        if (collision.relativeVelocity.sqrMagnitude < 1.2f && Time.time - _born < 0.12f)
            return;
        Explode();
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        _playerShot = true;
        Explode();
    }

    public void Explode()
    {
        if (_boom)
            return;
        _boom = true;
        Vector3 at = transform.position;
        HouseEggBlast.Play(at);
        if (HouseBuildMode.Current != null && HouseBuildMode.Holding)
            HouseBuildMode.Current.BlastHouse(at, 0.82f);

        if (_owner != null && !_owner.IsDead)
        {
            var bird = _owner;
            bird.TakeDamage(80f, at, (bird.transform.position - at).normalized);
            if (_playerShot && bird.IsDead)
            {
                var actor = RaceRoster.Local();
                RaceSim.AnnounceKill(actor != null ? actor.Id : (ushort)0, bird, "Pistol");
            }
        }

        var hits = Physics.OverlapSphere(at, BlastRadius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null)
                continue;
            var other = hits[i].GetComponentInParent<HouseBombEgg>();
            if (other != null && other != this && !other._boom)
            {
                if (_playerShot)
                    other._playerShot = true;
                other.Explode();
            }
        }
        Destroy(gameObject);
    }
}

public static class HouseEggBlast
{
    public static void Play(Vector3 pos)
    {
        var root = new GameObject("EggBlast");
        root.transform.position = pos;
        Object.Destroy(root, 4.2f);

        var lightGo = new GameObject("Flash");
        lightGo.transform.SetParent(root.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.32f);
        light.range = 18f;
        light.intensity = 14f;
        light.shadows = LightShadows.None;
        lightGo.AddComponent<HouseBlastFade>().Run(light);

        Burst(root.transform, "Core", 0.12f, 0.22f, 1.2f, 3.5f, 0.35f, 0.9f,
            new Color(1f, 0.95f, 0.65f, 1f), new Color(1f, 0.35f, 0.05f, 0.2f), 0.05f, 28, true);
        Burst(root.transform, "Fire", 0.22f, 0.45f, 2.2f, 6.5f, 0.22f, 0.7f,
            new Color(1f, 0.7f, 0.2f, 0.95f), new Color(0.35f, 0.04f, 0.01f, 0f), -0.2f, 36, true);
        Burst(root.transform, "Smoke", 0.7f, 1.8f, 0.6f, 2.1f, 0.7f, 2.4f,
            new Color(0.22f, 0.2f, 0.18f, 0.55f), new Color(0.08f, 0.08f, 0.08f, 0f), -0.12f, 32, false);
        Sparks(root.transform);
        Debris(root.transform);
        Shock(root.transform);
    }

    static void Burst(Transform parent, string name, float lifeMin, float lifeMax, float spdMin, float spdMax,
        float sizeMin, float sizeMax, Color a, Color b, float grav, int count, bool additive)
    {
        var ps = Make(parent, name, additive);
        var main = ps.main;
        main.duration = 0.18f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(spdMin, spdMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = grav;
        main.maxParticles = count + 8;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.18f;
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(count - 6), (short)count) });
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
            new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.55f));
        var nse = ps.noise;
        nse.enabled = true;
        nse.strength = 0.55f;
        nse.frequency = 0.7f;
        ps.Play();
    }

    static void Sparks(Transform parent)
    {
        var ps = Make(parent, "Sparks", true);
        var main = ps.main;
        main.duration = 0.15f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new Color(1f, 0.82f, 0.35f);
        main.gravityModifier = 1.1f;
        main.maxParticles = 40;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.1f;
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 18, 32) });
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.lengthScale = 3.2f;
        rend.velocityScale = 0.12f;
        ps.Play();
    }

    static void Debris(Transform parent)
    {
        var ps = Make(parent, "Debris", false);
        var main = ps.main;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
        main.startColor = new Color(0.45f, 0.28f, 0.12f);
        main.gravityModifier = 1.6f;
        main.maxParticles = 18;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Hemisphere;
        sh.radius = 0.15f;
        var em = ps.emission;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 16) });
        ps.Play();
    }

    static ParticleSystem Make(Transform parent, string name, bool additive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        string shaderName = additive
            ? "Universal Render Pipeline/Particles/Unlit"
            : "Universal Render Pipeline/Particles/Unlit";
        var shader = Shader.Find(shaderName) ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (additive && mat.HasProperty("_Blend"))
            mat.SetFloat("_Blend", 1f);
        Texture2D tex = LoadBlastTex(additive);
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
        }
        rend.sharedMaterial = mat;
        return ps;
    }

    static Texture2D LoadBlastTex(bool fire)
    {
#if UNITY_EDITOR
        string path = fire
            ? "Assets/JMO Assets/WarFX/Desktop/Textures/Flames/WFX_T_FlamesBig.tga"
            : "Assets/JMO Assets/WarFX/Desktop/Textures/Smoke/WFX_T_SmokeNoise.tga";
        var t = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (t != null)
            return t;
#endif
        return null;
    }

    static void Shock(Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Shock";
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 0.2f;
        Object.Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(sh);
        Color c = new Color(1f, 0.78f, 0.4f, 0.55f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", c);
        mat.color = c;
        r.sharedMaterial = mat;
        go.AddComponent<HouseShockGrow>();
    }
}

public class HouseBlastFade : MonoBehaviour
{
    Light _light;
    float _t;

    public void Run(Light light)
    {
        _light = light;
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_light != null)
            _light.intensity = Mathf.Lerp(14f, 0f, _t / 0.22f);
        if (_t > 0.35f)
            Destroy(this);
    }
}

public class HouseShockGrow : MonoBehaviour
{
    float _t;
    Renderer _r;

    void Awake()
    {
        _r = GetComponent<Renderer>();
    }

    void Update()
    {
        _t += Time.deltaTime;
        float u = Mathf.Clamp01(_t / 0.38f);
        transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 9.5f, 1f - (1f - u) * (1f - u));
        if (_r != null && _r.material != null)
        {
            Color c = _r.material.color;
            c.a = (1f - u) * 0.5f;
            if (_r.material.HasProperty("_BaseColor"))
                _r.material.SetColor("_BaseColor", c);
            _r.material.color = c;
        }
        if (_t > 0.34f)
            Destroy(gameObject);
    }
}
