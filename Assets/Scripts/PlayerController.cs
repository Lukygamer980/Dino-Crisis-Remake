using System.Collections.Generic;
using UnityEngine;

// Third-person controls: WASD or the left stick moves relative to where the camera looks, and the
// character turns to face where it is heading. Holding aim turns the character with the camera
// and lets it strafe; shots go to the crosshair at the center of the screen, also on the move.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.4f;
    public float aimMoveSpeed = 1.6f;
    public float rotationSpeed = 720f;

    [Header("Combat")]
    public int maxHealth = 100;
    public int ammo = 15;
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
    public Light muzzleFlash;

    [HideInInspector] public bool inputLocked;

    public int Health { get; private set; }
    public bool IsDead => Health <= 0;
    public bool IsAiming { get; private set; }

    public string Status => Health > maxHealth * 0.66f ? "FINE" : Health > maxHealth * 0.33f ? "CAUTION" : "DANGER";

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
        IsAiming = canAct && (Input.GetMouseButton(1) || Input.GetKey(PadAim));

        if (canAct)
        {
            Quaternion view = CameraYaw();
            Vector3 input = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")), 1f);
            Vector3 move = input.sqrMagnitude < 0.01f ? Vector3.zero : view * input;
            // Badly hurt characters limp, like the "Danger" state in the originals.
            float limp = Status == "DANGER" ? 0.6f : 1f;
            bool running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(PadRun);
            float speed = IsAiming ? aimMoveSpeed : running ? runSpeed : walkSpeed;
            motion += move * (speed * limp);

            Vector3 facing = IsAiming ? view * Vector3.forward : move;
            if (facing.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(facing);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotationSpeed * Time.deltaTime);
            }

            // The mouse only shoots while the game has it, so clicking back into the window doesn't fire.
            bool mouseFire = Cursor.lockState == CursorLockMode.Locked && Input.GetMouseButtonDown(0);
            bool fire = mouseFire || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(PadFire);
            if (fire && Time.time >= nextShotTime) Shoot(IsAiming ? aimAssistAngle : hipFireAssistAngle);

            if (!IsAiming) UpdateInteraction();
        }

        if (gunModel != null)
        {
            Vector3 raised = gunRestPosition + new Vector3(-0.04f, 0.3f, 0.2f);
            gunModel.localPosition = Vector3.Lerp(gunModel.localPosition, IsAiming ? raised : gunRestPosition, 15f * Time.deltaTime);
        }

        verticalVelocity = controller.isGrounded ? -1f : verticalVelocity - 20f * Time.deltaTime;
        motion.y = verticalVelocity;
        if (controller.enabled) controller.Move(motion * Time.deltaTime);
    }

    Quaternion CameraYaw() => viewCamera != null ? Quaternion.Euler(0f, viewCamera.transform.eulerAngles.y, 0f) : transform.rotation;

    void Shoot(float assistAngle)
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
        Vector3 direction = (AimPoint(origin, assistAngle) - origin).normalized;

        // Snap to face the shot, so firing on the run or from the hip points the gun the right way.
        Vector3 facing = new(direction.x, 0f, direction.z);
        if (facing.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(facing);

        if (Physics.Raycast(origin, direction, out RaycastHit hit, shotRange, ~0, QueryTriggerInteraction.Ignore))
        {
            Raptor hitRaptor = hit.collider.GetComponentInParent<Raptor>();
            if (hitRaptor != null) hitRaptor.TakeHit(shotDamage, direction);
        }
    }

    // What the crosshair at the center of the screen points at: the raptor closest to it within
    // the assist cone, otherwise whatever the view ray hits.
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
            if (angle < bestAngle)
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
}
