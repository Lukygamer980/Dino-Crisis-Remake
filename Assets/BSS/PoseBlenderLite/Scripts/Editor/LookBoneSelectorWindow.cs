using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace BSS.PoseBlender
{
    public class LookBoneSelectorWindow : EditorWindow
    {
        private PoseBlenderLite targetScript;
        private Vector2 scrollPos;

        private Dictionary<Transform, bool> foldoutStates = new Dictionary<Transform, bool>();
        private Dictionary<Transform, bool> selectionStates = new Dictionary<Transform, bool>();

        public static void Open(PoseBlenderLite script)
        {
            var wnd = GetWindow<LookBoneSelectorWindow>("Select Look Bones");
            wnd.targetScript = script;
            wnd.foldoutStates.Clear();
            wnd.selectionStates.Clear();
            wnd.Show();
        }

        private void OnGUI()
        {
            if (targetScript == null || targetScript.animationRoot == null)
            {
                EditorGUILayout.HelpBox("No PoseBlenderLite or animationRoot assigned.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Step 2: Choose Which Bones to Drive", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Scrollable area for the skeleton hierarchy
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            DrawBoneHierarchy(targetScript.animationRoot, 0);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
                SetAllSelection(targetScript.animationRoot, true);
            if (GUILayout.Button("Clear All"))
                SetAllSelection(targetScript.animationRoot, false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // NOTE: We now call ApplySelectionToSceneInstance() instead of ApplySelectionToPrefab()
            if (GUILayout.Button("Accept Selection", GUILayout.Height(30)))
            {
                ApplySelectionToSceneInstance();
            }
        }

        private void DrawBoneHierarchy(Transform current, int indent)
        {
            if (current == null) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);

            bool isSel = selectionStates.ContainsKey(current) && selectionStates[current];
            bool newSel = EditorGUILayout.Toggle(isSel, GUILayout.Width(18));
            if (newSel != isSel)
                selectionStates[current] = newSel;

            bool isExp = foldoutStates.ContainsKey(current) && foldoutStates[current];
            foldoutStates[current] = EditorGUILayout.Foldout(isExp, current.name);
            EditorGUILayout.EndHorizontal();

            if (foldoutStates[current])
            {
                for (int i = 0; i < current.childCount; i++)
                {
                    DrawBoneHierarchy(current.GetChild(i), indent + 1);
                }
            }
        }

        private void SetAllSelection(Transform current, bool value)
        {
            if (current == null) return;
            selectionStates[current] = value;
            for (int i = 0; i < current.childCount; i++)
                SetAllSelection(current.GetChild(i), value);
        }

        /// <summary>
        /// Collects all checked transforms, writes them into availableLookBones,
        /// creates one default LookConfig with zeroed offsets, and updates the Scene instance.
        /// </summary>
        private void ApplySelectionToSceneInstance()
        {
            // 1) Gather all selected transforms
            List<Transform> chosen = new List<Transform>();
            foreach (var kvp in selectionStates)
            {
                if (kvp.Value && kvp.Key != null)
                    chosen.Add(kvp.Key);
            }

            if (chosen.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Bones Selected",
                    "Please select at least one bone to use for look offsets.",
                    "OK"
                );
                return;
            }

            // 2) Directly assign into the scene instance (targetScript.availableLookBones)
            Undo.RecordObject(targetScript, "Assign Available Look Bones");
            targetScript.availableLookBones = new List<Transform>(chosen);
            
            // Set default look-at origin bone to the last bone in the list
            targetScript.lookAtOriginBoneIndex = chosen.Count - 1;

            // 3) Initialize a default �Base� profile so lookConfigs has exactly one entry, matching availableLookBones.Count
            //    If you have InitializeBaseLookProfile() implemented, simply call it:
            targetScript.InitializeBaseLookProfile();

            // 4) Mark the component as dirty so Unity knows the scene has changed
            EditorUtility.SetDirty(targetScript);

            // 5) Close the selector
            this.Close();
        }
    }
}
