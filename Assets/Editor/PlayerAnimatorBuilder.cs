using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// Phase 2: turns the clips into the player's Animator.
//
// The model is picked in the Player Character asset (see PlayerCharacterSetup); by default it is the
// pack's own MotusMan, the skeleton the clips were recorded on. Every clip comes from the MocapOnline
// "Pistol Basic 27A" set: relaxed, aiming, crouched and crouched aiming, with steps
// back and aside, turns in place that step her feet round, and curves that lean on the move. Fire,
// reload, draw and holster play on an upper-body layer, so she can do them on the move; two additive
// layers point the pistol, or the head, where the camera looks.
//
// The set has no clips without the pistol in hand, and even the free left arm is held ready rather
// than swinging. Holstered she moves on the same relaxed clips, and the Unarmed Arms layer, synced to
// the base layer, gives her arms that hang loose and swing against each clip's own steps.
//
// PlayerController moves and turns the body. Root motion only reports how fast the animated steps
// go (first steps and stops). Speeds and turn rates come from the clips themselves and are written
// to Eve Locomotion.asset, which PlayerController reads.
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
    const string ArmsFolder = Generated + "/Unarmed Arms";

    // Speeds that are a design choice, not a clip's own pace. Raptors chase at 4.7 m/s.
    const float SprintSpeed = 4.4f;
    // The turn-in-place loops step 45 degrees in 0.67 s. Played up to three times as fast, she still
    // turns round in under a second.
    const float MaxTurnInPlaceRate = 200f;
    // The fastest walk, as a share of the walk's own pace, before she breaks into a jog or a run.
    const float BriskWalk = 1.35f;
    // Every reload takes this long, so PlayerController knows when the rounds go in.
    public const float ReloadTime = 1.6f;
    // The continuous-fire loops fire four rounds a cycle; holding the trigger fires at that pace.
    const int RapidShotsPerLoop = 4;
    // A hit is the first 20 frames of falling backward: she staggers, and catches herself before the fall.
    const int HitFrames = 20;

    // Stances, as the Stance parameter that PlayerController sets. Holstered or not, she stands,
    // crouches and limps on the same states; only the Unarmed Arms layer tells them apart.
    public const int Relaxed = 1, Aim = 2, Crouch = 3, CrouchAim = 4, Hurt = 5;
    // Jumps, as the JumpKind parameter.
    public const int JumpStand = 0, JumpRun = 1, JumpAimStand = 2, JumpAimForward = 3, JumpAimLeft = 4, JumpAimRight = 5;

    // The pack holsters the pistol in the middle of the back ("MOB"). This is where its holster clip
    // lets go of it and its draw clip takes hold, in MotusMan's hips, measured at PlayerController's
    // HolsterStowTime and DrawGrabTime (the two agree within 5 mm), so the pistol never jumps.
    const string HolsterBone = "Hips";
    static readonly Vector3 HolsterPosition = new(-0.089f, 0.098f, 0.145f), HolsterRotation = new(355.5f, 96.5f, 151f);

    static AnimatorController controller;
    static float scale;
    static Avatar motusMan, mannequin;
    // True when the shared mannequin avatar was just rebuilt, so the files copying it import again.
    static bool mannequinRebuilt;
    // How the Unarmed Arms layer makes a clip's arms empty-handed (see UnarmedArms).
    enum Arms { Swing, Mirror, Hands }
    static readonly Dictionary<(Motion motion, Arms arms), Motion> armsVersions = new();
    // The relaxed idle, whose left hand gives every empty hand its fingers and wrist.
    static AnimationClip relaxedHand;

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
        scale = HumanScale(setup);
        if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder("Assets/Characters/Player", "Generated");
        if (!AssetDatabase.IsValidFolder(ArmsFolder)) AssetDatabase.CreateFolder(Generated, "Unarmed Arms");
        armsVersions.Clear();

        // Standing with the pistol down, or holstered. Turns keep their yaw out of the pose, so
        // PlayerController turns the body and the feet step round with it.
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
        AnimationClip runLeft = PackClip(Loops + "W1_Run_L_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip runRight = PackClip(Loops + "W1_Run_R_CIR_Loop.fbx", loop: true, turn: true);
        AnimationClip runSideLeft = PackClip(Loops + "W1_Run_L_Loop.fbx", loop: true);
        AnimationClip runSideRight = PackClip(Loops + "W1_Run_R_Loop.fbx", loop: true);
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

        string[] deathFiles = { "W1_Stand_Relaxed_Death_F", "W1_Stand_Relaxed_Death_B", "W1_Stand_Relaxed_Death_L", "W1_Stand_Relaxed_Death_R" };
        var deaths = new AnimationClip[4];
        for (int i = 0; i < 4; i++) deaths[i] = PackClip(RootMotion + deathFiles[i] + ".fbx");
        AnimationClip hit = PackTake(RootMotion + "W1_Stand_Relaxed_Death_B.fbx", "W1_Stand_Relaxed_Hit", HitFrames);

        // Split jumps: the start, a loop in the air, the landing. Only in-place versions exist.
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

        // Speeds on her body, in m/s.
        float walkSpeed = Speed(walk), jogSpeed = Speed(jog);
        float aimWalkSpeed = Speed(aimWalk), aimJogSpeed = Speed(aimJog);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var stances = new Dictionary<int, StanceStates>();

        // Standing free: walk, jog and run, the run leaning into curves. Where she moves off her
        // facing (sliding along a wall, pushed back) she steps back or aside instead.
        BlendTree freeMove = DirectionalTree("Free Move", idle,
            (GaitTree("Free Forward", null,
                new Gait(walk, walkSpeed, walkSpeed * BriskWalk),
                new Gait(jog, 0f, jogSpeed),
                new Gait(run, 0f, SprintSpeed, left: runLeft, right: runRight)), new Vector2(0f, walkSpeed)),
            (walkBack, new Vector2(0f, -Speed(walkBack))),
            (walkLeft, new Vector2(-Speed(walkLeft), 0f)),
            (walkRight, new Vector2(Speed(walkRight), 0f)),
            (jogBack, new Vector2(0f, -Speed(jogBack))),
            (jogLeft, new Vector2(-Speed(jogLeft), 0f)),
            (jogRight, new Vector2(Speed(jogRight), 0f)),
            (runSideLeft, new Vector2(-Speed(runSideLeft), 0f)),
            (runSideRight, new Vector2(Speed(runSideRight), 0f)));
        BlendTree freeTurn = TurnTree("Free Turn", turnLeft, turnRight, idle);

        // Relaxed (pistol down or holstered) and badly hurt move alike; hurt, PlayerController holds
        // her to a jog. A sprint stops with a last stride; standing still a while, she looks around.
        foreach ((int id, string name) in new[] { (Relaxed, "Relaxed"), (Hurt, "Hurt") })
        {
            var s = stances[id] = new StanceStates(machine, name);
            s.idle = s.Add("Idle", idle);
            s.turn = s.Add("Turn", freeTurn);
            s.move = s.Add("Move", freeMove);
            s.stop = s.Add("Stop", runStop, step: true);
            s.fidget = s.Add("Look Around", fidget);
        }
        // A sprint tuned faster than the run clip plays the whole state faster through MoveScale.
        stances[Relaxed].move.speedParameterActive = true;
        stances[Relaxed].move.speedParameter = "MoveScale";

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
        a.fidget = a.Add("Look Around", aimFidget);
        AnimatorState aStart = a.Add("First Step", aimStart, step: true);
        (AnimatorState left, AnimatorState right) aQuick = (a.Add("Quick Turn Left", aimQuickLeft, quickTurn: true), a.Add("Quick Turn Right", aimQuickRight, quickTurn: true));
        AnimatorState aWalkStop = a.Add("Walk Stop", aimWalkStop, step: true);
        AnimatorState aJogStop = a.Add("Jog Stop", aimJogStop, step: true);

        // Crouched, pistol down or holstered: creeps forward and turns to face the way she goes, and
        // steps back or aside where she moves off her facing.
        var c = stances[Crouch] = new StanceStates(machine, "Crouch");
        c.idle = c.Add("Idle", crouchIdle);
        c.turn = c.Add("Turn", TurnTree("Crouch Turn", crouchTurnLeft, crouchTurnRight, crouchIdle));
        c.move = c.Add("Move", DirectionalTree("Crouch Move", crouchIdle,
            (crouchWalk, new Vector2(0f, Speed(crouchWalk))),
            (crouchBack, new Vector2(0f, -Speed(crouchBack))),
            (crouchLeft, new Vector2(-Speed(crouchLeft), 0f)),
            (crouchRight, new Vector2(Speed(crouchRight), 0f))));

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

        machine.defaultState = stances[Relaxed].idle;
        // Unity's Foot IK puts the feet where the clip's flat-footed actor put his; on heels that bends
        // her ankles, so it is a choice in the Player Character asset.
        foreach (StanceStates s in stances.Values)
            foreach (AnimatorState state in s.all) state.iKOnFeet = setup.stateFootIK;

        // ---- Base layer transitions ----
        // Raising or lowering the pistol from a stand plays the matching clip; anything else crossfades.
        Go(stances[Relaxed].idle, raise, 0.15f, Is("Stance", Aim), Off("Moving"));
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

            // Standing still a while, she looks around; setting off or turning cuts the look short.
            if (s.fidget != null)
            {
                Go(s.idle, s.fidget, 0.4f, Above("IdleTime", 8f)).interruptionSource = TransitionInterruptionSource.Source;
                Go(s.fidget, s.turn, 0.3f, On("TurnInPlace"));
                Go(s.fidget, s.move, 0.3f, On("Moving"));
                ExitTo(s.fidget, s.idle, 0.95f);
            }
        }

        // Relaxed and hurt: a sprint ends in a last stride. Stop is set the frame she lets go, with
        // StopSpeed the pace she let go at.
        foreach (int id in new[] { Relaxed, Hurt })
        {
            StanceStates s = stances[id];
            Go(s.idle, s.move, 0.2f, On("Moving"));
            Go(s.move, s.stop, 0.2f, On("Stop"), Above("StopSpeed", SprintSpeed * 0.7f));
            Go(s.stop, s.turn, 0.25f, On("TurnInPlace"));
            Go(s.stop, s.move, 0.2f, On("Moving"));
            ExitTo(s.stop, s.idle, 0.85f);
            Stop(s);
        }

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

        // Crouched, with the pistol down or raised.
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

        // Holstered, she stands, crouches and jumps on the same states with loose arms; a flinch
        // throws both arms like the free one; falling dead, only her right hand lets go.
        relaxedHand = idle;
        var unarmed = new List<(AnimatorState state, Arms arms)>();
        foreach (int id in new[] { Relaxed, Hurt, Crouch })
            foreach (AnimatorState state in stances[id].all) unarmed.Add((state, Arms.Swing));
        foreach (AnimatorState jump in jumpStates) unarmed.Add((jump, Arms.Swing));
        unarmed.Add((hitState, Arms.Mirror));
        foreach (AnimatorState death in deathStates) unarmed.Add((death, Arms.Hands));
        AddUnarmedArms(unarmed);
        // Arm clips no state uses any more go.
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ArmsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!armsVersions.ContainsValue(AssetDatabase.LoadAssetAtPath<AnimationClip>(path))) AssetDatabase.DeleteAsset(path);
        }

        WriteProfile(walkSpeed, jogSpeed, aimWalkSpeed, aimJogSpeed, Speed(crouchWalk), Speed(crouchAimWalk), upper.rapidFire.length / RapidShotsPerLoop,
            (run, runLeft, runRight), (aimWalk, aimWalkCurveLeft, aimWalkCurveRight), (aimJog, aimJogCurveLeft, aimJogCurveRight), turnLeft, turnRight);

        // HomebrewIK places the feet in OnAnimatorIK, which only runs on a layer with its IK pass on.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = setup.footIK;
        controller.layers = layers;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        controller = null;
        armsVersions.Clear();
    }

    // How long a jump's wind-up lasts before she leaves the ground. PlayerController waits as long.
    public const float JumpWindUp = 0.25f;

    // ---- Stances ----

    class StanceStates
    {
        readonly AnimatorStateMachine machine;
        public AnimatorState idle, turn, move, stop, fidget;
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
    // Aiming, she may set off at once; unarmed, she is already holding the stick, so the turn plays
    // most of the way before she walks on.
    static void QuickTurns(StanceStates s, (AnimatorState left, AnimatorState right) quick, bool setOffEarly = true)
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
            if (setOffEarly) Go(state, s.move, 0.2f, On("Moving"));
            else ExitTo(state, s.move, 0.75f, 0.2f, On("Moving"));
            ExitTo(state, s.turn, 0.9f, 0.25f, On("TurnInPlace"));
            ExitTo(state, s.idle, 0.9f, 0.25f);
        }
    }

    // Where a first step starts moving: the clip opens with a still weight shift, which made setting
    // off feel slow. The step starts just before its pace reaches a fifth of the walk's.
    static float StepStart(AnimationClip step, AnimationClip loop)
    {
        float loopSpeed = Stride(loop) / loop.length;
        for (float n = 0.02f; n < 0.5f; n += 0.01f)
        {
            float t = n * step.length;
            Vector3 travel = RootPosition(step, t + 0.05f) - RootPosition(step, t - 0.05f);
            if (new Vector2(travel.x, travel.z).magnitude / 0.1f >= loopSpeed * 0.2f) return Mathf.Max(0f, n - 0.04f);
        }
        return 0f;
    }

    // A first step hands over to the walk when it reaches walking pace, mid-stride, measured from the clips.
    static void StepInto(AnimatorState step, AnimatorState move, AnimationClip stepClip, AnimationClip loop)
    {
        (float exit, float offset) = HandOver(stepClip, loop);
        ExitTo(step, move, exit, 0.15f).offset = offset;
    }

    // ---- Upper body and offsets ----

    class UpperBodyClips
    {
        public AnimationClip fire, crouchFire, rapidFire, crouchRapidFire, reload, aimReload, crouchReload;
        public AnimationClip draw, crouchDraw, holster, crouchHolster, toSecond, crouchToSecond, toPistol, crouchToPistol;
    }

    static void BuildUpperBody(UpperBodyClips clips)
    {
        AnimatorStateMachine upper = AddLayer("Upper Body", null, additive: false);
        AnimatorState empty = upper.AddState("Empty");
        upper.defaultState = empty;

        Motion standReload = Tree1D("Stand Reload", "AimBlend", (clips.reload, 0f, clips.reload.length / ReloadTime, 0f), (clips.aimReload, 1f, clips.aimReload.length / ReloadTime, 0f));
        AnimatorState fireState = AddState(upper, "Fire", Tree1D("Fire", "CrouchBlend", (clips.fire, 0f, 1f, 0f), (clips.crouchFire, 1f, 1f, 0f)));
        AnimatorState reloadState = AddState(upper, "Reload", Tree1D("Reload", "CrouchBlend", (standReload, 0f, 1f, 0f), (clips.crouchReload, 1f, clips.crouchReload.length / ReloadTime, 0f)));
        AnimatorState drawState = AddState(upper, "Draw", Tree1D("Draw", "CrouchBlend", (clips.draw, 0f, 1f, 0f), (clips.crouchDraw, 1f, clips.crouchDraw.length / clips.draw.length, 0f)));
        AnimatorState holsterState = AddState(upper, "Holster", Tree1D("Holster", "CrouchBlend", (clips.holster, 0f, 1f, 0f), (clips.crouchHolster, 1f, clips.crouchHolster.length / clips.holster.length, 0f)));
        // Swapping to a second weapon and back. Nothing calls them yet: the demo has only the pistol.
        AnimatorState toSecond = AddState(upper, "Swap To Second Weapon", Tree1D("Swap To Second Weapon", "CrouchBlend", (clips.toSecond, 0f, 1f, 0f), (clips.crouchToSecond, 1f, clips.crouchToSecond.length / clips.toSecond.length, 0f)));
        AnimatorState toPistol = AddState(upper, "Swap To Pistol", Tree1D("Swap To Pistol", "CrouchBlend", (clips.toPistol, 0f, 1f, 0f), (clips.crouchToPistol, 1f, clips.crouchToPistol.length / clips.toPistol.length, 0f)));

        foreach ((AnimatorState state, string trigger, float exit) in new[] { (fireState, "Fire", 0.55f), (reloadState, "Reload", 0.92f), (drawState, "Draw", 0.9f), (holsterState, "Holster", 0.95f) })
        {
            AnimatorStateTransition into = upper.AddAnyStateTransition(state);
            into.duration = trigger == "Fire" ? 0.05f : 0.15f;
            into.canTransitionToSelf = trigger == "Fire";
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");
            ExitTo(state, empty, exit);
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
        AnimatorState rapidState = AddState(upper, "Rapid Fire", Tree1D("Rapid Fire", "CrouchBlend", (clips.rapidFire, 0f, 1f, 0f), (clips.crouchRapidFire, 1f, clips.crouchRapidFire.length / clips.rapidFire.length, 0f)));
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

    // Arms and hands only, so relaxed arms can hang from whatever pose the body is in.
    static AvatarMask ArmsMask()
    {
        string path = Generated + "/Arms.mask";
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }
        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, part is AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm
                or AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers or AvatarMaskBodyPart.LeftHandIK or AvatarMaskBodyPart.RightHandIK);
        EditorUtility.SetDirty(mask);
        return mask;
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

    // ---- Empty hands ----

    // A layer synced to the base layer that plays, for every state she can be in with the pistol
    // holstered, the same motion with empty-handed arms. PlayerController fades it in while holstered.
    static void AddUnarmedArms(IEnumerable<(AnimatorState state, Arms arms)> states)
    {
        var machine = new AnimatorStateMachine { name = "Unarmed Arms", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        var layer = new AnimatorControllerLayer
        {
            name = "Unarmed Arms",
            stateMachine = machine,
            syncedLayerIndex = 0,
            syncedLayerAffectsTiming = false,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,
            avatarMask = ArmsMask(),
        };
        foreach ((AnimatorState state, Arms arms) in states) layer.SetOverrideMotion(state, ArmsVersion(state.motion, arms));
        controller.AddLayer(layer);
    }

    // The empty-handed version of a clip or a whole blend tree, made once per build.
    static Motion ArmsVersion(Motion motion, Arms arms)
    {
        if (motion == null) return null;
        if (armsVersions.TryGetValue((motion, arms), out Motion done)) return done;
        Motion result;
        if (motion is AnimationClip clip) result = UnarmedArms(clip, arms);
        else
        {
            var tree = (BlendTree)motion;
            var copy = new BlendTree
            {
                name = tree.name + " " + arms,
                blendType = tree.blendType,
                blendParameter = tree.blendParameter,
                blendParameterY = tree.blendParameterY,
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(copy, controller);
            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++) children[i].motion = ArmsVersion(children[i].motion, arms);
            copy.children = children;
            result = copy;
        }
        armsVersions[(motion, arms)] = result;
        return result;
    }

    // Empty-handed arms for a pistol-pack clip, in one of three ways:
    // - Swing: loose arms, hanging at her sides with the relaxed idle's hands. Each swings forward as
    //   the other side's foot steps forward, as far as the clip's feet travel; the faster the clip,
    //   the harder they swing and the more the elbows bend (a run's are at about 90 degrees).
    //   Crouched, they hang forward over the knees and barely swing.
    // - Mirror: the free left arm copied onto the right, both reacting together (a flinch).
    // - Hands: the arms keep the clip's own motion; only the right hand opens like the left (a fall).
    static AnimationClip UnarmedArms(AnimationClip source, Arms arms)
    {
        string path = ArmsFolder + "/" + source.name + " " + arms + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = source.frameRate;
        bool loop = AnimationUtility.GetAnimationClipSettings(source).loopTime;
        if (arms == Arms.Swing) SwingingArms(source, clip);
        else
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                string muscle = binding.propertyName;
                bool left = muscle.StartsWith("Left") && IsArmMuscle(muscle.Substring(4));
                bool right = muscle.StartsWith("Right") && IsArmMuscle(muscle.Substring(5));
                if (!left && !right) continue;
                bool finger = muscle.Contains("Hand.");
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (left)
                {
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                    if (arms == Arms.Hands && !finger) continue;
                    EditorCurveBinding mirrored = binding;
                    mirrored.propertyName = "Right" + muscle.Substring(4);
                    AnimationUtility.SetEditorCurve(clip, mirrored, curve);
                }
                else if (arms == Arms.Hands && !finger)
                {
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }
        }
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // Loose arms for Arms.Swing, in muscle values measured on MotusMan against his torso: Arm Down-Up
    // -0.7 and Arm Front-Back 0.1 hang the arm down at his side, 9 degrees out and 4 forward; 0.2 more
    // or less of Front-Back swings it about 15 degrees back or forward; Forearm Stretch 0.7 bends the
    // elbow to about 156 degrees, -0.1 to about 90. Crouched, Down-Up -0.475 and Front-Back -0.125
    // hold the arm 28 degrees forward, over the knees.
    static void SwingingArms(AnimationClip source, AnimationClip clip)
    {
        AnimationCurve rootY = Curve(source, "RootT.y");
        AnimationCurve leftFoot = Curve(source, "LeftFootT.z"), rightFoot = Curve(source, "RightFootT.z");
        float length = Mathf.Max(source.length, 1f / source.frameRate);
        int frames = Mathf.Max(1, Mathf.RoundToInt(length * source.frameRate));
        float dt = length / frames;
        float Mean(AnimationCurve curve)
        {
            if (curve == null) return 0f;
            float sum = 0f;
            for (int i = 0; i <= frames; i++) sum += curve.Evaluate(i * dt);
            return sum / (frames + 1);
        }
        bool crouched = Mean(rootY) < 0.8f;
        // How hard she moves: 0 at a walk or slower, 1 at a run, from the clip's own pace.
        float pace = Stride(source) / length;
        float effort = crouched ? 0f : Mathf.InverseLerp(1.5f, 4.5f, pace);
        // Front-Back per metre the opposite foot is ahead of its average.
        float swing = crouched ? 0.35f : Mathf.Lerp(0.7f, 1f, effort);
        float down = crouched ? -0.475f : -0.7f, hang = crouched ? -0.125f : 0.1f;
        float elbow = crouched ? 0.4f : Mathf.Lerp(0.7f, -0.1f, effort);
        float leftMean = Mean(leftFoot), rightMean = Mean(rightFoot);

        var muscles = new Dictionary<string, float[]>();
        foreach (string side in new[] { "Left", "Right" })
        {
            AnimationCurve otherFoot = side == "Left" ? rightFoot : leftFoot;
            float otherMean = side == "Left" ? rightMean : leftMean;
            var frontBack = new float[frames + 1];
            for (int i = 0; i <= frames; i++)
                frontBack[i] = hang - swing * (otherFoot != null ? otherFoot.Evaluate(i * dt) - otherMean : 0f);
            muscles[side + " Arm Front-Back"] = frontBack;
            muscles[side + " Arm Down-Up"] = Constant(down, frames);
            muscles[side + " Shoulder Down-Up"] = Constant(0f, frames);
            muscles[side + " Shoulder Front-Back"] = Constant(0f, frames);
            muscles[side + " Forearm Stretch"] = Constant(elbow, frames);
        }
        // The wrist, the forearm's turn and the fingers come from the relaxed idle's free left hand.
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(relaxedHand))
        {
            string muscle = binding.propertyName;
            if (!muscle.StartsWith("Left") || !IsArmMuscle(muscle.Substring(4))) continue;
            string part = muscle.Substring(4);
            if (part.StartsWith(" Arm Front-Back") || part.StartsWith(" Arm Down-Up") || part.StartsWith(" Shoulder") || part.StartsWith(" Forearm Stretch")) continue;
            float value = AnimationUtility.GetEditorCurve(relaxedHand, binding).Evaluate(0f);
            muscles[muscle] = Constant(value, frames);
            muscles["Right" + part] = Constant(value, frames);
        }
        foreach (var pair in muscles)
        {
            var keys = new Keyframe[frames + 1];
            float[] values = pair.Value;
            for (int i = 0; i <= frames; i++)
            {
                float before = values[Mathf.Max(0, i - 1)], after = values[Mathf.Min(frames, i + 1)];
                float slope = (after - before) / ((Mathf.Min(frames, i + 1) - Mathf.Max(0, i - 1)) * dt);
                keys[i] = new Keyframe(i * dt, values[i], slope, slope);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), new AnimationCurve(keys));
        }
    }

    static float[] Constant(float value, int frames)
    {
        var values = new float[frames + 1];
        for (int i = 0; i <= frames; i++) values[i] = value;
        return values;
    }

    // The shoulder, arm, forearm and wrist muscles and the fingers, named without their side.
    static bool IsArmMuscle(string muscle) =>
        muscle.StartsWith(" Shoulder") || muscle.StartsWith(" Arm") || muscle.StartsWith(" Forearm") || muscle.StartsWith(" Hand") || muscle.StartsWith("Hand.");

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

    // A 1D blend on Speed in m/s. Each clip plays just fast enough that her feet cover the speed it
    // sits at. Two gaits blend at the same cadence: the slower one speeds up to its top, and the
    // faster one takes over at the speed its longer stride gives at that cadence.
    static BlendTree GaitTree(string name, AnimationClip idle, params Gait[] gaits)
    {
        var children = new List<(Motion motion, float at, float timeScale, float cycleOffset)>();
        // Without an idle, the tree starts at the first gait; an outer blend fades it in from a stand.
        if (idle != null) children.Add((idle, 0f, 1f, 0f));
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
        float stride = new Vector2(end.x - start.x, end.z - start.z).magnitude;
        return stride > 0.05f ? stride : InPlaceStride(clip);
    }

    // An in-place clip keeps the body still, so its
    // stride is read from the feet instead: the planted foot slides back at the speed the body would
    // go forward. Only frames with a foot within 2 cm of its lowest point count, so a run's flight
    // phase doesn't drag the figure down. Checked against a walk with root motion: within 10%.
    static float InPlaceStride(AnimationClip clip)
    {
        AnimationCurve leftY = Curve(clip, "LeftFootT.y"), rightY = Curve(clip, "RightFootT.y");
        AnimationCurve leftZ = Curve(clip, "LeftFootT.z"), rightZ = Curve(clip, "RightFootT.z");
        if (leftY == null || rightY == null || leftZ == null || rightZ == null || clip.length <= 0f) return 0f;
        const int samples = 300;
        float dt = clip.length / samples, leftMin = float.MaxValue, rightMin = float.MaxValue;
        for (int i = 0; i < samples; i++)
        {
            leftMin = Mathf.Min(leftMin, leftY.Evaluate(i * dt));
            rightMin = Mathf.Min(rightMin, rightY.Evaluate(i * dt));
        }
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < samples; i++)
        {
            float t = i * dt;
            foreach ((AnimationCurve y, AnimationCurve z, float min) in new[] { (leftY, leftZ, leftMin), (rightY, rightZ, rightMin) })
            {
                if (y.Evaluate(t) > min + 0.02f) continue;
                sum += -(z.Evaluate(t + dt) - z.Evaluate(t)) / dt;
                count++;
            }
        }
        float speed = count > 0 ? sum / count : 0f;
        // Standing loops shuffle their feet a little; that is not a stride.
        return speed > 0.3f ? speed * clip.length : 0f;
    }

    // Speed along the ground on her, in m/s. Humanoid clips store their motion for a human of scale 1;
    // she is smaller, so each of her steps covers less ground than the clip's own figure.
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

    static void WriteProfile(float walk, float jog, float aimWalk, float aimJog, float crouchWalk, float crouchAimWalk, float rapidFireInterval,
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
        // The pack has no limp: badly hurt she walks at the walk's own pace and hurries no faster than a jog.
        profile.hurtWalk = walk;
        profile.hurtRun = jog;
        profile.rapidFireInterval = rapidFireInterval;
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
    // files on that skeleton. Turns keep their yaw out of the pose.
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

    // A pack clip's first `frames` frames as a clip of their own, next to the whole one in the same file.
    static AnimationClip PackTake(string path, string name, int frames)
    {
        AnimationClip whole = PackClip(path);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        var clips = new List<ModelImporterClipAnimation>(importer.clipAnimations);
        ModelImporterClipAnimation take = clips.Find(c => c.name == name);
        ModelImporterClipAnimation first = clips[0];
        if (take == null || take.firstFrame != first.firstFrame || !Mathf.Approximately(take.lastFrame, first.firstFrame + frames))
        {
            clips.Remove(take);
            clips.Add(new ModelImporterClipAnimation
            {
                name = name,
                takeName = first.takeName,
                firstFrame = first.firstFrame,
                lastFrame = first.firstFrame + frames,
                loopTime = false,
                lockRootHeightY = first.lockRootHeightY,
                keepOriginalPositionY = first.keepOriginalPositionY,
                heightFromFeet = first.heightFromFeet,
                lockRootRotation = first.lockRootRotation,
                keepOriginalOrientation = first.keepOriginalOrientation,
                heightOffset = first.heightOffset,
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

    // Root motion is off for the body, so whatever a clip leaves as root motion is thrown away.
    // The hips' rise and fall has to stay in the pose, or the legs bend while the body glides level
    // and the feet sink into the floor. Forward travel stays root motion and is dropped, because
    // PlayerController moves the body.
    static void SetClipSettings(ModelImporter importer, bool loop, bool bakeRotation)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            // Extra takes (the hit cut from a fall) never loop.
            bool clipLoop = i == 0 && loop;
            if (clip.loopTime == clipLoop && clip.lockRootHeightY && clip.keepOriginalPositionY && !clip.heightFromFeet
                && clip.lockRootRotation == bakeRotation && (!bakeRotation || clip.keepOriginalOrientation) && clip.heightOffset == 0f)
                continue;
            clip.heightOffset = 0f;
            clip.loopTime = clipLoop;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
            clip.heightFromFeet = false;
            clip.lockRootRotation = bakeRotation;
            if (bakeRotation) clip.keepOriginalOrientation = true;
            changed = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
        if (changed) importer.SaveAndReimport();
    }

    static AnimationClip Load(string path, string name = null)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview") && (name == null || clip.name == name)) return clip;
        }
        throw new InvalidOperationException("No animation clip " + name + " in " + path);
    }

    // How big the model's skeleton is against a human of scale 1, times the scale it is placed at.
    static float HumanScale(PlayerCharacterSetup setup)
    {
        GameObject model = Object.Instantiate(setup.model);
        model.hideFlags = HideFlags.HideAndDontSave;
        float humanScale = model.GetComponentInChildren<Animator>().humanScale;
        Object.DestroyImmediate(model);
        return humanScale * setup.modelScale;
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
        foreach (string name in new[] { "Speed", "Strafe", "Travel", "StopSpeed", "TurnRate", "AimPitch", "AimYaw", "AimBlend", "CrouchBlend", "IdleTime", "MoveScale" })
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        foreach (string name in new[] { "IsAiming", "Danger", "Moving", "Sprint", "Forward", "TurnInPlace", "Grounded", "IsDead", "RapidFire" })
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        foreach (string name in new[] { "Stance", "JumpKind", "DeathDir", "TurnSide", "Weapon" })
            controller.AddParameter(name, AnimatorControllerParameterType.Int);
        foreach (string name in new[] { "Fire", "Reload", "Draw", "Holster", "Swap", "Jump", "Stop", "QuickTurn", "Hit", "Dead" })
            controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        AnimatorControllerParameter[] parameters = controller.parameters;
        foreach (AnimatorControllerParameter parameter in parameters)
        {
            if (parameter.name == "Stance") parameter.defaultInt = Relaxed;
            if (parameter.name == "Grounded") parameter.defaultBool = true;
            if (parameter.name == "MoveScale") parameter.defaultFloat = 1f;
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
