#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BSS.PoseBlender
{
    [CustomEditor(typeof(StabilizerLite))]
    public class CameraTargetStabilizerLiteEditor : Editor
    {
        private SerializedProperty poseEditorProp;
        private SerializedProperty headProp;
        private SerializedProperty spineProp;
        private SerializedProperty cameraHolderProp;
        private SerializedProperty restOffsetProp;

        private string explanationText =    "This component adjusts the camera's position based on the character's head and spine transforms. " +
                                            "It uses the Pose Editor Lite to calculate the necessary offsets, ensuring smooth transitions. " +
                                            "Modify the properties below to tune the stabilization behavior.";


        private void OnEnable()
        {
            poseEditorProp = serializedObject.FindProperty("poseBlender");
            headProp = serializedObject.FindProperty("head");
            spineProp = serializedObject.FindProperty("stabilizationTransform");
            cameraHolderProp = serializedObject.FindProperty("cameraHolder");
            restOffsetProp = serializedObject.FindProperty("cameraHolderOffset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Use a custom header style similar to the one in PoseBlenderLiteEditor.
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.LabelField("Stabilizer Lite", headerStyle);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(explanationText, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();


            EditorGUILayout.PropertyField(poseEditorProp, new GUIContent("Pose Editor Lite"));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(spineProp, new GUIContent("Stabilization Transform"));
            EditorGUILayout.PropertyField(headProp, new GUIContent("Head Transform"));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(cameraHolderProp, new GUIContent("Camera Holder"));
            EditorGUILayout.PropertyField(restOffsetProp, new GUIContent("Holder Local Offset"));

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}