using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// Builds the raptor's Animator from the clips in the Raptor Setup asset. Raptor drives it: Speed is
// how fast the NavMeshAgent goes, Searching is on while it looks around, and Alert, Attack, Hit and
// Die are triggers. The PBR Velociraptor's clips carry animation events that play its own sounds
// (RaptorSoundEffects on the model), so the screech, bite, yelp and death sound with the clips.
public static class RaptorAnimatorBuilder
{
    public const string ControllerPath = "Assets/Characters/Raptor/Raptor.controller";
    const string Folder = "Assets/Characters/Raptor";
    const string PackModel = "Assets/PBRVelociraptor/Models/15K/Raptor_Animated_FBX.fbx";
    const string PackPrefab = "Assets/PBRVelociraptor/Prefabs/PBR/15K/PBR_Velociraptor_Green.prefab";
    // How long Raptor's attack takes from wind-up to recovery (attackWindUp + attackRecovery).
    const float AttackTime = 1.55f;
    // How far into the screech it hands back to running.
    public const float ScreechExit = 0.9f;

    public static RaptorSetup Setup
    {
        get
        {
            var setup = AssetDatabase.LoadAssetAtPath<RaptorSetup>(RaptorSetup.Path);
            if (setup != null) return setup;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Characters", "Raptor");
            setup = ScriptableObject.CreateInstance<RaptorSetup>();
            setup.model = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefab);
            if (setup.model != null)
            {
                setup.idle = Clip("Raptor_Idle1_Anim");
                setup.walk = Clip("Raptor_Walk_Anim");
                setup.run = Clip("Raptor_Run1_Anim");
                setup.sprint = Clip("Raptor_Run2_Anim");
                setup.alert = Clip("Raptor_Screech1_Anim");
                setup.attack = Clip("Raptor_Bite2_Anim");
                setup.hit = Clip("Raptor_Hit1_Anim");
                setup.search = Clip("Raptor_SniffGround_Anim");
                setup.death = Clip("Raptor_Death1_Anim");
                setup.deathAlt = Clip("Raptor_Death2_Anim");
            }
            AssetDatabase.CreateAsset(setup, RaptorSetup.Path);
            return setup;
        }
    }

    static AnimationClip Clip(string take)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(PackModel))
            if (asset is AnimationClip clip && clip.name == "RaptorArmature|" + take) return clip;
        return null;
    }

    [MenuItem("Dino Demo/Build Raptor Animator")]
    public static RuntimeAnimatorController Build()
    {
        RaptorSetup setup = Setup;
        if (setup.model == null || setup.idle == null || setup.walk == null || setup.run == null) return null;
        // In-place clips loop on their own; the speeds come from the root-motion twin of each clip.
        setup.walkSpeed = Speed(setup.walk, setup.modelScale);
        setup.runSpeed = Speed(setup.run, setup.modelScale);
        setup.sprintSpeed = setup.sprint != null ? Speed(setup.sprint, setup.modelScale) : setup.runSpeed;
        EditorUtility.SetDirty(setup);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (asset != controller) Object.DestroyImmediate(asset, true);
            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            controller.AddLayer("Base Layer");
        }
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Searching", AnimatorControllerParameterType.Bool);
        controller.AddParameter("DeathIndex", AnimatorControllerParameterType.Int);
        foreach (string trigger in new[] { "Alert", "Attack", "Hit", "Die" })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        // Each gait plays at its own pace at the speed it covers; past the sprint, the sprint speeds up.
        var locomotion = new BlendTree { name = "Locomotion", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(locomotion, controller);
        locomotion.AddChild(setup.idle, 0f);
        locomotion.AddChild(setup.walk, setup.walkSpeed);
        locomotion.AddChild(setup.run, setup.runSpeed);
        if (setup.sprint != null) locomotion.AddChild(setup.sprint, setup.sprintSpeed);
        AnimationClip top = setup.sprint != null ? setup.sprint : setup.run;
        float topSpeed = setup.sprint != null ? setup.sprintSpeed : setup.runSpeed;
        locomotion.AddChild(top, topSpeed * 1.5f);
        ChildMotion[] children = locomotion.children;
        children[^1].timeScale = 1.5f;
        locomotion.children = children;

        AnimatorState move = machine.AddState("Locomotion");
        move.motion = locomotion;
        machine.defaultState = move;

        AnimatorState search = setup.search != null ? State(machine, "Search", setup.search) : null;
        if (search != null)
        {
            Transition(move, search, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Searching");
            Transition(search, move, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Searching");
        }

        // Played at its own pace; Raptor stops for as long (screechTime, set from the clip by CharacterPrefabs).
        OneShot(machine, move, "Alert", setup.alert, 1f, ScreechExit);
        if (setup.attack != null) OneShot(machine, move, "Attack", setup.attack, setup.attack.length / AttackTime, 0.95f);
        OneShot(machine, move, "Hit", setup.hit, 2f, 0.8f);

        // Death first, so nothing else can stand it back up.
        AnimationClip[] deaths = setup.deathAlt != null ? new[] { setup.death, setup.deathAlt } : new[] { setup.death };
        for (int i = 0; i < deaths.Length; i++)
        {
            if (deaths[i] == null) continue;
            AnimatorState dead = State(machine, "Dead " + (i + 1), deaths[i]);
            AnimatorStateTransition die = machine.AddAnyStateTransition(dead);
            die.duration = 0.15f;
            die.canTransitionToSelf = false;
            die.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            die.AddCondition(AnimatorConditionMode.Equals, i, "DeathIndex");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // A clip played once on a trigger from anywhere but death, then back to moving.
    static void OneShot(AnimatorStateMachine machine, AnimatorState back, string trigger, AnimationClip clip, float speed, float exitTime)
    {
        if (clip == null) return;
        AnimatorState state = State(machine, trigger, clip);
        state.speed = speed;
        AnimatorStateTransition into = machine.AddAnyStateTransition(state);
        into.duration = 0.1f;
        into.canTransitionToSelf = trigger == "Hit";
        into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        AnimatorStateTransition exit = Transition(state, back, 0.25f);
        exit.hasExitTime = true;
        exit.exitTime = exitTime;
    }

    static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = motion;
        return state;
    }

    static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        return transition;
    }

    // Ground speed of an in-place clip, read from its root-motion twin ("..._RM") in the same file:
    // how far the armature's root travels in one cycle, in the model's space and at its scale. With
    // no twin, a clip of its own root travel, or nothing, it falls back to the clip's own root.
    static float Speed(AnimationClip clip, float modelScale)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        AnimationClip measured = clip;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip twin && twin.name == clip.name + "_RM") measured = twin;
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        float best = 0f;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(measured))
        {
            if (!binding.propertyName.StartsWith("m_LocalPosition") || binding.path.Split('/').Length > 2) continue;
            AnimationCurve curve = AnimationUtility.GetEditorCurve(measured, binding);
            float travel = Mathf.Abs(curve.Evaluate(measured.length) - curve.Evaluate(0f));
            // The bone's own parent scales its local travel into the model's space.
            Transform bone = model != null ? model.transform.Find(binding.path) : null;
            float parentScale = bone != null && bone.parent != null ? bone.parent.lossyScale.x : 1f;
            best = Mathf.Max(best, travel * parentScale);
        }
        return measured.length > 0f ? best / measured.length * modelScale : 0f;
    }
}
