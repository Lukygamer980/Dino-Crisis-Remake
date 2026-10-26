using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// Phase 2: turns the clips in Assets/Characters/Player into Eve's Animator.
//
// With the pistol drawn Eve moves on the MocapOnline "Pistol Basic 27A" set: relaxed, aiming,
// crouched and crouched aiming, each with turns in place that step her feet round and curves that
// lean on the move. With the pistol holstered she moves on the Mixamo clips, which start with a
// first step. Fire, reload, draw and holster play on an upper-body layer, so she can do them on the
// move; two additive layers point the pistol, or the head, where the camera looks.
//
// PlayerController moves and turns the body. Root motion only reports how fast the animated steps
// go (first steps and stops). Speeds and turn rates come from the clips themselves and are written
// to Eve Locomotion.asset, which PlayerController reads.
public static class PlayerAnimatorBuilder
{
    public const string ModelPath = "Assets/Characters/Player/Eve By J.Gonzales.fbx";
    public const string ControllerPath = "Assets/Characters/Player/Eve.controller";
    public const string ProfilePath = "Assets/Characters/Player/Eve Locomotion.asset";
    public const string PistolPath = Pack + "/Pistol/1911_01.fbx";
    const string Pack = "Assets/Characters/Player/FBX_Pistol_Basic_27A2";
    const string MotusManPath = Pack + "/MotusMan/MotusMan_v55.fbx";
    const string RootMotion = Pack + "/Animation/Root_Motion/";
    const string Loops = RootMotion + "Loops/";
    const string SplitJumps = Pack + "/Animation/In-Place/Split_Jumps/";
    const string HolsterAndFire = Pack + "/Animation/Holster_and_Fire/";
    const string AimOffsets = Pack + "/Animation/Aim_Offsets/";
    const string Mixamo = "Assets/Characters/Player/animations/";
    const string Generated = "Assets/Characters/Player/Generated";

    // Speeds that are a design choice, not a clip's own pace. Raptors chase at 4.7 m/s.
    const float SprintSpeed = 4.4f;
    const float LimpFactor = 0.6f;
    // The turn-in-place loops step 45 degrees in 0.67 s. Played up to three times as fast, she still
    // turns round in under a second.
    const float MaxTurnInPlaceRate = 200f;
    // The fastest walk, as a share of the walk's own pace, before she breaks into a jog or a run.
    const float BriskWalk = 1.35f;
    // Every reload takes this long, so PlayerController knows when the rounds go in.
    public const float ReloadTime = 1.6f;

    // Stances, as the Stance parameter that PlayerController sets.
    public const int Holstered = 0, Relaxed = 1, Aim = 2, Crouch = 3, CrouchAim = 4, Hurt = 5, HurtHolstered = 6;
    // Jumps, as the JumpKind parameter.
    public const int JumpStand = 0, JumpRun = 1, JumpAimStand = 2, JumpAimForward = 3, JumpAimLeft = 4, JumpAimRight = 5;

    static AnimatorController controller;
    static float scale;

    [MenuItem("Dino Demo/Build Player Animator")]
    public static void Build()
    {
        PrepareMixamo(ModelPath, loop: false, bakeRotation: false);
        Avatar motusMan = PrepareMotusMan();
        scale = HumanScale();
        if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder("Assets/Characters/Player", "Generated");

        // Holstered: the Mixamo set.
        AnimationClip hIdle = Load(PrepareMixamo(Mixamo + "Idle.fbx", loop: true, bakeRotation: true));
        AnimationClip hStart = Load(PrepareMixamo(Mixamo + "Female Start Walking.fbx", loop: false, bakeRotation: true));
        AnimationClip hWalk = Load(PrepareMixamo(Mixamo + "Female Walk.fbx", loop: true, bakeRotation: true));
        AnimationClip hRun = Load(PrepareMixamo(Mixamo + "Running.fbx", loop: true, bakeRotation: true));
        AnimationClip limp = Load(PrepareMixamo(Mixamo + "Injured Run.fbx", loop: true, bakeRotation: true));
        AnimationClip hit = Load(PrepareMixamo(Mixamo + "Hit Reaction.fbx", loop: false, bakeRotation: false));

        // Pistol drawn: the MocapOnline set. Turns keep their yaw out of the pose, so PlayerController
        // turns the body and the feet step round with it.
        AnimationClip idle = PackClip(Loops + "W1_Stand_Relaxed_Idle.fbx", motusMan, loop: true);
        AnimationClip fidget = PackClip(RootMotion + "W1_Stand_Relaxed_Fgt_v2.fbx", motusMan);
        AnimationClip walk = PackClip(Loops + "W1_Walk_F_Loop.fbx", motusMan, loop: true);
        AnimationClip jog = PackClip(Loops + "W1_Jog_F_Loop.fbx", motusMan, loop: true);
        AnimationClip run = PackClip(Loops + "W1_Run_F_Loop.fbx", motusMan, loop: true);
        AnimationClip runLeft = PackClip(Loops + "W1_Run_L_CIR_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip runRight = PackClip(Loops + "W1_Run_R_CIR_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip runStop = PackClip(RootMotion + "W1_Run_F_to_Stand_Relaxed_RU.fbx", motusMan);
        AnimationClip turnLeft = PackClip(Loops + "W1_Stand_Rlx_Turn_In_Place_L_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip turnRight = PackClip(Loops + "W1_Stand_Rlx_Turn_In_Place_R_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip toAim = PackClip(RootMotion + "W1_Stand_Relaxed_To_Aim.fbx", motusMan);
        AnimationClip toRelaxed = PackClip(RootMotion + "W1_Stand_Aim_To_Relaxed.fbx", motusMan);

        AnimationClip aimIdle = PackClip(Loops + "W1_Stand_Aim_Idle.fbx", motusMan, loop: true);
        AnimationClip aimWalk = PackClip(Loops + "W1_Walk_Aim_F_Loop.fbx", motusMan, loop: true);
        AnimationClip aimWalkBack = PackClip(Loops + "W1_Walk_Aim_B_Loop.fbx", motusMan, loop: true);
        AnimationClip aimWalkLeft = PackClip(Loops + "W1_Walk_Aim_L_Loop.fbx", motusMan, loop: true);
        AnimationClip aimWalkRight = PackClip(Loops + "W1_Walk_Aim_R_Loop.fbx", motusMan, loop: true);
        AnimationClip aimWalkCurveLeft = PackClip(Loops + "W1_Walk_Aim_L_CIR_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip aimWalkCurveRight = PackClip(Loops + "W1_Walk_Aim_R_CIR_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip aimJog = PackClip(Loops + "W1_Jog_Aim_F_Loop.fbx", motusMan, loop: true);
        AnimationClip aimJogBack = PackClip(Loops + "W1_Jog_Aim_B_Loop.fbx", motusMan, loop: true);
        AnimationClip aimJogLeft = PackClip(Loops + "W1_Jog_Aim_L_Loop.fbx", motusMan, loop: true);
        AnimationClip aimJogRight = PackClip(Loops + "W1_Jog_Aim_R_Loop.fbx", motusMan, loop: true);
        AnimationClip aimJogCurveLeft = PackClip(Loops + "W1_Jog_Aim_L_CIR_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip aimJogCurveRight = PackClip(Loops + "W1_Jog_Aim_R_CIR_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip aimTurnLeft = PackClip(Loops + "W1_Stand_Aim_Turn_In_Place_L_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip aimTurnRight = PackClip(Loops + "W1_Stand_Aim_Turn_In_Place_R_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip aimStart = PackClip(RootMotion + "W1_Stand_Aim_To_Walk_Aim_F.fbx", motusMan);
        AnimationClip aimWalkStop = PackClip(RootMotion + "W1_Walk_Aim_F_To_Stand_Aim_RU.fbx", motusMan);
        AnimationClip aimJogStop = PackClip(RootMotion + "W1_Jog_Aim_F_to_Stand_Aim_RU.fbx", motusMan);
        AnimationClip aimToCrouchAim = PackClip(RootMotion + "W1_Stand_Aim_To_Crouch_Aim.fbx", motusMan);
        AnimationClip aimQuickLeft = PackClip(RootMotion + "W1_Stand_Aim_L_90.fbx", motusMan, turn: true);
        AnimationClip aimQuickRight = PackClip(RootMotion + "W1_Stand_Aim_R_90.fbx", motusMan, turn: true);

        AnimationClip crouchIdle = PackClip(Loops + "W1_Crouch_Idle.fbx", motusMan, loop: true);
        AnimationClip crouchWalk = PackClip(Loops + "W1_CrouchWalk_F_Loop.fbx", motusMan, loop: true);
        AnimationClip crouchTurnLeft = PackClip(Loops + "W1_Crouch_Rlx_Turn_In_Place_L_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip crouchTurnRight = PackClip(Loops + "W1_Crouch_Rlx_Turn_In_Place_R_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip crouchToCrouchAim = PackClip(RootMotion + "W1_Crouch_To_Crouch_Aim.fbx", motusMan);
        AnimationClip crouchAimIdle = PackClip(Loops + "W1_Crouch_Aim_Idle.fbx", motusMan, loop: true);
        AnimationClip crouchAimWalk = PackClip(Loops + "W1_CrouchWalk_Aim_F_Loop.fbx", motusMan, loop: true);
        AnimationClip crouchAimBack = PackClip(Loops + "W1_CrouchWalk_Aim_B_Loop.fbx", motusMan, loop: true);
        AnimationClip crouchAimLeft = PackClip(Loops + "W1_CrouchWalk_Aim_L_Loop.fbx", motusMan, loop: true);
        AnimationClip crouchAimRight = PackClip(Loops + "W1_CrouchWalk_Aim_R_Loop.fbx", motusMan, loop: true);
        AnimationClip crouchAimTurnLeft = PackClip(Loops + "W1_Crouch_Aim_Turn_In_Place_L_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip crouchAimTurnRight = PackClip(Loops + "W1_Crouch_Aim_Turn_In_Place_R_Loop.fbx", motusMan, loop: true, turn: true);
        AnimationClip crouchAimStart = PackClip(RootMotion + "W1_Crouch_Aim_To_CrouchWalk_Aim_F.fbx", motusMan);
        AnimationClip crouchAimStop = PackClip(RootMotion + "W1_CrouchWalk_Aim_F_To_Crouch_Aim_RU.fbx", motusMan);
        AnimationClip crouchAimToCrouch = PackClip(RootMotion + "W1_Crouch_Aim_To_Crouch.fbx", motusMan);
        AnimationClip crouchAimToAim = PackClip(RootMotion + "W1_Crouch_Aim_To_Stand_Aim.fbx", motusMan);
        AnimationClip crouchQuickLeft = PackClip(RootMotion + "W1_Crouch_Aim_L_90.fbx", motusMan, turn: true);
        AnimationClip crouchQuickRight = PackClip(RootMotion + "W1_Crouch_Aim_R_90.fbx", motusMan, turn: true);

        AnimationClip fire = PackClip(HolsterAndFire + "W1_Stand_Fire_Single.fbx", motusMan);
        AnimationClip crouchFire = PackClip(HolsterAndFire + "W1_Crouch_Fire_Single.fbx", motusMan);
        AnimationClip reload = PackClip(HolsterAndFire + "W1_Stand_Relaxed_Reload.fbx", motusMan);
        AnimationClip aimReload = PackClip(HolsterAndFire + "W1_Stand_Aim_Reload.fbx", motusMan);
        AnimationClip crouchReload = PackClip(HolsterAndFire + "W1_Crouch_Aim_Reload.fbx", motusMan);
        AnimationClip draw = PackClip(HolsterAndFire + "W1_Aim_Equip_Get_From_MOB.fbx", motusMan);
        AnimationClip crouchDraw = PackClip(HolsterAndFire + "W1_Crouch_Aim_Equip_Get_From_MOB.fbx", motusMan);
        AnimationClip holster = PackClip(HolsterAndFire + "W1_Aim_Equip_Return_To_MOB.fbx", motusMan);
        AnimationClip crouchHolster = PackClip(HolsterAndFire + "W1_Crouch_Aim_Equip_Return_To_MOB.fbx", motusMan);

        string[] deathFiles = { "W1_Stand_Relaxed_Death_F", "W1_Stand_Relaxed_Death_B", "W1_Stand_Relaxed_Death_L", "W1_Stand_Relaxed_Death_R" };
        var deaths = new AnimationClip[4];
        for (int i = 0; i < 4; i++) deaths[i] = PackClip(RootMotion + deathFiles[i] + ".fbx", motusMan);

        // Split jumps: the start, a loop in the air, the landing. Only in-place versions exist.
        string[] jumpNames = { "W1_Stand_Relaxed_Jump", "W1_Run_F_Jump_RU", "W1_Stand_Aim_Jump", "W1_Walk_Aim_F_Jump_RU", "W1_Jog_Aim_L_Jump_RU", "W1_Jog_Aim_R_Jump_RU" };
        var jumps = new AnimationClip[jumpNames.Length, 3];
        for (int i = 0; i < jumpNames.Length; i++)
        {
            jumps[i, 0] = PackClip(SplitJumps + jumpNames[i] + "_Start_IPC.fbx", motusMan);
            jumps[i, 1] = PackClip(SplitJumps + jumpNames[i] + "_Air_IPC.fbx", motusMan, loop: true);
            jumps[i, 2] = PackClip(SplitJumps + jumpNames[i] + "_End_IPC.fbx", motusMan);
        }

        // Rebuild in place: deleting the asset would give it a new GUID and leave the scene's Animator empty.
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else Clear(controller);
        AddParameters();

        // Speeds on Eve's body, in m/s.
        float walkSpeed = Speed(walk), jogSpeed = Speed(jog);
        float aimWalkSpeed = Speed(aimWalk), aimJogSpeed = Speed(aimJog);
        float holsteredWalk = Speed(hWalk);
        float hurtWalk = walkSpeed * LimpFactor, hurtRun = SprintSpeed * LimpFactor;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var stances = new Dictionary<int, StanceStates>();

        // Holstered: Idle, a first step, then walk into run.
        var h = stances[Holstered] = new StanceStates(machine, "Holstered");
        h.idle = h.Add("Idle", hIdle);
        h.turn = h.Add("Turn", TurnTree("Holstered Turn", turnLeft, turnRight, hIdle));
        h.move = h.Add("Move", GaitTree("Holstered Move", hIdle,
            new Gait(hWalk, holsteredWalk, holsteredWalk * BriskWalk),
            new Gait(hRun, 0f, SprintSpeed, cycleOffset: 0.92f)));
        AnimatorState hFirstStep = h.Add("First Step", hStart, step: true);

        // Relaxed: walk, jog and run; the run leans into curves; a sprint stops with a last stride.
        var r = stances[Relaxed] = new StanceStates(machine, "Relaxed");
        r.idle = r.Add("Idle", idle);
        r.turn = r.Add("Turn", TurnTree("Relaxed Turn", turnLeft, turnRight, idle));
        r.move = r.Add("Move", GaitTree("Relaxed Move", idle,
            new Gait(walk, walkSpeed, walkSpeed * BriskWalk),
            new Gait(jog, 0f, jogSpeed),
            new Gait(run, 0f, SprintSpeed, left: runLeft, right: runRight)));
        AnimatorState rStop = r.Add("Stop", runStop, step: true);
        AnimatorState rFidget = r.Add("Look Around", fidget);

        // Aiming: strafes in every direction; walking and jogging forward lean into curves.
        var a = stances[Aim] = new StanceStates(machine, "Aim");
        a.idle = a.Add("Idle", aimIdle);
        a.turn = a.Add("Turn", TurnTree("Aim Turn", aimTurnLeft, aimTurnRight, aimIdle));
        a.move = a.Add("Move", DirectionalTree("Aim Move", aimIdle,
            (LeanTree("Aim Walk Lean", aimWalkCurveLeft, aimWalk, aimWalkCurveRight), new Vector2(0f, aimWalkSpeed)),
            (aimWalkBack, new Vector2(0f, -Speed(aimWalkBack))),
            (aimWalkLeft, new Vector2(-Speed(aimWalkLeft), 0f)),
            (aimWalkRight, new Vector2(Speed(aimWalkRight), 0f)),
            (LeanTree("Aim Jog Lean", aimJogCurveLeft, aimJog, aimJogCurveRight), new Vector2(0f, aimJogSpeed)),
            (aimJogBack, new Vector2(0f, -Speed(aimJogBack))),
            (aimJogLeft, new Vector2(-Speed(aimJogLeft), 0f)),
            (aimJogRight, new Vector2(Speed(aimJogRight), 0f))));
        AnimatorState aStart = a.Add("First Step", aimStart, step: true);
        (AnimatorState left, AnimatorState right) aQuick = (a.Add("Quick Turn Left", aimQuickLeft, quickTurn: true), a.Add("Quick Turn Right", aimQuickRight, quickTurn: true));
        AnimatorState aWalkStop = a.Add("Walk Stop", aimWalkStop, step: true);
        AnimatorState aJogStop = a.Add("Jog Stop", aimJogStop, step: true);

        // Crouched: creeps forward and turns to face the way she goes.
        var c = stances[Crouch] = new StanceStates(machine, "Crouch");
        c.idle = c.Add("Idle", crouchIdle);
        c.turn = c.Add("Turn", TurnTree("Crouch Turn", crouchTurnLeft, crouchTurnRight, crouchIdle));
        c.move = c.Add("Move", GaitTree("Crouch Move", crouchIdle, new Gait(crouchWalk, Speed(crouchWalk), Speed(crouchWalk))));

        // Crouched and aiming: creeps in every direction.
        var ca = stances[CrouchAim] = new StanceStates(machine, "Crouch Aim");
        ca.idle = ca.Add("Idle", crouchAimIdle);
        ca.turn = ca.Add("Turn", TurnTree("Crouch Aim Turn", crouchAimTurnLeft, crouchAimTurnRight, crouchAimIdle));
        ca.move = ca.Add("Move", DirectionalTree("Crouch Aim Move", crouchAimIdle,
            (crouchAimWalk, new Vector2(0f, Speed(crouchAimWalk))),
            (crouchAimBack, new Vector2(0f, -Speed(crouchAimBack))),
            (crouchAimLeft, new Vector2(-Speed(crouchAimLeft), 0f)),
            (crouchAimRight, new Vector2(Speed(crouchAimRight), 0f))));
        AnimatorState caStart = ca.Add("First Step", crouchAimStart, step: true);
        (AnimatorState left, AnimatorState right) caQuick = (ca.Add("Quick Turn Left", crouchQuickLeft, quickTurn: true), ca.Add("Quick Turn Right", crouchQuickRight, quickTurn: true));
        AnimatorState caStop = ca.Add("Stop", crouchAimStop, step: true);

        // Badly hurt: a slow walk, and a limp when she hurries.
        var hu = stances[Hurt] = new StanceStates(machine, "Hurt");
        hu.idle = hu.Add("Idle", idle);
        hu.turn = hu.Add("Turn", TurnTree("Hurt Turn", turnLeft, turnRight, idle));
        hu.move = hu.Add("Move", GaitTree("Hurt Move", idle, new Gait(walk, hurtWalk, hurtWalk), new Gait(limp, 0f, hurtRun)));
        var hh = stances[HurtHolstered] = new StanceStates(machine, "Hurt Holstered");
        hh.idle = hh.Add("Idle", hIdle);
        hh.turn = hh.Add("Turn", TurnTree("Hurt Holstered Turn", turnLeft, turnRight, hIdle));
        hh.move = hh.Add("Move", GaitTree("Hurt Holstered Move", hIdle,
            new Gait(hWalk, holsteredWalk * LimpFactor, holsteredWalk * LimpFactor), new Gait(limp, 0f, hurtRun)));

        // Raising or lowering the pistol from a stand, and crouching or standing up with it raised.
        AnimatorStateMachine changes = machine.AddStateMachine("Stance Changes");
        AnimatorState raise = AddState(changes, "Relaxed To Aim", toAim, speed: 1.6f);
        AnimatorState lower = AddState(changes, "Aim To Relaxed", toRelaxed, speed: 1.8f);
        AnimatorState aimDown = AddState(changes, "Aim To Crouch Aim", aimToCrouchAim, speed: 1.4f);
        AnimatorState aimUp = AddState(changes, "Crouch Aim To Aim", crouchAimToAim, speed: 1.4f);
        AnimatorState crouchRaise = AddState(changes, "Crouch To Crouch Aim", crouchToCrouchAim, speed: 1.6f);
        AnimatorState crouchLower = AddState(changes, "Crouch Aim To Crouch", crouchAimToCrouch, speed: 1.6f);

        AnimatorStateMachine air = machine.AddStateMachine("Jump");
        var jumpStates = new AnimatorState[jumpNames.Length, 3];
        for (int i = 0; i < jumpNames.Length; i++)
        {
            string name = jumpNames[i].Replace("W1_", "").Replace("_", " ");
            // The take-off is at the end of the start clip; PlayerController leaves the ground after JumpWindUp.
            jumpStates[i, 0] = AddState(air, name + " Start", jumps[i, 0], speed: jumps[i, 0].length / JumpWindUp);
            jumpStates[i, 1] = AddState(air, name + " Air", jumps[i, 1]);
            jumpStates[i, 2] = AddState(air, name + " Land", jumps[i, 2], speed: 1.3f);
        }

        AnimatorStateMachine reactions = machine.AddStateMachine("Hit And Death");
        AnimatorState hitState = AddState(reactions, "Hit", hit);
        var deathStates = new AnimatorState[4];
        string[] deathNames = { "Fall Forward", "Fall Backward", "Fall Left", "Fall Right" };
        for (int i = 0; i < 4; i++) deathStates[i] = AddState(reactions, "Dead " + deathNames[i], deaths[i]);

        machine.defaultState = r.idle;
        foreach (StanceStates s in stances.Values) s.idle.iKOnFeet = s.move.iKOnFeet = s.turn.iKOnFeet = true;
        foreach (AnimatorState quick in new[] { aQuick.left, aQuick.right, caQuick.left, caQuick.right }) quick.iKOnFeet = true;
        hFirstStep.iKOnFeet = true;

        // ---- Base layer transitions ----
        // Raising or lowering the pistol from a stand plays the matching clip; anything else crossfades.
        Go(r.idle, raise, 0.15f, Is("Stance", Aim), Off("Moving"));
        Go(a.idle, lower, 0.15f, Is("Stance", Relaxed), Off("Moving"));
        Go(a.idle, aimDown, 0.15f, Is("Stance", CrouchAim));
        Go(ca.idle, aimUp, 0.15f, Is("Stance", Aim));
        Go(c.idle, crouchRaise, 0.15f, Is("Stance", CrouchAim));
        Go(ca.idle, crouchLower, 0.15f, Is("Stance", Crouch));
        // Each plays out into the stance it leads to, unless she sets off or changes stance again.
        foreach ((AnimatorState change, float exit, int target) in new[] { (raise, 0.8f, Aim), (lower, 0.75f, Relaxed), (aimDown, 0.85f, CrouchAim), (aimUp, 0.85f, Aim), (crouchRaise, 0.85f, CrouchAim), (crouchLower, 0.85f, Crouch) })
        {
            Go(change, stances[target].move, 0.25f, Is("Stance", target), On("Moving"));
            StanceChanges(change, stances, target, 0.3f);
            ExitBy(change, stances, exit);
        }

        foreach (var pair in stances)
        {
            StanceStates s = pair.Value;
            foreach (AnimatorState state in s.all) StanceChanges(state, stances, pair.Key, 0.25f);

            // Turning in place, moving, and back to a stand.
            Go(s.idle, s.turn, 0.2f, On("TurnInPlace"));
            Go(s.turn, s.move, 0.25f, On("Moving"), Off("TurnInPlace"));
            Go(s.turn, s.idle, 0.25f, Off("TurnInPlace"));
            Go(s.move, s.turn, 0.25f, On("TurnInPlace"), Below("Travel", 0.5f));
        }

        // Holstered: the first step, unless she sprints off.
        Go(h.idle, h.move, 0.2f, On("Moving"), On("Sprint"));
        Go(h.idle, hFirstStep, 0.15f, On("Moving"));
        Go(hFirstStep, h.idle, 0.25f, Off("Moving"));
        Go(hFirstStep, h.move, 0.25f, On("Sprint"));
        StepInto(hFirstStep, h.move, hStart, hWalk);
        Stop(h);

        // Relaxed: a sprint ends in a last stride; standing still a while, she looks around.
        Go(r.idle, r.move, 0.2f, On("Moving"));
        // Stop is set the frame she lets go, with StopSpeed the pace she let go at.
        Go(r.move, rStop, 0.2f, On("Stop"), Above("StopSpeed", SprintSpeed * 0.7f));
        Go(rStop, r.turn, 0.25f, On("TurnInPlace"));
        Go(rStop, r.move, 0.2f, On("Moving"));
        ExitTo(rStop, r.idle, 0.85f);
        Stop(r);
        // Setting off or turning just as she starts to look around cuts the look short.
        Go(r.idle, rFidget, 0.4f, Above("IdleTime", 8f)).interruptionSource = TransitionInterruptionSource.Source;
        Go(rFidget, r.turn, 0.3f, On("TurnInPlace"));
        Go(rFidget, r.move, 0.3f, On("Moving"));
        ExitTo(rFidget, r.idle, 0.95f);

        // Aiming with the camera swung far round, standing, she pivots a quarter turn in one go.
        QuickTurns(a, aQuick);
        QuickTurns(ca, caQuick);

        // Aiming: steps off forward, and stops with a last stride from a walk or a jog.
        Go(a.idle, aStart, 0.15f, On("Moving"), On("Forward"), Off("Sprint"));
        Go(a.idle, a.move, 0.2f, On("Moving"));
        Go(aStart, a.idle, 0.25f, Off("Moving"));
        Go(aStart, a.move, 0.25f, Off("Forward"));
        Go(aStart, a.move, 0.25f, On("Sprint"));
        StepInto(aStart, a.move, aimStart, aimWalk);
        Go(a.move, aJogStop, 0.2f, On("Stop"), On("Forward"), Above("StopSpeed", (aimWalkSpeed + aimJogSpeed) / 2f));
        Go(a.move, aWalkStop, 0.2f, On("Stop"), On("Forward"), Above("StopSpeed", aimWalkSpeed * 0.6f));
        foreach (AnimatorState stop in new[] { aWalkStop, aJogStop })
        {
            Go(stop, a.turn, 0.25f, On("TurnInPlace"));
            Go(stop, a.move, 0.2f, On("Moving"));
            ExitTo(stop, a.idle, 0.85f);
        }
        Stop(a);

        // Crouched.
        Go(c.idle, c.move, 0.2f, On("Moving"));
        Stop(c);
        Go(ca.idle, caStart, 0.15f, On("Moving"), On("Forward"));
        Go(ca.idle, ca.move, 0.2f, On("Moving"));
        Go(caStart, ca.idle, 0.25f, Off("Moving"));
        Go(caStart, ca.move, 0.25f, Off("Forward"));
        StepInto(caStart, ca.move, crouchAimStart, crouchAimWalk);
        Go(ca.move, caStop, 0.2f, On("Stop"), On("Forward"), Above("StopSpeed", Speed(crouchAimWalk) * 0.6f));
        Go(caStop, ca.move, 0.2f, On("Moving"));
        ExitTo(caStop, ca.idle, 0.85f);
        Stop(ca);

        Go(hu.idle, hu.move, 0.2f, On("Moving"));
        Stop(hu);
        Go(hh.idle, hh.move, 0.2f, On("Moving"));
        Stop(hh);

        // Death first, so a pending hit or jump can't stand her back up.
        for (int i = 0; i < 4; i++)
        {
            AnimatorStateTransition die = machine.AddAnyStateTransition(deathStates[i]);
            die.duration = 0.1f;
            die.canTransitionToSelf = false;
            die.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            die.AddCondition(AnimatorConditionMode.Equals, i, "DeathDir");
        }
        AnimatorStateTransition flinch = machine.AddAnyStateTransition(hitState);
        flinch.duration = 0.05f;
        flinch.canTransitionToSelf = true;
        flinch.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
        ExitBy(hitState, stances, 0.85f);

        // Jumps: the start, the air, the landing, then back to whatever she was doing.
        for (int i = 0; i < jumpNames.Length; i++)
        {
            AnimatorStateTransition into = machine.AddAnyStateTransition(jumpStates[i, 0]);
            into.duration = 0.1f;
            into.canTransitionToSelf = false;
            into.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            into.AddCondition(AnimatorConditionMode.Equals, i, "JumpKind");
            ExitTo(jumpStates[i, 0], jumpStates[i, 1], 0.95f, 0.05f);
            Go(jumpStates[i, 1], jumpStates[i, 2], 0.05f, On("Grounded"));
            ExitBy(jumpStates[i, 2], stances, 0.55f);
        }

        BuildUpperBody(fire, crouchFire, reload, aimReload, crouchReload, draw, crouchDraw, holster, crouchHolster);

        // Aim offsets: single poses, turned into differences from looking straight ahead.
        AnimationClip lookCenter = PackClip(AimOffsets + "W1_Stand_Relaxed_Look_Center.fbx", motusMan);
        Motion standAim = AimOffsetTree("Stand Aim Offset", "Stand_Aim", motusMan);
        Motion crouchAim = AimOffsetTree("Crouch Aim Offset", "Crouch_Aim", motusMan);
        AddLayer("Aim Offset", Tree1D("Aim Offset", "CrouchBlend", (standAim, 0f, 1f, 0f), (crouchAim, 1f, 1f, 0f)), additive: true);
        AddLayer("Look Offset", Tree1D("Look Offset", "AimPitch",
            (Additive("Look Down", PackClip(AimOffsets + "W1_Stand_Relaxed_Look_D90.fbx", motusMan), lookCenter), -1f, 1f, 0f),
            (Additive("Look Center", lookCenter, lookCenter), 0f, 1f, 0f),
            (Additive("Look Up", PackClip(AimOffsets + "W1_Stand_Relaxed_Look_U90.fbx", motusMan), lookCenter), 1f, 1f, 0f)), additive: true);

        WriteProfile(walkSpeed, jogSpeed, aimWalkSpeed, aimJogSpeed, Speed(crouchWalk), Speed(crouchAimWalk), holsteredWalk, hurtWalk, hurtRun,
            (run, runLeft, runRight), (aimWalk, aimWalkCurveLeft, aimWalkCurveRight), (aimJog, aimJogCurveLeft, aimJogCurveRight), turnLeft, turnRight);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        controller = null;
    }

    // How long a jump's wind-up lasts before she leaves the ground. PlayerController waits as long.
    public const float JumpWindUp = 0.25f;

    // ---- Stances ----

    class StanceStates
    {
        readonly AnimatorStateMachine machine;
        public AnimatorState idle, turn, move;
        public readonly List<AnimatorState> all = new();

        public StanceStates(AnimatorStateMachine root, string name) => machine = root.AddStateMachine(name);

        public AnimatorState Add(string name, Motion motion, bool step = false, bool quickTurn = false)
        {
            AnimatorState state = AddState(machine, name, motion);
            // PlayerController moves her as far as these clips step, and turns her as far as these turn.
            if (step) state.tag = "Step";
            if (quickTurn) state.tag = "QuickTurn";
            all.Add(state);
            return state;
        }
    }

    // From any state of one stance to another stance, picking up where she is: moving or standing.
    static void StanceChanges(AnimatorState from, Dictionary<int, StanceStates> stances, int own, float duration)
    {
        foreach (var pair in stances)
        {
            if (pair.Key == own) continue;
            Go(from, pair.Value.move, duration, Is("Stance", pair.Key), On("Moving"));
            Go(from, pair.Value.idle, duration, Is("Stance", pair.Key), Off("Moving"));
        }
    }

    // Back into the current stance once a one-off clip has played this far.
    static void ExitBy(AnimatorState from, Dictionary<int, StanceStates> stances, float exitTime)
    {
        foreach (var pair in stances)
        {
            ExitTo(from, pair.Value.move, exitTime, 0.25f, Is("Stance", pair.Key), On("Moving"));
            ExitTo(from, pair.Value.idle, exitTime, 0.25f, Is("Stance", pair.Key), Off("Moving"));
        }
    }

    // Move fades back to Idle once she has slowed below half walking pace: lower down the tree would
    // mix the idle cycle into the walk, which is many times longer, and the feet would drift.
    static void Stop(StanceStates s) => Go(s.move, s.idle, 0.3f, Off("Moving"), Below("Travel", 0.5f));

    // A quarter turn on the spot, picked by TurnSide, from standing or from a turn already under way. It
    // plays twice as fast as recorded; PlayerController turns her body as far as the clip's root turns.
    static void QuickTurns(StanceStates s, (AnimatorState left, AnimatorState right) quick)
    {
        foreach ((AnimatorState state, int side) in new[] { (quick.left, -1), (quick.right, 1) })
        {
            state.speed = 2f;
            foreach (AnimatorState from in new[] { s.idle, s.turn })
            {
                AnimatorStateTransition into = Go(from, state, 0.15f, On("QuickTurn"), Is("TurnSide", side));
                // Before the other transitions out of these states.
                AnimatorStateTransition[] transitions = from.transitions;
                System.Array.Copy(transitions, 0, transitions, 1, transitions.Length - 1);
                transitions[0] = into;
                from.transitions = transitions;
            }
            Go(state, s.move, 0.2f, On("Moving"));
            ExitTo(state, s.turn, 0.9f, 0.25f, On("TurnInPlace"));
            ExitTo(state, s.idle, 0.9f, 0.25f);
        }
    }

    // A first step hands over to the walk when it reaches walking pace, mid-stride, measured from the clips.
    static void StepInto(AnimatorState step, AnimatorState move, AnimationClip stepClip, AnimationClip loop)
    {
        (float exit, float offset) = HandOver(stepClip, loop);
        ExitTo(step, move, exit, 0.15f).offset = offset;
    }

    // ---- Upper body and offsets ----

    static void BuildUpperBody(AnimationClip fire, AnimationClip crouchFire, AnimationClip reload, AnimationClip aimReload, AnimationClip crouchReload,
        AnimationClip draw, AnimationClip crouchDraw, AnimationClip holster, AnimationClip crouchHolster)
    {
        AnimatorStateMachine upper = AddLayer("Upper Body", null, additive: false);
        AnimatorState empty = upper.AddState("Empty");
        upper.defaultState = empty;

        Motion standReload = Tree1D("Stand Reload", "AimBlend", (reload, 0f, reload.length / ReloadTime, 0f), (aimReload, 1f, aimReload.length / ReloadTime, 0f));
        AnimatorState fireState = AddState(upper, "Fire", Tree1D("Fire", "CrouchBlend", (fire, 0f, 1f, 0f), (crouchFire, 1f, 1f, 0f)));
        AnimatorState reloadState = AddState(upper, "Reload", Tree1D("Reload", "CrouchBlend", (standReload, 0f, 1f, 0f), (crouchReload, 1f, crouchReload.length / ReloadTime, 0f)));
        AnimatorState drawState = AddState(upper, "Draw", Tree1D("Draw", "CrouchBlend", (draw, 0f, 1f, 0f), (crouchDraw, 1f, crouchDraw.length / draw.length, 0f)));
        AnimatorState holsterState = AddState(upper, "Holster", Tree1D("Holster", "CrouchBlend", (holster, 0f, 1f, 0f), (crouchHolster, 1f, crouchHolster.length / holster.length, 0f)));

        foreach ((AnimatorState state, string trigger, float exit) in new[] { (fireState, "Fire", 0.55f), (reloadState, "Reload", 0.92f), (drawState, "Draw", 0.9f), (holsterState, "Holster", 0.95f) })
        {
            AnimatorStateTransition into = upper.AddAnyStateTransition(state);
            into.duration = trigger == "Fire" ? 0.05f : 0.15f;
            into.canTransitionToSelf = trigger == "Fire";
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
            ExitTo(state, empty, exit);
        }
        AnimatorStateTransition stop = upper.AddAnyStateTransition(empty);
        stop.duration = 0.2f;
        stop.canTransitionToSelf = false;
        stop.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");
    }

    // Stand or crouch aim offsets as a 2D blend on where the camera looks against where she faces.
    static Motion AimOffsetTree(string name, string prefix, Avatar motusMan)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = "AimYaw",
            blendParameterY = "AimPitch",
            useAutomaticThresholds = false,
        };
        AnimationClip center = PackClip(AimOffsets + "W1_" + prefix + "_Point_Center.fbx", motusMan);
        (string pose, Vector2 at)[] poses = { ("Center", Vector2.zero), ("U90", Vector2.up), ("D90", Vector2.down), ("L90", Vector2.left), ("R90", Vector2.right) };
        foreach ((string pose, Vector2 at) in poses)
        {
            AnimationClip clip = PackClip(AimOffsets + "W1_" + prefix + "_Point_" + pose + ".fbx", motusMan);
            tree.AddChild(Additive(prefix.Replace("_", " ") + " " + pose, clip, center), at);
        }
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    // A copy of a pose that adds only its difference from `reference` (the pose looking straight
    // ahead), so an additive layer can turn the arms and head on top of whatever the body does.
    static AnimationClip Additive(string name, AnimationClip pose, AnimationClip reference)
    {
        string path = Generated + "/" + name + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(pose))
        {
            // The body stays where the base layer puts it; only the limbs turn.
            if (binding.propertyName.StartsWith("Root") || binding.propertyName.StartsWith("Motion")) continue;
            float value = AnimationUtility.GetEditorCurve(pose, binding).Evaluate(0f);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 1f / 30f, value));
        }
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAdditiveReferencePose(clip, reference, 0f);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorStateMachine AddLayer(string name, Motion motion, bool additive)
    {
        var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        if (motion != null)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = motion;
            machine.defaultState = state;
        }
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = name,
            stateMachine = machine,
            blendingMode = additive ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override,
            // PlayerController fades the offsets in and out.
            defaultWeight = additive ? 0f : 1f,
            avatarMask = UpperBodyMask(),
        });
        return machine;
    }

    // Spine, head, arms and hands; the legs and the body's travel stay with the base layer.
    static AvatarMask UpperBodyMask()
    {
        string path = Generated + "/Upper Body.mask";
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }
        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            bool upper = part is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head or AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm
                or AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers or AvatarMaskBodyPart.LeftHandIK or AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, upper);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    // ---- Blend trees ----

    // One gait in a speed blend: the clip from `from` to `to` m/s. A `from` of 0 means "where the
    // previous gait hands over, at the same cadence". With `left` and `right`, it leans into curves.
    readonly struct Gait
    {
        public readonly AnimationClip clip, left, right;
        public readonly float from, to, cycleOffset;

        public Gait(AnimationClip clip, float from, float to, AnimationClip left = null, AnimationClip right = null, float cycleOffset = 0f)
        {
            this.clip = clip;
            this.from = from;
            this.to = to;
            this.left = left;
            this.right = right;
            this.cycleOffset = cycleOffset;
        }
    }

    // A 1D blend on Speed in m/s. Each clip plays just fast enough that Eve's feet cover the speed it
    // sits at. Two gaits blend at the same cadence: the slower one speeds up to its top, and the
    // faster one takes over at the speed its longer stride gives at that cadence.
    static BlendTree GaitTree(string name, AnimationClip idle, params Gait[] gaits)
    {
        var children = new List<(Motion motion, float at, float timeScale, float cycleOffset)> { (idle, 0f, 1f, 0f) };
        float last = 0f;
        AnimationClip previous = null;
        foreach (Gait gait in gaits)
        {
            float own = Speed(gait.clip);
            float from = gait.from > 0f ? gait.from : previous != null ? last * Stride(gait.clip) / Stride(previous) : own;
            from = Mathf.Max(from, last + 0.05f);
            foreach (float at in gait.to > from + 0.01f ? new[] { from, gait.to } : new[] { from })
            {
                float timeScale = at / own;
                // Unity ignores the time scale of a child that is itself a blend tree, so a leaning
                // gait plays its clips at that speed inside the lean tree instead.
                if (gait.left != null)
                    children.Add((LeanTree(name + " Lean " + at.ToString("F2"), gait.left, gait.clip, gait.right, timeScale, gait.cycleOffset), at, 1f, 0f));
                else
                    children.Add((gait.clip, at, timeScale, gait.cycleOffset));
                last = at;
            }
            previous = gait.clip;
        }
        return Tree1D(name, "Speed", children.ToArray());
    }

    // Leaning into a curve: the straight clip at TurnRate 0 and each curve clip at the rate it turns,
    // in degrees per second, all played at `timeScale`.
    static BlendTree LeanTree(string name, AnimationClip left, AnimationClip straight, AnimationClip right, float timeScale = 1f, float cycleOffset = 0f) =>
        Tree1D(name, "TurnRate", (left, YawRate(left) * timeScale, timeScale, cycleOffset), (straight, 0f, timeScale, cycleOffset), (right, YawRate(right) * timeScale, timeScale, cycleOffset));

    // Turning in place on TurnRate in degrees per second. The loops step at their own rate at 1x and
    // play faster for faster turns, so the feet always keep up with the body.
    static BlendTree TurnTree(string name, AnimationClip left, AnimationClip right, AnimationClip idle)
    {
        float leftRate = YawRate(left), rightRate = YawRate(right);
        return Tree1D(name, "TurnRate",
            (left, -MaxTurnInPlaceRate, MaxTurnInPlaceRate / -leftRate, 0f),
            (left, leftRate, 1f, 0f),
            (idle, 0f, 1f, 0f),
            (right, rightRate, 1f, 0f),
            (right, MaxTurnInPlaceRate, MaxTurnInPlaceRate / rightRate, 0f));
    }

    // Strafing in every direction on (Strafe, Speed) in m/s, each clip at its own pace.
    static BlendTree DirectionalTree(string name, AnimationClip idle, params (Motion motion, Vector2 at)[] directions)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "Strafe",
            blendParameterY = "Speed",
            useAutomaticThresholds = false,
        };
        tree.AddChild(idle, Vector2.zero);
        foreach ((Motion motion, Vector2 at) in directions) tree.AddChild(motion, at);
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    static BlendTree Tree1D(string name, string parameter, params (Motion motion, float at, float timeScale, float cycleOffset)[] children)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = parameter,
            useAutomaticThresholds = false,
        };
        foreach (var child in children) tree.AddChild(child.motion, child.at);
        // No minThreshold/maxThreshold here: setting them spreads the thresholds evenly again.
        ChildMotion[] motions = tree.children;
        for (int i = 0; i < children.Length; i++)
        {
            motions[i].timeScale = children[i].timeScale;
            motions[i].cycleOffset = children[i].cycleOffset;
        }
        tree.children = motions;
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    // ---- Clip measurements ----

    // How far the body travels in one cycle of the clip, for a human of scale 1. The clips were made
    // moving with the feet planted, so this is the ground each cycle covers. Read from the body's
    // path, because the averaged root motion depends on how the root rotation is baked.
    static float Stride(AnimationClip clip)
    {
        Vector3 start = RootPosition(clip, 0f), end = RootPosition(clip, clip.length);
        return new Vector2(end.x - start.x, end.z - start.z).magnitude;
    }

    // Speed along the ground on Eve, in m/s. Humanoid clips store their motion for a human of scale 1;
    // Eve is smaller, so each of her steps covers less ground than the clip's own figure.
    static float Speed(AnimationClip clip) => clip.length > 0f ? Stride(clip) / clip.length * scale : 0f;

    // A curve clip moves along an arc, whose path is longer than the straight line between its ends.
    static float ArcSpeed(AnimationClip clip)
    {
        float half = Mathf.Abs(YawRate(clip) * clip.length) * 0.5f * Mathf.Deg2Rad;
        return half > 0.001f ? Speed(clip) * half / Mathf.Sin(half) : Speed(clip);
    }

    // How fast the clip turns its body, in degrees per second (positive is to the right).
    static float YawRate(AnimationClip clip) => Mathf.DeltaAngle(RootYaw(clip, 0f), RootYaw(clip, clip.length)) / clip.length;

    static Vector3 RootPosition(AnimationClip clip, float time) =>
        new(Curve(clip, "RootT.x")?.Evaluate(time) ?? 0f, Curve(clip, "RootT.y")?.Evaluate(time) ?? 0f, Curve(clip, "RootT.z")?.Evaluate(time) ?? 0f);

    static float RootYaw(AnimationClip clip, float time) =>
        new Quaternion(Curve(clip, "RootQ.x").Evaluate(time), Curve(clip, "RootQ.y").Evaluate(time), Curve(clip, "RootQ.z").Evaluate(time), Curve(clip, "RootQ.w").Evaluate(time)).eulerAngles.y;

    static AnimationCurve Curve(AnimationClip clip, string property)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.propertyName == property) return AnimationUtility.GetEditorCurve(clip, binding);
        }
        return null;
    }

    // Where a first step should hand over to the walk: the moment it reaches nine tenths of the walk's
    // pace, and the point of the walk cycle whose legs match the step's legs at that moment.
    static (float exit, float offset) HandOver(AnimationClip step, AnimationClip loop)
    {
        float loopSpeed = Stride(loop) / loop.length;
        float exit = 0.8f;
        for (float n = 0.45f; n <= 0.9f; n += 0.01f)
        {
            float t = n * step.length;
            Vector3 travel = RootPosition(step, t + 0.05f) - RootPosition(step, t - 0.05f);
            if (new Vector2(travel.x, travel.z).magnitude / 0.1f >= loopSpeed * 0.9f)
            {
                exit = n;
                break;
            }
        }
        const string left = "Left Upper Leg Front-Back", right = "Right Upper Leg Front-Back";
        AnimationCurve stepLeft = Curve(step, left), stepRight = Curve(step, right), loopLeft = Curve(loop, left), loopRight = Curve(loop, right);
        float te = exit * step.length;
        float a = stepLeft.Evaluate(te), b = stepRight.Evaluate(te), direction = a - stepLeft.Evaluate(te - 0.03f);
        float best = 0f, bestError = float.MaxValue;
        for (int i = 0; i < 200; i++)
        {
            float tl = loop.length * i / 200f;
            float error = Mathf.Abs(loopLeft.Evaluate(tl) - a) + Mathf.Abs(loopRight.Evaluate(tl) - b);
            if (direction * (loopLeft.Evaluate(tl) - loopLeft.Evaluate(tl - 0.03f)) < 0f) error += 0.5f;
            if (error < bestError)
            {
                bestError = error;
                best = i / 200f;
            }
        }
        return (exit, best);
    }

    static void WriteProfile(float walk, float jog, float aimWalk, float aimJog, float crouchWalk, float crouchAimWalk, float holsteredWalk, float hurtWalk, float hurtRun,
        (AnimationClip straight, AnimationClip left, AnimationClip right) run, (AnimationClip straight, AnimationClip left, AnimationClip right) aimWalkTurn,
        (AnimationClip straight, AnimationClip left, AnimationClip right) aimJogTurn, AnimationClip turnLeft, AnimationClip turnRight)
    {
        LocomotionProfile profile = AssetDatabase.LoadAssetAtPath<LocomotionProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<LocomotionProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        profile.walk = walk;
        profile.jog = jog;
        profile.run = SprintSpeed;
        profile.aimWalk = aimWalk;
        profile.aimJog = aimJog;
        profile.crouchWalk = crouchWalk;
        profile.crouchAimWalk = crouchAimWalk;
        profile.holsteredWalk = holsteredWalk;
        profile.holsteredRun = SprintSpeed;
        profile.hurtWalk = hurtWalk;
        profile.hurtRun = hurtRun;
        profile.turnInPlaceRate = (YawRate(turnRight) - YawRate(turnLeft)) / 2f;
        profile.maxTurnInPlaceRate = MaxTurnInPlaceRate;
        profile.runTurn = Lean(run, SprintSpeed / Speed(run.straight));
        profile.aimWalkTurn = Lean(aimWalkTurn, 1f);
        profile.aimJogTurn = Lean(aimJogTurn, 1f);
        EditorUtility.SetDirty(profile);
    }

    // How fast a leaning curve turns at the gait's top speed, and how much of the straight speed it keeps.
    static LocomotionProfile.LeanTurn Lean((AnimationClip straight, AnimationClip left, AnimationClip right) clips, float timeScale) => new()
    {
        rate = (YawRate(clips.right) - YawRate(clips.left)) / 2f * timeScale,
        speedShare = (ArcSpeed(clips.left) + ArcSpeed(clips.right)) / 2f / Speed(clips.straight),
    };

    // ---- Importing ----

    // MotusMan is the skeleton most of the pack was recorded on; its avatar retargets those clips.
    static Avatar PrepareMotusMan()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(MotusManPath);
        if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(MotusManPath))
        {
            if (asset is Avatar avatar) return avatar;
        }
        throw new InvalidOperationException("MotusMan_v55 has no avatar");
    }

    // Some clips on the mannequin's skeleton still put Eve's feet too low with the height taken from the
    // feet. This is the importer's height offset that lifts them back (negative raises the body; one
    // unit is about 0.92 m on Eve), measured in Play mode as the lower ankle's mean height against the
    // MotusMan clips of the same stance. The relaxed jog needs none.
    static readonly Dictionary<string, float> MannequinLift = new()
    {
        { "W1_CrouchWalk_F_Loop", -0.083f },
    };

    // A pack clip, imported as Humanoid. Some files are on the Unreal mannequin's skeleton instead of
    // MotusMan's: they get their own avatar and take the body's height from the feet, because that
    // skeleton's proportions would lift her off the floor. Turns keep their yaw out of the pose.
    static AnimationClip PackClip(string path, Avatar motusMan, bool loop = false, bool turn = false)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer) throw new InvalidOperationException("Missing " + path);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        bool mannequin = model != null && HasChild(model.transform, "pelvis");
        bool changed = importer.animationType != ModelImporterAnimationType.Human;
        importer.animationType = ModelImporterAnimationType.Human;
        if (mannequin)
        {
            changed |= importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }
        else
        {
            changed |= importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != motusMan;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = motusMan;
        }
        if (changed) importer.SaveAndReimport();
        MannequinLift.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(path), out float lift);
        SetClipSettings(importer, loop, bakeRotation: !turn, fromFeet: mannequin, lift);
        return Load(path);
    }

    static bool HasChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name || HasChild(child, name)) return true;
        }
        return false;
    }

    static string PrepareMixamo(string path, bool loop, bool bakeRotation)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return path;
        if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }
        SetClipSettings(importer, loop, bakeRotation, fromFeet: false);
        return path;
    }

    // Root motion is off for the body, so whatever a clip leaves as root motion is thrown away.
    // The hips' rise and fall has to stay in the pose, or the legs bend while the body glides level
    // and the feet sink into the floor. Forward travel stays root motion and is dropped, because
    // PlayerController moves the body.
    static void SetClipSettings(ModelImporter importer, bool loop, bool bakeRotation, bool fromFeet, float lift = 0f)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = false;
        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.loopTime == loop && clip.lockRootHeightY && clip.keepOriginalPositionY == !fromFeet && clip.heightFromFeet == fromFeet
                && clip.lockRootRotation == bakeRotation && (!bakeRotation || clip.keepOriginalOrientation) && Mathf.Approximately(clip.heightOffset, lift))
                continue;
            clip.heightOffset = lift;
            clip.loopTime = loop;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = !fromFeet;
            clip.heightFromFeet = fromFeet;
            clip.lockRootRotation = bakeRotation;
            if (bakeRotation) clip.keepOriginalOrientation = true;
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
        throw new InvalidOperationException("No animation clip in " + path);
    }

    static float HumanScale()
    {
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        float humanScale = model.GetComponent<Animator>().humanScale;
        Object.DestroyImmediate(model);
        return humanScale;
    }

    // ---- Controller plumbing ----

    static void Clear(AnimatorController target)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
        {
            if (asset != target) Object.DestroyImmediate(asset, true);
        }
        target.layers = new AnimatorControllerLayer[0];
        target.parameters = new AnimatorControllerParameter[0];
        target.AddLayer("Base Layer");
    }

    static void AddParameters()
    {
        foreach (string name in new[] { "Speed", "Strafe", "Travel", "StopSpeed", "TurnRate", "AimPitch", "AimYaw", "AimBlend", "CrouchBlend", "IdleTime" })
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        foreach (string name in new[] { "IsAiming", "Danger", "Moving", "Sprint", "Forward", "TurnInPlace", "Grounded", "IsDead" })
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        foreach (string name in new[] { "Stance", "JumpKind", "DeathDir", "TurnSide" })
            controller.AddParameter(name, AnimatorControllerParameterType.Int);
        foreach (string name in new[] { "Fire", "Reload", "Draw", "Holster", "Jump", "Stop", "QuickTurn", "Hit", "Dead" })
            controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        AnimatorControllerParameter[] parameters = controller.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.name == "Stance") parameter.defaultInt = Relaxed;
            if (parameter.name == "Grounded") parameter.defaultBool = true;
        }
        controller.parameters = parameters;
    }

    static AnimatorState AddState(AnimatorStateMachine machine, string name, Motion motion, float speed = 1f)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = motion;
        state.speed = speed;
        return state;
    }

    static (AnimatorConditionMode mode, float threshold, string parameter) On(string parameter) => (AnimatorConditionMode.If, 0f, parameter);
    static (AnimatorConditionMode mode, float threshold, string parameter) Off(string parameter) => (AnimatorConditionMode.IfNot, 0f, parameter);
    static (AnimatorConditionMode mode, float threshold, string parameter) Is(string parameter, int value) => (AnimatorConditionMode.Equals, value, parameter);
    static (AnimatorConditionMode mode, float threshold, string parameter) Above(string parameter, float value) => (AnimatorConditionMode.Greater, value, parameter);
    static (AnimatorConditionMode mode, float threshold, string parameter) Below(string parameter, float value) => (AnimatorConditionMode.Less, value, parameter);

    static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        foreach (var (mode, threshold, parameter) in conditions) transition.AddCondition(mode, threshold, parameter);
        return transition;
    }

    static AnimatorStateTransition ExitTo(AnimatorState from, AnimatorState to, float exitTime, float duration = 0.3f, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
    {
        AnimatorStateTransition transition = Go(from, to, duration, conditions);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        return transition;
    }
}
