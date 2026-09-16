using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Прибитое весло: R сесть/выйти, WASD грести. Пока гребёшь — без подсказок и без предметов.
/// </summary>
public class BoatOarStation : MonoBehaviour
{
    public static BoatOarStation Active { get; private set; }

    static readonly string[] StrokeParts = { "Shaft", "ShaftCol", "Neck", "Blade", "Collar" };

    HorrorFirstPersonController _move;
    BoatPiece _oar;
    float _stroke;
    float _angle;
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
        if (Active == null || Active._oar == null || piece == null)
            return;
        if (piece == Active._oar || piece.SharesIslandWith(Active._oar))
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
            _move.BindBoatFollow();
        }
        CacheStroke();
        BoatBuildHud.Clear();
    }

    void Stop()
    {
        ResetStroke();
        if (_move != null)
            _move.MovementLocked = false;
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
        _angle = 0f;
        ApplyStroke(0f);
    }

    void ApplyStroke(float angle)
    {
        if (_parts == null || _oar == null)
            return;
        Quaternion swing = Quaternion.Euler(angle, BoatPaddle.Steer * 0.35f, 0f);
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
            Abort("Oar came off");
            return;
        }
        if (!_oar.HasDrivenNail())
        {
            Abort("Oar came off");
            return;
        }
        if (_oar.HullFlood >= 0.2f)
        {
            Abort("Boat is flooding");
            return;
        }
        if (Vector3.Distance(transform.position, _oar.transform.position) > 3.2f)
        {
            Abort();
            return;
        }
        if (_move != null && _move.IsSwimming)
        {
            Abort();
            return;
        }

        Vector2 input = _move != null ? _move.MoveInput : Vector2.zero;
        BoatPaddle.TickSteer(Time.deltaTime, input.x);
        bool rowing = Mathf.Abs(input.y) > 0.08f;
        if (rowing)
            _stroke += Time.deltaTime * 11.2f;
        else
            _stroke = Mathf.MoveTowards(_stroke, 0f, Time.deltaTime * 4f);
        float amp = 7.2f;
        float target = rowing ? Mathf.Sin(_stroke) * amp * Mathf.Sign(input.y) : 0f;
        _angle = Mathf.Lerp(_angle, target, 1f - Mathf.Exp(-7.5f * Time.deltaTime));
        ApplyStroke(_angle);
    }

    void FixedUpdate()
    {
        if (_oar == null)
            return;
        Vector2 input = _move != null ? _move.MoveInput : Vector2.zero;
        Rigidbody boat = _oar.IslandRootBody();
        if (boat == null)
            return;

        Vector3 av = boat.angularVelocity;
        Vector3 yaw = Vector3.Project(av, Vector3.up);
        Vector3 roll = av - yaw;
        boat.AddTorque(-roll * 5.5f, ForceMode.Acceleration);

        if (Mathf.Abs(input.x) > 0.08f)
            boat.AddTorque(Vector3.up * (input.x * 5.2f), ForceMode.Acceleration);

        if (Mathf.Abs(input.y) < 0.08f)
            return;
        if (!BoatPaddle.PieceBladeInWater(_oar))
            return;
        Vector3 fwd = boat.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f)
            return;
        fwd.Normalize();
        float pulse = 0.62f + 0.38f * Mathf.Abs(Mathf.Sin(_stroke));
        boat.AddForce(fwd * (input.y * 155f * pulse), ForceMode.Force);
    }
}
