using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using static BSS.PoseBlender.AnimationPoseDataSO;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BSS.PoseBlender
{
    public class PoseRecorderLite : MonoBehaviour
    {
        [Tooltip("Reference to the Animator component.")]
        public Animator animator;

        [Tooltip("Root transform to record from.")]
        public Transform recordingRoot;

        [Tooltip("The ScriptableObject asset that will hold the recorded pose data.")]
        public AnimationPoseDataSO poseDataAsset;

        [Tooltip("Optional: Controller to swap in for posing.")]
        public RuntimeAnimatorController controllerForPosing;
        public AnimatorOverrideController overrideControllerForPosing;

        [Tooltip("The AnimationClip to record.")]
        public AnimationClip clip;

        [Tooltip("Has Initialize() already been run?")]
        [SerializeField] private bool Initialized = false;

        // Internal references we disable/restore
        [SerializeField] private PoseBlenderLite poseBlenderLite;
        private IKController ikController;

        private RuntimeAnimatorController _cachedController;

        /// <summary>
        /// Auto‐assigns animator & recordingRoot from a PoseBlenderLite on the same GameObject.
        /// </summary>
        public void Initialize()
        {
            if (!TryGetComponent<PoseBlenderLite>(out poseBlenderLite))
            {
                Debug.LogError("[PoseRecorder] No PoseBlenderLite found on this GameObject!");
                return;
            }

            // grab its references if ours are empty
            if (animator == null && poseBlenderLite.animator != null)
                animator = poseBlenderLite.animator;

            if(_cachedController == null)
                _cachedController = poseBlenderLite.animator.runtimeAnimatorController;

            if (recordingRoot == null && poseBlenderLite.animationRoot != null)
                recordingRoot = poseBlenderLite.animationRoot;

            // mark initialized only if both are present
            Initialized = (animator != null && recordingRoot != null);
            if (!Initialized)
                Debug.LogWarning("[PoseRecorder] Initialize did not find both animator and recordingRoot.");
        }

        /// <summary>
        /// Clears out the animator & root references so you can start over.
        /// </summary>
        public void Reset()
        {
            animator = null;
            recordingRoot = null;
            Initialized = false;
        }

        /// <summary>
        /// Captures only the first frame (t = 0) of the assigned clip
        /// and writes it into poseDataAsset.boneTransforms.
        /// </summary>
        [ContextMenu("Record First Frame Pose")]
        public void RecordFirstFramePose()
        {
            if (!Initialized)
            {
                Debug.LogError("[PoseRecorder] Please run Initialize() first (or click Auto Setup).");
                return;
            }
            if (clip == null || poseDataAsset == null)
            {
                Debug.LogError("[PoseRecorder] Missing Clip or PoseDataAsset!");
                return;
            }

            if (TryGetComponent<IKController>(out ikController))
                ikController.enabled = false;
            poseBlenderLite.enabled = false;

            // Swap in the "Empty" → clip override
            SetupPoseEditor();

            // Sample t=0
            animator.Play("Empty", 0, 0f);
            animator.Update(0f);

            // Build the single‐frame list
            poseDataAsset.boneTransforms.Clear();
            foreach (var bone in recordingRoot.GetComponentsInChildren<Transform>())
            {
                var data = new BonePoseData
                {
                    boneName = bone.name,
                    bonePath = GetRelativePath(recordingRoot, bone),
                    localRotation = (bone == recordingRoot)
                                    ? bone.rotation
                                    : Quaternion.Inverse(recordingRoot.rotation) * bone.rotation
                };
                poseDataAsset.boneTransforms.Add(data);
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(poseDataAsset);
            AssetDatabase.SaveAssets();
#endif

            // Restore
            poseBlenderLite.enabled = true;
            if (ikController != null) ikController.enabled = true;

            animator.runtimeAnimatorController = _cachedController;

            Debug.Log($"[PoseRecorder] Captured first frame pose: {poseDataAsset.boneTransforms.Count} bones.");
        }

        /// <summary>
        /// Swaps in your override or base controller and replaces the
        /// "Empty" clip with the target clip if using an AnimatorOverrideController.
        /// </summary>
        public void SetupPoseEditor()
        {
            if (overrideControllerForPosing != null)
            {
                var newOverride = new AnimatorOverrideController(overrideControllerForPosing);
                var overridesList = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                newOverride.GetOverrides(overridesList);

                for (int i = 0; i < overridesList.Count; i++)
                {
                    if (overridesList[i].Key.name == "Empty")
                        overridesList[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                            overridesList[i].Key,
                            clip
                        );
                }
                newOverride.ApplyOverrides(overridesList);
                animator.runtimeAnimatorController = newOverride;
            }
            else if (controllerForPosing != null)
            {
                animator.runtimeAnimatorController = controllerForPosing;
            }
            else
            {
                Debug.LogWarning("[PoseRecorder] No controller assigned for posing; using existing controller.");
            }
        }

        /// <summary>
        /// Builds the relative path from root to target (e.g. "Hips/Spine/Chest").
        /// </summary>
        private string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
                return "";
            var path = target.name;
            var current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }
}
