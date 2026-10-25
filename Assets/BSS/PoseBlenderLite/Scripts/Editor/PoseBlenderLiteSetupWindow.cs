#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace BSS.PoseBlender
{
    public class PoseBlenderLiteSetupWindow : EditorWindow
    {
        private GameObject sceneCharacter;
        private Animator animatorRef;
        private Transform animationRootRef;
        private PoseBlenderLite targetPB;
        private bool hasAppliedSetup = false;

        private Vector2 scrollPos;
        private Dictionary<Transform, bool> foldoutStates = new Dictionary<Transform, bool>();
        private Dictionary<Transform, bool> selectionStates = new Dictionary<Transform, bool>();
        private bool hasSelectedBones = false;

        [MenuItem("Tools/Pose Blender Lite/Character Setup Lite")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<PoseBlenderLiteSetupWindow>(true, "PoseBlender Lite Character Setup", true);
            wnd.minSize = new Vector2(500, 400);
            wnd.ResetAll();
        }

        private void ResetAll()
        {
            sceneCharacter = null;
            animatorRef = null;
            animationRootRef = null;
            targetPB = null;
            hasAppliedSetup = false;
            hasSelectedBones = false;
            foldoutStates.Clear();
            selectionStates.Clear();
            scrollPos = Vector2.zero;
        }

        private void OnGUI()
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("PoseBlender Lite Character Setup", EditorStyles.boldLabel);
            GUILayout.Space(12);

            DrawStep1_SelectCharacter();
            GUILayout.Space(20);

            if (hasAppliedSetup)
            {
                DrawStep2_SelectBones();
                GUILayout.Space(20);
            }

            if (hasAppliedSetup && hasSelectedBones)
            {
                DrawStep3_SavePrefab();
            }
        }

        private void DrawStep1_SelectCharacter()
        {
            EditorGUILayout.LabelField("Step 1: Select Your Scene Character", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            sceneCharacter = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Scene Character", "Drag your character GameObject from the Hierarchy"),
                sceneCharacter,
                typeof(GameObject),
                true
            );
            if (EditorGUI.EndChangeCheck())
            {
                animatorRef = null;
                animationRootRef = null;
                targetPB = null;
                hasAppliedSetup = false;
                hasSelectedBones = false;
                foldoutStates.Clear();
                selectionStates.Clear();
            }

            GUILayout.Space(4);
            animatorRef = (Animator)EditorGUILayout.ObjectField(
                new GUIContent("Animator Component", "Drag the Animator component from your character"),
                animatorRef,
                typeof(Animator),
                true
            );

            GUILayout.Space(4);
            animationRootRef = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Animation Root", "Drag the root bone (e.g. Hips) of your rig"),
                animationRootRef,
                typeof(Transform),
                true
            );

            GUILayout.Space(8);
            EditorGUI.BeginDisabledGroup(sceneCharacter == null || animatorRef == null || animationRootRef == null || hasAppliedSetup);
            if (GUILayout.Button("Apply Setup & Select Bones", GUILayout.Height(30)))
            {
                ApplySetupToSceneCharacter();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void ApplySetupToSceneCharacter()
        {
            if (sceneCharacter == null || animatorRef == null || animationRootRef == null)
            {
                Debug.LogError("[Setup] Missing fields. Please assign Scene Character, Animator, and Animation Root.");
                return;
            }

            // Add (or get) PoseBlenderLite on the sceneCharacter
            targetPB = sceneCharacter.GetComponent<PoseBlenderLite>();
            if (targetPB == null)
            {
                targetPB = Undo.AddComponent<PoseBlenderLite>(sceneCharacter);
            }
            Undo.RecordObject(targetPB, "PoseBlenderLite Setup");

            targetPB.animator = animatorRef;
            targetPB.animationRoot = animationRootRef;
            targetPB.initialized = true;

            EditorUtility.SetDirty(targetPB);

            hasAppliedSetup = true;
            hasSelectedBones = false;

            foldoutStates.Clear();
            selectionStates.Clear();
            scrollPos = Vector2.zero;
        }

        private void DrawStep2_SelectBones()
        {
            EditorGUILayout.LabelField("Step 2: Choose Which Bones Drive the Look Offsets", EditorStyles.boldLabel);
            GUILayout.Space(4);

            if (targetPB == null || animationRootRef == null)
            {
                EditorGUILayout.HelpBox("PoseBlenderLite or animationRoot missing.", MessageType.Error);
                return;
            }

            // Seed dictionary entries for every bone
            SeedBoneStateRecursive(animationRootRef);

            // Draw scrollable hierarchy
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
            DrawBoneHierarchy(animationRootRef, 0);
            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
                SetAllSelection(animationRootRef, true);
            if (GUILayout.Button("Clear All"))
                SetAllSelection(animationRootRef, false);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            EditorGUI.BeginDisabledGroup(hasSelectedBones);
            if (GUILayout.Button("Accept Selection", GUILayout.Height(30)))
            {
                ApplySelectionToPoseBlender();
            }
            EditorGUI.EndDisabledGroup();

            if (hasSelectedBones)
            {
                EditorGUILayout.HelpBox("Bones assigned; Base profile initialized.", MessageType.Info);
            }
        }

        /// <summary>
        /// Ensures every Transform under 'node' has an entry in both dictionaries (foldout & selection).
        /// </summary>
        private void SeedBoneStateRecursive(Transform node)
        {
            if (node == null) return;
            if (!foldoutStates.ContainsKey(node))
                foldoutStates[node] = false;
            if (!selectionStates.ContainsKey(node))
                selectionStates[node] = false;

            for (int i = 0; i < node.childCount; i++)
                SeedBoneStateRecursive(node.GetChild(i));
        }

        private void DrawBoneHierarchy(Transform current, int indent)
        {
            if (current == null) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);

            bool isSel = selectionStates[current];
            bool newSel = EditorGUILayout.Toggle(isSel, GUILayout.Width(18));
            if (newSel != isSel)
            {
                selectionStates[current] = newSel;
                Repaint();
            }

            bool isExp = foldoutStates[current];
            bool newExp = EditorGUILayout.Foldout(isExp, current.name);
            if (newExp != isExp)
            {
                foldoutStates[current] = newExp;
                Repaint();
            }

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

            Repaint();
        }

        private void ApplySelectionToPoseBlender()
        {
            if (targetPB == null)
            {
                Debug.LogError("[Setup] No PoseBlenderLite found.");
                return;
            }

            List<Transform> chosen = new List<Transform>();
            CollectSelectedBonesRecursive(animationRootRef, chosen);

            if (chosen.Count == 0)
            {
                EditorUtility.DisplayDialog("No Bones Selected",
                    "Please select at least one bone.", "OK");
                return;
            }

            Undo.RecordObject(targetPB, "Assign Available Look Bones");
            targetPB.availableLookBones = new List<Transform>(chosen);
            targetPB.InitializeBaseLookProfile();
            EditorUtility.SetDirty(targetPB);

            hasSelectedBones = true;
        }

        private void CollectSelectedBonesRecursive(Transform node, List<Transform> outList)
        {
            if (node == null) return;

            if (selectionStates[node])
                outList.Add(node);

            for (int i = 0; i < node.childCount; i++)
            {
                CollectSelectedBonesRecursive(node.GetChild(i), outList);
            }
        }

        private void DrawStep3_SavePrefab()
        {
            EditorGUILayout.LabelField("Step 3: Save as Prefab", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click below to save the configured character as a new prefab asset.",
                MessageType.Info);
            GUILayout.Space(8);

            if (GUILayout.Button("Save Scene Character as Prefab…", GUILayout.Height(30)))
            {
                SaveSceneCharacterAsPrefab();
            }
        }

        private void SaveSceneCharacterAsPrefab()
        {
            if (sceneCharacter == null)
            {
                Debug.LogError("[Setup] No Scene Character assigned.");
                return;
            }

            string defaultName = sceneCharacter.name + "_PB.prefab";
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Character as Prefab",
                defaultName,
                "prefab",
                "Choose folder & name for the new prefab asset."
            );
            if (string.IsNullOrEmpty(path))
                return;

            GameObject newPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                sceneCharacter,
                path,
                InteractionMode.UserAction
            );

            if (newPrefab != null)
            {
                Debug.Log($"[Setup] Prefab created at: {path}");
                EditorGUIUtility.PingObject(newPrefab);
            }
            else
            {
                Debug.LogError($"[Setup] Failed to save prefab at: {path}");
            }
        }
    }
}
#endif
