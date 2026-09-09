using UnityEngine;

/// <summary>
/// Всё, с чем игрок может взаимодействовать по близости + кнопке (F предметы, E босс):
/// подбираемые предметы, двери, рычаги и т.п.
/// <see cref="PlayerInteractor"/> наводится лучом из камеры, показывает подсказку
/// рядом с объектом и вызывает <see cref="Interact"/> по нажатию.
/// </summary>
public interface IInteractable
{
    /// <summary>Текст подсказки без клавиши, напр. "Подобрать Фонарь".</summary>
    string GetPrompt();

    /// <summary>Можно ли сейчас взаимодействовать (напр. хватает ли места в инвентаре).</summary>
    bool CanInteract(GameObject interactor);

    /// <summary>Выполнить взаимодействие.</summary>
    void Interact(GameObject interactor);

    /// <summary>Точка в мире, над которой рисуется подсказка (обычно сам предмет).</summary>
    Transform GetAnchor();

    /// <summary>Клавиша, которую показать в подсказке (напр. "F" или "E").</summary>
    string GetInteractKey();
}
