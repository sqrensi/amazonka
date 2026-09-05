using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Инвентарь на фиксированное число слотов (по умолчанию 5), первые из которых
/// привязаны к клавишам 1..5. Хранит инстансы <see cref="HeldItem"/> детьми сокета руки:
/// экипированный — активен и "висит" перед камерой, остальные скрыты.
///
/// Также следит за препятствием перед камерой: если игрок прижался к стене,
/// предмет в руке прячется (чтобы не проходил сквозь стену), а использование
/// (стрельба/свет) блокируется.
///
/// Ввод (PlayerInput, SendMessages):
///  - OnSlot1..OnSlot5 — выбрать слот (повторное нажатие текущего = убрать в пустые руки);
///  - OnAttack — использовать предмет в руке (ЛКМ).
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [SerializeField] Transform heldSocket;
    [SerializeField] Camera playerCamera;
    [Tooltip("Число слотов инвентаря (клавиши 1..N для первых слотов).")]
    [SerializeField] int slotCount = 5;
    [Tooltip("Автоматически доставать только что подобранный предмет, если руки пусты.")]
    [SerializeField] bool autoEquipOnPickup = true;

    [Header("Obstruction (близость к стене-коллайдеру)")]
    [Tooltip("Дистанция, с которой предмет начинает плавно убираться (нет касания = достаётся).")]
    [SerializeField] float obstructionStartDistance = 0.9f;
    [Tooltip("Дистанция, на которой предмет полностью убран.")]
    [SerializeField] float obstructionFullDistance = 0.35f;
    [Tooltip("Радиус сферы проверки (объёмнее тонкого луча — надёжнее ловит коллайдеры/стены).")]
    [SerializeField] float obstructionProbeRadius = 0.15f;
    [Tooltip("Слои коллайдеров, от которых убирается предмет. Триггеры игнорируются.")]
    [SerializeField] LayerMask obstructionMask = ~0;

    HeldItem[] _slots;
    int _equipped = -1;
    float _obstructionAmount;

    public int SlotCount => _slots?.Length ?? slotCount;
    public int EquippedSlot => _equipped;
    public HeldItem Current => (_equipped >= 0 && _equipped < SlotCount) ? _slots[_equipped] : null;
    public float ObstructionAmount => _obstructionAmount;

    /// <summary>Изменился состав слотов или выбранный слот (для HUD).</summary>
    public event Action OnInventoryChanged;

    void Awake()
    {
        _slots = new HeldItem[Mathf.Max(1, slotCount)];
        if (heldSocket == null)
            heldSocket = transform;
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        UpdateObstruction();
    }

    public HeldItem GetSlot(int index) =>
        (index >= 0 && index < SlotCount) ? _slots[index] : null;

    public bool HasItemOfType<T>() where T : HeldItem
    {
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] is T)
                return true;
        return false;
    }

    /// <summary>
    /// Создать предмет из префаба и положить в инвентарь.
    /// Возвращает false, если нет свободного слота.
    /// </summary>
    public bool AddItem(HeldItem prefab)
    {
        if (prefab == null)
            return false;

        int slot = ChooseSlot(prefab.PreferredSlot);
        if (slot < 0)
            return false; // инвентарь полон

        HeldItem instance = Instantiate(prefab, heldSocket);
        instance.name = prefab.name;
        instance.gameObject.SetActive(false);
        instance.ApplyHeldPose();
        instance.Initialize(this, gameObject);

        _slots[slot] = instance;

        if (autoEquipOnPickup && _equipped < 0)
            EquipSlot(slot);
        else
            OnInventoryChanged?.Invoke();

        return true;
    }

    int ChooseSlot(int preferred)
    {
        if (preferred >= 0 && preferred < SlotCount && _slots[preferred] == null)
            return preferred;
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] == null)
                return i;
        return -1;
    }

    public void EquipSlot(int index)
    {
        if (index < 0 || index >= SlotCount)
            return;

        // Повторное нажатие занятого текущего слота — убрать в "пустые руки".
        if (_equipped == index)
        {
            Holster();
            return;
        }

        if (Current != null)
        {
            Current.OnUnequip();
            Current.gameObject.SetActive(false);
        }

        _equipped = index;

        HeldItem next = _slots[index];
        if (next != null)
        {
            next.gameObject.SetActive(true);
            next.ResetRetract();
            next.OnEquip();
        }

        OnInventoryChanged?.Invoke();
    }

    /// <summary>Убрать предмет из рук (пустые руки), не выбрасывая из инвентаря.</summary>
    public void Holster()
    {
        if (Current != null)
        {
            Current.OnUnequip();
            Current.gameObject.SetActive(false);
        }
        _equipped = -1;
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Забрать предмет из слота (для рулетки/перемещения). Возвращает предмет или null.</summary>
    public HeldItem ExtractSlot(int index)
    {
        if (index < 0 || index >= SlotCount)
            return null;
        HeldItem item = _slots[index];
        if (item == null)
            return null;

        if (_equipped == index)
            Holster();

        item.gameObject.SetActive(false);
        _slots[index] = null;
        OnInventoryChanged?.Invoke();
        return item;
    }

    /// <summary>Положить предмет в конкретный слот (должен быть пуст). Возвращает успех.</summary>
    public bool PlaceInSlot(int index, HeldItem item)
    {
        if (item == null || index < 0 || index >= SlotCount || _slots[index] != null)
            return false;
        _slots[index] = item;
        item.gameObject.SetActive(false);
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Поменять местами содержимое двух слотов (drag&drop в инвентаре).</summary>
    public void SwapSlots(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= SlotCount || b >= SlotCount)
            return;

        (_slots[a], _slots[b]) = (_slots[b], _slots[a]);

        // Сохраняем экипировку за "тем же предметом": двигаем индекс выбранного слота.
        if (_equipped == a) _equipped = b;
        else if (_equipped == b) _equipped = a;

        OnInventoryChanged?.Invoke();
    }

    void UpdateObstruction()
    {
        HeldItem item = Current;
        if (item == null)
        {
            _obstructionAmount = 0f;
            return;
        }

        float target = 0f;
        if (playerCamera != null)
        {
            Vector3 origin = playerCamera.transform.position;
            Vector3 dir = playerCamera.transform.forward;

            // Сфера объёмнее тонкого луча — надёжнее ловит стену/box collider перед предметом.
            // Триггеры игнорируем — реагируем только на реальные коллайдеры.
            if (Physics.SphereCast(origin, obstructionProbeRadius, dir, out RaycastHit hit,
                    obstructionStartDistance, obstructionMask, QueryTriggerInteraction.Ignore))
            {
                // Пропорционально: дальше startDistance = 0, ближе fullDistance = 1.
                target = Mathf.InverseLerp(obstructionStartDistance, obstructionFullDistance, hit.distance);
            }
            else if (Physics.CheckSphere(origin, obstructionProbeRadius, obstructionMask, QueryTriggerInteraction.Ignore))
            {
                // Камера уже внутри/вплотную к коллайдеру — считаем полностью убранным.
                target = 1f;
            }
        }

        _obstructionAmount = target;
        // Плавно (SmoothDamp) двигаем предмет к целевой убранности.
        item.SetRetractTarget(target);
        item.TickRetract(Time.deltaTime);
    }

    // ---------------------------------------------------------------- Input (SendMessages)

    public void OnSlot1(InputValue value) { if (value.isPressed) EquipSlot(0); }
    public void OnSlot2(InputValue value) { if (value.isPressed) EquipSlot(1); }
    public void OnSlot3(InputValue value) { if (value.isPressed) EquipSlot(2); }
    public void OnSlot4(InputValue value) { if (value.isPressed) EquipSlot(3); }
    public void OnSlot5(InputValue value) { if (value.isPressed) EquipSlot(4); }

    public void OnAttack(InputValue value)
    {
        // ЛКМ работает и как "перехватить курсор" (см. контроллер) — стреляем только при захвате.
        if (Cursor.lockState != CursorLockMode.Locked)
            return;
        // Предмет убран у стены — использование заблокировано.
        if (Current != null && Current.IsUseBlocked)
            return;

        if (value.isPressed)
            Current?.OnUseStart();
        else
            Current?.OnUseStop();
    }
}
