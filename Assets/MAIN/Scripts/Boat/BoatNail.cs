using UnityEngine;

/// <summary>
/// Забитый или ещё торчащий гвоздь между двумя деталями.
/// </summary>
public class BoatNail : MonoBehaviour
{
    public BoatPiece A;
    public BoatPiece B;
    public bool Driven;
    FixedJoint _joint;

    public void Drive()
    {
        if (Driven || A == null || B == null)
            return;
        var rbA = A.Body;
        var rbB = B.Body;
        if (rbA == null || rbB == null)
            return;

        _joint = A.gameObject.AddComponent<FixedJoint>();
        _joint.connectedBody = rbB;
        _joint.breakForce = 14000f;
        _joint.breakTorque = 11000f;
        _joint.enableCollision = false;
        Driven = true;
        Hide();
    }

    public void Reconnect(BoatPiece newA, BoatPiece newB)
    {
        if (_joint != null)
        {
            Destroy(_joint);
            _joint = null;
        }
        A = newA;
        B = newB;
        Driven = false;
        if (A != null && B != null)
            Drive();
    }

    void Hide()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = false;
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null)
                cols[i].enabled = false;
        gameObject.SetActive(false);
    }

    void OnJointBreak(float _)
    {
        Driven = false;
        _joint = null;
    }

    void OnDestroy()
    {
        if (_joint != null)
            Destroy(_joint);
    }
}
