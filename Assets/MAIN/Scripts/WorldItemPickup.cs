using UnityEngine;

/// <summary>
/// Старый мировой pickup. Если на объекте уже есть <see cref="HeldItem"/> — подбираем его.
/// Иначе (устаревшие префабы в сцене) создаём предмет из ссылки.
/// </summary>
public class WorldItemPickup : MonoBehaviour, IInteractable
{
    [SerializeField] HeldItem itemPrefab;
    [SerializeField] string displayName = "";
    [SerializeField] Transform promptAnchor;

    HeldItem _selfItem;

    string ItemName =>
        !string.IsNullOrEmpty(displayName) ? displayName :
        (_selfItem != null ? _selfItem.DisplayName :
        (itemPrefab != null ? itemPrefab.DisplayName : "Item"));

    void Awake()
    {
        _selfItem = GetComponent<HeldItem>();
        if (_selfItem != null)
            enabled = false;
        else
            BoatLayers.BindPickup(this, true);
    }

    public string GetPrompt() => _selfItem != null ? _selfItem.GetPrompt() : $"Pickup {ItemName}";

    public bool CanInteract(GameObject interactor)
    {
        if (_selfItem != null)
            return _selfItem.CanInteract(interactor);
        return itemPrefab != null && interactor != null &&
               interactor.GetComponent<PlayerInventory>() != null;
    }

    public void Interact(GameObject interactor)
    {
        var inventory = interactor != null ? interactor.GetComponent<PlayerInventory>() : null;
        if (inventory == null)
            return;

        if (_selfItem != null)
        {
            inventory.PickupExisting(_selfItem);
            return;
        }

        if (itemPrefab != null && inventory.AddItem(itemPrefab))
            Destroy(gameObject);
    }

    public Transform GetAnchor() =>
        _selfItem != null ? _selfItem.GetAnchor() :
        (promptAnchor != null ? promptAnchor : transform);

    public string GetInteractKey() => "F";
}
