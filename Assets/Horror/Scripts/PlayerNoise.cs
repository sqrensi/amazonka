using UnityEngine;

/// <summary>
/// Переводит поведение игрока в шум и сообщает о нём в <see cref="NoiseManager"/>.
///
/// Шаги генерируют импульсы шума с интервалом, зависящим от стойки:
///  - присед/ползком — почти тихо;
///  - ходьба — умеренно;
///  - бег — громко.
/// Приземление после прыжка/падения — отдельный разовый всплеск.
/// Метод <see cref="EmitNoise"/> нужен для будущих источников (выстрел, дверь, взрыв).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerNoise : MonoBehaviour
{
    [SerializeField] HorrorFirstPersonController controller;

    [Header("Step noise (единицы шкалы 0..100)")]
    [Tooltip("Импульсы подобраны так, чтобы ходьба выходила на ~8, бег на ~20.")]
    [SerializeField] float walkStepNoise = 5f;
    [SerializeField] float sprintStepNoise = 7f;
    [SerializeField] float crouchStepNoise = 1f;

    [Header("Step interval (сек)")]
    [SerializeField] float walkStepInterval = 0.45f;
    [SerializeField] float sprintStepInterval = 0.3f;
    [SerializeField] float crouchStepInterval = 0.7f;

    [Header("Other")]
    [SerializeField] float landNoise = 8f;
    [SerializeField] float minMoveSpeed = 0.4f;

    CharacterController _cc;
    NoiseManager _noise;
    float _stepTimer;
    bool _wasGrounded = true;

    /// <summary>Текущий общий шум (0..100). Удобно для UI-шкалы.</summary>
    public float CurrentNoise => _noise != null ? _noise.Noise : 0f;
    public float CurrentNoiseNormalized => _noise != null ? _noise.NoiseNormalized : 0f;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (controller == null)
            controller = GetComponent<HorrorFirstPersonController>();
    }

    void Start()
    {
        _noise = NoiseManager.GetOrCreate();
    }

    void Update()
    {
        if (_noise == null)
            _noise = NoiseManager.GetOrCreate();

        float dt = Time.deltaTime;
        bool grounded = _cc.isGrounded;

        // Приземление.
        if (grounded && !_wasGrounded)
            _noise.Report(transform.position, landNoise);
        _wasGrounded = grounded;

        Vector3 planar = _cc.velocity;
        planar.y = 0f;
        bool moving = grounded && planar.magnitude > minMoveSpeed;

        if (!moving)
        {
            _stepTimer = 0f;
            return;
        }

        bool crouch = controller != null && controller.IsCrouching;
        bool sprint = controller != null && controller.IsSprinting;

        float interval = crouch ? crouchStepInterval : (sprint ? sprintStepInterval : walkStepInterval);
        float stepNoise = crouch ? crouchStepNoise : (sprint ? sprintStepNoise : walkStepNoise);

        _stepTimer += dt;
        if (_stepTimer >= interval)
        {
            _stepTimer = 0f;
            if (stepNoise > 0f)
                _noise.Report(transform.position, stepNoise);
        }
    }

    /// <summary>Разовый шум из внешнего источника (выстрел, дверь, взрыв и т.д.).</summary>
    public void EmitNoise(float amount)
    {
        (_noise ?? NoiseManager.GetOrCreate()).Report(transform.position, amount);
    }

    /// <summary>Разовый шум в произвольной точке.</summary>
    public void EmitNoiseAt(Vector3 position, float amount)
    {
        (_noise ?? NoiseManager.GetOrCreate()).Report(position, amount);
    }
}
