using System.Collections.Generic;
using UnityEngine;

// Camera-relative controls: WASD moves in screen directions and the character turns to face
// where it is heading. Aiming and shooting work while moving. While the same direction stays
// held (or is pressed again right after letting go), the camera basis from when it was first
// pressed is kept, so walking through a hard cut between fixed shots doesn't reverse the
// character; a new direction re-reads the current camera.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.4f;
    public float aimMoveSpeed = 1.6f;
    public float rotationSpeed = 720f;
    // Letting go of the stick for less than this and pushing the same way again keeps walking
    // the same way, even if the camera has cut since the direction was first pressed.
    public float basisHoldAfterRelease = 1f;

    [Header("Combat")]
    public int maxHealth = 100;
    public int ammo = 15;
    public int shotDamage = 1;
    public float shotRange = 15f;
    public float shotCooldown = 0.6f;
    // Lock-on cone for keyboard aiming and for firing from the hip.
    public float autoAimAngle = 70f;
    // Small assist cone around the mouse cursor, so shots at a raptor's silhouette connect.
    public float mouseAimAssistAngle = 12f;

    [Header("Parts (assigned by the scene builder)")]
    public Transform gunModel;
    public Transform gunPoint;
    public Light muzzleFlash;

    [HideInInspector] public bool inputLocked;

    public int Health { get; private set; }
    public bool IsDead => Health <= 0;

    public string Status => Health > maxHealth * 0.66f ? "FINE" : Health > maxHealth * 0.33f ? "CAUTION" : "DANGER";

    // Any real change of direction after a cut re-reads the camera, since players point the stick
    // where they want to go on screen; steering in the old basis would come out mirrored. The
    // tolerance only absorbs thumb wobble on an analog stick.
    const float BasisRefreshAngle = 25f;
    // A held basis within this angle of the camera now showing still belongs to the same shot,
    // so steering just follows the stick instead of snapping to a tracking camera's new angle.
    const float SameShotAngle = 60f;

    // PlayStation (DualSense / DualShock 4) buttons as the legacy Input Manager numbers them on
    // macOS and Windows. Xbox pads number their buttons differently.
    const KeyCode PadRun = KeyCode.JoystickButton0;      // Square
    const KeyCode PadInteract = KeyCode.JoystickButton1; // Cross
    const KeyCode PadAim = KeyCode.JoystickButton6;      // L2
    const KeyCode PadFire = KeyCode.JoystickButton7;     // R2

    readonly HashSet<string> items = new();
    CharacterController controller;
    Camera viewCamera;
    float verticalVelocity;
    float nextShotTime;
    Vector3 knockback;
    Vector3 gunRestPosition;
    Vector3 latchedInput;
    float lastMoveTime = float.NegativeInfinity;
    int lastMoveFrame = -1;
    Raptor lockedTarget;
    Quaternion moveBasis = Quaternion.identity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Health = maxHealth;
        if (gunModel != null) gunRestPosition = gunModel.localPosition;
    }

    void Start() => viewCamera = Camera.main;

    void Update()
    {
        Vector3 motion = knockback;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 12f * Time.deltaTime);

        GameUI.Instance.SetPrompt(null);
        bool canAct = !IsDead && !inputLocked;
        bool mouseAim = canAct && Input.GetMouseButton(1);
        bool lockOnAim = canAct && !mouseAim && (Input.GetKey(KeyCode.K) || Input.GetKey(PadAim));
        bool aiming = mouseAim || lockOnAim;
        if (!lockOnAim) lockedTarget = null;

        if (canAct)
        {
            Vector3 move = ReadMoveInput();
            // Badly hurt characters limp, like the "Danger" state in the originals.
            float limp = Status == "DANGER" ? 0.6f : 1f;
            bool running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(PadRun);
            float speed = aiming ? aimMoveSpeed : running ? runSpeed : walkSpeed;
            motion += move * (speed * limp);

            Vector3 aimDirection = aiming ? GetAimDirection(mouseAim, move) : move;
            if (aimDirection.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(aimDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotationSpeed * Time.deltaTime);
            }

            bool fire = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(PadFire);
            if (fire && Time.time >= nextShotTime)
            {
                // Shoot where the player is steering, not where the body has turned to so far.
                Vector3 shotDirection = aimDirection.sqrMagnitude > 0.01f ? aimDirection.normalized : transform.forward;
                Shoot(shotDirection, mouseAim ? mouseAimAssistAngle : autoAimAngle);
            }

            if (!aiming) UpdateInteraction();
        }

        if (gunModel != null)
        {
            Vector3 raised = gunRestPosition + new Vector3(-0.04f, 0.3f, 0.2f);
            gunModel.localPosition = Vector3.Lerp(gunModel.localPosition, aiming ? raised : gunRestPosition, 15f * Time.deltaTime);
        }

        verticalVelocity = controller.isGrounded ? -1f : verticalVelocity - 20f * Time.deltaTime;
        motion.y = verticalVelocity;
        if (controller.enabled) controller.Move(motion * Time.deltaTime);
    }

    Vector3 ReadMoveInput() => CameraRelative(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")));

    // Turns screen-space input (x right, z up) into a world-space move direction of length up to 1.
    Vector3 CameraRelative(Vector3 input)
    {
        if (input.sqrMagnitude < 0.01f) return Vector3.zero;
        input = Vector3.ClampMagnitude(input, 1f);

        bool held = lastMoveFrame == Time.frameCount - 1;
        bool resumed = Time.time - lastMoveTime <= basisHoldAfterRelease;
        lastMoveTime = Time.time;
        lastMoveFrame = Time.frameCount;

        Quaternion shotBasis = CameraBasis();
        bool steered = Vector3.Angle(input, latchedInput) > BasisRefreshAngle;
        bool sameShot = Quaternion.Angle(shotBasis, moveBasis) < SameShotAngle;
        if (latchedInput == Vector3.zero || !resumed || (steered && !(held && sameShot)))
        {
            latchedInput = input;
            moveBasis = shotBasis;
        }
        else if (held && sameShot)
        {
            // Measure later steering from the stick as it was at the last cut, not at the first press.
            latchedInput = input;
        }
        return moveBasis * input;
    }

    Quaternion CameraBasis()
    {
        if (viewCamera == null) return Quaternion.identity;
        Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
        // A top-down shot has no horizontal forward; its "up" points into the screen instead.
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(viewCamera.transform.up, Vector3.up);
        return Quaternion.LookRotation(forward);
    }

    // Mouse aim faces the cursor on the floor; keyboard and gamepad aim lock on to the nearest raptor
    // ahead and otherwise turn with the movement, so they still work without a mouse.
    Vector3 GetAimDirection(bool mouseAim, Vector3 move)
    {
        if (mouseAim && viewCamera != null)
        {
            Ray ray = viewCamera.ScreenPointToRay(Input.mousePosition);
            Plane floor = new(Vector3.up, transform.position);
            if (floor.Raycast(ray, out float distance))
            {
                Vector3 toCursor = ray.GetPoint(distance) - transform.position;
                toCursor.y = 0f;
                if (toCursor.sqrMagnitude > 0.09f) return toCursor.normalized;
            }
            return transform.forward;
        }

        // Keep the lock until that raptor dies or gets out of range, so turning toward it doesn't
        // bring another one into the cone and switch targets.
        if (lockedTarget != null && (!Raptor.All.Contains(lockedTarget)
            || Vector3.Distance(lockedTarget.transform.position, transform.position) > shotRange))
        {
            lockedTarget = null;
        }
        if (lockedTarget == null) lockedTarget = FindAimTarget(transform.forward, autoAimAngle);
        if (lockedTarget == null) return move;
        Vector3 toTarget = lockedTarget.transform.position - transform.position;
        toTarget.y = 0f;
        return toTarget.normalized;
    }

    Raptor FindAimTarget(Vector3 direction, float maxAngle)
    {
        Raptor best = null;
        float bestDistance = shotRange;
        foreach (Raptor raptor in Raptor.All)
        {
            Vector3 toRaptor = raptor.transform.position - transform.position;
            toRaptor.y = 0f;
            float distance = toRaptor.magnitude;
            if (distance < bestDistance && Vector3.Angle(direction, toRaptor) < maxAngle)
            {
                best = raptor;
                bestDistance = distance;
            }
        }
        return best;
    }

    void Shoot(Vector3 aimDirection, float assistAngle)
    {
        nextShotTime = Time.time + shotCooldown;
        if (ammo <= 0)
        {
            GameUI.Instance.ShowMessage("*click* Out of ammo.");
            return;
        }
        ammo--;
        if (muzzleFlash != null)
        {
            muzzleFlash.enabled = true;
            CancelInvoke(nameof(HideMuzzleFlash));
            Invoke(nameof(HideMuzzleFlash), 0.06f);
        }

        // Gunshots are loud: every raptor nearby comes to investigate.
        foreach (Raptor raptor in Raptor.All)
        {
            if (Vector3.Distance(raptor.transform.position, transform.position) < 18f) raptor.Alert();
        }

        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Raptor target = FindAimTarget(aimDirection, assistAngle);
        Vector3 direction = target != null ? (target.transform.position + Vector3.up * 0.9f - origin).normalized : aimDirection;

        // Snap to face the shot, so firing on the run or from the hip points the gun the right way.
        Vector3 facing = new(direction.x, 0f, direction.z);
        if (facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(facing);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, shotRange, ~0, QueryTriggerInteraction.Ignore))
        {
            Raptor hitRaptor = hit.collider.GetComponentInParent<Raptor>();
            if (hitRaptor != null) hitRaptor.TakeHit(shotDamage, direction);
        }
    }

    void HideMuzzleFlash() => muzzleFlash.enabled = false;

    void UpdateInteraction()
    {
        Interactable nearest = null;
        float nearestDistance = 1.7f;
        foreach (Interactable interactable in Interactable.All)
        {
            Vector3 toTarget = interactable.transform.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance < nearestDistance && Vector3.Dot(transform.forward, toTarget.normalized) > 0.3f)
            {
                nearest = interactable;
                nearestDistance = distance;
            }
        }

        GameUI.Instance.SetPrompt(nearest != null ? "[E / Cross] " + nearest.Prompt : null);
        if (nearest != null && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(PadInteract))) nearest.Interact(this);
    }

    public void TakeDamage(int amount, Vector3 fromPosition)
    {
        if (IsDead) return;
        Health = Mathf.Max(0, Health - amount);
        Vector3 away = transform.position - fromPosition;
        away.y = 0f;
        knockback = away.normalized * 6f;
        GameUI.Instance.FlashDamage();

        if (IsDead)
        {
            GameUI.Instance.SetPrompt(null);
            transform.Rotate(-80f, 0f, 0f);
            GameUI.Instance.GameOver();
        }
    }

    public void Heal(int amount) => Health = Mathf.Min(maxHealth, Health + amount);

    public void AddItem(string item) => items.Add(item);

    public bool HasItem(string item) => items.Contains(item);

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        // A CharacterController overrides transform changes while enabled.
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
        knockback = Vector3.zero;
        // The camera cuts to a new room, so the next input reads the new shot's directions.
        latchedInput = Vector3.zero;
    }
}
