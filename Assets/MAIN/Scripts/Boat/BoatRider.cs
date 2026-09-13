using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Посадка на собранную (гвоздями) лодку. WASD — гребля, Space — сойти.
/// </summary>
public class BoatRider : MonoBehaviour
{
    public static BoatRider Active { get; private set; }

    HorrorFirstPersonController _move;
    CharacterController _cc;
    Rigidbody _boat;
    Vector3 _localSeat;

    public static void Board(GameObject player, BoatPiece piece)
    {
        if (player == null || piece == null)
            return;
        var rider = player.GetComponent<BoatRider>();
        if (rider == null)
            rider = player.AddComponent<BoatRider>();
        rider.Attach(piece);
    }

    public static void Leave()
    {
        if (Active != null)
            Active.Detach();
    }

    void Attach(BoatPiece piece)
    {
        _move = GetComponent<HorrorFirstPersonController>();
        _cc = GetComponent<CharacterController>();
        _boat = piece.IslandRootBody();
        if (_boat == null)
            return;

        Active = this;
        if (_move != null)
            _move.MovementLocked = true;
        if (_cc != null)
            _cc.enabled = false;

        _localSeat = _boat.transform.InverseTransformPoint(transform.position);
        _localSeat.y = Mathf.Max(_localSeat.y, 0.55f);
        transform.SetParent(_boat.transform, true);
        transform.localPosition = _localSeat;
        BoatBuildHud.Hint("WASD paddle   Space leave");
    }

    void Detach()
    {
        transform.SetParent(null, true);
        if (_cc != null)
            _cc.enabled = true;
        if (_move != null)
            _move.MovementLocked = false;
        if (_boat != null)
            transform.position = _boat.transform.TransformPoint(_localSeat + Vector3.right * 0.7f + Vector3.up * 0.2f);
        if (Active == this)
            Active = null;
        BoatBuildHud.Hint("");
        Destroy(this);
    }

    void Update()
    {
        if (_boat == null)
        {
            Detach();
            return;
        }

        if (KeyboardJump())
        {
            Detach();
            return;
        }

        if (_move == null)
            return;
        Vector2 input = _move.MoveInput;
        Vector3 force = _boat.transform.forward * input.y * 14f + _boat.transform.right * input.x * 7f;
        _boat.AddForce(force, ForceMode.Acceleration);
        _boat.AddTorque(_boat.transform.up * input.x * 2.4f, ForceMode.Acceleration);
        transform.localPosition = _localSeat;
    }

    static bool KeyboardJump()
    {
        var kb = Keyboard.current;
        return kb != null && kb.spaceKey.wasPressedThisFrame;
    }
}
