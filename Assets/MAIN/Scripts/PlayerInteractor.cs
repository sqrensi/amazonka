using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Прицел видит только слой взаимодействия и обычные предметы, не физические коллайдеры лодки.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] float pickupRadius = 2.4f;
    [SerializeField] LayerMask mask = ~0;

    readonly Collider[] _buffer = new Collider[32];
    readonly RaycastHit[] _hits = new RaycastHit[24];

    IInteractable _current;
    Component _currentComponent;
    Camera _cam;
    float _nextCheck;
    Vector3 _lastPos;
    Quaternion _lastLook;
    bool _maskReady;

    public IInteractable Current => _current;
    public Component CurrentComponent => _currentComponent;
    public bool HasTarget => _current != null && _currentComponent != null;

    void Awake()
    {
        _cam = GetComponentInChildren<Camera>();
        if (GetComponent<PickupPromptHUD>() == null)
            gameObject.AddComponent<PickupPromptHUD>();
        if (GetComponent<KillNoticeHUD>() == null)
            gameObject.AddComponent<KillNoticeHUD>();
        if (GetComponent<BoatBuildHud>() == null)
            gameObject.AddComponent<BoatBuildHud>();
        EnsureMask();
    }

    void EnsureMask()
    {
        if (_maskReady)
            return;
        BoatLayers.Ensure();
        mask = BoatLayers.InteractorMask();
        _maskReady = true;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame)
            TryRow();

        if (BoatOarStation.Active != null)
        {
            _current = null;
            _currentComponent = null;
            return;
        }

        EnsureMask();
        Transform look = _cam != null ? _cam.transform : transform;
        bool moved = (transform.position - _lastPos).sqrMagnitude > 0.0008f
            || Quaternion.Angle(_lastLook, look.rotation) > 0.8f;
        if (!moved && Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + 0.05f;
        _lastPos = transform.position;
        _lastLook = look.rotation;
        RefreshTarget();
    }

    void TryRow()
    {
        if (BoatOarStation.Active != null)
        {
            BoatOarStation.Toggle(gameObject, BoatOarStation.Active.Oar);
            return;
        }
        if (_current is BoatPiece piece)
            piece.TryRow(gameObject);
        else if (_current is BoatPart part && part.Piece != null)
            part.Piece.TryRow(gameObject);
    }

    void RefreshTarget()
    {
        _current = null;
        _currentComponent = null;
        if (_cam == null)
            _cam = GetComponentInChildren<Camera>();
        if (_cam == null)
            return;

        Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int hitCount = Physics.RaycastNonAlloc(ray, _hits, pickupRadius + 0.6f, mask, QueryTriggerInteraction.Collide);
        if (PickAimed(_hits, hitCount, preferSolid: true, out IInteractable aimed, out Component aimedComp)
            || PickAimed(_hits, hitCount, preferSolid: false, out aimed, out aimedComp))
        {
            ResolveTarget(aimed, aimedComp, out _current, out _currentComponent);
            return;
        }

        int sphere = Physics.SphereCastNonAlloc(ray, 0.12f, _hits, pickupRadius, mask, QueryTriggerInteraction.Collide);
        if (PickAimed(_hits, sphere, preferSolid: true, out aimed, out aimedComp)
            || PickAimed(_hits, sphere, preferSolid: false, out aimed, out aimedComp))
        {
            ResolveTarget(aimed, aimedComp, out _current, out _currentComponent);
            return;
        }

        Vector3 origin = transform.position;
        Vector3 aim = _cam.transform.forward;
        Vector3 eye = _cam.transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, pickupRadius, _buffer, mask, QueryTriggerInteraction.Collide);
        Collider bestCol = null;
        float bestScore = -1f;
        for (int i = 0; i < count; i++)
        {
            var col = _buffer[i];
            if (col == null)
                continue;
            Vector3 pt = BoatBuildUtil.ClosestPoint(col, eye);
            Vector3 to = pt - eye;
            float dist = to.magnitude;
            if (dist < 0.01f || dist > pickupRadius)
                continue;
            float align = Vector3.Dot(aim, to / dist);
            if (align < 0.72f)
                continue;
            float score = align * 4f - dist * 0.15f;
            if (col.isTrigger)
                score -= 0.35f;
            if (score > bestScore)
            {
                bestScore = score;
                bestCol = col;
            }
        }
        if (bestCol != null && TryHost(bestCol, out IInteractable interactable, out Component component))
            ResolveTarget(interactable, component, out _current, out _currentComponent);
    }

    bool PickAimed(RaycastHit[] hits, int count, bool preferSolid, out IInteractable interactable, out Component component)
    {
        interactable = null;
        component = null;
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var col = hits[i].collider;
            if (col == null)
                continue;
            if (preferSolid && col.isTrigger)
                continue;
            if (hits[i].distance < bestDist)
            {
                bestDist = hits[i].distance;
                best = i;
            }
        }
        return best >= 0 && TryHost(hits[best].collider, out interactable, out component);
    }

    void ResolveTarget(IInteractable interactable, Component component, out IInteractable resolved, out Component resolvedComp)
    {
        resolved = interactable;
        resolvedComp = component;
        BoatPiece piece = interactable as BoatPiece;
        if (piece == null && interactable is BoatPart part)
            piece = part.Piece;
        if (piece == null)
            return;
        if (!piece.IsLockedInBoat())
            return;
        if (piece.IslandRowOar(gameObject) is BoatPiece oar)
        {
            resolved = oar;
            resolvedComp = oar;
        }
    }

    bool TryHost(Collider col, out IInteractable interactable, out Component component)
    {
        interactable = null;
        component = null;
        if (col == null)
            return false;
        Transform t = col.transform;
        if (t == transform || t.IsChildOf(transform))
            return false;

        BoatPart part = col.GetComponent<BoatPart>();
        if (part != null && part.Piece != null)
        {
            var piece = part.Piece;
            if (!piece.CanInteract(gameObject))
                return false;
            if (string.IsNullOrEmpty(piece.GetPrompt()))
                return false;
            interactable = piece;
            component = piece;
            return true;
        }

        InteractLink link = col.GetComponent<InteractLink>();
        if (link != null && link.Host is IInteractable linked)
        {
            if (!linked.CanInteract(gameObject))
                return false;
            if (string.IsNullOrEmpty(linked.GetPrompt()))
                return false;
            interactable = linked;
            component = link.Host;
            return true;
        }

        Component host = col.GetComponent<BoatNail>();
        if (host == null)
            host = col.GetComponent<HeldItem>();
        if (host == null)
            host = col.GetComponent<WorldItemPickup>();
        if (host == null)
            host = col.GetComponent<BoatPiece>();
        if (host == null)
            host = col.GetComponentInParent<BoatNail>();
        if (host == null)
            host = col.GetComponentInParent<HeldItem>();
        if (host == null)
            host = col.GetComponentInParent<WorldItemPickup>();
        if (host == null)
            host = col.GetComponentInParent<BoatPiece>();
        if (host is not IInteractable candidate)
            return false;
        if (!candidate.CanInteract(gameObject))
            return false;
        if (string.IsNullOrEmpty(candidate.GetPrompt()))
            return false;
        component = host;
        interactable = candidate;
        return true;
    }

    public void OnInteract(InputValue value)
    {
        if (!value.isPressed)
            return;
        if (BoatOarStation.Active != null)
            return;
        if (_current != null && _current.CanInteract(gameObject))
            _current.Interact(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
