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
///  - OnAttack — использовать предмет в руке (ЛКМ);
///  - OnDrop / G — выбросить предмет из рук.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [SerializeField] Transform heldSocket;
    [SerializeField] Camera playerCamera;
    [Tooltip("Число слотов инвентаря (клавиши 1..N для первых слотов).")]
    [SerializeField] int slotCount = 5;
    [Tooltip("Автоматически доставать только что подобранный предмет, если руки пусты.")]
    [SerializeField] bool autoEquipOnPickup = true;

    [Header("Выброс (G)")]
    [SerializeField] float dropForwardSpeed = 2.4f;
    [SerializeField] float dropUpSpeed = 1.35f;
    [Tooltip("Насколько сильный взмах камеры добавляет скорость броска.")]
    [SerializeField] float dropLookGain = 0.72f;
    [Tooltip("Плечо броска (м): угловая скорость камеры * плечо = линейная скорость.")]
    [SerializeField] float dropLookArm = 0.52f;

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
    readonly RaycastHit[] _obstructionHits = new RaycastHit[16];
    readonly Collider[] _obstructionOverlaps = new Collider[16];

    public int SlotCount => _slots?.Length ?? slotCount;
    public int EquippedSlot => _equipped;
    public HeldItem Current => (_equipped >= 0 && _equipped < SlotCount) ? _slots[_equipped] : null;
    public float ObstructionAmount => _obstructionAmount;
    public Transform HeldSocket => heldSocket;

    public bool HasFreeSlot(int preferred = -1) => ChooseSlot(preferred) >= 0;

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
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame &&
            Cursor.lockState == CursorLockMode.Locked)
            DropEquipped();
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
    /// Положить уже существующий предмет из мира в инвентарь (тот же инстанс, состояние сохраняется).
    /// </summary>
    public bool PickupExisting(HeldItem item)
    {
        if (item == null || item.IsCarried)
            return false;

        int slot = ChooseSlot(item.PreferredSlot);
        if (slot < 0)
            return false;

        item.AttachToInventory(this, gameObject);
        item.gameObject.SetActive(false);
        item.ApplyHeldPose();
        _slots[slot] = item;

        if (autoEquipOnPickup && _equipped < 0)
            EquipSlot(slot);
        else
            OnInventoryChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Создать предмет из префаба и положить в инвентарь.
    /// </summary>
    public bool AddItem(HeldItem prefab)
    {
        if (prefab == null || !HasFreeSlot(prefab.PreferredSlot))
            return false;

        HeldItem instance = Instantiate(prefab, heldSocket);
        instance.name = prefab.name;
        instance.gameObject.SetActive(false);
        if (!PickupExisting(instance))
        {
            Destroy(instance.gameObject);
            return false;
        }
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

    /// <summary>Выбросить предмет из рук в мир — тот же объект, состояние (свет) сохраняется.</summary>
    public bool DropEquipped()
    {
        HeldItem item = Current;
        if (item == null)
            return false;

        Vector3 planar = transform.forward;
        Vector3 camFwd = planar;
        Vector3 camRight = transform.right;
        Vector3 camUp = Vector3.up;
        if (playerCamera != null)
        {
            camFwd = playerCamera.transform.forward;
            camRight = playerCamera.transform.right;
            camUp = playerCamera.transform.up;
            planar = camFwd;
            planar.y = 0f;
            if (planar.sqrMagnitude < 0.001f)
                planar = transform.forward;
            planar.Normalize();
        }

        Vector3 velocity = planar * dropForwardSpeed + Vector3.up * dropUpSpeed;
        var cc = GetComponent<CharacterController>();
        if (cc != null)
            velocity += new Vector3(cc.velocity.x, 0f, cc.velocity.z) * 0.45f;

        var move = GetComponent<HorrorFirstPersonController>();
        if (move != null)
        {
            Vector2 look = move.LookVelocity;
            float yawRad = look.x * Mathf.Deg2Rad;
            float pitchRad = look.y * Mathf.Deg2Rad;
            Vector3 flick = (camRight * yawRad - camUp * pitchRad) * dropLookArm * dropLookGain;
            float flickSpeed = flick.magnitude;
            // Резкий взмах — ещё и в сторону взгляда (куда крутанул камеру).
            float flick01 = Mathf.Clamp01(flickSpeed / 8f);
            velocity += flick;
            velocity += camFwd * (dropForwardSpeed * 0.7f * flick01);
            velocity += Vector3.up * (dropUpSpeed * 0.2f * flick01);
        }

        int slot = _equipped;
        _slots[slot] = null;
        _equipped = -1;

        item.DropIntoWorld(velocity, gameObject);
        OnInventoryChanged?.Invoke();
        return true;
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

    /// <summary>Съесть/уничтожить предмет в руках.</summary>
    public bool DestroyEquipped()
    {
        HeldItem item = Current;
        if (item == null)
            return false;

        int slot = _equipped;
        item.OnUnequip();
        _slots[slot] = null;
        _equipped = -1;
        Destroy(item.gameObject);
        OnInventoryChanged?.Invoke();
        return true;
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

            // Сфера ловит стену; коллайдеры игрока пропускаем (камера внутри CharacterController).
            float hitDistance = NearestObstructionDistance(origin, dir);
            if (hitDistance < float.PositiveInfinity)
            {
                // Пропорционально: дальше startDistance = 0, ближе fullDistance = 1.
                target = Mathf.InverseLerp(obstructionStartDistance, obstructionFullDistance, hitDistance);
            }
            else if (IsInsideForeignCollider(origin))
            {
                // Камера уже внутри/вплотную к чужому коллайдеру — считаем полностью убранным.
                target = 1f;
            }
        }

        _obstructionAmount = target;
        // Плавно (SmoothDamp) двигаем предмет к целевой убранности.
        item.SetRetractTarget(target);
        item.TickRetract(Time.deltaTime);
    }

    float NearestObstructionDistance(Vector3 origin, Vector3 dir)
    {
        int count = Physics.SphereCastNonAlloc(
            origin, obstructionProbeRadius, dir, _obstructionHits,
            obstructionStartDistance, obstructionMask, QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider col = _obstructionHits[i].collider;
            if (IsOwnCollider(col))
                continue;
            float d = _obstructionHits[i].distance;
            if (d < best)
                best = d;
        }
        return best;
    }

    bool IsInsideForeignCollider(Vector3 origin)
    {
        int count = Physics.OverlapSphereNonAlloc(
            origin, obstructionProbeRadius, _obstructionOverlaps,
            obstructionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsOwnCollider(_obstructionOverlaps[i]))
                return true;
        }
        return false;
    }

    bool IsOwnCollider(Collider col)
    {
        if (col == null)
            return true;
        Transform t = col.transform;
        return t == transform || t.IsChildOf(transform);
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

    public void OnDrop(InputValue value)
    {
        if (!value.isPressed)
            return;
        if (Cursor.lockState != CursorLockMode.Locked)
            return;
        DropEquipped();
    }
}
