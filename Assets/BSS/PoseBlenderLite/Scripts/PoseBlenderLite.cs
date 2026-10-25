using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace BSS.PoseBlender
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(IKController))]
    public class PoseBlenderLite : MonoBehaviour
    {
        /// <summary>
        /// The set of Transforms (bones) that the user has chosen in the Setup step.
        /// </summary>
        [Header("Look-Offset Bone Selection (Base Profile)")]
        public List<Transform> availableLookBones = new List<Transform>();

        [SerializeField, Range(0, 1f)] public float masterWeight = 1f;

        /// <summary>
        /// A “look profile” holds a name and one BoneOffset per bone in availableLookBones.
        /// </summary>
        [System.Serializable]
        public class LookConfig
        {
            public string configName = "New Profile";
            public List<BoneOffset> boneOffsets = new List<BoneOffset>();
        }

        [HideInInspector] public int activeLookConfigIndex = 0;

        [System.Serializable]
        public struct BoneRotationSettings
        {
            public string boneName;
            // The bone to rotate.
            public Transform bone;
            // Blend weight (0 to 1) for how strongly to apply the recorded rotation.
            [Range(0f, 1f)]
            public float blendWeight;
            // Per-bone rotation offset (applied during recorded pose processing).
            public Vector3 rotationOffset;
        }

        [System.Serializable]
        public class BoneChain
        {
            public string chainName;
            // Chain blend weight (0 to 1) that controls the overall blending for all bones in this chain.
            [Range(0f, 1f)]
            public float blendWeight = 1f;

            public List<BoneRotationSettings> bones = new List<BoneRotationSettings>();
        }

        [System.Serializable]
        public class AnimationOverride
        {
            public string animationName;
            [Range(0f, 1f)]
            public float blendWeight = 1f; // Overall influence of this overlay
            public AnimationPoseDataSO poseData;
            public List<BoneChain> boneChains = new List<BoneChain>();
        }

        [System.Serializable]
        public struct BoneOffset
        {
            public string boneName;
            public Transform bone;
            public Vector2 xMinMax; // Minimum and maximum rotation for the x-axis.
            public Vector2 yMinMax; // Minimum and maximum rotation for the y-axis.
            public Vector2 zMinMax; // Minimum and maximum rotation for the z-axis.

            // This field will store the computed rotation (for debugging or reference).
            public Vector3 currentRootSpaceRotation;
        }

        [Header("Preview")]
        [SerializeField] public bool previewInEditor = false;

        // The root transform used when recording the pose.
        [Header("Setup Character")]
        public Animator animator;
        public Transform animationRoot;

        [Header("Look Profiles")]
        public List<LookConfig> lookConfigs = new List<LookConfig>();

        [Tooltip("How long (seconds) it takes to blend between two look profiles.")]
        public float profileBlendDuration = 0.5f;

        public bool enableLookAtMode = false;
        public Transform lookAtTarget;
        [Range(0f, 1f)] public float lookAtBlendWeight = 1f;
        [Tooltip("Index of the bone in availableLookBones to use as the look-at origin point.")]
        public int lookAtOriginBoneIndex = -1;

        // Input offsets (expected between -90 and 90) coming from your player controller.
        [Header("Offsets")]
        [Range(-90, 90)] public float lookVerticalOffset = 0;
        [Range(-90, 90)] public float lookHorizontalOffset = 0;
        [Range(-90, 90)] public float leaningOffset = 0;

        [Header("Character Overlay Poses")]
        public List<AnimationOverride> overlayPoses = new List<AnimationOverride>();

        // Legacy fields kept only to migrate existing scenes (were 2 fixed overlay slots).
        [SerializeField, SerializeReference, HideInInspector] private AnimationOverride overlayPose1;
        [SerializeField, SerializeReference, HideInInspector] private AnimationOverride overlayPose2;

        [SerializeField, HideInInspector] private LookConfig currentLookConfig;
        [SerializeField, HideInInspector] public bool initialized = false;

        private Dictionary<string, Coroutine> activeBlendCoroutines = new Dictionary<string, Coroutine>();

        private Coroutine lookOffsetBlendCoroutine = null;

        private Dictionary<Transform, string> relativePathCache = new Dictionary<Transform, string>();

        /// <summary>
        /// Sets the look offsets for vertical, horizontal, and leaning angles, clamping each value to the range -90 to 90.
        /// </summary>
        public void SetLookOffsets(float vertical, float horizontal, float leaning)
        {
            lookVerticalOffset = Mathf.Clamp(vertical, -90f, 90f);
            lookHorizontalOffset = Mathf.Clamp(horizontal, -90f, 90f);
            leaningOffset = Mathf.Clamp(leaning, -90f, 90f);
        }

#if UNITY_EDITOR
        private float lastEditorTime = 0f;

        void OnEnable()
        {
            if (relativePathCache == null)
                relativePathCache = new Dictionary<Transform, string>();
            if (activeBlendCoroutines == null)
                activeBlendCoroutines = new Dictionary<string, Coroutine>();

            // Only subscribe in edit mode
            if (!Application.isPlaying)
            {
                lastEditorTime = (float)EditorApplication.timeSinceStartup;
                EditorApplication.update += EditorUpdate;
            }

            // Migrate any old overlayPose1/2 into the new overlayPoses list
            MigrateLegacyOverlaysIfNeeded();

            RebuildCurrentLookConfig();
            ClearRelativePathCache();
        }

        void OnDisable()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= EditorUpdate;
            }
        }

        private void MigrateLegacyOverlaysIfNeeded()
        {
            if (overlayPoses == null)
                overlayPoses = new List<AnimationOverride>();

            // If the list is already populated, don't touch it.
            if (overlayPoses.Count > 0) return;

            // If we have legacy overlays, move them into the list once.
            bool changed = false;
            if (overlayPose1 != null)
            {
                overlayPoses.Add(overlayPose1);
                changed = true;
            }
            if (overlayPose2 != null)
            {
                overlayPoses.Add(overlayPose2);
                changed = true;
            }

            if (changed)
            {
                overlayPose1 = null;
                overlayPose2 = null;
#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
#endif
            }
        }

        /// <summary>
        /// Called on each editor update tick when previewing in the editor.
        /// It updates the animator based on elapsed time, processes overlay poses and root rotations,
        /// and repaints the Scene view for immediate visual feedback.
        /// </summary>
        void EditorUpdate()
        {
            if (!previewInEditor)
                return;

            float currentTime = (float)EditorApplication.timeSinceStartup;
            float dt = currentTime - lastEditorTime;
            lastEditorTime = currentTime;

            if (animator)
                animator.Update(dt);

            ProcessOverlayPoses();
            ProcessRootRotation();
            SceneView.RepaintAll();
        }
#endif

        void Start()
        {
            if (Application.isPlaying && !initialized)
            {
                RebuildCurrentLookConfig();
                ClearRelativePathCache();
                initialized = true;
            }
        }

        // This LateUpdate will run during play mode
        void LateUpdate()
        {
            if (Application.isPlaying)
            {
                if (previewInEditor) previewInEditor = false;

                ProcessOverlayPoses();
                ProcessRootRotation();
            }
        }

        /// <summary>
        /// Apply all overlay pose layers from overlayPoses (unlimited).
        /// Single-threaded version of Pro's overlay logic.
        /// </summary>
        private void ProcessOverlayPoses()
        {
            if (animationRoot == null) return;

            Quaternion poseRootRotation = animationRoot.rotation;

            foreach (var overlay in overlayPoses)
            {
                if (overlay == null || overlay.poseData == null)
                    continue;

                var frame = overlay.poseData;

                foreach (var chain in overlay.boneChains)
                {
                    foreach (var boneSettings in chain.bones)
                    {
                        Transform bone = boneSettings.bone;
                        if (bone == null)
                            continue;

                        string relativePath = GetRelativePath(animationRoot, bone);
                        var boneData = frame.boneTransforms.Find(b => b.bonePath == relativePath);
                        if (boneData == null)
                            continue;

                        // Local recorded rotation + per-bone offset
                        Quaternion recordedLocalRotation = boneData.localRotation;
                        Quaternion perBoneOffset = Quaternion.Euler(boneSettings.rotationOffset);
                        recordedLocalRotation = perBoneOffset * recordedLocalRotation;

                        // Convert to global
                        Quaternion recordedGlobalRotation = poseRootRotation * recordedLocalRotation;

                        float effectiveBlend =
                            boneSettings.blendWeight *
                            chain.blendWeight *
                            overlay.blendWeight *
                            masterWeight;

                        bone.rotation = Quaternion.Slerp(bone.rotation, recordedGlobalRotation, effectiveBlend);
                    }
                }
            }
        }

        /// <summary>
        /// Maps an input value (expected in the range -90 to 90) to a target rotation value defined by the given min–max range.
        /// </summary>
        float MapInputToBoneRotation(float input, Vector2 limits)
        {
            if (input >= 0f)
            {
                float t = input / 90f; // Map [0, 90] to [0, 1]
                return Mathf.Lerp(0f, limits.y, t);
            }
            else
            {
                float t = (-input) / 90f; // Map [0, 90] to [0, 1] (input is negative)
                return Mathf.Lerp(0f, limits.x, t);
            }
        }

        /// <summary>
        /// Applies look-offset rotations to each bone based on currentLookConfig.
        /// </summary>
        private void ProcessRootRotation()
        {
            // If nothing to do, bail.
            if (currentLookConfig == null
                || currentLookConfig.boneOffsets == null
                || currentLookConfig.boneOffsets.Count == 0
                || animationRoot == null)
            {
                return;
            }

            if (enableLookAtMode && lookAtTarget != null)
            {
                Transform lookOrigin = animationRoot;
                
                if (availableLookBones != null && availableLookBones.Count > 0)
                {
                    int boneIndex = lookAtOriginBoneIndex;
                    if (boneIndex < 0 || boneIndex >= availableLookBones.Count)
                        boneIndex = availableLookBones.Count - 1;
                    
                    lookOrigin = availableLookBones[boneIndex];
                }

                Vector3 dir = lookAtTarget.position - lookOrigin.position;
                Vector3 localDir = animationRoot.InverseTransformDirection(dir.normalized);

                float targetHorizontal = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
                float targetVertical = Mathf.Asin(-localDir.y) * Mathf.Rad2Deg;

                lookVerticalOffset = Mathf.Lerp(0, targetVertical, lookAtBlendWeight);
                lookHorizontalOffset = Mathf.Lerp(0, targetHorizontal, lookAtBlendWeight);
            }

            float vertical = lookVerticalOffset;
            float horizontal = lookHorizontalOffset;
            float lean = leaningOffset;

            for (int i = 0; i < currentLookConfig.boneOffsets.Count; i++)
            {
                BoneOffset bo = currentLookConfig.boneOffsets[i];
                if (bo.bone == null)
                    continue;

                float mx = MapInputToBoneRotation(vertical, bo.xMinMax);
                float my = MapInputToBoneRotation(horizontal, bo.yMinMax);
                float mz = MapInputToBoneRotation(-lean, bo.zMinMax);

                Vector3 targetEuler = new Vector3(mx, my, mz) * masterWeight;

                bo.currentRootSpaceRotation = targetEuler;
                Quaternion offsetQ = animationRoot.rotation * Quaternion.Euler(targetEuler) * Quaternion.Inverse(animationRoot.rotation);

                Quaternion originalRotation = bo.bone.rotation;
                Quaternion targetRotation = offsetQ * originalRotation;
                bo.bone.rotation = Quaternion.Slerp(originalRotation, targetRotation, 1f);

                currentLookConfig.boneOffsets[i] = bo;
            }
        }

        /// <summary>
        /// Computes the relative path (hierarchical name) from the provided root transform to the target transform.
        /// Useful for matching recorded bone data with scene bones.
        /// </summary>
        private string GetRelativePath(Transform root, Transform target)
        {
            if (relativePathCache == null)
                relativePathCache = new Dictionary<Transform, string>();

            if (relativePathCache.TryGetValue(target, out string cachedPath))
                return cachedPath;

            if (target == root)
            {
                relativePathCache[target] = "";
                return "";
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            relativePathCache[target] = path;
            return path;
        }

        private void ClearRelativePathCache()
        {
            if (relativePathCache == null)
                relativePathCache = new Dictionary<Transform, string>();
            else
                relativePathCache.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  OVERLAY BLENDING API (UNLIMITED OVERLAYS)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Smoothly blends a single overlay (by animationName) to a target blend weight.
        /// </summary>
        public void BlendOverlay(string overlayName, float targetBlend, float duration)
        {
            if (string.IsNullOrEmpty(overlayName))
                return;

            if (activeBlendCoroutines.ContainsKey(overlayName) && activeBlendCoroutines[overlayName] != null)
            {
                StopCoroutine(activeBlendCoroutines[overlayName]);
            }
            activeBlendCoroutines[overlayName] = StartCoroutine(BlendOverlayPoseCoroutine(overlayName, targetBlend, duration));
        }

        private IEnumerator BlendOverlayPoseCoroutine(string overlayName, float targetBlend, float duration)
        {
            var targetOv = overlayPoses.Find(o => o != null && o.animationName == overlayName);
            if (targetOv == null)
            {
                Debug.LogWarning($"Overlay pose '{overlayName}' not found.");
                activeBlendCoroutines.Remove(overlayName);
                yield break;
            }

            float initialBlend = targetOv.blendWeight;
            float time = 0f;
            targetBlend = Mathf.Clamp01(targetBlend);

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);
                targetOv.blendWeight = Mathf.Lerp(initialBlend, targetBlend, t);
                yield return null;
            }

            targetOv.blendWeight = targetBlend;
            activeBlendCoroutines.Remove(overlayName);
        }

        /// <summary>
        /// Smoothly blends ALL overlays to a target blend weight.
        /// </summary>
        public void BlendAllOverlays(float targetBlend, float duration)
        {
            foreach (var kvp in new List<KeyValuePair<string, Coroutine>>(activeBlendCoroutines))
            {
                if (kvp.Value != null)
                    StopCoroutine(kvp.Value);
            }
            activeBlendCoroutines.Clear();

            StartCoroutine(BlendAllOverlaysCoroutine(targetBlend, duration));
        }

        private IEnumerator BlendAllOverlaysCoroutine(float targetBlendWeight, float duration)
        {
            if (overlayPoses == null || overlayPoses.Count == 0)
                yield break;

            Dictionary<AnimationOverride, float> initialBlends = new Dictionary<AnimationOverride, float>();
            foreach (var ov in overlayPoses)
            {
                if (ov == null) continue;
                initialBlends[ov] = ov.blendWeight;
            }

            float time = 0f;
            targetBlendWeight = Mathf.Clamp01(targetBlendWeight);

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);

                foreach (var ov in overlayPoses)
                {
                    if (ov == null) continue;
                    ov.blendWeight = Mathf.Lerp(initialBlends[ov], targetBlendWeight, t);
                }

                yield return null;
            }

            foreach (var ov in overlayPoses)
            {
                if (ov == null) continue;
                ov.blendWeight = targetBlendWeight;
            }
        }

        /// <summary>
        /// Directly sets the vertical look offset angle (up/down).
        /// </summary>
        public void SetVerticalOffset(float offset)
        {
            lookVerticalOffset = Mathf.Clamp(offset, -90f, 90f);
        }

        /// <summary>
        /// Directly sets the horizontal look offset angle (left/right).
        /// </summary>
        public void SetHorizontalOffset(float offset)
        {
            lookHorizontalOffset = Mathf.Clamp(offset, -90f, 90f);
        }

        /// <summary>
        /// Directly sets the leaning offset angle.
        /// </summary>
        public void SetLeaningOffset(float offset)
        {
            leaningOffset = Mathf.Clamp(offset, -90f, 90f);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  LOOK PROFILE MANAGEMENT (UNLIMITED PROFILES)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from the setup window after the bones are selected.
        /// Creates a first profile ("Profile 1") that distributes ranges over the available bones.
        /// </summary>
        public void InitializeBaseLookProfile()
        {
            if (availableLookBones == null || availableLookBones.Count == 0)
            {
                Debug.LogWarning("[PoseBlenderLite] No available look bones to initialize profile.");
                return;
            }

            lookConfigs = new List<LookConfig>();

            float perRange = 90f / Mathf.Max(1, availableLookBones.Count);
            LookConfig cfg = new LookConfig
            {
                configName = "Profile 1",
                boneOffsets = new List<BoneOffset>()
            };

            foreach (var bone in availableLookBones)
            {
                BoneOffset bo = new BoneOffset
                {
                    boneName = bone != null ? bone.name : "Bone",
                    bone = bone,
                    xMinMax = new Vector2(-perRange, perRange),
                    yMinMax = new Vector2(-perRange, perRange),
                    zMinMax = new Vector2(-perRange, perRange),
                    currentRootSpaceRotation = Vector3.zero
                };
                cfg.boneOffsets.Add(bo);
            }

            lookConfigs.Add(cfg);
            activeLookConfigIndex = 0;
            RebuildCurrentLookConfig();
        }

        /// <summary>
        /// Rebuilds currentLookConfig from the active entry in lookConfigs.
        /// </summary>
        public void RebuildCurrentLookConfig()
        {
            if (lookConfigs != null && lookConfigs.Count > 0 &&
                activeLookConfigIndex >= 0 && activeLookConfigIndex < lookConfigs.Count)
            {
                var saved = lookConfigs[activeLookConfigIndex];
                currentLookConfig = new LookConfig
                {
                    configName = saved.configName,
                    boneOffsets = new List<BoneOffset>()
                };
                foreach (var bo in saved.boneOffsets)
                {
                    currentLookConfig.boneOffsets.Add(new BoneOffset
                    {
                        boneName = bo.boneName,
                        bone = bo.bone,
                        xMinMax = bo.xMinMax,
                        yMinMax = bo.yMinMax,
                        zMinMax = bo.zMinMax,
                        currentRootSpaceRotation = Vector3.zero
                    });
                }
            }
            else
            {
                currentLookConfig = null;
            }
        }

        /// <summary>
        /// Adds a new look profile using the currently selected availableLookBones.
        /// </summary>
        public void AddLookProfile(string newName = null)
        {
            if (availableLookBones == null || availableLookBones.Count == 0) return;

            var cfg = new LookConfig
            {
                configName = string.IsNullOrEmpty(newName) ? $"Profile {lookConfigs.Count + 1}" : newName,
                boneOffsets = new List<BoneOffset>()
            };

            float perRange = 90f / Mathf.Max(1, availableLookBones.Count);
            foreach (var b in availableLookBones)
            {
                cfg.boneOffsets.Add(new BoneOffset
                {
                    boneName = b != null ? b.name : "Bone",
                    bone = b,
                    xMinMax = new Vector2(-perRange, perRange),
                    yMinMax = new Vector2(-perRange, perRange),
                    zMinMax = new Vector2(-perRange, perRange),
                    currentRootSpaceRotation = Vector3.zero
                });
            }

            lookConfigs.Add(cfg);
        }

        /// <summary>
        /// Blends from the current look profile into the target profile index over duration.
        /// This is a single-threaded version of the Pro behaviour (no Jobs).
        /// </summary>
        public void BlendToProfile(int targetIndex, float duration)
        {
            if (lookConfigs == null || lookConfigs.Count == 0) return;
            if (targetIndex < 0 || targetIndex >= lookConfigs.Count) return;

            if (lookOffsetBlendCoroutine != null)
                StopCoroutine(lookOffsetBlendCoroutine);

            lookOffsetBlendCoroutine = StartCoroutine(BlendLookProfilesCoroutine(targetIndex, duration));
        }

        private IEnumerator BlendLookProfilesCoroutine(int targetIndex, float duration)
        {
            if (currentLookConfig == null || currentLookConfig.boneOffsets.Count == 0)
            {
                RebuildCurrentLookConfig();
                if (currentLookConfig == null) yield break;
            }

            LookConfig targetConfig = lookConfigs[targetIndex];
            if (targetConfig == null || targetConfig.boneOffsets.Count != currentLookConfig.boneOffsets.Count)
            {
                // Fallback – just snap.
                activeLookConfigIndex = targetIndex;
                RebuildCurrentLookConfig();
                yield break;
            }

            var fromOffsets = new List<BoneOffset>(currentLookConfig.boneOffsets);
            var toOffsets = targetConfig.boneOffsets;

            float time = 0f;
            duration = Mathf.Max(0.0001f, duration);

            while (time < duration)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / duration);

                for (int i = 0; i < currentLookConfig.boneOffsets.Count; i++)
                {
                    var from = fromOffsets[i];
                    var to = toOffsets[i];
                    BoneOffset blended = currentLookConfig.boneOffsets[i];

                    blended.xMinMax = Vector2.Lerp(from.xMinMax, to.xMinMax, t);
                    blended.yMinMax = Vector2.Lerp(from.yMinMax, to.yMinMax, t);
                    blended.zMinMax = Vector2.Lerp(from.zMinMax, to.zMinMax, t);

                    currentLookConfig.boneOffsets[i] = blended;
                }

                yield return null;
            }

            // At the end snap exactly to target settings and record that profile as active.
            for (int i = 0; i < currentLookConfig.boneOffsets.Count; i++)
            {
                var to = toOffsets[i];
                BoneOffset bo = currentLookConfig.boneOffsets[i];
                bo.xMinMax = to.xMinMax;
                bo.yMinMax = to.yMinMax;
                bo.zMinMax = to.zMinMax;
                currentLookConfig.boneOffsets[i] = bo;
            }

            activeLookConfigIndex = targetIndex;
        }
    }
}
