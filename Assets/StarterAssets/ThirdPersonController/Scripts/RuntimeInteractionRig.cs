/*
 * Created :    2026-02-18
 * Author :     Antigravity (Advanced Agentic Coding)
 * Project :    Unified Interaction System
 * Description: Handles Foot IK, Hand Interactions, and Step-Up mechanics using Unity Animation Rigging.
 *              Automatically builds the Rig hierarchy at runtime.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StarterAssets
{

    [DefaultExecutionOrder(-100)] // FIX: Run before Animation Rigging
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(RigBuilder))] // Dependencies
    public class RuntimeInteractionRig : MonoBehaviour
    {
        [Header("Settings - Foot IK")]
        [SerializeField] private LayerMask groundLayerMask = 1; // Default
        [SerializeField] private float footRaycastDistance = 1.5f;
        [SerializeField] private float footHeightOffset = 0.12f;
        [SerializeField] private float pelvicDropSpeed = 10f;
        [SerializeField] private float stepUpHeight = 0.5f;
        [SerializeField] private bool useAnimationCurves = true; // NEW: Toggle for curves

        [Header("Settings - Hand IK")]
        [SerializeField] private LayerMask wallLayerMask = 1; // Default
        [SerializeField] private float wallTouchDetails = 0.6f;
        [SerializeField] private float handReachSpeed = 8f;

        [Header("Target References (Manual Setup)")]
        [Tooltip("Assign the Left Foot Target Transform")]
        [SerializeField] private Transform leftFootTarget;
        [Tooltip("Assign the Right Foot Target Transform")]
        [SerializeField] private Transform rightFootTarget;
        [Tooltip("Assign the Left Hand Target Transform")]
        [SerializeField] private Transform leftHandTarget;
        [Tooltip("Assign the Right Hand Target Transform")]
        [SerializeField] private Transform rightHandTarget;
        [Tooltip("Assign the Hips Target Transform")]
        [SerializeField] private Transform hipsTarget;

        [Header("Hint References (Manual Setup)")]
        [Tooltip("Assign Left Knee Hint")]
        [SerializeField] private Transform leftKneeHint;
        [Tooltip("Assign Right Knee Hint")]
        [SerializeField] private Transform rightKneeHint;
        [Tooltip("Assign Left Elbow Hint")]
        [SerializeField] private Transform leftElbowHint;
        [Tooltip("Assign Right Elbow Hint")]
        [SerializeField] private Transform rightElbowHint;

        [Header("Constraint References (Manual Setup)")]
        [Tooltip("Assign the Left Foot TwoBoneIKConstraint here")]
        [SerializeField] private TwoBoneIKConstraint _leftFootConstraint;
        [Tooltip("Assign the Right Foot TwoBoneIKConstraint here")]
        [SerializeField] private TwoBoneIKConstraint _rightFootConstraint;
        [Tooltip("Assign the Left Hand TwoBoneIKConstraint here")]
        [SerializeField] private TwoBoneIKConstraint _leftHandConstraint;
        [Tooltip("Assign the Right Hand TwoBoneIKConstraint here")]
        [SerializeField] private TwoBoneIKConstraint _rightHandConstraint;
        [Tooltip("Assign the Hips MultiPositionConstraint here")]
        [SerializeField] private MultiPositionConstraint _hipsConstraint;

        private Animator _animator;
        private ThirdPersonController _controller;
        private Rig _rig; 
        private RigBuilder _rigBuilder;
        private float _currentPelvisOffset;
        private Vector3 _originalHipsLocalPos;

        // Debug Data
        private Vector3 _leftFootHitPos, _leftFootHitNormal;
        private Vector3 _rightFootHitPos, _rightFootHitNormal;
        private Vector3 _leftHandHitPos, _leftHandHitNormal;
        private Vector3 _rightHandHitPos, _rightHandHitNormal;
        private bool _leftFootGrounded, _rightFootGrounded;
        private bool _leftHandTouching, _rightHandTouching;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _controller = GetComponent<ThirdPersonController>();
            _rigBuilder = GetComponent<RigBuilder>();
            // Try to find Rig component in children or self
            _rig = GetComponentInChildren<Rig>();  
        }

        private void Start()
        {
             if (_animator == null) Debug.LogError("RuntimeInteractionRig: Missing Animator!");
             if (_controller == null) Debug.LogWarning("RuntimeInteractionRig: Missing ThirdPersonController! Ground checks may fail.");
             if (_rig == null) Debug.LogWarning("RuntimeInteractionRig: Could not find 'Rig' component in children. Ensure you created 'IK_Rig'.");
             
             if (_leftFootConstraint == null || _rightFootConstraint == null)
             {
                 Debug.LogWarning("RuntimeInteractionRig: Constraints are not assigned! Please assign them in the Inspector.");
                 return;
             }
             
             if (leftFootTarget == null || rightFootTarget == null)
             {
                 Debug.LogWarning("RuntimeInteractionRig: Foot Targets are not assigned! Please assign them in the Inspector.");
             }

             // Store original hips position for offsetting
             Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
             if (hips) _originalHipsLocalPos = hips.localPosition;

             // Auto-Fix: Ensure Constraints use the correct Targets and SNAP to them (Disable Offsets)
             // This solves "Floating Feet" where the target moves but the foot stays offset.
             if (_leftFootConstraint && leftFootTarget) 
             { 
                 var d = _leftFootConstraint.data; 
                 d.target = leftFootTarget; 
                 d.maintainTargetPositionOffset = false; 
                 d.maintainTargetRotationOffset = false; 
                 _leftFootConstraint.data = d; 
                 
                 // Snap Target to Foot initially
                 var footParams = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                 if (footParams) { leftFootTarget.position = footParams.position; leftFootTarget.rotation = footParams.rotation; }
             }
             if (_rightFootConstraint && rightFootTarget) 
             { 
                 var d = _rightFootConstraint.data; 
                 d.target = rightFootTarget; 
                 d.maintainTargetPositionOffset = false; 
                 d.maintainTargetRotationOffset = false; 
                 _rightFootConstraint.data = d; 

                 // Snap Target to Foot initially
                 var footParams = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
                 if (footParams) { rightFootTarget.position = footParams.position; rightFootTarget.rotation = footParams.rotation; }
             }

             // Hands: Same logic (Snap)
             if (_leftHandConstraint && leftHandTarget) 
             { 
                 var d = _leftHandConstraint.data; 
                 d.target = leftHandTarget; 
                 d.maintainTargetPositionOffset = false; 
                 d.maintainTargetRotationOffset = false;
                 _leftHandConstraint.data = d; 
             }
             if (_rightHandConstraint && rightHandTarget) 
             { 
                 var d = _rightHandConstraint.data; 
                 d.target = rightHandTarget; 
                 d.maintainTargetPositionOffset = false; 
                 d.maintainTargetRotationOffset = false;
                 _rightHandConstraint.data = d; 
             }
             
             // Hips: Maintain Offset (Keep original relative)
             if (_hipsConstraint && hipsTarget) 
             { 
                 var d = _hipsConstraint.data; 
                 d.sourceObjects = new WeightedTransformArray { new WeightedTransform(hipsTarget, 1f) };
                 d.maintainOffset = true;
                 _hipsConstraint.data = d; 
             }
             
             // Ensure Rig Builder is built
             if (_rigBuilder != null) _rigBuilder.Build();
             
             // FIX: Initialize Hand Weights to 0
             if (_leftHandConstraint) _leftHandConstraint.weight = 0f;
             if (_rightHandConstraint) _rightHandConstraint.weight = 0f;
        }

        private void LateUpdate()
        {
            if (_animator == null) return;
            if (_leftFootConstraint == null) return; 

            // 0. Global Weight Control (Disable IK when Jumping/Falling or Vaulting)
            if (_controller != null)
            {
                // Strict State Check: Only run IK if Grounded AND Not Vaulting
                bool shouldRunIK = _controller.Grounded && !_controller.IsVaulting;
                
                float targetWeight = shouldRunIK ? 1f : 0f;
                // Faster blend out for jump/vault to prevent glitches
                float blendSpeed = shouldRunIK ? 10f : 20f; 
                
                if (_rig != null) 
                {
                    _rig.weight = Mathf.Lerp(_rig.weight, targetWeight, Time.deltaTime * blendSpeed);
                    
                    // If weight is essentially zero, return early to save cost and avoid weird posing
                    if (_rig.weight < 0.01f)
                    {
                         _rig.weight = 0f;
                         _currentPelvisOffset = 0f; // Reset offset
                         return;
                    }
                }

                // If airborne/vaulting, reset pelvis offset
                if (!shouldRunIK)
                {
                    _currentPelvisOffset = Mathf.Lerp(_currentPelvisOffset, 0f, Time.deltaTime * 10f);
                    Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hipsTarget != null && hips != null)
                    {
                         hipsTarget.position = hips.position; 
                         hipsTarget.rotation = hips.rotation;
                    }
                    // Reset Hand Weights too
                    if (_leftHandConstraint) _leftHandConstraint.weight = Mathf.Lerp(_leftHandConstraint.weight, 0f, Time.deltaTime * 15f);
                    if (_rightHandConstraint) _rightHandConstraint.weight = Mathf.Lerp(_rightHandConstraint.weight, 0f, Time.deltaTime * 15f);
                    
                    return; // Strictly skip IK processing
                }
            }
            else
            {
                // Fallback if no controller: Force Rig On
                if (_rig != null) _rig.weight = 1f;
            }

            // 1. Update Foot IK (Ground Placement + Pelvis Drag)
            float l_offset = HandleFootIK(HumanBodyBones.LeftFoot, _leftFootConstraint, leftFootTarget, leftKneeHint, "LeftFootIK");
            float r_offset = HandleFootIK(HumanBodyBones.RightFoot, _rightFootConstraint, rightFootTarget, rightKneeHint, "RightFootIK");

            // 2. Handle Pelvis Drop (Lowest Foot)
            float targetDrop = Mathf.Min(l_offset, r_offset);
            targetDrop = Mathf.Clamp(targetDrop, -0.8f, 0.2f); // Limit drop

            // Smoothly interpolate pelvis offset
            _currentPelvisOffset = Mathf.Lerp(_currentPelvisOffset, targetDrop, Time.deltaTime * pelvicDropSpeed);
            
            // Apply to Hips Target
            if (hipsTarget != null && _hipsConstraint != null)
            {
                 Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                 if (hips != null)
                 {
                    // Update Target Position
                    hipsTarget.position = hips.position + Vector3.up * _currentPelvisOffset;
                    
                    // Force Hips Constraint Weight
                    _hipsConstraint.weight = 1f;
                 }
            }

            // 3. Handle Wall Touching (Simple Raycast)
            if (_leftHandConstraint != null) HandleHandWallTouch(HumanBodyBones.LeftHand, _leftHandConstraint, leftHandTarget, leftElbowHint, -transform.right);
            if (_rightHandConstraint != null) HandleHandWallTouch(HumanBodyBones.RightHand, _rightHandConstraint, rightHandTarget, rightElbowHint, transform.right);
        }

        // --- Interaction API ---
        public void SetLeftHandTarget(Transform target, float weight)
        {
            if (leftHandTarget == null) return;
            // Override Hand IK for interaction
            // This is a simple implementation. For complex interactions, we'd blend weights.
            // Here we assume 'target' is a temporary override.
            
            // To do this properly with Rigging, we usually add a NEW Constraint layer or override the current target.
            // Let's override the current target position/rotation securely.
            if (weight > 0)
            {
                leftHandTarget.position = Vector3.Lerp(leftHandTarget.position, target.position, weight);
                leftHandTarget.rotation = Quaternion.Slerp(leftHandTarget.rotation, target.rotation, weight);
                _leftHandConstraint.weight = Mathf.Lerp(_leftHandConstraint.weight, 1f, weight);
            }
        }

        public void SetRightHandTarget(Transform target, float weight)
        {
             if (rightHandTarget == null) return;
             if (weight > 0)
             {
                 rightHandTarget.position = Vector3.Lerp(rightHandTarget.position, target.position, weight);
                 rightHandTarget.rotation = Quaternion.Slerp(rightHandTarget.rotation, target.rotation, weight);
                 _rightHandConstraint.weight = Mathf.Lerp(_rightHandConstraint.weight, 1f, weight);
             }
        }

        private float HandleFootIK(HumanBodyBones bone, TwoBoneIKConstraint constraint, Transform target, Transform hint, string curveName)
        {
            Transform boneTransform = _animator.GetBoneTransform(bone);
            if (boneTransform == null) return 0f;

            // Raycast Origin: Above the foot
            Vector3 origin = boneTransform.position + Vector3.up * 0.5f;
            
            // Step-Up Prediction
             Vector3 kneePos = boneTransform.position + Vector3.up * 0.3f;
             bool isSteppingUp = false;
             float stepOffset = 0f;

            // Step-Up Smoothing Var
             float stepTargetY = boneTransform.position.y;

             // Check for step-up
             RaycastHit forwardHit;
             Vector3 checkDir = transform.forward;
             if (Physics.Raycast(kneePos, checkDir, out forwardHit, 0.5f, groundLayerMask))
             {
                 Vector3 stepCheckOrigin = forwardHit.point + Vector3.up * 0.5f + checkDir * 0.1f;
                 if (Physics.Raycast(stepCheckOrigin, Vector3.down, out RaycastHit stepTopHit, 1f, groundLayerMask))
                 {
                      if (stepTopHit.point.y > boneTransform.position.y + 0.05f && stepTopHit.point.y < boneTransform.position.y + stepUpHeight)
                      {
                          isSteppingUp = true;
                          stepOffset = stepTopHit.point.y - boneTransform.position.y;
                          stepTargetY = stepTopHit.point.y + footHeightOffset;
                      }
                 }
             }

            // SphereCast to find ground
            RaycastHit hit;
            if (Physics.SphereCast(origin, 0.1f, Vector3.down, out hit, footRaycastDistance, groundLayerMask))
            {
                Vector3 targetPos = hit.point + Vector3.up * footHeightOffset;
                
                if (isSteppingUp)
                {
                     // FIX: Smoothly blend step offset based on distance
                     float dist = Vector3.Distance(new Vector3(kneePos.x, 0, kneePos.z), new Vector3(forwardHit.point.x, 0, forwardHit.point.z));
                     float stepWeight = 1f - Mathf.Clamp01((dist - 0.1f) / 0.3f); // Max weight when close
                     
                     // Lerp to step height (incorporating foot offset for proper placement)
                     targetPos.y = Mathf.Lerp(targetPos.y, stepTargetY, stepWeight);
                }

                // Align Rotation
                // FIX: Use bone forward projected, not transform forward
                Vector3 boneForward = boneTransform.forward;
                Vector3 projectForward = Vector3.ProjectOnPlane(boneForward, hit.normal);
                Quaternion targetRot = Quaternion.LookRotation(projectForward, hit.normal);

                // Set Target
                target.position = targetPos;
                target.rotation = targetRot;
                
                // Blend In
                float targetWeight = 1f;
                // CHECK CURVES
                if (useAnimationCurves && _animator)
                {
                    targetWeight *= _animator.GetFloat(curveName); 
                }
                
                constraint.weight = Mathf.Lerp(constraint.weight, targetWeight, Time.deltaTime * 10f);

                // Update Debug Data
                if (bone == HumanBodyBones.LeftFoot) { _leftFootHitPos = hit.point; _leftFootHitNormal = hit.normal; _leftFootGrounded = true; }
                if (bone == HumanBodyBones.RightFoot) { _rightFootHitPos = hit.point; _rightFootHitNormal = hit.normal; _rightFootGrounded = true; }

                return targetPos.y - boneTransform.position.y;
            }



            // No hit -> Fade out
            constraint.weight = Mathf.Lerp(constraint.weight, 0f, Time.deltaTime * 10f);
            target.position = boneTransform.position;
            target.rotation = boneTransform.rotation;
            
            // Clear Debug Data
            if (bone == HumanBodyBones.LeftFoot) _leftFootGrounded = false;
            if (bone == HumanBodyBones.RightFoot) _rightFootGrounded = false;
            
            return 0f;
        }

        private void HandleHandWallTouch(HumanBodyBones bone, TwoBoneIKConstraint constraint, Transform target, Transform hint, Vector3 dir)
        {
            Transform shoulder = _animator.GetBoneTransform(bone == HumanBodyBones.LeftHand ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
            Vector3 origin = shoulder.position;
            
            // Debug Draw
            Debug.DrawRay(origin, dir * wallTouchDetails, Color.cyan);
            
            if (Physics.Raycast(origin, dir, out RaycastHit hit, wallTouchDetails, wallLayerMask))
            {
                target.position = Vector3.Lerp(target.position, hit.point + hit.normal * 0.1f, Time.deltaTime * handReachSpeed);
                target.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
                
                // FIX: Distance-based Weighting (prevent "stuck out" arms)
                float distFactor = 1f - Mathf.Clamp01(hit.distance / wallTouchDetails);
                float targetWeight = Mathf.SmoothStep(0f, 1f, distFactor);
                
                constraint.weight = Mathf.Lerp(constraint.weight, targetWeight, Time.deltaTime * 5f);
                
                // Debug Data
                 if (bone == HumanBodyBones.LeftHand) { _leftHandHitPos = hit.point; _leftHandHitNormal = hit.normal; _leftHandTouching = true; }
                 if (bone == HumanBodyBones.RightHand) { _rightHandHitPos = hit.point; _rightHandHitNormal = hit.normal; _rightHandTouching = true; }
            }
            else
            {
                constraint.weight = Mathf.Lerp(constraint.weight, 0f, Time.deltaTime * 5f);
                Transform handBone = _animator.GetBoneTransform(bone);
                target.position = handBone.position;
                target.rotation = handBone.rotation;
                
                if (bone == HumanBodyBones.LeftHand) _leftHandTouching = false;
                if (bone == HumanBodyBones.RightHand) _rightHandTouching = false;
            }
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (leftFootTarget != null) 
            { 
                Gizmos.color = Color.green; 
                Gizmos.DrawWireSphere(leftFootTarget.position, 0.1f); 
                string label = $"L Foot Tgt (W:{_leftFootConstraint?.weight:F2})";
                Handles.Label(leftFootTarget.position + Vector3.up * 0.2f, label);
            }
            if (rightFootTarget != null) 
            { 
                Gizmos.color = Color.green; 
                Gizmos.DrawWireSphere(rightFootTarget.position, 0.1f); 
                Handles.Label(rightFootTarget.position + Vector3.up * 0.2f, "R Foot Target");
            }
            if (hipsTarget != null) 
            { 
                Gizmos.color = Color.blue; 
                Gizmos.DrawWireSphere(hipsTarget.position, 0.15f); 
                Handles.Label(hipsTarget.position + Vector3.up * 0.2f, "Hips Target");
            }
            
            // Draw Hints
            Gizmos.color = Color.yellow;
            if (leftKneeHint != null) Gizmos.DrawWireSphere(leftKneeHint.position, 0.05f);
            if (rightKneeHint != null) Gizmos.DrawWireSphere(rightKneeHint.position, 0.05f);
            if (leftElbowHint != null) Gizmos.DrawWireSphere(leftElbowHint.position, 0.05f);
            if (rightElbowHint != null) Gizmos.DrawWireSphere(rightElbowHint.position, 0.05f);

            // Visualize Wall Rays
            Gizmos.color = Color.cyan;
            Vector3 lOrigins = transform.position + Vector3.up * 1.4f; 
            Gizmos.DrawLine(lOrigins, lOrigins - transform.right * wallTouchDetails);
            Gizmos.DrawLine(lOrigins, lOrigins + transform.right * wallTouchDetails);

            // Rich Visualization (Discs)
            if (Application.isPlaying)
            {
                if (_leftFootGrounded)
                {
                    Handles.color = new Color(0, 1, 0, 0.5f);
                    Handles.DrawSolidDisc(_leftFootHitPos, _leftFootHitNormal, 0.2f);
                    Handles.DrawLine(leftFootTarget.position, _leftFootHitPos);
                
                // Show Ray Origin
                 var lBone = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                 if (lBone) {
                     Handles.color = Color.yellow;
                     Handles.DrawWireDisc(lBone.position + Vector3.up * 0.5f, Vector3.up, 0.05f); // Origin
                     Handles.DrawLine(lBone.position, lBone.position + Vector3.up * 0.5f);
                 }
                }
                if (_rightFootGrounded)
                {
                    Handles.color = new Color(0, 1, 0, 0.5f);
                    Handles.DrawSolidDisc(_rightFootHitPos, _rightFootHitNormal, 0.2f);
                    Handles.DrawLine(rightFootTarget.position, _rightFootHitPos);
                    
                    // Show Ray Origin
                     var rBone = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
                     if (rBone) {
                         Handles.color = Color.yellow;
                         Handles.DrawWireDisc(rBone.position + Vector3.up * 0.5f, Vector3.up, 0.05f); // Origin
                         Handles.DrawLine(rBone.position, rBone.position + Vector3.up * 0.5f);
                     }
                }
                if (_leftHandTouching)
                {
                    Handles.color = new Color(0, 1, 1, 0.5f);
                    Handles.DrawSolidDisc(_leftHandHitPos, _leftHandHitNormal, 0.1f);
                }
                if (_rightHandTouching)
                {
                    Handles.color = new Color(0, 1, 1, 0.5f);
                    Handles.DrawSolidDisc(_rightHandHitPos, _rightHandHitNormal, 0.1f);
                }
            }
#endif
        }


    }
}
