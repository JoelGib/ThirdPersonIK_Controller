// RUNTIME INTERACTION RIG
//  - FOOT IK : Unreal-style "Leg IK + Pelvis Offset", solved in the Animator's humanoid IK pass
//  - HAND IK : Anatomical shoulder-to-wall & vault-surface Hand IK, also solved in OnAnimatorIK
//
// HOW THE FOOT IK WORKS (same idea as UE's default third person / Control Rig foot placement):
//   The CharacterController capsule always rests on the HIGHEST point under it (rounded bottom).
//   We can't change that, so we fix it visually:
//     1. For each foot, trace down under the ANIMATED foot position.
//     2. footOffset = groundHeightUnderFoot - capsuleBottomHeight     (usually <= 0)
//     3. pelvisOffset = min(leftOffset, rightOffset, 0)               (lower hips to reach lowest foot)
//     4. foot IK goal = animatedFootPosition + up * footOffset         (ADD to animation, don't replace)
//        -> the walk/run swing lift is preserved, the knee bends to absorb the pelvis drop.
//
// HOW THE HAND IK WORKS:
//   - Traces from each SHOULDER (at chest height, angled slightly forward) instead of the hips.
//   - Only reacts to steep wall surfaces (ignores floors, ramps, and walkable slopes).
//   - Aligns the true PALM normal flat against the wall and points the FINGERS upward (automatically
//     calibrated from the rig's finger & thumb bones, so mirrored left/right FBX bone axes work cleanly).
//   - During a vault, suppresses side wall-touch and optionally plants the hand on top of the obstacle.
//
// REQUIREMENT: "IK Pass" must be ticked on the Base Layer of the Animator Controller.

using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace StarterAssets
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(RigBuilder))]
    public class RuntimeInteractionRig : MonoBehaviour
    {
        public enum CastType { Raycast, Spherecast }

        [Header("Foot IK - Toggles")]
        [SerializeField] bool enableFootIK = true;
        [SerializeField] bool enablePelvisOffset = true;
        [SerializeField] bool alignFeetToSlope = true;

        [Header("Foot IK - Tracing")]
        [SerializeField] LayerMask groundLayerMask = 1;
        [SerializeField] CastType currentCastType = CastType.Raycast;
        [SerializeField] float spherecastRadius = 0.05f;
        [Tooltip("Trace starts this far ABOVE the capsule bottom (lets a foot step up onto something higher)")]
        [SerializeField] float traceStartHeight = 0.5f;
        [Tooltip("Trace reaches this far BELOW the capsule bottom")]
        [SerializeField] float traceBelowDistance = 0.75f;

        [Header("Foot IK - Limits")]
        [Tooltip("Max distance a foot may be pushed DOWN from its animated position")]
        [SerializeField] float maxFootDrop = 0.55f;
        [Tooltip("Max distance a foot may be pushed UP from its animated position")]
        [SerializeField] float maxFootRaise = 0.35f;
        [Tooltip("Max distance the pelvis/hips may be lowered")]
        [SerializeField] float maxPelvisDropDistance = 0.5f;
        [Tooltip("Max foot tilt to match slopes (degrees)")]
        [SerializeField, Range(0f, 60f)] float maxFootAngle = 40f;
        [Tooltip("Extra height added to the foot goal (fine tune if soles clip or float)")]
        [SerializeField] float footSoleOffset = 0f;

        [Header("Foot IK - Smoothing & Curves")]
        [Tooltip("How fast each foot follows ground height changes")]
        [SerializeField] float footInterpSpeed = 15f;
        [Tooltip("How fast the pelvis lowers/raises")]
        [SerializeField] float pelvisAdjustSpeed = 10f;
        [Tooltip("Seconds to blend Foot IK ON (e.g. when landing)")]
        [SerializeField, Range(0.02f, 1f)] float footBlendInDuration = 0.22f;
        [Tooltip("Seconds to blend Foot IK OFF (e.g. when jumping or vaulting)")]
        [SerializeField, Range(0.02f, 1f)] float footBlendOutDuration = 0.14f;
        [Tooltip("Custom curve shaping Foot IK weight (X = 0..1 blend progress, Y = IK weight)")]
        [SerializeField] AnimationCurve footIKBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Hand IK - Wall Touch & Vault")]
        [SerializeField] bool enableHandIK = true;
        [Tooltip("Disable sideways wall-touch Hand IK while sprinting (vault hand plant still works)")]
        [SerializeField] bool disableWallHandWhileSprinting = true;
        [Tooltip("Place a supporting hand on top of the obstacle while vaulting")]
        [SerializeField] bool enableVaultHandIK = true;
        [SerializeField] LayerMask wallLayerMask = 1;
        [Tooltip("Max distance from the shoulder to detect a wall")]
        [SerializeField] float wallTouchDistance = 0.55f;
        [Tooltip("How far below shoulder height the hand rests on the wall")]
        [SerializeField] float handHeightBelowShoulder = 0.14f;
        [Tooltip("How far in front of the shoulder the hand rests on the wall (gives room for elbow bend)")]
        [SerializeField] float handForwardOffset = 0.18f;
        [Tooltip("Small clearance along the wall normal so the palm sits flush without clipping")]
        [SerializeField] float handSurfaceOffset = 0.03f;
        [Tooltip("Vertical offset for the vault hand plant on top of the obstacle (lower if hovering, raise if clipping)")]
        [SerializeField] float vaultHandHeightOffset = 0.015f;
        [Tooltip("Outward finger tilt angle on the wall (degrees) for a relaxed wrist")]
        [SerializeField, Range(0f, 35f)] float handFingerTiltAngle = 14f;
        [Tooltip("Max fraction of full arm length allowed (keeps a natural elbow bend so the arm never locks rigidly straight)")]
        [SerializeField, Range(0.75f, 0.96f)] float maxArmExtension = 0.90f;

        [Header("Hand IK - Smoothing & Curves")]
        [Tooltip("Seconds to reach out and blend Wall-Touch Hand IK ON")]
        [SerializeField, Range(0.05f, 1f)] float handBlendInDuration = 0.28f;
        [Tooltip("Seconds to relax and blend Wall-Touch Hand IK OFF")]
        [SerializeField, Range(0.05f, 1f)] float handBlendOutDuration = 0.35f;
        [Tooltip("Custom curve shaping Wall-Touch Hand IK weight (X = 0..1 blend progress, Y = IK weight)")]
        [SerializeField] AnimationCurve wallHandBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Custom curve shaping Vault Hand Plant weight over the vault timeline (X = VaultNormalizedTime 0..1, Y = IK weight)")]
        [SerializeField] AnimationCurve vaultHandWeightCurve = new AnimationCurve(
            new Keyframe(0.00f, 0f),
            new Keyframe(0.08f, 0f),
            new Keyframe(0.22f, 1f),
            new Keyframe(0.45f, 1f),
            new Keyframe(0.68f, 0f),
            new Keyframe(1.00f, 0f)
        );
        [Tooltip("SmoothDamp time for hand position tracking along a surface (higher = softer, less mechanical)")]
        [SerializeField, Range(0.01f, 0.25f)] float handPositionSmoothTime = 0.07f;
        [SerializeField] float handNormalTrackSpeed = 12f;
        public float leftHandWeight;
        public float rightHandWeight;

        [Header("Scene Targets (optional visualizers)")]
        [SerializeField] Transform leftHandTarget;
        [SerializeField] Transform rightHandTarget;

        [Header("Legacy Rig Constraints (forced to weight 0)")]
        [Tooltip("All IK is now solved in OnAnimatorIK from the pure animated pose, so these constraints are kept at 0.")]
        [SerializeField] TwoBoneIKConstraint leftFootIK;
        [SerializeField] TwoBoneIKConstraint rightFootIK;
        [SerializeField] TwoBoneIKConstraint leftHandIK;
        [SerializeField] TwoBoneIKConstraint rightHandIK;
        [SerializeField] OverrideTransform hipsConstraint;

        [Header("Debug")]
        [SerializeField] bool debugFootIK = false;
        [SerializeField] bool debugHandIK = false;

        Animator animator;
        ThirdPersonController tpc;
        Rig[] childRigs;
        IRigConstraint[] childConstraints;

        // Foot IK runtime state
        float footBlendT;
        float ikWeight;
        float leftFootOffset;
        float rightFootOffset;
        float pelvisOffset;
        Vector3 leftNormal = Vector3.up;
        Vector3 rightNormal = Vector3.up;

        // Hand IK calibration & runtime state
        struct HandCalibration
        {
            public Quaternion localBasis;      // LookRotation(localPalmNormal, localFingerDir)
            public Vector3 localPalmCenter;    // Wrist -> palm center in hand bone local space
            public float armLength;            // Measured upperArm + forearm length (shoulder -> wrist)
            public bool valid;
        }

        struct HandState
        {
            public float blendT;               // Linear 0..1 progress fed into wallHandBlendCurve
            public float weight;               // Evaluated curve weight 0..1
            public Vector3 smoothedPalmPos;
            public Vector3 posVelocity;        // SmoothDamp velocity
            public Vector3 smoothedNormal;
            public bool initialized;
        }

        HandCalibration leftHandCalib;
        HandCalibration rightHandCalib;
        HandState leftHandState;
        HandState rightHandState;

        public float PelvisOffset => pelvisOffset;

        void Awake()
        {
            animator = GetComponent<Animator>();
            tpc = GetComponent<ThirdPersonController>();
            childRigs = GetComponentsInChildren<Rig>(true);
            childConstraints = GetComponentsInChildren<IRigConstraint>(true);
            DisableLegacyConstraints();
        }

        void OnEnable()
        {
            DisableLegacyConstraints();
        }

        void Start()
        {
            DisableLegacyConstraints();
            leftHandCalib  = CalibrateHand(true);
            rightHandCalib = CalibrateHand(false);
        }

        void LateUpdate()
        {
            DisableLegacyConstraints();
        }

        void DisableLegacyConstraints()
        {
            if (leftFootIK) leftFootIK.weight = 0f;
            if (rightFootIK) rightFootIK.weight = 0f;
            if (leftHandIK) leftHandIK.weight = 0f;
            if (rightHandIK) rightHandIK.weight = 0f;
            if (hipsConstraint) hipsConstraint.weight = 0f;

            // Also zero out ANY Animation Rigging Rig / constraint on children automatically,
            // so even if the Inspector slots above are unassigned (None), Target_Hips / TwoBoneIK
            // can never pull or collapse the skeleton.
            if (childRigs != null)
            {
                for (int i = 0; i < childRigs.Length; i++)
                    if (childRigs[i]) childRigs[i].weight = 0f;
            }
            if (childConstraints != null)
            {
                for (int i = 0; i < childConstraints.Length; i++)
                    if (childConstraints[i] != null) childConstraints[i].weight = 0f;
            }
        }

        /// <summary>
        /// Automatically detects the hand bone's local finger axis and palm normal from the child finger/thumb bones.
        /// Works even when left and right hands have mirrored local bone axes (as in Mixamo / StarterAssets rigs).
        /// </summary>
        HandCalibration CalibrateHand(bool isLeft)
        {
            HandCalibration cal = default;
            if (!animator) return cal;

            Transform upperArm = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lowerArm = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (!middle) middle = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform thumb = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);

            if (!hand) return cal;

            float measuredArm = 0.52f;
            if (upperArm && lowerArm)
            {
                float upperLen = Vector3.Distance(upperArm.position, lowerArm.position);
                float lowerLen = Vector3.Distance(lowerArm.position, hand.position);
                if (upperLen + lowerLen > 0.1f) measuredArm = upperLen + lowerLen;
            }
            cal.armLength = measuredArm;

            Vector3 fingerDir = (middle && middle.localPosition.sqrMagnitude > 0.00001f)
                ? middle.localPosition.normalized
                : (isLeft ? Vector3.up : Vector3.down);

            Vector3 thumbDir = (thumb && thumb.localPosition.sqrMagnitude > 0.00001f)
                ? thumb.localPosition.normalized
                : (isLeft ? Vector3.forward : Vector3.back);

            // In Unity's left-handed coordinate system:
            //  - Left hand:  Cross(thumbDir, fingerDir) points out of the PALM
            //  - Right hand: Cross(fingerDir, thumbDir) points out of the PALM
            Vector3 palmNormal = (isLeft
                ? Vector3.Cross(thumbDir, fingerDir)
                : Vector3.Cross(fingerDir, thumbDir)).normalized;

            if (palmNormal.sqrMagnitude < 0.001f)
                palmNormal = isLeft ? Vector3.left : Vector3.right;

            fingerDir = Vector3.ProjectOnPlane(fingerDir, palmNormal).normalized;
            if (fingerDir.sqrMagnitude < 0.001f)
                fingerDir = isLeft ? Vector3.up : Vector3.down;

            cal.localBasis = Quaternion.LookRotation(palmNormal, fingerDir);
            cal.localPalmCenter = middle ? middle.localPosition * 0.55f : fingerDir * 0.045f;
            cal.valid = true;
            return cal;
        }

        // ═══════════════════════════════════════════════════
        //  UNIFIED HUMANOID IK PASS (Feet + Pelvis + Hands)
        // ═══════════════════════════════════════════════════

        void OnAnimatorIK(int layerIndex)
        {
            if (!animator || layerIndex != 0) return;

            float dt = Time.deltaTime;
            bool isGrounded = tpc == null || tpc.Grounded;
            bool isVaulting = tpc != null && tpc.IsVaulting;
            bool footActive = enableFootIK && isGrounded && !isVaulting;

            float footDur = footActive ? footBlendInDuration : footBlendOutDuration;
            footBlendT = Mathf.MoveTowards(footBlendT, footActive ? 1f : 0f, dt / Mathf.Max(0.01f, footDur));
            ikWeight = footIKBlendCurve != null ? Mathf.Clamp01(footIKBlendCurve.Evaluate(footBlendT)) : footBlendT;

            // 1) Read the PURE animated foot goals (before we modify anything this frame)
            Vector3 animLeftPos  = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 animRightPos = animator.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion animLeftRot  = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
            Quaternion animRightRot = animator.GetIKRotation(AvatarIKGoal.RightFoot);

            float rootY = transform.position.y;

            // 2) Trace ground under each foot -> target offsets
            float leftTarget = 0f, rightTarget = 0f;
            Vector3 leftTargetNormal = Vector3.up, rightTargetNormal = Vector3.up;

            if (footActive)
            {
                if (TraceGround(animLeftPos, rootY, out float lY, out leftTargetNormal))
                    leftTarget = Mathf.Clamp(lY - rootY, -maxFootDrop, maxFootRaise);
                if (TraceGround(animRightPos, rootY, out float rY, out rightTargetNormal))
                    rightTarget = Mathf.Clamp(rY - rootY, -maxFootDrop, maxFootRaise);
            }

            // 3) Pelvis goes down to the LOWEST foot (never up)
            float pelvisTarget = 0f;
            if (footActive && enablePelvisOffset)
                pelvisTarget = Mathf.Clamp(Mathf.Min(leftTarget, rightTarget, 0f), -maxPelvisDropDistance, 0f);

            // 4) Smooth foot & pelvis offsets (frame-rate independent exponential smoothing)
            float footT   = 1f - Mathf.Exp(-footInterpSpeed * dt);
            float pelvisT = 1f - Mathf.Exp(-pelvisAdjustSpeed * dt);

            leftFootOffset  = Mathf.Lerp(leftFootOffset,  leftTarget,  footT);
            rightFootOffset = Mathf.Lerp(rightFootOffset, rightTarget, footT);
            pelvisOffset    = Mathf.Lerp(pelvisOffset,    pelvisTarget, pelvisT);
            leftNormal  = Vector3.Slerp(leftNormal,  leftTargetNormal,  footT);
            rightNormal = Vector3.Slerp(rightNormal, rightTargetNormal, footT);

            // 5) Apply pelvis (bodyPosition here is this frame's pure animated value -> no accumulation)
            if (enablePelvisOffset)
                animator.bodyPosition += Vector3.up * pelvisOffset;

            // 6) Apply feet: animated goal + ground offset. IK solver bends the knees.
            ApplyFoot(AvatarIKGoal.LeftFoot,  animLeftPos,  animLeftRot,  leftFootOffset,  leftNormal);
            ApplyFoot(AvatarIKGoal.RightFoot, animRightPos, animRightRot, rightFootOffset, rightNormal);

            // 7) Solve & apply hands (wall touch when grounded, or obstacle-top hand plant when vaulting)
            if (enableHandIK)
            {
                SolveAndApplyHand(true,  AvatarIKGoal.LeftHand,  AvatarIKHint.LeftElbow,  leftHandCalib,  ref leftHandState,  leftHandTarget,  dt, isGrounded, isVaulting);
                SolveAndApplyHand(false, AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, rightHandCalib, ref rightHandState, rightHandTarget, dt, isGrounded, isVaulting);
                leftHandWeight  = leftHandState.weight;
                rightHandWeight = rightHandState.weight;
            }
            else
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                leftHandWeight = rightHandWeight = 0f;
            }
        }

        // ═══════════════════════════════════════════════════
        //  FOOT HELPERS
        // ═══════════════════════════════════════════════════

        void ApplyFoot(AvatarIKGoal goal, Vector3 animPos, Quaternion animRot, float offset, Vector3 normal)
        {
            animator.SetIKPositionWeight(goal, ikWeight);
            animator.SetIKPosition(goal, animPos + Vector3.up * (offset + footSoleOffset * ikWeight));

            if (alignFeetToSlope)
            {
                animator.SetIKRotationWeight(goal, ikWeight);
                animator.SetIKRotation(goal, ClampedTilt(normal) * animRot);
            }
            else
            {
                animator.SetIKRotationWeight(goal, 0f);
            }
        }

        Quaternion ClampedTilt(Vector3 normal)
        {
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, normal);
            float angle = Vector3.Angle(Vector3.up, normal);
            if (angle > maxFootAngle && angle > 0.001f)
                tilt = Quaternion.Slerp(Quaternion.identity, tilt, maxFootAngle / angle);
            return tilt;
        }

        bool TraceGround(Vector3 footPos, float rootY, out float groundY, out Vector3 normal)
        {
            groundY = rootY;
            normal = Vector3.up;

            Vector3 origin = new Vector3(footPos.x, rootY + traceStartHeight, footPos.z);
            float length = traceStartHeight + traceBelowDistance;
            bool didHit = false;

            if (currentCastType == CastType.Raycast)
            {
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, length, groundLayerMask, QueryTriggerInteraction.Ignore))
                {
                    groundY = hit.point.y;
                    normal = hit.normal;
                    didHit = true;
                }
            }
            else
            {
                if (Physics.SphereCast(origin, spherecastRadius, Vector3.down, out RaycastHit hit, length, groundLayerMask, QueryTriggerInteraction.Ignore))
                {
                    groundY = origin.y - hit.distance - spherecastRadius;
                    normal = Physics.Raycast(origin, Vector3.down, out RaycastHit rayHit, length + spherecastRadius, groundLayerMask, QueryTriggerInteraction.Ignore)
                        ? rayHit.normal : Vector3.up;
                    didHit = true;
                }
            }

            if (debugFootIK)
            {
                Vector3 end = new Vector3(origin.x, didHit ? groundY : origin.y - length, origin.z);
                Debug.DrawLine(origin, end, didHit ? Color.green : Color.red);
                if (didHit) Debug.DrawRay(end, normal * 0.3f, Color.cyan);
            }

            return didHit;
        }

        // ═══════════════════════════════════════════════════
        //  HAND IK — Wall Touch + Vault Hand Plant
        // ═══════════════════════════════════════════════════

        void SolveAndApplyHand(
            bool isLeft,
            AvatarIKGoal goal,
            AvatarIKHint elbowHint,
            HandCalibration calib,
            ref HandState state,
            Transform debugTarget,
            float dt,
            bool isGrounded,
            bool isVaulting)
        {
            Transform shoulder = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform handBone = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!shoulder || !handBone || !calib.valid)
            {
                animator.SetIKPositionWeight(goal, 0f);
                animator.SetIKRotationWeight(goal, 0f);
                animator.SetIKHintPositionWeight(elbowHint, 0f);
                return;
            }

            // Pure animated IK goal & bone transforms for this frame
            Vector3 animGoalPos = animator.GetIKPosition(goal);
            Quaternion animGoalRot = animator.GetIKRotation(goal);
            Quaternion boneToGoalRot = Quaternion.Inverse(handBone.rotation) * animGoalRot;
            Vector3 localGoalOffset = handBone.InverseTransformPoint(animGoalPos);

            Vector3 sideDir = isLeft ? -transform.right : transform.right;
            bool foundSurface = false;
            Vector3 targetPalmPos = Vector3.zero;
            Vector3 targetSurfaceNormal = -sideDir;
            bool isVaultPlant = false;

            // Mode A: Vault Hand Plant — weight is driven directly by vaultHandWeightCurve over VaultNormalizedTime (0..1)
            if (isVaulting && enableVaultHandIK && isLeft && tpc != null)
            {
                float vt = tpc.VaultNormalizedTime;
                float curveW = vaultHandWeightCurve != null ? Mathf.Clamp01(vaultHandWeightCurve.Evaluate(vt)) : 0f;
                isVaultPlant = true;
                foundSurface = curveW > 0.001f;
                state.weight = curveW;
                state.blendT = curveW;

                if (foundSurface)
                {
                    targetSurfaceNormal = tpc.VaultTopNormal;
                    targetPalmPos = tpc.VaultTopPoint
                                  + tpc.VaultDirection * 0.12f
                                  + sideDir * 0.14f
                                  + targetSurfaceNormal * vaultHandHeightOffset;
                }
            }
            // Mode B: Wall Touch — blendT advances over handBlendInDuration / handBlendOutDuration, shaped by wallHandBlendCurve
            else
            {
                bool isSprinting = tpc != null && tpc.IsSprinting;
                bool allowWallTouch = isGrounded && !isVaulting && !(disableWallHandWhileSprinting && isSprinting);
                if (allowWallTouch)
                    foundSurface = TryFindWallTouchPoint(shoulder.position, sideDir, out targetPalmPos, out targetSurfaceNormal);

                float dur = foundSurface ? handBlendInDuration : handBlendOutDuration;
                state.blendT = Mathf.MoveTowards(state.blendT, foundSurface ? 1f : 0f, dt / Mathf.Max(0.01f, dur));
                state.weight = wallHandBlendCurve != null ? Mathf.Clamp01(wallHandBlendCurve.Evaluate(state.blendT)) : state.blendT;
            }

            if (foundSurface)
            {
                if (!state.initialized || state.weight <= 0.02f)
                {
                    state.smoothedPalmPos = targetPalmPos;
                    state.posVelocity = Vector3.zero;
                    state.smoothedNormal = targetSurfaceNormal;
                    state.initialized = true;
                }
                else
                {
                    state.smoothedPalmPos = Vector3.SmoothDamp(
                        state.smoothedPalmPos,
                        targetPalmPos,
                        ref state.posVelocity,
                        isVaultPlant ? handPositionSmoothTime * 0.5f : handPositionSmoothTime
                    );
                    float normT = 1f - Mathf.Exp(-handNormalTrackSpeed * dt);
                    state.smoothedNormal = Vector3.Slerp(state.smoothedNormal, targetSurfaceNormal, normT);
                }
            }

            if (state.weight <= 0.001f)
            {
                animator.SetIKPositionWeight(goal, 0f);
                animator.SetIKRotationWeight(goal, 0f);
                animator.SetIKHintPositionWeight(elbowHint, 0f);
                if (debugTarget)
                {
                    debugTarget.position = handBone.position;
                    debugTarget.rotation = handBone.rotation;
                }
                return;
            }

            // Compute desired hand bone rotation so:
            //   - the PALM faces directly into the surface (-smoothedNormal)
            //   - the FINGERS point up along the wall (or forward on a horizontal vault top)
            Vector3 worldPalmDir = -state.smoothedNormal.normalized;
            Vector3 referenceUp = isVaultPlant ? transform.forward : Vector3.up;
            Vector3 worldFingerDir = Vector3.ProjectOnPlane(referenceUp, worldPalmDir).normalized;
            if (worldFingerDir.sqrMagnitude < 0.001f)
                worldFingerDir = transform.up;

            if (!isVaultPlant && handFingerTiltAngle > 0.01f)
            {
                // Slight outward tilt around the wall normal so the wrist looks relaxed
                float tiltSign = isLeft ? -1f : 1f;
                worldFingerDir = Quaternion.AngleAxis(tiltSign * handFingerTiltAngle, state.smoothedNormal) * worldFingerDir;
            }

            Quaternion worldHandBasis = Quaternion.LookRotation(worldPalmDir, worldFingerDir);
            Quaternion desiredBoneRot = worldHandBasis * Quaternion.Inverse(calib.localBasis);

            // Place the PALM CENTER on smoothedPalmPos, then convert back to wrist & AvatarIKGoal space
            Vector3 desiredWristPos = state.smoothedPalmPos - (desiredBoneRot * calib.localPalmCenter);
            Vector3 desiredGoalPos  = desiredWristPos + (desiredBoneRot * localGoalOffset);
            Quaternion desiredGoalRot = desiredBoneRot * boneToGoalRot;

            // Soft-clamp reach to a fraction of the measured arm length so the elbow NEVER locks rigidly straight.
            // Also fade IK weight out when the surface is beyond comfortable reach so the arm lets go naturally.
            Vector3 shoulderToGoal = desiredGoalPos - shoulder.position;
            float dist = shoulderToGoal.magnitude;
            float maxExt = isVaultPlant ? Mathf.Min(maxArmExtension + 0.03f, 0.94f) : maxArmExtension;
            float maxReach = calib.armLength * maxExt;
            float softStart = calib.armLength * (maxExt - 0.12f);

            if (dist > softStart && dist > 0.0001f)
            {
                // Asymptotic soft compression: smoothly eases into maxReach instead of hitting a hard wall
                float span = Mathf.Max(0.01f, maxReach - softStart);
                float excess = dist - softStart;
                float compressedDist = softStart + span * (1f - Mathf.Exp(-excess / span));
                desiredGoalPos = shoulder.position + (shoulderToGoal / dist) * compressedDist;
            }

            // Fade weight smoothly to 0 if the true surface is out of reach (prevents rigid mid-air stretching)
            float reachFadeStart = calib.armLength * 0.95f;
            float reachFadeEnd   = calib.armLength * 1.15f;
            float reachFactor = 1f - Mathf.InverseLerp(reachFadeStart, reachFadeEnd, dist);
            float effectiveWeight = state.weight * reachFactor;

            // Apply to Animator Humanoid IK
            animator.SetIKPositionWeight(goal, effectiveWeight);
            animator.SetIKRotationWeight(goal, effectiveWeight * 0.9f);
            animator.SetIKPosition(goal, desiredGoalPos);
            animator.SetIKRotation(goal, desiredGoalRot);

            // Natural elbow hint (down, slightly out, and behind the shoulder-to-hand line)
            Vector3 elbowHintPos = shoulder.position
                                 + sideDir * 0.24f
                                 - transform.up * 0.30f
                                 - transform.forward * 0.14f;
            animator.SetIKHintPositionWeight(elbowHint, effectiveWeight * 0.85f);
            animator.SetIKHintPosition(elbowHint, elbowHintPos);

            if (debugTarget)
            {
                debugTarget.position = desiredWristPos;
                debugTarget.rotation = desiredBoneRot;
            }
        }

        bool TryFindWallTouchPoint(Vector3 shoulderPos, Vector3 sideDir, out Vector3 palmPoint, out Vector3 wallNormal)
        {
            palmPoint = Vector3.zero;
            wallNormal = -sideDir;

            // Start at comfortable chest/lower-shoulder height, slightly forward of the shoulder joint
            Vector3 rayOrigin = shoulderPos
                              - transform.up * handHeightBelowShoulder
                              + transform.forward * 0.06f;

            // Cast a 2-ray fan: slightly forward-outward (catches angled walls) and pure side-outward
            Vector3 dirPrimary   = (sideDir + transform.forward * 0.45f).normalized;
            Vector3 dirSecondary = (sideDir + transform.forward * 0.12f).normalized;

            RaycastHit bestHit = default;
            bool found = false;

            if (Physics.SphereCast(rayOrigin, 0.04f, dirPrimary, out RaycastHit hit1, wallTouchDistance, wallLayerMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit1.normal.y) < 0.35f) // Only vertical-ish walls, never ramps/floors
            {
                bestHit = hit1;
                found = true;
            }

            if (Physics.SphereCast(rayOrigin, 0.04f, dirSecondary, out RaycastHit hit2, wallTouchDistance, wallLayerMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit2.normal.y) < 0.35f)
            {
                if (!found || hit2.distance < bestHit.distance)
                {
                    bestHit = hit2;
                    found = true;
                }
            }

            if (debugHandIK)
            {
                Debug.DrawRay(rayOrigin, dirPrimary * wallTouchDistance, found ? Color.green : Color.yellow);
                Debug.DrawRay(rayOrigin, dirSecondary * wallTouchDistance, found ? Color.green : Color.yellow);
            }

            if (!found) return false;

            // Ignore low obstacles whose top is right at or below the touch point (those are vaultable/step blocks, not walls to lean on)
            Vector3 wallTopCheck = bestHit.point - bestHit.normal * 0.06f + Vector3.up * 0.25f;
            if (!Physics.Raycast(wallTopCheck, Vector3.down, 0.2f, wallLayerMask, QueryTriggerInteraction.Ignore)
                && !Physics.CheckSphere(bestHit.point + Vector3.up * 0.18f, 0.08f, wallLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            wallNormal = bestHit.normal;

            // Project shoulder onto the wall plane, then shift slightly forward along the wall
            // so the hand rests naturally ahead of the shoulder (giving the elbow room to bend).
            Vector3 wallTangentForward = Vector3.ProjectOnPlane(transform.forward, wallNormal);
            if (wallTangentForward.sqrMagnitude > 0.001f)
                wallTangentForward.Normalize();

            Vector3 projectedOnWall = bestHit.point + wallTangentForward * (handForwardOffset * 0.5f);
            projectedOnWall.y = shoulderPos.y - handHeightBelowShoulder;

            // Re-verify exact surface point at the shifted forward position so the hand follows corners cleanly
            Vector3 verifyOrigin = projectedOnWall + wallNormal * 0.25f;
            if (Physics.Raycast(verifyOrigin, -wallNormal, out RaycastHit refinedHit, 0.45f, wallLayerMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(refinedHit.normal.y) < 0.35f)
            {
                wallNormal = refinedHit.normal;
                palmPoint = refinedHit.point + wallNormal * handSurfaceOffset;
            }
            else
            {
                palmPoint = bestHit.point + wallNormal * handSurfaceOffset;
            }

            if (debugHandIK)
            {
                Debug.DrawLine(shoulderPos, palmPoint, Color.magenta);
                Debug.DrawRay(palmPoint, wallNormal * 0.25f, Color.cyan);
            }

            return true;
        }
    }
}
