// RUNTIME INTERACTION RIG — CORRECTED VERSION

using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace StarterAssets
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(RigBuilder))]
    public class RuntimeInteractionRig : MonoBehaviour
    {
        [Header("Foot IK")]
        [SerializeField] LayerMask groundLayerMask = 1;
        [SerializeField] float footRaycastDistance = 1.5f;
        [SerializeField] float footHeightOffset = 0.12f;
        [SerializeField] float pelvicDropSpeed = 10f;
        public float leftFootWeight = 1f;
        public float rightFootWeight = 1f;

        [Header("Hand IK")]
        [SerializeField] LayerMask wallLayerMask = 1;
        [SerializeField] float wallTouchDistance = 0.6f;
        [SerializeField] float handReachSpeed = 10f;
        public float leftHandWeight;
        public float rightHandWeight;

        [Header("Targets")]
        [SerializeField] Transform leftFootTarget;
        [SerializeField] Transform rightFootTarget;
        [SerializeField] Transform leftHandTarget;
        [SerializeField] Transform rightHandTarget;
        [SerializeField] Transform hipsTarget;

        [Header("Constraints")]
        [SerializeField] TwoBoneIKConstraint leftFootIK;
        [SerializeField] TwoBoneIKConstraint rightFootIK;
        [SerializeField] TwoBoneIKConstraint leftHandIK;
        [SerializeField] TwoBoneIKConstraint rightHandIK;
        [SerializeField] MultiPositionConstraint hipsConstraint;

        Animator animator;
        Rig rig;
        ThirdPersonController tpc;

        // Per-foot raycast results (needed for pelvis calculation)
        float leftFootOffset;   // vertical offset: hitY - characterBaseY
        float rightFootOffset;
        bool  leftFootHit;
        bool  rightFootHit;

        // Smoothed pelvis drop (purely visual — does NOT move CharacterController)
        float pelvisOffset;
        float targetPelvisOffset;

        void Awake()
        {
            animator = GetComponent<Animator>();
            rig = GetComponentInChildren<Rig>();
            tpc = GetComponent<ThirdPersonController>();
        }

        void LateUpdate()
        {
            if (!animator) return;

            // Gate foot IK: only when grounded and not vaulting
            bool footIKActive = true;
            if (tpc != null)
                footIKActive = tpc.Grounded && !tpc.IsVaulting;

            HandleFeet(footIKActive);
            HandlePelvis(footIKActive);
            HandleHands();

            // Write weights
            if (leftFootIK)
            {
                leftFootIK.weight = leftFootWeight;
                // Ensure rotation weight is on so foot tilts to surface
                leftFootIK.data.targetRotationWeight = 1f;
            }
            if (rightFootIK)
            {
                rightFootIK.weight = rightFootWeight;
                rightFootIK.data.targetRotationWeight = 1f;
            }
            if (leftHandIK) leftHandIK.weight = leftHandWeight;
            if (rightHandIK) rightHandIK.weight = rightHandWeight;
        }

        void HandleFeet(bool active)
        {
            Transform leftBone  = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (!leftBone || !rightBone) return;

            float baseY = transform.position.y; // CharacterController base

            // Solve each foot
            SolveFoot(leftBone,  leftFootTarget,  ref leftFootWeight,
                      out leftFootHit,  out leftFootOffset,  baseY, active);
            SolveFoot(rightBone, rightFootTarget, ref rightFootWeight,
                      out rightFootHit, out rightFootOffset, baseY, active);

            // Calculate pelvis drop target from the two foot offsets.
            // The LOWEST foot drives the drop so the other leg can bend naturally.
            if (active && (leftFootHit || rightFootHit))
            {
                float drop = 0f;
                if (leftFootHit && rightFootHit)
                    drop = Mathf.Min(leftFootOffset, rightFootOffset);
                else if (leftFootHit)
                    drop = Mathf.Min(leftFootOffset, 0f);
                else
                    drop = Mathf.Min(rightFootOffset, 0f);

                // Clamp so pelvis doesn't drop more than half the ray distance
                targetPelvisOffset = Mathf.Clamp(drop, -footRaycastDistance * 0.35f, 0f);
            }
            else
            {
                targetPelvisOffset = 0f;
            }
        }

        void SolveFoot(Transform footBone, Transform target, ref float weight,
                       out bool didHit, out float vertOffset, float baseY, bool active)
        {
            didHit     = false;
            vertOffset = 0f;

            if (!target || !footBone)
            {
                weight = Mathf.MoveTowards(weight, 0f, Time.deltaTime * 8f);
                return;
            }

            // Reset target to animation pose first
            target.position = footBone.position;
            target.rotation = footBone.rotation;

            if (!active)
            {
                weight = Mathf.MoveTowards(weight, 0f, Time.deltaTime * 8f);
                return;
            }

            // Ray origin: foot's XZ at a stable height above character base
            Vector3 rayOrigin = new Vector3(
                footBone.position.x,
                baseY + footRaycastDistance * 0.7f,
                footBone.position.z
            );

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, footRaycastDistance, groundLayerMask))
            {
                didHit     = true;
                vertOffset = hit.point.y - baseY;

                // Foot position: on the surface + sole offset
                // Do NOT subtract pelvisOffset here — the pelvis drop + TwoBoneIK
                // chain handles leg bending automatically
                target.position = hit.point + Vector3.up * footHeightOffset;

                // Foot rotation: tilt the ANIMATED foot rotation by the surface slope
                // This preserves the foot's natural orientation from the animation
                // and only adds the delta tilt from the surface normal
                Quaternion surfaceTilt = Quaternion.FromToRotation(Vector3.up, hit.normal);
                target.rotation = surfaceTilt * footBone.rotation;

                weight = Mathf.MoveTowards(weight, 1f, Time.deltaTime * 8f);
            }
            else
            {
                weight = Mathf.MoveTowards(weight, 0f, Time.deltaTime * 8f);
            }
        }

        void HandleHands()
        {
            SolveHand(HumanBodyBones.LeftHand, leftHandTarget, ref leftHandWeight, -transform.right);
            SolveHand(HumanBodyBones.RightHand, rightHandTarget, ref rightHandWeight, transform.right);
        }

        void SolveHand(HumanBodyBones bone, Transform target, ref float weight, Vector3 dir)
        {
            Transform boneT = animator.GetBoneTransform(bone);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!boneT || !hips) return;

            // 🔹 reset target to animation pose each frame
            target.position = boneT.position;
            target.rotation = boneT.rotation;

            if (Physics.Raycast(hips.position, dir, out RaycastHit hit, wallTouchDistance, wallLayerMask))
            {
                target.position = Vector3.Lerp(
                    boneT.position,
                    hit.point + hit.normal * 0.05f,
                    0.9f
                );

                target.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);

                weight = Mathf.MoveTowards(weight, 1f, Time.deltaTime * 6f);
            }
            else
            {
                weight = Mathf.MoveTowards(weight, 0f, Time.deltaTime * 6f);
            }
        }

        void HandlePelvis(bool active)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!hipsTarget || !hips) return;

            if (!active)
            {
                pelvisOffset = 0f;
                targetPelvisOffset = 0f;
                hipsTarget.position = hips.position;
                return;
            }

            // Smooth the pelvis offset (pelvisOffset is negative or zero)
            pelvisOffset = Mathf.Lerp(pelvisOffset, targetPelvisOffset, Time.deltaTime * pelvicDropSpeed);

            // Apply: move hip target down by the drop amount
            hipsTarget.position = hips.position + Vector3.up * pelvisOffset;
        }
    }
}
