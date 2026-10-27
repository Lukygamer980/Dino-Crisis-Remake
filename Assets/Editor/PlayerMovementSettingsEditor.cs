using UnityEditor;
using UnityEngine;

// The Player Movement asset with a button that builds its Animator settings into the player's Animator.
[CustomEditor(typeof(PlayerMovementSettings))]
public class PlayerMovementSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Live values take effect at once, also in Play mode. Values marked (rebuild) need the button below, outside Play mode.", MessageType.Info);
        DrawDefaultInspector();
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Rebuild Animator", GUILayout.Height(28))) PlayerAnimatorBuilder.Build();
        }
    }
}
