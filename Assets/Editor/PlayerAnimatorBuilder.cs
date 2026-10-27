using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// Phase 2: turns the clips into the player's Animator, the way the MocapOnline "Pistol Basic 27A"
// pack is made to be used.
//
// The model is picked in the Player Character asset (see PlayerCharacterSetup); by default it is the
// pack's own MotusMan, the skeleton the clips were recorded on. Every clip plays at the speed it was
// recorded at, except the turn-in-place loops (TurnInPlaceSpeed), and the pack's root motion moves
// her: PlayerController hands each frame's animated step to the CharacterController, so her feet
// carry her exactly as far as they step. Turns on the spot and the quarter turns turn her by their
// own root rotation. On the move PlayerController steers her toward the stick, and the pack's circle
// loops lean her into the curve.
//
// The pack only moves with the pistol in hand: its holster and draw clips connect to the "MOB stand"
// of MocapOnline's separate Mobility pack. Holstered, she stands in that pose (the last frame of the
// holster clip, on the upper body); as soon as she moves, PlayerController draws the pistol.
//
// Fire, reload, draw and holster play on an upper-body layer, so she can do them on the move; two
// additive layers point the pistol, or the head, where the camera looks.
public static class PlayerAnimatorBuilder
{
    // The model from the Player Character asset.
    public static string ModelPath => Setup.model != null ? AssetDatabase.GetAssetPath(Setup.model) : "";
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
    // The pack's files on the Unreal mannequin's skeleton share the avatar of this one.
    const string MannequinSource = Loops + "W1_Jog_F_Loop.fbx";
    const string Generated = "Assets/Characters/Player/Generated";

    // Gaits in the movement blends, as the MoveX/MoveY radius PlayerController asks for: the clips
    // themselves set how fast each one goes.
    public const float WalkGait = 1f, JogGait = 2f, RunGait = 3f;
    // The turn-in-place loops step 45 degrees in 0.67 s, so a half turn takes 2.7 s at their own
    // pace. They play this much faster; the feet still step the whole turn.
    public const float TurnInPlaceSpeed = 2f;
    // The continuous-fire loops fire four rounds a cycle; holding the trigger fires at that pace.
    const int RapidShotsPerLoop = 4;
    // A hit is the first 20 frames of falling backward: she staggers, and catches herself before the fall.
    const int HitFrames = 20;

    // Stances, as the Stance parameter that PlayerController sets.
    public const int Relaxed = 1, Aim = 2, Crouch = 3, CrouchAim = 4, Hurt = 5;
    // Jumps, as the JumpKind parameter.
    public const int JumpStand = 0, JumpRun = 1, JumpAimStand = 2, JumpAimForward = 3, JumpAimLeft = 4, JumpAimRight = 5;

    // The pack holsters the pistol in the middle of the back ("MOB"). This is where its holster clip
    // lets go of it and its draw clip takes hold, in MotusMan's hips, measured at the pack's holster
    // frames (0.63 s and 0.23 s; the two agree within 5 mm), so the pistol never jumps.
    const string HolsterBone = "Hips";
    static readonly Vector3 HolsterPosition = new(-0.089f, 0.098f, 0.145f), HolsterRotation = new(355.5f, 96.5f, 151f);

    static AnimatorController controller;
    static Avatar motusMan, mannequin;
    // True when the shared mannequin avatar was just rebuilt, so the files copying it import again.
    static bool mannequinRebuilt;

    // The Player Character asset, created with the defaults the first time it is needed.
    public static PlayerCharacterSetup Setup
    {
        get
        {
            var setup = AssetDatabase.LoadAssetAtPath<PlayerCharacterSetup>(PlayerCharacterSetup.Path);
            if (setup != null) return setup;
            // MotusMan, the pack's own model: the clips play on him exactly as recorded, so his feet
            // need no limits, and Unity's Foot IK puts them where the actor's were.
            setup = ScriptableObject.CreateInstance<PlayerCharacterSetup>();
            setup.model = AssetDatabase.LoadAssetAtPath<GameObject>(MotusManPath);
            // MotusMan is 1.84 m tall; at 0.94 he stands 1.73 m, the height the level and camera were made for.
            setup.modelScale = 0.94f;
            setup.limitFeet = false;
            setup.stateFootIK = true;
            // HomebrewIK's foot placement bent his ankles 7 degrees further at a sprint on flat ground;
            // it stays an option in the asset for levels with many steps and slopes.
            setup.footIK = false;
            setup.handgun = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPath);
            // The pack's weapon joint in the right hand: the 1911 sits in the grip with no offset.
            setup.handSocketParent = "hand_r_wep";
            setup.holsterSocketParent = HolsterBone;
            setup.holsterPosition = HolsterPosition;
            setup.holsterRotation = HolsterRotation;
            AssetDatabase.CreateAsset(setup, PlayerCharacterSetup.Path);
            return setup;
        }
    }

    [MenuItem("Dino Demo/Build Player Animator")]
    public static void Build()
    {
        PlayerCharacterSetup setup = Setup;
        if (setup.model == null) throw new InvalidOperationException("The Player Character asset has no model: " + PlayerCharacterSetup.Path);
        string avatarPath = PrepareModel(setup);
        if (setup.limitFeet) LimitFeet(avatarPath, setup.footUpLimit, setup.footTwistLimit);
        motusMan = PrepareMotusMan();
        mannequin = PrepareMannequin();
        if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder("Assets/Characters/Player", "Generated");
        // The arm clips an earlier build made for the holstered stance.
        if (AssetDatabase.IsValidFolder(Generated + "/Unarmed Arms")) AssetDatabase.DeleteAsset(Generated + "/Unarmed Arms");
        AssetDatabase.DeleteAsset(Generated + "/Arms.mask");

        // Standing with the pistol down. Straight loops keep their turn out of the root motion; turns
        // on the spot, quarter turns and circles turn by it.
        AnimationClip idle = PackClip(Loops + "W1_Stand_Relaxed_Idle.fbx", loop: true);
        AnimationClip fidget = PackClip(RootMotion + "W1_Stand_Relaxed_Fgt_v2.fbx");
        AnimationClip walk = PackClip(Loops + "W1_Walk_F_Loop.fbx", loop: true);
        AnimationClip walkBack = PackClip(Loops + "W1_Walk_B_Loop.fbx", loop: true);
        AnimationClip walkLeft = PackClip(Loops + "W1_Walk_L_Loop.fbx", loop: true);
        AnimationClip walkRight = PackClip(Loops + "W1_Walk_R_Loop.fbx", loop: true);
        AnimationClip jog = PackClip(Loops + "W1_Jog_F_Loop.fbx", loop: true);
        AnimationClip jogBack = PackClip(Loops + "W1_Jog_B_Loop.fbx", loop: true);
        AnimationClip jogLeft = PackClip(Loops + "W1_Jog_L_Loop.fbx", loop: true);
        AnimationClip jogRight = PackClip(Loops + "W1_Jog_R_Loop.fbx", loop: true);
        AnimationClip run = PackClip(Loops + "W1_Run_F_Loop.fbx", loop: true);
        AnimationClip runCurveLeft = PackClip(Loops + "W1_Run_L_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip runCurveRight = PackClip(Loops + "W1_Run_R_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip runLeft = PackClip(Loops + "W1_Run_L_Loop.fbx", loop: true);
        AnimationClip runRight = PackClip(Loops + "W1_Run_R_Loop.fbx", loop: true);
        AnimationClip runStop = PackClip(RootMotion + "W1_Run_F_to_Stand_Relaxed_RU.fbx");
        AnimationClip turnLeft = PackClip(Loops + "W1_Stand_Rlx_Turn_In_Place_L_Loop.fbx", loop: true, turn: true);
        AnimationClip turnRight = PackClip(Loops + "W1_Stand_Rlx_Turn_In_Place_R_Loop.fbx", loop: true, turn: true);
        AnimationClip toAim = PackClip(RootMotion + "W1_Stand_Relaxed_To_Aim.fbx");
        AnimationClip toRelaxed = PackClip(RootMotion + "W1_Stand_Aim_To_Relaxed.fbx");

        AnimationClip aimIdle = PackClip(Loops + "W1_Stand_Aim_Idle.fbx", loop: true);
        AnimationClip aimFidget = PackClip(RootMotion + "W1_Stand_Aim_Fgt_v1.fbx");
        AnimationClip aimWalk = PackClip(Loops + "W1_Walk_Aim_F_Loop.fbx", loop: true);
        AnimationClip aimWalkBack = PackClip(Loops + "W1_Walk_Aim_B_Loop.fbx", loop: true);
        AnimationClip aimWalkLeft = PackClip(Loops + "W1_Walk_Aim_L_Loop.fbx", loop: true);
        AnimationClip aimWalkRight = PackClip(Loops + "W1_Walk_Aim_R_Loop.fbx", loop: true);
        AnimationClip aimWalkCurveLeft = PackClip(Loops + "W1_Walk_Aim_L_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip aimWalkCurveRight = PackClip(Loops + "W1_Walk_Aim_R_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip aimJog = PackClip(Loops + "W1_Jog_Aim_F_Loop.fbx", loop: true);
        AnimationClip aimJogBack = PackClip(Loops + "W1_Jog_Aim_B_Loop.fbx", loop: true);
        AnimationClip aimJogLeft = PackClip(Loops + "W1_Jog_Aim_L_Loop.fbx", loop: true);
        AnimationClip aimJogRight = PackClip(Loops + "W1_Jog_Aim_R_Loop.fbx", loop: true);
        AnimationClip aimJogCurveLeft = PackClip(Loops + "W1_Jog_Aim_L_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip aimJogCurveRight = PackClip(Loops + "W1_Jog_Aim_R_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip aimTurnLeft = PackClip(Loops + "W1_Stand_Aim_Turn_In_Place_L_Loop.fbx", loop: true, turn: true);
        AnimationClip aimTurnRight = PackClip(Loops + "W1_Stand_Aim_Turn_In_Place_R_Loop.fbx", loop: true, turn: true);
        AnimationClip aimStart = PackClip(RootMotion + "W1_Stand_Aim_To_Walk_Aim_F.fbx");
        AnimationClip aimWalkStop = PackClip(RootMotion + "W1_Walk_Aim_F_To_Stand_Aim_RU.fbx");
        AnimationClip aimJogStop = PackClip(RootMotion + "W1_Jog_Aim_F_to_Stand_Aim_RU.fbx");
        AnimationClip aimToCrouchAim = PackClip(RootMotion + "W1_Stand_Aim_To_Crouch_Aim.fbx");
        AnimationClip aimQuickLeft = PackClip(RootMotion + "W1_Stand_Aim_L_90.fbx", turn: true);
        AnimationClip aimQuickRight = PackClip(RootMotion + "W1_Stand_Aim_R_90.fbx", turn: true);

        AnimationClip crouchIdle = PackClip(Loops + "W1_Crouch_Idle.fbx", loop: true);
        AnimationClip crouchWalk = PackClip(Loops + "W1_CrouchWalk_F_Loop.fbx", loop: true);
        AnimationClip crouchBack = PackClip(Loops + "W1_CrouchWalk_B_Loop.fbx", loop: true);
        AnimationClip crouchLeft = PackClip(Loops + "W1_CrouchWalk_L_Loop.fbx", loop: true);
        AnimationClip crouchRight = PackClip(Loops + "W1_CrouchWalk_R_Loop.fbx", loop: true);
        AnimationClip crouchTurnLeft = PackClip(Loops + "W1_Crouch_Rlx_Turn_In_Place_L_Loop.fbx", loop: true, turn: true);
        AnimationClip crouchTurnRight = PackClip(Loops + "W1_Crouch_Rlx_Turn_In_Place_R_Loop.fbx", loop: true, turn: true);
        AnimationClip crouchToCrouchAim = PackClip(RootMotion + "W1_Crouch_To_Crouch_Aim.fbx");
        AnimationClip crouchAimIdle = PackClip(Loops + "W1_Crouch_Aim_Idle.fbx", loop: true);
        AnimationClip crouchAimWalk = PackClip(Loops + "W1_CrouchWalk_Aim_F_Loop.fbx", loop: true);
        AnimationClip crouchAimBack = PackClip(Loops + "W1_CrouchWalk_Aim_B_Loop.fbx", loop: true);
        AnimationClip crouchAimLeft = PackClip(Loops + "W1_CrouchWalk_Aim_L_Loop.fbx", loop: true);
        AnimationClip crouchAimRight = PackClip(Loops + "W1_CrouchWalk_Aim_R_Loop.fbx", loop: true);
        AnimationClip crouchAimTurnLeft = PackClip(Loops + "W1_Crouch_Aim_Turn_In_Place_L_Loop.fbx", loop: true, turn: true);
        AnimationClip crouchAimTurnRight = PackClip(Loops + "W1_Crouch_Aim_Turn_In_Place_R_Loop.fbx", loop: true, turn: true);
        AnimationClip crouchAimStart = PackClip(RootMotion + "W1_Crouch_Aim_To_CrouchWalk_Aim_F.fbx");
        AnimationClip crouchAimStop = PackClip(RootMotion + "W1_CrouchWalk_Aim_F_To_Crouch_Aim_RU.fbx");
        AnimationClip crouchAimToCrouch = PackClip(RootMotion + "W1_Crouch_Aim_To_Crouch.fbx");
        AnimationClip crouchAimToAim = PackClip(RootMotion + "W1_Crouch_Aim_To_Stand_Aim.fbx");
        AnimationClip crouchQuickLeft = PackClip(RootMotion + "W1_Crouch_Aim_L_90.fbx", turn: true);
        AnimationClip crouchQuickRight = PackClip(RootMotion + "W1_Crouch_Aim_R_90.fbx", turn: true);

        var upper = new UpperBodyClips
        {
            fire = PackClip(HolsterAndFire + "W1_Stand_Fire_Single.fbx"),
            crouchFire = PackClip(HolsterAndFire + "W1_Crouch_Fire_Single.fbx"),
            rapidFire = PackClip(HolsterAndFire + "W1_Stand_Fire_Continuous.fbx", loop: true),
            crouchRapidFire = PackClip(HolsterAndFire + "W1_Crouch_Fire_Continuous.fbx", loop: true),
            reload = PackClip(HolsterAndFire + "W1_Stand_Relaxed_Reload.fbx"),
            aimReload = PackClip(HolsterAndFire + "W1_Stand_Aim_Reload.fbx"),
            crouchReload = PackClip(HolsterAndFire + "W1_Crouch_Aim_Reload.fbx"),
            draw = PackClip(HolsterAndFire + "W1_Aim_Equip_Get_From_MOB.fbx"),
            crouchDraw = PackClip(HolsterAndFire + "W1_Crouch_Aim_Equip_Get_From_MOB.fbx"),
            holster = PackClip(HolsterAndFire + "W1_Aim_Equip_Return_To_MOB.fbx"),
            crouchHolster = PackClip(HolsterAndFire + "W1_Crouch_Aim_Equip_Return_To_MOB.fbx"),
            toSecond = PackClip(HolsterAndFire + "W1_Stand_Aim_Swap_To_W2.fbx"),
            crouchToSecond = PackClip(HolsterAndFire + "W1_Crouch_Aim_Swap_To_W2.fbx"),
            toPistol = PackClip(HolsterAndFire + "W2_Stand_Aim_Swap_To_W1.fbx"),
            crouchToPistol = PackClip(HolsterAndFire + "W2_Crouch_Aim_Swap_To_W1.fbx"),
        };
        // The MOB stand and crouch: the holster clips' last frames, standing and crouched with empty hands.
        upper.mobStand = PackTake(HolsterAndFire + "W1_Aim_Equip_Return_To_MOB.fbx", "W1_MOB_Stand", last: true, frames: 1);
        upper.mobCrouch = PackTake(HolsterAndFire + "W1_Crouch_Aim_Equip_Return_To_MOB.fbx", "W1_MOB_Crouch", last: true, frames: 1);

        string[] deathFiles = { "W1_Stand_Relaxed_Death_F", "W1_Stand_Relaxed_Death_B", "W1_Stand_Relaxed_Death_L", "W1_Stand_Relaxed_Death_R" };
        var deaths = new AnimationClip[4];
        for (int i = 0; i < 4; i++) deaths[i] = PackClip(RootMotion + deathFiles[i] + ".fbx");
        AnimationClip hit = PackTake(RootMotion + "W1_Stand_Relaxed_Death_B.fbx", "W1_Stand_Relaxed_Hit", last: false, frames: HitFrames);

        // Split jumps: the start, a loop in the air, the landing. The pack has them only in place;
        // PlayerController lifts her off when the start clip ends and carries her speed through the air.
        string[] jumpNames = { "W1_Stand_Relaxed_Jump", "W1_Run_F_Jump_RU", "W1_Stand_Aim_Jump", "W1_Walk_Aim_F_Jump_RU", "W1_Jog_Aim_L_Jump_RU", "W1_Jog_Aim_R_Jump_RU" };
        var jumps = new AnimationClip[jumpNames.Length, 3];
        for (int i = 0; i < jumpNames.Length; i++)
        {
            jumps[i, 0] = PackClip(SplitJumps + jumpNames[i] + "_Start_IPC.fbx");
            jumps[i, 1] = PackClip(SplitJumps + jumpNames[i] + "_Air_IPC.fbx", loop: true);
            jumps[i, 2] = PackClip(SplitJumps + jumpNames[i] + "_End_IPC.fbx");
        }

        // Rebuild in place: deleting the asset would give it a new GUID and leave the scene's Animator empty.
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else Clear(controller);
        AddParameters();

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var stances = new Dictionary<int, StanceStates>();

        // Moving on (MoveX, MoveY): walk at 1, jog at 2, run at 3 in any direction the pack has. The
        // run, and aiming the walk and jog, lean into a curve on Turn.
        BlendTree freeMove = DirectionalTree("Free Move", idle,
            (walk, new Vector2(0f, WalkGait)),
            (jog, new Vector2(0f, JogGait)),
            (LeanTree("Run Lean", runCurveLeft, run, runCurveRight), new Vector2(0f, RunGait)),
            (walkBack, new Vector2(0f, -WalkGait)),
            (jogBack, new Vector2(0f, -JogGait)),
            (walkLeft, new Vector2(-WalkGait, 0f)),
            (jogLeft, new Vector2(-JogGait, 0f)),
            (runLeft, new Vector2(-RunGait, 0f)),
            (walkRight, new Vector2(WalkGait, 0f)),
            (jogRight, new Vector2(JogGait, 0f)),
            (runRight, new Vector2(RunGait, 0f)));

        // Relaxed (pistol down) and badly hurt move alike; hurt, PlayerController holds her to a jog.
        // A run ends in a last stride; standing still a while, she looks around.
        foreach ((int id, string name) in new[] { (Relaxed, "Relaxed"), (Hurt, "Hurt") })
        {
            var s = stances[id] = new StanceStates(machine, name);
            s.idle = s.Add("Idle", idle);
            s.move = s.Add("Move", freeMove, "Move");
            (s.turnLeft, s.turnRight) = (s.Add("Turn Left", turnLeft, "Turn"), s.Add("Turn Right", turnRight, "Turn"));
            s.stop = s.Add("Stop", runStop, "Move");
            s.fidget = s.Add("Look Around", fidget);
        }

        // Aiming: strafes in every direction; walking and jogging forward lean into curves.
        var a = stances[Aim] = new StanceStates(machine, "Aim");
        a.idle = a.Add("Idle", aimIdle);
        a.move = a.Add("Move", DirectionalTree("Aim Move", aimIdle,
            (LeanTree("Aim Walk Lean", aimWalkCurveLeft, aimWalk, aimWalkCurveRight), new Vector2(0f, WalkGait)),
            (LeanTree("Aim Jog Lean", aimJogCurveLeft, aimJog, aimJogCurveRight), new Vector2(0f, JogGait)),
            (aimWalkBack, new Vector2(0f, -WalkGait)),
            (aimJogBack, new Vector2(0f, -JogGait)),
            (aimWalkLeft, new Vector2(-WalkGait, 0f)),
            (aimJogLeft, new Vector2(-JogGait, 0f)),
            (aimWalkRight, new Vector2(WalkGait, 0f)),
            (aimJogRight, new Vector2(JogGait, 0f))), "Move");
        (a.turnLeft, a.turnRight) = (a.Add("Turn Left", aimTurnLeft, "Turn"), a.Add("Turn Right", aimTurnRight, "Turn"));
        (a.quickLeft, a.quickRight) = (a.Add("Quick Turn Left", aimQuickLeft, "Turn"), a.Add("Quick Turn Right", aimQuickRight, "Turn"));
        a.fidget = a.Add("Look Around", aimFidget);
        a.start = a.Add("First Step", aimStart, "Move");
        AnimatorState aWalkStop = a.Add("Walk Stop", aimWalkStop, "Move");
        AnimatorState aJogStop = a.Add("Jog Stop", aimJogStop, "Move");

        // Crouched: creeps in every direction, the pistol down.
        var c = stances[Crouch] = new StanceStates(machine, "Crouch");
        c.idle = c.Add("Idle", crouchIdle);
        c.move = c.Add("Move", DirectionalTree("Crouch Move", crouchIdle,
            (crouchWalk, new Vector2(0f, WalkGait)), (crouchBack, new Vector2(0f, -WalkGait)),
            (crouchLeft, new Vector2(-WalkGait, 0f)), (crouchRight, new Vector2(WalkGait, 0f))), "Move");
        (c.turnLeft, c.turnRight) = (c.Add("Turn Left", crouchTurnLeft, "Turn"), c.Add("Turn Right", crouchTurnRight, "Turn"));

        // Crouched and aiming.
        var ca = stances[CrouchAim] = new StanceStates(machine, "Crouch Aim");
        ca.idle = ca.Add("Idle", crouchAimIdle);
        ca.move = ca.Add("Move", DirectionalTree("Crouch Aim Move", crouchAimIdle,
            (crouchAimWalk, new Vector2(0f, WalkGait)), (crouchAimBack, new Vector2(0f, -WalkGait)),
            (crouchAimLeft, new Vector2(-WalkGait, 0f)), (crouchAimRight, new Vector2(WalkGait, 0f))), "Move");
        (ca.turnLeft, ca.turnRight) = (ca.Add("Turn Left", crouchAimTurnLeft, "Turn"), ca.Add("Turn Right", crouchAimTurnRight, "Turn"));
        (ca.quickLeft, ca.quickRight) = (ca.Add("Quick Turn Left", crouchQuickLeft, "Turn"), ca.Add("Quick Turn Right", crouchQuickRight, "Turn"));
        ca.start = ca.Add("First Step", crouchAimStart, "Move");
        AnimatorState caStop = ca.Add("Stop", crouchAimStop, "Move");

        foreach (StanceStates s in stances.Values)
        {
            foreach (AnimatorState turn in new[] { s.turnLeft, s.turnRight }) turn.speed = TurnInPlaceSpeed;
            // Unity's Foot IK puts the feet where the clip's flat-footed actor put his; on heels that
            // bends the ankles, so it is a choice in the Player Character asset.
            foreach (AnimatorState state in s.all) state.iKOnFeet = setup.stateFootIK;
        }

        // Raising or lowering the pistol from a stand, and crouching or standing up with it raised.
        AnimatorStateMachine changes = machine.AddStateMachine("Stance Changes");
        AnimatorState raise = AddState(changes, "Relaxed To Aim", toAim);
        AnimatorState lower = AddState(changes, "Aim To Relaxed", toRelaxed);
        AnimatorState aimDown = AddState(changes, "Aim To Crouch Aim", aimToCrouchAim);
        AnimatorState aimUp = AddState(changes, "Crouch Aim To Aim", crouchAimToAim);
        AnimatorState crouchRaise = AddState(changes, "Crouch To Crouch Aim", crouchToCrouchAim);
        AnimatorState crouchLower = AddState(changes, "Crouch Aim To Crouch", crouchAimToCrouch);

        AnimatorStateMachine air = machine.AddStateMachine("Jump");
        var jumpStates = new AnimatorState[jumpNames.Length, 3];
        for (int i = 0; i < jumpNames.Length; i++)
        {
            string name = jumpNames[i].Replace("W1_", "").Replace("_", " ");
            jumpStates[i, 0] = AddState(air, name + " Start", jumps[i, 0]);
            jumpStates[i, 1] = AddState(air, name + " Air", jumps[i, 1]);
            jumpStates[i, 2] = AddState(air, name + " Land", jumps[i, 2]);
        }

        AnimatorStateMachine reactions = machine.AddStateMachine("Hit And Death");
        AnimatorState hitState = AddState(reactions, "Hit", hit);
        var deathStates = new AnimatorState[4];
        string[] deathNames = { "Fall Forward", "Fall Backward", "Fall Left", "Fall Right" };
        for (int i = 0; i < 4; i++) deathStates[i] = AddState(reactions, "Dead " + deathNames[i], deaths[i]);

        machine.defaultState = stances[Relaxed].idle;

        // ---- Base layer transitions ----
        // Raising or lowering the pistol from a stand plays the matching clip; anything else crossfades.
        Go(stances[Relaxed].idle, raise, 0.15f, Is("Stance", Aim), Off("Moving"));
        Go(a.idle, lower, 0.15f, Is("Stance", Relaxed), Off("Moving"));
        Go(a.idle, aimDown, 0.15f, Is("Stance", CrouchAim));
        Go(ca.idle, aimUp, 0.15f, Is("Stance", Aim));
        Go(c.idle, crouchRaise, 0.15f, Is("Stance", CrouchAim));
        Go(ca.idle, crouchLower, 0.15f, Is("Stance", Crouch));
        // Each plays out into the stance it leads to, unless she sets off or changes stance again.
        foreach ((AnimatorState change, int target) in new[] { (raise, Aim), (lower, Relaxed), (aimDown, CrouchAim), (aimUp, Aim), (crouchRaise, CrouchAim), (crouchLower, Crouch) })
        {
            Go(change, stances[target].move, 0.25f, Is("Stance", target), On("Moving"));
            StanceChanges(change, stances, target, 0.3f);
            ExitBy(change, stances, 0.9f);
        }

        foreach (var pair in stances)
        {
            StanceStates s = pair.Value;
            foreach (AnimatorState state in s.all) StanceChanges(state, stances, pair.Key, 0.25f);

            // Quarter turns first, then turning on the spot, setting off, and stopping.
            if (s.quickLeft != null) QuickTurns(s);
            foreach ((AnimatorState turn, int side) in new[] { (s.turnLeft, -1), (s.turnRight, 1) })
            {
                Go(s.idle, turn, 0.2f, On("TurnInPlace"), Is("TurnSide", side));
                Go(turn, s.move, 0.25f, On("Moving"), Off("TurnInPlace"));
                Go(turn, s.idle, 0.2f, Off("TurnInPlace"));
                Go(turn, turn == s.turnLeft ? s.turnRight : s.turnLeft, 0.2f, On("TurnInPlace"), Is("TurnSide", -side));
            }
            if (s.start != null)
            {
                Go(s.idle, s.start, 0.15f, On("Moving"), Above("MoveY", 0.3f), Above("MoveX", -0.4f), Below("MoveX", 0.4f));
                Go(s.start, s.idle, 0.25f, Off("Moving"));
                ExitTo(s.start, s.move, 0.85f, 0.2f);
            }
            Go(s.idle, s.move, 0.2f, On("Moving"));
            if (s.fidget != null)
            {
                // Standing still a while, she looks around; setting off or turning cuts the look short.
                Go(s.idle, s.fidget, 0.4f, Above("IdleTime", 8f)).interruptionSource = TransitionInterruptionSource.Source;
                Go(s.fidget, s.move, 0.3f, On("Moving"));
                Go(s.fidget, s.idle, 0.3f, On("TurnInPlace"));
                ExitTo(s.fidget, s.idle, 0.95f);
            }
        }

        // Stops, from the pace she let go at: the pack's last strides, before the plain fade to a stand.
        foreach (int id in new[] { Relaxed, Hurt })
        {
            StanceStates s = stances[id];
            Go(s.move, s.stop, 0.2f, Off("Moving"), Above("MoveY", 2.4f));
            Go(s.stop, s.move, 0.2f, On("Moving"));
            ExitTo(s.stop, s.idle, 0.9f);
        }
        Go(a.move, aJogStop, 0.2f, Off("Moving"), Above("MoveY", 1.5f), Above("MoveX", -0.5f), Below("MoveX", 0.5f));
        Go(a.move, aWalkStop, 0.2f, Off("Moving"), Above("MoveY", 0.6f), Above("MoveX", -0.5f), Below("MoveX", 0.5f));
        foreach (AnimatorState stop in new[] { aWalkStop, aJogStop })
        {
            Go(stop, a.move, 0.2f, On("Moving"));
            ExitTo(stop, a.idle, 0.9f);
        }
        Go(ca.move, caStop, 0.2f, Off("Moving"), Above("MoveY", 0.6f), Above("MoveX", -0.5f), Below("MoveX", 0.5f));
        Go(caStop, ca.move, 0.2f, On("Moving"));
        ExitTo(caStop, ca.idle, 0.9f);
        foreach (StanceStates s in stances.Values) Go(s.move, s.idle, 0.3f, Off("Moving"));

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
        ExitBy(hitState, stances, 0.8f);

        // Jumps: the start, the air, the landing, then back to whatever she was doing.
        var windUps = new float[jumpNames.Length];
        for (int i = 0; i < jumpNames.Length; i++)
        {
            AnimatorStateTransition into = machine.AddAnyStateTransition(jumpStates[i, 0]);
            into.duration = 0.1f;
            into.canTransitionToSelf = false;
            into.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            into.AddCondition(AnimatorConditionMode.Equals, i, "JumpKind");
            ExitTo(jumpStates[i, 0], jumpStates[i, 1], 0.95f, 0.05f);
            Go(jumpStates[i, 1], jumpStates[i, 2], 0.05f, On("Grounded"));
            ExitBy(jumpStates[i, 2], stances, 0.7f);
            windUps[i] = jumps[i, 0].length * 0.95f;
        }

        BuildUpperBody(upper);

        // Aim offsets: single poses, turned into differences from looking straight ahead.
        AnimationClip lookCenter = PackClip(AimOffsets + "W1_Stand_Relaxed_Look_Center.fbx");
        Motion standAim = AimOffsetTree("Stand Aim Offset", "Stand_Aim");
        Motion crouchAim = AimOffsetTree("Crouch Aim Offset", "Crouch_Aim");
        AddLayer("Aim Offset", Tree1D("Aim Offset", "CrouchBlend", (standAim, 0f, 1f, 0f), (crouchAim, 1f, 1f, 0f)), additive: true);
        AddLayer("Look Offset", Tree1D("Look Offset", "AimPitch",
            (Additive("Look Down", PackClip(AimOffsets + "W1_Stand_Relaxed_Look_D90.fbx"), lookCenter), -1f, 1f, 0f),
            (Additive("Look Center", lookCenter, lookCenter), 0f, 1f, 0f),
            (Additive("Look Up", PackClip(AimOffsets + "W1_Stand_Relaxed_Look_U90.fbx"), lookCenter), 1f, 1f, 0f)), additive: true);

        WriteProfile(upper, windUps, (runCurveLeft, runCurveRight), (aimWalkCurveLeft, aimWalkCurveRight), (aimJogCurveLeft, aimJogCurveRight));

        // HomebrewIK places the feet in OnAnimatorIK, which only runs on a layer with its IK pass on.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = setup.footIK;
        controller.layers = layers;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        controller = null;
    }

    // ---- Stances ----

    class StanceStates
    {
        readonly AnimatorStateMachine machine;
        public AnimatorState idle, move, turnLeft, turnRight, quickLeft, quickRight, start, stop, fidget;
        public readonly List<AnimatorState> all = new();

        public StanceStates(AnimatorStateMachine root, string name) => machine = root.AddStateMachine(name);

        // A tag of "Move" marks a state whose root motion carries her and whose turns PlayerController
        // steers; "Turn" marks one whose root rotation turns her.
        public AnimatorState Add(string name, Motion motion, string tag = null)
        {
            AnimatorState state = AddState(machine, name, motion);
            if (tag != null) state.tag = tag;
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

    // A quarter turn on the spot, picked by TurnSide, from standing or from a turn already under way,
    // at the clip's own pace. Its root rotation turns her; she may set off once it is mostly done.
    static void QuickTurns(StanceStates s)
    {
        foreach ((AnimatorState state, int side) in new[] { (s.quickLeft, -1), (s.quickRight, 1) })
        {
            foreach (AnimatorState from in new[] { s.idle, s.turnLeft, s.turnRight })
            {
                AnimatorStateTransition into = Go(from, state, 0.15f, On("QuickTurn"), Is("TurnSide", side));
                // Before the other transitions out of these states.
                AnimatorStateTransition[] transitions = from.transitions;
                Array.Copy(transitions, 0, transitions, 1, transitions.Length - 1);
                transitions[0] = into;
                from.transitions = transitions;
            }
            ExitTo(state, s.move, 0.7f, 0.2f, On("Moving"));
            ExitTo(state, s.idle, 0.9f, 0.25f);
        }
    }

    // ---- Upper body and offsets ----

    class UpperBodyClips
    {
        public AnimationClip fire, crouchFire, rapidFire, crouchRapidFire, reload, aimReload, crouchReload;
        public AnimationClip draw, crouchDraw, holster, crouchHolster, toSecond, crouchToSecond, toPistol, crouchToPistol;
        public AnimationClip mobStand, mobCrouch;
    }

    // Everything here plays at the pack's own pace; a crouched and a standing clip of the same action
    // blend on CrouchBlend. Holstered, the upper body holds the MOB stand or crouch.
    static void BuildUpperBody(UpperBodyClips clips)
    {
        AnimatorStateMachine upper = AddLayer("Upper Body", null, additive: false);
        AnimatorState empty = upper.AddState("Empty");
        AnimatorState holstered = AddState(upper, "Holstered", Tree1D("Holstered", "CrouchBlend", (clips.mobStand, 0f, 1f, 0f), (clips.mobCrouch, 1f, 1f, 0f)));
        upper.defaultState = holstered;
        Go(empty, holstered, 0.3f, On("Unarmed"));
        Go(holstered, empty, 0.3f, Off("Unarmed"));

        Motion standReload = Tree1D("Stand Reload", "AimBlend", (clips.reload, 0f, 1f, 0f), (clips.aimReload, 1f, 1f, 0f));
        AnimatorState fireState = AddState(upper, "Fire", Tree1D("Fire", "CrouchBlend", (clips.fire, 0f, 1f, 0f), (clips.crouchFire, 1f, 1f, 0f)));
        AnimatorState reloadState = AddState(upper, "Reload", Tree1D("Reload", "CrouchBlend", (standReload, 0f, 1f, 0f), (clips.crouchReload, 1f, 1f, 0f)));
        AnimatorState drawState = AddState(upper, "Draw", Tree1D("Draw", "CrouchBlend", (clips.draw, 0f, 1f, 0f), (clips.crouchDraw, 1f, 1f, 0f)));
        AnimatorState holsterState = AddState(upper, "Holster", Tree1D("Holster", "CrouchBlend", (clips.holster, 0f, 1f, 0f), (clips.crouchHolster, 1f, 1f, 0f)));
        // Swapping to a second weapon and back. Nothing calls them yet: the demo has only the pistol.
        AnimatorState toSecond = AddState(upper, "Swap To Second Weapon", Tree1D("Swap To Second Weapon", "CrouchBlend", (clips.toSecond, 0f, 1f, 0f), (clips.crouchToSecond, 1f, 1f, 0f)));
        AnimatorState toPistol = AddState(upper, "Swap To Pistol", Tree1D("Swap To Pistol", "CrouchBlend", (clips.toPistol, 0f, 1f, 0f), (clips.crouchToPistol, 1f, 1f, 0f)));

        foreach ((AnimatorState state, string trigger, float exit) in new[] { (fireState, "Fire", 0.55f), (reloadState, "Reload", 0.92f), (drawState, "Draw", 0.9f), (holsterState, "Holster", 0.95f) })
        {
            AnimatorStateTransition into = upper.AddAnyStateTransition(state);
            into.duration = trigger == "Fire" ? 0.05f : 0.15f;
            into.canTransitionToSelf = trigger == "Fire";
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
            ExitTo(state, state == holsterState ? holstered : empty, exit);
        }
        foreach ((AnimatorState state, int weapon) in new[] { (toSecond, 2), (toPistol, 1) })
        {
            AnimatorStateTransition into = upper.AddAnyStateTransition(state);
            into.duration = 0.15f;
            into.canTransitionToSelf = false;
            into.AddCondition(AnimatorConditionMode.If, 0f, "Swap");
            into.AddCondition(AnimatorConditionMode.Equals, weapon, "Weapon");
            into.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
            ExitTo(state, empty, 0.95f);
        }

        // Holding the trigger: the continuous-fire loop, whose four recoils a cycle PlayerController
        // fires in step with. The first recoil comes two frames in, just after the shot.
        AnimatorState rapidState = AddState(upper, "Rapid Fire", Tree1D("Rapid Fire", "CrouchBlend", (clips.rapidFire, 0f, 1f, 0f), (clips.crouchRapidFire, 1f, 1f, 0f)));
        AnimatorStateTransition rapid = upper.AddAnyStateTransition(rapidState);
        rapid.duration = 0.05f;
        rapid.canTransitionToSelf = false;
        rapid.AddCondition(AnimatorConditionMode.If, 0f, "RapidFire");
        rapid.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
        Go(rapidState, empty, 0.2f, Off("RapidFire"));

        AnimatorStateTransition stop = upper.AddAnyStateTransition(empty);
        stop.duration = 0.2f;
        stop.canTransitionToSelf = false;
        stop.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");
    }

    // Stand or crouch aim offsets as a 2D blend on where the camera looks against where she faces.
    static Motion AimOffsetTree(string name, string prefix)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformCartesian2D,
            blendParameter = "AimYaw",
            blendParameterY = "AimPitch",
            useAutomaticThresholds = false,
        };
        AnimationClip center = PackClip(AimOffsets + "W1_" + prefix + "_Point_Center.fbx");
        (string pose, Vector2 at)[] poses = { ("Center", Vector2.zero), ("U90", Vector2.up), ("D90", Vector2.down), ("L90", Vector2.left), ("R90", Vector2.right) };
        foreach ((string pose, Vector2 at) in poses)
        {
            AnimationClip clip = PackClip(AimOffsets + "W1_" + prefix + "_Point_" + pose + ".fbx");
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

    static AnimatorStateMachine AddLayer(string name, Motion motion, bool additive, AvatarMask mask = null)
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
            // PlayerController fades the offsets and the unarmed arms in and out.
            defaultWeight = additive || mask != null ? 0f : 1f,
            avatarMask = mask != null ? mask : UpperBodyMask(),
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

    // Leaning into a curve on Turn, from -1 (the left circle loop) through 0 (straight) to 1 (the right one).
    static BlendTree LeanTree(string name, AnimationClip left, AnimationClip straight, AnimationClip right) =>
        Tree1D(name, "Turn", (left, -1f, 1f, 0f), (straight, 0f, 1f, 0f), (right, 1f, 1f, 0f));

    // Moving in every direction on (MoveX, MoveY), each clip at its own pace, standing still at the center.
    static BlendTree DirectionalTree(string name, AnimationClip idle, params (Motion motion, Vector2 at)[] directions)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveY",
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

    // How fast the clip turns its body, in degrees per second (positive is to the right).
    static float YawRate(AnimationClip clip) => Mathf.DeltaAngle(RootYaw(clip, 0f), RootYaw(clip, clip.length)) / clip.length;

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

    // ---- What PlayerController needs to know of the clips ----

    static void WriteProfile(UpperBodyClips upper, float[] jumpWindUps, (AnimationClip left, AnimationClip right) runCurve,
        (AnimationClip left, AnimationClip right) aimWalkCurve, (AnimationClip left, AnimationClip right) aimJogCurve)
    {
        LocomotionProfile profile = AssetDatabase.LoadAssetAtPath<LocomotionProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<LocomotionProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }
        profile.rapidFireInterval = upper.rapidFire.length / RapidShotsPerLoop;
        profile.reloadTime = upper.reload.length;
        profile.aimReloadTime = upper.aimReload.length;
        profile.crouchReloadTime = upper.crouchReload.length;
        profile.drawTime = upper.draw.length;
        profile.crouchDrawTime = upper.crouchDraw.length;
        profile.holsterTime = upper.holster.length;
        profile.crouchHolsterTime = upper.crouchHolster.length;
        profile.jumpWindUps = jumpWindUps;
        profile.runLeanRate = (YawRate(runCurve.right) - YawRate(runCurve.left)) / 2f;
        profile.aimWalkLeanRate = (YawRate(aimWalkCurve.right) - YawRate(aimWalkCurve.left)) / 2f;
        profile.aimJogLeanRate = (YawRate(aimJogCurve.right) - YawRate(aimJogCurve.left)) / 2f;
        EditorUtility.SetDirty(profile);
    }

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
        return LoadAvatar(MotusManPath);
    }

    // 22 files of the pack (the relaxed jog, the relaxed walk back and aside, the relaxed crouch walk)
    // are on the Unreal mannequin's skeleton, and have no bind pose: Unity took each one's avatar from
    // its first frame, so a bent left knee and a right foot pointing up counted as standing straight,
    // and those clips broke her ankles. They share one avatar instead, built from the mannequin's bones
    // laid out in MotusMan's T-pose: every limb points the way MotusMan's does, with the feet as high
    // off the ground.
    static Avatar PrepareMannequin()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(MannequinSource);
        if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }
        HumanDescription description = importer.humanDescription;
        HumanDescription reference = ((ModelImporter)AssetImporter.GetAtPath(MotusManPath)).humanDescription;
        mannequinRebuilt = AlignToReference(ref description, AssetDatabase.LoadAssetAtPath<GameObject>(MannequinSource).transform,
            reference, AssetDatabase.LoadAssetAtPath<GameObject>(MotusManPath).transform);
        if (mannequinRebuilt)
        {
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }
        return LoadAvatar(MannequinSource);
    }

    // The limbs laid out like the reference's, from the body outward: each bone points at the next,
    // and where a roll is given, the bone also turns about its length until the line between the two
    // roll bones lies as the reference's does (the palm across the knuckles, the foot ahead of the shin).
    const HumanBodyBones None = HumanBodyBones.LastBone;
    static readonly (HumanBodyBones bone, HumanBodyBones next, HumanBodyBones rollFrom, HumanBodyBones rollTo)[] AlignedLimbs =
    {
        (HumanBodyBones.Spine, HumanBodyBones.Chest, None, None), (HumanBodyBones.Chest, HumanBodyBones.UpperChest, None, None), (HumanBodyBones.Chest, HumanBodyBones.Neck, None, None),
        (HumanBodyBones.UpperChest, HumanBodyBones.Neck, None, None), (HumanBodyBones.Neck, HumanBodyBones.Head, None, None),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, None, None),
        (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
        (HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, None, None),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, None, None),
        (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightFoot, HumanBodyBones.RightToes),
        (HumanBodyBones.RightFoot, HumanBodyBones.RightToes, None, None),
        (HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, None, None), (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, None, None),
        (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal),
        (HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal),
        (HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, None, None), (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, None, None),
        (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal),
        (HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal),
    };

    // Turns the target's rest pose so each limb in AlignedLimbs lies where the reference's does in
    // its T-pose, then raises or lowers the hips so the feet are as high as the reference's. Both
    // models face +Z with Y up. Returns false when the pose already matches.
    static bool AlignToReference(ref HumanDescription target, Transform targetRoot, HumanDescription reference, Transform referenceRoot)
    {
        Dictionary<string, Matrix4x4> referencePose = RestPose(reference.skeleton, referenceRoot, out _);
        Dictionary<string, string> referenceBones = BoneNames(reference), bones = BoneNames(target);
        SkeletonBone[] skeleton = target.skeleton;
        var index = new Dictionary<string, int>();
        for (int i = 0; i < skeleton.Length; i++) index[skeleton[i].name] = i;
        bool changed = false;
        bool Has(HumanBodyBones bone) => bones.ContainsKey(HumanTrait.BoneName[(int)bone]) && referenceBones.ContainsKey(HumanTrait.BoneName[(int)bone]);
        Vector3 At(Dictionary<string, Matrix4x4> pose, Dictionary<string, string> names, HumanBodyBones bone) => pose[names[HumanTrait.BoneName[(int)bone]]].GetColumn(3);

        foreach ((HumanBodyBones bone, HumanBodyBones next, HumanBodyBones rollFrom, HumanBodyBones rollTo) in AlignedLimbs)
        {
            if (!Has(bone) || !Has(next) || !index.TryGetValue(bones[HumanTrait.BoneName[(int)bone]], out int i)) continue;
            string name = bones[HumanTrait.BoneName[(int)bone]];
            Dictionary<string, Matrix4x4> pose = RestPose(skeleton, targetRoot, out Dictionary<string, string> parents);
            Vector3 want = (At(referencePose, referenceBones, next) - At(referencePose, referenceBones, bone)).normalized;
            Vector3 have = (At(pose, bones, next) - At(pose, bones, bone)).normalized;
            Quaternion turn = Quaternion.FromToRotation(have, want);
            if (rollFrom != None && Has(rollFrom) && Has(rollTo))
            {
                Vector3 rollWant = Vector3.ProjectOnPlane(At(referencePose, referenceBones, rollTo) - At(referencePose, referenceBones, rollFrom), want);
                Vector3 rollHave = Vector3.ProjectOnPlane(turn * (At(pose, bones, rollTo) - At(pose, bones, rollFrom)), want);
                turn = Quaternion.AngleAxis(Vector3.SignedAngle(rollHave, rollWant, want), want) * turn;
            }
            float angle = Quaternion.Angle(Quaternion.identity, turn);
            if (angle < 0.5f) continue;
            Quaternion parent = Rotation(pose[parents[name]]);
            skeleton[i].rotation = Quaternion.Inverse(parent) * (turn * Rotation(pose[name]));
            changed = true;
        }

        // Feet on the ground: lift or lower the hips by however far the feet are off the reference's.
        Dictionary<string, Matrix4x4> laidOut = RestPose(skeleton, targetRoot, out Dictionary<string, string> parentOf);
        float FootHeight(Dictionary<string, Matrix4x4> pose, Dictionary<string, string> names) =>
            (pose[names["LeftFoot"]].GetColumn(3).y + pose[names["RightFoot"]].GetColumn(3).y) / 2f;
        float lift = FootHeight(referencePose, referenceBones) - FootHeight(laidOut, bones);
        if (Mathf.Abs(lift) > 0.001f && index.TryGetValue(bones["Hips"], out int hips))
        {
            Matrix4x4 parent = laidOut[parentOf[bones["Hips"]]];
            skeleton[hips].position += (Vector3)parent.inverse.MultiplyVector(Vector3.up * lift);
            changed = true;
        }
        target.skeleton = skeleton;
        return changed;
    }

    // Each bone's rest pose in the model's space, by name, from the avatar's skeleton where it has the
    // bone and from the model itself where it doesn't, with each bone's parent.
    static Dictionary<string, Matrix4x4> RestPose(SkeletonBone[] skeleton, Transform root, out Dictionary<string, string> parents)
    {
        var bones = new Dictionary<string, SkeletonBone>();
        foreach (SkeletonBone bone in skeleton) bones[bone.name] = bone;
        var pose = new Dictionary<string, Matrix4x4>();
        var parentNames = new Dictionary<string, string>();
        void Walk(Transform transform, Matrix4x4 parent)
        {
            Matrix4x4 local = bones.TryGetValue(transform.name, out SkeletonBone bone)
                ? Matrix4x4.TRS(bone.position, bone.rotation, bone.scale)
                : Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale);
            Matrix4x4 world = parent * local;
            pose[transform.name] = world;
            foreach (Transform child in transform)
            {
                parentNames[child.name] = transform.name;
                Walk(child, world);
            }
        }
        pose[root.name] = Matrix4x4.identity;
        foreach (Transform child in root)
        {
            parentNames[child.name] = root.name;
            Walk(child, Matrix4x4.identity);
        }
        parents = parentNames;
        return pose;
    }

    static Quaternion Rotation(Matrix4x4 m) => Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));

    static Dictionary<string, string> BoneNames(HumanDescription description)
    {
        var names = new Dictionary<string, string>();
        foreach (HumanBone bone in description.human) names[bone.humanName] = bone.boneName;
        return names;
    }

    static Avatar LoadAvatar(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Avatar avatar) return avatar;
        }
        throw new InvalidOperationException("No avatar in " + path);
    }

    // A pack clip, imported as Humanoid on MotusMan's avatar, or the shared mannequin avatar for the
    // files on that skeleton, with the pack's root motion: travel along the ground is root motion,
    // and so is the turn of a turning clip (`turn`); everything else is baked into the pose.
    static AnimationClip PackClip(string path, bool loop = false, bool turn = false)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer) throw new InvalidOperationException("Missing " + path);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        bool onMannequin = model != null && HasChild(model.transform, "pelvis");
        bool changed = importer.animationType != ModelImporterAnimationType.Human;
        importer.animationType = ModelImporterAnimationType.Human;
        if (path != MannequinSource)
        {
            Avatar source = onMannequin ? mannequin : motusMan;
            changed |= importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != source || (onMannequin && mannequinRebuilt);
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = source;
        }
        if (changed) importer.SaveAndReimport();
        SetClipSettings(importer, loop, bakeRotation: !turn);
        return Load(path, importer.clipAnimations[0].name);
    }

    // A few frames of a pack clip as a clip of their own, next to the whole one in the same file: its
    // first `frames` frames, or with `last` its last ones (held as a pose).
    static AnimationClip PackTake(string path, string name, bool last, int frames)
    {
        AnimationClip whole = PackClip(path);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        var clips = new List<ModelImporterClipAnimation>(importer.clipAnimations);
        ModelImporterClipAnimation take = clips.Find(c => c.name == name);
        ModelImporterClipAnimation first = clips[0];
        float from = last ? first.lastFrame - frames : first.firstFrame, to = last ? first.lastFrame : first.firstFrame + frames;
        if (take == null || !Mathf.Approximately(take.firstFrame, from) || !Mathf.Approximately(take.lastFrame, to) || take.loopTime != last)
        {
            clips.Remove(take);
            clips.Add(new ModelImporterClipAnimation
            {
                name = name,
                takeName = first.takeName,
                firstFrame = from,
                lastFrame = to,
                loopTime = last,
                lockRootHeightY = true,
                keepOriginalPositionY = true,
                lockRootRotation = true,
                keepOriginalOrientation = true,
                lockRootPositionXZ = last,
                keepOriginalPositionXZ = true,
            });
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
        }
        return whole != null ? Load(path, name) : null;
    }

    static bool HasChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name || HasChild(child, name)) return true;
        }
        return false;
    }

    // Makes sure the model is humanoid and returns the file its avatar comes from.
    static string PrepareModel(PlayerCharacterSetup setup)
    {
        Animator animator = setup.model.GetComponentInChildren<Animator>();
        string path = animator != null && animator.avatar != null ? AssetDatabase.GetAssetPath(animator.avatar) : AssetDatabase.GetAssetPath(setup.model);
        if (AssetImporter.GetAtPath(path) is ModelImporter importer && importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }
        return path;
    }

    // Caps how far the feet bend up toward the shins and twist, on the model's own avatar, so every
    // clip retargeted onto a model in high heels keeps them believable.
    static void LimitFeet(string path, float upLimit, float twistLimit)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;
        HumanDescription description = importer.humanDescription;
        HumanBone[] bones = description.human;
        bool changed = false;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].humanName != "LeftFoot" && bones[i].humanName != "RightFoot") continue;
            var limit = new HumanLimit
            {
                useDefaultValues = false,
                // A foot's muscles: y is Foot Twist In-Out, z is Foot Up-Down (x has none). Negative z
                // bends the foot up toward the shin, which heels allow little of; pointing the toes
                // keeps Unity's 50 degrees.
                min = new Vector3(0f, -twistLimit, -upLimit),
                max = new Vector3(0f, twistLimit, 50f),
                center = Vector3.zero,
            };
            if (!bones[i].limit.useDefaultValues && bones[i].limit.min == limit.min && bones[i].limit.max == limit.max) continue;
            bones[i].limit = limit;
            changed = true;
        }
        if (!changed) return;
        description.human = bones;
        importer.humanDescription = description;
        importer.SaveAndReimport();
    }

    // The pack's root motion: travel along the ground (XZ) stays root motion, and PlayerController
    // moves her by it. The hips' rise and fall stays in the pose. A turning clip's turn is root motion
    // too; every other clip keeps its facing in the pose, as recorded.
    static void SetClipSettings(ModelImporter importer, bool loop, bool bakeRotation)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        // Extra takes (the hit cut from a fall, the MOB poses) are set up where they are made.
        ModelImporterClipAnimation clip = clips[0];
        bool same = clip.loopTime == loop && clip.lockRootHeightY && clip.keepOriginalPositionY && !clip.heightFromFeet && clip.heightOffset == 0f
            && !clip.lockRootPositionXZ && clip.lockRootRotation == bakeRotation && (!bakeRotation || clip.keepOriginalOrientation) && !clip.loopPose;
        if (!same)
        {
            clip.heightOffset = 0f;
            clip.loopTime = loop;
            clip.loopPose = false;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
            clip.heightFromFeet = false;
            clip.lockRootPositionXZ = false;
            clip.lockRootRotation = bakeRotation;
            if (bakeRotation) clip.keepOriginalOrientation = true;
        }
        importer.clipAnimations = clips;
        if (!same) importer.SaveAndReimport();
    }

    static AnimationClip Load(string path, string name = null)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview") && (name == null || clip.name == name)) return clip;
        }
        throw new InvalidOperationException("No animation clip " + name + " in " + path);
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
        foreach (string name in new[] { "MoveX", "MoveY", "Turn", "AimPitch", "AimYaw", "AimBlend", "CrouchBlend", "IdleTime" })
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        foreach (string name in new[] { "IsAiming", "Moving", "TurnInPlace", "Grounded", "IsDead", "RapidFire", "Unarmed" })
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        foreach (string name in new[] { "Stance", "JumpKind", "DeathDir", "TurnSide", "Weapon" })
            controller.AddParameter(name, AnimatorControllerParameterType.Int);
        foreach (string name in new[] { "Fire", "Reload", "Draw", "Holster", "Swap", "Jump", "QuickTurn", "Hit", "Dead" })
            controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        AnimatorControllerParameter[] parameters = controller.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.name == "Stance") parameter.defaultInt = Relaxed;
            if (parameter.name == "Grounded") parameter.defaultBool = true;
            if (parameter.name == "Unarmed") parameter.defaultBool = true;
            if (parameter.name == "Weapon") parameter.defaultInt = 1;
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
