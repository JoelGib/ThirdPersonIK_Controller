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

        [Header("Vaulting")]
        [Tooltip("Distance to raycast forward when checking for vaultable obstacles")]
        [Range(0.5f, 5.0f)]
        public float VaultCheckDistance = 1.5f;

        [Tooltip("Minimum obstacle height to vault (must be higher than step height)")]
        public float VaultMinHeight = 0.5f;

        [Tooltip("Maximum obstacle height to vault (must be lower than chest height)")]
        public float VaultMaxHeight = 1.8f;

        [Tooltip("Time in seconds to complete the vault animation")]
        public float VaultDuration = 0.6f;

        [Tooltip("How far forward the player lands after vaulting")]
        public float VaultForwardOffset = 0.5f;

        [Tooltip("Height added to obstacle top for landing position")]
        public float VaultUpwardOffset = 0.2f;

        [Tooltip("Layer mask for objects that can be vaulted")]
        public LayerMask VaultLayers;

        [Tooltip("Number of forward raycasts to perform for obstacle detection (3-5 recommended)")]
        [Range(1, 5)]
        public int VaultRaycastCount = 3;

        [Tooltip("Enable vaulting debug logs in console")]
        public bool DebugVault = false;

        [Tooltip("Enable vaulting Gizmo visualization in editor")]
        public bool DebugVaultGizmos = false;

        [Header("Vaulting - Advanced")]
        [Tooltip("Maximum depth (thickness) of an obstacle to be vaultable")]
        public float MaxVaultDepth = 2.0f;
        
        [Tooltip("Distance to land past the back edge of the obstacle")]
        public float VaultLandingOffset = 0.5f;

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

        // jump state tracking
        private bool _jumpInputProcessed = false;

        // vault gizmo debug data
        private List<RaycastHit> _lastVaultForwardHits = new List<RaycastHit>();
        private List<Vector3> _lastVaultRayOrigins = new List<Vector3>();
        private RaycastHit _lastVaultDownwardHit;
        private bool _lastVaultDownwardHitValid = false;
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

            JumpAndGravity();
            GroundedCheck();

            /* PATCH 1: Check for vault attempt before normal movement with movement intent validation */
            if (Grounded && _input.vault && !_isVaultingRef)
            {
                Vector3 vaultLandingPosition;
                
                if (DebugVault)
                {
                    Debug.Log("[VAULT DEBUG] Vault input detected! Checking movement intent and valid landing...");
                }

                /* Validate that player has movement intent */
                if (!HasVaultMovementIntent())
                {
                    if (DebugVault)
                    {
                        Debug.Log("[VAULT DEBUG] ✗ Vault input ignored - no forward movement intent");
                    }
                }
                else if (TryGetVaultLanding(out vaultLandingPosition))
                {
                    if (DebugVault)
                    {
                        Debug.LogError($"[VAULT DEBUG] ✓ Valid vault target found at position: {vaultLandingPosition}");
                    }
                    StartCoroutine(PerformVault(vaultLandingPosition));
                    return; /* Skip normal movement this frame */
                }
                else
                {
                    if (DebugVault)
                    {
                        Debug.Log("[VAULT DEBUG] ✗ No valid vault target - obstacle check failed");
                    }
                }
            }

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

            // current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

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
                if (_input.jump && _jumpTimeoutDelta <= 0.0f && !_jumpInputProcessed)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }

                    // Mark that we've processed this jump input
                    _jumpInputProcessed = true;
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }

                // Reset jump input tracking when jump button is released
                if (!_input.jump)
                {
                    _jumpInputProcessed = false;
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

        /// <summary>
        /// PATCH 1 - FIX: Validates that the player has movement intent toward forward direction.
        /// Converts camera-relative input to world-space before comparing to player forward.
        /// </summary>
        private bool HasVaultMovementIntent()
        {
            /* Check if movement input has sufficient magnitude */
            if (_input.move.magnitude < _vaultMovementIntentThreshold)
            {
                return false;
            }

            /* For AI (no camera) or World Space Movement, just check forward alignment based on character forward */
            if (IsEnemy || UseWorldSpaceMovement || _mainCamera == null)
            {
                float moveAlignment = Vector3.Dot(_input.move.normalized, transform.forward);
                return moveAlignment > 0f;
            }

            /* Convert camera-relative input to world-space direction */
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
            Vector3 cameraForward = new Vector3(_mainCamera.transform.forward.x, 0.0f, _mainCamera.transform.forward.z).normalized;
            Vector3 cameraRight = new Vector3(_mainCamera.transform.right.x, 0.0f, _mainCamera.transform.right.z).normalized;
            
            /* Construct world-space movement direction from camera-relative input */
            Vector3 worldMoveDirection = (cameraRight * inputDirection.x + cameraForward * inputDirection.z).normalized;
            
            /* Check if player is moving roughly forward (within 90 degrees of forward direction) */
            Vector3 playerForward = transform.forward;
            float cameraAlignedMovement = Vector3.Dot(worldMoveDirection, playerForward);

            if (DebugVault && IsEnemy)
            {
                Debug.Log($"[VAULT DEBUG] Movement intent check - Magnitude: {_input.move.magnitude:F2}, World direction: {worldMoveDirection}, Forward alignment: {cameraAlignedMovement:F2}");
            }

            return cameraAlignedMovement > 0f;
        }

        /// <summary>
        /// Detects if there is a valid vaultable obstacle in front of the player.
        /// Performs multiple raycasts at different heights for robust obstacle detection.
        /// </summary>
        /// <param name="vaultLandingPosition">Output: The world position where the player should land after vaulting</param>
        /// <returns>True if a valid vault target is found, false otherwise</returns>
        private bool TryGetVaultLanding(out Vector3 vaultLandingPosition)
        {
            vaultLandingPosition = Vector3.zero;
            _lastVaultForwardHits.Clear();
            _lastVaultRayOrigins.Clear();
            _lastVaultDownwardHitValid = false;
            _lastVaultLandingValid = false;

            /* Perform multiple forward raycasts at different heights */
            RaycastHit bestHit = default;
            float lowestHitPoint = float.MaxValue;
            bool foundObstacle = false;

            /* Calculate height spread for raycasts - WAIST LEVEL DETECTION */
            float heightMin = _controller.height * 0.3f;  /* Lower waist */
            float heightMax = _controller.height * 0.65f; /* Mid-chest */
            float heightStep = (heightMax - heightMin) / Mathf.Max(1, VaultRaycastCount - 1);

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Performing {VaultRaycastCount} forward raycasts at waist level...");
            }

            /* Cast multiple rays at different heights and find the lowest hit point (closest to ground) */
            for (int i = 0; i < VaultRaycastCount; i++)
            {
                float heightOffset = heightMin + (i * heightStep);
                Vector3 rayOrigin = transform.position + Vector3.up * heightOffset;
                Vector3 rayDirection = transform.forward;

                _lastVaultRayOrigins.Add(rayOrigin);

                RaycastHit hitInfo;
                if (Physics.Raycast(rayOrigin, rayDirection, out hitInfo, VaultCheckDistance, VaultLayers, QueryTriggerInteraction.Ignore))
                {
                    _lastVaultForwardHits.Add(hitInfo);
                    
                    if (!foundObstacle || hitInfo.distance < bestHit.distance)
                    {
                        bestHit = hitInfo;
                        foundObstacle = true;
                    }

                    /* Track the lowest Y point for accurate obstacle height measurement */
                    if (hitInfo.point.y < lowestHitPoint)
                    {
                        lowestHitPoint = hitInfo.point.y;
                    }

                    if (DebugVault)
                    {
                        Debug.Log($"[VAULT DEBUG] Raycast {i + 1}/{VaultRaycastCount} HIT: {hitInfo.collider.name} at distance {hitInfo.distance:F2}m (height: {heightOffset:F2}, hit Y: {hitInfo.point.y:F2})");
                    }
                }
                else
                {
                    if (DebugVault)
                    {
                        Debug.Log($"[VAULT DEBUG] Raycast {i + 1}/{VaultRaycastCount} miss (height: {heightOffset:F2})");
                    }
                }
            }

            if (!foundObstacle)
            {
                if (DebugVault)
                {
                    Debug.Log("[VAULT DEBUG] ✗ Forward raycasts: No obstacles found");
                }
                return false;
            }

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] ✓ Best hit found at distance {bestHit.distance:F2}m on {bestHit.collider.name}");
            }

            /* Raycast downward from above the hit point to find obstacle top */
            Vector3 obstacleTopSearchOrigin = bestHit.point + Vector3.up * VaultMaxHeight;
            RaycastHit downHit;
            if (!Physics.Raycast(obstacleTopSearchOrigin, Vector3.down, out downHit, VaultMaxHeight, VaultLayers, QueryTriggerInteraction.Ignore))
            {
                if (DebugVault)
                {
                    Debug.Log("[VAULT DEBUG] ✗ Downward raycast: Could not find obstacle top surface");
                }
                return false;
            }

            _lastVaultDownwardHit = downHit;
            _lastVaultDownwardHitValid = true;

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] ✓ Downward raycast HIT: Found obstacle top at Y={downHit.point.y:F2}");
            }

            /* FIX: Calculate obstacle height relative to lowest hit point (closest to obstacle base) */
            float obstacleHeight = downHit.point.y - lowestHitPoint;

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Obstacle height: {obstacleHeight:F2}m (from Y={lowestHitPoint:F2} to Y={downHit.point.y:F2}) (Min: {VaultMinHeight:F2}m, Max: {VaultMaxHeight:F2}m)");
            }

            /* Validate obstacle height is within range */
            if (obstacleHeight < VaultMinHeight || obstacleHeight > VaultMaxHeight)
            {
                if (DebugVault)
                {
                    Debug.Log($"[VAULT DEBUG] ✗ Obstacle height OUT OF RANGE: {obstacleHeight:F2}m");
                }
                return false;
            }

            /* PATCH 3: Scan forward to find the back edge of the obstacle */
            Vector3 scanStartPoint = downHit.point;
            Vector3 scanDirection = transform.forward;
            Vector3 lastValidSurfacePoint = scanStartPoint;
            bool edgeFound = false;
            
            float scanStep = 0.1f;
            int maxSteps = Mathf.CeilToInt(MaxVaultDepth / scanStep);

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Scanning max depth {MaxVaultDepth}m to find back edge...");
            }

            for (int i = 1; i <= maxSteps; i++)
            {
                // Move forward along the top surface
                Vector3 checkPos = scanStartPoint + (scanDirection * (i * scanStep));
                // Raycast down from slightly above expected surface height
                Vector3 rayOrigin = checkPos + Vector3.up * 1.0f; 
                
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit surfaceHit, 2.0f, VaultLayers, QueryTriggerInteraction.Ignore))
                {
                    // Still on the obstacle surface
                    lastValidSurfacePoint = surfaceHit.point;
                    
                    // Optional: Check if surface height changed drastically (steep slope or wall)
                    if (Mathf.Abs(surfaceHit.point.y - scanStartPoint.y) > 0.5f)
                    {
                        // If it DROPPED significantly, we found the back edge (e.g. hit the ground)
                        if (surfaceHit.point.y < scanStartPoint.y)
                        {
                            if (DebugVault) Debug.Log($"[VAULT DEBUG] ✓ Back edge found (drop-off) at step {i}");
                            edgeFound = true;
                            // The PREVIOUS point was likely the last valid point on the surface
                            // But we are now past the edge, so we can calculate landing from here or lastValid
                            break;
                        }
                        
                        // If it went UP significantly, we hit a wall -> invalid
                        if (DebugVault) Debug.Log($"[VAULT DEBUG] ✗ Surface height rose too much (wall?) at step {i}");
                        return false; 
                    }
                }
                else
                {
                    // Raycast missed! We found the edge.
                    edgeFound = true;
                    if (DebugVault) Debug.Log($"[VAULT DEBUG] Back edge found at step {i} ({i*scanStep:F2}m depth)");
                    break;
                }
            }

            if (!edgeFound)
            {
                if (DebugVault)
                {
                    Debug.Log($"[VAULT DEBUG] ✗ Obstacle too deep! (>{MaxVaultDepth}m)");
                }
                return false;
            }

            // Calculate landing position relative to the back edge
            vaultLandingPosition = lastValidSurfacePoint + (scanDirection * VaultLandingOffset);
            
            // Adjust Y to ground level if needed, or let gravity handle it?
            // Better to find ground at landing position
            if (Physics.Raycast(vaultLandingPosition + Vector3.up, Vector3.down, out RaycastHit groundHit, 3.0f, GroundLayers, QueryTriggerInteraction.Ignore))
            {
                vaultLandingPosition = groundHit.point;
            }
            else
            {
                 // If no ground found, landing might be in air/pit? Unsafe.
                 if (DebugVault) Debug.Log("[VAULT DEBUG] ✗ No ground found at landing position");
                 return false;
            }

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Landing position calculated: {vaultLandingPosition}");
            }

            /* Check if there is clear space for the capsule at landing position */
            if (!IsCapsuleClearAtPosition(vaultLandingPosition))
            {
                if (DebugVault)
                {
                    Debug.Log("[VAULT DEBUG] ✗ Landing space blocked - capsule collision detected!");
                }
                return false;
            }

            _lastVaultLandingPosition = vaultLandingPosition;
            _lastVaultLandingValid = true;

            if (DebugVault)
            {
                Debug.Log("[VAULT DEBUG] ✓ Landing space is clear!");
            }

            return true;
        }

        /// <summary>
        /// Checks if the character capsule can fit at the given position without colliding with geometry.
        /// </summary>
        private bool IsCapsuleClearAtPosition(Vector3 checkPosition)
        {
            /* Capsule overlap check at landing position */
            Vector3 capsuleBottom = checkPosition + Vector3.up * _controller.radius;
            Vector3 capsuleTop = checkPosition + Vector3.up * (_controller.height - _controller.radius);

            Collider[] overlaps = Physics.OverlapCapsule(capsuleBottom, capsuleTop, _controller.radius, VaultLayers, QueryTriggerInteraction.Ignore);

            if (DebugVault && overlaps.Length > 0)
            {
                Debug.Log($"[VAULT DEBUG] Capsule clearance check: {overlaps.Length} collider(s) blocking space");
            }

            /* If no overlaps, space is clear */
            return overlaps.Length == 0;
        }

        /// <summary>
        /// Coroutine that smoothly moves the player from current position to vault landing position.
        /// Uses a sinusoidal arc for natural-looking movement.
        /// </summary>
        private IEnumerator PerformVault(Vector3 landingPosition)
        {
            _isVaultingRef = true;

            if (DebugVault)
            {
                Debug.Log("[VAULT DEBUG] ▶ VAULT STARTED");
            }

            /* Trigger vault animation */
            if (_hasAnimator)
            {
                _animator.SetTrigger(_animIDVault);
                if (DebugVault)
                {
                    Debug.Log("[VAULT DEBUG] Vault animation triggered");
                }
            }

            /* Disable CharacterController to move player manually */
            _controller.enabled = false;

            Vector3 startPosition = transform.position;
            float elapsedTime = 0f;

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Vault motion: Start {startPosition}, Landing {landingPosition}");
            }

            /* Execute vault motion for full duration */
            while (elapsedTime < VaultDuration)
            {
                elapsedTime += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsedTime / VaultDuration);

                /* Linear horizontal movement */
                Vector3 horizontalPosition = Vector3.Lerp(startPosition, landingPosition, normalizedTime);

                /* Sinusoidal arc for vertical movement (peak at 50% of duration) */
                float arcHeight = Mathf.Sin(normalizedTime * Mathf.PI) * (_controller.height * 0.5f);
                Vector3 currentPosition = horizontalPosition + Vector3.up * arcHeight;

                /* Apply position */
                transform.position = currentPosition;

                if (DebugVault && normalizedTime >= 0.5f && normalizedTime < 0.51f)
                {
                    Debug.Log($"[VAULT DEBUG] Vault arc peak reached (50% progress)");
                }

                yield return null;
            }

            /* Ensure we end at exact landing position */
            transform.position = landingPosition;

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Vault movement complete. Final position: {landingPosition}");
            }

            /* Re-enable CharacterController */
            _controller.enabled = true;

            /* Reset vertical velocity for gravity to work properly after landing */
            _verticalVelocity = 0f;

            _isVaultingRef = false;

            if (DebugVault)
            {
                Debug.Log("[VAULT DEBUG] ■ VAULT COMPLETED");
            }
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

            // ===== VAULT RAYCASTS VISUALIZATION =====
            if (!DebugVaultGizmos || !_controller)
                return;

            /* Draw all forward raycasts at different heights */
            float heightMin = _controller.height * 0.5f;
            float heightMax = _controller.height * 0.85f;
            float heightStep = (heightMax - heightMin) / Mathf.Max(1, VaultRaycastCount - 1);

            for (int i = 0; i < VaultRaycastCount; i++)
            {
                float heightOffset = heightMin + (i * heightStep);
                Vector3 rayOrigin = transform.position + Vector3.up * heightOffset;
                Vector3 rayEnd = rayOrigin + transform.forward * VaultCheckDistance;

                /* Color code: brighter yellow for center raycasts */
                float colorLerp = VaultRaycastCount > 1 ? i / (float)(VaultRaycastCount - 1) : 0.5f;
                Color rayColor = Color.Lerp(new Color(0.8f, 0.8f, 0f, 1f), new Color(1f, 1f, 0f, 1f), colorLerp);

                Gizmos.color = rayColor;
                Gizmos.DrawLine(rayOrigin, rayEnd);
                Gizmos.DrawSphere(rayOrigin, 0.06f);
            }

            /* Draw hits if any */
            for (int i = 0; i < _lastVaultForwardHits.Count; i++)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_lastVaultForwardHits[i].point, 0.08f);
            }

            /* Draw downward raycast from best hit point */
            if (_lastVaultDownwardHitValid)
            {
                RaycastHit bestHit = _lastVaultForwardHits[0];
                Vector3 downRayStart = bestHit.point + Vector3.up * VaultMaxHeight;
                Vector3 downRayEnd = bestHit.point - Vector3.up * 0.1f;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(downRayStart, downRayEnd);
                Gizmos.DrawSphere(downRayStart, 0.06f);

                /* Draw downward hit point (obstacle top) */
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(_lastVaultDownwardHit.point, 0.1f);
            }

            /* Draw landing position and capsule clearance zone */
            if (_lastVaultLandingValid)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(_lastVaultLandingPosition, 0.15f);

                /* Draw capsule at landing position */
                Vector3 capsuleBottom = _lastVaultLandingPosition + Vector3.up * _controller.radius;
                Vector3 capsuleTop = _lastVaultLandingPosition + Vector3.up * (_controller.height - _controller.radius);

                Gizmos.color = new Color(1.0f, 0.5f, 0.0f, 1.0f); // Orange
                Gizmos.DrawLine(capsuleBottom - Vector3.right * _controller.radius, 
                                capsuleBottom + Vector3.right * _controller.radius);
                Gizmos.DrawLine(capsuleBottom - Vector3.forward * _controller.radius, 
                                capsuleBottom + Vector3.forward * _controller.radius);
                Gizmos.DrawLine(capsuleTop - Vector3.right * _controller.radius, 
                                capsuleTop + Vector3.right * _controller.radius);
                Gizmos.DrawLine(capsuleTop - Vector3.forward * _controller.radius, 
                                capsuleTop + Vector3.forward * _controller.radius);
            }

            /* Draw vault distance range indicator */
            Gizmos.color = new Color(1.0f, 1.0f, 0.0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * (_controller.height * 0.7f), VaultCheckDistance);
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