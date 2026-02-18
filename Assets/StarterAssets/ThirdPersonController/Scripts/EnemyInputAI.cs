using UnityEngine;
using UnityEngine.AI;

namespace StarterAssets
{
    /// <summary>
    /// AI input provider for enemies using NavMeshAgent for pathfinding.
    /// Implements IInputProvider to feed movement commands into ThirdPersonController.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(ThirdPersonController))]
    public class EnemyInputAI : MonoBehaviour, IInputProvider
    {
        [Header("AI Input - Patrol")]
        [SerializeField] private Transform[] _patrolWaypoints;
        [SerializeField] private float _waypointStoppingDistance = 0.5f;

        [Header("AI Input - Detection")]
        [SerializeField] private Transform _playerTransform;
        [SerializeField] private float _sightRange = 15.0f;
        [SerializeField] private float _sightAngle = 60.0f; // degrees from forward
        [SerializeField] private bool _requireLineOfSight = true;
        [Tooltip("Layers that block line of sight (e.g., walls, obstacles)")]
        [SerializeField] private LayerMask _lineOfSightBlockers;

        [Header("AI Input - Chase")]
        [SerializeField] private float _lastKnownPositionTimeout = 5.0f;
        [SerializeField] private float _attackDistance = 2.0f;
        [SerializeField] private float _sprintDistance = 8.0f;

        [Header("Debug")]
        [SerializeField] private bool _debugDrawSight = false;
        [SerializeField] private bool _debugAIState = false;
        [SerializeField] private bool _debugAIAgent = false;

        // AI state
        private enum AIState
        {
            Patrolling,
            Chasing,
            SearchingLastKnown
        }

        private AIState _currentState = AIState.Patrolling;
        private int _currentWaypointIndex = 0;
        private Vector3 _lastKnownPlayerPosition;
        private float _timeSinceLastSight = 0f;
        private float _pathUpdateCooldown = 0.2f;
        private float _pathUpdateTimer = 0f;

        // IInputProvider fields
        private Vector2 _moveInput = Vector2.zero;
        private Vector2 _lookInput = Vector2.zero;
        private bool _jumpInput = false;
        private bool _sprintInput = false;
        private bool _vaultInput = false;

        // Components
        private NavMeshAgent _agent;
        private ThirdPersonController _controller;

        // IInputProvider properties
        public Vector2 move => _moveInput;
        public Vector2 look => _lookInput;
        public bool jump => _jumpInput;
        public bool sprint => _sprintInput;
        public bool vault => _vaultInput;
        public bool analogMovement => true; // AI uses analog movement for smooth pathing

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _controller = GetComponent<ThirdPersonController>();

            if (_agent == null)
            {
                Debug.LogError("[EnemyInputAI] NavMeshAgent component not found!", gameObject);
            }

            if (_controller == null)
            {
                Debug.LogError("[EnemyInputAI] ThirdPersonController component not found!", gameObject);
            }

            // Find player if not assigned
            if (_playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    _playerTransform = player.transform;
                }
            }
        }

        private void Start()
        {
            // Initialize NavMeshAgent - we'll control it manually
            if (_agent != null)
            {
                _agent.enabled = true;
                _agent.updatePosition = false; // We move the character, not NavMeshAgent
                _agent.updateRotation = false; // Disable NavMesh rotation to stop conflict with ThirdPersonController
                _agent.isStopped = false;
                
                // Match agent speed to controller speed for better simulation
                _agent.speed = _controller.MoveSpeed;
                _agent.acceleration = _controller.SpeedChangeRate * 2f;
            }

            // Force World Space movement for AI so it doesn't move relative to the camera
            if (_controller != null)
            {
                _controller.UseWorldSpaceMovement = true;
                _controller.IsEnemy = true;
            }

            if (_patrolWaypoints.Length == 0)
            {
                Debug.LogWarning("[EnemyInputAI] No patrol waypoints assigned! Enemy will stand idle.", gameObject);
            }

            // Initial destination
            if (_patrolWaypoints.Length > 0 && _patrolWaypoints[0] != null)
            {
                 _agent.SetDestination(_patrolWaypoints[0].position);
                 if (_debugAIState) Debug.Log($"[EnemyAI] Initial Dest: {_agent.destination} (Waypoint 0)");
            }
        }

        private void Update()
        {
            if (_agent == null || _controller == null) return;

            // Sync NavMeshAgent position with character every frame BEFORE calculating desired velocity
            _agent.nextPosition = transform.position;

            // Update AI logic
            UpdateAIState();

            // DEBUG: Detailed diagnostics
            if (Time.frameCount % 60 == 0) // Log once per second approx
            {
                 if (_debugAIState) Debug.Log($"[EnemyAI] State:{_currentState} Pos:{transform.position} Dest:{_agent.destination}");
                 if (_debugAIAgent)
                 {
                     Debug.Log($"[EnemyAI] Agent -> Vel:{_agent.velocity.magnitude:F2} DesVel:{_agent.desiredVelocity.magnitude:F2} Path:{_agent.hasPath} Status:{_agent.pathStatus}");
                     Debug.Log($"[EnemyAI] Input -> Move:{_moveInput} (Mag:{_moveInput.magnitude:F2})");
                 }
            }

            // Reset one-shot inputs (sprint is a state, handled by behaviors)
            _jumpInput = false;
            _vaultInput = false;
        }

        // Removed LateUpdate as we sync in Update for fresher data
        /*
        private void LateUpdate()
        {
            if (_agent == null) return;
            // _agent.nextPosition = transform.position;
        }
        */


        /// <summary>
        /// Main AI state machine logic
        /// </summary>
        private void UpdateAIState()
        {
            bool playerDetected = CanSeePlayer();

            switch (_currentState)
            {
                case AIState.Patrolling:
                    if (playerDetected)
                    {
                        _currentState = AIState.Chasing;
                        _lastKnownPlayerPosition = _playerTransform.position;
                        _timeSinceLastSight = 0f;
                    }
                    else
                    {
                        PatrolBehavior();
                    }
                    break;

                case AIState.Chasing:
                    if (playerDetected)
                    {
                        _lastKnownPlayerPosition = _playerTransform.position;
                        _timeSinceLastSight = 0f;
                        ChaseBehavior();
                    }
                    else
                    {
                        _timeSinceLastSight += Time.deltaTime;
                        if (_timeSinceLastSight > _lastKnownPositionTimeout)
                        {
                            _currentState = AIState.Patrolling;
                        }
                        else
                        {
                            SearchBehavior();
                        }
                    }
                    break;

                case AIState.SearchingLastKnown:
                    if (playerDetected)
                    {
                        _currentState = AIState.Chasing;
                        _lastKnownPlayerPosition = _playerTransform.position;
                        _timeSinceLastSight = 0f;
                    }
                    else
                    {
                        _timeSinceLastSight += Time.deltaTime;
                        if (_timeSinceLastSight > _lastKnownPositionTimeout)
                        {
                            _currentState = AIState.Patrolling;
                        }
                        else
                        {
                            SearchBehavior();
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Patrol between waypoints
        /// </summary>
        private void PatrolBehavior()
        {
            if (_patrolWaypoints.Length == 0)
            {
                _moveInput = Vector2.zero;
                return;
            }

            _sprintInput = false;

            Transform targetWaypoint = _patrolWaypoints[_currentWaypointIndex];

            // Update path periodically
            _pathUpdateTimer -= Time.deltaTime;
            if (_pathUpdateTimer <= 0f)
            {
                _agent.SetDestination(targetWaypoint.position);
                _pathUpdateTimer = _pathUpdateCooldown;
            }

            // Get desired direction from NavMeshAgent
            Vector3 desiredDirection = GetDesiredMovementDirection();

            if (desiredDirection.magnitude > 0.01f)
            {
                _moveInput = new Vector2(desiredDirection.x, desiredDirection.z).normalized;
            }
            else
            {
                _moveInput = Vector2.zero;
            }

            // Check if reached waypoint
            float distanceToWaypoint = Vector3.Distance(transform.position, targetWaypoint.position);
            if (distanceToWaypoint < _waypointStoppingDistance)
            {
                _currentWaypointIndex = (_currentWaypointIndex + 1) % _patrolWaypoints.Length;
            }
        }

        /// <summary>
        /// Chase the player
        /// </summary>
        private void ChaseBehavior()
        {
            float distanceToPlayer = Vector3.Distance(transform.position, _playerTransform.position);

            // If within attack range, stop moving and face player
            if (distanceToPlayer <= _attackDistance)
            {
                _moveInput = Vector2.zero;
                _sprintInput = false;
                
                // Face the player directly
                Vector3 direction = (_playerTransform.position - transform.position).normalized;
                direction.y = 0;
                if (direction != Vector3.zero)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);
                }
                
                return;
            }



            // Sprint only if far away
            _sprintInput = distanceToPlayer > _sprintDistance;

            _pathUpdateTimer -= Time.deltaTime;
            if (_pathUpdateTimer <= 0f)
            {
                _agent.SetDestination(_playerTransform.position);
                _pathUpdateTimer = _pathUpdateCooldown;
            }

            Vector3 desiredDirection = GetDesiredMovementDirection();

            if (desiredDirection.magnitude > 0.01f)
            {
                _moveInput = new Vector2(desiredDirection.x, desiredDirection.z).normalized;
            }
            else
            {
                _moveInput = Vector2.zero;
            }
        }

        /// <summary>
        /// Move to last known player position
        /// </summary>
        private void SearchBehavior()
        {
            _sprintInput = false;

            _pathUpdateTimer -= Time.deltaTime;
            if (_pathUpdateTimer <= 0f)
            {
                _agent.SetDestination(_lastKnownPlayerPosition);
                _pathUpdateTimer = _pathUpdateCooldown;
            }

            Vector3 desiredDirection = GetDesiredMovementDirection();

            if (desiredDirection.magnitude > 0.01f)
            {
                _moveInput = new Vector2(desiredDirection.x, desiredDirection.z).normalized;
            }
            else
            {
                _moveInput = Vector2.zero;
            }
        }

        /// <summary>
        /// Reads NavMeshAgent's desired velocity and converts to movement input.
        /// AI does not use camera-relative input; movement is in world space.
        /// </summary>
        private Vector3 GetDesiredMovementDirection()
        {
            if (_agent == null || !_agent.isOnNavMesh)
                return Vector3.zero;

            Vector3 vel = _agent.desiredVelocity;
            vel.y = 0f;

            if (vel.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            return vel.normalized;
        }


        /// <summary>
        /// Determines if the AI can see the player
        /// </summary>
        private bool CanSeePlayer()
        {
            if (_playerTransform == null) return false;

            Vector3 directionToPlayer = (_playerTransform.position - transform.position);
            float distanceToPlayer = directionToPlayer.magnitude;

            // Check distance
            if (distanceToPlayer > _sightRange)
            {
                return false;
            }

            // Check angle
            float angleToPlayer = Vector3.Angle(transform.forward, directionToPlayer);
            if (angleToPlayer > _sightAngle)
            {
                return false;
            }

            // Check line of sight
            if (_requireLineOfSight)
            {
                Vector3 eyePosition = transform.position + Vector3.up * 1.5f;
                Vector3 playerEyePosition = _playerTransform.position + Vector3.up * 1.5f;
                Vector3 sightDirection = (playerEyePosition - eyePosition).normalized;

                RaycastHit hit;
                if (Physics.Raycast(eyePosition, sightDirection, out hit, distanceToPlayer, _lineOfSightBlockers, QueryTriggerInteraction.Ignore))
                {
                    // Something is blocking the line of sight
                    if (_debugDrawSight)
                    {
                        Debug.DrawLine(eyePosition, hit.point, Color.yellow);
                    }
                    return false;
                }

                if (_debugDrawSight)
                {
                    Debug.DrawLine(eyePosition, playerEyePosition, Color.green);
                }
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_debugDrawSight) return;

            // Draw sight range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _sightRange);

            // Draw sight cone
            Vector3 forward = transform.forward;
            Vector3 right = Quaternion.AngleAxis(_sightAngle, transform.up) * forward;
            Vector3 left = Quaternion.AngleAxis(-_sightAngle, transform.up) * forward;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + right * _sightRange);
            Gizmos.DrawLine(transform.position, transform.position + left * _sightRange);

            // Draw waypoints
            if (_patrolWaypoints.Length > 0)
            {
                Gizmos.color = Color.blue;
                for (int i = 0; i < _patrolWaypoints.Length; i++)
                {
                    if (_patrolWaypoints[i] != null)
                    {
                        Gizmos.DrawWireSphere(_patrolWaypoints[i].position, 0.5f);

                        // Draw line to next waypoint
                        Transform nextWaypoint = _patrolWaypoints[(i + 1) % _patrolWaypoints.Length];
                        Gizmos.DrawLine(_patrolWaypoints[i].position, nextWaypoint.position);
                    }
                }
            }

            // Draw GREEN Sphere if Player is currently SEEN
            if (Application.isPlaying && CanSeePlayer())
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_playerTransform.position + Vector3.up * 1.5f, 0.4f);
            }

            // Draw RED Sphere at Last Known Position (Memory)
            if (_currentState == AIState.Chasing || _currentState == AIState.SearchingLastKnown)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(_lastKnownPlayerPosition + Vector3.up * 1.5f, 0.4f);
                // Draw line from current pos to last known
                Gizmos.DrawLine(transform.position + Vector3.up, _lastKnownPlayerPosition + Vector3.up * 1.5f);
            }
        }
    }
}
