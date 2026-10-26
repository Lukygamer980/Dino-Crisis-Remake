using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Phase 2: turns the Mixamo clips in Assets/Characters/Player into Eve's Animator.
// Locomotion is a blend on Speed. Aiming swaps to the pistol set, with Strafe for sidestep.
// Fire, Hit and Dead are triggers. Root motion stays off; PlayerController moves the body.
public static class PlayerAnimatorBuilder
{
    public const string ModelPath = "Assets/Characters/Player/Eve By J.Gonzales.fbx";
    public const string ControllerPath = "Assets/Characters/Player/Eve.controller";
    const string ClipFolder = "Assets/Characters/Player/animations";

    [MenuItem("Dino Demo/Build Player Animator")]
    public static void Build()
    {
        PrepareClip(ModelPath, loop: false);
        string idle = Clip("Idle.fbx", loop: true);
        string walk = Clip("Female Walk.fbx", loop: true);
        string run = Clip("Running.fbx", loop: true);
        string limp = Clip("Injured Run.fbx", loop: true);
        string aimIdle = Clip("Pistol Idle.fbx", loop: true);
        string aimWalk = Clip("Pistol Walk.fbx", loop: true);
        string aimRun = Clip("Pistol Run.fbx", loop: true);
        string aimBack = Clip("Pistol Walk Backward.fbx", loop: true);
        string aimLeft = Clip("Pistol Strafe Left.fbx", loop: true);
        string aimRight = Clip("Pistol Strafe Right.fbx", loop: true);
        string shoot = Clip("Shooting.fbx", loop: false);
        string hit = Clip("Hit Reaction.fbx", loop: false);
        string dead = Clip("Dying.fbx", loop: false);

        // Rebuild in place: deleting the asset would give it a new GUID and leave the scene's Animator empty.
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else Clear(controller);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Strafe", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Danger", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        // Running covers 3.6 m/s at 1x, so 1.2x matches the 4.4 m/s sprint without the feet skating.
        AnimatorState free = State(controller, machine, "Free", Tree(controller, "Free", Load(idle), 0f, Load(walk), 2.2f, Load(run), 3.3f, 1.2f));
        AnimatorState hurt = State(controller, machine, "Hurt", Tree(controller, "Hurt", Load(idle), 0f, Load(walk), 1.4f, Load(limp), 2.6f, 1f));
        AnimatorState aim = State(controller, machine, "Aim", AimTree(controller, Load(aimIdle), Load(aimWalk), Load(aimRun), Load(aimBack), Load(aimLeft), Load(aimRight)));
        AnimatorState shooting = OneShot(machine, "Shoot", Load(shoot));
        AnimatorState hitState = OneShot(machine, "Hit", Load(hit));
        AnimatorState deadState = OneShot(machine, "Dead", Load(dead));
        machine.defaultState = free;

        Link(free, aim, "IsAiming", on: true);
        Link(hurt, aim, "IsAiming", on: true);
        Link(aim, hurt, "IsAiming", on: false);
        // The transition above also needs Danger, otherwise a healthy character would limp after aiming.
        aim.transitions[aim.transitions.Length - 1].AddCondition(AnimatorConditionMode.If, 0f, "Danger");
        Link(aim, free, "IsAiming", on: false);
        Link(free, hurt, "Danger", on: true);
        Link(hurt, free, "Danger", on: false);

        // Hit and Fire restart their clip, so a second trigger is used at once and not left
        // pending, where it would later pull Eve out of Dead.
        Any(machine, deadState, "Dead", toSelf: false);
        Any(machine, hitState, "Hit", toSelf: true);
        Any(machine, shooting, "Fire", toSelf: true);
        Return(shooting, free, hurt, aim);
        Return(hitState, free, hurt, aim);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static void Clear(AnimatorController controller)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
        {
            if (asset != controller) Object.DestroyImmediate(asset, true);
        }
        controller.layers = new AnimatorControllerLayer[0];
        controller.parameters = new AnimatorControllerParameter[0];
        controller.AddLayer("Base Layer");
    }

    static string Clip(string file, bool loop)
    {
        string path = ClipFolder + "/" + file;
        PrepareClip(path, loop);
        return path;
    }

    static void PrepareClip(string path, bool loop)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.loopTime == loop) continue;
            clip.loopTime = loop;
            changed = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
        if (changed) importer.SaveAndReimport();
    }

    static AnimationClip Load(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview")) return clip;
        }
        Debug.LogError("No animation clip in " + path);
        return null;
    }

    static BlendTree Tree(AnimatorController controller, string name, AnimationClip a, float aAt, AnimationClip b, float bAt, AnimationClip c, float cAt, float lastTimeScale)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
        };
        tree.AddChild(a, aAt);
        tree.AddChild(b, bAt);
        tree.AddChild(c, cAt);
        // No minThreshold/maxThreshold here: setting them spreads the thresholds evenly again.
        if (lastTimeScale != 1f)
        {
            ChildMotion[] children = tree.children;
            children[2].timeScale = lastTimeScale;
            tree.children = children;
        }
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    static BlendTree AimTree(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip back, AnimationClip left, AnimationClip right)
    {
        var tree = new BlendTree
        {
            name = "Aim",
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "Strafe",
            blendParameterY = "Speed",
        };
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, Vector2.zero);
        tree.AddChild(walk, new Vector2(0f, 2.2f));
        tree.AddChild(run, new Vector2(0f, 3.3f));
        tree.AddChild(back, new Vector2(0f, -1.6f));
        tree.AddChild(left, new Vector2(-1.6f, 0f));
        tree.AddChild(right, new Vector2(1.6f, 0f));
        // Pistol Run covers 2.95 m/s at 1x. Aimed movement is capped at aimMoveSpeed, so this only shows up under knockback.
        ChildMotion[] children = tree.children;
        children[2].timeScale = 1.1f;
        tree.children = children;
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    static AnimatorState State(AnimatorController controller, AnimatorStateMachine machine, string name, Motion motion)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = motion;
        return state;
    }

    static AnimatorState OneShot(AnimatorStateMachine machine, string name, AnimationClip clip)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        return state;
    }

    static void Link(AnimatorState from, AnimatorState to, string parameter, bool on)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.15f;
        transition.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
    }

    static void Any(AnimatorStateMachine machine, AnimatorState to, string trigger, bool toSelf)
    {
        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.canTransitionToSelf = toSelf;
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    // Hurt comes before Free, otherwise a badly hurt Eve runs normally for a moment after each hit.
    static void Return(AnimatorState from, AnimatorState free, AnimatorState hurt, AnimatorState aim)
    {
        AnimatorStateTransition toHurt = from.AddTransition(hurt);
        toHurt.hasExitTime = true;
        toHurt.exitTime = 0.85f;
        toHurt.duration = 0.1f;
        toHurt.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsAiming");
        toHurt.AddCondition(AnimatorConditionMode.If, 0f, "Danger");

        AnimatorStateTransition toFree = from.AddTransition(free);
        toFree.hasExitTime = true;
        toFree.exitTime = 0.85f;
        toFree.duration = 0.1f;
        toFree.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsAiming");

        AnimatorStateTransition toAim = from.AddTransition(aim);
        toAim.hasExitTime = true;
        toAim.exitTime = 0.85f;
        toAim.duration = 0.1f;
        toAim.AddCondition(AnimatorConditionMode.If, 0f, "IsAiming");
    }
}
