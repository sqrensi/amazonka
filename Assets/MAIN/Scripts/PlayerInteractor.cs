using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Цель взаимодействия — то, на что смотрит прицел. Иначе ближайший объект у центра экрана.
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
    }

    void Update()
    {
        RefreshTarget();
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
        int bestHit = -1;
        float bestHitDist = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            if (!TryInteractable(_hits[i].collider, out _, out _))
                continue;
            if (_hits[i].distance < bestHitDist)
            {
                bestHitDist = _hits[i].distance;
                bestHit = i;
            }
        }

        if (bestHit < 0)
        {
            int sphere = Physics.SphereCastNonAlloc(ray, 0.12f, _hits, pickupRadius, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < sphere; i++)
            {
                if (!TryInteractable(_hits[i].collider, out _, out _))
                    continue;
                if (_hits[i].distance < bestHitDist)
                {
                    bestHitDist = _hits[i].distance;
                    bestHit = i;
                }
            }
        }

        if (bestHit >= 0 && TryInteractable(_hits[bestHit].collider, out IInteractable aimed, out Component aimedComp))
        {
            ResolveTarget(aimed, aimedComp, out _current, out _currentComponent);
            return;
        }

        Vector3 origin = transform.position;
        Vector3 aim = _cam.transform.forward;
        Vector3 eye = _cam.transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, pickupRadius, _buffer, mask, QueryTriggerInteraction.Collide);
        float bestScore = -1f;
        for (int i = 0; i < count; i++)
        {
            if (!TryInteractable(_buffer[i], out IInteractable interactable, out Component component))
                continue;
            ResolveTarget(interactable, component, out interactable, out component);
            Vector3 to = PromptWorld(interactable, component) - eye;
            float dist = to.magnitude;
            if (dist < 0.01f || dist > pickupRadius)
                continue;
            float align = Vector3.Dot(aim, to / dist);
            if (align < 0.72f)
                continue;
            float score = align * 4f - dist * 0.15f;
            if (score > bestScore)
            {
                bestScore = score;
                _current = interactable;
                _currentComponent = component;
            }
        }
    }

    void ResolveTarget(IInteractable interactable, Component component, out IInteractable resolved, out Component resolvedComp)
    {
        resolved = interactable;
        resolvedComp = component;
        if (interactable is BoatPiece piece)
        {
            var lead = piece.IslandLeader();
            if (lead != null)
            {
                resolved = lead;
                resolvedComp = lead;
            }
        }
    }

    static Vector3 PromptWorld(IInteractable interactable, Component component)
    {
        Transform a = interactable != null ? interactable.GetAnchor() : null;
        if (a != null)
            return a.position;
        return component != null ? component.transform.position : Vector3.zero;
    }

    bool TryInteractable(Collider col, out IInteractable interactable, out Component component)
    {
        interactable = null;
        component = null;
        if (col == null)
            return false;
        Transform t = col.transform;
        if (t == transform || t.IsChildOf(transform))
            return false;

        var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
        for (int b = 0; b < behaviours.Length; b++)
        {
            if (behaviours[b] is not IInteractable candidate)
                continue;
            if (!candidate.CanInteract(gameObject))
                continue;
            if (string.IsNullOrEmpty(candidate.GetPrompt()))
                continue;
            component = behaviours[b];
            interactable = candidate;
            return true;
        }
        return false;
    }

    public void OnInteract(InputValue value)
    {
        if (!value.isPressed)
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
