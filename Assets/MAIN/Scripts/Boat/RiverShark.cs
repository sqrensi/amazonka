using UnityEngine;

/// <summary>
/// Речная акула: догоняет цель RaceActor и грызёт корпус.
/// Модель: Assets/MAIN/Sharks (Resources/SharkV1).
/// </summary>
[DefaultExecutionOrder(40)]
public class RiverShark : MonoBehaviour, IDamageable
{
    public const float MaxHp = 160f;
    const string ModelResource = "SharkV1";
    const float VisualLength = 3.35f;

    public enum Kind
    {
        Stalker,
        Bull,
        Runner
    }

    [SerializeField] float moveSpeed = 18.5f;
    [SerializeField] float biteRange = 2.95f;
    [SerializeField] float retreatSeconds = 3.4f;
    [SerializeField] float retreatDistance = 11f;

    enum Pass
    {
        Hunt,
        Circle,
        Lunge,
        Peel,
        Ambush
    }

    Kind _kind = Kind.Stalker;
    float _kindSpeed = 1f;
    float _kindTurn = 1f;
    float _kindPeel = 1f;
    float _maxHp = MaxHp;
    ushort _id;
    ushort _targetId;
    float _hp = MaxHp;
    Pass _pass = Pass.Hunt;
    Vector3 _peelPoint;
    float _peelUntil;
    bool _dead;
    bool _engaged;
    float _sink;
    float _blindUntil;
    float _flashReady;
    float _throwHitAt;
    Vector3 _stunDir;
    Renderer[] _rends;
    Color[] _baseColors;
    CapsuleCollider _body;
    Rigidbody _rb;
    bool _rising;
    Vector3 _smoothVel;
    Vector3 _velDamp;
    Vector3 _heading;
    Vector3 _smoothCarry;
    Vector3 _avoid;
    float _speed;
    float _jukePhase;
    float _jukeAmp;
    float _jukeHz;
    float _moodUntil;
    int _mood;
    float _rage;
    float _lungeUntil;
    Vector3 _aimSmooth;
    Vector3 _aimDamp;
    Vector3 _simPosVel;
    float _yawRate;
    float _bank;
    float _stuckTime;
    Vector3 _simPos;
    Quaternion _simRot = Quaternion.identity;
    bool _simReady;
    static readonly RaycastHit[] SweepHits = new RaycastHit[16];
    static readonly Collider[] OverlapBuf = new Collider[16];

    public ushort Id => _id;
    public bool IsDead => _dead;
    public float HealthMax => _maxHp;
    public float Hp01 => Mathf.Clamp01(_hp / Mathf.Max(1f, _maxHp));
    public Kind Breed => _kind;
    public bool Engaged => _engaged && !_dead;
    public bool ShowHud
    {
        get
        {
            if (_dead)
                return false;
            var player = RaceRoster.Local();
            if (player == null)
                return false;
            if ((transform.position - player.transform.position).sqrMagnitude > 20f * 20f)
                return false;
            if (BoatWater.TryHeight(transform.position, out float y))
                return transform.position.y > y - 1.8f;
            return !_rising;
        }
    }
    public string DisplayName
    {
        get
        {
            switch (_kind)
            {
                case Kind.Bull: return "Bull";
                case Kind.Runner: return "Runner";
                default: return "Stalker";
            }
        }
    }

    public static RiverShark Spawn(Vector3 pos, ushort id, Kind kind = Kind.Stalker)
    {
        if (!Finite(pos))
            return null;
        var go = new GameObject("RiverShark");
        go.transform.position = pos;
        var shark = go.AddComponent<RiverShark>();
        shark._id = id;
        shark.ApplyKind(kind);
        shark.Build();
        shark._rising = true;
        shark.RollMood();
        shark._speed = 7.2f * shark._kindSpeed;
        return shark;
    }

    public void HoldAmbush(Vector3 home)
    {
        _peelPoint = home;
        _pass = Pass.Ambush;
        _moodUntil = RaceSim.RaceElapsed + 18f;
    }

    void ApplyKind(Kind kind)
    {
        _kind = kind;
        switch (kind)
        {
            case Kind.Bull:
                _kindSpeed = 1.06f;
                _kindTurn = 1.18f;
                _kindPeel = 0.55f;
                _maxHp = 190f;
                moveSpeed = 7.1f;
                biteRange = 3.05f;
                retreatSeconds = 2.1f;
                retreatDistance = 7.5f;
                break;
            case Kind.Runner:
                _kindSpeed = 1.12f;
                _kindTurn = 0.92f;
                _kindPeel = 0.7f;
                _maxHp = 125f;
                moveSpeed = 7.6f;
                biteRange = 2.75f;
                retreatSeconds = 2.8f;
                retreatDistance = 10f;
                break;
            default:
                _kindSpeed = 1f;
                _kindTurn = 1.28f;
                _kindPeel = 1f;
                _maxHp = 160f;
                moveSpeed = 6.6f;
                biteRange = 2.9f;
                retreatSeconds = 3.8f;
                retreatDistance = 12f;
                break;
        }
        _hp = _maxHp;
    }

    static GameObject LoadModel()
    {
        var prefab = Resources.Load<GameObject>(ModelResource);
        if (prefab != null)
            return prefab;
#if UNITY_EDITOR
        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MAIN/Sharks/SharkV1.prefab");
#endif
        return prefab;
    }

    void Build()
    {
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var prefab = LoadModel();
        Transform vis = null;
        if (prefab != null)
        {
            var model = Instantiate(prefab, transform);
            model.name = "SharkV1";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            vis = model.transform;
            StripColliders(model);
            StripBodies(model);
        }

        FitCollider(vis);
        _rends = GetComponentsInChildren<Renderer>();
        _baseColors = new Color[_rends.Length];
        for (int i = 0; i < _rends.Length; i++)
        {
            if (_rends[i] == null)
                continue;
            ForceOpaque(_rends[i]);
            _baseColors[i] = ReadTint(_rends[i]);
        }
    }

    static void StripBodies(GameObject root)
    {
        var bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null)
                continue;
            bodies[i].detectCollisions = false;
            bodies[i].isKinematic = true;
            Destroy(bodies[i]);
        }
    }

    static bool Finite(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }

    static Vector3 FlatDir(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        if (Finite(v) && v.sqrMagnitude > 0.0001f)
            return v.normalized;
        fallback.y = 0f;
        if (Finite(fallback) && fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;
        return Vector3.forward;
    }

    static void StripColliders(GameObject root)
    {
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                Destroy(cols[i]);
        }
    }

    void FitCollider(Transform vis)
    {
        Bounds b = default;
        bool any = false;
        var rends = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null)
                continue;
            if (!any)
            {
                b = rends[i].bounds;
                any = true;
            }
            else
                b.Encapsulate(rends[i].bounds);
        }

        if (any && vis != null)
        {
            float len = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (len > 0.05f)
                vis.localScale *= VisualLength / len;
            if (b.size.x > b.size.z * 1.15f)
                vis.localRotation = Quaternion.Euler(0f, 90f, 0f);
            rends = GetComponentsInChildren<Renderer>();
            any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null)
                    continue;
                if (!any)
                {
                    b = rends[i].bounds;
                    any = true;
                }
                else
                    b.Encapsulate(rends[i].bounds);
            }
        }

        var cap = gameObject.AddComponent<CapsuleCollider>();
        cap.direction = 2;
        if (!any)
        {
            cap.radius = 0.42f;
            cap.height = VisualLength;
            cap.center = Vector3.zero;
        }
        else
        {
            Vector3 localCenter = transform.InverseTransformPoint(b.center);
            Vector3 localSize = transform.InverseTransformVector(b.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            cap.center = localCenter;
            cap.height = Mathf.Max(1.2f, localSize.z);
            cap.radius = Mathf.Max(0.22f, Mathf.Max(localSize.x, localSize.y) * 0.45f);
        }
        _body = cap;
        _rb = GetComponent<Rigidbody>();
        IgnorePlayers();
    }

    static void IgnorePlayers()
    {
        var players = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var sharks = Object.FindObjectsByType<RiverShark>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int s = 0; s < sharks.Length; s++)
        {
            var shark = sharks[s];
            if (shark == null)
                continue;
            var sc = shark.GetComponent<Collider>();
            if (sc == null)
                continue;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null)
                    Physics.IgnoreCollision(sc, players[i], true);
            }
        }
    }

    public void HitByThrown(string weapon, Collision collision)
    {
        if (_dead || collision == null)
            return;
        if (Time.time < _throwHitAt + 0.2f)
            return;
        Vector3 rel = collision.relativeVelocity;
        rel.y = 0f;
        float planar = rel.magnitude;
        if (planar < 3.2f)
            return;
        _throwHitAt = Time.time;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 dir = collision.relativeVelocity.sqrMagnitude > 0.01f
            ? collision.relativeVelocity.normalized
            : (transform.position - point).normalized;
        float amount = Mathf.Clamp(7f + planar * 1.15f, 6f, 16f);
        var actor = RaceRoster.Local();
        if (actor != null)
            actor.WeaponName = weapon;
        ushort id = actor != null ? actor.Id : (ushort)0;
        RaceSim.RequestDamage(id, this, amount, point, dir, weapon);
    }

    public static void TryFlash(Vector3 origin, Vector3 dir, float range, float coneDegrees)
    {
        var sharks = Object.FindObjectsByType<RiverShark>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < sharks.Length; i++)
        {
            if (sharks[i] != null)
                sharks[i].ReceiveFlash(origin, dir, range, coneDegrees);
        }
    }

    void ReceiveFlash(Vector3 origin, Vector3 dir, float range, float coneDegrees)
    {
        if (_dead)
            return;
        if (Time.time < _blindUntil || Time.time < _flashReady)
            return;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
        float half = Mathf.Cos(coneDegrees * Mathf.Deg2Rad * 0.5f);
        Vector3 a = transform.position;
        Vector3 fwd = _heading.sqrMagnitude > 0.01f ? _heading : transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f)
            fwd = transform.forward;
        fwd.Normalize();
        Vector3 b = a + fwd * 1.15f;
        Vector3 c = a - fwd * 1.15f;
        if (!FlashHits(origin, dir, range, half, a)
            && !FlashHits(origin, dir, range, half, b)
            && !FlashHits(origin, dir, range, half, c))
            return;
        _stunDir = Quaternion.Euler(0f, Random.Range(-180f, 180f), 0f) * Vector3.forward;
        float blind = _rage > 0.35f ? 0.55f : 1.05f;
        _blindUntil = Time.time + blind;
        _flashReady = Time.time + (_rage > 0.35f ? 5.2f : 7.5f);
        Flash(transform.position);
        Anger(0.22f, _stunDir);
    }

    bool FlashHits(Vector3 origin, Vector3 dir, float range, float minDot, Vector3 point)
    {
        Vector3 to = point - origin;
        float dist = to.magnitude;
        if (dist < 0.15f || dist > range)
            return false;
        Vector3 n = to / dist;
        if (Vector3.Dot(dir, n) < minDot)
            return false;
        if (!Physics.Raycast(origin, n, out RaycastHit hit, dist - 0.08f, ~0, QueryTriggerInteraction.Ignore))
            return true;
        var col = hit.collider;
        if (col == null)
            return true;
        if (col.transform.IsChildOf(transform) || col.GetComponentInParent<RiverShark>() == this)
            return true;
        if (col.GetComponentInParent<BoatWater>() != null)
            return true;
        if (col.GetComponentInParent<CharacterController>() != null)
            return true;
        if (col.GetComponentInParent<HorrorFirstPersonController>() != null)
            return true;
        if (col.GetComponentInParent<HeldItem>() != null)
            return true;
        return false;
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection)
    {
        if (_dead)
            return;
        _hp = Mathf.Max(0f, _hp - amount);
        _engaged = true;
        Flash(hitPoint);
        Anger(Mathf.Clamp01(amount / 40f) + 0.28f, hitDirection);
        if (_hp <= 0.01f)
            Die();
    }

    void Anger(float amount, Vector3 fromHit)
    {
        _rage = Mathf.Clamp01(_rage + amount);
        _moodUntil = RaceSim.RaceElapsed + Random.Range(3.5f, 6.5f);
        if (_pass == Pass.Peel || _pass == Pass.Circle)
            _pass = Pass.Hunt;
        if (_rage > 0.55f && _kind != Kind.Stalker)
        {
            _pass = Pass.Lunge;
            _lungeUntil = RaceSim.RaceElapsed + 1.8f;
        }
        if (fromHit.sqrMagnitude > 0.01f)
        {
            Vector3 away = fromHit;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f)
                _heading = Vector3.Slerp(_heading, away.normalized, 0.12f);
        }
    }

    public void Die()
    {
        if (_dead)
            return;
        _dead = true;
        _sink = 0f;
        var col = GetComponent<Collider>();
        if (col != null)
            col.enabled = false;
    }

    void Update()
    {
        if (!_dead)
            return;
        _simReady = false;
        _sink += Time.deltaTime;
        transform.position += Vector3.down * (0.38f * Time.deltaTime);
        transform.Rotate(22f * Time.deltaTime, 0f, 8f * Time.deltaTime, Space.Self);
        Fade(Mathf.InverseLerp(8f, 14.5f, _sink));
        float s = Mathf.Lerp(1f, 0.82f, Mathf.InverseLerp(6f, 14f, _sink));
        transform.localScale = Vector3.one * s;
        if (_sink > 14.5f)
            Destroy(gameObject);
    }

    void FixedUpdate()
    {
        if (_dead || !RaceSim.HasAuthority)
            return;

        if (!_simReady)
        {
            _simPos = _rb != null ? _rb.position : transform.position;
            _simRot = _rb != null ? _rb.rotation : transform.rotation;
            _simReady = true;
        }

        Vector3 pos = _simPos;
        Quaternion rot = _simRot;
        Vector3 fwd = rot * Vector3.forward;
        Vector3 right = rot * Vector3.right;

        var prey = RaceRoster.Find(_targetId) ?? RaceRoster.PreyNear(pos);
        if (prey != null)
            _targetId = prey.Id;
        if (prey == null)
            return;

        Vector3 chase = prey.Craft != null ? prey.Craft.transform.position : prey.transform.position;
        Vector3 flow = BoatWater.CurrentAt(pos);
        flow.y = 0f;
        Vector3 carry = PreyCarry(prey, flow);
        float dt = Time.fixedDeltaTime;
        _rage = Mathf.MoveTowards(_rage, 0f, dt * 0.085f);

        bool blinded = Time.time < _blindUntil;
        float range = Planar(chase, pos);
        Vector3 toBoat = chase - pos;
        toBoat.y = 0f;
        Vector3 boatDir = toBoat.sqrMagnitude > 0.01f ? toBoat.normalized : fwd;
        Vector3 side = Vector3.Cross(Vector3.up, boatDir);
        if (side.sqrMagnitude < 0.01f)
            side = right;
        side.Normalize();
        if (_mood == 1)
            side = -side;

        Vector3 aim;
        float extra;
        if (blinded)
        {
            if (_stunDir.sqrMagnitude < 0.01f)
                _stunDir = fwd;
            _stunDir = Vector3.Slerp(_stunDir, Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.55f) * 28f, 0f) * _stunDir, dt * 0.9f);
            _stunDir.y = 0f;
            if (_stunDir.sqrMagnitude < 0.01f)
                _stunDir = right;
            _stunDir.Normalize();
            aim = pos + _stunDir * 12f;
            extra = CatchExtra(range, 0.82f);
        }
        else if (_pass == Pass.Ambush)
        {
            _pass = Pass.Hunt;
            aim = Intercept(chase, carry, pos);
            extra = CatchExtra(range, 1.05f);
        }
        else if (_pass == Pass.Peel)
        {
            if (RaceSim.RaceElapsed >= _peelUntil || _rage > 0.7f)
                _pass = Pass.Hunt;
            float hold = Planar(_peelPoint, pos);
            if (hold < 2.4f || range > 14f)
            {
                aim = Intercept(chase, carry, pos);
                extra = CatchExtra(range, 1.05f);
            }
            else
            {
                aim = _peelPoint;
                extra = CatchExtra(range, 0.95f);
            }
        }
        else if (_pass == Pass.Circle)
        {
            _pass = Pass.Lunge;
            _lungeUntil = RaceSim.RaceElapsed + Random.Range(1.15f, 1.9f);
            aim = Intercept(chase, carry, pos);
            extra = CatchExtra(range, 1.15f);
        }
        else if (_pass == Pass.Lunge)
        {
            if (RaceSim.RaceElapsed >= _lungeUntil && range > biteRange + 1.2f)
                _pass = Pass.Hunt;
            aim = Intercept(chase, carry, pos);
            extra = CatchExtra(range, 1.28f + _rage * 0.28f);
        }
        else
        {
            float weave = Mathf.Sin(Time.time * _jukeHz + _jukePhase) * _jukeAmp * 0.35f * (1f - _rage);
            aim = Intercept(chase, carry, pos) + side * weave;
            extra = CatchExtra(range, 1.08f + _rage * 0.22f);
            if (range < 14f && Vector3.Dot(_heading, boatDir) > 0.62f && Random.value < dt * 0.55f)
            {
                _pass = Pass.Lunge;
                _lungeUntil = RaceSim.RaceElapsed + Random.Range(0.9f, 1.6f);
            }
        }

        if (!Finite(aim))
            aim = pos + FlatDir(_heading, fwd) * 8f;
        if (!Finite(_aimSmooth) || _aimSmooth.sqrMagnitude < 0.01f)
            _aimSmooth = aim;
        if (!Finite(_aimDamp))
            _aimDamp = Vector3.zero;
        _aimSmooth = Vector3.SmoothDamp(_aimSmooth, aim, ref _aimDamp, 0.28f, 42f, dt);
        if (!Finite(_aimSmooth))
            _aimSmooth = aim;

        Vector3 to = _aimSmooth - pos;
        to.y = 0f;
        float dist = to.magnitude;
        Vector3 wishDir = dist > 0.08f && Finite(to) ? to / dist : FlatDir(_heading, fwd);
        wishDir.y = 0f;
        wishDir = FlatDir(wishDir, fwd);
        Vector3 peer = AvoidPeers(pos);
        peer.y = 0f;
        if (peer.sqrMagnitude > 0.04f && Finite(peer))
            wishDir = FlatDir(wishDir + peer.normalized * Mathf.Clamp01(peer.magnitude * 0.07f), wishDir);

        if (BoatCurrentPath.TryCenter(pos, out Vector3 mid, out _))
        {
            Vector3 home = mid - pos;
            home.y = 0f;
            float off = home.magnitude;
            if (off > 3.4f && Finite(home))
                wishDir = FlatDir(wishDir + home.normalized * 0.22f, wishDir);
        }

        Vector3 feelFwd = FlatDir(_heading, wishDir);
        Vector3 felt = FeelObstacles(pos, feelFwd, range);
        if (!Finite(felt))
            felt = Vector3.zero;
        _avoid = Vector3.Lerp(_avoid, felt, 1f - Mathf.Exp(-3.1f * dt));
        if (!Finite(_avoid))
            _avoid = Vector3.zero;
        if (_avoid.sqrMagnitude > 0.85f)
            _avoid = _avoid.normalized * 0.85f;
        if (_avoid.sqrMagnitude > 0.01f)
            wishDir = FlatDir(wishDir + _avoid * 1.35f, wishDir);

        extra *= Mathf.Lerp(1f, 0.9f, Mathf.Clamp01(_avoid.magnitude));
        if (blinded)
            extra *= 0.78f;

        _smoothCarry = Vector3.Lerp(_smoothCarry, carry, 1f - Mathf.Exp(-2.6f * dt));
        if (_heading.sqrMagnitude < 0.01f || !Finite(_heading))
            _heading = wishDir;
        float turn = (1.55f * _kindTurn) * Mathf.Lerp(1.1f, 0.78f, Mathf.Clamp01(_speed / 14f));
        turn *= 1f + _rage * 0.22f;
        Vector3 prevHead = _heading;
        _heading = Vector3.RotateTowards(_heading, wishDir, turn * dt, 0f);
        _heading.y = 0f;
        _heading = FlatDir(_heading, prevHead);
        float signed = Vector3.SignedAngle(prevHead, _heading, Vector3.up) / Mathf.Max(dt, 0.0001f);
        _yawRate = Mathf.Lerp(_yawRate, signed, 1f - Mathf.Exp(-5.5f * dt));

        float accel = range > 16f ? 16f : 9f;
        accel *= 1f + _rage * 0.25f;
        _speed = Mathf.MoveTowards(_speed, extra, accel * dt);
        Vector3 ride = flow;
        ride.y = 0f;
        if (ride.sqrMagnitude < 0.2f)
        {
            ride = carry;
            ride.y = 0f;
        }
        _smoothVel = ride + _heading * _speed;

        Vector3 from = pos;
        float surfY = from.y;
        if (BoatWater.TryHeight(from + _smoothVel * dt, out float waterY))
            surfY = waterY - 0.2f;
        _rising = from.y < surfY - 0.28f;
        Vector3 planarStep = _smoothVel * dt;
        planarStep.y = 0f;
        Vector3 next = Glide(from, from + planarStep);
        next.y = Mathf.MoveTowards(from.y, surfY, (_rising ? 6.4f : 2.8f) * dt);
        next = NudgeOut(next, dt);
        next = SharkDirector.StayInRiver(next, Mathf.Max(planarStep.magnitude + 0.12f, 0.22f));
        if (!Finite(next))
        {
            _smoothVel = Vector3.zero;
            _aimDamp = Vector3.zero;
            _avoid = Vector3.zero;
            _heading = FlatDir(fwd, Vector3.forward);
            return;
        }
        Vector3 moved = next - from;
        moved.y = 0f;
        if (moved.sqrMagnitude > 0.00012f)
        {
            Vector3 along = moved.normalized;
            _heading = Vector3.RotateTowards(_heading, along, turn * dt * 0.85f, 0f);
            _heading = FlatDir(_heading, along);
        }
        float slid = Planar(from, next);
        if (range > 9f && slid < 0.07f && (_avoid.sqrMagnitude > 0.04f || _pass == Pass.Ambush))
            _stuckTime += dt;
        else
            _stuckTime = Mathf.Max(0f, _stuckTime - dt * 1.6f);
        if (_stuckTime > 0.55f)
        {
            UnstickTowardChannel(chase, from);
            if (_stuckTime > 1.4f)
                _stuckTime = 0.4f;
        }
        if (_heading.sqrMagnitude > 0.04f && Finite(_heading))
        {
            float wantBank = Mathf.Clamp(-_yawRate * 0.05f, -9f, 9f);
            _bank = Mathf.Lerp(_bank, wantBank, 1f - Mathf.Exp(-6f * dt));
            Quaternion want = Quaternion.LookRotation(_heading, Vector3.up) * Quaternion.Euler(0f, 0f, _bank);
            rot = Quaternion.Slerp(rot, want, 1f - Mathf.Exp(-3.8f * dt));
        }

        if (!Finite(_simPosVel))
            _simPosVel = Vector3.zero;
        _simPos = Vector3.SmoothDamp(_simPos, next, ref _simPosVel, 0.04f, 36f, dt);
        _simRot = rot;
        ApplySimPose();

        float biteDist = Planar(chase, from);
        if (biteDist < 52f)
            _engaged = true;

        if (!_rising && !blinded && _pass != Pass.Peel && biteDist <= biteRange
            && SharkDirector.CanStrikeFrom(from))
        {
            Bite(prey, chase);
            Vector3 away = from - chase;
            away.y = 0f;
            if (away.sqrMagnitude < 0.04f)
                away = -fwd;
            away.Normalize();
            Vector3 peelSide = Vector3.Cross(Vector3.up, away);
            if (Random.value > 0.5f)
                peelSide = -peelSide;
            float retreat = retreatDistance * Random.Range(0.7f, 1.15f) * _kindPeel;
            if (_rage > 0.5f)
                retreat *= 0.55f;
            _peelPoint = chase + away * retreat + peelSide * Random.Range(2.5f, 7.5f);
            _peelUntil = RaceSim.RaceElapsed + retreatSeconds * Random.Range(0.55f, 1.05f) * _kindPeel;
            if (_rage > 0.75f)
                _peelUntil = RaceSim.RaceElapsed + 0.55f;
            _pass = Pass.Peel;
            RollMood();
        }
    }

    void ApplySimPose()
    {
        if (_rb != null)
        {
            _rb.MovePosition(_simPos);
            _rb.MoveRotation(_simRot);
        }
        else
            transform.SetPositionAndRotation(_simPos, _simRot);
    }

    void UnstickTowardChannel(Vector3 chase, Vector3 here)
    {
        if (!BoatCurrentPath.TryAhead(here, 4f, 0f, out Vector3 open, out Vector3 tan)
            && !BoatCurrentPath.TryCenter(here, out open, out tan))
            open = chase;
        Vector3 pull = open - here;
        pull.y = 0f;
        if (pull.sqrMagnitude < 0.04f)
            pull = tan.sqrMagnitude > 0.01f ? tan : (chase - here);
        pull.y = 0f;
        if (pull.sqrMagnitude < 0.01f)
            return;
        pull.Normalize();
        _heading = Vector3.Slerp(_heading, pull, SimTime.Blend(0.12f, Time.fixedDeltaTime));
        _heading.y = 0f;
        if (_heading.sqrMagnitude > 0.01f)
            _heading.Normalize();
        _aimSmooth = here + pull * 8f;
        _aimDamp = Vector3.zero;
        _speed = Mathf.Max(_speed, 4.5f * _kindSpeed);
        _pass = Pass.Hunt;
    }

    void RollMood()
    {
        _jukePhase = Random.Range(0f, 6.3f);
        _jukeAmp = _kind == Kind.Stalker ? Random.Range(1.6f, 3.4f) : Random.Range(0.8f, 2.2f);
        _jukeHz = Random.Range(0.28f, 0.55f);
        _mood = Random.Range(0, 2);
        _moodUntil = RaceSim.RaceElapsed + Random.Range(1.8f, 4.6f);
    }

    float Cruise()
    {
        return moveSpeed * _kindSpeed;
    }

    float CatchExtra(float range, float drive)
    {
        drive = Mathf.Clamp(drive, 0.85f, 1.4f);
        float extra = 3.6f * _kindSpeed;
        extra += Mathf.Lerp(2.4f, 8.2f, Mathf.InverseLerp(4f, 40f, range)) * _kindSpeed;
        extra += _rage * 0.55f;
        if (_pass == Pass.Lunge)
            extra += 1.5f;
        return extra * drive;
    }

    static Vector3 Intercept(Vector3 chase, Vector3 boatVel, Vector3 from)
    {
        Vector3 rel = chase - from;
        rel.y = 0f;
        boatVel.y = 0f;
        float dist = rel.magnitude;
        if (dist < 0.2f)
            return chase;
        float t = dist / 6.5f;
        t = Mathf.Clamp(t, 0.15f, 1.4f);
        Vector3 lead = chase + boatVel * t;
        lead.y = chase.y;
        return lead;
    }

    Vector3 FeelObstacles(Vector3 pos, Vector3 fwd, float preyRange)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f)
            fwd = transform.forward;
        fwd.Normalize();
        Vector3 acc = Vector3.zero;
        const float r = 0.4f;
        float[] ang = { 0f, -20f, 20f, -42f, 42f, -70f, 70f };
        float[] reach = { 3.8f, 3.1f, 3.1f, 2.3f, 2.3f, 1.5f, 1.5f };
        int mask = SolidMask();
        for (int i = 0; i < ang.Length; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(ang[i], Vector3.up) * fwd;
            int n = Physics.SphereCastNonAlloc(pos, r, dir, SweepHits, reach[i], mask, QueryTriggerInteraction.Ignore);
            float best = reach[i];
            Vector3 nrm = Vector3.zero;
            bool hit = false;
            for (int h = 0; h < n; h++)
            {
                var s = SweepHits[h];
                if (s.collider == null || IgnoreSolid(s.collider) || IsPeer(s.collider))
                    continue;
                if (preyRange < 9.5f && s.collider.GetComponentInParent<BoatPiece>() != null)
                    continue;
                if (s.distance < 0.05f)
                {
                    Vector3 away = pos - s.point;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.0001f)
                        away = -dir;
                    acc += away.normalized * 2.2f;
                    continue;
                }
                Vector3 nn = PlanarNormal(s.normal);
                if (nn.sqrMagnitude < 0.05f)
                    continue;
                if (s.distance < best)
                {
                    best = s.distance;
                    nrm = nn;
                    hit = true;
                }
            }
            if (!hit)
                continue;
            float t = 1f - best / reach[i];
            acc += nrm.normalized * (t * t);
        }
        return acc;
    }

    Vector3 Glide(Vector3 from, Vector3 want)
    {
        Vector3 delta = want - from;
        delta.y = 0f;
        float dist = delta.magnitude;
        if (dist < 0.00005f)
            return from;
        Vector3 dir = delta / dist;
        const float r = 0.34f;
        int n = Physics.SphereCastNonAlloc(from, r, dir, SweepHits, dist + 0.1f, SolidMask(), QueryTriggerInteraction.Ignore);
        float best = dist;
        Vector3 nrm = Vector3.zero;
        bool hit = false;
        for (int i = 0; i < n; i++)
        {
            var h = SweepHits[i];
            if (h.collider == null || IgnoreSolid(h.collider) || IsPeer(h.collider))
                continue;
            if (h.distance < 0.05f)
                return from;
            Vector3 nn = PlanarNormal(h.normal);
            if (nn.sqrMagnitude < 0.05f)
                continue;
            if (h.distance < best)
            {
                best = h.distance;
                nrm = nn;
                hit = true;
            }
        }
        if (!hit)
            return from + delta;
        nrm.Normalize();
        _avoid = Vector3.Lerp(_avoid, nrm, SimTime.Blend(0.08f, Time.fixedDeltaTime));
        Vector3 along = Vector3.ProjectOnPlane(delta, nrm);
        if (along.sqrMagnitude < 0.000001f)
            along = Vector3.Cross(Vector3.up, nrm) * dist * 0.35f;
        float alongDist = along.magnitude;
        if (alongDist < 0.00005f)
            return from;
        Vector3 alongDir = along / alongDist;
        n = Physics.SphereCastNonAlloc(from, r, alongDir, SweepHits, alongDist, SolidMask(), QueryTriggerInteraction.Ignore);
        float go = alongDist;
        for (int i = 0; i < n; i++)
        {
            var h = SweepHits[i];
            if (h.collider == null || IgnoreSolid(h.collider) || IsPeer(h.collider))
                continue;
            if (h.distance < 0.05f)
            {
                go = 0f;
                break;
            }
            if (h.distance < go)
                go = Mathf.Max(0f, h.distance - 0.04f);
        }
        return from + alongDir * go;
    }

    Vector3 NudgeOut(Vector3 pos, float dt)
    {
        const float r = 0.32f;
        int n = Physics.OverlapSphereNonAlloc(pos, r, OverlapBuf, SolidMask(), QueryTriggerInteraction.Ignore);
        Vector3 push = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            var col = OverlapBuf[i];
            if (IgnoreSolid(col) || IsPeer(col))
                continue;
            Vector3 closest = BoatBuildUtil.ClosestPoint(col, pos);
            Vector3 d = pos - closest;
            d.y = 0f;
            float mag = d.magnitude;
            if (mag < 0.04f && BoatCurrentPath.TryCenter(pos, out Vector3 mid, out _))
            {
                d = mid - pos;
                d.y = 0f;
                mag = d.magnitude;
            }
            float need = r - mag;
            if (need > 0f && mag > 0.001f)
                push += d * (need / mag);
        }
        if (push.sqrMagnitude < 0.0001f)
            return pos;
        float max = 0.7f * dt;
        if (push.magnitude > max)
            push = push.normalized * max;
        _avoid = Vector3.Lerp(_avoid, push.normalized, SimTime.Blend(0.12f, dt));
        return pos + push;
    }

    static Vector3 PlanarNormal(Vector3 n)
    {
        if (Mathf.Abs(n.y) > 0.92f)
            n = Vector3.ProjectOnPlane(n, Vector3.up);
        n.y = 0f;
        return n;
    }

    Vector3 AvoidPeers(Vector3 pos)
    {
        Vector3 steer = Vector3.zero;
        int n = Physics.OverlapSphereNonAlloc(pos, 4.2f, OverlapBuf, ~0, QueryTriggerInteraction.Ignore);
        RiverShark seen = null;
        for (int i = 0; i < n; i++)
        {
            var col = OverlapBuf[i];
            if (col == null)
                continue;
            var other = col.GetComponentInParent<RiverShark>();
            if (other == null || other == this || other.IsDead || other == seen)
                continue;
            seen = other;
            Vector3 d = pos - other.transform.position;
            d.y = 0f;
            float mag = d.magnitude;
            if (mag > 4f)
                continue;
            if (mag < 0.05f)
                d = transform.right;
            else
                d /= mag;
            float t = 1f - mag / 4f;
            steer += d * (t * t * 4.2f);
            if (mag < 2.2f)
                steer += d * ((2.2f - mag) * 2.4f);
        }
        return steer;
    }

    static int SolidMask()
    {
        int mask = ~0;
        int water = LayerMask.NameToLayer("Water");
        if (water >= 0)
            mask &= ~(1 << water);
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (ignore >= 0)
            mask &= ~(1 << ignore);
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0)
            mask &= ~(1 << ui);
        return mask;
    }

    bool IsPeer(Collider col)
    {
        if (col == null)
            return false;
        var s = col.GetComponentInParent<RiverShark>();
        return s != null && s != this;
    }

    bool IgnoreSolid(Collider col)
    {
        if (col == null || !col.enabled || col.isTrigger)
            return true;
        Transform t = col.transform;
        if (t == transform || t.IsChildOf(transform) || transform.IsChildOf(t))
            return true;
        if (col.GetComponentInParent<RiverShark>() == this)
            return true;
        if (col.GetComponentInParent<BoatWater>() != null)
            return true;
        if (col.GetComponentInParent<CharacterController>() != null)
            return true;
        if (col.GetComponentInParent<HorrorFirstPersonController>() != null)
            return true;
        return false;
    }

    static Vector3 PreyCarry(RaceActor prey, Vector3 flow)
    {
        if (prey != null && prey.Craft != null)
        {
            var rb = prey.Craft.IslandRootBody();
            if (rb != null)
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                if (v.sqrMagnitude > 0.01f)
                    return v;
            }
        }
        return flow;
    }

    static float Planar(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void Bite(RaceActor prey, Vector3 at)
    {
        var craft = prey.Craft != null ? prey.Craft : FindCraft(at);
        if (craft != null)
            BoatHull.SharkBite(craft, transform.position, 0.09f);
        _engaged = true;
    }

    static BoatPiece FindCraft(Vector3 at)
    {
        var cols = Physics.OverlapSphere(at, 2.4f, ~0, QueryTriggerInteraction.Ignore);
        BoatPiece best = null;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < cols.Length; i++)
        {
            var p = cols[i] != null ? cols[i].GetComponentInParent<BoatPiece>() : null;
            if (p == null)
                continue;
            float d = (p.transform.position - at).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = p;
            }
        }
        return best;
    }

    static Color ReadTint(Renderer r)
    {
        if (r == null)
            return Color.white;
        var mat = r.sharedMaterial;
        if (mat == null)
            return Color.white;
        if (mat.HasProperty("_BaseColor"))
            return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color"))
            return mat.GetColor("_Color");
        return mat.color;
    }

    static void WriteTint(Renderer r, Color c)
    {
        if (r == null)
            return;
        var mat = r.material;
        c.a = 1f;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", c);
        mat.color = c;
    }

    static void ForceOpaque(Renderer r)
    {
        if (r == null)
            return;
        var mat = r.material;
        mat.SetFloat("_Surface", 0f);
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 2000;
        if (mat.HasProperty("_BaseColor"))
        {
            var c = mat.GetColor("_BaseColor");
            c.a = 1f;
            mat.SetColor("_BaseColor", c);
        }
        var tint = mat.color;
        tint.a = 1f;
        mat.color = tint;
        r.material = mat;
    }

    void Flash(Vector3 point)
    {
        if (_rends == null)
            return;
        for (int i = 0; i < _rends.Length; i++)
        {
            if (_rends[i] == null)
                continue;
            Color baseC = _baseColors != null && i < _baseColors.Length ? _baseColors[i] : ReadTint(_rends[i]);
            WriteTint(_rends[i], Color.Lerp(baseC, Color.white, 0.45f));
        }
    }

    void Fade(float t)
    {
        t = Mathf.Clamp01(t);
        if (_rends == null)
            return;
        for (int i = 0; i < _rends.Length; i++)
        {
            if (_rends[i] == null)
                continue;
            Color baseC = _baseColors != null && i < _baseColors.Length ? _baseColors[i] : ReadTint(_rends[i]);
            Color c = Color.Lerp(baseC, baseC * 0.15f, t);
            c.a = 1f;
            WriteTint(_rends[i], c);
        }
    }
}
