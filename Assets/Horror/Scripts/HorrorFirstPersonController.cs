using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class HorrorFirstPersonController : MonoBehaviour
{
    [SerializeField] Transform cameraPivot;
    [SerializeField] Camera playerCamera;
    [SerializeField] Light flashlight;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 2f;
    [SerializeField] float sprintSpeed = 5f;
    [SerializeField] float crouchSpeed = 1.2f;
    [SerializeField] float acceleration = 12f;
    [SerializeField] float gravity = -22f;
    [SerializeField] float jumpHeight = 1.05f;

    [Header("Look")]
    [SerializeField] float lookSensitivity = 0.08f;
    [SerializeField] float stickLookSensitivity = 120f;
    [SerializeField] float minPitch = -82f;
    [SerializeField] float maxPitch = 82f;
    [SerializeField] bool invertY;

    [Header("Stance")]
    [SerializeField] float standingHeight = 1.8f;
    [SerializeField] float crouchHeight = 1.15f;
    [SerializeField] float standingCameraHeight = 1.62f;
    [SerializeField] float crouchCameraHeight = 0.92f;
    [SerializeField] float stanceLerp = 12f;

    [Header("Stamina")]
    [SerializeField] float maxStamina = 4.5f;
    [SerializeField] float staminaDrain = 1.15f;
    [SerializeField] float staminaRegen = 0.65f;
    [SerializeField] float staminaRegenDelay = 0.8f;

    [Header("Camera feel")]
    [SerializeField] float baseFov = 65f;
    [SerializeField] float sprintFov = 73f;
    [SerializeField] float bobWalkAmount = 0.012f;
    [SerializeField] float bobSprintAmount = 0.02f;
    [SerializeField] float bobCrouchAmount = 0.008f;
    [SerializeField] float bobWalkFrequency = 6.2f;
    [SerializeField] float bobSprintFrequency = 8.2f;
    [SerializeField] float bobHorizontalScale = 0.5f;
    [SerializeField] float bobRollAmount = 0.35f;
    [SerializeField] float cameraSmoothing = 12f;
    [SerializeField] float idleSway = 0.006f;
    [SerializeField] float idleSwaySpeed = 0.7f;
    [SerializeField] float landBob = 0.05f;

    [Header("Flashlight")]
    [SerializeField] bool flashlightStartsOn = true;
    [SerializeField] float flashlightBaseIntensity = 220f;
    [SerializeField] bool flashlightFlicker;
    [SerializeField] float flickerAmount = 18f;

    [Header("Footsteps")]
    [SerializeField] AudioSource footstepSource;
    [SerializeField] AudioClip[] walkFootsteps;
    [SerializeField] AudioClip[] sprintFootsteps;
    [SerializeField] float walkStepInterval = 0.58f;
    [SerializeField] float sprintStepInterval = 0.38f;
    [SerializeField] float crouchStepInterval = 0.78f;

    CharacterController _controller;
    PlayerInput _playerInput;
    InputAction _crouchAction;
    InputAction _sprintAction;
    Vector2 _moveInput;
    Vector2 _lookInput;
    bool _sprintHeld;
    bool _crouchHeld;
    bool _jumpQueued;
    float _pitch;
    float _yaw;
    float _verticalVelocity;
    float _stamina;
    float _staminaCooldown;
    float _currentSpeed;
    float _bobTimer;
    float _idleTimer;
    float _stepTimer;
    float _landOffset;
    bool _wasGrounded = true;
    bool _flashlightOn;
    int _lastFootstepIndex = -1;
    Vector3 _camPosVelocity;
    float _camRoll;

    public float StaminaNormalized => maxStamina <= 0f ? 0f : _stamina / maxStamina;
    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _playerInput = GetComponent<PlayerInput>();
        if (_playerInput != null && _playerInput.actions != null)
        {
            _crouchAction = _playerInput.actions.FindAction("Crouch", false);
            _sprintAction = _playerInput.actions.FindAction("Sprint", false);
        }
        _stamina = maxStamina;
        _flashlightOn = flashlightStartsOn;
        _yaw = transform.eulerAngles.y;

        if (cameraPivot == null)
        {
            var found = transform.Find("CameraPivot");
            if (found != null)
                cameraPivot = found;
        }

        if (playerCamera == null && cameraPivot != null)
            playerCamera = cameraPivot.GetComponentInChildren<Camera>();

        if (flashlight == null && cameraPivot != null)
            flashlight = cameraPivot.GetComponentInChildren<Light>();

        if (footstepSource == null)
            footstepSource = GetComponent<AudioSource>();

        ApplyFlashlightState();
        SetControllerHeight(standingHeight, true);
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerCamera != null)
            playerCamera.fieldOfView = baseFov;
    }

    void Update()
    {
        HandleCursor();
        UpdateLook();
        UpdateStance(Time.deltaTime);
        UpdateMotor(Time.deltaTime);
        UpdateCameraMotion(Time.deltaTime);
        UpdateFlashlight();
    }

    public void OnMove(InputValue value) => _moveInput = value.Get<Vector2>();

    public void OnLook(InputValue value) => _lookInput = value.Get<Vector2>();

    public void OnSprint(InputValue value) => _sprintHeld = value.isPressed;

    public void OnCrouch(InputValue value) => _crouchHeld = value.isPressed;

    public void OnJump(InputValue value)
    {
        if (value.isPressed)
            _jumpQueued = true;
    }

    public void OnFlashlight(InputValue value)
    {
        if (value.isPressed)
            ToggleFlashlight();
    }

    void HandleCursor()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void UpdateLook()
    {
        float ySign = invertY ? 1f : -1f;
        float scale = lookSensitivity;
        if (_playerInput != null && _playerInput.currentControlScheme == "Gamepad")
            scale = stickLookSensitivity * Time.deltaTime;

        _yaw += _lookInput.x * scale;
        _pitch += _lookInput.y * scale * ySign;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    // Опрашиваем action напрямую (надёжнее, чем ловить сообщение об отпускании клавиши).
    bool CrouchHeld => _crouchAction != null ? _crouchAction.IsPressed() : _crouchHeld;
    bool SprintHeld => _sprintAction != null ? _sprintAction.IsPressed() : _sprintHeld;

    void UpdateStance(float dt)
    {
        bool wantCrouch = CrouchHeld;
        if (!wantCrouch && IsCrouching && !CanStand())
            wantCrouch = true;

        IsCrouching = wantCrouch;
        float targetHeight = IsCrouching ? crouchHeight : standingHeight;
        SetControllerHeight(Mathf.Lerp(_controller.height, targetHeight, 1f - Mathf.Exp(-stanceLerp * dt)), false);

        if (cameraPivot != null)
        {
            float targetCamY = IsCrouching ? crouchCameraHeight : standingCameraHeight;
            Vector3 local = cameraPivot.localPosition;
            local.y = Mathf.Lerp(local.y, targetCamY, 1f - Mathf.Exp(-stanceLerp * dt));
            cameraPivot.localPosition = local;
        }
    }

    void UpdateMotor(float dt)
    {
        Vector3 planar = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        if (planar.sqrMagnitude > 1f)
            planar.Normalize();

        bool grounded = _controller.isGrounded;
        if (grounded && !_wasGrounded)
            _landOffset = -landBob;

        bool wantsSprint = SprintHeld && !IsCrouching && planar.sqrMagnitude > 0.05f && _stamina > 0.05f;
        IsSprinting = wantsSprint;

        if (IsSprinting)
        {
            _stamina = Mathf.Max(0f, _stamina - staminaDrain * dt);
            _staminaCooldown = staminaRegenDelay;
            if (_stamina <= 0f)
                IsSprinting = false;
        }
        else
        {
            _staminaCooldown -= dt;
            if (_staminaCooldown <= 0f)
                _stamina = Mathf.Min(maxStamina, _stamina + staminaRegen * dt);
        }

        float targetSpeed = walkSpeed;
        if (IsCrouching)
            targetSpeed = crouchSpeed;
        else if (IsSprinting)
            targetSpeed = sprintSpeed;

        if (planar.sqrMagnitude < 0.0001f)
            targetSpeed = 0f;

        _currentSpeed = Mathf.Lerp(_currentSpeed, targetSpeed, 1f - Mathf.Exp(-acceleration * dt));
        Vector3 velocity = planar * _currentSpeed;

        if (grounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        if (_jumpQueued && grounded && !IsCrouching)
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpQueued = false;
        }
        else
        {
            _jumpQueued = false;
        }

        _verticalVelocity += gravity * dt;
        velocity.y = _verticalVelocity;
        _controller.Move(velocity * dt);

        UpdateFootsteps(dt, grounded, planar.magnitude);
        _wasGrounded = grounded;
    }

    void UpdateCameraMotion(float dt)
    {
        if (cameraPivot == null || playerCamera == null)
            return;

        float planarSpeed = new Vector2(_controller.velocity.x, _controller.velocity.z).magnitude;
        bool moving = _controller.isGrounded && planarSpeed > 0.15f;

        float bobAmount = bobWalkAmount;
        float bobFreq = bobWalkFrequency;
        if (IsCrouching)
        {
            bobAmount = bobCrouchAmount;
            bobFreq = bobWalkFrequency * 0.75f;
        }
        else if (IsSprinting)
        {
            bobAmount = bobSprintAmount;
            bobFreq = bobSprintFrequency;
        }

        // Плавно гасим амплитуду покачивания, когда игрок останавливается,
        // вместо резкого включения/выключения bob.
        float speedFactor = Mathf.Clamp01(planarSpeed / Mathf.Max(0.1f, walkSpeed));
        float bobWeight = moving ? speedFactor : 0f;

        _bobTimer += dt * bobFreq * (moving ? speedFactor : 0f);
        _idleTimer += dt * idleSwaySpeed;
        _landOffset = Mathf.Lerp(_landOffset, 0f, 1f - Mathf.Exp(-8f * dt));

        float bobY = Mathf.Sin(_bobTimer * Mathf.PI * 2f) * bobAmount * bobWeight;
        float bobX = Mathf.Cos(_bobTimer * Mathf.PI) * bobAmount * bobHorizontalScale * bobWeight;
        float swayY = Mathf.Sin(_idleTimer) * idleSway;
        float swayX = Mathf.Cos(_idleTimer * 0.73f) * idleSway * 0.6f;

        Transform cam = playerCamera.transform;
        Vector3 targetPos = new Vector3(bobX + swayX, bobY + swayY + _landOffset, 0f);
        float smoothTime = 1f / Mathf.Max(0.01f, cameraSmoothing);
        cam.localPosition = Vector3.SmoothDamp(cam.localPosition, targetPos, ref _camPosVelocity, smoothTime, Mathf.Infinity, dt);

        float targetRoll = Mathf.Cos(_bobTimer * Mathf.PI) * bobRollAmount * bobWeight;
        _camRoll = Mathf.Lerp(_camRoll, targetRoll, 1f - Mathf.Exp(-cameraSmoothing * dt));
        cam.localRotation = Quaternion.Euler(0f, 0f, _camRoll);

        float targetFov = IsSprinting ? sprintFov : baseFov;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-6f * dt));
    }

    void UpdateFootsteps(float dt, bool grounded, float moveAmount)
    {
        if (!grounded || moveAmount < 0.2f || footstepSource == null)
        {
            _stepTimer = 0f;
            return;
        }

        float interval = walkStepInterval;
        AudioClip[] set = walkFootsteps;
        if (IsCrouching)
            interval = crouchStepInterval;
        else if (IsSprinting)
        {
            interval = sprintStepInterval;
            if (sprintFootsteps != null && sprintFootsteps.Length > 0)
                set = sprintFootsteps;
        }

        _stepTimer += dt;
        if (_stepTimer < interval)
            return;

        _stepTimer = 0f;
        if (set == null || set.Length == 0)
            return;

        int index = Random.Range(0, set.Length);
        if (set.Length > 1 && index == _lastFootstepIndex)
            index = (index + 1) % set.Length;
        _lastFootstepIndex = index;

        AudioClip clip = set[index];
        if (clip != null)
            footstepSource.PlayOneShot(clip, IsCrouching ? 0.45f : 1f);
    }

    void UpdateFlashlight()
    {
        if (flashlight == null || !_flashlightOn)
            return;

        float intensity = flashlightBaseIntensity;
        if (flashlightFlicker)
            intensity += (Mathf.PerlinNoise(Time.time * 9.1f, 0.37f) - 0.5f) * 2f * flickerAmount;
        flashlight.intensity = intensity;
    }

    void ToggleFlashlight()
    {
        _flashlightOn = !_flashlightOn;
        ApplyFlashlightState();
    }

    void ApplyFlashlightState()
    {
        if (flashlight != null)
            flashlight.enabled = _flashlightOn;
    }

    bool CanStand()
    {
        float currentHeight = _controller.height;
        // Если и так почти в полный рост — вставать можно.
        if (standingHeight - currentHeight <= 0.02f)
            return true;

        GetStandCheckSphere(_controller, currentHeight, out Vector3 origin, out float radius, out float castDistance);

        // Проверяем ТОЛЬКО потолок прямо над головой (не стены сбоку).
        // SphereCastAll + ручной игнор всей иерархии игрока — не зависит от того,
        // какой именно коллайдер стоит на игроке и приходят ли сообщения ввода.
        var hits = Physics.SphereCastAll(origin, radius, Vector3.up, castDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == null)
                continue;
            if (t == transform || t.IsChildOf(transform))
                continue; // это сам игрок — игнорируем
            return false;  // над головой есть препятствие
        }
        return true;
    }

    // Параметры сферы проверки потолка (используются и логикой, и gizmo).
    void GetStandCheckSphere(CharacterController cc, float currentHeight, out Vector3 origin, out float radius, out float castDistance)
    {
        radius = Mathf.Max(0.05f, cc.radius - 0.05f);
        origin = transform.position + Vector3.up * (currentHeight - radius);
        castDistance = Mathf.Max(0f, standingHeight - currentHeight) + 0.05f;
    }

    void OnDrawGizmosSelected()
    {
        // Визуализация сферы проверки "можно ли встать": видно прямо над игроком.
        var cc = _controller != null ? _controller : GetComponent<CharacterController>();
        if (cc == null)
            return;

        GetStandCheckSphere(cc, cc.height, out Vector3 origin, out float radius, out float castDistance);
        Vector3 top = origin + Vector3.up * castDistance;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, radius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(top, radius);
        Gizmos.DrawLine(origin, top);
    }

    void SetControllerHeight(float height, bool instant)
    {
        height = Mathf.Max(height, _controller.radius * 2f + 0.05f);
        _controller.height = height;
        _controller.center = new Vector3(0f, height * 0.5f, 0f);
        if (instant && cameraPivot != null)
        {
            Vector3 local = cameraPivot.localPosition;
            local.y = standingCameraHeight;
            cameraPivot.localPosition = local;
        }
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
