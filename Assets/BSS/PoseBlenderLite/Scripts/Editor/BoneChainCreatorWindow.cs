#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static BSS.PoseBlender.PoseBlenderLite;

namespace BSS.PoseBlender
{
    public class BoneChainCreatorWindow : EditorWindow
    {
        private PoseBlenderLite targetScript;
        private string newChainName = "New Bone Chain";

        public int selectedOverlayPoseIndex = 0;
        private int selectedBoneChainIndex = 0;
        private int actionModeIndex = 0;
        private readonly string[] actionModes = { "Create New Bone Chain", "Edit Existing Bone Chain" };

        private Dictionary<Transform, bool> foldoutStates = new Dictionary<Transform, bool>();
        private Dictionary<Transform, bool> selectionStates = new Dictionary<Transform, bool>();
        private HashSet<Transform> toggledDragSet = new HashSet<Transform>();
        private Vector2 scrollPos;

        private Transform Root => targetScript != null ? targetScript.animationRoot : null;

        // Menu item – still labeled "Lite" but now works with unlimited overlays
        [MenuItem("Tools/Pose Blender Lite/Bone Chain Creator Lite")]
        private static void ShowWindow()
        {
            var window = GetWindow<BoneChainCreatorWindow>("Bone Chain Creator Lite");
            if (Selection.activeGameObject != null)
            {
                var pb = Selection.activeGameObject.GetComponent<PoseBlenderLite>();
                if (pb != null) window.targetScript = pb;
            }
            window.Show();
        }

        public static void OpenWindow(PoseBlenderLite script, int overlayIndex = 0)
        {
            var window = GetWindow<BoneChainCreatorWindow>("Bone Chain Creator Lite");
            window.targetScript = script;

            if (script != null && script.overlayPoses != null && script.overlayPoses.Count > 0)
                window.selectedOverlayPoseIndex = Mathf.Clamp(overlayIndex, 0, script.overlayPoses.Count - 1);
            else
                window.selectedOverlayPoseIndex = 0;

            window.Show();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseUp)
                toggledDragSet.Clear();

            if (targetScript == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with PoseBlenderLite to begin.", MessageType.Error);
                return;
            }

            if (Root == null)
            {
                EditorGUILayout.HelpBox("Assign an animationRoot in the PoseBlenderLite component.", MessageType.Warning);
                return;
            }

            // Clamp selected overlay index to valid range
            if (targetScript.overlayPoses == null)
                targetScript.overlayPoses = new List<AnimationOverride>();

            if (targetScript.overlayPoses.Count > 0)
                selectedOverlayPoseIndex = Mathf.Clamp(selectedOverlayPoseIndex, 0, targetScript.overlayPoses.Count - 1);
            else
                selectedOverlayPoseIndex = 0;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.LabelField("Bone Chain Creator / Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Action mode: Create / Edit
            actionModeIndex = EditorGUILayout.Popup("Action", actionModeIndex, actionModes);
            EditorGUILayout.Space();

            // Overlay Pose dropdown (unlimited overlays)
            if (targetScript.overlayPoses.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No overlay poses found. Add an overlay pose in the PoseBlenderLite component first.",
                    MessageType.Warning
                );
                EditorGUILayout.EndScrollView();
                return;
            }

            string[] overlayNames = new string[targetScript.overlayPoses.Count];
            for (int i = 0; i < targetScript.overlayPoses.Count; i++)
            {
                var ov = targetScript.overlayPoses[i];
                string name = ov != null && !string.IsNullOrEmpty(ov.animationName)
                    ? ov.animationName
                    : $"Overlay {i + 1}";
                overlayNames[i] = name;
            }

            selectedOverlayPoseIndex = EditorGUILayout.Popup("Select Overlay Pose", selectedOverlayPoseIndex, overlayNames);
            EditorGUILayout.Space();

            // Retrieve chosen overlay
            AnimationOverride overlay = null;
            if (selectedOverlayPoseIndex >= 0 && selectedOverlayPoseIndex < targetScript.overlayPoses.Count)
                overlay = targetScript.overlayPoses[selectedOverlayPoseIndex];

            // Edit mode: choose existing chain
            if (actionModeIndex == 1)
            {
                if (overlay == null || overlay.boneChains == null || overlay.boneChains.Count == 0)
                {
                    EditorGUILayout.HelpBox("No bone chains available to edit in this overlay.", MessageType.Warning);
                }
                else
                {
                    string[] chainNames = overlay.boneChains.ConvertAll(c =>
                        string.IsNullOrEmpty(c.chainName) ? "<Unnamed Chain>" : c.chainName
                    ).ToArray();

                    selectedBoneChainIndex = Mathf.Clamp(selectedBoneChainIndex, 0, overlay.boneChains.Count - 1);
                    selectedBoneChainIndex = EditorGUILayout.Popup("Select Bone Chain", selectedBoneChainIndex, chainNames);

                    if (GUILayout.Button("Load Selected Bone Chain"))
                    {
                        selectionStates.Clear();
                        var chain = overlay.boneChains[selectedBoneChainIndex];
                        newChainName = chain.chainName;
                        foreach (var bs in chain.bones)
                            if (bs.bone != null) selectionStates[bs.bone] = true;
                    }
                }
                EditorGUILayout.Space();
            }

            newChainName = EditorGUILayout.TextField("Chain Name", newChainName);
            EditorGUILayout.Space();

            // Toolbar
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All")) SelectAllBones(Root);
            if (GUILayout.Button("Clear Selection")) ClearSelection(Root);
            if (GUILayout.Button("Expand All")) SetAllFoldouts(Root, true);
            if (GUILayout.Button("Collapse All")) SetAllFoldouts(Root, false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (GUILayout.Button(actionModeIndex == 0 ? "Create Bone Chain from Selection" : "Update Bone Chain from Selection"))
                ApplyBoneChainChanges();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Select Bones:", EditorStyles.boldLabel);

            DrawBoneHierarchy(Root, 0);

            EditorGUILayout.EndScrollView();
        }

        private void DrawBoneHierarchy(Transform current, int indent)
        {
            if (current == null) return;

            EditorGUILayout.BeginHorizontal();

            Rect toggleRect = GUILayoutUtility.GetRect(20, EditorGUIUtility.singleLineHeight, GUILayout.Width(20));
            bool sel = selectionStates.ContainsKey(current) && selectionStates[current];

            Event e = Event.current;
            if (toggleRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown ||
                    (e.type == EventType.MouseDrag && !toggledDragSet.Contains(current)))
                {
                    sel = !sel;
                    toggledDragSet.Add(current);
                    e.Use();
                }
            }

            sel = EditorGUI.Toggle(toggleRect, sel);
            selectionStates[current] = sel;

            GUILayout.Space(indent * 15);

            bool fold = foldoutStates.ContainsKey(current) && foldoutStates[current];
            fold = EditorGUILayout.Foldout(fold, current.name, true);
            foldoutStates[current] = fold;

            EditorGUILayout.EndHorizontal();

            if (fold)
            {
                foreach (Transform child in current)
                    DrawBoneHierarchy(child, indent + 1);
            }
        }

        private void ApplyBoneChainChanges()
        {
            // Collect selected bones
            var selBones = new List<Transform>();
            foreach (var kv in selectionStates)
                if (kv.Value) selBones.Add(kv.Key);

            if (selBones.Count == 0)
            {
                EditorUtility.DisplayDialog("No Bones", "Select at least one bone.", "OK");
                return;
            }

            if (targetScript.overlayPoses == null ||
                selectedOverlayPoseIndex < 0 ||
                selectedOverlayPoseIndex >= targetScript.overlayPoses.Count)
            {
                EditorUtility.DisplayDialog("No Overlay", "Selected overlay index is invalid.", "OK");
                return;
            }

            var overlay = targetScript.overlayPoses[selectedOverlayPoseIndex];
            if (overlay == null)
            {
                EditorUtility.DisplayDialog("No Overlay", "Selected overlay is null.", "OK");
                return;
            }

            if (overlay.boneChains == null)
                overlay.boneChains = new List<BoneChain>();

            if (actionModeIndex == 0)
            {
                // CREATE new chain
                var chain = new BoneChain { chainName = newChainName, blendWeight = 1f, bones = new List<BoneRotationSettings>() };
                foreach (var b in selBones)
                {
                    chain.bones.Add(new BoneRotationSettings
                    {
                        boneName = b.name,
                        bone = b,
                        blendWeight = 1f,
                        rotationOffset = Vector3.zero
                    });
                }

                Undo.RecordObject(targetScript, "Add Bone Chain");
                overlay.boneChains.Add(chain);
            }
            else
            {
                // EDIT existing chain
                var chains = overlay.boneChains;
                if (chains == null || chains.Count == 0)
                {
                    EditorUtility.DisplayDialog("No Chains", "No bone chains to edit.", "OK");
                    return;
                }

                selectedBoneChainIndex = Mathf.Clamp(selectedBoneChainIndex, 0, chains.Count - 1);
                var chain = chains[selectedBoneChainIndex];

                chain.chainName = newChainName;
                chain.bones = new List<BoneRotationSettings>();

                foreach (var b in selBones)
                {
                    chain.bones.Add(new BoneRotationSettings
                    {
                        boneName = b.name,
                        bone = b,
                        blendWeight = 1f,
                        rotationOffset = Vector3.zero
                    });
                }

                Undo.RecordObject(targetScript, "Update Bone Chain");
                overlay.boneChains[selectedBoneChainIndex] = chain;
            }

            EditorUtility.SetDirty(targetScript);
            selectionStates.Clear();
            Close();
        }

        private void SelectAllBones(Transform t)
        {
            if (t == null) return;
            selectionStates[t] = true;
            foreach (Transform c in t) SelectAllBones(c);
        }

        private void ClearSelection(Transform t)
        {
            if (t == null) return;
            selectionStates[t] = false;
            foreach (Transform c in t) ClearSelection(c);
        }

        private void SetAllFoldouts(Transform t, bool s)
        {
            if (t == null) return;
            foldoutStates[t] = s;
            foreach (Transform c in t) SetAllFoldouts(c, s);
        }
    }
}
#endif
