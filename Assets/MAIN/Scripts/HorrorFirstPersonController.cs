using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class HorrorFirstPersonController : MonoBehaviour
{
    [SerializeField] Transform cameraPivot;
    [SerializeField] Camera playerCamera;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 2f;
    [SerializeField] float sprintSpeed = 5f;
    [SerializeField] float crouchSpeed = 1.2f;
    [SerializeField] float acceleration = 12f;
    [SerializeField] float gravity = -22f;
    [SerializeField] float jumpHeight = 1.05f;
    [Tooltip("Насколько глубоко щупаем землю под капсулой. Не используем CC.isGrounded — он срабатывает и от стен.")]
    [SerializeField] float groundProbeDistance = 0.18f;
    [Tooltip("Слои, которые считаются землёй.")]
    [SerializeField] LayerMask groundMask = ~0;

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
    [Tooltip("Если выключено, бег и прыжки не тратят выносливость.")]
    [SerializeField] bool useStamina;
    [SerializeField] float maxStamina = 4.5f;
    [SerializeField] float staminaDrain = 1.15f;
    [SerializeField] float staminaRegen = 0.65f;
    [SerializeField] float staminaRegenDelay = 0.8f;

    [Header("Camera feel")]
    [SerializeField] float baseFov = 65f;
    [SerializeField] float sprintFov = 73f;
    [SerializeField] float bobWalkAmount = 0.018f;
    [SerializeField] float bobSprintAmount = 0.032f;
    [SerializeField] float bobCrouchAmount = 0.01f;
    [SerializeField] float bobWalkFrequency = 1.55f;
    [SerializeField] float bobSprintFrequency = 2.15f;
    [SerializeField] float bobHorizontalScale = 0.72f;
    [SerializeField] float bobRollAmount = 0.55f;
    [SerializeField] float cameraSmoothing = 18f;
    [SerializeField] float idleSway = 0.0045f;
    [SerializeField] float idleSwaySpeed = 0.85f;
    [SerializeField] float landBob = 0.085f;
    [Tooltip("Сглаживание мыши (сек). Чуть больше нуля убирает цифровой дёрганый вид.")]
    [SerializeField] float lookSmoothTime = 0.018f;
    [Tooltip("Инерция камеры за поворотом (лаг взгляда).")]
    [SerializeField] float lookLag = 0.035f;
    [Tooltip("Наклон камеры при стрейфе (градусы).")]
    [SerializeField] float strafeLean = 1.35f;
    [Tooltip("Кивок камеры при разгоне/торможении (градусы).")]
    [SerializeField] float accelPitch = 1.8f;

    [Header("Footsteps")]
    [SerializeField] AudioSource footstepSource;
    [SerializeField] AudioSource impactSource;
    [SerializeField] AudioClip[] walkFootsteps;
    [SerializeField] AudioClip[] sprintFootsteps;
    [SerializeField] AudioClip[] jumpClips;
    [SerializeField] AudioClip[] landClips;
    [SerializeField] float walkStepInterval = 0.64f;
    [SerializeField] float sprintStepInterval = 0.32f;
    [SerializeField] float crouchStepInterval = 0.85f;

    [Header("Предметы")]
    [Tooltip("Насколько сильно капсула толкает лежащие предметы (CharacterController сам их не двигает).")]
    [SerializeField] float itemPushSpeed = 2.2f;

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
    float _airborneTime;
    float _moveAudioLock;
    bool _strideStarted;
    bool _jumpedThisAir;
    float _defaultStepOffset = 0.3f;
    int _lastFootstepIndex = -1;
    Vector3 _camPosVelocity;
    float _camRoll;
    Vector2 _lookSmoothed;
    Vector2 _lookSmoothVel;
    float _prevYaw;
    float _prevPitch;
    float _lagPitch;
    float _lagYaw;
    float _lagPitchVel;
    float _lagYawVel;
    float _lean;
    float _leanVel;
    float _accelTilt;
    float _accelTiltVel;
    float _prevPlanarSpeed;
    float _stepPunch;
    float _stepPunchVel;
    float _landOffsetVel;
    float _bobWeight;

    public float StaminaNormalized =>
        !useStamina || maxStamina <= 0f ? 1f : _stamina / maxStamina;
    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsGroundedNow => _wasGrounded;
    /// <summary>Сглаженная скорость взгляда (град/сек), для инерции предметов.</summary>
    public Vector2 LookVelocity { get; private set; }
    /// <summary>Скорость в локальных осях игрока (x стрейф, z вперёд).</summary>
    public Vector3 LocalPlanarVelocity { get; private set; }
    /// <summary>Фаза шага 0..1 (для покачивания предметов).</summary>
    public float StepCycle { get; private set; }
    public float BobWeight => _bobWeight;
    public float LandPunch => _landOffset;
    public float StepPunch => _stepPunch;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _defaultStepOffset = _controller.stepOffset;
        _playerInput = GetComponent<PlayerInput>();
        if (_playerInput != null && _playerInput.actions != null)
        {
            _crouchAction = _playerInput.actions.FindAction("Crouch", false);
            _sprintAction = _playerInput.actions.FindAction("Sprint", false);
        }
        _stamina = maxStamina;
        _yaw = transform.eulerAngles.y;
        _prevYaw = _yaw;
        _prevPitch = _pitch;

        if (cameraPivot == null)
        {
            var found = transform.Find("CameraPivot");
            if (found != null)
                cameraPivot = found;
        }

        if (playerCamera == null && cameraPivot != null)
            playerCamera = cameraPivot.GetComponentInChildren<Camera>();

        if (footstepSource == null)
            footstepSource = GetComponent<AudioSource>();
        if (footstepSource != null)
            footstepSource.spatialBlend = 0f;
        if (impactSource == null)
        {
            impactSource = gameObject.AddComponent<AudioSource>();
            impactSource.playOnAwake = false;
            impactSource.spatialBlend = 0f;
            impactSource.loop = false;
        }

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

        _lookSmoothed = Vector2.SmoothDamp(_lookSmoothed, _lookInput, ref _lookSmoothVel,
            lookSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

        _yaw += _lookSmoothed.x * scale;
        _pitch += _lookSmoothed.y * scale * ySign;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        LookVelocity = new Vector2((_yaw - _prevYaw) / dt, (_pitch - _prevPitch) / dt);
        _prevYaw = _yaw;
        _prevPitch = _pitch;

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        if (cameraPivot != null)
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    bool CrouchHeld => _crouchAction != null ? _crouchAction.IsPressed() : _crouchHeld;

    // Только реальный Shift (или стик геймпада). Action/OnSprint не используем:
    // Button-событие может «залипнуть», а Sprint ещё привязан к XR-триггеру.
    bool SprintHeld
    {
        get
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
                return true;

            if (_playerInput != null && _playerInput.currentControlScheme == "Gamepad")
            {
                var pad = Gamepad.current;
                if (pad != null && pad.leftStickButton.isPressed)
                    return true;
            }

            return false;
        }
    }

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

        // Свой probe, а не CC.isGrounded: у дома из брёвен боковой контакт
        // даёт ложный grounded и "присасывает" к стене в прыжке.
        bool grounded = IsOnWalkableGround();
        _controller.stepOffset = grounded ? _defaultStepOffset : 0f;

        if (grounded)
        {
            if (!_wasGrounded)
            {
                _landOffset = -landBob;
                PlayLandSound(Mathf.Abs(_verticalVelocity), _airborneTime);
                _jumpedThisAir = false;
            }
            _airborneTime = 0f;
        }
        else
            _airborneTime += dt;

        bool wantsSprint = SprintHeld && !IsCrouching && planar.sqrMagnitude > 0.05f;
        if (useStamina)
            wantsSprint &= _stamina > 0.05f;
        IsSprinting = wantsSprint;

        if (useStamina)
        {
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
            PlayJumpSound();
            _jumpedThisAir = true;
            _stepTimer = 0f;
        }
        else
        {
            _jumpQueued = false;
        }

        _verticalVelocity += gravity * dt;
        velocity.y = _verticalVelocity;

        Vector3 before = transform.position;
        _controller.Move(velocity * dt);

        // CC на выпуклых MeshCollider (брёвна дома) может вытолкнуть вверх,
        // как будто зашагивает на стену. В воздухе это отменяем.
        if (!grounded && _verticalVelocity <= 0f)
        {
            float lifted = transform.position.y - before.y;
            if (lifted > 0.001f)
                _controller.Move(new Vector3(0f, -lifted, 0f));
        }

        UpdateFootsteps(dt, grounded, planar.magnitude);
        Vector3 worldVel = _controller.velocity;
        LocalPlanarVelocity = transform.InverseTransformDirection(new Vector3(worldVel.x, 0f, worldVel.z));
        _wasGrounded = grounded;
    }

    /// <summary>
    /// Реально стоим на поверхности, по которой можно ходить (нормаль не круче slopeLimit).
    /// Стены и бока брёвен не считаются землёй — иначе прыжок рядом с домом прилипает.
    /// </summary>
    bool IsOnWalkableGround()
    {
        float radius = Mathf.Max(0.08f, _controller.radius * 0.85f);
        Vector3 center = transform.position + _controller.center;
        float toFeet = Mathf.Max(0.02f, _controller.center.y - radius);
        float maxDist = toFeet + groundProbeDistance;
        float minY = Mathf.Cos(_controller.slopeLimit * Mathf.Deg2Rad);

        // Каст из центра капсулы вниз — сфера не стартует внутри пола
        // (иначе SphereCast пропускает уже перекрытый коллайдер).
        var hits = Physics.SphereCastAll(center, radius, Vector3.down, maxDist,
            groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == null || t == transform || t.IsChildOf(transform))
                continue;
            if (hits[i].normal.y >= minY)
                return true;
        }

        if (Physics.Raycast(transform.position + Vector3.up * 0.28f, Vector3.down, out RaycastHit ray,
                0.42f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (ray.transform != transform && !ray.transform.IsChildOf(transform) && ray.normal.y >= minY)
                return true;
        }

        return false;
    }

    void UpdateCameraMotion(float dt)
    {
        if (cameraPivot == null || playerCamera == null)
            return;

        float planarSpeed = new Vector2(_controller.velocity.x, _controller.velocity.z).magnitude;
        bool moving = _wasGrounded && planarSpeed > 0.12f;

        float bobAmount = bobWalkAmount;
        float bobFreq = bobWalkFrequency;
        if (IsCrouching)
        {
            bobAmount = bobCrouchAmount;
            bobFreq = bobWalkFrequency * 0.82f;
        }
        else if (IsSprinting)
        {
            bobAmount = bobSprintAmount;
            bobFreq = bobSprintFrequency;
        }

        float speedFactor = Mathf.Clamp01(planarSpeed / Mathf.Max(0.15f, walkSpeed));
        float bobTarget = moving ? speedFactor : 0f;
        _bobWeight = Mathf.Lerp(_bobWeight, bobTarget, 1f - Mathf.Exp(-10f * dt));

        float prevCycle = _bobTimer;
        _bobTimer += dt * bobFreq * _bobWeight;
        StepCycle = Mathf.Repeat(_bobTimer, 1f);

        // Удар шага: в нижней точке цикла камера чуть проседает — как перенос веса.
        if (_bobWeight > 0.25f && _wasGrounded)
        {
            float prev = Mathf.Sin(prevCycle * Mathf.PI * 2f);
            float now = Mathf.Sin(_bobTimer * Mathf.PI * 2f);
            if (prev >= 0f && now < 0f)
                _stepPunch -= bobAmount * 1.65f;
        }

        _idleTimer += dt * idleSwaySpeed;
        _landOffset = Mathf.SmoothDamp(_landOffset, 0f, ref _landOffsetVel, 0.12f, Mathf.Infinity, dt);
        _stepPunch = Mathf.SmoothDamp(_stepPunch, 0f, ref _stepPunchVel, 0.09f, Mathf.Infinity, dt);

        float cycle = _bobTimer * Mathf.PI * 2f;
        float bobY = Mathf.Sin(cycle) * bobAmount * _bobWeight;
        float bobX = Mathf.Sin(cycle * 0.5f) * bobAmount * bobHorizontalScale * _bobWeight;
        // Лёгкая «восьмёрка» вместо голого синуса.
        bobY += Mathf.Sin(cycle * 2f) * bobAmount * 0.12f * _bobWeight;

        float breath = Mathf.Sin(_idleTimer * 1.15f) * idleSway * (1.15f - _bobWeight * 0.6f);
        float breathX = Mathf.Cos(_idleTimer * 0.67f) * idleSway * 0.45f;
        float micro = (Mathf.PerlinNoise(Time.time * 1.1f, 0.17f) - 0.5f) * idleSway * 0.35f;

        float sprintShake = 0f;
        if (IsSprinting && _wasGrounded)
            sprintShake = (Mathf.PerlinNoise(Time.time * 14f, 2.4f) - 0.5f) * 0.006f;

        Transform cam = playerCamera.transform;
        Vector3 targetPos = new Vector3(
            bobX + breathX + micro,
            bobY + breath + _landOffset + _stepPunch + sprintShake,
            sprintShake * 0.4f);

        float smoothTime = 1f / Mathf.Max(0.01f, cameraSmoothing);
        cam.localPosition = Vector3.SmoothDamp(cam.localPosition, targetPos, ref _camPosVelocity, smoothTime, Mathf.Infinity, dt);

        // Инерция взгляда — камера чуть отстаёт от поворота тела.
        float lagTargetPitch = Mathf.Clamp(-LookVelocity.y * lookLag * 0.012f, -2.2f, 2.2f);
        float lagTargetYaw = Mathf.Clamp(-LookVelocity.x * lookLag * 0.012f, -2.4f, 2.4f);
        _lagPitch = Mathf.SmoothDamp(_lagPitch, lagTargetPitch, ref _lagPitchVel, 0.08f, 40f, dt);
        _lagYaw = Mathf.SmoothDamp(_lagYaw, lagTargetYaw, ref _lagYawVel, 0.08f, 40f, dt);

        float leanTarget = Mathf.Clamp(-LocalPlanarVelocity.x / Mathf.Max(0.1f, sprintSpeed) * strafeLean, -strafeLean, strafeLean);
        _lean = Mathf.SmoothDamp(_lean, leanTarget, ref _leanVel, 0.14f, 30f, dt);

        float accel = (planarSpeed - _prevPlanarSpeed) / Mathf.Max(dt, 0.0001f);
        _prevPlanarSpeed = planarSpeed;
        float accelTarget = Mathf.Clamp(-accel * 0.035f * accelPitch, -accelPitch, accelPitch);
        if (!_wasGrounded)
            accelTarget += Mathf.Clamp(_verticalVelocity * 0.04f, -2.5f, 1.2f);
        _accelTilt = Mathf.SmoothDamp(_accelTilt, accelTarget, ref _accelTiltVel, 0.16f, 25f, dt);

        float walkRoll = Mathf.Sin(cycle * 0.5f) * bobRollAmount * _bobWeight;
        float lookRoll = Mathf.Clamp(-LookVelocity.x * 0.004f, -1.1f, 1.1f);
        float targetRoll = walkRoll + _lean + lookRoll;
        _camRoll = Mathf.Lerp(_camRoll, targetRoll, 1f - Mathf.Exp(-cameraSmoothing * dt));

        cam.localRotation = Quaternion.Euler(_lagPitch + _accelTilt, _lagYaw, _camRoll);

        float targetFov = IsSprinting ? sprintFov : baseFov;
        if (!_wasGrounded)
            targetFov += 1.4f;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-5.5f * dt));
    }

    void UpdateFootsteps(float dt, bool grounded, float moveAmount)
    {
        if (!grounded)
        {
            if (footstepSource != null && footstepSource.isPlaying)
                footstepSource.Stop();
            _stepTimer = 0f;
            _strideStarted = false;
            return;
        }

        if (moveAmount < 0.2f)
        {
            _stepTimer = 0f;
            _strideStarted = false;
            return;
        }

        if (Time.time < _moveAudioLock)
            return;

        float interval = walkStepInterval;
        if (IsCrouching)
            interval = crouchStepInterval;
        else if (IsSprinting)
            interval = sprintStepInterval;

        _stepTimer += dt;
        float need = _strideStarted ? interval : interval * 0.45f;
        if (_stepTimer < need)
            return;

        PlayFootstep();
        _stepTimer = 0f;
        _strideStarted = true;
    }

    void PlayFootstep()
    {
        if (Time.time < _moveAudioLock)
            return;
        AudioClip[] set = walkFootsteps;
        if (IsSprinting && sprintFootsteps != null && sprintFootsteps.Length > 0)
            set = sprintFootsteps;
        float volume = IsCrouching ? 0.4f : (IsSprinting ? 0.9f : 0.72f);
        float pitchLo = IsSprinting ? 0.98f : 0.92f;
        float pitchHi = IsSprinting ? 1.08f : 1.02f;
        PlayOn(footstepSource, set, volume, pitchLo, pitchHi, replace: true);
    }

    void PlayJumpSound()
    {
        if (footstepSource != null)
            footstepSource.Stop();
        PlayOn(impactSource != null ? impactSource : footstepSource,
            ClipSet(jumpClips, walkFootsteps), 0.8f, 1.05f, 1.16f, replace: false);
        _strideStarted = false;
    }

    void PlayLandSound(float impactSpeed, float airTime)
    {
        bool realJump = _jumpedThisAir;
        bool longFall = airTime >= 0.28f && impactSpeed >= 5f;
        if (!realJump && !longFall)
            return;

        if (footstepSource != null)
            footstepSource.Stop();

        PlayOn(impactSource != null ? impactSource : footstepSource,
            ClipSet(landClips, walkFootsteps), Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(4f, 11f, impactSpeed)),
            0.8f, 0.92f, replace: false);
        _moveAudioLock = Time.time + 0.1f;
        _stepTimer = 0f;
        _strideStarted = false;
    }

    static AudioClip[] ClipSet(AudioClip[] preferred, AudioClip[] fallback)
    {
        if (preferred != null && preferred.Length > 0)
            return preferred;
        return fallback;
    }

    void PlayOn(AudioSource source, AudioClip[] set, float volume, float pitchLo, float pitchHi, bool replace)
    {
        if (source == null || set == null || set.Length == 0)
            return;
        int index = Random.Range(0, set.Length);
        if (set.Length > 1 && index == _lastFootstepIndex)
            index = (index + 1) % set.Length;
        _lastFootstepIndex = index;
        AudioClip clip = set[index];
        if (clip == null)
            return;

        source.spatialBlend = 0f;
        source.loop = false;
        source.pitch = Random.Range(pitchLo, pitchHi);
        if (replace)
        {
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }
        else
            source.PlayOneShot(clip, volume);
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

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider != null ? hit.collider.attachedRigidbody : null;
        if (body == null || body.isKinematic)
            return;

        var item = hit.collider.GetComponentInParent<HeldItem>();
        if (item == null || item.IsCarried)
            return;

        // Стоим сверху — не вдавливаем в пол.
        if (hit.moveDirection.y < -0.2f)
            return;

        Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (push.sqrMagnitude < 0.0001f)
            return;

        if (body.linearVelocity.sqrMagnitude > 16f)
            return;

        push.Normalize();
        Vector3 v = body.linearVelocity;
        body.linearVelocity = new Vector3(
            push.x * itemPushSpeed,
            Mathf.Clamp(v.y, -2f, 1.2f),
            push.z * itemPushSpeed);
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
