using UnityEngine;

/// <summary>
/// Забитый или ещё торчащий гвоздь между двумя деталями.
/// Незабитый можно подобрать.
/// </summary>
public class BoatNail : MonoBehaviour, IInteractable
{
    public BoatPiece A;
    public BoatPiece B;
    public Vector3 Aim;
    public bool Driven;
    FixedJoint _joint;

    public string GetPrompt() => Driven ? "" : "Pick up Nail";
    public string GetInteractKey() => "F";
    public Transform GetAnchor() => transform;

    public bool CanInteract(GameObject interactor)
    {
        if (Driven || !isActiveAndEnabled || interactor == null)
            return false;
        var held = GetComponentInParent<HeldItem>();
        if (held != null && held.IsCarried)
            return false;
        var inv = interactor.GetComponent<PlayerInventory>();
        return inv != null && inv.HasFreeSlot();
    }

    public void Interact(GameObject interactor)
    {
        if (Driven)
            return;
        var inv = interactor.GetComponent<PlayerInventory>();
        if (inv == null)
            return;

        if (A != null)
            A.UnregisterNail(this);
        if (B != null)
            B.UnregisterNail(this);
        A = null;
        B = null;

        transform.SetParent(null, true);
        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null)
                continue;
            cols[i].isTrigger = false;
            cols[i].enabled = true;
        }

        Destroy(this);
        var item = gameObject.GetComponent<NailItem>();
        if (item == null)
            item = gameObject.AddComponent<NailItem>();
        item.SetDisplayName("Nail");
        if (!inv.PickupExisting(item))
            return;
    }

    public void DisconnectJointKeepState()
    {
        if (_joint != null)
        {
            Destroy(_joint);
            _joint = null;
        }
    }

    public void SplitSeam()
    {
        DisconnectJointKeepState();
        if (A != null)
            A.UnregisterNail(this);
        if (B != null)
            B.UnregisterNail(this);
        A = null;
        B = null;
        Driven = false;
        Destroy(gameObject);
    }

    public void DropLoose()
    {
        DisconnectJointKeepState();
        if (A != null)
            A.UnregisterNail(this);
        if (B != null)
            B.UnregisterNail(this);
        A = null;
        B = null;
        Driven = false;

        transform.SetParent(null, true);
        gameObject.SetActive(true);

        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = true;

        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null)
                continue;
            cols[i].isTrigger = false;
            cols[i].enabled = true;
        }

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.detectCollisions = true;
        rb.mass = 0.08f;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 0.4f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        BoatBuildUtil.StopMotion(rb);
        rb.AddForce(Vector3.down * 0.4f, ForceMode.VelocityChange);

        Destroy(this);
        var item = gameObject.GetComponent<NailItem>();
        if (item == null)
            item = gameObject.AddComponent<NailItem>();
        item.SetDisplayName("Nail");
    }

    public void Drive()
    {
        if (Driven || A == null || B == null)
            return;
        var rbA = A.Body;
        var rbB = B.Body;
        if (rbA == null || rbB == null)
            return;

        if (_joint != null)
            Destroy(_joint);

        BoatBuildUtil.SnapTogether(A, B, Aim);
        if (rbA != null)
            BoatBuildUtil.StopMotion(rbA);
        if (rbB != null)
            BoatBuildUtil.StopMotion(rbB);

        _joint = A.gameObject.AddComponent<FixedJoint>();
        _joint.connectedBody = rbB;
        _joint.breakForce = 14000f;
        _joint.breakTorque = 4000f;
        _joint.enableCollision = A.Kind != BoatPieceKind.Oar && B.Kind != BoatPieceKind.Oar;
        _joint.enablePreprocessing = true;
        Driven = true;
        Hide();
    }

    public void RebuildJoint()
    {
        if (A == null || B == null)
            return;
        if (_joint != null)
        {
            Destroy(_joint);
            _joint = null;
        }
        Driven = false;
        Drive();
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

    public FixedJoint Joint => _joint;

    public void SetBreakLimit(float force, float torque)
    {
        if (_joint == null)
            return;
        _joint.breakForce = force;
        _joint.breakTorque = torque;
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
        _joint = null;
        if (A != null)
            BoatHull.JointBroke(A);
    }

    void OnDestroy()
    {
        if (_joint != null)
            Destroy(_joint);
    }
}
