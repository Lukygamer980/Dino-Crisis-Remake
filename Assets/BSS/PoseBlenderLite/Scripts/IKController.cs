using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace BSS.PoseBlender
{
    /// <summary>
    /// An advanced IK controller that can handle multiple IK constraints for character hands and other limbs.
    /// All processing happens in LateUpdate using a custom TwoBoneIK solver.
    /// </summary>
    [ExecuteInEditMode]
    public class IKController : MonoBehaviour
    {
        [System.Serializable]
        public class IKConstraint
        {
            public string name = "New Constraint";
            public bool enabled = true;

            [Header("Limb Setup")]
            public TwoBoneIKSolver.HumanoidLimb limbType = TwoBoneIKSolver.HumanoidLimb.RightArm;

            [Header("Manual Bone Setup (Optional)")]
            [Tooltip("If set, will use these bones instead of the humanoid bones")]
            public bool useManualBones = false;
            public Transform rootBone;
            public Transform midBone;
            public Transform endBone;

            [Header("Targets")]
            public Transform targetPosition;
            [Tooltip("Position that controls the bend direction of the limb")]
            public Transform poleTarget;

            [Header("Weights")]
            [Range(0f, 1f)]
            public float positionWeight = 1.0f;
            [Range(0f, 1f)]
            public float rotationWeight = 1.0f;

            [Header("Options")]
            public bool maintainEndRotation = false;

            [HideInInspector]
            public Vector3 lastValidPosition;
            [HideInInspector]
            public Quaternion lastValidRotation;
        }

        [Header("Global Settings")]
        [Range(0f, 1f), Tooltip("Master weight that affects all constraints")]
        public float masterWeight = 1.0f;

        [Header("Animator Reference")]
        [Tooltip("Animator on the character (must be Humanoid for automatic bone setup)")]
        public Animator animator;

        [Header("IK Constraints")]
        [Tooltip("List of IK constraints to apply")]
        [Range(1, 100)]
        public int maxIkSolverIterations = 50;
        public List<IKConstraint> constraints = new List<IKConstraint>();

        private Coroutine masterBlendCoroutine;

        private void Reset()
        {
            // Add default constraints when component is first added
            animator = GetComponent<Animator>();

            if (constraints.Count == 0)
            {
                // Add default left and right hand constraints
                constraints.Add(new IKConstraint
                {
                    name = "Left Hand",
                    limbType = TwoBoneIKSolver.HumanoidLimb.LeftArm
                });

                constraints.Add(new IKConstraint
                {
                    name = "Right Hand",
                    limbType = TwoBoneIKSolver.HumanoidLimb.RightArm
                });
            }
        }

        /// <summary>
        /// Sets the overall master IK weight, clamped between 0 and 1.
        /// </summary>
        public void SetMasterWeight(float weight)
        {
            masterWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Sets both position and rotation weights for a constraint by name.
        /// </summary>
        public void SetLayerWeightByName(string layerName, float blendWeight)
        {
            float clampedBlend = Mathf.Clamp01(blendWeight);
            bool found = false;

            foreach (var constraint in constraints)
            {
                if (constraint.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase))
                {
                    constraint.positionWeight = clampedBlend;
                    constraint.rotationWeight = clampedBlend;
                    found = true;
                }
            }

            if (!found)
            {
                Debug.LogWarning("No IK constraint found with the name: " + layerName);
            }
        }

        /// <summary>
        /// Sets position and rotation weights independently for a constraint by name.
        /// </summary>
        public void SetLayerWeightByName(string layerName, float positionBlendWeight, float rotationBlendWeight)
        {
            float clampedPosBlend = Mathf.Clamp01(positionBlendWeight);
            float clampedRotBlend = Mathf.Clamp01(rotationBlendWeight);
            bool found = false;

            foreach (var constraint in constraints)
            {
                if (constraint.name.Equals(layerName, System.StringComparison.OrdinalIgnoreCase))
                {
                    constraint.positionWeight = clampedPosBlend;
                    constraint.rotationWeight = clampedRotBlend;
                    found = true;
                }
            }

            if (!found)
            {
                Debug.LogWarning("No IK constraint found with the name: " + layerName);
            }
        }

        private void Start()
        {
            // Initialize last valid positions/rotations
            foreach (var constraint in constraints)
            {
                if (constraint.targetPosition != null)
                {
                    constraint.lastValidPosition = constraint.targetPosition.position;
                    constraint.lastValidRotation = constraint.targetPosition.rotation;
                }
            }
        }

        private void LateUpdate()
        {
            ApplyTwoBoneIK();
        }

        private void ApplyTwoBoneIK()
        {
            if (animator == null) return;

            foreach (var constraint in constraints)
            {
                if (!constraint.enabled) continue;
                if (constraint.targetPosition == null) continue;

                // Update last valid position/rotation
                constraint.lastValidPosition = constraint.targetPosition.position;
                constraint.lastValidRotation = constraint.targetPosition.rotation;

                float effectiveWeight = masterWeight * constraint.positionWeight;
                if (effectiveWeight <= 0f) continue;

                bool hasPoleTarget = constraint.poleTarget != null;
                Vector3 polePosition = Vector3.zero;

                if (hasPoleTarget)
                {
                    polePosition = constraint.poleTarget.position;
                }
                else
                {
                    Transform rootBone = GetRootBone(constraint);
                    if (rootBone == null) continue;

                    Vector3 rootToTarget = constraint.targetPosition.position - rootBone.position;
                    Vector3 poleDir = Vector3.Cross(rootToTarget, Vector3.up).normalized;
                    if (poleDir.magnitude < 0.001f)
                        poleDir = Vector3.Cross(rootToTarget, Vector3.right).normalized;

                    polePosition = rootBone.position + poleDir * 0.5f;
                }

                if (constraint.useManualBones && constraint.rootBone != null &&
                    constraint.midBone != null && constraint.endBone != null)
                {
                    TwoBoneIKSolver.Solve(
                        constraint.rootBone,
                        constraint.midBone,
                        constraint.endBone,
                        constraint.targetPosition.position,
                        polePosition,
                        hasPoleTarget,
                        effectiveWeight,
                        constraint.maintainEndRotation,
                        maxIkSolverIterations
                    );
                }
                else
                {
                    TwoBoneIKSolver.SolveHumanoidLimb(
                        animator,
                        constraint.limbType,
                        constraint.targetPosition.position,
                        polePosition,
                        hasPoleTarget,
                        effectiveWeight,
                        constraint.maintainEndRotation,
                        maxIkSolverIterations
                    );
                }

                // Apply rotation if needed and not maintaining end rotation
                if (!constraint.maintainEndRotation && constraint.rotationWeight > 0f)
                {
                    Transform endBone = GetEndBone(constraint);
                    if (endBone != null)
                    {
                        float rotBlend = masterWeight * constraint.rotationWeight;
                        endBone.rotation = Quaternion.Slerp(
                            endBone.rotation,
                            constraint.targetPosition.rotation,
                            rotBlend
                        );
                    }
                }
            }
        }

        // Helper to get root bone from constraint
        private Transform GetRootBone(IKConstraint constraint)
        {
            if (constraint.useManualBones && constraint.rootBone != null)
                return constraint.rootBone;

            if (animator == null || !animator.isHuman)
                return null;

            switch (constraint.limbType)
            {
                case TwoBoneIKSolver.HumanoidLimb.LeftArm:
                    return animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                case TwoBoneIKSolver.HumanoidLimb.RightArm:
                    return animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                case TwoBoneIKSolver.HumanoidLimb.LeftLeg:
                    return animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                case TwoBoneIKSolver.HumanoidLimb.RightLeg:
                    return animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                default:
                    return null;
            }
        }

        // Helper to get end bone from constraint
        private Transform GetEndBone(IKConstraint constraint)
        {
            if (constraint.useManualBones && constraint.endBone != null)
                return constraint.endBone;

            if (animator == null || !animator.isHuman)
                return null;

            switch (constraint.limbType)
            {
                case TwoBoneIKSolver.HumanoidLimb.LeftArm:
                    return animator.GetBoneTransform(HumanBodyBones.LeftHand);
                case TwoBoneIKSolver.HumanoidLimb.RightArm:
                    return animator.GetBoneTransform(HumanBodyBones.RightHand);
                case TwoBoneIKSolver.HumanoidLimb.LeftLeg:
                    return animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                case TwoBoneIKSolver.HumanoidLimb.RightLeg:
                    return animator.GetBoneTransform(HumanBodyBones.RightFoot);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Smoothly blends the master IK weight over time.
        /// </summary>
        public void BlendMasterWeight(float targetWeight, float durationInSeconds)
        {
            if (masterBlendCoroutine != null)
            {
                StopCoroutine(masterBlendCoroutine);
            }

            masterBlendCoroutine = StartCoroutine(BlendMasterIkWeight(targetWeight, durationInSeconds));
        }

        private IEnumerator BlendMasterIkWeight(float targetWeight, float durationInSeconds)
        {
            float startWeight = masterWeight;
            float elapsed = 0f;

            if (Mathf.Approximately(durationInSeconds, 0f))
            {
                masterWeight = targetWeight;
                yield break;
            }

            while (elapsed < durationInSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / durationInSeconds);
                masterWeight = Mathf.Lerp(startWeight, targetWeight, t);
                yield return null;
            }

            masterWeight = targetWeight;
        }
    }
}
