using UnityEngine;

/// <summary>
/// Прибитое весло: R сесть/выйти, WASD грести. Уключина стоит, древко качается.
/// W/S вдоль весла (локальный +X, перпендикуляр к древку), A/D поворот, вместе — диагональ.
/// </summary>
public class BoatOarStation : MonoBehaviour
{
    public static BoatOarStation Active { get; private set; }

    static readonly string[] StrokeParts = { "Shaft", "ShaftCol", "Neck", "Blade", "Collar" };

    HorrorFirstPersonController _move;
    BoatPiece _oar;
    Rigidbody _boundRoot;
    float _stroke;
    float _pitch;
    Transform[] _parts;
    Vector3[] _restPos;
    Quaternion[] _restRot;
    Vector3 _pivot;

    public BoatPiece Oar => _oar;

    public static bool IsUsing(BoatPiece oar)
    {
        return Active != null && Active._oar == oar;
    }

    public static void Abort(string hint = null)
    {
        if (Active == null)
            return;
        if (!string.IsNullOrEmpty(hint))
            BoatBuildHud.Hint(hint, 2f);
        Active.Stop();
    }

    public static void AbortIfIsland(BoatPiece piece, string hint = null)
    {
        if (Active == null || Active._oar == null)
            return;
        if (!Active._oar.OarIsMounted())
            Active.FinishRow(null, string.IsNullOrEmpty(hint) ? "Oar came off" : hint);
    }

    void FinishRow(BoatPiece stayOn, string hint = null)
    {
        if (_move != null)
        {
            if (stayOn != null)
                _move.BindBoatFollow(stayOn);
            else
            {
                var hull = StandPiece();
                if (hull != null)
                    _move.BindBoatFollow(hull);
            }
        }
        Abort(hint);
    }

    public static void Toggle(GameObject player, BoatPiece oar)
    {
        if (player == null || oar == null)
            return;
        if (Active != null && Active._oar == oar)
        {
            Active.Stop();
            return;
        }
        if (Active != null)
            Active.Stop();
        var station = player.GetComponent<BoatOarStation>();
        if (station == null)
            station = player.AddComponent<BoatOarStation>();
        station.StartUse(oar);
    }

    void StartUse(BoatPiece oar)
    {
        _oar = oar;
        _move = GetComponent<HorrorFirstPersonController>();
        Active = this;
        var inv = GetComponent<PlayerInventory>();
        if (inv != null)
            inv.Holster();
        if (_move != null)
        {
            _move.MovementLocked = true;
            var hull = oar.IslandLeader() ?? oar;
            _move.BindBoatFollow(hull);
            _boundRoot = hull.IslandRootBody();
            if (_move.TryGetComponent(out CharacterController cc))
                cc.enabled = false;
        }
        CacheStroke();
        BoatBuildHud.Clear();
    }

    void Stop()
    {
        ResetStroke();
        if (_move != null)
        {
            _move.MovementLocked = false;
            if (_move.TryGetComponent(out CharacterController cc))
                cc.enabled = true;
        }
        if (Active == this)
            Active = null;
        BoatBuildHud.Clear();
        Destroy(this);
    }

    void CacheStroke()
    {
        if (_oar == null)
            return;
        Transform pin = _oar.transform.Find("OarlockPin");
        _pivot = pin != null ? pin.localPosition : new Vector3(0f, 0f, 0.2f);
        _parts = new Transform[StrokeParts.Length];
        _restPos = new Vector3[StrokeParts.Length];
        _restRot = new Quaternion[StrokeParts.Length];
        for (int i = 0; i < StrokeParts.Length; i++)
        {
            Transform t = _oar.transform.Find(StrokeParts[i]);
            _parts[i] = t;
            if (t == null)
                continue;
            _restPos[i] = t.localPosition;
            _restRot[i] = t.localRotation;
        }
    }

    void ResetStroke()
    {
        _pitch = 0f;
        ApplyStroke(0f, 0f);
    }

    void ApplyStroke(float pitch, float yaw)
    {
        if (_parts == null || _oar == null)
            return;
        Quaternion swing = Quaternion.Euler(pitch, yaw, 0f);
        for (int i = 0; i < _parts.Length; i++)
        {
            Transform t = _parts[i];
            if (t == null)
                continue;
            Vector3 off = _restPos[i] - _pivot;
            t.localPosition = _pivot + swing * off;
            t.localRotation = swing * _restRot[i];
        }
    }

    void Update()
    {
        if (_oar == null || !_oar.isActiveAndEnabled)
        {
            FinishRow(null, "Oar came off");
            return;
        }
        if (!_oar.OarIsMounted())
        {
            FinishRow(StandPiece(), "Oar came off");
            return;
        }

        var stand = StandPiece();
        if (stand != null && !stand.SharesIslandWith(_oar))
        {
            FinishRow(stand);
            return;
        }

        if (HullGoingUnder())
        {
            FinishRow(null, "Boat going under");
            return;
        }

        var hull = _oar.IslandLeader() ?? _oar;
        var root = hull.IslandRootBody();
        if (_move != null && root != null && root != _boundRoot)
        {
            _boundRoot = root;
            _move.BindBoatFollow(hull);
        }

        Vector2 input = _move != null ? _move.MoveInput : Vector2.zero;
        BoatPaddle.TickSteer(Time.deltaTime, input.x);
        bool drive = Mathf.Abs(input.y) > 0.08f;
        if (drive)
            _stroke += Time.deltaTime * 11.2f;
        else
            _stroke = Mathf.MoveTowards(_stroke, 0f, Time.deltaTime * 4f);
        float targetPitch = drive ? Mathf.Sin(_stroke) * 11f * Mathf.Sign(input.y) : 0f;
        _pitch = Mathf.Lerp(_pitch, targetPitch, 1f - Mathf.Exp(-8f * Time.deltaTime));
        ApplyStroke(_pitch, BoatPaddle.Steer * 0.4f);
    }

    bool HullGoingUnder()
    {
        var lead = _oar != null ? _oar.IslandLeader() ?? _oar : null;
        if (lead == null)
            return false;
        return lead.HullFlood >= 0.88f || lead.DeckSubmerged();
    }

    BoatPiece StandPiece()
    {
        Vector3 origin = transform.position + Vector3.up * 0.45f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 2.8f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        BoatPiece found = null;
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.distance >= best)
                continue;
            if (hit.collider.GetComponentInParent<BoatWater>() != null)
                continue;
            var piece = hit.collider.GetComponentInParent<BoatPiece>();
            if (piece == null || piece.Kind == BoatPieceKind.Oar)
                continue;
            best = hit.distance;
            found = piece;
        }
        return found;
    }

    void FixedUpdate()
    {
        if (_oar == null)
            return;
        Vector2 input = _move != null ? _move.MoveInput : Vector2.zero;
        bool drive = Mathf.Abs(input.y) > 0.08f;
        bool steer = Mathf.Abs(input.x) > 0.08f;

        var hull = _oar.IslandLeader() ?? _oar;
        if (drive && !steer)
            BoatPaddle.DampIslandYaw(hull, 6.5f);
        if (steer)
            BoatPaddle.YawIsland(hull, input.x * 5.4f);
        if (!drive)
            return;
        Vector3 fwd = hull.CraftForward();
        if (fwd.sqrMagnitude < 0.0001f)
            return;
        float pulse = 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(_stroke));
        BoatPaddle.PushIsland(hull, fwd * (input.y * 9f * pulse), ForceMode.Acceleration);
    }
}
