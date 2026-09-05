using UnityEngine;

/// <summary>
/// Базовый класс предмета, который игрок держит "в руке" (viewmodel, висящий в воздухе
/// перед камерой — без модели рук). Конкретные предметы (фонарь, пистолет) наследуются
/// и переопределяют реакции на события экипировки и использования.
///
/// Экземпляр предмета живёт как ребёнок сокета руки (<see cref="PlayerInventory"/>),
/// прячется/показывается при смене слота. Локальный трансформ при экипировке задаётся
/// полями ниже, чтобы модель можно было точно "подвесить" перед камерой.
///
/// При приближении к стене предмет ПЛАВНО убирается к камере (retract) и так же плавно
/// достаётся обратно — без мгновенного исчезновения. Пока предмет достаточно убран,
/// использование (стрельба/свет) блокируется.
/// </summary>
public abstract class HeldItem : MonoBehaviour
{
    [Header("Item")]
    [Tooltip("Отображаемое имя (в подсказке подбора и слоте инвентаря).")]
    [SerializeField] string displayName = "Предмет";
    [Tooltip("Желаемый быстрый слот (0..4 -> клавиши 1..5). -1 = первый свободный.")]
    [SerializeField] int preferredSlot = -1;
    [Tooltip("Во что превращается при успешном апгрейде в рулетке. Пусто = улучшать нельзя.")]
    [SerializeField] HeldItem upgradeResultPrefab;

    [Header("Положение в руке (относительно сокета камеры)")]
    [SerializeField] Vector3 heldLocalPosition = Vector3.zero;
    [SerializeField] Vector3 heldLocalEuler = Vector3.zero;
    [SerializeField] Vector3 heldLocalScale = Vector3.one;

    [Header("Убирание у стены (retract)")]
    [Tooltip("Смещение позиции в полностью убранном состоянии (тянем назад/вниз к камере).")]
    [SerializeField] Vector3 retractedPositionOffset = new Vector3(0f, 0.06f, -0.28f);
    [Tooltip("Довороты в полностью убранном состоянии (градусы).")]
    [SerializeField] Vector3 retractedEulerOffset = new Vector3(-25f, 0f, 0f);
    [Tooltip("Время сглаживания убирания/доставания (сек). Больше — мягче/медленнее.")]
    [SerializeField] float retractSmoothTime = 0.12f;
    [Tooltip("Порог убранности, при котором использование блокируется (0..1).")]
    [SerializeField, Range(0f, 1f)] float useBlockThreshold = 0.5f;

    protected PlayerInventory Inventory { get; private set; }
    protected GameObject Owner { get; private set; }

    Renderer[] _renderers;
    float _retract;        // текущая доля убранности 0..1
    float _retractTarget;  // цель 0..1 (0 достаём, 1 полностью убран)
    float _retractVel;     // скорость для SmoothDamp
    bool _hidden;

    public string DisplayName => displayName;
    public int PreferredSlot => preferredSlot;
    public HeldItem UpgradeResultPrefab => upgradeResultPrefab;
    public bool CanUpgrade => upgradeResultPrefab != null;

    /// <summary>Текущая доля убранности предмета (0 — в руке, 1 — полностью убран).</summary>
    public float RetractAmount => _retract;

    /// <summary>Предмет достаточно убран у стены — использование заблокировано.</summary>
    public bool IsUseBlocked => _retract >= useBlockThreshold;

    /// <summary>Применить позу в руке с учётом текущей убранности (retract).</summary>
    public void ApplyHeldPose()
    {
        transform.localPosition = heldLocalPosition + retractedPositionOffset * _retract;
        transform.localRotation = Quaternion.Euler(heldLocalEuler + retractedEulerOffset * _retract);
        transform.localScale = heldLocalScale;
    }

    /// <summary>Вызывается один раз при добавлении в инвентарь.</summary>
    public virtual void Initialize(PlayerInventory inventory, GameObject owner)
    {
        Inventory = inventory;
        Owner = owner;
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>Задать целевую долю убранности 0..1 (пропорционально близости к стене).</summary>
    public void SetRetractTarget(float target01)
    {
        _retractTarget = Mathf.Clamp01(target01);
    }

    /// <summary>Мгновенно сбросить убранность (при экипировке нового предмета).</summary>
    public void ResetRetract()
    {
        _retract = 0f;
        _retractTarget = 0f;
        _retractVel = 0f;
        ApplyHeldPose();
        SetRenderersHidden(false);
    }

    /// <summary>Плавно (SmoothDamp) двигаем убранность к цели и применяем позу. Вызывает инвентарь каждый кадр.</summary>
    public void TickRetract(float dt)
    {
        _retract = Mathf.SmoothDamp(_retract, _retractTarget, ref _retractVel, retractSmoothTime, Mathf.Infinity, dt);
        _retract = Mathf.Clamp01(_retract);

        // Прячем рендеры только когда предмет практически полностью убран (кончик мог бы проткнуть стену);
        // всё остальное время видна плавная анимация.
        SetRenderersHidden(_retract >= 0.995f);

        ApplyHeldPose();
        OnRetractChanged(_retract);
    }

    void SetRenderersHidden(bool hidden)
    {
        if (_hidden == hidden)
            return;
        _hidden = hidden;

        if (_renderers == null)
            _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers)
            if (r != null)
                r.enabled = !hidden;
    }

    /// <summary>Убранность изменилась (для наследников — напр. плавно гасить свет фонаря).</summary>
    protected virtual void OnRetractChanged(float retract) { }

    /// <summary>Достали в руку (объект стал активным).</summary>
    public virtual void OnEquip() { }

    /// <summary>Убрали в инвентарь (объект скрывается).</summary>
    public virtual void OnUnequip() { }

    /// <summary>Основное действие (ЛКМ): выстрел / включение фонаря и т.п.</summary>
    public virtual void OnUseStart() { }

    /// <summary>Отпустили основное действие (для авто-огня/зарядки, если понадобится).</summary>
    public virtual void OnUseStop() { }
}
