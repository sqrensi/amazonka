using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
[DefaultExecutionOrder(80)]
public class HorrorFirstPersonController : MonoBehaviour
{
    [SerializeField] Transform cameraPivot;
    [SerializeField] Camera playerCamera;
    public Camera PlayerCam => playerCamera;

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
    float _heaveLockUntil;
    bool _heaving;
    Rigidbody _rideBoat;
    Vector3 _rideLocal;
    BoatPiece _rideHull;
    bool _rideIgnore;
    float _rideIgnoreUntil;
    float _offRaftUntil;
    float _launchSeatUntil;
    float _pickupSettleUntil;
    float _jumpLift;
    float _jumpLiftVel;
    float _jumpLiftPrev;
    Vector3 _rideLocalPrev;
    float _drownBoat;
    Vector3 _physPos;
    Vector3 _physPosPrev;
    bool _physReady;
    bool _wasAtSwimSurface;
    Vector3 _swimVel;
    int _lastFootstepIndex = -1;
    Vector3 _groundNrm = Vector3.up;
    float _slopeAngle;
    bool _onSteepTerrain;
    const float TerrainWalkSlope = 68f;
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
    float _coyoteTime;
    float _swimBobTime;
    float _airAudioTime;
    readonly Collider[] _groundOverlap = new Collider[32];

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
    /// <summary>Скорость ввода движения (для гребли на лодке).</summary>
    public Vector2 MoveInput => _moveInput;
    public bool AirborneNow => _jumpedThisAir || _airborneTime > 0.1f;
    public float YawDegrees => _yaw;
    public bool IsSwimming { get; private set; }
    public bool IsOnCraft => _rideBoat != null && !IsSwimming;

    public void ApplyGroundLurch(Vector3 delta)
    {
        if (_controller == null || !_controller.enabled)
            return;
        if (delta.sqrMagnitude < 0.0000001f)
            return;
        _controller.Move(delta);
        _physPos += delta;
        _physPosPrev += delta;
    }

    public void WarpTo(Vector3 pos, Quaternion rot)
    {
        ReleaseBoatFollow();
        if (_controller != null)
            _controller.enabled = false;
        transform.SetPositionAndRotation(pos, rot);
        SyncPhysPose();
        if (_controller != null)
            _controller.enabled = true;
        Physics.SyncTransforms();
    }

    bool HoldingLaunchSeat => Time.unscaledTime < _launchSeatUntil && _rideBoat != null;

    public void ArmLaunchSeat(float seconds)
    {
        _launchSeatUntil = Time.unscaledTime + Mathf.Max(0.2f, seconds);
        _offRaftUntil = 0f;
        IsSwimming = false;
        _heaving = false;
        _jumpedThisAir = false;
    }
    /// <summary>Игрок сидит на лодке — обычный мотор капсулы выключен.</summary>
    public bool MovementLocked { get; set; }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _controller.enableOverlapRecovery = true;
        _controller.slopeLimit = Mathf.Max(_controller.slopeLimit, TerrainWalkSlope);
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

        if (playerCamera != null && playerCamera.GetComponent<UnderwaterFx>() == null)
            playerCamera.gameObject.AddComponent<UnderwaterFx>();
        if (playerCamera != null && playerCamera.GetComponent<AtmosphereFog>() == null)
            playerCamera.gameObject.AddComponent<AtmosphereFog>();
        if (playerCamera != null && playerCamera.GetComponent<GameLook>() == null)
            playerCamera.gameObject.AddComponent<GameLook>();

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
        if (!PlaySession.MenuOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (playerCamera != null)
            playerCamera.fieldOfView = baseFov;
        SyncPhysPose();
    }

    void Update()
    {
        HandleCursor();
        if (_rideBoat != null)
            ApplyBoatFollow();
        else
            DrawPhysInterp();
        UpdateLook();
        UpdateStanceView(Time.deltaTime);
        UpdateCameraMotion(Time.deltaTime);
    }

    void LateUpdate()
    {
        if (_rideBoat != null)
            ApplyBoatFollow();
        else
            DrawPhysInterp();
    }

    void FixedUpdate()
    {
        if (_rideBoat != null)
        {
            _rideLocalPrev = _rideLocal;
            _jumpLiftPrev = _jumpLift;
        }
        if (_rideBoat == null)
            ApplyPhysBody();
        UpdateStanceBody(Time.fixedDeltaTime);
        UpdateMotor(Time.fixedDeltaTime);
        if (_rideBoat == null)
            CapturePhysBody();
        else
            SyncPhysPose();
    }

    void ApplyDeckWeight()
    {
    }

    void PunchDeck(float downImpulse)
    {
    }

    void SyncPhysPose()
    {
        _physPos = transform.position;
        _physPosPrev = _physPos;
        _physReady = true;
    }

    void ApplyPhysBody()
    {
        if (!_physReady)
            SyncPhysPose();
        transform.position = _physPos;
    }

    void CapturePhysBody()
    {
        _physPosPrev = _physPos;
        _physPos = transform.position;
        _physReady = true;
    }

    void DrawPhysInterp()
    {
        if (!_physReady || _rideBoat != null)
            return;
        transform.position = Vector3.Lerp(_physPosPrev, _physPos, SimTime.VisualAlpha());
    }

    void WriteRideLocal(Vector3 local)
    {
        _rideLocal = local;
        _rideLocalPrev = local;
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
        if (PlaySession.MenuOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

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
        if (PlaySession.MenuOpen)
            return;
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

    void UpdateStanceView(float dt)
    {
        bool wantCrouch = CrouchHeld;
        if (!wantCrouch && IsCrouching && !CanStand())
            wantCrouch = true;
        IsCrouching = wantCrouch;

        if (cameraPivot != null)
        {
            float targetCamY = IsCrouching ? crouchCameraHeight : standingCameraHeight;
            Vector3 local = cameraPivot.localPosition;
            local.y = Mathf.Lerp(local.y, targetCamY, 1f - Mathf.Exp(-stanceLerp * dt));
            cameraPivot.localPosition = local;
        }
    }

    void UpdateStanceBody(float dt)
    {
        bool wantCrouch = CrouchHeld;
        if (!wantCrouch && IsCrouching && !CanStand())
            wantCrouch = true;
        IsCrouching = wantCrouch;
        float targetHeight = IsCrouching ? crouchHeight : standingHeight;
        SetControllerHeight(Mathf.Lerp(_controller.height, targetHeight, 1f - Mathf.Exp(-stanceLerp * dt)), false);
    }

    void UpdateMotor(float dt)
    {
        if (_rideIgnore && _rideBoat == null && Time.time >= _rideIgnoreUntil)
        {
            if (_rideHull != null && _controller != null)
                BoatBuildUtil.SetActorIgnoreIsland(_controller, _rideHull, false);
            _rideIgnore = false;
            _rideHull = null;
        }
        if (MovementLocked)
        {
            _jumpQueued = false;
            IsSwimming = false;
            _heaving = false;
            return;
        }

        bool onDeck = TryBoatDeck(out float deckY, out _);
        bool overWater = BoatWater.TryHeight(transform.position, out float waterY);
        if (onDeck && overWater && deckY < waterY - 0.16f)
            onDeck = false;
        if (HoldingLaunchSeat)
            onDeck = true;
        if (_rideBoat != null && _jumpedThisAir)
            onDeck = false;
        bool grounded = IsOnWalkableGround() || onDeck;
        float feetY = transform.position.y;
        float camLocalY = cameraPivot != null ? cameraPivot.localPosition.y : standingCameraHeight;
        float floatFeet = waterY - camLocalY + 0.32f;
        bool inWater = !onDeck && overWater && waterY > feetY + 0.06f;
        bool wasSwimming = IsSwimming;
        bool swimming = !onDeck && overWater && !grounded &&
            (feetY < waterY - 0.06f || _heaving || (wasSwimming && feetY < waterY + 0.9f && !_jumpedThisAir));
        if (_rideBoat != null)
            swimming = false;
        IsSwimming = swimming;
        if (swimming)
        {
            _airborneTime = 0f;
            _jumpedThisAir = false;
        }
        float ledgeY = 0f;
        Vector3 planar = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        if (planar.sqrMagnitude > 1f)
            planar.Normalize();
        bool nearLedge = swimming && TryFindWaterExit(planar.sqrMagnitude > 0.04f ? planar : transform.forward, waterY, out ledgeY);
        _controller.stepOffset = onDeck
            ? _defaultStepOffset
            : !swimming || _heaving || nearLedge
                ? Mathf.Max(_defaultStepOffset, (_heaving || nearLedge) ? 0.62f : _defaultStepOffset)
                : 0f;

        if (grounded)
        {
            _heaving = false;
            _swimVel = Vector3.zero;
            _wasAtSwimSurface = false;
            _coyoteTime = 0.14f;
            if (!_wasGrounded)
            {
                PunchDeck(Mathf.Clamp(Mathf.Abs(_verticalVelocity) * 0.85f, 3.5f, 12f));
                _landOffset = -landBob;
                PlayLandSound(Mathf.Abs(_verticalVelocity), _airborneTime);
                _jumpedThisAir = false;
            }
            _airborneTime = 0f;
        }
        else
        {
            _airborneTime += dt;
            _coyoteTime -= dt;
        }

        bool wantsSprint = SprintHeld && !IsCrouching && planar.sqrMagnitude > 0.05f;
        if (useStamina)
            wantsSprint &= _stamina > 0.05f;
        float quakeMove = HouseBuildMode.MoveScale;
        if (quakeMove < 0.84f)
            wantsSprint = false;
        if (_onSteepTerrain && _slopeAngle > 26f)
            wantsSprint = false;
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
        if (IsCrouching && !swimming)
            targetSpeed = crouchSpeed;
        else if (IsSprinting)
            targetSpeed = sprintSpeed;
        if (swimming)
            targetSpeed = IsSprinting ? 2.35f : 1.65f;
        else if (inWater)
            targetSpeed *= 0.85f;
        if (!swimming && quakeMove < 0.999f)
            targetSpeed *= quakeMove;

        if (!swimming && planar.sqrMagnitude < 0.0001f)
            targetSpeed = 0f;

        _currentSpeed = Mathf.Lerp(_currentSpeed, targetSpeed, 1f - Mathf.Exp(-acceleration * dt));
        Vector3 velocity = planar * _currentSpeed;

        bool wantJump = _jumpQueued && !IsCrouching;
        if (_rideBoat != null && RideDeck(dt, planar, wantJump))
            return;

        if (IsSwimming || swimming)
        {
            _jumpedThisAir = false;
            if (!wasSwimming)
            {
                _swimVel = planar * _currentSpeed;
                _swimVel.y = Mathf.Max(_swimVel.y, Mathf.Clamp(_verticalVelocity, -4.2f, 3.5f));
            }
            UpdateSwim(dt, waterY, floatFeet, nearLedge, planar);
            return;
        }

        _heaving = false;

        if (grounded && _verticalVelocity < 0f && !_onSteepTerrain)
            _verticalVelocity = -2f;

        if (_jumpQueued && !IsCrouching && _coyoteTime > 0f)
        {
            float jh = jumpHeight;
            if (_onSteepTerrain)
                jh *= Mathf.Lerp(1f, 0.72f, Mathf.InverseLerp(22f, 58f, _slopeAngle));
            _verticalVelocity = Mathf.Sqrt(jh * -2f * gravity);
            velocity = planar * (_currentSpeed * 0.82f);
            _jumpQueued = false;
            PlayJumpSound();
            _jumpedThisAir = true;
            _coyoteTime = 0f;
            _stepTimer = 0f;
            PunchDeck(6.5f);
        }
        else
        {
            _jumpQueued = false;
            if (grounded && !_jumpedThisAir)
                velocity = SlopeWalk(planar, _currentSpeed);
        }

        if (_jumpedThisAir || !grounded)
        {
            if (overWater && !grounded && _verticalVelocity < 0f && feetY < waterY + 0.45f)
            {
                _verticalVelocity += gravity * 0.55f * dt;
                if (_verticalVelocity < -4.5f)
                    _verticalVelocity = Mathf.Lerp(_verticalVelocity, -3.2f, 1f - Mathf.Exp(-6f * dt));
            }
            else
                _verticalVelocity += gravity * dt;
            velocity.y = _verticalVelocity;
        }
        else if (!_onSteepTerrain)
        {
            _verticalVelocity += gravity * dt;
            velocity.y = _verticalVelocity;
        }
        else
            _verticalVelocity = velocity.y;

        Vector3 before = transform.position;
        _controller.Move(velocity * dt);

        // CC на выпуклых MeshCollider (брёвна дома) может вытолкнуть вверх,
        // как будто зашагивает на стену. В воздухе это отменяем.
        bool settlePickup = Time.time < _pickupSettleUntil;
        if (settlePickup || (!grounded && _verticalVelocity <= 0f && !overWater))
        {
            float lifted = transform.position.y - before.y;
            if (lifted > 0.001f)
                _controller.Move(new Vector3(0f, -lifted, 0f));
        }
        if (settlePickup && _verticalVelocity > 0f)
            _verticalVelocity = 0f;
        RestoreOverlapAfterPickup();

        UpdateFootsteps(dt, grounded, planar.magnitude);
        Vector3 worldVel = _controller.velocity;
        LocalPlanarVelocity = transform.InverseTransformDirection(new Vector3(worldVel.x, 0f, worldVel.z));
        _wasGrounded = grounded;
        if (!settlePickup && _rideBoat == null && !_jumpedThisAir)
            SeparateFromBoatPieces(false);
        if (!_jumpedThisAir)
            CaptureBoatFollow();
    }

    void UpdateSwim(float dt, float waterY, float floatFeet, bool nearLedge, Vector3 planar)
    {
        if (Time.time >= _offRaftUntil && TryBoatDeckAt(transform.position, out float deckY, out BoatPiece deck, true, true)
            && deck != null
            && transform.position.y > deckY - 0.28f
            && !(BoatWater.TryHeight(transform.position, out float wy) && deckY < wy - 0.45f))
        {
            if (transform.position.y < deckY - 0.03f)
                _controller.Move(Vector3.up * (deckY - transform.position.y));
            IsSwimming = false;
            _heaving = false;
            _verticalVelocity = -2f;
            _swimVel = Vector3.zero;
            _wasGrounded = true;
            BindBoatFollow(deck);
            if (_rideBoat != null)
                WriteRideLocal(_rideBoat.transform.InverseTransformPoint(transform.position));
            return;
        }
        if (_rideBoat != null)
            LeaveRaft();
        Transform look = playerCamera != null ? playerCamera.transform : (cameraPivot != null ? cameraPivot : transform);
        Vector3 right = look.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;
        right.Normalize();

        Vector3 wish = look.forward * _moveInput.y + right * _moveInput.x;
        bool space = _jumpQueued || (Keyboard.current != null && Keyboard.current.spaceKey.isPressed);
        bool atSurface = transform.position.y >= floatFeet - 0.28f && transform.position.y <= floatFeet + 0.28f;

        if (!_heaving && space && atSurface && _wasAtSwimSurface && Time.time >= _heaveLockUntil)
        {
            _heaving = true;
            _heaveLockUntil = Time.time + 1.7f;
            _verticalVelocity = Mathf.Sqrt(2f * 7.5f * 0.7f);
            _swimVel.y = _verticalVelocity;
        }
        _jumpQueued = false;

        if (_heaving)
        {
            Vector3 air = planar * Mathf.Min(_currentSpeed, 1.8f);
            float heaveG = _verticalVelocity > 0f ? -7.5f : -6.2f;
            _verticalVelocity += heaveG * dt;
            if (_verticalVelocity < -2.8f)
                _verticalVelocity = Mathf.Lerp(_verticalVelocity, -2.4f, 1f - Mathf.Exp(-4.5f * dt));
            _swimVel.x = air.x;
            _swimVel.z = air.z;
            _swimVel.y = _verticalVelocity;
            Vector3 heaveBefore = transform.position;
            _controller.Move((_swimVel + SwimCurrent()) * dt);
            ClampPickupLift(heaveBefore);
            if (Time.time >= _pickupSettleUntil)
                SeparateFromBoatPieces(true);
            if (IsOnWalkableGround() || TryBoatDeck(out _, out _))
            {
                IsSwimming = false;
                _heaving = false;
                _verticalVelocity = -2f;
                _swimVel = Vector3.zero;
                _wasGrounded = true;
            }
            else
            {
                _wasGrounded = false;
                if (transform.position.y <= floatFeet + 0.12f && _verticalVelocity <= 0f)
                    _heaving = false;
            }
            FinishSwimMove(planar);
            return;
        }

        if (_swimVel.y < -1.15f)
            _swimVel.y = Mathf.Lerp(_swimVel.y, -0.35f, 1f - Mathf.Exp(-6.5f * dt));

        if (space && !atSurface)
            wish += Vector3.up * 1.15f;
        if (CrouchHeld)
            wish += Vector3.down * 1.2f;
        if (wish.sqrMagnitude > 1f)
            wish.Normalize();

        float speed = _moveInput.sqrMagnitude > 0.04f || space || CrouchHeld
            ? (IsSprinting ? 2.35f : 1.65f)
            : 0f;
        Vector3 target = wish * speed;

        float depth = floatFeet - transform.position.y;
        float buoy = Mathf.Clamp(depth * 2.6f, -1.15f, 4.4f);
        if (wish.y < -0.15f)
            buoy *= 0.18f;
        else if (wish.sqrMagnitude > 0.08f)
            buoy *= 0.55f;
        if (depth > 0.6f && wish.y >= -0.05f)
            buoy = Mathf.Max(buoy, 2.2f);
        target.y += buoy;

        float accel = 4.2f;
        _swimVel = Vector3.Lerp(_swimVel, target, 1f - Mathf.Exp(-accel * dt));
        _swimVel *= 1f - Mathf.Clamp01(1.15f * dt);

        if (transform.position.y > floatFeet + 0.1f && _swimVel.y > 0f)
            _swimVel.y = Mathf.Min(_swimVel.y, (floatFeet - transform.position.y) * 4.5f);

        _verticalVelocity = _swimVel.y;
        Vector3 swimMove = _swimVel + SwimCurrent();
        Vector3 swimBefore = transform.position;
        _controller.Move(swimMove * dt);
        ClampPickupLift(swimBefore);
        if (Time.time >= _pickupSettleUntil)
            SeparateFromBoatPieces(nearLedge);
        if (transform.position.y > floatFeet + 0.18f)
        {
            float pull = (transform.position.y - floatFeet) * Mathf.Min(1f, 3.2f * dt);
            _controller.Move(Vector3.down * pull);
            _swimVel.y = Mathf.Min(_swimVel.y, -0.15f);
            _verticalVelocity = _swimVel.y;
        }

        _wasGrounded = false;
        _wasAtSwimSurface = atSurface && !_heaving;
        FinishSwimMove(planar);
    }

    Vector3 SwimCurrent()
    {
        if (_rideBoat != null || MovementLocked)
            return Vector3.zero;
        Vector3 cur = BoatWater.CurrentAt(transform.position);
        cur.y = 0f;
        return cur * 0.28f;
    }

    void FinishSwimMove(Vector3 planar)
    {
        _currentSpeed = new Vector2(_swimVel.x, _swimVel.z).magnitude;
        UpdateFootsteps(Time.deltaTime, false, planar.magnitude);
        LocalPlanarVelocity = transform.InverseTransformDirection(new Vector3(_controller.velocity.x, 0f, _controller.velocity.z));
    }

    bool TryFindWaterExit(Vector3 planar, float waterY, out float ledgeY)
    {
        ledgeY = transform.position.y;
        Vector3 fwd = planar.sqrMagnitude > 0.04f ? planar.normalized : transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f)
            return false;
        fwd.Normalize();

        float feet = transform.position.y;
        float minNy = Mathf.Cos(_controller.slopeLimit * Mathf.Deg2Rad);
        float best = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < 6; i++)
        {
            float dist = 0.22f + i * 0.14f;
            Vector3 origin = transform.position + fwd * dist + Vector3.up * 1.4f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.7f, groundMask, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            if (hit.collider == null || hit.collider.GetComponentInParent<BoatWater>() != null)
                continue;
            bool boat = BoatPart.FromCollider(hit.collider) != null;
            if (boat)
                continue;
            if (!boat && hit.point.y <= waterY + 0.08f)
                continue;
            if (hit.point.y < feet - 0.05f || hit.point.y > feet + 1.2f)
                continue;
            if (hit.point.y > waterY + 0.95f)
                continue;
            if (hit.point.y > best)
            {
                best = hit.point.y;
                found = true;
            }
        }

        if (!found)
            return false;
        ledgeY = best;
        return true;
    }

    void SeparateFromBoatPieces(bool climbing)
    {
        if (_controller == null)
            return;
        float r = Mathf.Max(0.08f, _controller.radius * 0.92f);
        Vector3 p1 = transform.position + Vector3.up * r;
        Vector3 p2 = transform.position + Vector3.up * Mathf.Max(r + 0.05f, _controller.height - r);
        int n = Physics.OverlapCapsuleNonAlloc(p1, p2, r, _groundOverlap, groundMask, QueryTriggerInteraction.Ignore);
        Vector3 push = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            var col = _groundOverlap[i];
            if (col == null || BoatPart.FromCollider(col) == null)
                continue;
            if (col.GetComponentInParent<BoatIgnoreActors>() != null)
                continue;
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;
            if (!Physics.ComputePenetration(
                    _controller, transform.position, transform.rotation,
                    col, col.transform.position, col.transform.rotation,
                    out Vector3 dir, out float dist) || dist <= 0.001f)
                continue;
            if (!climbing && dir.y > 0.25f)
            {
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                {
                    dir = transform.position - col.bounds.center;
                    dir.y = 0f;
                }
                if (dir.sqrMagnitude < 0.0001f)
                    continue;
                dir.Normalize();
            }
            push += dir * (dist + 0.03f);
        }
        if (push.sqrMagnitude > 0.0001f)
            _controller.Move(push);
    }

    Vector3 SlopeWalk(Vector3 planar, float speed)
    {
        if (!_onSteepTerrain)
            return planar * speed;

        Vector3 n = _groundNrm.sqrMagnitude > 0.01f ? _groundNrm.normalized : Vector3.up;
        Vector3 along = Vector3.ProjectOnPlane(planar.sqrMagnitude > 0.0001f ? planar : Vector3.zero, n);
        if (along.sqrMagnitude > 0.0001f)
            along = along.normalized * speed;

        float steep = Mathf.InverseLerp(10f, 50f, _slopeAngle);
        float grade = along.sqrMagnitude > 0.0001f ? Vector3.Dot(along.normalized, Vector3.up) : 0f;
        if (grade > 0.02f)
            along *= Mathf.Lerp(1f, 0.16f, steep);
        else if (grade < -0.02f)
            along *= Mathf.Lerp(1f, 0.7f, steep);

        Vector3 slide = Vector3.ProjectOnPlane(Vector3.down, n);
        if (planar.sqrMagnitude < 0.02f && slide.sqrMagnitude > 0.0001f)
        {
            slide.Normalize();
            along += slide * Mathf.Lerp(0f, 0.85f, Mathf.InverseLerp(28f, 55f, _slopeAngle));
        }

        along.y -= 2.4f;
        return along;
    }

    /// <summary>
    /// Реально стоим на поверхности, по которой можно ходить.
    /// Стены и бока брёвен не считаются землёй — иначе прыжок рядом с домом прилипает.
    /// Склон террейна до TerrainWalkSlope — ходим, только медленнее.
    /// </summary>
    bool IsOnWalkableGround()
    {
        _groundNrm = Vector3.up;
        _slopeAngle = 0f;
        _onSteepTerrain = false;
        if (TryBoatDeck(out _, out _))
            return true;
        float radius = Mathf.Max(0.08f, _controller.radius * 0.85f);
        Vector3 center = transform.position + _controller.center;
        float toFeet = Mathf.Max(0.02f, _controller.center.y - radius);
        float maxDist = toFeet + groundProbeDistance;

        var hits = Physics.SphereCastAll(center, radius, Vector3.down, maxDist,
            groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == null || t == transform || t.IsChildOf(transform))
                continue;
            if (!AcceptStand(hits[i].collider, hits[i].point, hits[i].normal))
                continue;
            return true;
        }

        if (Physics.Raycast(transform.position + Vector3.up * 0.28f, Vector3.down, out RaycastHit ray,
                0.55f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (ray.transform != transform && !ray.transform.IsChildOf(transform) &&
                AcceptStand(ray.collider, ray.point, ray.normal))
                return true;
        }

        Vector3 feet = transform.position + Vector3.up * 0.12f;
        int n = Physics.OverlapSphereNonAlloc(feet, radius + 0.06f, _groundOverlap, groundMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = _groundOverlap[i];
            if (col == null || col.transform == transform || col.transform.IsChildOf(transform))
                continue;
            Vector3 p = BoatBuildUtil.ClosestPoint(col, feet);
            Vector3 away = feet - p;
            if (away.sqrMagnitude < 0.0001f)
                continue;
            if (AcceptStand(col, p, away.normalized))
                return true;
        }

        return false;
    }

    bool AcceptStand(Collider col, Vector3 point, Vector3 nrm)
    {
        if (nrm.sqrMagnitude < 0.0001f)
            nrm = Vector3.up;
        nrm.Normalize();
        bool boat = col != null && BoatPart.FromCollider(col) != null;
        float minY = boat
            ? Mathf.Cos(48f * Mathf.Deg2Rad)
            : Mathf.Cos(TerrainWalkSlope * Mathf.Deg2Rad);
        if (!IsStandSurface(col, point, nrm.y >= minY))
            return false;
        _groundNrm = nrm;
        _slopeAngle = Vector3.Angle(nrm, Vector3.up);
        _onSteepTerrain = !boat && _slopeAngle > 17f;
        return true;
    }

    public bool RidesIsland(BoatPiece piece)
    {
        if (piece == null)
            return false;
        if (_rideBoat != null)
        {
            if (_rideHull != null && piece.SharesIslandWith(_rideHull))
                return true;
            if (piece.IslandRootBody() == _rideBoat)
                return true;
        }
        if (IsSwimming)
            return false;
        return TryBoatDeck(out _, out BoatPiece hull) && hull != null && hull.SharesIslandWith(piece);
    }

    public void BindBoatFollow()
    {
        _offRaftUntil = 0f;
        CaptureBoatFollow();
    }

    public void RetargetBoatFollow(BoatPiece hull)
    {
        if (hull == null)
            return;
        Rigidbody boat = hull.IslandRootBody() ?? hull.Body;
        if (boat == null)
            return;
        if (boat == _rideBoat)
        {
            _rideHull = hull;
            return;
        }
        if (_rideHull != null && hull.SharesIslandWith(_rideHull))
        {
            Vector3 world = transform.position;
            _rideHull = hull;
            _rideBoat = boat;
            WriteRideLocal(boat.transform.InverseTransformPoint(world));
            return;
        }
        BindBoatFollow(hull);
    }

    public void BindBoatFollow(BoatPiece hull)
    {
        _offRaftUntil = 0f;
        if (hull == null)
            return;
        Rigidbody boat = hull.IslandRootBody() ?? hull.Body;
        if (boat == null)
            return;
        if (boat == _rideBoat)
        {
            _rideHull = hull;
            if (_controller != null && !_rideIgnore)
            {
                BoatBuildUtil.SetActorIgnoreIsland(_controller, hull, true);
                _rideIgnore = true;
            }
            return;
        }
        Vector3 world = transform.position;
        ClearBoatFollow();
        _rideHull = hull;
        _rideBoat = boat;
        WriteRideLocal(boat.transform.InverseTransformPoint(world));
        if (_controller == null)
            return;
        BoatBuildUtil.SetActorIgnoreIsland(_controller, hull, true);
        _rideIgnore = true;
    }

    public void ReleaseBoatFollow()
    {
        ClearBoatFollow();
    }

    /// <summary>
    /// Подбор детали, на которой стоишь: не тащить игрока вместе с куском и не дать CC вытолкнуть вверх.
    /// </summary>
    public void SuppressPickupLaunch()
    {
        _jumpLift = 0f;
        _jumpLiftVel = 0f;
        _drownBoat = 0f;
        ClearBoatFollow(true);
        _offRaftUntil = Time.time + 0.28f;
        _jumpedThisAir = false;
        _verticalVelocity = Mathf.Min(_verticalVelocity, 0f);
        _swimVel.y = Mathf.Min(_swimVel.y, 0f);
        _pickupSettleUntil = Time.time + 0.38f;
        if (_controller != null)
            _controller.enableOverlapRecovery = false;
    }

    void ClampPickupLift(Vector3 before)
    {
        if (_controller == null || Time.time >= _pickupSettleUntil)
            return;
        float lifted = transform.position.y - before.y;
        if (lifted > 0.001f)
            _controller.Move(new Vector3(0f, -lifted, 0f));
        if (_verticalVelocity > 0f)
            _verticalVelocity = 0f;
        _swimVel.y = Mathf.Min(_swimVel.y, 0f);
        RestoreOverlapAfterPickup();
    }

    void RestoreOverlapAfterPickup()
    {
        if (_controller == null)
            return;
        if (Time.time >= _pickupSettleUntil && !_controller.enableOverlapRecovery)
            _controller.enableOverlapRecovery = true;
    }

    const float DeckReach = 0.24f;
    readonly RaycastHit[] _deckHits = new RaycastHit[16];

    bool RideDeck(float dt, Vector3 planar, bool wantJump)
    {
        if (MovementLocked)
            return false;
        if (Time.time < _offRaftUntil && _rideBoat == null)
            return false;
        if (_rideBoat != null && TryDetachIfSinking(dt))
            return false;
        if (_rideHull != null && !_rideHull.CanRide() && !HoldingLaunchSeat
            && !TryBoatDeckAt(transform.position, out _, out _, true, true))
            return false;
        if (!TryBoatDeck(out _, out _) && _rideBoat == null)
            return false;
        if (!(_jumpedThisAir && _rideBoat != null))
            CaptureBoatFollow();
        if (_rideBoat == null)
            return false;

        Vector3 worldStep = planar * _currentSpeed * dt;
        Vector3 localStep = _rideBoat.transform.InverseTransformDirection(worldStep);
        localStep.y = 0f;

        if (wantJump && !_jumpedThisAir)
        {
            _jumpedThisAir = true;
            _jumpLift = 0.02f;
            _jumpLiftVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpQueued = false;
            PlayJumpSound();
            _coyoteTime = 0f;
            _stepTimer = 0f;
        }

        if (_jumpedThisAir)
        {
            _jumpLiftVel += gravity * dt;
            _jumpLift += _jumpLiftVel * dt;
            _rideLocal += localStep;
            if (_jumpLift > 0f)
            {
                _heaving = false;
                IsSwimming = false;
                LocalPlanarVelocity = transform.InverseTransformDirection(new Vector3(worldStep.x, 0f, worldStep.z) / Mathf.Max(dt, 0.0001f));
                return true;
            }
            _jumpLift = 0f;
            _jumpLiftVel = 0f;
            _jumpedThisAir = false;
            Vector3 next = _rideBoat.transform.TransformPoint(_rideLocal);
            if (TryBoatDeckAt(next, out _, out BoatPiece landHull, true, true) && landHull != null)
            {
                CatchRideAt(next, landHull);
                return true;
            }
            LeaveRaft();
            return false;
        }

        Vector3 proposed = _rideLocal + localStep;
        Vector3 nextWorld = _rideBoat.transform.TransformPoint(proposed);
        if (TryBoatDeckAt(nextWorld, out _, out BoatPiece walkHull, true, true) && walkHull != null)
        {
            if (_rideHull != null && walkHull.SharesIslandWith(_rideHull))
            {
                _rideLocal = proposed;
                PlaceOnRide(walkHull, dt, planar, worldStep);
            }
            else if (!TryLandOnFloater(nextWorld, dt, planar, worldStep))
            {
                _rideLocal = proposed;
                PlaceOnRide(walkHull, dt, planar, worldStep);
            }
            return true;
        }

        if (planar.sqrMagnitude > 0.04f)
        {
            LeaveRaft();
            if (worldStep.sqrMagnitude > 0.0000001f)
                _controller.Move(worldStep);
            return false;
        }
        PlaceOnRide(_rideHull, dt, planar, Vector3.zero);
        return true;
    }

    bool TryDetachIfSinking(float dt)
    {
        if (_rideHull == null || _jumpedThisAir || HoldingLaunchSeat)
        {
            _drownBoat = 0f;
            return false;
        }
        if (!_rideHull.DeckSubmerged(0.55f))
        {
            _drownBoat = 0f;
            return false;
        }
        _drownBoat += dt;
        if (_drownBoat < 0.4f)
            return false;
        LeaveRaft();
        IsSwimming = true;
        _heaving = false;
        _swimVel.y = Mathf.Max(_swimVel.y, 2.4f);
        _verticalVelocity = Mathf.Max(_verticalVelocity, 2.4f);
        _drownBoat = 0f;
        return true;
    }

    void CatchRideAt(Vector3 world, BoatPiece hull)
    {
        if (hull == null || _rideBoat == null && hull == null)
            return;
        if (_rideHull == null || !hull.SharesIslandWith(_rideHull) || _rideBoat == null)
            BindBoatFollow(hull);
        if (_rideBoat == null)
            return;
        _rideHull = hull;
        WriteRideLocal(_rideBoat.transform.InverseTransformPoint(world));
        _verticalVelocity = 0f;
        _jumpLift = 0f;
        _jumpLiftVel = 0f;
        _jumpedThisAir = false;
        IsSwimming = false;
        _wasGrounded = true;
        _airborneTime = 0f;
    }

    bool TryLandOnFloater(Vector3 world, float dt, Vector3 planar, Vector3 worldStep)
    {
        if (!TryBoatDeckAt(world, out float y, out BoatPiece hull, true, true) || hull == null)
            return false;
        Vector3 stand = world;
        stand.y = y;
        bool same = _rideHull != null && hull.SharesIslandWith(_rideHull);
        if (!same)
            BindBoatFollow(hull);
        else if (_rideBoat == null)
            BindBoatFollow(hull);
        if (_rideBoat == null)
            return false;
        WriteRideLocal(_rideBoat.transform.InverseTransformPoint(stand));
        _jumpedThisAir = false;
        _jumpLift = 0f;
        _jumpLiftVel = 0f;
        PlaceOnRide(hull, dt, planar, worldStep);
        return true;
    }

    void PlaceOnRide(BoatPiece hull, float dt, Vector3 planar, Vector3 worldStep)
    {
        if (_rideBoat == null)
            return;
        if (hull != null)
            _rideHull = hull;
        Vector3 world = _rideBoat.transform.TransformPoint(_rideLocal);
        if (TryBoatDeckAt(world, out float deckY, out BoatPiece deck, true, true) && deck != null)
        {
            world.y = deckY;
            _rideHull = deck;
            _rideLocal = _rideBoat.transform.InverseTransformPoint(world);
        }
        _verticalVelocity = 0f;
        _jumpLift = 0f;
        _jumpLiftVel = 0f;
        _heaving = false;
        _swimVel = Vector3.zero;
        IsSwimming = false;
        _wasGrounded = true;
        _airborneTime = 0f;
        _coyoteTime = 0.14f;
        UpdateFootsteps(dt, true, planar.magnitude);
        LocalPlanarVelocity = transform.InverseTransformDirection(new Vector3(worldStep.x, 0f, worldStep.z) / Mathf.Max(dt, 0.0001f));
    }

    static bool IsOarCollider(Collider col)
    {
        if (col == null)
            return false;
        Transform t = col.transform;
        while (t != null)
        {
            string n = t.name;
            if (n.IndexOf("Oar", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Shaft", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Blade", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Oarlock", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (t.GetComponent<BoatPiece>() != null)
                break;
            t = t.parent;
        }
        return false;
    }

    bool TryBoatDeck(out float supportY, out BoatPiece hull)
    {
        return TryBoatDeckAt(transform.position, out supportY, out hull);
    }

    public void SnapOntoHull(BoatPiece lead)
    {
        if (lead == null || _controller == null)
            return;
        IsSwimming = false;
        _heaving = false;
        _jumpedThisAir = false;
        Vector3 pos;
        BoatPiece hull;
        if (!BoatPiece.BestDeckStand(lead, out pos, out hull))
        {
            var body = lead.IslandRootBody();
            Vector3 com = body != null ? body.worldCenterOfMass : lead.transform.position;
            pos = new Vector3(com.x, com.y + 0.35f, com.z);
            hull = lead;
        }
        _controller.enabled = false;
        transform.position = pos;
        Physics.SyncTransforms();
        if (TryBoatDeckAt(pos, out float y, out BoatPiece deck, true, true) && deck != null)
        {
            pos.y = y + 0.12f;
            hull = deck;
            transform.position = pos;
        }
        else
        {
            pos.y += 0.1f;
            transform.position = pos;
        }
        BindBoatFollow(hull != null ? hull : lead);
        if (_rideBoat != null)
            WriteRideLocal(_rideBoat.transform.InverseTransformPoint(transform.position));
        Physics.SyncTransforms();
        _controller.enabled = true;
        SyncPhysPose();
    }

    public void ClearSwimState()
    {
        IsSwimming = false;
        _heaving = false;
        _jumpedThisAir = false;
    }

    bool TryBoatDeckAt(Vector3 feet, out float supportY, out BoatPiece hull, bool ignoreVert = false, bool anyHull = false)
    {
        supportY = float.NegativeInfinity;
        hull = null;
        if (_controller == null)
            return false;
        float lift = ignoreVert ? 1.35f : 0.5f;
        float dist = ignoreVert ? 2.6f : 0.95f;
        float reach = anyHull ? 0.34f : DeckReach;
        int n = Physics.SphereCastNonAlloc(
            feet + Vector3.up * lift,
            reach,
            Vector3.down,
            _deckHits,
            dist,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var hit = _deckHits[i];
            var col = hit.collider;
            if (col == null || col.isTrigger)
                continue;
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;
            if (col.GetComponentInParent<BoatWater>() != null)
                continue;
            if (col.GetComponentInParent<RiverShark>() != null)
                continue;
            var piece = BoatPart.FromCollider(col);
            if (piece == null || piece.Kind == BoatPieceKind.Oar)
                continue;
            bool sameRide = _rideHull != null && piece.SharesIslandWith(_rideHull);
            if (!anyHull && !sameRide && !piece.CanRide())
                continue;
            if (IsOarCollider(col))
                continue;
            if (hit.normal.y < 0.45f)
                continue;
            Vector3 planar = hit.point - feet;
            planar.y = 0f;
            if (planar.sqrMagnitude > (anyHull ? 0.62f * 0.62f : 0.38f * 0.38f))
                continue;
            if (!ignoreVert && (hit.point.y < feet.y - 0.55f || hit.point.y > feet.y + 0.7f))
                continue;
            if (hit.point.y > supportY)
            {
                supportY = hit.point.y;
                hull = piece;
            }
        }
        return hull != null;
    }

    public BoatPiece HullUnderFeet()
    {
        return TryBoatDeck(out _, out BoatPiece hull) ? hull : null;
    }

    BoatPiece StandingOnBoat()
    {
        return HullUnderFeet();
    }

    void CaptureBoatFollow()
    {
        if (!MovementLocked && Time.time < _offRaftUntil)
            return;
        var hull = StandingOnBoat();
        if (hull == null && BoatOarStation.Active != null)
            hull = BoatOarStation.Active.Oar;
        if (hull == null && _rideBoat != null)
            return;
        Rigidbody boat = hull != null ? hull.IslandRootBody() : null;

        if (boat != null && _rideBoat != null && boat != _rideBoat && hull != null
            && hull.IslandRootBody() == _rideBoat)
            boat = _rideBoat;

        if (boat == _rideBoat)
        {
            if (_rideBoat == null)
                return;
            if (!_rideIgnore && hull != null)
            {
                BoatBuildUtil.SetActorIgnoreIsland(_controller, hull, true);
                _rideIgnore = true;
            }
            if (hull != null)
                _rideHull = hull;
            return;
        }

        ClearBoatFollow();
        _rideBoat = boat;
        _rideHull = hull;
        if (boat == null || hull == null || _controller == null)
            return;
        WriteRideLocal(boat.transform.InverseTransformPoint(transform.position));
        BoatBuildUtil.SetActorIgnoreIsland(_controller, hull, true);
        _rideIgnore = true;
    }

    void ApplyBoatFollow()
    {
        if (_rideBoat == null)
            return;
        Vector3 local = Vector3.Lerp(_rideLocalPrev, _rideLocal, SimTime.VisualAlpha());
        Vector3 p = _rideBoat.transform.TransformPoint(local);
        if (_jumpedThisAir)
        {
            float lift = Mathf.Lerp(_jumpLiftPrev, _jumpLift, SimTime.VisualAlpha());
            p.y += Mathf.Max(0f, lift);
        }
        transform.position = p;
    }

    void LeaveRaft()
    {
        _jumpLift = 0f;
        _jumpLiftVel = 0f;
        _drownBoat = 0f;
        ClearBoatFollow(true);
        _offRaftUntil = Time.time + 0.22f;
        _jumpedThisAir = true;
        SyncPhysPose();
    }

    void ClearBoatFollow(bool keepIgnore = false)
    {
        if (!keepIgnore)
        {
            if (_rideIgnore && _rideHull != null && _controller != null)
                BoatBuildUtil.SetActorIgnoreIsland(_controller, _rideHull, false);
            _rideIgnore = false;
            _rideHull = null;
            _rideIgnoreUntil = 0f;
        }
        else
        {
            _rideIgnoreUntil = Time.time + 0.32f;
            _rideIgnore = true;
        }
        _rideBoat = null;
    }

    bool IsStandSurface(Collider col, Vector3 point, bool flatEnough)
    {
        if (col == null)
            return false;
        if (col.GetComponentInParent<BoatWater>() != null)
            return false;
        bool boat = BoatPart.FromCollider(col) != null;
        if (boat)
            return flatEnough;
        if (!flatEnough)
            return false;
        if (BoatWater.TryHeight(point, out float waterY) && point.y < waterY - 0.08f)
            return false;
        return true;
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
        bobY += Mathf.Sin(cycle * 2f) * bobAmount * 0.12f * _bobWeight;
        float swimPitch = 0f;
        float swimRoll = 0f;

        if (IsSwimming)
        {
            float swimSpeed = _controller.velocity.magnitude;
            float stroke = Mathf.Clamp01(swimSpeed / 1.55f);
            _swimBobTime += dt * (0.62f + stroke * 1.05f);
            float t = _swimBobTime;
            bobY = Mathf.Sin(t * 1.15f) * 0.032f + Mathf.Sin(t * 0.47f) * 0.022f;
            bobY += Mathf.Sin(t * 2.55f) * 0.042f * stroke;
            bobX = Mathf.Sin(t * 1.28f) * 0.018f + Mathf.Sin(t * 2.55f) * 0.028f * stroke;
            if (_heaving)
                bobY += Mathf.Clamp(_verticalVelocity * 0.014f, -0.07f, 0.09f);
            swimPitch = Mathf.Sin(t * 2.55f) * 2.1f * stroke + Mathf.Sin(t * 0.62f) * 0.7f;
            swimRoll = Mathf.Sin(t * 1.28f) * (1.1f + 1.6f * stroke);
            _bobWeight = stroke;
        }

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
        if (!_wasGrounded && !IsSwimming)
            accelTarget += Mathf.Clamp(_verticalVelocity * 0.04f, -2.5f, 1.2f);
        if (IsSwimming)
            accelTarget = swimPitch;
        _accelTilt = Mathf.SmoothDamp(_accelTilt, accelTarget, ref _accelTiltVel, IsSwimming ? 0.22f : 0.16f, 25f, dt);

        float walkRoll = Mathf.Sin(cycle * 0.5f) * bobRollAmount * _bobWeight;
        float lookRoll = Mathf.Clamp(-LookVelocity.x * 0.004f, -1.1f, 1.1f);
        float targetRoll = walkRoll + _lean + lookRoll + swimRoll;
        _camRoll = Mathf.Lerp(_camRoll, targetRoll, 1f - Mathf.Exp(-cameraSmoothing * dt));

        cam.localRotation = Quaternion.Euler(_lagPitch + _accelTilt, _lagYaw, _camRoll);

        float targetFov = IsSprinting ? sprintFov : baseFov;
        if (!_wasGrounded)
            targetFov += 1.4f;
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-5.5f * dt));
    }

    void UpdateFootsteps(float dt, bool grounded, float moveAmount)
    {
        if (IsSwimming || !grounded)
        {
            _airAudioTime += dt;
            if (_airAudioTime > 0.18f && footstepSource != null && footstepSource.isPlaying)
                footstepSource.Stop();
            if (_airAudioTime > 0.18f)
            {
                _stepTimer = 0f;
                _strideStarted = false;
            }
            return;
        }

        _airAudioTime = 0f;

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
        PunchDeck(IsSprinting ? 2.4f : (IsCrouching ? 1.1f : 1.7f));
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
        var piece = BoatPart.FromCollider(hit.collider);
        if (piece != null && hit.moveDirection.y < -0.18f)
            return;
        if (item != null && !item.IsCarried && hit.moveDirection.y < -0.18f)
        {
            if (BoatWater.TryHeight(hit.point, out float wy) && hit.point.y < wy + 0.55f)
                body.AddForceAtPosition(Vector3.down * 220f, hit.point, ForceMode.Force);
            return;
        }
        if (item == null || item.IsCarried)
            return;

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
