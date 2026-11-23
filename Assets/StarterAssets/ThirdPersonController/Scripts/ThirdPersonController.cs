using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM 
using UnityEngine.InputSystem;
#endif

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM 
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("How fast the character turns to face movement direction")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -30.0f;

        [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool LockCameraPosition = false;

        [Header("Vaulting (Uncharted / AC style)")]
        [Tooltip("How far ahead (from the capsule edge) to look for vaultable obstacles")]
        [Range(0.3f, 3.0f)]
        public float VaultCheckDistance = 0.9f;

        [Tooltip("Lowest obstacle top (above your feet) that is vaulted. 0 = auto (Step Offset + 0.05). Anything lower is walked over with Step Offset + Foot IK.")]
        public float MinVaultHeight = 0f;

        [Tooltip("Highest obstacle top (above your feet) that is vaulted. 0 = auto (hip height + 0.1).")]
        public float MaxVaultHeight = 0f;

        [Tooltip("Max angle between your movement direction and the obstacle face's normal. The vault itself always crosses PERPENDICULAR to the face.")]
        [Range(0f, 85f)]
        public float VaultMaxApproachAngle = 65f;

        [Tooltip("Maximum thickness of an obstacle that can be vaulted across")]
        public float MaxVaultDepth = 2.0f;

        [Tooltip("Distance to land past the far edge of the obstacle")]
        public float VaultLandingOffset = 0.35f;

        [Tooltip("Maximum drop below your feet allowed on the landing side")]
        public float MaxVaultDrop = 2.0f;

        [Tooltip("How high the feet pass above the obstacle top")]
        public float VaultClearance = 0.12f;

        [Tooltip("How much the vault animation itself already lifts the legs/hips. Lowers the root arc so the body doesn't float too high above the obstacle and the hand can reach the top surface.")]
        [Range(0f, 0.9f)]
        public float VaultAnimLiftCompensation = 0.45f;

        [Tooltip("Minimum horizontal speed during a vault (m/s). Faster running vaults keep their speed.")]
        public float VaultMoveSpeed = 3.0f;

        [Tooltip("How long a vault press is remembered (press slightly early, or while standing at the obstacle)")]
        public float VaultInputBuffer = 0.3f;

        [Tooltip("Holding the vault/sprint button while moving into an obstacle vaults automatically")]
        public bool AutoVaultWhileHeld = true;

        [Tooltip("Layer mask for objects that can be vaulted")]
        public LayerMask VaultLayers;

        [Header("Vaulting - Animation")]
        [Tooltip("The vault clip, used to time-sync the animation with the motion. Empty = auto-find.")]
        public AnimationClip VaultClip;

        [Tooltip("Animator state (Base Layer) that plays the vault")]
        public string VaultStateName = "Vault";

        [Tooltip("Normalized time in the vault clip where the feet land (keep close to the Vault -> Locomotion exit time)")]
        [Range(0.3f, 1f)]
        public float VaultClipLandTime = 0.8f;

        [Tooltip("Tight playback speed bounds so the animation never looks slow-mo or sped-up (space is warped instead of time)")]
        [Range(0.7f, 1.0f)]
        public float MinVaultPlaybackSpeed = 0.88f;

        [Range(1.0f, 1.4f)]
        public float MaxVaultPlaybackSpeed = 1.15f;

        [Tooltip("Fraction of the vault (0..1) when the body reaches the near edge of the obstacle")]
        [Range(0.1f, 0.5f)]
        public float VaultNearEdgePhase = 0.28f;

        [Tooltip("Fraction of the vault (0..1) when the body clears the far edge of the obstacle")]
        [Range(0.4f, 0.9f)]
        public float VaultFarEdgePhase = 0.62f;

        [Header("Vaulting - Debug")]
        [Tooltip("Log why a vault was accepted / rejected")]
        public bool DebugVault = false;

        [Tooltip("Draw vault probes in the Scene view (select the player)")]
        public bool DebugVaultGizmos = false;

        [Header("AI")]
        [Tooltip("Is this controller used by an enemy AI?")]
        public bool IsEnemy = false;
        public bool UseWorldSpaceMovement = false;
        
        [Header("Debug")]
        public bool DebugMovement = false;


        // cinemachine
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private int _animIDVault;

        // vault state
        private bool _isVaultingRef = false;
        public bool IsVaulting => _isVaultingRef; // Expose for IK
        public bool IsSprinting => _input != null && _input.sprint && _input.move != Vector2.zero;
        public float VaultNormalizedTime { get; private set; }
        public Vector3 VaultTopPoint => _currentVault.nearTop;
        public Vector3 VaultTopNormal => Vector3.up;
        public Vector3 VaultDirection => _currentVault.vaultDir;
        private VaultData _currentVault;
        private float _postVaultMomentumTimer = 0f;
        private float _vaultBufferTimer = 0f;
        private float _vaultCooldown = 0f;
        private bool _vaultHeldLastFrame = false;
        private bool _vaultVerbose = false;
        private bool _vaultPressLogged = false;
        private float _hipHeight = 0.95f;
        private float _vaultClipLength = 1f;
        private int _vaultStateHash;
        private int _animIDVaultSpeed;
        private int _animIDVaultHeight;
        private bool _hasVaultSpeedParam;
        private bool _hasVaultHeightParam;

        // vault gizmo debug data
        private List<RaycastHit> _lastVaultForwardHits = new List<RaycastHit>();
        private List<Vector3> _lastVaultRayOrigins = new List<Vector3>();
        private Vector3 _lastVaultProbeDir = Vector3.forward;
        private float _lastVaultProbeDistance = 0f;
        private RaycastHit _lastVaultDownwardHit;
        private bool _lastVaultDownwardHitValid = false;
        private Vector3 _lastVaultFarTop = Vector3.zero;
        private Vector3 _lastVaultLandingPosition = Vector3.zero;
        private bool _lastVaultLandingValid = false;

#if ENABLE_INPUT_SYSTEM 
        private PlayerInput _playerInput;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private IInputProvider _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;
        private const float _vaultMovementIntentThreshold = 0.2f;

        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput != null && _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }


        private void Awake()
        {
            // get a reference to our main camera
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            if (CinemachineCameraTarget != null)
                _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            
            // Try to get input provider - prioritize EnemyInputAI if IsEnemy is true
            var enemyInput = GetComponent<EnemyInputAI>();
            if (enemyInput != null)
            {
                _input = enemyInput;
                IsEnemy = true; // Auto-set flag
            }
            else
            {
                _input = GetComponent<IInputProvider>();
            }

            if (_input == null)
            {
                _input = GetComponentInChildren<IInputProvider>();
            }
            if (_input == null)
            {
                _input = GetComponentInParent<IInputProvider>();
            }
            
            if (_input == null)
            {
                Debug.LogError($"[ThirdPersonController] No IInputProvider found on {gameObject.name}. Attach StarterAssetsInputs or EnemyInputAI component.", gameObject);
            }
            else
            {
                Debug.Log($"[ThirdPersonController] IN USE Input Provider: {_input.GetType().Name} on {gameObject.name} (IsEnemy: {IsEnemy})");
            }

#if ENABLE_INPUT_SYSTEM 
            _playerInput = GetComponent<PlayerInput>();
#endif

            AssignAnimationIDs();
            CacheVaultSetup();

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            if (DebugVault)
            {
                Debug.Log("[VAULT DEBUG] Vault system initialized successfully!");
            }
        }

        private void Update()
        {
            if (_input == null) return;

            _hasAnimator = TryGetComponent(out _animator);

            if (_postVaultMomentumTimer > 0f)
                _postVaultMomentumTimer -= Time.deltaTime;

            // While vaulting, skip GroundedCheck / JumpAndGravity / Move so the Animator never
            // enters FreeFall -> InAir -> JumpLand mid-vault.
            if (_isVaultingRef)
            {
                Grounded = true;
                _fallTimeoutDelta = FallTimeout;
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDGrounded, true);
                    _animator.SetBool(_animIDFreeFall, false);
                    _animator.SetBool(_animIDJump, false);
                }
                return;
            }

            JumpAndGravity();
            GroundedCheck();

            // Vault decision lives in HandleVaultInput() -> TryFindVault(). Returns true if a vault started.
            if (HandleVaultInput())
                return; // skip normal movement this frame

            Move();
        }

        private void LateUpdate()
        {
            if (_input == null) return;
            CameraRotation();
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
            _animIDVault = Animator.StringToHash("Vault");
        }

        private void GroundedCheck()
        {
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
                QueryTriggerInteraction.Ignore);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            /* PATCH 2: Allow camera rotation during vault but skip if no camera target */
            if (CinemachineCameraTarget == null)
                return;

            // if there is an input and camera position is not fixed
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                //Don't multiply mouse input by Time.deltaTime;
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            // clamp our rotations so our values are limited 360 degrees
            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            // Cinemachine will follow this target
            if (CinemachineCameraTarget != null)
            {
                CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                    _cinemachineTargetYaw, 0.0f);
            }
        }

        private void Move()
        {
            /* PATCH 2: Skip movement input during vault */
            if (_isVaultingRef)
                return;

            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            // if there is no input, set the target speed to 0
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            // current horizontal velocity (preserve momentum immediately after a vault when CC was disabled)
            float currentHorizontalSpeed = _postVaultMomentumTimer > 0f
                ? _speed
                : new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // accelerate or decelerate
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // normalise input direction
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // if there is movement input, rotate character
            if (_input.move != Vector2.zero)
            {
                // 🔥 KEY FIX — support AI world-space movement
                if (UseWorldSpaceMovement || _mainCamera == null)
                {
                    // AI / world space movement
                    _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg;
                }
                else
                {
                    // Player camera-relative movement
                    _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                    _mainCamera.transform.eulerAngles.y;
                }

                float rotation = Mathf.SmoothDampAngle(
                    transform.eulerAngles.y,
                    _targetRotation,
                    ref _rotationVelocity,
                    RotationSmoothTime
                );

                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }

            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            // move character
            Vector3 moveVector = targetDirection.normalized * (_speed * Time.deltaTime) +
                new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime;

            if (IsEnemy && DebugMovement && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[TPC-ENEMY] Move: Input:{_input.move} TargetDir:{targetDirection} Speed:{_speed} VertVel:{_verticalVelocity} FINAL_VECTOR:{moveVector}");
                Debug.Log($"[TPC-ENEMY] Controller Enabled:{_controller.enabled} IsGrounded:{_controller.isGrounded} MinMoveDist:{_controller.minMoveDistance}");
            }

            _controller.Move(moveVector);

            // update animator
            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }


        private void JumpAndGravity()
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // update animator if using character
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump
                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }

                    // Consume the jump input so it doesn't fire again
                    _input.jump = false;
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  VAULTING  (Uncharted / Assassin's Creed style)
        //
        //  Decision order (TryFindVault) — the first failing step rejects the vault:
        //    1. FACE      : thin sphere-casts from knee to hip height, in the direction you are
        //                   moving, must hit a steep face (a wall-like side, not a ramp).
        //    2. ANGLE     : your move direction must be within VaultMaxApproachAngle of the face
        //                   normal. The vault itself ALWAYS crosses perpendicular to the face.
        //    3. HEIGHT    : obstacle top above your feet must be MinVault..MaxVault.
        //                     below MinVault (≈ Step Offset)  -> walk over it (Step Offset + Foot IK)
        //                     above MaxVault (≈ hip height)   -> too tall (future: climb / mantle)
        //    4. HEADROOM  : space above the top for the body to pass.
        //    5. DEPTH     : walk across the top (perpendicular) until it drops off; must be <= MaxVaultDepth.
        //    6. LANDING   : ground on the far side, lower than the top, with room for the capsule.
        // ═══════════════════════════════════════════════════════════════════════

        private struct VaultData
        {
            public Vector3 contactPoint;  // where the probe hit the near face
            public Vector3 vaultDir;      // horizontal, perpendicular INTO the face
            public Vector3 nearTop;       // top surface at the near edge
            public Vector3 farTop;        // top surface at the far edge
            public Vector3 landing;       // ground point beyond the far edge
            public float height;          // obstacle top above the feet
            public float depth;           // thickness along vaultDir
        }

        private float EffectiveMinVaultHeight =>
            MinVaultHeight > 0f ? MinVaultHeight : (_controller != null ? _controller.stepOffset : 0.25f) + 0.05f;

        private float EffectiveMaxVaultHeight =>
            MaxVaultHeight > 0f ? MaxVaultHeight : _hipHeight + 0.1f;

        private static float Planar(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private void VaultLog(string message)
        {
            if (DebugVault && _vaultVerbose)
                Debug.Log("[VAULT] " + message, this);
        }

        private void CacheVaultSetup()
        {
            _hipHeight = 0.95f;
            if (_hasAnimator && _animator.isHuman)
            {
                Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips) _hipHeight = Mathf.Clamp(hips.position.y - transform.position.y, 0.5f, 1.5f);
            }

            _animIDVaultSpeed = Animator.StringToHash("VaultSpeed");
            _animIDVaultHeight = Animator.StringToHash("VaultHeight");
            _vaultStateHash = Animator.StringToHash("Base Layer." + VaultStateName);
            _hasVaultSpeedParam = _hasVaultHeightParam = false;
            _vaultClipLength = 1f;

            if (!_hasAnimator) return;

            foreach (var p in _animator.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float) continue;
                if (p.nameHash == _animIDVaultSpeed) _hasVaultSpeedParam = true;
                if (p.nameHash == _animIDVaultHeight) _hasVaultHeightParam = true;
            }

            AnimationClip clip = VaultClip;
            if (clip == null && _animator.runtimeAnimatorController != null)
            {
                foreach (var c in _animator.runtimeAnimatorController.animationClips)
                {
                    if (c != null && (c.name.ToLower().Contains("vault") || c.name == "Jumping"))
                    {
                        clip = c;
                        break;
                    }
                }
            }
            if (clip != null) _vaultClipLength = clip.length;

            if (DebugVault)
                Debug.Log($"[VAULT] Ready. Vault heights {EffectiveMinVaultHeight:F2}m - {EffectiveMaxVaultHeight:F2}m (hip {_hipHeight:F2}m), " +
                          $"clip length {_vaultClipLength:F2}s, VaultSpeed param: {_hasVaultSpeedParam}", this);
        }

        /// <summary>World-space direction the player wants to move in (camera relative), or zero.</summary>
        private Vector3 GetWorldMoveDirection()
        {
            if (_input.move.magnitude < _vaultMovementIntentThreshold) return Vector3.zero;

            Vector3 inputDir = new Vector3(_input.move.x, 0f, _input.move.y).normalized;
            if (UseWorldSpaceMovement || _mainCamera == null) return inputDir;

            return Quaternion.Euler(0f, _mainCamera.transform.eulerAngles.y, 0f) * inputDir;
        }

        /// <summary>Reads vault input (buffered press or held auto-vault). Returns true if a vault started.</summary>
        private bool HandleVaultInput()
        {
            bool held = _input.vault;
            if (held && !_vaultHeldLastFrame)
            {
                // fresh press -> remember it for a short time
                _vaultBufferTimer = VaultInputBuffer;
                _vaultPressLogged = false;
            }
            _vaultHeldLastFrame = held;

            if (_vaultBufferTimer > 0f) _vaultBufferTimer -= Time.deltaTime;
            if (_vaultCooldown > 0f) _vaultCooldown -= Time.deltaTime;

            bool pressedRecently = _vaultBufferTimer > 0f;
            bool autoVault = AutoVaultWhileHeld && held;
            if (!(pressedRecently || autoVault) || _vaultCooldown > 0f) return false;

            _vaultVerbose = pressedRecently && !_vaultPressLogged; // log once per press, never spam during auto-vault

            if (!Grounded)
            {
                VaultLog("✗ Not grounded.");
                if (_vaultVerbose) _vaultPressLogged = true;
                return false;
            }

            Vector3 moveDir = GetWorldMoveDirection();
            bool hasMoveInput = moveDir.sqrMagnitude > 0f;

            // Auto-vault needs you to move INTO the obstacle. A fresh press also works standing still
            // right in front of an obstacle you are facing.
            if (!hasMoveInput && !pressedRecently) return false;

            Vector3 probeDir = hasMoveInput ? moveDir : transform.forward;
            float probeDistance = hasMoveInput ? VaultCheckDistance : Mathf.Min(VaultCheckDistance, 0.6f);

            if (!TryFindVault(probeDir, probeDistance, out VaultData vault))
            {
                if (_vaultVerbose) _vaultPressLogged = true;
                return false;
            }

            _vaultBufferTimer = 0f;
            StartCoroutine(PerformVault(vault));
            return true;
        }

        private bool TryFindVault(Vector3 probeDir, float probeDistance, out VaultData vault)
        {
            vault = default;
            _lastVaultForwardHits.Clear();
            _lastVaultRayOrigins.Clear();
            _lastVaultDownwardHitValid = false;
            _lastVaultLandingValid = false;

            probeDir.y = 0f;
            if (probeDir.sqrMagnitude < 0.0001f) return false;
            probeDir.Normalize();
            _lastVaultProbeDir = probeDir;
            _lastVaultProbeDistance = probeDistance;

            Vector3 feet = transform.position;
            float radius = _controller.radius;
            float minH = EffectiveMinVaultHeight;
            float maxH = EffectiveMaxVaultHeight;
            int mask = VaultLayers.value;

            if (mask == 0)
            {
                VaultLog("✗ VaultLayers is set to Nothing.");
                return false;
            }

            // ── 1. FACE ──────────────────────────────────────────────────────
            // Casts start on the capsule's centre line, so they still work when pressed against the obstacle.
            // A cast at height h only hits obstacles taller than h -> lowest cast sits just under MinVaultHeight.
            const int castCount = 4;
            float lowCast = Mathf.Max(0.1f, minH - 0.1f);
            float highCast = Mathf.Max(lowCast + 0.05f, maxH - 0.05f);
            RaycastHit face = default;
            bool foundFace = false;

            for (int i = 0; i < castCount; i++)
            {
                float h = Mathf.Lerp(lowCast, highCast, i / (float)(castCount - 1));
                Vector3 origin = feet + Vector3.up * h;
                _lastVaultRayOrigins.Add(origin);

                if (Physics.SphereCast(origin, 0.08f, probeDir, out RaycastHit hit, probeDistance + radius, mask, QueryTriggerInteraction.Ignore))
                {
                    _lastVaultForwardHits.Add(hit);
                    bool steepFace = Mathf.Abs(hit.normal.y) < 0.4f;
                    if (steepFace && (!foundFace || hit.distance < face.distance))
                    {
                        face = hit;
                        foundFace = true;
                    }
                }
            }

            if (!foundFace)
            {
                VaultLog($"✗ No steep obstacle face within {probeDistance:F2}m between {lowCast:F2}m and {highCast:F2}m height.");
                return false;
            }

            // ── 2. ANGLE (vault direction = perpendicular into the face) ─────
            Vector3 vaultDir = new Vector3(-face.normal.x, 0f, -face.normal.z);
            if (vaultDir.sqrMagnitude < 0.0001f) return false;
            vaultDir.Normalize();

            float approachAngle = Vector3.Angle(probeDir, vaultDir);
            if (approachAngle > VaultMaxApproachAngle)
            {
                VaultLog($"✗ Approach angle {approachAngle:F0}° > {VaultMaxApproachAngle:F0}°. Move more directly at '{face.collider.name}'.");
                return false;
            }

            // ── 3. HEIGHT (top at the near edge) ─────────────────────────────
            float searchTopY = feet.y + maxH + 0.75f;
            Vector3 topProbe = face.point + vaultDir * 0.1f;
            topProbe.y = searchTopY;
            if (!Physics.Raycast(topProbe, Vector3.down, out RaycastHit top, searchTopY - feet.y + 0.3f, mask, QueryTriggerInteraction.Ignore))
            {
                VaultLog($"✗ Could not find the top of '{face.collider.name}'.");
                return false;
            }

            // If the probe started INSIDE a very tall obstacle it falls through to the ground below.
            if (top.point.y < face.point.y - 0.02f)
            {
                VaultLog($"✗ '{face.collider.name}' is far taller than hip height.");
                return false;
            }

            _lastVaultDownwardHit = top;
            _lastVaultDownwardHitValid = true;

            float height = top.point.y - feet.y;
            if (height < minH)
            {
                VaultLog($"✗ Too low to vault ({height:F2}m < {minH:F2}m) -> walk over it (Step Offset + Foot IK).");
                return false;
            }
            if (height > maxH)
            {
                VaultLog($"✗ Too high to vault ({height:F2}m > {maxH:F2}m hip height).");
                return false;
            }
            if (top.normal.y < 0.7f)
            {
                VaultLog("✗ Obstacle top is too steep to pass over.");
                return false;
            }

            // ── 4. HEADROOM ──────────────────────────────────────────────────
            Vector3 headA = top.point + Vector3.up * (radius + 0.15f);
            Vector3 headB = headA + Vector3.up * 0.5f;
            if (Physics.CheckCapsule(headA, headB, radius * 0.8f, mask, QueryTriggerInteraction.Ignore))
            {
                VaultLog("✗ No headroom above the obstacle.");
                return false;
            }

            // ── 5. DEPTH: walk across the top, perpendicular, until it drops off ──
            Vector3 farTop = top.point;
            bool foundEdge = false;
            const float step = 0.1f;
            int steps = Mathf.CeilToInt(MaxVaultDepth / step);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 o = top.point + vaultDir * (i * step) + Vector3.up * 1.0f;
                if (Physics.Raycast(o, Vector3.down, out RaycastHit s, 1.3f, mask, QueryTriggerInteraction.Ignore))
                {
                    float dy = s.point.y - top.point.y;
                    if (dy > 0.3f)
                    {
                        VaultLog("✗ Something taller sits on / behind the obstacle.");
                        return false;
                    }
                    if (dy < -0.3f) { foundEdge = true; break; }
                    farTop = s.point;
                }
                else
                {
                    foundEdge = true;
                    break;
                }
            }

            if (!foundEdge)
            {
                VaultLog($"✗ Deeper than {MaxVaultDepth:F1}m — that's a platform, not a vaultable object.");
                return false;
            }

            // ── 6. LANDING ───────────────────────────────────────────────────
            int groundMask = GroundLayers.value | mask;
            Vector3 landXZ = farTop + vaultDir * (radius + VaultLandingOffset);
            Vector3 landProbe = new Vector3(landXZ.x, top.point.y + 0.5f, landXZ.z);
            float landRay = (top.point.y + 0.5f) - (feet.y - MaxVaultDrop);
            if (!Physics.Raycast(landProbe, Vector3.down, out RaycastHit ground, landRay, groundMask, QueryTriggerInteraction.Ignore))
            {
                VaultLog($"✗ No ground within {MaxVaultDrop:F1}m on the far side.");
                return false;
            }
            if (ground.point.y > top.point.y - 0.25f)
            {
                VaultLog("✗ Far side is as high as the top (would be a vault ONTO, not over).");
                return false;
            }
            if (ground.normal.y < 0.6f)
            {
                VaultLog("✗ Landing spot is too steep.");
                return false;
            }

            Vector3 capBottom = ground.point + Vector3.up * (radius + 0.05f);  // lifted so touching the floor isn't "blocked"
            Vector3 capTop = ground.point + Vector3.up * (_controller.height - radius);
            if (Physics.CheckCapsule(capBottom, capTop, radius * 0.95f, groundMask, QueryTriggerInteraction.Ignore))
            {
                VaultLog("✗ Landing spot is blocked.");
                return false;
            }

            vault.contactPoint = face.point;
            vault.vaultDir = vaultDir;
            vault.nearTop = top.point;
            vault.farTop = farTop;
            vault.landing = ground.point;
            vault.height = height;
            vault.depth = Planar(farTop - top.point) + step * 0.5f;

            _lastVaultFarTop = farTop;
            _lastVaultLandingPosition = ground.point;
            _lastVaultLandingValid = true;

            if (DebugVault)
                Debug.Log($"[VAULT] ✓ Vaulting '{face.collider.name}': height {height:F2}m, depth {vault.depth:F2}m, approach {approachAngle:F0}°", this);

            return true;
        }

        private IEnumerator PerformVault(VaultData v)
        {
            _isVaultingRef = true;
            _currentVault = v;
            VaultNormalizedTime = 0f;

            Vector3 start = transform.position;
            Vector3 planarVel = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z);
            float entrySpeed = Mathf.Max(_speed, planarVel.magnitude);
            float hSpeed = Mathf.Max(entrySpeed, VaultMoveSpeed);

            _controller.enabled = false;

            // Path: start -> A (feet over the near edge) -> B (over the far edge) -> landing.
            // A, B and the landing all lie on the line through the contact point PERPENDICULAR to the face,
            // so a diagonal approach turns into a straight, full-depth crossing.
            Vector3 A = v.nearTop - v.vaultDir * 0.05f;
            Vector3 B = v.farTop;
            Vector3 L = v.landing;
            // Subtract VaultAnimLiftCompensation because the vault/jump animation already tucks/lifts the legs
            // above the root. Otherwise the shoulders rise ~1.5m above the obstacle and the hand IK can't reach it.
            float peakY = Mathf.Max(start.y, Mathf.Max(v.nearTop.y, v.farTop.y) + VaultClearance - VaultAnimLiftCompensation);

            // Motion-Warping style timing: keep animation playback near 1.0x (natural gravity & limb speed)
            // and warp SPACE (start -> A -> B -> L) across the clip's natural duration instead of stretching time.
            float naturalClipDuration = Mathf.Max(0.25f, _vaultClipLength * VaultClipLandTime);

            // Subtle speed adjustment from entry momentum (sprinting = slightly snappier, walking = slightly calmer),
            // strictly clamped to [MinVaultPlaybackSpeed, MaxVaultPlaybackSpeed].
            float speedRatio = Mathf.InverseLerp(MoveSpeed, Mathf.Max(MoveSpeed + 0.1f, SprintSpeed), entrySpeed);
            float playbackSpeed = Mathf.Lerp(MinVaultPlaybackSpeed, MaxVaultPlaybackSpeed, speedRatio);
            float total = naturalClipDuration / playbackSpeed;

            float nearPhase = Mathf.Clamp(VaultNearEdgePhase, 0.12f, 0.45f);
            float farPhase  = Mathf.Clamp(VaultFarEdgePhase, nearPhase + 0.15f, 0.88f);
            float t1 = total * nearPhase;                  // run-up + rise onto the near edge
            float t2 = total * (farPhase - nearPhase);     // across the top
            float t3 = total * (1f - farPhase);            // drop to the landing

            if (_hasAnimator)
            {
                _animator.SetBool(_animIDFreeFall, false);
                _animator.SetBool(_animIDJump, false);
                _animator.SetBool(_animIDGrounded, true);

                float minH = EffectiveMinVaultHeight;
                float range = Mathf.Max(0.01f, EffectiveMaxVaultHeight - minH);
                if (_hasVaultHeightParam)
                    _animator.SetFloat(_animIDVaultHeight, Mathf.Clamp01((v.height - minH) / range));

                // Lock Animator playback speed to the natural clamped rate (matches `total` exactly).
                if (_hasVaultSpeedParam)
                    _animator.SetFloat(_animIDVaultSpeed, playbackSpeed);

                // CrossFade straight into the state: works from ANY current state (a trigger only works
                // from states that have a transition to Vault, and can fire late).
                _animator.ResetTrigger(_animIDVault);
                if (_animator.HasState(0, _vaultStateHash))
                    _animator.CrossFadeInFixedTime(_vaultStateHash, 0.1f, 0, 0f);
                else
                    _animator.SetTrigger(_animIDVault);
            }

            Quaternion startRot = transform.rotation;
            Quaternion vaultRot = Quaternion.LookRotation(v.vaultDir, Vector3.up);
            float turnTime = Mathf.Max(t1, 0.12f);

            float t = 0f;
            while (t < total)
            {
                t = Mathf.Min(t + Time.deltaTime, total);

                Vector3 p;
                if (t <= t1)
                {
                    float k = t / t1;
                    p = Vector3.Lerp(start, A, k);
                    p.y = Mathf.Lerp(start.y, peakY, Mathf.Sin(k * Mathf.PI * 0.5f)); // fast rise, eases at the top
                }
                else if (t <= t1 + t2)
                {
                    float k = (t - t1) / t2;
                    p = Vector3.Lerp(A, B, k);
                    p.y = peakY + Mathf.Sin(k * Mathf.PI) * 0.04f;                    // small hop over the top
                }
                else
                {
                    float k = (t - t1 - t2) / t3;
                    p = Vector3.Lerp(B, L, k);
                    p.y = Mathf.Lerp(peakY, L.y, k * k);                               // accelerating fall
                }

                transform.position = p;
                transform.rotation = Quaternion.Slerp(startRot, vaultRot, Mathf.SmoothStep(0f, 1f, t / turnTime));
                VaultNormalizedTime = t / total;

                yield return null;
            }

            transform.position = L;
            transform.rotation = vaultRot;
            Physics.SyncTransforms();
            _controller.enabled = true;

            // Land straight into locomotion: grounded, facing the vault direction, momentum kept.
            _verticalVelocity = -2f;
            Grounded = true;
            _fallTimeoutDelta = FallTimeout;
            _targetRotation = vaultRot.eulerAngles.y;
            _rotationVelocity = 0f;
            _speed = _input.move != Vector2.zero ? entrySpeed : 0f;
            _animationBlend = _speed;
            if (_hasAnimator) _animator.SetFloat(_animIDSpeed, _animationBlend);
            _postVaultMomentumTimer = 0.15f;
            _vaultCooldown = 0.2f;

            VaultNormalizedTime = 0f;
            _isVaultingRef = false;
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
                GroundedRadius);

            // ===== VAULT DEBUG VISUALIZATION =====
            if (!DebugVaultGizmos || !_controller)
                return;

            // Probe casts (yellow) and what they hit (green)
            Gizmos.color = Color.yellow;
            foreach (var origin in _lastVaultRayOrigins)
                Gizmos.DrawLine(origin, origin + _lastVaultProbeDir * (_lastVaultProbeDistance + _controller.radius));
            Gizmos.color = Color.green;
            foreach (var hit in _lastVaultForwardHits)
                Gizmos.DrawSphere(hit.point, 0.05f);

            // Obstacle top at the near edge (blue)
            if (_lastVaultDownwardHitValid)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(_lastVaultDownwardHit.point, 0.07f);
            }

            // Far edge (cyan) + landing spot & capsule (magenta)
            if (_lastVaultLandingValid)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(_lastVaultFarTop, 0.07f);
                Gizmos.DrawLine(_lastVaultDownwardHit.point, _lastVaultFarTop);

                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(_lastVaultFarTop, _lastVaultLandingPosition);
                Gizmos.DrawSphere(_lastVaultLandingPosition, 0.1f);
                Gizmos.DrawWireSphere(_lastVaultLandingPosition + Vector3.up * _controller.radius, _controller.radius);
                Gizmos.DrawWireSphere(_lastVaultLandingPosition + Vector3.up * (_controller.height - _controller.radius), _controller.radius);
            }
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }
    }
}
