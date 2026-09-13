using UnityEngine;

/// <summary>
/// Забитый или ещё торчащий гвоздь между двумя деталями.
/// Незабитый можно подобрать.
/// </summary>
public class BoatNail : MonoBehaviour, IInteractable
{
    public BoatPiece A;
    public BoatPiece B;
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
        _joint = A.gameObject.AddComponent<FixedJoint>();
        _joint.connectedBody = rbB;
        _joint.breakForce = float.PositiveInfinity;
        _joint.breakTorque = float.PositiveInfinity;
        _joint.enableCollision = false;
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
    }

    void OnDestroy()
    {
        if (_joint != null)
            Destroy(_joint);
    }
}
