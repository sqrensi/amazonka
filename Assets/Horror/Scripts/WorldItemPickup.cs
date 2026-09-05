using UnityEngine;

/// <summary>
/// Предмет, лежащий в мире (фонарь/пистолет, расставленные вручную в сцене).
/// При взгляде показывается подсказка рядом с ним; по нажатию F предмет
/// добавляется в инвентарь игрока, а объект в мире исчезает.
/// </summary>
public class WorldItemPickup : MonoBehaviour, IInteractable
{
    [Tooltip("Префаб предмета в руке, который получает игрок (FlashlightItem / PistolItem).")]
    [SerializeField] HeldItem itemPrefab;
    [Tooltip("Имя для подсказки. Если пусто — берётся из префаба предмета.")]
    [SerializeField] string displayName = "";
    [Tooltip("Точка, над которой рисуется подсказка. Если пусто — этот объект.")]
    [SerializeField] Transform promptAnchor;

    string ItemName =>
        !string.IsNullOrEmpty(displayName) ? displayName :
        (itemPrefab != null ? itemPrefab.DisplayName : "Предмет");

    public string GetPrompt() => $"Подобрать {ItemName}";

    public bool CanInteract(GameObject interactor)
    {
        return itemPrefab != null && interactor != null &&
               interactor.GetComponent<PlayerInventory>() != null;
    }

    public void Interact(GameObject interactor)
    {
        var inventory = interactor != null ? interactor.GetComponent<PlayerInventory>() : null;
        if (inventory == null || itemPrefab == null)
            return;

        if (inventory.AddItem(itemPrefab))
            Destroy(gameObject);
        // Если инвентарь полон — предмет остаётся лежать.
    }

    public Transform GetAnchor() => promptAnchor != null ? promptAnchor : transform;
}
