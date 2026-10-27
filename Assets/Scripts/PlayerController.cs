using System.Collections.Generic;
using UnityEngine;

// Third-person controls: WASD or the left stick moves relative to where the camera looks, and she
// turns to face where she is heading. The MocapOnline pack's root motion moves her: every frame the
// Animator's step goes to the CharacterController, so her feet carry her exactly as far as the clips
// step, at the pace they were recorded. PlayerController only picks the gait (walk, or run with the
// run button), steers her toward the stick on the move, and asks the Animator to turn her on the spot
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
    const float DrawGrabTime = 0.23f, CrouchDrawGrabTime = 0.43f, HolsterStowTime = 0.63f, CrouchHolsterStowTime = 0.53f;

    [Header("Movement")]
    [Tooltip("The gaits' tuning: walk, jog and sprint standing up, and the walk crouched. Edit the asset, also while playing.")]
    public PlayerMovementSettings movement;
    public LocomotionProfile locomotion;
    [Tooltip("Aiming: how hard she turns toward the camera on the move, degrees per second for each degree off.")]
    public float turnResponse = 5f;
    [Tooltip("The fastest she turns on the move aiming, in degrees per second.")]
    public float maxMovingTurnRate = 220f;
    [Tooltip("How quickly a turn builds up and eases off aiming, in degrees per second squared.")]
    public float turnAcceleration = 1200f;
    [Tooltip("Crouched with the pistol down and standing, facing farther off than this she turns on the spot before setting off.")]
    public float turnOnSpotAngle = 50f;
    [Tooltip("Aiming and standing, the camera this far off her facing turns her on the spot, until she is within a third of it. The aim offset points the pistol across what is left.")]
    public float aimTurnAngle = 24f;
    [Tooltip("Aiming and standing, the camera this far off her facing turns her a quarter turn at once.")]
    public float quickTurnAngle = 80f;
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
    // The gait she asks for: walk 1, jog 2, run 3. It stays at the last one when she lets go, so the
    // Animator picks the stop that fits.
    int gait = 1;
    float aimWeight;
    float lookWeight;
    int aimLayer = -1, lookLayer = -1;

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
    static readonly int FidgetTag = Animator.StringToHash("Fidget");

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
            if (rootMotion != null) rootMotion.Moved += OnRootMotion;
            aimLayer = animator.GetLayerIndex("Aim Offset");
            lookLayer = animator.GetLayerIndex("Look Offset");
        }
        SetCrouch(false);
        if (movement == null) Debug.LogWarning("PlayerController has no Player Movement asset; it moves on the defaults. Rebuild the character prefabs.", this);
    }

    void OnDestroy()
    {
        if (rootMotion != null) rootMotion.Moved -= OnRootMotion;
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
        Vector2 stick = canAct ? GameInput.Move.ReadValue<Vector2>() : Vector2.zero;
        Vector3 input = Vector3.ClampMagnitude(new Vector3(stick.x, 0f, stick.y), 1f);
        Vector3 move = input.magnitude < Mathf.Max(0.05f, Movement.deadzone) ? Vector3.zero : view * input;
        bool running = canAct && GameInput.Run.IsPressed();
        // Running gets her up off her knees.
        if (running && IsCrouching && move != Vector3.zero) TryStandUp();
        wantsToMove = move != Vector3.zero;

        Steer(move, input.magnitude, running, view);

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

    // The Animator's step for this frame. A turn clip's own rotation turns her; on the move
    // PlayerController steers her instead, so the circle loops only lean her.
    void OnRootMotion(Vector3 step, Quaternion turn)
    {
        if (ClipTag(TurnTag)) transform.Rotate(0f, Mathf.DeltaAngle(0f, turn.eulerAngles.y), 0f);
        MoveBy(step);
    }

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
    // aiming, she faces the camera and strafes.
    void Steer(Vector3 move, float amount, bool running, Quaternion view)
    {
        float yaw = transform.eulerAngles.y;
        float targetRate = 0f;
        moveX = moveY = 0f;
        if (IsDead)
        {
            turnRate = 0f;
            turningOnSpot = false;
            return;
        }
        bool clipTurning = ClipTag(TurnTag);
        if (IsAiming) stopWait = 0f;

        if (IsAiming)
        {
            float error = Mathf.DeltaAngle(yaw, view.eulerAngles.y);
            if (wantsToMove)
            {
                turningOnSpot = false;
                targetRate = Mathf.Clamp(error * turnResponse * 1.5f, -maxMovingTurnRate, maxMovingTurnRate);
                Vector3 local = Quaternion.Inverse(transform.rotation) * move.normalized;
                gait = !IsCrouching && running ? 2 : 1;
                moveX = local.x * gait;
                moveY = local.z * gait;
                if (animator != null) animator.ResetTrigger("QuickTurn");
            }
            else
            {
                // Standing, the clips turn her: a quarter turn when the camera swings far round,
                // otherwise the turn loop until she faces it.
                turnSide = error < 0f ? -1 : 1;
                if (!clipTurning && Mathf.Abs(error) > quickTurnAngle && animator != null && rootMotion != null)
                {
                    animator.SetInteger("TurnSide", turnSide);
                    animator.SetTrigger("QuickTurn");
                }
                else if (animator != null) animator.ResetTrigger("QuickTurn");
                // The turn loop runs on for its blend out, so she stops it well short of facing the camera.
                turningOnSpot = turningOnSpot ? Mathf.Abs(error) > aimTurnAngle / 3f : Mathf.Abs(error) > aimTurnAngle;
            }
        }
        else
        {
            PlayerMovementSettings ms = Movement;
            bool free = !IsCrouching;
            bool unarmed = IsHolstered;
            bool quickTurn = false;
            if (wantsToMove)
            {
                gait = !running || IsCrouching ? 1 : Status == "DANGER" ? 2 : 3;
                PlayerMovementSettings.Gait g = ms.For(gait);
                float error = Mathf.DeltaAngle(yaw, Quaternion.LookRotation(move).eulerAngles.y);
                turnSide = error < 0f ? -1 : 1;
                bool onTheMove = ClipTag(MoveTag);
                // Holstered and standing, a turn of more than quarterTurnMax (Player Movement) is stepped round on the
                // spot, since two quarter turns would take seconds.
                if (unarmed && !onTheMove && Mathf.Abs(error) > ms.quarterTurnMax) turningOnSpot = true;
                else if (unarmed && !turningOnSpot && !clipTurning && animator != null && rootMotion != null
                    && Mathf.Abs(error) > (onTheMove ? g.turnClipAngle : ms.quarterTurnAngle) && (!onTheMove || (gait < 3 && !IsCrouching && g.turnClipAngle < 180f)))
                {
                    // Holstered, the Mobility pack turns her: a quarter turn from a stand (crouched too),
                    // or its turn on the move from a walk or a jog.
                    animator.SetInteger("TurnSide", turnSide);
                    animator.SetTrigger("QuickTurn");
                    quickTurn = true;
                    turningOnSpot = false;
                }
                // Standing and facing well away, she steps round on the spot first and sets off once nearly there.
                else if (!unarmed && !onTheMove && Mathf.Abs(error) > (free ? ms.turnOnSpotAngle : turnOnSpotAngle)) turningOnSpot = true;
                else if (Mathf.Abs(error) < 30f) turningOnSpot = false;
                if (!turningOnSpot)
                {
                    // While the pack's turn on the move takes over, she doesn't also steer by hand.
                    if (!quickTurn) targetRate = Mathf.Clamp(error * g.turnResponse, -g.maxTurnRate, g.maxTurnRate);
                    // A sharp turn slows her, so she curves round rather than running a wide arc.
                    float sharpness = Mathf.Max(Mathf.Clamp01(1f - (Mathf.Abs(error) - 45f) / 120f), g.sharpTurnSpeedShare);
                    moveY = Mathf.Max(WalkGait, gait * sharpness);
                    // With analog walk, a stick pushed part way walks slower.
                    if (gait == 1 && ms.analogWalk) moveY = Mathf.Lerp(ms.analogWalkMin, 1f, Mathf.InverseLerp(ms.deadzone, 1f, amount));
                }
            }
            else
            {
                turningOnSpot = false;
                // Letting go: a stop clip that starts on a given foot waits for it, so she walks on at
                // the same gait until the Animator takes the stop, at most one cycle.
                if (wasMoving && HasStopClip(Stance(), gait) && ms.For(gait).stopClip && ms.For(gait).stopOnFoot)
                {
                    float[] cycles = Profile.gaitCycles;
                    int index = IsCrouching ? cycles.Length - 1 : gait - 1;
                    float cycle = index >= 0 && index < cycles.Length ? cycles[index] : 1.2f;
                    stopWait = cycle / Mathf.Max(0.1f, ms.For(gait).playbackSpeed) + 0.05f;
                    // Above the stand fade's threshold, at the pace she walked.
                    heldMoveY = Mathf.Max(lastMoveY, 0.35f);
                }
                if (stopWait > 0f && animator != null)
                {
                    stopWait -= Time.deltaTime;
                    if (ClipTag(StopTag)) stopWait = 0f;
                    else moveY = heldMoveY;
                }
            }
            if (!quickTurn && animator != null) animator.ResetTrigger("QuickTurn");
            if (quickTurn) turnRate = 0f;
            if (wantsToMove) stopWait = 0f;
        }
        wasMoving = wantsToMove && !turningOnSpot;
        if (wantsToMove) lastMoveY = moveY;

        // The clip turns her on the spot; on the move, turns build up and ease off rather than snapping.
        if (clipTurning || turningOnSpot || !wantsToMove) targetRate = 0f;
        float ease = !IsAiming ? Movement.For(gait).turnAcceleration : turnAcceleration;
        turnRate = clipTurning ? 0f : Mathf.MoveTowards(turnRate, targetRate, ease * Time.deltaTime);
        transform.Rotate(0f, turnRate * Time.deltaTime, 0f);
    }

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
            animator.SetInteger("JumpKind", kind);
            animator.SetTrigger("Jump");
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
        if (animator != null) animator.SetTrigger("Reload");
        return true;
    }

    void StartDraw()
    {
        if (!IsHolstered || weaponAction != WeaponAction.None || gunHandSocket == null || IsDead) return;
        weaponAction = WeaponAction.Drawing;
        weaponTimer = 0f;
        Play(drawSound);
        if (animator != null) animator.SetTrigger("Draw");
    }

    void StartHolster()
    {
        if (IsHolstered || weaponAction != WeaponAction.None || IsReloading || gunHolsterSocket == null || IsDead) return;
        weaponAction = WeaponAction.Holstering;
        weaponTimer = 0f;
        if (animator != null) animator.SetTrigger("Holster");
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
        float blend = free ? ms.For(gait).blendTime : 0.15f;
        animator.SetFloat("MoveX", moveX, Mathf.Max(0.02f, blend * 0.7f), dt);
        animator.SetFloat("MoveY", moveY, Mathf.Max(0.02f, blend), dt);
        // The gaits' playback speeds, blended as the gaits are.
        float gaitNow = Mathf.Abs(animator.GetFloat("MoveY"));
        float playback = gaitNow <= WalkGait ? ms.walk.playbackSpeed : gaitNow <= JogGait ? Mathf.Lerp(ms.walk.playbackSpeed, ms.jog.playbackSpeed, gaitNow - WalkGait)
            : Mathf.Lerp(ms.jog.playbackSpeed, ms.sprint.playbackSpeed, Mathf.Clamp01(gaitNow - JogGait));
        animator.SetFloat("MoveSpeed", free ? playback : 1f);
        animator.SetBool("UseStart", ms.For(gait).startClip);
        animator.SetBool("UseStop", ms.For(gait).stopClip);
        // Steering as fast as the pack's circle loops turn leans her fully into the curve.
        float leanRate = stance == AimStance ? Mathf.Lerp(p.aimWalkLeanRate, p.aimJogLeanRate, Mathf.Clamp01(moveY - WalkGait))
            : IsHolstered ? (moveY > JogGait ? p.unarmedRunLeanRate : moveY > WalkGait ? p.unarmedJogLeanRate : p.unarmedWalkLeanRate)
            : p.runLeanRate;
        float leanScale = free ? ms.For(gait).lean : 1f;
        animator.SetFloat("Turn", Mathf.Clamp(turnRate / Mathf.Max(1f, leanRate) * leanScale, -1.5f, 1.5f), 0.2f, dt);
        animator.SetInteger("Stance", stance);
        animator.SetBool("IsAiming", IsAiming);
        animator.SetBool("Moving", wantsToMove && !turningOnSpot);
        animator.SetBool("TurnInPlace", turningOnSpot);
        animator.SetInteger("TurnSide", turnSide);
        animator.SetBool("Grounded", !airborne);
        animator.SetBool("RapidFire", rapidFire);
        animator.SetBool("Unarmed", IsHolstered);
        animator.SetInteger("Gait", gait);
        unarmedBlend = Mathf.MoveTowards(unarmedBlend, IsHolstered ? 1f : 0f, 4f * dt);
        animator.SetFloat("UnarmedBlend", unarmedBlend);
        animator.SetFloat("CrouchBlend", CrouchAmount);

        // Where the camera looks: up and down for the pistol or the head, and how far the body still
        // has to turn while she aims, so the pistol stays on the crosshair as her feet catch up.
        float pitch = viewCamera != null ? Mathf.DeltaAngle(0f, viewCamera.transform.eulerAngles.x) : 0f;
        float yawOff = viewCamera != null ? Mathf.DeltaAngle(transform.eulerAngles.y, viewCamera.transform.eulerAngles.y) : 0f;
        animator.SetFloat("AimPitch", Mathf.Clamp(-pitch / 90f, -1f, 1f));
        animator.SetFloat("AimYaw", IsAiming ? Mathf.Clamp(yawOff / 90f, -1f, 1f) : 0f);
        aimWeight = Mathf.MoveTowards(aimWeight, IsAiming ? 1f : 0f, 5f * Time.deltaTime);
        lookWeight = Mathf.MoveTowards(lookWeight, !IsAiming && !IsDead ? 1f : 0f, 3f * Time.deltaTime);
        animator.SetFloat("AimBlend", aimWeight);
        if (aimLayer >= 0) animator.SetLayerWeight(aimLayer, aimWeight);
        if (lookLayer >= 0) animator.SetLayerWeight(lookLayer, lookWeight * 0.7f);

        // Standing still a while, she looks around (holstered: waits or talks to herself, picked afresh
        // each time); the wait starts over in a new stance.
        bool still = (stance == RelaxedStance || stance == AimStance || stance == UnarmedStance) && stance == lastStance && !wantsToMove && !turningOnSpot && !IsReloading && weaponAction == WeaponAction.None;
        // Once a look around begins, the wait for the next one starts over, with a new pick.
        if (ClipTag(FidgetTag)) still = false;
        if (!still && idleTime > 0f) animator.SetInteger("IdleVariant", Random.Range(0, 2));
        idleTime = still ? idleTime + Time.deltaTime : 0f;
        lastStance = stance;
        animator.SetFloat("IdleTime", idleTime);
    }

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
        string text = $"stance {Stance()}  gait {gait}  pace {v.magnitude:F2} m/s\n" +
            $"MoveY {animator.GetFloat("MoveY"):F2}  MoveX {animator.GetFloat("MoveX"):F2}  speed x{animator.GetFloat("MoveSpeed"):F2}\n" +
            $"turn {turnRate:F0} deg/s  lean {animator.GetFloat("Turn"):F2}  on the spot {turningOnSpot}\n" +
            $"state {StateName(state.shortNameHash)} {state.normalizedTime % 1f:F2}{(animator.IsInTransition(0) ? " -> " + StateName(animator.GetNextAnimatorStateInfo(0).shortNameHash) : "")}" +
            (stopWait > 0f ? $"  stop waits {stopWait:F2}s" : "");
        GUI.Box(new Rect(10f, 10f, 440f, 74f), GUIContent.none);
        GUI.Label(new Rect(18f, 14f, 430f, 70f), text);
    }

    static readonly string[] KnownStates = { "Idle", "Move", "Turn Left", "Turn Right", "Quick Turn Left", "Quick Turn Right", "Start Walk", "Start Jog", "Start Run",
        "Walk Stop", "Jog Stop", "Run Stop", "Stop", "Walk Turn Left", "Walk Turn Right", "Jog Turn Left", "Jog Turn Right", "First Step", "Look Around", "Wait", "Talk",
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
        if (animator != null && !rapid) animator.SetTrigger("Fire");
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

        // From the hip she snaps round to face the shot, so the pistol points the right way. Aiming,
        // she already faces the crosshair.
        Vector3 facing = new(direction.x, 0f, direction.z);
        if (!IsAiming && facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(facing);

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

        GameUI.Instance.SetPrompt(nearest != null ? "[E / Cross] " + nearest.Prompt : null);
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
                animator.ResetTrigger("Hit");
                animator.ResetTrigger("Fire");
                animator.ResetTrigger("Jump");
                animator.SetInteger("DeathDir", DeathDirection(fromPosition));
                animator.SetBool("IsDead", true);
            }
            animator.SetTrigger(IsDead ? "Dead" : "Hit");
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
