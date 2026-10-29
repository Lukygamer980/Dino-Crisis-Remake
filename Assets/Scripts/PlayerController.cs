using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Third-person controls: WASD or the left stick moves relative to where the camera looks, and she
// turns to face where she is heading. The MocapOnline pack's root motion moves her: every frame the
// Animator's step goes to the CharacterController, so her feet carry her exactly as far as the clips
// step, at the pace they were recorded. PlayerController only picks the gait (how far the stick is
// pushed sets a creep or a walk; holding the run button jogs, tapping it sprints), steers her
// toward the stick on the move, and asks the Animator to turn her on the spot
// when she stands facing well away; the turn clips' own rotation turns her then.
// Holding aim turns her with the camera and lets her strafe; shots go to the crosshair at the center
// of the screen, also on the move. She can crouch, jump, reload, and holster or draw the pistol;
// holding the trigger keeps firing. Holstered she moves on MocapOnline's Mobility pack: turning
// sharply, from a stand or on the move, plays its quarter turns instead of steering by hand.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // Stances, as PlayerAnimatorBuilder lays them out in the Animator.
    const int RelaxedStance = 1, AimStance = 2, CrouchStance = 3, CrouchAimStance = 4, HurtStance = 5, UnarmedStance = 6, UnarmedCrouchStance = 7, UnarmedHurtStance = 8;
    // Jumps, as the Animator's JumpKind.
    const int JumpStand = 0, JumpRun = 1, JumpAimStand = 2, JumpAimForward = 3, JumpAimLeft = 4, JumpAimRight = 5;
    // Gaits, as the radius of PlayerAnimatorBuilder's movement blends.
    const float WalkGait = 1f, JogGait = 2f, RunGait = 3f;
    // When the pistol reaches her hand while drawing and the holster while holstering, standing and
    // crouched (the frames in the MocapOnline list, at 30 fps).
    public const float DrawGrabTime = 0.23f, CrouchDrawGrabTime = 0.43f, HolsterStowTime = 0.63f, CrouchHolsterStowTime = 0.53f;

    [Header("Movement")]
    [Tooltip("The gaits' tuning: walk, jog and sprint standing up, and the walk crouched. Edit the asset, also while playing.")]
    public PlayerMovementSettings movement;
    public LocomotionProfile locomotion;
    public float jumpSpeed = 4.6f;
    public float standingHeight = 1.7f;
    public float crouchingHeight = 1.15f;

    [Header("Combat")]
    public int maxHealth = 100;
    // Spare rounds. At the start the pistol is loaded from them, so this is all she carries.
    public int ammo = 15;
    public int magazineSize = 7;
    public int shotDamage = 1;
    public float shotRange = 15f;
    public float shotCooldown = 0.3f;
    // Assist cones around the crosshair, in degrees: a small one so aimed shots at a raptor's
    // silhouette connect, a wide one for snap shots from the hip.
    public float aimAssistAngle = 4f;
    public float hipFireAssistAngle = 20f;

    [Header("Parts (assigned by the scene builder)")]
    public Transform gunModel;
    public Transform gunPoint;
    // Where the pistol sits in her right hand, and in its holster.
    public Transform gunHandSocket;
    public Transform gunHolsterSocket;
    public Light muzzleFlash;
    public Animator animator;

    [Header("Sounds and effects")]
    public AudioSource audioSource;
    public AudioClip[] fireSounds = new AudioClip[0];
    public AudioClip[] shellSounds = new AudioClip[0];
    public AudioClip dryFireSound, reloadSound, drawSound;
    // Played at the muzzle on every shot (a child of the gun point, so it travels with the gun).
    public ParticleSystem muzzleEffect;
    // Spawned where a shot lands: on a raptor, on metal (a material named Metal, Door, Duct or
    // Fence), or on anything else.
    public GameObject fleshImpact, metalImpact, stoneImpact;

    [HideInInspector] public bool inputLocked;

    public int Health { get; private set; }
    public bool IsDead => Health <= 0;
    public bool IsAiming { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsHolstered { get; private set; }
    public bool IsReloading => reloadTimer > 0f;
    public int Loaded { get; private set; }
    // 0 standing up to 1 crouched, eased, for the camera.
    public float CrouchAmount { get; private set; }
    // Where shots leave from and raptors look for her.
    public float ChestHeight => Mathf.Lerp(1.2f, 0.75f, CrouchAmount);

    public string Status => Health > maxHealth * 0.66f ? "FINE" : Health > maxHealth * 0.33f ? "CAUTION" : "DANGER";

    enum WeaponAction { None, Drawing, Holstering }

    readonly HashSet<string> items = new();
    CharacterController controller;
    Camera viewCamera;
    RootMotionRelay rootMotion;
    float verticalVelocity;
    float nextShotTime;
    Vector3 knockback;
    Vector3 gunRestPosition;

    // What she asks the Animator for: the gait on (MoveX, MoveY) in her own frame, and whether she
    // sets off, or turns on the spot and to which side.
    float moveX, moveY;
    bool wantsToMove;
    bool turningOnSpot;
    int turnSide = 1;
    // The side a quarter turn under way turns to, and whether to end it now: she faces where she is
    // asked to, let go, or the stick swung the other way.
    int quarterSide = 1;
    bool turnDone;
    // How far the quarter turn under way has turned her toward its side. The packs' quarter turns turn
    // 90 degrees, so past QuarterTurnSpent the clip has little turning left and steering does the rest.
    // A turn on the move can't turn back: swinging the stick round ends it instead.
    float quarterTurned;
    bool quarterOnTheMove;
    // Turning about on the move (Player Movement's pivot), or turning quickly as she sets off (its turn
    // start), and to which side.
    bool pivoting, startTurning;
    // A turn about cut short (let go, or the clip changed) still turning faster than steering does:
    // it eases off at the pivot's rate, so she doesn't spin on.
    bool pivotWindingDown;
    int pivotSide = 1;
    const float QuarterTurnSpent = 80f;
    // The same for the turns about at a sprint, which turn 180 degrees.
    const float TurnAboutSpent = 170f;
    // Farther off than this she faces nearly away, where the stick's side is noise.
    const float FacingAway = 150f;
    // How fast PlayerController steers her on the move, in degrees per second.
    float turnRate;
    // Her pace along the ground from the last animated step; a jump carries it through the air and
    // a little way past the landing.
    Vector3 groundVelocity, airVelocity, landingCarry;
    float idleTime;
    int lastStance;
    float unarmedBlend = 1f;
    // The last MoveY she asked for on the move, which a waiting stop holds.
    float lastMoveY;
    // With the stick pushed only a little she creeps: the walk played this much slower (1 at a walk
    // and faster). A waiting stop keeps it, so her steps carry on at the same pace. The Animator gets
    // it eased, so the playback never jumps.
    float creep = 1f, easedCreep = 1f;
    // How fast the stick is being pushed (deflection per second, held at its peak a moment), and how
    // sharp the push was that set her off (0 gentle, 1 sharp): the start clip plays slower or faster
    // by it (eased), and the Animator skips more or less of its weight shift.
    float lastStickMagnitude, stickSpeed;
    float setOffEnergy = 1f, easedStart = 1f;
    bool wantedToMove;
    // The run button: taps in a row so far, when the last press came, whether those presses are still
    // taps (none held past the hold threshold), and what they ask for: a jog, or a sprint.
    int runTaps;
    float lastRunPress = -10f;
    bool runTapping, runJog, sprinting;
    // The gait she asks for: walk 1, jog 2, run 3. It stays at the last one when she lets go, so the
    // Animator picks the stop that fits.
    int gait = 1;
    float aimWeight;
    float lookWeight;
    int aimLayer = -1, lookLayer = -1, handsLayer = -1;

    float reloadTimer, reloadDuration;
    bool roundsIn;
    // True while she holds the trigger and the pistol keeps firing.
    bool rapidFire;
    // A trigger press the game took, and how many rounds it has fired. A press that hasn't fired yet
    // (a tap during the cooldown, or one let go within the same frame) waits for its shot; after that
    // it lasts while the trigger is held. The first round of a press is a single shot, the rest rapid
    // fire. The click that hands the mouse to the game is never taken, so it never fires.
    bool triggerHeld;
    int pressShots;
    WeaponAction weaponAction;
    float weaponTimer;
    float jumpTimer;
    bool airborne;

    static readonly int MoveTag = Animator.StringToHash("Move");
    static readonly int TurnTag = Animator.StringToHash("Turn");
    static readonly int QuarterTag = Animator.StringToHash("Quarter");
    static readonly int TurnAboutTag = Animator.StringToHash("TurnAbout");
    static readonly int FidgetTag = Animator.StringToHash("Fidget");

    // The Animator's parameters, hashed once (PlayerAnimatorBuilder.AddParameters names them).
    static readonly int MoveXParameter = Animator.StringToHash("MoveX");
    static readonly int MoveYParameter = Animator.StringToHash("MoveY");
    static readonly int TurnParameter = Animator.StringToHash("Turn");
    static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
    static readonly int SetOffEnergyParameter = Animator.StringToHash("SetOffEnergy");
    static readonly int AimPitchParameter = Animator.StringToHash("AimPitch");
    static readonly int AimYawParameter = Animator.StringToHash("AimYaw");
    static readonly int AimBlendParameter = Animator.StringToHash("AimBlend");
    static readonly int CrouchBlendParameter = Animator.StringToHash("CrouchBlend");
    static readonly int UnarmedBlendParameter = Animator.StringToHash("UnarmedBlend");
    static readonly int IdleTimeParameter = Animator.StringToHash("IdleTime");
    static readonly int IsAimingParameter = Animator.StringToHash("IsAiming");
    static readonly int MovingParameter = Animator.StringToHash("Moving");
    static readonly int TurnInPlaceParameter = Animator.StringToHash("TurnInPlace");
    static readonly int TurnDoneParameter = Animator.StringToHash("TurnDone");
    static readonly int GroundedParameter = Animator.StringToHash("Grounded");
    static readonly int IsDeadParameter = Animator.StringToHash("IsDead");
    static readonly int RapidFireParameter = Animator.StringToHash("RapidFire");
    static readonly int UnarmedParameter = Animator.StringToHash("Unarmed");
    static readonly int UseStartParameter = Animator.StringToHash("UseStart");
    static readonly int UseStopParameter = Animator.StringToHash("UseStop");
    static readonly int StanceParameter = Animator.StringToHash("Stance");
    static readonly int JumpKindParameter = Animator.StringToHash("JumpKind");
    static readonly int DeathDirParameter = Animator.StringToHash("DeathDir");
    static readonly int TurnSideParameter = Animator.StringToHash("TurnSide");
    static readonly int GaitParameter = Animator.StringToHash("Gait");
    static readonly int IdleVariantParameter = Animator.StringToHash("IdleVariant");
    static readonly int FireParameter = Animator.StringToHash("Fire");
    static readonly int ReloadParameter = Animator.StringToHash("Reload");
    static readonly int DrawParameter = Animator.StringToHash("Draw");
    static readonly int HolsterParameter = Animator.StringToHash("Holster");
    static readonly int JumpParameter = Animator.StringToHash("Jump");
    static readonly int QuickTurnParameter = Animator.StringToHash("QuickTurn");
    static readonly int SprintTurnParameter = Animator.StringToHash("SprintTurn");
    static readonly int HitParameter = Animator.StringToHash("Hit");
    static readonly int DeadParameter = Animator.StringToHash("Dead");

    LocomotionProfile Profile => locomotion != null ? locomotion : defaultProfile ??= ScriptableObject.CreateInstance<LocomotionProfile>();
    static LocomotionProfile defaultProfile;
    PlayerMovementSettings Movement => movement != null ? movement : defaultMovement ??= ScriptableObject.CreateInstance<PlayerMovementSettings>();
    static PlayerMovementSettings defaultMovement;

    // A stop waiting for the foot its clip starts on: the gait is held this much longer at most.
    float stopWait;
    float heldMoveY;
    bool wasMoving;
    static readonly int StopTag = Animator.StringToHash("Stop");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Health = maxHealth;
        Loaded = Mathf.Min(magazineSize, ammo);
        ammo -= Loaded;
        if (gunModel != null) gunRestPosition = gunModel.localPosition;
        if (animator != null)
        {
            rootMotion = animator.GetComponent<RootMotionRelay>();
            if (rootMotion != null)
            {
                rootMotion.Moved += OnRootMotion;
                rootMotion.IK += OnAnimatorIKPass;
            }
            aimLayer = animator.GetLayerIndex("Aim Offset");
            lookLayer = animator.GetLayerIndex("Look Offset");
            handsLayer = animator.GetLayerIndex("Relaxed Hands");
        }
        SetCrouch(false);
        if (movement == null) Debug.LogWarning("PlayerController has no Player Movement asset; it moves on the defaults. Rebuild the character prefabs.", this);
    }

    void OnDestroy()
    {
        if (rootMotion != null)
        {
            rootMotion.Moved -= OnRootMotion;
            rootMotion.IK -= OnAnimatorIKPass;
        }
    }

    // She starts with the pistol in its holster and her hands empty.
    void Start()
    {
        viewCamera = Camera.main;
        if (gunHolsterSocket != null) PutGun(gunHolsterSocket, holstered: true);
    }

    void Update()
    {
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 12f * Time.deltaTime);

        GameUI.Instance.SetPrompt(null);
        bool canAct = !IsDead && !inputLocked;

        UpdateWeapon(canAct);
        IsAiming = canAct && GameInput.Aim.IsPressed() && !IsHolstered && weaponAction == WeaponAction.None;
        UpdateCrouch(canAct);

        Quaternion view = CameraYaw();
        ReadViewTurn(view);
        bool fromKeys = false;
        Vector2 stick = canAct ? GameInput.MoveStick(out fromKeys) : Vector2.zero;
        float deflection = Movement.Deflection(stick.magnitude);
        Vector3 move = deflection > 0f ? view * new Vector3(stick.x, 0f, stick.y).normalized * deflection : Vector3.zero;
        // How fast the stick asks her to go (Player Movement's stick pace), a creep up to a walk: the keys walk.
        float stickPace = deflection <= 0f ? 0f : fromKeys ? WalkGait : Mathf.Clamp(Movement.stickPace.Evaluate(deflection), 0f, WalkGait);
        UpdateRunButton(canAct);
        // Running gets her up off her knees.
        if (canAct && GameInput.Run.IsPressed() && IsCrouching && move != Vector3.zero) TryStandUp();
        wantsToMove = move != Vector3.zero;
        ReadPush(stick.magnitude, fromKeys);

        Steer(move, stickPace, view);

        if (canAct)
        {
            // The mouse only shoots while the game has it, so clicking back into the window doesn't fire.
            bool mouseOk = Cursor.lockState == CursorLockMode.Locked || !GameInput.FromMouse(GameInput.Fire);
            bool fire = GameInput.Fire.WasPressedThisFrame() && mouseOk;
            bool held = GameInput.Fire.IsPressed() && mouseOk;
            if (fire)
            {
                triggerHeld = true;
                pressShots = 0;
            }
            if (!held && pressShots > 0) triggerHeld = false;
            if (triggerHeld && Time.time >= nextShotTime)
            {
                if (pressShots == 0)
                {
                    triggerHeld = PullTrigger(rapid: false);
                    if (triggerHeld) pressShots = 1;
                }
                // Held down after a shot, the pistol keeps firing at the pace of the continuous-fire clip.
                else if (CanFire && Loaded > 0 && PullTrigger(rapid: true)) pressShots++;
            }
            if (!triggerHeld || pressShots < 2 || !CanFire || Loaded <= 0) rapidFire = false;
            if (GameInput.Jump.WasPressedThisFrame()) TryJump();
            if (!IsAiming) UpdateInteraction();
        }
        else rapidFire = triggerHeld = false;

        // Without an animated model (the primitive stand-in) nothing hands her a step, so she moves by hand.
        if (animator == null || rootMotion == null)
        {
            if (gunModel != null)
            {
                Vector3 raised = gunRestPosition + new Vector3(-0.04f, 0.3f, 0.2f);
                gunModel.localPosition = Vector3.Lerp(gunModel.localPosition, IsAiming ? raised : gunRestPosition, 15f * Time.deltaTime);
            }
            float pace = moveY > JogGait ? 4.4f : moveY > WalkGait ? 3.3f : 1.3f;
            MoveBy(transform.TransformDirection(new Vector3(moveX, 0f, moveY)).normalized * (wantsToMove ? pace * Time.deltaTime : 0f));
        }
        DriveAnimator();
    }

    // ---- Moving and turning ----

    // How sharply the stick is pushed as she sets off (Player Movement's gentle and sharp push speeds).
    // The raw stick is read inside the deadzone too, so a slow push is seen as slow from its start.
    // The keys are always a sharp push.
    void ReadPush(float magnitude, bool fromKeys)
    {
        float dt = Time.deltaTime;
        if (dt > 0f) stickSpeed = Mathf.Max((magnitude - lastStickMagnitude) / dt, stickSpeed * Mathf.Exp(-dt / PushMemory));
        lastStickMagnitude = magnitude;
        if (wantsToMove && !wantedToMove)
            setOffEnergy = fromKeys ? 1f : Mathf.InverseLerp(Movement.gentlePushSpeed, Movement.sharpPushSpeed, stickSpeed);
        wantedToMove = wantsToMove;
    }

    // How long a push's speed is remembered, in seconds.
    const float PushMemory = 0.1f;

    static readonly int[] StartStates = { Animator.StringToHash("Start Walk"), Animator.StringToHash("Start Jog"), Animator.StringToHash("Start Run"), Animator.StringToHash("First Step") };

    // True while the base layer plays, or blends into, a start clip.
    bool InStartClip()
    {
        if (animator == null) return false;
        int current = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
        return System.Array.IndexOf(StartStates, current) >= 0 || System.Array.IndexOf(StartStates, next) >= 0;
    }

    // The run button (Player Movement's run button settings). Every press jogs at once. Held past the
    // hold threshold it is a hold: she jogs until it is let go, then walks. Taps in a row, each pressed
    // within the tap window of the last, sprint for as long as they keep coming, with no jog or walk
    // between them; once they stop she walks again.
    void UpdateRunButton(bool canAct)
    {
        PlayerMovementSettings ms = Movement;
        float now = Time.time;
        bool pressed = canAct && GameInput.Run.IsPressed();
        if (canAct && GameInput.Run.WasPressedThisFrame())
        {
            runTaps = runTapping && now - lastRunPress <= ms.sprintTapWindow ? runTaps + 1 : 1;
            lastRunPress = now;
            runTapping = true;
        }
        // A press held past the threshold is a hold, which ends the taps.
        if (pressed && now - lastRunPress > ms.holdThreshold) runTapping = false;
        bool tapsGoing = canAct && runTapping && now - lastRunPress <= ms.sprintTapWindow;
        runJog = pressed || tapsGoing;
        sprinting = tapsGoing && runTaps >= ms.sprintTaps;
    }

    // The Animator's step for this frame. A turn clip's own rotation turns her; on the move
    // PlayerController steers her instead, so the circle loops only lean her.
    void OnRootMotion(Vector3 step, Quaternion turn)
    {
        bool quarter = ClipTag(QuarterTag) || ClipTag(TurnAboutTag);
        // Aiming, she follows the camera by steering, the turn loop only stepping her feet round.
        if ((quarter || ClipTag(TurnTag)) && !(IsAiming && Movement.aimFollowCamera))
        {
            float yaw = Mathf.DeltaAngle(0f, turn.eulerAngles.y);
            transform.Rotate(0f, yaw, 0f);
            if (quarter) quarterTurned += yaw * quarterSide;
        }
        MoveBy(step);
    }

    // After the Animator has posed her: during the holstered turn about, she leans further into it
    // (Player Movement's sprint turn lean, head nod and arms in), as much as the turn has blended in.
    void LateUpdate()
    {
        if (animator == null || !IsHolstered) return;
        float weight = TagWeight(TurnAboutTag);
        if (weight > 0f) LeanIntoTurn(weight);
    }

    void LeanIntoTurn(float weight)
    {
        PlayerMovementSettings ms = Movement;
        Vector3 right = transform.right;
        if (leanBones == null)
            leanBones = new[] { HumanBodyBones.Spine, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm }
                .Select(animator.GetBoneTransform).ToArray();
        Transform spine = leanBones[0], neck = leanBones[1], head = leanBones[2];
        if (spine == null || neck == null || head == null) return;
        spine.rotation = Quaternion.AngleAxis(ms.sprintTurnLean * weight, right) * spine.rotation;
        head.rotation = Quaternion.AngleAxis(ms.sprintTurnHeadNod * weight, right) * head.rotation;
        // The upper arms swing toward hanging straight down the leaning torso.
        Vector3 down = (spine.position - neck.position).normalized;
        for (int i = 3; i < leanBones.Length; i += 2)
        {
            Transform arm = leanBones[i], elbow = leanBones[i + 1];
            if (arm == null || elbow == null) continue;
            Vector3 along = (elbow.position - arm.position).normalized;
            Vector3 toward = Vector3.RotateTowards(along, down, ms.sprintTurnArmsIn * weight * Mathf.Deg2Rad, 0f);
            arm.rotation = Quaternion.FromToRotation(along, toward) * arm.rotation;
        }
    }

    // Spine, neck, head, and each upper arm and forearm, looked up once.
    Transform[] leanBones;

    // How much of the base layer's pose comes from states with this tag, 0 to 1, through transitions.
    float TagWeight(int tag)
    {
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0)) return current.tagHash == tag ? 1f : 0f;
        float blend = animator.GetAnimatorTransitionInfo(0).normalizedTime;
        return (current.tagHash == tag ? 1f - blend : 0f) + (animator.GetNextAnimatorStateInfo(0).tagHash == tag ? blend : 0f);
    }

    // The base layer's IK pass. During a turn about, a foot near the ground is set flat on it: the
    // Mixamo turns, retargeted, leave the planted foot tipped onto its heel or lifted.
    void OnAnimatorIKPass(int layer)
    {
        if (layer != 0 || animator == null) return;
        // As much as the turn about has blended in; the feet are left alone outside it (HomebrewIK, when
        // it is on, sets them then).
        float plant = Movement.turnAboutFootPlant ? TagWeight(TurnAboutTag) : 0f;
        if (plant <= 0f && !feetPlanted) return;
        feetPlanted = plant > 0f;
        PlantFoot(AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight, plant);
        PlantFoot(AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight, plant);
    }

    // Sets a foot that is within Player Movement's foot plant height of the ground flat on it, turned
    // only the way it points, fading out as it lifts, by `plant` (0 lets it go). `bottom` is how far the
    // sole is below its IK goal. The ground is taken to be level with her (the turns about are run on
    // flat floors).
    void PlantFoot(AvatarIKGoal goal, float bottom, float plant)
    {
        float weight = 0f;
        if (plant > 0f)
        {
            Vector3 position = animator.GetIKPosition(goal);
            float ground = transform.position.y;
            float height = Movement.footPlantHeight;
            weight = plant * Mathf.InverseLerp(height, height * 0.3f, position.y - bottom - ground);
            Vector3 forward = animator.GetIKRotation(goal) * Vector3.forward;
            forward.y = 0f;
            if (weight > 0f && forward.sqrMagnitude > 1e-4f)
            {
                animator.SetIKPosition(goal, new Vector3(position.x, ground + bottom, position.z));
                animator.SetIKRotation(goal, Quaternion.LookRotation(forward, Vector3.up));
            }
            else weight = 0f;
        }
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKRotationWeight(goal, weight);
    }

    // Whether PlantFoot set the feet last pass, so it lets them go once.
    bool feetPlanted;

    // Moves the CharacterController by a step along the ground, with gravity, a jump and any knockback.
    void MoveBy(Vector3 step)
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        step.y = 0f;
        if (airborne || jumpTimer > 0f)
        {
            // The pack's jumps are in place: she keeps the pace she had when she jumped.
            step = airVelocity * dt;
        }
        else
        {
            groundVelocity = step / dt;
            landingCarry = Vector3.MoveTowards(landingCarry, Vector3.zero, 8f * dt);
            step += landingCarry * dt;
        }
        Vector3 motion = step + knockback * dt;
        motion.y = Fall() * dt;
        if (controller.enabled) controller.Move(motion);
        if (airborne && controller.isGrounded && verticalVelocity <= 0f)
        {
            airborne = false;
            landingCarry = airVelocity;
        }
    }

    // True while the base layer plays, or blends into, a state with this tag.
    bool ClipTag(int tag)
    {
        if (animator == null) return false;
        if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).tagHash == tag) return true;
        return animator.GetCurrentAnimatorStateInfo(0).tagHash == tag;
    }

    // Picks the gait and the turn. Free movement goes where she faces, so a turn on the move is a curve;
    // aiming, she faces the camera and strafes. `stickPace` is how fast the stick asks her to go:
    // under 1 a creep, 1 the walk.
    void Steer(Vector3 move, float stickPace, Quaternion view)
    {
        float yaw = transform.eulerAngles.y;
        float targetRate = 0f;
        // A turn about that has eased off by reaching the stick hands over to steering at once.
        bool pivotEnded = false;
        moveX = moveY = 0f;
        if (IsDead)
        {
            turnRate = 0f;
            turningOnSpot = pivoting = startTurning = pivotWindingDown = false;
            return;
        }
        bool quarterTurning = ClipTag(QuarterTag);
        bool turningAbout = ClipTag(TurnAboutTag);
        bool clipTurning = quarterTurning || turningAbout || ClipTag(TurnTag);
        turnDone = false;
        PlayerMovementSettings ms = Movement;
        if (IsAiming)
        {
            stopWait = 0f;
            creep = 1f;
            float error = Mathf.DeltaAngle(yaw, view.eulerAngles.y);
            // She turns as fast as the camera does, and makes up what is left of the angle besides, so
            // the body keeps up with the aim instead of trailing it.
            if (ms.aimFollowCamera) targetRate = Mathf.Clamp(viewTurnRate + error * ms.aimTurnResponse, -ms.aimMaxTurnRate, ms.aimMaxTurnRate);
            if (wantsToMove)
            {
                turningOnSpot = false;
                if (!ms.aimFollowCamera) targetRate = Mathf.Clamp(error * ms.aimTurnResponse, -ms.aimMaxTurnRate, ms.aimMaxTurnRate);
                // She keeps facing the camera and steps the stick's way round it, backing off when it
                // points behind her. The stick sets the pace up to a walk; aiming she never runs.
                Vector3 local = Quaternion.Inverse(transform.rotation) * move.normalized;
                float pace = stickPace;
                gait = 1;
                creep = pace < WalkGait ? Mathf.Lerp(ms.creepPlayback, 1f, pace) : 1f;
                moveX = local.x * Mathf.Max(WalkGait, pace);
                moveY = local.z * Mathf.Max(WalkGait, pace);
                // Setting off ends a quarter turn; she strafes round the rest.
                turnDone = quarterTurning;
                if (animator != null) animator.ResetTrigger(QuickTurnParameter);
            }
            else if (ms.aimFollowCamera)
            {
                // Standing, the turn loop steps her feet round while she turns faster than the aim
                // footwork rate.
                if (Mathf.Abs(turnRate) > 1f) turnSide = turnRate < 0f ? -1 : 1;
                turningOnSpot = Mathf.Abs(turnRate) > (turningOnSpot ? ms.aimFootworkRate * 0.5f : ms.aimFootworkRate);
                if (animator != null) animator.ResetTrigger(QuickTurnParameter);
            }
            else
            {
                // Standing, the clips turn her: a quarter turn when the camera swings far round,
                // otherwise the turn loop until she faces it.
                turnSide = error < 0f ? -1 : 1;
                bool turnBack = false;
                if (quarterTurning) turnDone = ClipTurnOver(error, ms.aimTurnAngle / 3f, ms.aimQuarterTurnAngle, QuarterTurnSpent, 180f, out turnBack);
                if ((turnBack || !clipTurning && Mathf.Abs(error) > ms.aimQuarterTurnAngle) && animator != null && rootMotion != null) QuarterTurn(turnSide, false);
                else if (animator != null) animator.ResetTrigger(QuickTurnParameter);
                // The turn loop runs on for its blend out, so she stops it well short of facing the camera.
                turningOnSpot = turningOnSpot ? Mathf.Abs(error) > ms.aimTurnAngle / 3f : Mathf.Abs(error) > ms.aimTurnAngle;
            }
        }
        else
        {
            bool free = !IsCrouching;
            bool unarmed = IsHolstered;
            bool quickTurn = false;
            if (wantsToMove)
            {
                // The stick sets the pace from a creep to a walk; the run button jogs, or sprints while
                // tapped (badly hurt, only jogs). Crouched she creeps or walks.
                float pace = IsCrouching ? stickPace
                    : sprinting ? (Status == "DANGER" ? JogGait : RunGait)
                    : runJog ? JogGait : stickPace;
                gait = pace > 2.5f ? 3 : pace > 1.5f ? 2 : 1;
                creep = pace < WalkGait ? Mathf.Lerp(ms.creepPlayback, 1f, pace) : 1f;
                PlayerMovementSettings.Gait g = ms.For(gait);
                float error = Mathf.DeltaAngle(yaw, Quaternion.LookRotation(move).eulerAngles.y);
                turnSide = error < 0f ? -1 : 1;
                bool onTheMove = ClipTag(MoveTag);
                // Jogging or sprinting, the stick swung far round turns her about quickly. The stick can
                // redirect it any time; only facing nearly away does she keep the side she picked, so
                // stick noise around 180 degrees can't flip it.
                float movingPace = animator != null ? animator.GetFloat(MoveYParameter) : moveY;
                // Sprinting, the stick swung far round plays the turn-about clip, which ends early once she
                // faces the stick or it lets go.
                bool turnAbout = false;
                // It only counts the stick as swung away once it is within a quarter turn on the other side:
                // facing away, its side is noise, and the pistol's turn always turns right.
                if (turningAbout) turnDone = ClipTurnOver(error, ms.turnFinishAngle, 360f, TurnAboutSpent, 90f, out _);
                else if (!pivoting && onTheMove && HasSprintTurn(Stance()) && movingPace >= ms.sprintTurnMinPace && Mathf.Abs(error) > ms.sprintTurnAngle
                    && animator != null && rootMotion != null)
                {
                    // Holstered the clip is mirrored for the left; the pistol's only turns right.
                    int side = IsHolstered ? turnSide : 1;
                    animator.SetInteger(TurnSideParameter, side);
                    animator.SetTrigger(SprintTurnParameter);
                    quarterSide = side;
                    quarterTurned = 0f;
                    turnAbout = true;
                }
                if (!turnAbout && animator != null) animator.ResetTrigger(SprintTurnParameter);
                if (!pivoting && !turnAbout && !turningAbout && onTheMove && movingPace >= ms.pivotMinPace && Mathf.Abs(error) > ms.pivotAngle)
                {
                    pivoting = true;
                    startTurning = false;
                    pivotSide = Mathf.Abs(turnRate) > 1f ? (turnRate < 0f ? -1 : 1) : turnSide;
                }
                // Standing up, holstered or with the pistol down, the stick well off her facing sets her
                // off at once, turning quickly toward it as she steps out (Player Movement's turn start)
                // instead of stepping round on the spot first.
                if (!pivoting && !onTheMove && !clipTurning && !IsCrouching && Mathf.Abs(error) > ms.turnStartAngle)
                {
                    pivoting = startTurning = true;
                    pivotSide = turnSide;
                    turningOnSpot = false;
                }
                if (pivoting && !onTheMove && !startTurning) pivoting = false;
                else if (pivoting && Mathf.Abs(error) < ms.pivotEndAngle)
                {
                    pivoting = startTurning = false;
                    pivotEnded = true;
                }
                else if (pivoting && Mathf.Abs(error) < FacingAway) pivotSide = turnSide;
                // A quarter turn under way ends once she faces the stick, and turns back the other way at
                // once if the stick swings far round.
                if (quarterTurning)
                {
                    turnDone = ClipTurnOver(error, ms.turnFinishAngle, ms.quarterTurnAngle, QuarterTurnSpent, 180f, out bool turnBack);
                    if (turnBack && quarterOnTheMove) turnDone = true;
                    else if (turnBack && unarmed && animator != null && rootMotion != null)
                    {
                        QuarterTurn(turnSide, false);
                        quickTurn = true;
                    }
                }
                // A turn about turns her by itself.
                else if (turningAbout) turningOnSpot = false;
                // Setting off with a turn, she doesn't turn on the spot. Otherwise, holstered and crouched, a
                // turn of more than quarterTurnMax (Player Movement) is stepped round on the spot, since two
                // quarter turns would take seconds.
                else if (unarmed && !startTurning && !onTheMove && Mathf.Abs(error) > ms.quarterTurnMax) turningOnSpot = true;
                else if (unarmed && !startTurning && !turningOnSpot && !clipTurning && !pivoting && animator != null && rootMotion != null
                    && Mathf.Abs(error) > (onTheMove ? g.turnClipAngle : ms.quarterTurnAngle) && (!onTheMove || (gait < 3 && !IsCrouching && g.turnClipAngle < 180f)))
                {
                    // Holstered, the Mobility pack turns her: a quarter turn from a stand (crouched too),
                    // or, if Player Movement asks for it, its turn on the move from a walk or a jog.
                    QuarterTurn(turnSide, onTheMove);
                    quickTurn = true;
                    turningOnSpot = false;
                }
                // Standing and facing well away, she steps round on the spot first and sets off once nearly there.
                else if (!unarmed && !startTurning && !onTheMove && Mathf.Abs(error) > (free ? ms.turnOnSpotAngle : ms.crouchTurnOnSpotAngle)) turningOnSpot = true;
                else if (Mathf.Abs(error) < 30f) turningOnSpot = false;
                if (pivoting)
                {
                    // As fast as it can still ease off in the angle left, so she stops turning where the
                    // stick points instead of spinning past it.
                    float left = pivotSide * error >= 0f ? Mathf.Abs(error) : 360f - Mathf.Abs(error);
                    targetRate = pivotSide * Mathf.Min(startTurning ? ms.turnStartRate : ms.pivotTurnRate, Mathf.Sqrt(2f * PivotAcceleration * left));
                    moveY = Mathf.Max(WalkGait, pace * ms.pivotPaceShare);
                }
                else if (!turningOnSpot)
                {
                    // Facing nearly away, she keeps turning the way she already is.
                    float steer = Mathf.Abs(error) > FacingAway && turnRate * error < 0f ? error - Mathf.Sign(error) * 360f : error;
                    // While the pack's turn on the move takes over, she doesn't also steer by hand.
                    if (!quickTurn) targetRate = Mathf.Clamp(steer * g.turnResponse, -g.maxTurnRate, g.maxTurnRate);
                    // A sharp turn slows her, so she curves round rather than running a wide arc.
                    // (A turn about keeps the sprint, which it runs on into.)
                    float sharpness = turningAbout ? 1f : Mathf.Max(Mathf.Clamp01(1f - (Mathf.Abs(error) - 45f) / 120f), g.sharpTurnSpeedShare);
                    moveY = Mathf.Max(WalkGait, pace * sharpness);
                }
            }
            else
            {
                turningOnSpot = false;
                pivoting = startTurning = false;
                // Letting go ends a quarter turn, or a turn about, where she is.
                turnDone = quarterTurning || turningAbout;
                // Letting go: a stop clip that starts on a given foot waits for it, so she walks on at
                // the same gait until the Animator takes the stop, at most one cycle.
                if (wasMoving && HasStopClip(Stance(), gait) && UseStopClip && ms.For(gait).stopOnFoot)
                {
                    float[] cycles = Profile.gaitCycles;
                    int index = IsCrouching ? cycles.Length - 1 : gait - 1;
                    float cycle = index >= 0 && index < cycles.Length ? cycles[index] : 1.2f;
                    // Holstered either foot will do, so the wait is half a stride at most; walking or
                    // crouched never past the max (faster gaits need their stop to slow down).
                    float stride = cycle * (HasMirroredStop(Stance()) ? 0.5f : 1f);
                    stopWait = stride / Mathf.Max(0.1f, ms.For(gait).playbackSpeed * easedCreep) + 0.05f;
                    if (gait == 1 || IsCrouching) stopWait = Mathf.Min(stopWait, ms.maxStopWait);
                    // Above the stand fade's threshold, at the pace she walked.
                    heldMoveY = Mathf.Max(lastMoveY, 0.35f);
                }
                if (stopWait > 0f && animator != null)
                {
                    stopWait -= Time.deltaTime;
                    if (ClipTag(StopTag)) stopWait = 0f;
                    else moveY = heldMoveY;
                }
                // The stop itself plays at the gait's pace.
                if (stopWait <= 0f) creep = 1f;
            }
            if (!quickTurn && animator != null) animator.ResetTrigger(QuickTurnParameter);
            if (wantsToMove) stopWait = 0f;
        }
        wasMoving = wantsToMove && !turningOnSpot;
        if (wantsToMove) lastMoveY = moveY;

        // The clip turns her on the spot; on the move, turns build up and ease off rather than snapping,
        // also as a turn clip takes over. Steering picks up as soon as a quarter turn is over.
        bool followingAim = IsAiming && ms.aimFollowCamera;
        if (!followingAim && (clipTurning && !(turnDone && wantsToMove) || turningOnSpot || !wantsToMove)) targetRate = 0f;
        if (IsAiming) pivoting = startTurning = false;
        if (pivoting) pivotWindingDown = true;
        else if (Mathf.Abs(turnRate) <= (wantsToMove ? ms.For(gait).maxTurnRate : 1f)) pivotWindingDown = false;
        float ease = pivotWindingDown ? PivotAcceleration : IsAiming ? ms.aimTurnAcceleration : ms.For(gait).turnAcceleration;
        turnRate = pivotEnded ? targetRate : Mathf.MoveTowards(turnRate, targetRate, ease * Time.deltaTime);
        transform.Rotate(0f, turnRate * Time.deltaTime, 0f);
    }

    // How quickly the turn about, or the turn as she sets off, builds up and eases off.
    float PivotAcceleration => startTurning ? Movement.turnStartAcceleration : Movement.pivotAcceleration;

    // Shooting from the hip on the move: where the last shot went, and how much longer the upper body
    // keeps pointing there.
    float hipShotYaw, hipAimTime;

    // How fast the camera is turning, degrees per second, from its yaw last frame.
    float viewTurnRate, lastViewYaw;

    void ReadViewTurn(Quaternion view)
    {
        float yaw = view.eulerAngles.y, dt = Time.deltaTime;
        viewTurnRate = dt > 0f ? Mathf.DeltaAngle(lastViewYaw, yaw) / dt : 0f;
        lastViewYaw = yaw;
    }

    // Asks the Animator for a quarter turn to this side, from a stand or (the pack's turn) on the move.
    void QuarterTurn(int side, bool onTheMove)
    {
        animator.SetInteger(TurnSideParameter, side);
        animator.SetTrigger(QuickTurnParameter);
        quarterSide = side;
        quarterTurned = 0f;
        quarterOnTheMove = onTheMove;
    }

    // While a quarter turn or a turn about plays, `error` degrees off where she is asked to face: whether
    // it is over (within `finish` of it, swung round to the other side within `swungWithin`, or the clip
    // has turned `spent` degrees, all but done), and whether the other side is so far off (past `again`)
    // that she turns back at once.
    bool ClipTurnOver(float error, float finish, float again, float spent, float swungWithin, out bool turnBack)
    {
        int side = error < 0f ? -1 : 1;
        bool swung = side != quarterSide && Mathf.Abs(error) < swungWithin;
        turnBack = swung && Mathf.Abs(error) > again;
        return !turnBack && (Mathf.Abs(error) < finish || swung || quarterTurned > spent);
    }

    // Whether the stance has a turn-about clip at a sprint: holstered or with the pistol down, standing
    // and not badly hurt (which only jogs).
    static bool HasSprintTurn(int stance) => stance is UnarmedStance or RelaxedStance;

    // ---- Crouching and jumping ----

    void UpdateCrouch(bool canAct)
    {
        if (canAct && GameInput.Crouch.WasPressedThisFrame() && !airborne && jumpTimer <= 0f)
        {
            if (IsCrouching) TryStandUp();
            // Crouching leaves the pistol where it is.
            else SetCrouch(true);
        }
        CrouchAmount = Mathf.MoveTowards(CrouchAmount, IsCrouching ? 1f : 0f, 4f * Time.deltaTime);
    }

    void SetCrouch(bool crouch)
    {
        IsCrouching = crouch;
        controller.height = crouch ? crouchingHeight : standingHeight;
        controller.center = new Vector3(0f, controller.height / 2f, 0f);
    }

    // She only stands up where there is room over her head.
    bool TryStandUp()
    {
        if (!IsCrouching) return true;
        Vector3 top = transform.position + Vector3.up * (crouchingHeight - controller.radius);
        if (Physics.SphereCast(top, controller.radius * 0.9f, Vector3.up, out _, standingHeight - crouchingHeight, ~0, QueryTriggerInteraction.Ignore))
            return false;
        SetCrouch(false);
        return true;
    }

    void TryJump()
    {
        // The Mobility pack's jumps are not in: holstered, she doesn't jump.
        if (airborne || jumpTimer > 0f || !controller.isGrounded || IsHolstered) return;
        // Jumping from a crouch only gets her up.
        if (IsCrouching)
        {
            TryStandUp();
            return;
        }
        int kind = JumpStand;
        if (IsAiming)
        {
            if (!wantsToMove || moveY < -Mathf.Abs(moveX)) kind = JumpAimStand;
            else if (Mathf.Abs(moveX) > moveY) kind = moveX < 0f ? JumpAimLeft : JumpAimRight;
            else kind = JumpAimForward;
        }
        else if (moveY > JogGait && groundVelocity.magnitude > 2.5f)
        {
            kind = JumpRun;
        }
        if (animator != null)
        {
            animator.SetInteger(JumpKindParameter, kind);
            animator.SetTrigger(JumpParameter);
        }
        float[] windUps = Profile.jumpWindUps;
        jumpTimer = windUps != null && kind < windUps.Length ? windUps[kind] : 0.3f;
        airVelocity = groundVelocity;
    }

    // Gravity, and the take-off once a jump has wound up.
    float Fall()
    {
        if (jumpTimer > 0f)
        {
            jumpTimer -= Time.deltaTime;
            if (jumpTimer <= 0f)
            {
                verticalVelocity = jumpSpeed;
                airborne = true;
                return verticalVelocity;
            }
        }
        verticalVelocity = controller.isGrounded && verticalVelocity <= 0f ? -1f : verticalVelocity - 20f * Time.deltaTime;
        return verticalVelocity;
    }

    // ---- The pistol ----

    void UpdateWeapon(bool canAct)
    {
        if (reloadTimer > 0f)
        {
            reloadTimer -= Time.deltaTime;
            // The new magazine is in three quarters of the way through.
            if (!roundsIn && reloadTimer <= reloadDuration * 0.25f)
            {
                roundsIn = true;
                int take = Mathf.Min(magazineSize - Loaded, ammo);
                Loaded += take;
                ammo -= take;
            }
        }

        if (weaponAction != WeaponAction.None)
        {
            weaponTimer += Time.deltaTime;
            LocomotionProfile p = Profile;
            if (weaponAction == WeaponAction.Drawing)
            {
                if (IsHolstered && weaponTimer >= (IsCrouching ? CrouchDrawGrabTime : DrawGrabTime)) PutGun(gunHandSocket, holstered: false);
                if (weaponTimer >= (IsCrouching ? p.crouchDrawTime : p.drawTime) * 0.9f) weaponAction = WeaponAction.None;
            }
            else
            {
                if (!IsHolstered && weaponTimer >= (IsCrouching ? CrouchHolsterStowTime : HolsterStowTime)) PutGun(gunHolsterSocket, holstered: true);
                if (weaponTimer >= (IsCrouching ? p.crouchHolsterTime : p.holsterTime) * 0.95f) weaponAction = WeaponAction.None;
            }
        }

        if (!canAct) return;
        if (GameInput.Reload.WasPressedThisFrame()) TryReload();
        if (GameInput.Holster.WasPressedThisFrame())
        {
            if (IsHolstered) StartDraw();
            else StartHolster();
        }
        // Aiming with the pistol away draws it first.
        if (IsHolstered && GameInput.Aim.IsPressed()) StartDraw();
    }

    // Free to shoot: the pistol drawn and in hand, not mid-reload.
    bool CanFire => !IsHolstered && weaponAction == WeaponAction.None && !IsReloading;

    // Returns true when a round went off.
    bool PullTrigger(bool rapid)
    {
        if (IsHolstered)
        {
            StartDraw();
            return false;
        }
        if (weaponAction != WeaponAction.None || IsReloading) return false;
        if (Loaded <= 0)
        {
            nextShotTime = Time.time + shotCooldown;
            Play(dryFireSound);
            if (!TryReload()) GameUI.Instance.ShowMessage("*click* Out of ammo.");
            return false;
        }
        idleTime = 0f;
        Shoot(IsAiming ? aimAssistAngle : hipFireAssistAngle, rapid);
        return true;
    }

    bool TryReload()
    {
        if (IsHolstered || weaponAction != WeaponAction.None || IsReloading || Loaded >= magazineSize || ammo <= 0 || IsDead) return false;
        reloadDuration = IsCrouching ? Profile.crouchReloadTime : IsAiming ? Profile.aimReloadTime : Profile.reloadTime;
        reloadTimer = reloadDuration;
        roundsIn = false;
        Play(reloadSound);
        if (animator != null) animator.SetTrigger(ReloadParameter);
        return true;
    }

    void StartDraw()
    {
        if (!IsHolstered || weaponAction != WeaponAction.None || gunHandSocket == null || IsDead) return;
        weaponAction = WeaponAction.Drawing;
        weaponTimer = 0f;
        Play(drawSound);
        if (animator != null) animator.SetTrigger(DrawParameter);
    }

    void StartHolster()
    {
        if (IsHolstered || weaponAction != WeaponAction.None || IsReloading || gunHolsterSocket == null || IsDead) return;
        weaponAction = WeaponAction.Holstering;
        weaponTimer = 0f;
        if (animator != null) animator.SetTrigger(HolsterParameter);
    }

    void PutGun(Transform socket, bool holstered)
    {
        IsHolstered = holstered;
        if (gunModel == null || socket == null) return;
        gunModel.SetParent(socket, false);
        gunModel.localPosition = Vector3.zero;
        gunModel.localRotation = Quaternion.identity;
    }

    // ---- Animator ----

    int Stance()
    {
        if (IsHolstered) return IsCrouching ? UnarmedCrouchStance : Status == "DANGER" ? UnarmedHurtStance : UnarmedStance;
        if (IsCrouching) return IsAiming ? CrouchAimStance : CrouchStance;
        if (IsAiming) return AimStance;
        return Status == "DANGER" ? HurtStance : RelaxedStance;
    }

    // MoveX and MoveY are the gait she asks for in her own frame (walk 1, jog 2, run 3), eased, and
    // Turn how hard she leans into a curve. Moving and TurnInPlace pick setting off, stopping and
    // turning on the spot.
    void DriveAnimator()
    {
        if (animator == null) return;
        float dt = Time.deltaTime;
        int stance = Stance();
        LocomotionProfile p = Profile;
        PlayerMovementSettings ms = Movement;
        bool free = !IsAiming;
        // Speeding up blends over the faster gait's blend time, slowing down on the move over Player
        // Movement's slow down time.
        bool slowing = wantsToMove && moveY < animator.GetFloat(MoveYParameter) - 0.05f;
        float blend = !free ? 0.15f : slowing ? ms.slowDownTime : ms.For(gait).blendTime;
        animator.SetFloat(MoveXParameter, moveX, Mathf.Max(0.02f, blend * 0.7f), dt);
        animator.SetFloat(MoveYParameter, moveY, Mathf.Max(0.02f, blend), dt);
        // The gaits' playback speeds, blended as the gaits are.
        float gaitNow = Mathf.Abs(animator.GetFloat(MoveYParameter));
        float playback = gaitNow <= WalkGait ? ms.walk.playbackSpeed : gaitNow <= JogGait ? Mathf.Lerp(ms.walk.playbackSpeed, ms.jog.playbackSpeed, gaitNow - WalkGait)
            : Mathf.Lerp(ms.jog.playbackSpeed, ms.sprint.playbackSpeed, Mathf.Clamp01(gaitNow - JogGait));
        easedCreep = Mathf.MoveTowards(easedCreep, creep, 2f * dt);
        float startPlayback = InStartClip() ? Mathf.Lerp(ms.gentleStartPlayback, ms.sharpStartPlayback, setOffEnergy) : 1f;
        easedStart = Mathf.MoveTowards(easedStart, startPlayback, 2f * dt);
        animator.SetFloat(MoveSpeedParameter, (free ? playback : 1f) * easedCreep * easedStart);
        animator.SetFloat(SetOffEnergyParameter, setOffEnergy);
        animator.SetBool(UseStartParameter, ms.For(gait).startClip);
        animator.SetBool(UseStopParameter, UseStopClip);
        // Steering as fast as the pack's circle loops turn leans her fully into the curve.
        float leanRate = stance == AimStance ? Mathf.Lerp(p.aimWalkLeanRate, p.aimJogLeanRate, Mathf.Clamp01(moveY - WalkGait))
            : IsHolstered ? (moveY > JogGait ? p.unarmedRunLeanRate : moveY > WalkGait ? p.unarmedJogLeanRate : p.unarmedWalkLeanRate)
            : p.runLeanRate;
        // Turning about is too quick to lean into.
        float leanScale = pivoting ? 0f : free ? ms.For(gait).lean : 1f;
        animator.SetFloat(TurnParameter, Mathf.Clamp(turnRate / Mathf.Max(1f, leanRate) * leanScale, -1.5f, 1.5f), 0.2f, dt);
        animator.SetInteger(StanceParameter, stance);
        animator.SetBool(IsAimingParameter, IsAiming);
        animator.SetBool(MovingParameter, wantsToMove && !turningOnSpot);
        animator.SetBool(TurnInPlaceParameter, turningOnSpot);
        animator.SetBool(TurnDoneParameter, turnDone);
        animator.SetInteger(TurnSideParameter, turnSide);
        animator.SetBool(GroundedParameter, !airborne);
        animator.SetBool(RapidFireParameter, rapidFire);
        animator.SetBool(UnarmedParameter, IsHolstered);
        animator.SetInteger(GaitParameter, gait);
        unarmedBlend = Mathf.MoveTowards(unarmedBlend, IsHolstered ? 1f : 0f, 4f * dt);
        animator.SetFloat(UnarmedBlendParameter, unarmedBlend);
        animator.SetFloat(CrouchBlendParameter, CrouchAmount);

        // Where the camera looks: up and down for the pistol or the head, and how far the body still
        // has to turn while she aims, so the pistol stays on the crosshair as her feet catch up.
        float pitch = viewCamera != null ? Mathf.DeltaAngle(0f, viewCamera.transform.eulerAngles.x) : 0f;
        float yawOff = viewCamera != null ? Mathf.DeltaAngle(transform.eulerAngles.y, viewCamera.transform.eulerAngles.y) : 0f;
        // Shooting from the hip on the move, the upper body points at the shot instead.
        hipAimTime = IsAiming || IsHolstered ? 0f : Mathf.Max(0f, hipAimTime - dt);
        bool hipAiming = hipAimTime > 0f;
        if (hipAiming) yawOff = Mathf.DeltaAngle(transform.eulerAngles.y, hipShotYaw);
        animator.SetFloat(AimPitchParameter, Mathf.Clamp(-pitch / 90f, -1f, 1f));
        animator.SetFloat(AimYawParameter, IsAiming || hipAiming ? Mathf.Clamp(yawOff / 90f, -1f, 1f) : 0f);
        aimWeight = Mathf.MoveTowards(aimWeight, IsAiming || hipAiming ? 1f : 0f, 5f * Time.deltaTime);
        lookWeight = Mathf.MoveTowards(lookWeight, !IsAiming && !hipAiming && !IsDead ? 1f : 0f, 3f * Time.deltaTime);
        animator.SetFloat(AimBlendParameter, aimWeight);
        if (aimLayer >= 0) animator.SetLayerWeight(aimLayer, aimWeight);
        if (lookLayer >= 0) animator.SetLayerWeight(lookLayer, lookWeight * 0.7f);
        // Holstered, the pack's hands with the thumbs relaxed (Player Movement's relax hands).
        if (handsLayer >= 0) animator.SetLayerWeight(handsLayer, ms.relaxHands ? unarmedBlend : 0f);

        // Standing still a while, she looks around (holstered: waits or talks to herself, picked afresh
        // each time); the wait starts over in a new stance.
        bool still = (stance == RelaxedStance || stance == AimStance || stance == UnarmedStance) && stance == lastStance && !wantsToMove && !turningOnSpot && !IsReloading && weaponAction == WeaponAction.None;
        // Once a look around begins, the wait for the next one starts over, with a new pick.
        if (ClipTag(FidgetTag)) still = false;
        if (!still && idleTime > 0f) animator.SetInteger(IdleVariantParameter, Random.Range(0, 2));
        idleTime = still ? idleTime + Time.deltaTime : 0f;
        lastStance = stance;
        animator.SetFloat(IdleTimeParameter, idleTime);
    }

    // Whether letting go plays a stop clip (Player Movement's per gait, and crouched) or blends to a stand.
    bool UseStopClip => IsCrouching ? Movement.crouchStopClip : Movement.For(gait).stopClip;

    // Whether the stance's stops come mirrored too, for the other foot (holstered, standing or crouched).
    static bool HasMirroredStop(int stance) => stance is UnarmedStance or UnarmedHurtStance or UnarmedCrouchStance;

    // Whether the stance has a stop clip for the gait: holstered for every gait, crouched holstered
    // for the walk, and with the pistol down only from a run.
    static bool HasStopClip(int stance, int gait) => stance switch
    {
        UnarmedStance or UnarmedHurtStance => true,
        UnarmedCrouchStance => gait == 1,
        RelaxedStance or HurtStance => gait == 3,
        _ => false,
    };

    // Player Movement's debug readout: what she asks for and what the Animator plays.
    void OnGUI()
    {
        if (!Movement.showDebug || animator == null) return;
        Vector3 v = controller.velocity;
        v.y = 0f;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        string text = $"stance {Stance()}  gait {gait}  pace {v.magnitude:F2} m/s{(creep < 1f ? $"  creep x{creep:F2}" : "")}\n" +
            $"MoveY {animator.GetFloat(MoveYParameter):F2}  MoveX {animator.GetFloat(MoveXParameter):F2}  speed x{animator.GetFloat(MoveSpeedParameter):F2}\n" +
            $"turn {turnRate:F0} deg/s  lean {animator.GetFloat(TurnParameter):F2}  on the spot {turningOnSpot}\n" +
            $"state {StateName(state.shortNameHash)} {state.normalizedTime % 1f:F2}{(animator.IsInTransition(0) ? " -> " + StateName(animator.GetNextAnimatorStateInfo(0).shortNameHash) : "")}" +
            (stopWait > 0f ? $"  stop waits {stopWait:F2}s" : "");
        GUI.Box(new Rect(10f, 10f, 440f, 74f), GUIContent.none);
        GUI.Label(new Rect(18f, 14f, 430f, 70f), text);
    }

    static readonly string[] KnownStates = { "Idle", "Move", "Turn Left", "Turn Right", "Quick Turn Left", "Quick Turn Right", "Start Walk", "Start Jog", "Start Run",
        "Walk Stop", "Jog Stop", "Run Stop", "Stop", "Walk Stop Mirrored", "Jog Stop Mirrored", "Run Stop Mirrored", "Stop Mirrored", "Sprint Turn Left", "Sprint Turn Right", "Walk Turn Left", "Walk Turn Right", "Jog Turn Left", "Jog Turn Right", "First Step", "Look Around", "Wait", "Talk",
        "Relaxed To Aim", "Aim To Relaxed", "Unarmed To Crouch", "Unarmed Crouch To Stand", "Hit", "Unarmed Hit" };
    static Dictionary<int, string> stateNames;
    static string StateName(int hash)
    {
        if (stateNames == null)
        {
            stateNames = new Dictionary<int, string>();
            foreach (string name in KnownStates) stateNames[Animator.StringToHash(name)] = name;
        }
        return stateNames.TryGetValue(hash, out string found) ? found : "?";
    }

    Quaternion CameraYaw() => viewCamera != null ? Quaternion.Euler(0f, viewCamera.transform.eulerAngles.y, 0f) : transform.rotation;

    // ---- Shooting ----

    // A rapid shot keeps the continuous-fire loop going instead of starting a single shot.
    void Shoot(float assistAngle, bool rapid)
    {
        nextShotTime = Time.time + (rapid ? Profile.rapidFireInterval : shotCooldown);
        rapidFire = rapid;
        Loaded--;
        if (animator != null && !rapid) animator.SetTrigger(FireParameter);
        if (muzzleFlash != null)
        {
            muzzleFlash.enabled = true;
            CancelInvoke(nameof(HideMuzzleFlash));
            Invoke(nameof(HideMuzzleFlash), 0.06f);
        }
        if (muzzleEffect != null) muzzleEffect.Play(true);
        Play(fireSounds);
        if (shellSounds.Length > 0) Invoke(nameof(PlayShell), 0.35f);

        // Gunshots are loud: every raptor nearby comes to investigate.
        Raptor.MakeNoise(transform.position, 18f);

        Vector3 origin = transform.position + Vector3.up * ChestHeight;
        Vector3 direction = (AimPoint(origin, assistAngle) - origin).normalized;

        // From the hip and standing, she snaps round to face the shot, so the pistol points the right way.
        // On the move her legs carry on where the stick takes her and only her upper body turns to the
        // shot (the aim offset), for Player Movement's hip fire aim hold. Aiming, she already faces the
        // crosshair.
        Vector3 facing = new(direction.x, 0f, direction.z);
        if (!IsAiming && facing.sqrMagnitude > 0.01f)
        {
            float shotYaw = Quaternion.LookRotation(facing).eulerAngles.y;
            // A shot farther round than her upper body can twist turns her whole body after all.
            if (wantsToMove && Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, shotYaw)) <= Movement.hipFireMaxTwist)
            {
                hipShotYaw = shotYaw;
                hipAimTime = Movement.hipFireAimHold;
            }
            else
            {
                transform.rotation = Quaternion.LookRotation(facing);
                hipShotYaw = shotYaw;
                hipAimTime = 0f;
            }
        }

        if (Physics.Raycast(origin, direction, out RaycastHit hit, shotRange, ~0, QueryTriggerInteraction.Ignore))
        {
            Raptor hitRaptor = hit.collider.GetComponentInParent<Raptor>();
            if (hitRaptor != null) hitRaptor.TakeHit(shotDamage, direction);
            SpawnImpact(hit, hitRaptor != null);
        }
    }

    void SpawnImpact(RaycastHit hit, bool flesh)
    {
        GameObject prefab = flesh ? fleshImpact : IsMetal(hit.collider) ? metalImpact : stoneImpact;
        if (prefab == null) return;
        GameObject impact = Instantiate(prefab, hit.point, Quaternion.LookRotation(hit.normal));
        Destroy(impact, 3f);
    }

    static bool IsMetal(Collider collider)
    {
        Renderer renderer = collider.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null) return false;
        string name = renderer.sharedMaterial.name;
        return name.Contains("Metal") || name.Contains("Door") || name.Contains("Duct") || name.Contains("Fence");
    }

    void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }

    void Play(AudioClip[] clips)
    {
        if (clips.Length > 0) Play(clips[Random.Range(0, clips.Length)]);
    }

    void PlayShell() => Play(shellSounds);

    // What the crosshair at the center of the screen points at: the raptor closest to it within
    // the assist cone that the shot can actually reach, otherwise whatever the view ray hits.
    Vector3 AimPoint(Vector3 origin, float assistAngle)
    {
        if (viewCamera == null) return origin + transform.forward * shotRange;
        Ray view = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        Raptor target = null;
        float bestAngle = assistAngle;
        foreach (Raptor raptor in Raptor.All)
        {
            Vector3 center = raptor.transform.position + Vector3.up * 0.9f;
            if (Vector3.Distance(center, origin) > shotRange) continue;
            float angle = Vector3.Angle(view.direction, center - view.origin);
            // A wall between the chest and the raptor must not yank the shot or the body toward it.
            if (angle < bestAngle && HasClearShot(origin, center))
            {
                target = raptor;
                bestAngle = angle;
            }
        }
        if (target != null) return target.transform.position + Vector3.up * 0.9f;

        // Start the view ray level with the player, so nothing between the camera and the player counts.
        Vector3 start = view.GetPoint(Mathf.Max(0f, Vector3.Dot(origin - view.origin, view.direction)));
        return Physics.Raycast(start, view.direction, out RaycastHit hit, shotRange, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point
            : start + view.direction * shotRange;
    }

    void HideMuzzleFlash() => muzzleFlash.enabled = false;

    // True when the shot from the chest reaches the point without hitting level geometry first.
    bool HasClearShot(Vector3 origin, Vector3 point)
    {
        Vector3 toPoint = point - origin;
        float distance = toPoint.magnitude;
        if (distance < 0.01f) return true;
        if (!Physics.Raycast(origin, toPoint / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
            return true;
        return hit.collider.GetComponentInParent<Raptor>() != null;
    }

    void UpdateInteraction()
    {
        // Third person: the body faces the last step, so the prompt follows where the camera looks.
        Vector3 facing = viewCamera != null ? viewCamera.transform.forward : transform.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f) facing = transform.forward;
        else facing.Normalize();

        Interactable nearest = null;
        float nearestDistance = 2.4f;
        foreach (Interactable interactable in Interactable.All)
        {
            Vector3 toTarget = interactable.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance < 0.01f || distance >= nearestDistance) continue;
            if (Vector3.Dot(facing, toTarget / distance) > 0.3f)
            {
                nearest = interactable;
                nearestDistance = distance;
            }
        }

        GameUI.Instance.SetPrompt(nearest != null ? "[E / Triangle] " + nearest.Prompt : null);
        if (nearest != null && GameInput.Interact.WasPressedThisFrame()) nearest.Interact(this);
    }

    // ---- Damage and the rest ----

    public void TakeDamage(int amount, Vector3 fromPosition)
    {
        if (IsDead || inputLocked || GameUI.Instance.Ending) return;
        Health = Mathf.Max(0, Health - amount);
        Vector3 away = transform.position - fromPosition;
        away.y = 0f;
        knockback = away.normalized * 6f;
        GameUI.Instance.FlashDamage();
        if (animator != null)
        {
            // A Hit or Fire still pending would leave Dead through Any State and stand her back up.
            if (IsDead)
            {
                animator.ResetTrigger(HitParameter);
                animator.ResetTrigger(FireParameter);
                animator.ResetTrigger(JumpParameter);
                animator.SetInteger(DeathDirParameter, DeathDirection(fromPosition));
                animator.SetBool(IsDeadParameter, true);
            }
            animator.SetTrigger(IsDead ? DeadParameter : HitParameter);
        }

        if (IsDead)
        {
            GameUI.Instance.SetPrompt(null);
            if (animator == null) transform.Rotate(-80f, 0f, 0f);
            GameUI.Instance.GameOver();
        }
    }

    // She falls away from the blow: backward from the front, forward from behind, and sideways
    // away from an attack from the side. Indices follow the Animator's death states.
    int DeathDirection(Vector3 fromPosition)
    {
        Vector3 local = transform.InverseTransformDirection(fromPosition - transform.position);
        if (Mathf.Abs(local.x) > Mathf.Abs(local.z)) return local.x < 0f ? 3 : 2;
        return local.z > 0f ? 1 : 0;
    }

    // Moves the player somewhere else at once, as through a duct. The CharacterController has to be
    // off for that, or it puts the player back.
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
        knockback = Vector3.zero;
        groundVelocity = airVelocity = landingCarry = Vector3.zero;
        turnRate = 0f;
        turningOnSpot = false;
        verticalVelocity = 0f;
        jumpTimer = 0f;
        airborne = false;
    }

    public void Heal(int amount) => Health = Mathf.Min(maxHealth, Health + amount);

    public void AddItem(string item) => items.Add(item);

    public bool HasItem(string item) => items.Contains(item);
}
