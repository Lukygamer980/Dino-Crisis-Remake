#if UNITY_EDITOR
    using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace BSS.PoseBlender
{
    public class PoseRecorderLiteWindow : EditorWindow
    {
        private PoseBlenderLite poseBlenderLite;
        private Animator animator;
        private Transform recordingRoot;
        private AnimationPoseDataSO poseDataAsset;
        private RuntimeAnimatorController controllerForPosing;
        private AnimatorOverrideController overrideControllerForPosing;
        private AnimationClip clip;

        private const string POSEANIMATIONCONTROLLER = "PBL_Pose_Animation_Controller";
        private const string POSEOVERRIDEANIMATIONCONTROLLER = "PBL_Pose_Animation_OverrideController";

        [MenuItem("Tools/Pose Blender Lite/Pose Recorder Lite")]
        public static void ShowWindow()
        {
            var window = GetWindow<PoseRecorderLiteWindow>("Pose Recorder Lite");
            window.minSize = new Vector2(400, 350);
        }

        private void OnEnable()
        {
            AutoAssignControllers();
        }

        private void OnGUI()
        {
            // serializedObject = null; // ensure no leftover
            // Header
            GUIStyle header = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("Pose Recorder Lite", header);
            GUILayout.Space(10);

            // PoseBlenderLite picker
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            var newPbl = EditorGUILayout.ObjectField("Pose Blender Lite", poseBlenderLite, typeof(PoseBlenderLite), true) as PoseBlenderLite;
            if (newPbl != poseBlenderLite)
            {
                poseBlenderLite = newPbl;
                if (poseBlenderLite != null && poseBlenderLite.initialized)
                {
                    animator = poseBlenderLite.animator;
                    recordingRoot = poseBlenderLite.animationRoot;
                }
                else
                {
                    animator = null;
                    recordingRoot = null;
                }
            }

            if (poseBlenderLite == null || !poseBlenderLite.initialized)
            {
                EditorGUILayout.HelpBox("Please assign and initialize Pose Blender Lite first.", MessageType.Error);
                return;
            }
            GUILayout.Space(8);

            // Controllers group
            EditorGUILayout.LabelField("Controllers", EditorStyles.boldLabel);
            controllerForPosing = EditorGUILayout.ObjectField("Controller For Posing", controllerForPosing, typeof(RuntimeAnimatorController), false) as RuntimeAnimatorController;
            overrideControllerForPosing = EditorGUILayout.ObjectField("Override Controller", overrideControllerForPosing, typeof(AnimatorOverrideController), false) as AnimatorOverrideController;
            if (GUILayout.Button("Auto Assign Controllers", GUILayout.Height(24)))
            {
                AutoAssignControllers();
            }
            GUILayout.Space(10);

            // Input
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            clip = EditorGUILayout.ObjectField("Animation Clip", clip, typeof(AnimationClip), false) as AnimationClip;
            GUILayout.Space(6);

            // Output
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            poseDataAsset = EditorGUILayout.ObjectField("Pose Data Asset", poseDataAsset, typeof(AnimationPoseDataSO), false) as AnimationPoseDataSO;
            if (poseDataAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "No Pose Data Asset assigned. One will be created when you capture the pose.",
                    MessageType.Info
                );
            }
            GUILayout.Space(12);

            // Big Record button
            if (GUILayout.Button("Record First Frame Pose", GUILayout.Height(40)))
            {
                RecordFirstFrame();
            }
        }

        private void AutoAssignControllers()
        {
            var guids = AssetDatabase.FindAssets($"{POSEANIMATIONCONTROLLER} t:RuntimeAnimatorController");
            if (guids.Length > 0)
                controllerForPosing = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(guids[0]));

            guids = AssetDatabase.FindAssets($"{POSEOVERRIDEANIMATIONCONTROLLER} t:AnimatorOverrideController");
            if (guids.Length > 0)
                overrideControllerForPosing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void RecordFirstFrame()
        {
            // Ensure we have an asset
            if (poseDataAsset == null)
            {
#if UNITY_EDITOR
                poseDataAsset = ScriptableObject.CreateInstance<AnimationPoseDataSO>();
                string assetPath = EditorUtility.SaveFilePanelInProject(
                    "Save Pose Data", "NewAnimationPoseData", "asset", "Enter a file name for the pose data."
                );
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.CreateAsset(poseDataAsset, assetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                else
                {
                    Debug.LogWarning("PoseRecorderWindow: Pose Data Asset creation cancelled. Aborting.");
                    return;
                }
#else
                Debug.LogError("PoseRecorderWindow: No Pose Data Asset assigned at runtime!");
                return;
#endif
            }

            // Validate required fields
            if (animator == null || recordingRoot == null || clip == null)
            {
                Debug.LogError("PoseRecorderWindow: missing required fields! Ensure PoseBlenderLite, Animation Clip, and Pose Data Asset are set.");
                return;
            }

            // Use temporary recorder component
            var recorder = poseBlenderLite.GetComponent<PoseRecorderLite>();
            bool added = false;
            if (recorder == null)
            {
                recorder = poseBlenderLite.gameObject.AddComponent<PoseRecorderLite>();
                added = true;
            }

            // Assign and record
            recorder.animator = animator;
            recorder.recordingRoot = recordingRoot;
            recorder.controllerForPosing = controllerForPosing;
            recorder.overrideControllerForPosing = overrideControllerForPosing;
            recorder.poseDataAsset = poseDataAsset;
            recorder.clip = clip;
            recorder.Initialize();
            recorder.SetupPoseEditor();
            recorder.RecordFirstFramePose();

            // Cleanup
            if (added)
                DestroyImmediate(recorder);
        }
    }
}
#endif
