using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// HUD взаимодействия и инвентаря (строится из кода, как и <see cref="PlayerHUD"/>):
///  - подсказка "[F] Подобрать ..." рядом с предметом, к которому подошёл игрок;
///  - панель инвентаря по кнопке Tab (5 слотов) с drag&drop; при открытии курсор
///    разблокируется и управление приостанавливается;
///  - рулетка апгрейда: перетащи предмет в спец-слот, выставь шанс, нажми "АПГРЕЙД".
///    Крутится стрелка; при "заходе" в зелёный сектор выпадает улучшенный предмет
///    (фонарь -> пистолет), при провале поставленный предмет пропадает.
///
/// Перетаскивание/слайдер/кнопка обрабатываются вручную через мышь (без EventSystem).
/// </summary>
public class InteractionHUD : MonoBehaviour
{
    [SerializeField] PlayerInteractor interactor;
    [SerializeField] PlayerInventory inventory;
    [SerializeField] Camera playerCamera;
    [Tooltip("Контроллер игрока — на время инвентаря отключается (стоп камеры/движения).")]
    [SerializeField] HorrorFirstPersonController controller;

    [Header("Interact key label")]
    [SerializeField] string interactKey = "F";

    const int None = int.MinValue;
    const int UpgradeSlot = -2;
    const float SliderWidth = 300f;

    Font _font;
    RectTransform _root;

    // Prompt.
    GameObject _promptGO;
    RectTransform _promptRect;
    Text _promptText;

    // Inventory.
    GameObject _inventoryPanel;
    RectTransform[] _slotCells;
    Image[] _slotImages;
    Text[] _slotLabels;
    bool _inventoryOpen;

    // Drag&drop.
    int _dragFrom = None;
    RectTransform _dragGhost;
    bool _draggingSlider;

    // Roulette.
    HeldItem _upgradeItem;
    RectTransform _upgradeCellRect;
    Image _upgradeCellImage;
    Text _upgradeCellLabel;
    Image _wheelSuccessArc;
    RectTransform _needle;
    Text _chanceText;
    Text _resultText;
    RectTransform _sliderTrack;
    RectTransform _sliderHandle;
    Image _upgradeButtonImage;
    RectTransform _upgradeButtonRect;
    float _chance = 0.5f;

    bool _spinning;
    float _spinElapsed;
    float _spinDuration = 2.8f;
    float _spinStartAngle;
    float _spinEndAngle;
    bool _pendingSuccess;

    static Sprite _circleSprite;

    static readonly Color CellEmpty = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color CellFilled = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color CellSelected = new Color(0.95f, 0.8f, 0.35f, 0.85f);
    static readonly Color WheelFail = new Color(0.65f, 0.15f, 0.12f, 0.9f);
    static readonly Color WheelSuccess = new Color(0.25f, 0.7f, 0.3f, 0.95f);

    void Awake()
    {
        if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (controller == null) controller = GetComponent<HorrorFirstPersonController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();

        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null)
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Убираем ранее существующий Canvas (напр. запечённый в префаб) — против дублей интерфейса.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c.name == "InteractionHUD_Canvas")
                Destroy(c.gameObject);
        }

        BuildUI();
    }

    void OnEnable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged += RefreshInventory;
    }

    void OnDisable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= RefreshInventory;
    }

    void Start()
    {
        RefreshInventory();
        RefreshRoulette();
    }

    void Update()
    {
        if (_inventoryOpen)
            UpdateInventoryMouse();
        if (_spinning)
            UpdateSpin();
    }

    void LateUpdate() => UpdatePrompt();

    // ------------------------------------------------------------- Inventory open/close

    public void OnInventory(InputValue value)
    {
        if (value.isPressed)
            ToggleInventory();
    }

    void ToggleInventory()
    {
        _inventoryOpen = !_inventoryOpen;
        if (_inventoryPanel != null)
            _inventoryPanel.SetActive(_inventoryOpen);

        if (_inventoryOpen)
        {
            RefreshInventory();
            RefreshRoulette();
            if (controller != null)
                controller.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            CancelDrag();
            _draggingSlider = false;
            if (controller != null)
                controller.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ------------------------------------------------------------- Mouse (drag / slider / button)

    void UpdateInventoryMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null || _spinning)
            return;

        Vector2 pos = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (RectContains(_sliderTrack, pos))
            {
                _draggingSlider = true;
                SetChanceFromMouse(pos);
            }
            else if (RectContains(_upgradeButtonRect, pos))
            {
                TryStartUpgrade();
            }
            else
            {
                int slot = SlotUnderPoint(pos);
                if (HasItemAt(slot))
                    BeginDrag(slot, pos);
            }
        }

        if (_draggingSlider)
            SetChanceFromMouse(pos);

        if (_dragFrom != None && _dragGhost != null)
            _dragGhost.position = pos;

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            _draggingSlider = false;
            if (_dragFrom != None)
            {
                int target = SlotUnderPoint(pos);
                MoveItem(_dragFrom, target);
                CancelDrag();
            }
        }
    }

    void BeginDrag(int slot, Vector2 pos)
    {
        _dragFrom = slot;

        var go = new GameObject("DragGhost", typeof(Image));
        var img = go.GetComponent<Image>();
        img.color = new Color(0.95f, 0.8f, 0.35f, 0.9f);
        img.raycastTarget = false;
        _dragGhost = img.rectTransform;
        _dragGhost.SetParent(_root, false);
        _dragGhost.sizeDelta = new Vector2(200f, 44f);
        _dragGhost.SetAsLastSibling();
        _dragGhost.position = pos;

        var label = new GameObject("Label", typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(_dragGhost, false);
        label.font = _font;
        label.text = ItemNameAt(slot);
        label.fontSize = 18;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.1f, 0.08f, 0f, 1f);
        label.raycastTarget = false;
        Stretch(label.rectTransform);
    }

    void CancelDrag()
    {
        if (_dragGhost != null)
            Destroy(_dragGhost.gameObject);
        _dragGhost = null;
        _dragFrom = None;
    }

    void MoveItem(int from, int to)
    {
        if (to == None || to == from)
            return;

        bool fromInv = from >= 0;
        bool toInv = to >= 0;

        if (fromInv && toInv)
        {
            inventory.SwapSlots(from, to);
        }
        else if (fromInv && to == UpgradeSlot)
        {
            var item = inventory.ExtractSlot(from);
            if (item == null)
                return;
            var prev = _upgradeItem;
            _upgradeItem = item;
            if (prev != null)
                inventory.PlaceInSlot(from, prev);
        }
        else if (from == UpgradeSlot && toInv)
        {
            if (_upgradeItem == null)
                return;
            var slotItem = inventory.ExtractSlot(to); // null если пусто
            inventory.PlaceInSlot(to, _upgradeItem);
            _upgradeItem = slotItem;
        }

        RefreshInventory();
        RefreshRoulette();
    }

    int SlotUnderPoint(Vector2 screenPoint)
    {
        if (_upgradeCellRect != null && RectContains(_upgradeCellRect, screenPoint))
            return UpgradeSlot;
        if (_slotCells != null)
        {
            for (int i = 0; i < _slotCells.Length; i++)
                if (RectContains(_slotCells[i], screenPoint))
                    return i;
        }
        return None;
    }

    bool HasItemAt(int slot)
    {
        if (slot == UpgradeSlot)
            return _upgradeItem != null;
        if (slot >= 0 && inventory != null)
            return inventory.GetSlot(slot) != null;
        return false;
    }

    string ItemNameAt(int slot)
    {
        if (slot == UpgradeSlot)
            return _upgradeItem != null ? _upgradeItem.DisplayName : "";
        HeldItem it = inventory != null ? inventory.GetSlot(slot) : null;
        return it != null ? it.DisplayName : "";
    }

    static bool RectContains(RectTransform rt, Vector2 screenPoint) =>
        rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);

    // ------------------------------------------------------------- Roulette logic

    void SetChanceFromMouse(Vector2 screenPoint)
    {
        if (_sliderTrack == null)
            return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_sliderTrack, screenPoint, null, out Vector2 local))
        {
            float w = _sliderTrack.rect.width;
            _chance = Mathf.Clamp01((local.x + w * 0.5f) / w);
            RefreshRoulette();
        }
    }

    void TryStartUpgrade()
    {
        if (_spinning)
            return;
        if (_upgradeItem == null)
        {
            _resultText.text = "Поместите предмет в слот";
            return;
        }
        if (!_upgradeItem.CanUpgrade)
        {
            _resultText.text = "Этот предмет улучшить нельзя";
            return;
        }

        _pendingSuccess = Random.value < _chance;

        float successSpan = 360f * _chance;
        float landing;
        if (_chance <= 0f)
            landing = Random.Range(2f, 358f);          // всё в провал
        else if (_chance >= 1f)
            landing = Random.Range(2f, 358f);          // всё в успех
        else if (_pendingSuccess)
            landing = Random.Range(2f, Mathf.Max(2f, successSpan - 2f));
        else
            landing = Random.Range(successSpan + 2f, 358f);

        _spinStartAngle = 0f;
        _spinEndAngle = 360f * 5f + landing; // 5 оборотов + финальный угол
        _spinElapsed = 0f;
        _spinning = true;
        _resultText.text = "";
        if (_needle != null)
            _needle.localEulerAngles = Vector3.zero;
        RefreshRoulette();
    }

    void UpdateSpin()
    {
        _spinElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_spinElapsed / _spinDuration);
        float eased = 1f - Mathf.Pow(1f - t, 3f); // плавное торможение
        float angle = Mathf.Lerp(_spinStartAngle, _spinEndAngle, eased);
        if (_needle != null)
            _needle.localEulerAngles = new Vector3(0f, 0f, -angle); // по часовой от верха

        if (t >= 1f)
        {
            _spinning = false;
            FinishSpin();
        }
    }

    void FinishSpin()
    {
        HeldItem placed = _upgradeItem;
        _upgradeItem = null;

        if (placed != null)
        {
            HeldItem resultPrefab = placed.UpgradeResultPrefab;
            Destroy(placed.gameObject);

            if (_pendingSuccess && resultPrefab != null)
            {
                inventory.AddItem(resultPrefab);
                _resultText.text = $"УСПЕХ! Получен: {resultPrefab.DisplayName}";
                _resultText.color = new Color(0.5f, 0.9f, 0.5f, 1f);
            }
            else
            {
                _resultText.text = "ПРОВАЛ. Предмет утерян";
                _resultText.color = new Color(0.95f, 0.5f, 0.45f, 1f);
            }
        }

        RefreshInventory();
        RefreshRoulette();
    }

    void RefreshRoulette()
    {
        if (_upgradeCellLabel != null)
        {
            bool has = _upgradeItem != null;
            _upgradeCellLabel.text = has ? _upgradeItem.DisplayName : "перетащите предмет";
            _upgradeCellLabel.color = has ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1f);
            _upgradeCellImage.color = has ? CellFilled : CellEmpty;
        }

        if (_wheelSuccessArc != null)
            _wheelSuccessArc.fillAmount = _chance;

        if (_chanceText != null)
            _chanceText.text = $"Шанс апгрейда: {Mathf.RoundToInt(_chance * 100f)}%";

        if (_sliderHandle != null)
            _sliderHandle.anchoredPosition = new Vector2(_chance * SliderWidth, 0f);

        if (_upgradeButtonImage != null)
        {
            bool canUpgrade = !_spinning && _upgradeItem != null && _upgradeItem.CanUpgrade;
            _upgradeButtonImage.color = canUpgrade
                ? new Color(0.85f, 0.7f, 0.2f, 0.95f)
                : new Color(0.35f, 0.35f, 0.38f, 0.7f);
        }
    }

    // ------------------------------------------------------------- Prompt

    void UpdatePrompt()
    {
        bool show = !_inventoryOpen && interactor != null && interactor.HasTarget && playerCamera != null;
        if (!show)
        {
            if (_promptGO.activeSelf)
                _promptGO.SetActive(false);
            return;
        }

        Transform anchor = interactor.Current.GetAnchor();
        Vector3 screen = playerCamera.WorldToScreenPoint(anchor.position);
        if (screen.z <= 0f)
        {
            _promptGO.SetActive(false);
            return;
        }

        _promptGO.SetActive(true);
        _promptText.text = $"[{interactKey}] {interactor.Current.GetPrompt()}";
        _promptRect.position = new Vector3(screen.x, screen.y + 45f, 0f);
    }

    // ------------------------------------------------------------- Inventory refresh

    void RefreshInventory()
    {
        if (_slotImages == null || inventory == null)
            return;

        for (int i = 0; i < _slotImages.Length; i++)
        {
            HeldItem item = inventory.GetSlot(i);
            bool selected = inventory.EquippedSlot == i;

            _slotImages[i].color = selected ? CellSelected : (item != null ? CellFilled : CellEmpty);
            _slotLabels[i].text = item != null ? $"<b>{i + 1}</b>   {item.DisplayName}" : $"<b>{i + 1}</b>   —";
            _slotLabels[i].color = selected ? new Color(0.1f, 0.08f, 0f, 1f) : new Color(0.92f, 0.92f, 0.92f, 1f);
        }
    }

    // ------------------------------------------------------------- Build

    void BuildUI()
    {
        var canvasGO = new GameObject("InteractionHUD_Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 95;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        _root = canvasGO.GetComponent<RectTransform>();

        // Прицел по просьбе временно отключён (не строим).
        BuildPrompt();
        BuildInventory();
    }

    void BuildPrompt()
    {
        _promptGO = new GameObject("InteractPrompt", typeof(Image));
        _promptGO.transform.SetParent(_root, false);
        var bg = _promptGO.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        bg.raycastTarget = false;
        _promptRect = bg.rectTransform;
        _promptRect.sizeDelta = new Vector2(300f, 40f);
        _promptRect.pivot = new Vector2(0.5f, 0.5f);

        _promptText = NewText(_promptRect, "Text", 20, TextAnchor.MiddleCenter, Color.white);
        Stretch(_promptText.rectTransform, 8f);

        _promptGO.SetActive(false);
    }

    void BuildInventory()
    {
        int count = inventory != null ? inventory.SlotCount : 5;

        _inventoryPanel = new GameObject("InventoryPanel", typeof(Image));
        _inventoryPanel.transform.SetParent(_root, false);
        var dim = _inventoryPanel.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = false;
        Stretch(dim.rectTransform);

        var panel = NewImage(dim.rectTransform, "Panel", new Color(0.06f, 0.07f, 0.09f, 0.6f));
        panel.raycastTarget = false;
        var panelRt = panel.rectTransform;
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(920f, 600f);
        panelRt.anchoredPosition = Vector2.zero;

        var accent = NewImage(panelRt, "Accent", new Color(0.95f, 0.8f, 0.35f, 0.7f));
        accent.raycastTarget = false;
        var accentRt = accent.rectTransform;
        accentRt.anchorMin = new Vector2(0f, 1f);
        accentRt.anchorMax = new Vector2(1f, 1f);
        accentRt.pivot = new Vector2(0.5f, 1f);
        accentRt.sizeDelta = new Vector2(0f, 3f);
        accentRt.anchoredPosition = Vector2.zero;

        var title = NewText(panelRt, "Title", 26, TextAnchor.MiddleLeft, new Color(0.95f, 0.9f, 0.8f, 1f));
        title.fontStyle = FontStyle.Bold;
        title.text = "ИНВЕНТАРЬ";
        var titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(-48f, 44f);
        titleRt.anchoredPosition = new Vector2(0f, -18f);

        BuildSlots(panelRt, count);
        BuildRoulette(panelRt);

        var hint = NewText(panelRt, "Hint", 14, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f, 0.9f));
        hint.text = "Перетаскивай предметы • 1..5 — быстрый выбор • Tab — закрыть";
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.sizeDelta = new Vector2(0f, 26f);
        hintRt.anchoredPosition = new Vector2(0f, 12f);

        _inventoryPanel.SetActive(false);
    }

    void BuildSlots(RectTransform panelRt, int count)
    {
        // Левая колонка — слоты.
        var left = NewChild(panelRt, "Slots");
        left.anchorMin = new Vector2(0f, 0f);
        left.anchorMax = new Vector2(0.5f, 1f);
        left.offsetMin = new Vector2(24f, 44f);
        left.offsetMax = new Vector2(-12f, -70f);

        _slotCells = new RectTransform[count];
        _slotImages = new Image[count];
        _slotLabels = new Text[count];

        const float cellH = 60f;
        const float gap = 10f;
        float top = 0f;

        for (int i = 0; i < count; i++)
        {
            var cell = NewImage(left, $"Cell{i + 1}", CellEmpty);
            cell.raycastTarget = false;
            var rt = cell.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, cellH);
            rt.anchoredPosition = new Vector2(0f, top - i * (cellH + gap));

            var label = NewText(rt, "Label", 22, TextAnchor.MiddleLeft, new Color(0.92f, 0.92f, 0.92f, 1f));
            Stretch(label.rectTransform, 20f);

            _slotCells[i] = rt;
            _slotImages[i] = cell;
            _slotLabels[i] = label;
        }
    }

    void BuildRoulette(RectTransform panelRt)
    {
        // Правая колонка — рулетка апгрейда.
        var right = NewChild(panelRt, "Roulette");
        right.anchorMin = new Vector2(0.5f, 0f);
        right.anchorMax = new Vector2(1f, 1f);
        right.offsetMin = new Vector2(12f, 44f);
        right.offsetMax = new Vector2(-24f, -70f);

        var header = NewText(right, "Header", 20, TextAnchor.UpperCenter, new Color(0.95f, 0.9f, 0.8f, 1f));
        header.fontStyle = FontStyle.Bold;
        header.text = "РУЛЕТКА АПГРЕЙДА";
        var hrt = header.rectTransform;
        hrt.anchorMin = new Vector2(0f, 1f);
        hrt.anchorMax = new Vector2(1f, 1f);
        hrt.pivot = new Vector2(0.5f, 1f);
        hrt.sizeDelta = new Vector2(0f, 28f);
        hrt.anchoredPosition = new Vector2(0f, -4f);

        // Спец-слот для предмета.
        _upgradeCellImage = NewImage(right, "UpgradeSlot", CellEmpty);
        _upgradeCellImage.raycastTarget = false;
        _upgradeCellRect = _upgradeCellImage.rectTransform;
        _upgradeCellRect.anchorMin = new Vector2(0.5f, 1f);
        _upgradeCellRect.anchorMax = new Vector2(0.5f, 1f);
        _upgradeCellRect.pivot = new Vector2(0.5f, 1f);
        _upgradeCellRect.sizeDelta = new Vector2(300f, 48f);
        _upgradeCellRect.anchoredPosition = new Vector2(0f, -40f);
        _upgradeCellLabel = NewText(_upgradeCellRect, "Label", 18, TextAnchor.MiddleCenter, Color.white);
        Stretch(_upgradeCellLabel.rectTransform, 8f);

        // Колесо (диск + зелёный сектор успеха + стрелка).
        var wheel = NewChild(right, "Wheel");
        wheel.anchorMin = wheel.anchorMax = new Vector2(0.5f, 1f);
        wheel.pivot = new Vector2(0.5f, 1f);
        wheel.sizeDelta = new Vector2(210f, 210f);
        wheel.anchoredPosition = new Vector2(0f, -100f);

        Sprite circle = GetCircleSprite();

        var disc = NewImage(wheel, "Disc", WheelFail);
        disc.sprite = circle;
        disc.raycastTarget = false;
        Stretch(disc.rectTransform);

        _wheelSuccessArc = NewImage(wheel, "SuccessArc", WheelSuccess);
        _wheelSuccessArc.sprite = circle;
        _wheelSuccessArc.type = Image.Type.Filled;
        _wheelSuccessArc.fillMethod = Image.FillMethod.Radial360;
        _wheelSuccessArc.fillOrigin = (int)Image.Origin360.Top;
        _wheelSuccessArc.fillClockwise = true;
        _wheelSuccessArc.fillAmount = _chance;
        _wheelSuccessArc.raycastTarget = false;
        Stretch(_wheelSuccessArc.rectTransform);

        _needle = NewChild(wheel, "Needle");
        _needle.anchorMin = _needle.anchorMax = new Vector2(0.5f, 0.5f);
        _needle.pivot = new Vector2(0.5f, 0f); // низ по центру колеса
        _needle.sizeDelta = new Vector2(6f, 92f);
        _needle.anchoredPosition = Vector2.zero;
        var needleImg = _needle.gameObject.AddComponent<Image>();
        needleImg.color = new Color(0.98f, 0.98f, 0.98f, 1f);
        needleImg.raycastTarget = false;

        var hub = NewImage(wheel, "Hub", new Color(0.1f, 0.1f, 0.12f, 1f));
        hub.sprite = circle;
        hub.raycastTarget = false;
        var hubRt = hub.rectTransform;
        hubRt.anchorMin = hubRt.anchorMax = new Vector2(0.5f, 0.5f);
        hubRt.pivot = new Vector2(0.5f, 0.5f);
        hubRt.sizeDelta = new Vector2(26f, 26f);
        hubRt.anchoredPosition = Vector2.zero;

        // Текст шанса.
        _chanceText = NewText(right, "ChanceText", 16, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.9f, 1f));
        var ctRt = _chanceText.rectTransform;
        ctRt.anchorMin = new Vector2(0f, 1f);
        ctRt.anchorMax = new Vector2(1f, 1f);
        ctRt.pivot = new Vector2(0.5f, 1f);
        ctRt.sizeDelta = new Vector2(0f, 24f);
        ctRt.anchoredPosition = new Vector2(0f, -322f);

        // Слайдер шанса (трек + заливка + ручка).
        _sliderTrack = NewChild(right, "SliderTrack");
        _sliderTrack.anchorMin = _sliderTrack.anchorMax = new Vector2(0.5f, 1f);
        _sliderTrack.pivot = new Vector2(0.5f, 1f);
        _sliderTrack.sizeDelta = new Vector2(SliderWidth, 16f);
        _sliderTrack.anchoredPosition = new Vector2(0f, -350f);
        var trackImg = _sliderTrack.gameObject.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.12f);
        trackImg.raycastTarget = false;

        _sliderHandle = NewChild(_sliderTrack, "Handle");
        _sliderHandle.anchorMin = _sliderHandle.anchorMax = new Vector2(0f, 0.5f);
        _sliderHandle.pivot = new Vector2(0.5f, 0.5f);
        _sliderHandle.sizeDelta = new Vector2(16f, 26f);
        var handleImg = _sliderHandle.gameObject.AddComponent<Image>();
        handleImg.color = new Color(0.95f, 0.8f, 0.35f, 1f);
        handleImg.raycastTarget = false;

        // Кнопка апгрейда.
        _upgradeButtonImage = NewImage(right, "UpgradeButton", new Color(0.85f, 0.7f, 0.2f, 0.95f));
        _upgradeButtonImage.raycastTarget = false;
        _upgradeButtonRect = _upgradeButtonImage.rectTransform;
        _upgradeButtonRect.anchorMin = _upgradeButtonRect.anchorMax = new Vector2(0.5f, 1f);
        _upgradeButtonRect.pivot = new Vector2(0.5f, 1f);
        _upgradeButtonRect.sizeDelta = new Vector2(220f, 46f);
        _upgradeButtonRect.anchoredPosition = new Vector2(0f, -382f);
        var btnLabel = NewText(_upgradeButtonRect, "Label", 20, TextAnchor.MiddleCenter, new Color(0.1f, 0.08f, 0f, 1f));
        btnLabel.fontStyle = FontStyle.Bold;
        btnLabel.text = "АПГРЕЙД";
        Stretch(btnLabel.rectTransform);

        // Результат.
        _resultText = NewText(right, "Result", 16, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.9f, 1f));
        var rrt = _resultText.rectTransform;
        rrt.anchorMin = new Vector2(0f, 1f);
        rrt.anchorMax = new Vector2(1f, 1f);
        rrt.pivot = new Vector2(0.5f, 1f);
        rrt.sizeDelta = new Vector2(0f, 26f);
        rrt.anchoredPosition = new Vector2(0f, -440f);
    }

    // ------------------------------------------------------------- UI helpers

    RectTransform NewChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    Image NewImage(Transform parent, string name, Color color)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        return img;
    }

    Text NewText(Transform parent, string name, int fontSize, TextAnchor anchor, Color color)
    {
        var t = new GameObject(name, typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = _font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Stretch(RectTransform rt, float padding = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    static Sprite GetCircleSprite()
    {
        if (_circleSprite != null)
            return _circleSprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        var center = new Vector2(r, r);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                byte a = (byte)(d <= r - 1f ? 255 : 0);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _circleSprite;
    }
}
