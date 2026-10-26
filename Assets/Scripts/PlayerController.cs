using System.Collections.Generic;
using UnityEngine;

// Third-person controls: WASD or the left stick moves relative to where the camera looks, and Eve
// turns to face where she is heading. Standing, she steps round on the spot; on the move she curves
// and leans into the turn, and slows for a sharp one. Holding aim turns her with the camera and lets
// her strafe; shots go to the crosshair at the center of the screen, also on the move.
// She can crouch, jump, reload, and holster or draw the pistol. Her speeds and turn rates come from
// the LocomotionProfile that PlayerAnimatorBuilder measures from the clips, so her feet keep up.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // Stances, as PlayerAnimatorBuilder lays them out in the Animator.
    const int HolsteredStance = 0, RelaxedStance = 1, AimStance = 2, CrouchStance = 3, CrouchAimStance = 4, HurtStance = 5, HurtHolsteredStance = 6, CrouchHolsteredStance = 7;
    // Jumps, as the Animator's JumpKind.
    const int JumpStand = 0, JumpRun = 1, JumpAimStand = 2, JumpAimForward = 3, JumpAimLeft = 4, JumpAimRight = 5;
    // How long a jump winds up before she leaves the ground, and how long a reload takes; the
    // Animator plays its clips to fit (PlayerAnimatorBuilder.JumpWindUp and ReloadTime).
    const float JumpWindUp = 0.25f;
    const float ReloadTime = 1.6f;
    // When the pistol reaches her hand while drawing, and the holster while holstering (from the
    // MocapOnline list, at 30 fps), and when each clip is done.
    const float DrawGrabTime = 0.23f, DrawTime = 0.9f, HolsterStowTime = 0.63f, HolsterTime = 1.4f;

    [Header("Movement")]
    public LocomotionProfile locomotion;
    // How fast the travel speed changes, in m/s². Animated first steps and stops set their own pace.
    public float acceleration = 8f;
    public float deceleration = 10f;
    // How hard she turns toward where she is heading (degrees per second for each degree off), the
    // fastest she turns on the move, and how quickly a turn can build up or ease off.
    public float turnResponse = 5f;
    public float maxMovingTurnRate = 220f;
    public float turnAcceleration = 1200f;
    // Farther off than this, standing, she turns on the spot before she sets off.
    public float turnOnSpotAngle = 50f;
    // Aiming, with the camera this far round, she pivots a quarter turn at once.
    public float quickTurnAngle = 80f;
    // The most she leans into a curve where no clip leans for her, in degrees.
    public float maxLean = 7f;
    public float jumpSpeed = 4.6f;
    public float standingHeight = 1.7f;
    public float crouchingHeight = 1.15f;

    [Header("Unarmed (pistol holstered)")]
    [Tooltip("Sprint speed with the pistol holstered, in m/s. The run clip covers 3.07 m/s at its own pace; faster plays it faster so the feet keep up.")]
    public float unarmedSprintSpeed = 3.1f;
    [Tooltip("Standing and facing farther off than this, she turns before setting off (a Mixamo quarter turn if the clips are in). Below it she sets off at once and curves round.")]
    public float unarmedTurnOnSpotAngle = 120f;
    [Tooltip("The fastest she turns on the move, in degrees per second.")]
    public float unarmedMaxTurnRate = 260f;
    [Tooltip("How quickly a turn builds up and eases off, in degrees per second squared. Lower is smoother.")]
    public float unarmedTurnAcceleration = 700f;
    [Tooltip("In a sharp turn she slows to this share of her speed rather than stopping, so she curves round instead of spinning on the spot.")]
    [Range(0f, 1f)] public float unarmedSharpTurnSpeedShare = 0.35f;

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

    // Planned travel along the ground, before a leaning curve slows it.
    Vector3 travel;
    float travelShare = 1f;
    float turnRate;
    bool turningOnSpot;
    bool wantsToMove;
    bool wasMoving;
    bool holdTravel;
    bool wantsToSprint;
    bool movingForward;
    float lean;
    float speedParameter;
    float idleTime;
    float aimWeight;
    float lookWeight;
    int aimLayer = -1, lookLayer = -1, armsLayer = -1;
    float armsWeight;

    float reloadTimer;
    bool roundsIn;
    WeaponAction weaponAction;
    float weaponTimer;
    float jumpTimer;
    bool airborne;

    static readonly int StepTag = Animator.StringToHash("Step");
    static readonly int IdleState = Animator.StringToHash("Idle");
    static readonly int QuickTurnTag = Animator.StringToHash("QuickTurn");

    LocomotionProfile Profile => locomotion != null ? locomotion : defaultProfile ??= ScriptableObject.CreateInstance<LocomotionProfile>();
    static LocomotionProfile defaultProfile;

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
            aimLayer = animator.GetLayerIndex("Aim Offset");
            lookLayer = animator.GetLayerIndex("Look Offset");
            armsLayer = animator.GetLayerIndex("Unarmed Arms");
        }
        SetCrouch(false);
    }

    void Start() => viewCamera = Camera.main;

    void Update()
    {
        Vector3 motion = knockback;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 12f * Time.deltaTime);

        GameUI.Instance.SetPrompt(null);
        bool canAct = !IsDead && !inputLocked;

        UpdateWeapon(canAct);
        IsAiming = canAct && GameInput.Aim.IsPressed() && !IsHolstered && weaponAction == WeaponAction.None;
        UpdateCrouch(canAct);

        Quaternion view = CameraYaw();
        Vector2 stick = canAct ? GameInput.Move.ReadValue<Vector2>() : Vector2.zero;
        Vector3 input = Vector3.ClampMagnitude(new Vector3(stick.x, 0f, stick.y), 1f);
        Vector3 move = input.sqrMagnitude < 0.01f ? Vector3.zero : view * input;
        bool running = canAct && GameInput.Run.IsPressed();
        // Sprinting gets her up off her knees.
        if (running && IsCrouching && move != Vector3.zero) TryStandUp();
        wantsToMove = move != Vector3.zero;
        wantsToSprint = wantsToMove && running;
        // Letting go on the move can end in a last stride: the Animator picks it from the pace she let
        // go at, and she keeps that pace until the stride takes over.
        if (animator != null)
        {
            if (wasMoving && !wantsToMove)
            {
                animator.SetFloat("StopSpeed", travel.magnitude);
                animator.SetTrigger("Stop");
                holdTravel = true;
            }
            else if (wantsToMove)
            {
                animator.ResetTrigger("Stop");
            }
        }
        wasMoving = wantsToMove;

        Steer(move, input.magnitude, view);
        motion += travel * travelShare;

        if (canAct)
        {
            // The mouse only shoots while the game has it, so clicking back into the window doesn't fire.
            bool fire = GameInput.Fire.WasPressedThisFrame()
                && (Cursor.lockState == CursorLockMode.Locked || !GameInput.FromMouse(GameInput.Fire));
            if (fire && Time.time >= nextShotTime) PullTrigger();
            if (GameInput.Jump.WasPressedThisFrame()) TryJump();
            if (!IsAiming) UpdateInteraction();
        }

        // The pistol pose lives in the animation once Eve is on. The primitive gun still lifts by hand.
        if (gunModel != null && animator == null)
        {
            Vector3 raised = gunRestPosition + new Vector3(-0.04f, 0.3f, 0.2f);
            gunModel.localPosition = Vector3.Lerp(gunModel.localPosition, IsAiming ? raised : gunRestPosition, 15f * Time.deltaTime);
        }

        motion.y = Fall();
        if (controller.enabled) controller.Move(motion * Time.deltaTime);
        if (airborne && controller.isGrounded && verticalVelocity <= 0f) airborne = false;
        DriveAnimator();
        Lean();
    }

    // ---- Moving and turning ----

    // Turns her toward where she is heading and sets how fast she travels. Free movement goes where she
    // faces, so a turn on the move is a curve; aiming, she faces the camera and strafes.
    void Steer(Vector3 move, float amount, Quaternion view)
    {
        LocomotionProfile p = Profile;
        float yaw = transform.eulerAngles.y;
        float targetRate = 0f;
        Vector3 desired = Vector3.zero;
        float speed = travel.magnitude;

        if (IsDead)
        {
            travel = Vector3.zero;
            turnRate = 0f;
            turningOnSpot = false;
            return;
        }

        if (IsAiming)
        {
            float error = Mathf.DeltaAngle(yaw, view.eulerAngles.y);
            float maxRate = speed < 0.3f ? p.maxTurnInPlaceRate : maxMovingTurnRate;
            if (Mathf.Abs(error) > 1.5f) targetRate = Mathf.Clamp(error * turnResponse * 1.5f, -maxRate, maxRate);
            // A quarter turn plays out at the clip's own pace, and her body follows its root.
            if (QuickTurning())
            {
                turnRate = rootMotion.TurnSpeed;
                transform.Rotate(0f, turnRate * Time.deltaTime, 0f);
                turningOnSpot = false;
                travel = Vector3.MoveTowards(travel, Vector3.zero, deceleration * Time.deltaTime);
                travelShare = 1f;
                movingForward = true;
                return;
            }
            if (!wantsToMove && speed < 0.3f && Mathf.Abs(error) > quickTurnAngle && animator != null && rootMotion != null)
            {
                animator.SetInteger("TurnSide", error < 0f ? -1 : 1);
                animator.SetTrigger("QuickTurn");
            }
            else if (animator != null) animator.ResetTrigger("QuickTurn");
            float top = IsCrouching ? p.crouchAimWalk : wantsToSprint ? p.aimJog : p.aimWalk;
            desired = move.normalized * (amount * top);
            movingForward = !wantsToMove || Vector3.Dot(move.normalized, transform.forward) > 0.8f;
            turningOnSpot = !wantsToMove && speed < 0.3f && (turningOnSpot ? Mathf.Abs(targetRate) > 4f : Mathf.Abs(targetRate) > 12f);
        }
        else
        {
            movingForward = true;
            // Unarmed she sets off at once and curves round; only a big turn from a stand happens on
            // the spot, as a Mixamo quarter turn when the clips are in.
            bool unarmed = IsHolstered && !IsCrouching;
            if (unarmed && QuickTurning())
            {
                turnRate = rootMotion.TurnSpeed;
                transform.Rotate(0f, turnRate * Time.deltaTime, 0f);
                turningOnSpot = false;
                travel = Vector3.MoveTowards(travel, Vector3.zero, deceleration * Time.deltaTime);
                travelShare = 1f;
                return;
            }
            if (wantsToMove)
            {
                float error = Mathf.DeltaAngle(yaw, Quaternion.LookRotation(move).eulerAngles.y);
                float onSpotAngle = unarmed ? unarmedTurnOnSpotAngle : turnOnSpotAngle;
                bool standing = speed < (unarmed ? 0.15f : 0.4f);
                bool quickTurn = false;
                // Standing and facing well away, she steps round first and sets off once nearly there.
                if (standing && Mathf.Abs(error) > onSpotAngle)
                {
                    // Only the healthy unarmed stance has quarter turns; hurt she turns in place.
                    if (unarmed && Stance() == HolsteredStance && p.unarmed.quickTurns && animator != null && rootMotion != null)
                    {
                        animator.SetInteger("TurnSide", error < 0f ? -1 : 1);
                        animator.SetTrigger("QuickTurn");
                        quickTurn = true;
                    }
                    else turningOnSpot = true;
                }
                else if (Mathf.Abs(error) < 20f) turningOnSpot = false;
                if (!quickTurn && animator != null) animator.ResetTrigger("QuickTurn");
                float movingRate = unarmed ? unarmedMaxTurnRate : maxMovingTurnRate;
                float maxRate = turningOnSpot ? p.maxTurnInPlaceRate : Mathf.Lerp(movingRate, movingRate * 0.7f, speed / p.run);
                targetRate = Mathf.Clamp(error * turnResponse, -maxRate, maxRate);
                // A sharp turn slows her down, so she curves round rather than running a wide arc.
                float sharpness = Mathf.Clamp01(1f - (Mathf.Abs(error) - 45f) / 120f);
                if (unarmed) sharpness = Mathf.Max(sharpness, unarmedSharpTurnSpeedShare);
                float top = TopSpeed(p, wantsToSprint);
                desired = turningOnSpot || quickTurn ? Vector3.zero : transform.forward * (amount * top * sharpness);
            }
            else
            {
                turningOnSpot = false;
            }
        }

        // Turns build up and ease off rather than snapping.
        float turnEase = IsHolstered && !IsAiming && !IsCrouching ? unarmedTurnAcceleration : turnAcceleration;
        turnRate = Mathf.MoveTowards(turnRate, targetRate, turnEase * Time.deltaTime);
        if (!wantsToMove && !IsAiming) turnRate = Mathf.MoveTowards(turnRate, 0f, turnAcceleration * Time.deltaTime);
        transform.Rotate(0f, turnRate * Time.deltaTime, 0f);
        if (!IsAiming && desired != Vector3.zero) desired = transform.forward * desired.magnitude;

        if (AnimatedStep(out float stepSpeed))
        {
            travel = transform.forward * stepSpeed;
        }
        else if (holdTravel)
        {
            holdTravel = false;
        }
        else
        {
            float rate = desired.sqrMagnitude >= travel.sqrMagnitude ? acceleration : deceleration;
            travel = Vector3.MoveTowards(travel, desired, rate * Time.deltaTime);
            // Free movement keeps going where she faces while she slows.
            if (!IsAiming) travel = transform.forward * travel.magnitude;
        }
        travelShare = CurveShare(p);
    }

    float TopSpeed(LocomotionProfile p, bool sprint)
    {
        if (IsCrouching) return IsHolstered ? p.unarmed.crouchWalk : p.crouchWalk;
        bool hurt = Status == "DANGER";
        if (IsHolstered) return hurt ? (sprint ? p.hurtRun : p.hurtWalk) : sprint ? unarmedSprintSpeed : p.holsteredWalk;
        if (hurt) return sprint ? p.hurtRun : p.hurtWalk;
        return sprint ? p.run : p.walk;
    }

    // In a curve that a clip leans through, she keeps only the share of her speed that the clip's
    // tighter path covers, so her feet don't skate. Elsewhere she keeps it all and leans by hand.
    float CurveShare(LocomotionProfile p)
    {
        LocomotionProfile.LeanTurn curve = default;
        float weight = 0f;
        float speed = travel.magnitude;
        int stance = Stance();
        if (stance == RelaxedStance && speed > p.jog)
        {
            curve = p.runTurn;
            curve.rate *= speed / p.run;
            weight = Mathf.Clamp01((speed - p.jog) / Mathf.Max(0.01f, p.run - p.jog) * 2f);
        }
        else if (stance == AimStance && speed > 0.3f)
        {
            float t = Mathf.InverseLerp(p.aimWalk, p.aimJog, speed);
            curve.rate = Mathf.Lerp(p.aimWalkTurn.rate, p.aimJogTurn.rate, t);
            curve.speedShare = Mathf.Lerp(p.aimWalkTurn.speedShare, p.aimJogTurn.speedShare, t);
            weight = Mathf.Clamp01(Vector3.Dot(travel / speed, transform.forward));
        }
        if (weight <= 0f || curve.rate <= 0f) return 1f;
        float leaning = Mathf.Clamp01(Mathf.Abs(turnRate) / curve.rate) * weight;
        return 1f - leaning * (1f - curve.speedShare);
    }

    // True while an animated step (a first step or a stop) sets her pace, with that pace. While the
    // Animator blends into one she keeps her speed, or stays put if she was standing: the blend's root
    // motion reports a lurch the feet don't make.
    bool AnimatedStep(out float speed)
    {
        speed = 0f;
        if (animator == null || rootMotion == null) return false;
        if (animator.IsInTransition(0))
        {
            if (animator.GetNextAnimatorStateInfo(0).tagHash != StepTag) return false;
            speed = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == IdleState ? 0f : travel.magnitude;
            return true;
        }
        if (animator.GetCurrentAnimatorStateInfo(0).tagHash != StepTag) return false;
        speed = rootMotion.Speed;
        return true;
    }

    // True while a quarter turn plays, or the Animator blends into one.
    bool QuickTurning()
    {
        if (animator == null || rootMotion == null) return false;
        if (animator.IsInTransition(0)) return animator.GetNextAnimatorStateInfo(0).tagHash == QuickTurnTag;
        return animator.GetCurrentAnimatorStateInfo(0).tagHash == QuickTurnTag;
    }

    // Leans the body into a curve where no clip does it: centripetal lean from speed and turn rate.
    void Lean()
    {
        if (animator == null) return;
        int stance = Stance();
        bool clipLeans = (stance == RelaxedStance && travel.magnitude > Profile.jog) || stance == AimStance;
        float target = 0f;
        if (!clipLeans && !airborne && !IsDead)
        {
            float centripetal = travel.magnitude * turnRate * Mathf.Deg2Rad / 9.81f;
            target = Mathf.Clamp(Mathf.Atan(centripetal) * Mathf.Rad2Deg, -maxLean, maxLean);
        }
        lean = Mathf.Lerp(lean, target, 8f * Time.deltaTime);
        animator.transform.localRotation = Quaternion.Euler(0f, 0f, -lean);
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
        if (airborne || jumpTimer > 0f || !controller.isGrounded) return;
        // Jumping from a crouch only gets her up.
        if (IsCrouching)
        {
            TryStandUp();
            return;
        }
        int kind = JumpStand;
        if (IsAiming)
        {
            Vector3 local = transform.InverseTransformDirection(travel);
            if (travel.magnitude < 0.4f || local.z < -Mathf.Abs(local.x)) kind = JumpAimStand;
            else if (Mathf.Abs(local.x) > local.z) kind = local.x < 0f ? JumpAimLeft : JumpAimRight;
            else kind = JumpAimForward;
        }
        else if (travel.magnitude > 2.5f)
        {
            kind = JumpRun;
        }
        if (animator != null)
        {
            animator.SetInteger("JumpKind", kind);
            animator.SetTrigger("Jump");
        }
        jumpTimer = JumpWindUp;
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
            if (!roundsIn && reloadTimer <= ReloadTime * 0.25f)
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
            if (weaponAction == WeaponAction.Drawing)
            {
                if (IsHolstered && weaponTimer >= DrawGrabTime) PutGun(gunHandSocket, holstered: false);
                if (weaponTimer >= DrawTime) weaponAction = WeaponAction.None;
            }
            else
            {
                if (!IsHolstered && weaponTimer >= HolsterStowTime) PutGun(gunHolsterSocket, holstered: true);
                if (weaponTimer >= HolsterTime) weaponAction = WeaponAction.None;
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

    void PullTrigger()
    {
        if (IsHolstered)
        {
            StartDraw();
            return;
        }
        if (weaponAction != WeaponAction.None || IsReloading) return;
        if (Loaded <= 0)
        {
            nextShotTime = Time.time + shotCooldown;
            if (!TryReload()) GameUI.Instance.ShowMessage("*click* Out of ammo.");
            return;
        }
        idleTime = 0f;
        Shoot(IsAiming ? aimAssistAngle : hipFireAssistAngle);
    }

    bool TryReload()
    {
        if (IsHolstered || weaponAction != WeaponAction.None || IsReloading || Loaded >= magazineSize || ammo <= 0 || IsDead) return false;
        reloadTimer = ReloadTime;
        roundsIn = false;
        if (animator != null) animator.SetTrigger("Reload");
        return true;
    }

    void StartDraw()
    {
        if (!IsHolstered || weaponAction != WeaponAction.None || gunHandSocket == null || IsDead) return;
        weaponAction = WeaponAction.Drawing;
        weaponTimer = 0f;
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
        bool hurt = Status == "DANGER";
        if (IsCrouching) return IsAiming ? CrouchAimStance : IsHolstered ? CrouchHolsteredStance : CrouchStance;
        if (IsHolstered) return hurt ? HurtHolsteredStance : HolsteredStance;
        if (IsAiming) return AimStance;
        return hurt ? HurtStance : RelaxedStance;
    }

    // Speed is metres per second along the facing when aiming, otherwise the travel speed; in a leaning
    // curve it is the straight pace the curve was slowed from, so the blend stays in the same gait.
    // Strafe is only used by the aiming blends, because free movement turns the body to face the step.
    // Moving, Sprint and Forward are what the player asks for: they pick first steps, stops and starts.
    void DriveAnimator()
    {
        if (animator == null) return;
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float share = Mathf.Max(0.05f, travelShare);
        int stance = Stance();
        bool strafing = stance == AimStance || stance == CrouchAimStance;
        // Running into a wall stops her at once; the gait eases down rather than snapping to a stand.
        float speed = strafing ? Vector3.Dot(velocity, transform.forward) / share : velocity.magnitude / share;
        speedParameter = speed > speedParameter || strafing ? speed : Mathf.MoveTowards(speedParameter, speed, 25f * Time.deltaTime);
        animator.SetFloat("Speed", speedParameter);
        // The unarmed run sits at its own pace in the blend; a faster sprint plays the state faster.
        LocomotionProfile p = Profile;
        animator.SetFloat("MoveScale", stance == HolsteredStance && speedParameter > p.holsteredRun ? speedParameter / p.holsteredRun : 1f);
        animator.SetFloat("Strafe", strafing ? Vector3.Dot(velocity, transform.right) : 0f);
        animator.SetFloat("Travel", travel.magnitude);
        animator.SetFloat("TurnRate", turnRate);
        animator.SetInteger("Stance", stance);
        animator.SetBool("IsAiming", IsAiming);
        animator.SetBool("Danger", Status == "DANGER");
        animator.SetBool("Moving", wantsToMove);
        animator.SetBool("Sprint", wantsToSprint);
        animator.SetBool("Forward", movingForward);
        animator.SetBool("TurnInPlace", turningOnSpot);
        animator.SetBool("Grounded", !airborne);
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

        // Unarmed, where a pistol-pack clip stands in (turning on the spot, crouching), her arms hang
        // relaxed instead of holding a pistol that is in its holster.
        bool standIn = (turningOnSpot && (stance == HolsteredStance || stance == HurtHolsteredStance))
            || (stance == CrouchHolsteredStance && p.unarmed.crouchStandIn);
        armsWeight = Mathf.MoveTowards(armsWeight, standIn && IsHolstered && weaponAction == WeaponAction.None ? 1f : 0f, 6f * Time.deltaTime);
        if (armsLayer >= 0) animator.SetLayerWeight(armsLayer, armsWeight);

        // Standing still a while, she looks around.
        bool still = stance == RelaxedStance && !wantsToMove && !turningOnSpot && travel.sqrMagnitude < 0.01f && !IsReloading && weaponAction == WeaponAction.None;
        idleTime = still ? idleTime + Time.deltaTime : 0f;
        animator.SetFloat("IdleTime", idleTime);
    }

    Quaternion CameraYaw() => viewCamera != null ? Quaternion.Euler(0f, viewCamera.transform.eulerAngles.y, 0f) : transform.rotation;

    // ---- Shooting ----

    void Shoot(float assistAngle)
    {
        nextShotTime = Time.time + shotCooldown;
        Loaded--;
        if (animator != null) animator.SetTrigger("Fire");
        if (muzzleFlash != null)
        {
            muzzleFlash.enabled = true;
            CancelInvoke(nameof(HideMuzzleFlash));
            Invoke(nameof(HideMuzzleFlash), 0.06f);
        }

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
        }
    }

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
        travel = Vector3.zero;
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
