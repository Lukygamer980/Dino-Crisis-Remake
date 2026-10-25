#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace BSS.PoseBlender
{
    [CustomEditor(typeof(PoseBlenderLite))]
    public class PoseBlenderLiteEditor : Editor
    {
        // Serialized properties
        private SerializedProperty masterWeightProp;
        private SerializedProperty previewInEditorProp;
        private SerializedProperty lookConfigsProp;
        private SerializedProperty activeLookConfigIndexProp;
        private SerializedProperty overlayPosesProp;
        private SerializedProperty enableLookAtModeProp;
        private SerializedProperty lookAtTargetProp;
        private SerializedProperty lookAtBlendWeightProp;
        private SerializedProperty lookAtOriginBoneIndexProp;
        private SerializedProperty lookVerticalOffsetProp;
        private SerializedProperty lookHorizontalOffsetProp;
        private SerializedProperty leaningOffsetProp;
        private SerializedProperty initializedProp;
        private SerializedProperty profileBlendDurationProp;
        private SerializedProperty availableLookBonesProp;

        private enum Axis { X, Y, Z }

        // Editor-only toggle state per profile for Auto Distribution
        private List<bool> _autoDistributeStates = new List<bool>();

        void OnEnable()
        {
            if (target == null) return;

            masterWeightProp = serializedObject.FindProperty("masterWeight");
            previewInEditorProp = serializedObject.FindProperty("previewInEditor");
            lookConfigsProp = serializedObject.FindProperty("lookConfigs");
            activeLookConfigIndexProp = serializedObject.FindProperty("activeLookConfigIndex");
            overlayPosesProp = serializedObject.FindProperty("overlayPoses");
            enableLookAtModeProp = serializedObject.FindProperty("enableLookAtMode");
            lookAtTargetProp = serializedObject.FindProperty("lookAtTarget");
            lookAtBlendWeightProp = serializedObject.FindProperty("lookAtBlendWeight");
            lookAtOriginBoneIndexProp = serializedObject.FindProperty("lookAtOriginBoneIndex");
            lookVerticalOffsetProp = serializedObject.FindProperty("lookVerticalOffset");
            lookHorizontalOffsetProp = serializedObject.FindProperty("lookHorizontalOffset");
            leaningOffsetProp = serializedObject.FindProperty("leaningOffset");
            initializedProp = serializedObject.FindProperty("initialized");
            profileBlendDurationProp = serializedObject.FindProperty("profileBlendDuration");
            availableLookBonesProp = serializedObject.FindProperty("availableLookBones");

            ResizeAutoDistributeStates();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            PoseBlenderLite pbl = (PoseBlenderLite)target;

            EditorGUILayout.Space();
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.LowerCenter
            };
            EditorGUILayout.LabelField("Pose Blender Lite", headerStyle);
            EditorGUILayout.Space();

            bool isInitialized = initializedProp.boolValue;
            if (!isInitialized)
            {
                EditorGUILayout.HelpBox(
                    "Character not initialized. Click the button below to run setup.",
                    MessageType.Warning
                );
                EditorGUILayout.Space();

                if (GUILayout.Button("Open Setup Window", GUILayout.Height(30)))
                {
                    PoseBlenderLiteSetupWindow.ShowWindow();
                }

                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.Space();

            // Preview In Editor (only in edit mode)
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enable to see real-time pose blending in the Scene view.",
                    MessageType.Info
                );
                EditorGUILayout.PropertyField(previewInEditorProp, new GUIContent("Preview In Editor"));
                EditorGUILayout.Space();
            }

            // Master weight
            EditorGUILayout.LabelField("Master Weight", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(masterWeightProp, new GUIContent("Value"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);
            EditorGUILayout.Space();

            // Offsets
            EditorGUILayout.LabelField("Offsets", EditorStyles.boldLabel);
            if (enableLookAtModeProp.boolValue && lookAtTargetProp.objectReferenceValue != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Slider(lookVerticalOffsetProp, -90f, 90f, new GUIContent("Look Vertical"));
                EditorGUILayout.Slider(lookHorizontalOffsetProp, -90f, 90f, new GUIContent("Look Horizontal"));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Slider(leaningOffsetProp, -90f, 90f, new GUIContent("Leaning"));
            }
            else
            {
                EditorGUILayout.Slider(lookVerticalOffsetProp, -90f, 90f, new GUIContent("Look Vertical"));
                EditorGUILayout.Slider(lookHorizontalOffsetProp, -90f, 90f, new GUIContent("Look Horizontal"));
                EditorGUILayout.Slider(leaningOffsetProp, -90f, 90f, new GUIContent("Leaning"));
            }

            EditorGUILayout.Space();

            // Look-at mode
            EditorGUILayout.LabelField("Look-At Mode", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableLookAtModeProp, new GUIContent("Enable"));
            if (enableLookAtModeProp.boolValue)
            {
                EditorGUILayout.PropertyField(lookAtTargetProp, new GUIContent("Look At Target"));
                EditorGUILayout.PropertyField(lookAtBlendWeightProp, new GUIContent("Look At Blend Weight"));
                
                // Dropdown for selecting look-at origin bone
                if (availableLookBonesProp != null && availableLookBonesProp.arraySize > 0)
                {
                    string[] boneNames = new string[availableLookBonesProp.arraySize];
                    for (int i = 0; i < availableLookBonesProp.arraySize; i++)
                    {
                        var boneProp = availableLookBonesProp.GetArrayElementAtIndex(i);
                        Transform bone = boneProp.objectReferenceValue as Transform;
                        boneNames[i] = bone != null ? bone.name : $"Bone {i}";
                    }
                    
                    int currentIndex = lookAtOriginBoneIndexProp.intValue;
                    if (currentIndex < 0 || currentIndex >= availableLookBonesProp.arraySize)
                    {
                        currentIndex = availableLookBonesProp.arraySize - 1;
                        lookAtOriginBoneIndexProp.intValue = currentIndex;
                    }
                    
                    int newIndex = EditorGUILayout.Popup("Look Origin Bone", currentIndex, boneNames);
                    if (newIndex != currentIndex)
                    {
                        lookAtOriginBoneIndexProp.intValue = newIndex;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No look bones selected. Use 'Select Look Bones' to choose bones.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);

            // ─────────────────────────────────────────────────────────────
            // LOOK PROFILES (multi-profile like Pro)
            // ─────────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Look Profiles", EditorStyles.boldLabel);

            // Button to select which bones participate in look offset
            if (GUILayout.Button("Select Look Bones"))
            {
                LookBoneSelectorWindow.Open(pbl);
            }
            EditorGUILayout.Space();

            // Active Profile Dropdown & Blend Button
            if (lookConfigsProp.arraySize > 0)
            {
                string[] profileNames = new string[lookConfigsProp.arraySize];
                for (int i = 0; i < lookConfigsProp.arraySize; i++)
                {
                    var cfg = lookConfigsProp.GetArrayElementAtIndex(i);
                    profileNames[i] = cfg.FindPropertyRelative("configName").stringValue;
                }

                int currentIndex = activeLookConfigIndexProp.intValue;
                int newIndex = EditorGUILayout.Popup("Active Profile", currentIndex, profileNames);
                if (newIndex != currentIndex)
                {
                    activeLookConfigIndexProp.intValue = newIndex;
                    serializedObject.ApplyModifiedProperties();

                    pbl.RebuildCurrentLookConfig();

                    float duration = profileBlendDurationProp.floatValue;
                    pbl.BlendToProfile(newIndex, duration);

                    SceneView.RepaintAll();
                }
                EditorGUILayout.Space();
            }

            EditorGUILayout.PropertyField(profileBlendDurationProp, new GUIContent("Profile Blend Duration (sec)"));
            EditorGUILayout.Space();

            if (lookConfigsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No profiles found. Run the character setup or create a new profile.",
                    MessageType.Warning
                );
            }
            else
            {
                ResizeAutoDistributeStates();

                for (int i = 0; i < lookConfigsProp.arraySize; i++)
                {
                    var configProp = lookConfigsProp.GetArrayElementAtIndex(i);
                    var nameProp = configProp.FindPropertyRelative("configName");
                    var boneList = configProp.FindPropertyRelative("boneOffsets");

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    configProp.isExpanded = EditorGUILayout.Foldout(
                        configProp.isExpanded,
                        nameProp.stringValue,
                        true
                    );
                    if (configProp.isExpanded)
                    {
                        EditorGUILayout.Space();

                        // Profile name + delete (if not first)
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PropertyField(nameProp, new GUIContent("Profile Name"));
                        if (i != 0)
                        {
                            if (GUILayout.Button("Delete Profile", GUILayout.Width(120)))
                            {
                                Undo.RecordObject(pbl, "Delete Look Profile");
                                lookConfigsProp.DeleteArrayElementAtIndex(i);

                                int activeIdx = activeLookConfigIndexProp.intValue;
                                if (activeIdx == i) activeLookConfigIndexProp.intValue = 0;
                                else if (activeIdx > i) activeLookConfigIndexProp.intValue = activeIdx - 1;

                                serializedObject.ApplyModifiedProperties();
                                ResizeAutoDistributeStates();
                                return;
                            }
                        }
                        EditorGUILayout.EndHorizontal();

                        // Auto distribution toggle
                        _autoDistributeStates[i] = EditorGUILayout.Toggle(
                            new GUIContent("Auto Distribution"),
                            _autoDistributeStates[i]
                        );
                        EditorGUILayout.Space();

                        // Totals check
                        float totalXMin = 0f, totalXMax = 0f;
                        float totalYMin = 0f, totalYMax = 0f;
                        float totalZMin = 0f, totalZMax = 0f;

                        for (int j = 0; j < boneList.arraySize; j++)
                        {
                            var e = boneList.GetArrayElementAtIndex(j);
                            Vector2 xv = e.FindPropertyRelative("xMinMax").vector2Value;
                            Vector2 yv = e.FindPropertyRelative("yMinMax").vector2Value;
                            Vector2 zv = e.FindPropertyRelative("zMinMax").vector2Value;

                            totalXMin += xv.x; totalXMax += xv.y;
                            totalYMin += yv.x; totalYMax += yv.y;
                            totalZMin += zv.x; totalZMax += zv.y;
                        }

                        EditorGUILayout.HelpBox(
                            $"X Sum: [{totalXMin:F1}, {totalXMax:F1}]  Y Sum: [{totalYMin:F1}, {totalYMax:F1}]  Z Sum: [{totalZMin:F1}, {totalZMax:F1}]",
                            MessageType.Info
                        );

                        // Bone offsets
                        boneList.isExpanded = EditorGUILayout.Foldout(boneList.isExpanded, "Bone Offsets", true);
                        if (boneList.isExpanded)
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                for (int j = 0; j < boneList.arraySize; j++)
                                {
                                    var e = boneList.GetArrayElementAtIndex(j);
                                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                                    EditorGUILayout.PropertyField(e.FindPropertyRelative("boneName"));
                                    EditorGUILayout.PropertyField(e.FindPropertyRelative("bone"));

                                    using (var xcc = new EditorGUI.ChangeCheckScope())
                                    {
                                        EditorGUILayout.PropertyField(
                                            e.FindPropertyRelative("xMinMax"),
                                            new GUIContent("X Limits")
                                        );
                                        if (xcc.changed && _autoDistributeStates[i])
                                            RedistributeAxisLimits(boneList, j, Axis.X);
                                    }

                                    using (var ycc = new EditorGUI.ChangeCheckScope())
                                    {
                                        EditorGUILayout.PropertyField(
                                            e.FindPropertyRelative("yMinMax"),
                                            new GUIContent("Y Limits")
                                        );
                                        if (ycc.changed && _autoDistributeStates[i])
                                            RedistributeAxisLimits(boneList, j, Axis.Y);
                                    }

                                    using (var zcc = new EditorGUI.ChangeCheckScope())
                                    {
                                        EditorGUILayout.PropertyField(
                                            e.FindPropertyRelative("zMinMax"),
                                            new GUIContent("Z Limits")
                                        );
                                        if (zcc.changed && _autoDistributeStates[i])
                                            RedistributeAxisLimits(boneList, j, Axis.Z);
                                    }

                                    EditorGUILayout.EndVertical();
                                }

                                if (cc.changed)
                                {
                                    serializedObject.ApplyModifiedProperties();
                                    pbl.RebuildCurrentLookConfig();
                                    return;
                                }
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space();
                }
            }

            // Add new profile button
            if (GUILayout.Button("Add New Look Profile"))
            {
                Undo.RecordObject(pbl, "Add Look Profile");
                pbl.AddLookProfile();
                serializedObject.Update(); // refresh props
                ResizeAutoDistributeStates();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);

            // ─────────────────────────────────────────────────────────────
            // OVERLAY POSES (unlimited, like Pro)
            // ─────────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Overlay Poses", EditorStyles.boldLabel);

            if (overlayPosesProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No overlay poses defined. Use 'Add New Overlay Pose' to create one.",
                    MessageType.Info
                );
            }
            else
            {
                for (int i = 0; i < overlayPosesProp.arraySize; i++)
                {
                    var overlayProp = overlayPosesProp.GetArrayElementAtIndex(i);
                    var nameProp = overlayProp.FindPropertyRelative("animationName");
                    var poseProp = overlayProp.FindPropertyRelative("poseData");
                    var blendProp = overlayProp.FindPropertyRelative("blendWeight");
                    var chainsProp = overlayProp.FindPropertyRelative("boneChains");

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    string overlayLabel = string.IsNullOrEmpty(nameProp.stringValue)
                        ? $"Overlay {i + 1}"
                        : nameProp.stringValue;

                    overlayProp.isExpanded = EditorGUILayout.Foldout(overlayProp.isExpanded, overlayLabel, true);
                    if (overlayProp.isExpanded)
                    {
                        EditorGUILayout.Space();

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Overlay Name", GUILayout.Width(100));
                        EditorGUILayout.PropertyField(nameProp, GUIContent.none, GUILayout.MinWidth(200));
                        GUILayout.FlexibleSpace();

                        if (i > 0 && GUILayout.Button("Move Up", GUILayout.Width(80)))
                        {
                            overlayPosesProp.MoveArrayElement(i, i - 1);
                            serializedObject.ApplyModifiedProperties();
                            return;
                        }

                        if (i < overlayPosesProp.arraySize - 1 && GUILayout.Button("Move Down", GUILayout.Width(80)))
                        {
                            overlayPosesProp.MoveArrayElement(i, i + 1);
                            serializedObject.ApplyModifiedProperties();
                            return;
                        }

                        if (GUILayout.Button("Delete", GUILayout.Width(80)))
                        {
                            Undo.RecordObject(pbl, "Delete Overlay Pose");
                            overlayPosesProp.DeleteArrayElementAtIndex(i);
                            serializedObject.ApplyModifiedProperties();
                            return;
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Space();

                        // Pose and blend
                        EditorGUILayout.PropertyField(poseProp, new GUIContent("Pose Data"));
                        EditorGUILayout.PropertyField(blendProp, new GUIContent("Blend Weight"));
                        EditorGUILayout.Space();

                        // Bone Chains
                        EditorGUILayout.LabelField("Bone Chains", EditorStyles.boldLabel);

                        for (int c = 0; c < chainsProp.arraySize; c++)
                        {
                            var chainProp = chainsProp.GetArrayElementAtIndex(c);
                            var chainNameProp = chainProp.FindPropertyRelative("chainName");
                            var chainBlendProp = chainProp.FindPropertyRelative("blendWeight");
                            var chainBonesProp = chainProp.FindPropertyRelative("bones");

                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            string chainLabel = string.IsNullOrEmpty(chainNameProp.stringValue)
                                ? "<New Bone Chain>"
                                : chainNameProp.stringValue;

                            chainProp.isExpanded = EditorGUILayout.Foldout(chainProp.isExpanded, chainLabel, true);
                            if (chainProp.isExpanded)
                            {
                                EditorGUILayout.Space();
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.PropertyField(chainNameProp, new GUIContent("Chain Name"));
                                if (GUILayout.Button("Delete Chain", GUILayout.Width(100)))
                                {
                                    Undo.RecordObject(pbl, "Delete Bone Chain");
                                    chainsProp.DeleteArrayElementAtIndex(c);
                                    serializedObject.ApplyModifiedProperties();
                                    return;
                                }
                                EditorGUILayout.EndHorizontal();

                                EditorGUILayout.Slider(chainBlendProp, 0f, 1f, new GUIContent("Blend Weight"));
                                EditorGUILayout.PropertyField(chainBonesProp, new GUIContent("Bones"), true);
                                EditorGUILayout.Space();
                            }
                            EditorGUILayout.EndVertical();
                            GUILayout.Space(4);
                        }

                        if (GUILayout.Button("Add New Bone Chain"))
                        {
                            BoneChainCreatorWindow.OpenWindow(pbl, i);
                        }

                        EditorGUILayout.Space();
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(6);
                }
            }

            if (GUILayout.Button("Add New Overlay Pose"))
            {
                int newIndex = overlayPosesProp.arraySize;
                overlayPosesProp.InsertArrayElementAtIndex(newIndex);
                var newOverlay = overlayPosesProp.GetArrayElementAtIndex(newIndex);

                newOverlay.FindPropertyRelative("animationName").stringValue = "NewOverlay" + (newIndex + 1);
                newOverlay.FindPropertyRelative("blendWeight").floatValue = 1f;
                newOverlay.FindPropertyRelative("poseData").objectReferenceValue = null;
                newOverlay.FindPropertyRelative("boneChains").ClearArray();

                serializedObject.ApplyModifiedProperties();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private string GetAxisName(Axis axis)
        {
            switch (axis)
            {
                case Axis.X: return "xMinMax";
                case Axis.Y: return "yMinMax";
                default: return "zMinMax";
            }
        }

        private void ResizeAutoDistributeStates()
        {
            int configCount = lookConfigsProp != null ? lookConfigsProp.arraySize : 0;
            while (_autoDistributeStates.Count < configCount)
                _autoDistributeStates.Add(false);
            while (_autoDistributeStates.Count > configCount)
                _autoDistributeStates.RemoveAt(_autoDistributeStates.Count - 1);
        }

        private void RedistributeAxisLimits(SerializedProperty boneList, int changedIndex, Axis axis)
        {
            int count = boneList.arraySize;
            if (changedIndex < 0 || changedIndex >= count) return;

            float sumMin = 0f, sumMax = 0f;
            for (int i = 0; i <= changedIndex; i++)
            {
                Vector2 minMax = boneList
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(GetAxisName(axis))
                    .vector2Value;
                sumMin += minMax.x;
                sumMax += minMax.y;
            }

            int remainCount = count - (changedIndex + 1);
            if (remainCount <= 0) return;

            float eachMin = (-90f - sumMin) / remainCount;
            float eachMax = (90f - sumMax) / remainCount;

            for (int i = changedIndex + 1; i < count; i++)
            {
                var axisProp = boneList
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(GetAxisName(axis));
                axisProp.vector2Value = new Vector2(eachMin, eachMax);
            }
        }
    }
}
#endif
