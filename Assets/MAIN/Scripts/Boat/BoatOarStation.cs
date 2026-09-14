using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Прибитое весло: R сесть, ЛКМ грести, Q/E рулить. Игрок привязан к лодке.
/// </summary>
public class BoatOarStation : MonoBehaviour
{
    public static BoatOarStation Active { get; private set; }

    static readonly string[] StrokeParts = { "Shaft", "Neck", "Blade", "Collar" };

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
        if (_move != null)
        {
            _move.MovementLocked = true;
            _move.BindBoatFollow();
        }
        CacheStroke();
        BoatBuildHud.Hint("LMB row   Q/E steer   R stop", 4f);
    }

    void Stop()
    {
        ResetStroke();
        if (_move != null)
            _move.MovementLocked = false;
        if (Active == this)
            Active = null;
        BoatBuildHud.Hint("");
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
        Quaternion swing = Quaternion.Euler(angle, BoatPaddle.Steer * 0.55f, 0f);
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
            Stop();
            return;
        }
        if (Vector3.Distance(transform.position, _oar.transform.position) > 3.2f)
        {
            Stop();
            return;
        }
        if (_move != null && _move.IsSwimming)
        {
            Stop();
            return;
        }

        BoatPaddle.TickSteer(Time.deltaTime);
        bool rowing = Mouse.current != null && Mouse.current.leftButton.isPressed
            && Cursor.lockState == CursorLockMode.Locked;
        if (rowing)
            _stroke += Time.deltaTime * 3.1f;
        else
            _stroke = 0f;
        float target = rowing ? Mathf.Sin(_stroke) * 28f : 0f;
        _angle = Mathf.Lerp(_angle, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        ApplyStroke(_angle);
    }

    void FixedUpdate()
    {
        if (_oar == null)
            return;
        bool rowing = Mouse.current != null && Mouse.current.leftButton.isPressed
            && Cursor.lockState == CursorLockMode.Locked;
        if (!rowing)
            return;
        if (!BoatPaddle.PieceBladeInWater(_oar))
            return;
        Rigidbody boat = _oar.IslandRootBody();
        Transform blade = _oar.transform.Find("Blade");
        Vector3 at = blade != null ? blade.position : _oar.transform.position;
        Vector3 dir = BoatPaddle.SteerDir(transform.forward);
        BoatPaddle.Push(boat, dir, at, 42f);
    }
}
