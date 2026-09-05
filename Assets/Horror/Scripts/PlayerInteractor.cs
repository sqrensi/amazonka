using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ищет ближайший интерактивный объект <see cref="IInteractable"/> рядом с игроком
/// (по радиусу, без наведения лучом). Держит текущую цель для показа подсказки в HUD
/// и по нажатию F выполняет взаимодействие (подбор предмета и т.п.).
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Tooltip("Радиус, в котором можно подобрать/использовать предмет рядом.")]
    [SerializeField] float pickupRadius = 2.2f;
    [Tooltip("Слои, на которых ищем интерактивные объекты.")]
    [SerializeField] LayerMask mask = ~0;

    readonly Collider[] _buffer = new Collider[32];

    IInteractable _current;
    Component _currentComponent;

    /// <summary>Ближайшая интерактивная цель в радиусе (или null).</summary>
    public IInteractable Current => _current;
    /// <summary>MonoBehaviour текущей цели (для доступа к Transform/якорю).</summary>
    public Component CurrentComponent => _currentComponent;
    public bool HasTarget => _current != null && _currentComponent != null;

    void Update()
    {
        RefreshTarget();
    }

    void RefreshTarget()
    {
        _current = null;
        _currentComponent = null;

        Vector3 origin = transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, pickupRadius, _buffer, mask, QueryTriggerInteraction.Collide);

        float bestSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var col = _buffer[i];
            if (col == null)
                continue;

            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.CanInteract(gameObject))
                continue;

            var component = interactable as Component;
            if (component == null)
                continue;

            float sqr = (component.transform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                _current = interactable;
                _currentComponent = component;
            }
        }
    }

    // Ввод (PlayerInput, SendMessages) — привязано к клавише F.
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
