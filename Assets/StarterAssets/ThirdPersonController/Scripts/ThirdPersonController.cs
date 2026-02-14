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
        private bool _isVaulting = false;

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
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
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
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM 
            _playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
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
            _hasAnimator = TryGetComponent(out _animator);

            JumpAndGravity();
            GroundedCheck();

            /* Check for vault attempt before normal movement */
            if (Grounded && _input.vault && !_isVaulting)
            {
                Vector3 vaultLandingPosition;
                if (DebugVault)
                {
                    Debug.Log("[VAULT DEBUG] Vault input detected! Checking for valid landing...");
                }

                if (TryGetVaultLanding(out vaultLandingPosition))
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
            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw, 0.0f);
        }

        private void Move()
        {
            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

            // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is no input, set the target speed to 0
            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            // a reference to the players current horizontal velocity
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // accelerate or decelerate to target speed
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                // creates curved result rather than a linear one giving a more organic speed change
                // note T in Lerp is clamped, so we don't need to clamp our speed
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                // round speed to 3 decimal places
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

            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (_input.move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                  _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);

                // rotate to face input direction relative to camera position
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }


            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            // move the player
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            // update animator if using character
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

                // if we are not grounded, do not jump
                _input.jump = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
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
            bool foundObstacle = false;

            /* Calculate height spread for raycasts - WAIST LEVEL DETECTION */
            float heightMin = _controller.height * 0.3f;  /* Lower waist */
            float heightMax = _controller.height * 0.65f; /* Mid-chest */
            float heightStep = (heightMax - heightMin) / Mathf.Max(1, VaultRaycastCount - 1);

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Performing {VaultRaycastCount} forward raycasts at waist level...");
            }

            /* Cast multiple rays at different heights */
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

                    if (DebugVault)
                    {
                        Debug.Log($"[VAULT DEBUG] Raycast {i + 1}/{VaultRaycastCount} HIT: {hitInfo.collider.name} at distance {hitInfo.distance:F2}m (height: {heightOffset:F2})");
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

            /* Calculate obstacle height */
            float obstacleHeight = downHit.point.y - bestHit.point.y;

            if (DebugVault)
            {
                Debug.Log($"[VAULT DEBUG] Obstacle height: {obstacleHeight:F2}m (Min: {VaultMinHeight:F2}m, Max: {VaultMaxHeight:F2}m)");
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

            /* Calculate landing position: on top of obstacle + offset */
            Vector3 landingPositionOnObstacle = downHit.point + Vector3.up * VaultUpwardOffset;
            vaultLandingPosition = landingPositionOnObstacle + transform.forward * VaultForwardOffset;

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
            _isVaulting = true;

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

            /* Reset vertical velocity for gravity to work properly */
            _verticalVelocity = 0f;

            _isVaulting = false;

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