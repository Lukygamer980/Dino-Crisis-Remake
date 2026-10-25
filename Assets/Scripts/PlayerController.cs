using System.Collections.Generic;
using UnityEngine;

// Camera-relative controls: WASD moves in screen directions and the character turns to face
// where it is heading. Aiming and shooting work while moving. While the same direction is held,
// the camera basis from when it was pressed is kept, so a hard cut between fixed shots doesn't
// suddenly flip the controls.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.4f;
    public float aimMoveSpeed = 1.6f;
    public float rotationSpeed = 720f;
    // How far the held direction may drift before the controls re-read the current camera.
    public float basisRefreshAngle = 60f;

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

    readonly HashSet<string> items = new();
    CharacterController controller;
    Camera viewCamera;
    float verticalVelocity;
    float nextShotTime;
    Vector3 knockback;
    Vector3 gunRestPosition;
    Vector3 latchedInput;
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
        bool aiming = mouseAim || (canAct && Input.GetKey(KeyCode.K));

        if (canAct)
        {
            Vector3 move = ReadMoveInput();
            // Badly hurt characters limp, like the "Danger" state in the originals.
            float limp = Status == "DANGER" ? 0.6f : 1f;
            float speed = aiming ? aimMoveSpeed : Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
            motion += move * (speed * limp);

            Vector3 aimDirection = aiming ? GetAimDirection(mouseAim, move) : move;
            if (aimDirection.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(aimDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotationSpeed * Time.deltaTime);
            }

            bool fire = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space);
            if (fire && Time.time >= nextShotTime)
            {
                if (mouseAim) Shoot(aimDirection, mouseAimAssistAngle);
                else Shoot(transform.forward, autoAimAngle);
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

    // Returns the world-space move direction (length up to 1) for the WASD input.
    Vector3 ReadMoveInput()
    {
        Vector3 input = new(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude < 0.01f)
        {
            latchedInput = Vector3.zero;
            return Vector3.zero;
        }
        input = Vector3.ClampMagnitude(input, 1f);

        if (latchedInput == Vector3.zero || Vector3.Angle(input, latchedInput) > basisRefreshAngle)
        {
            latchedInput = input;
            moveBasis = CameraBasis();
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

    // Mouse aim faces the cursor on the floor; keyboard aim locks on to the nearest raptor ahead
    // and otherwise turns with the movement, so it still works without a mouse.
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

        Raptor target = FindAimTarget(transform.forward, autoAimAngle);
        if (target == null) return move;
        Vector3 toTarget = target.transform.position - transform.position;
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

        GameUI.Instance.SetPrompt(nearest != null ? "[E] " + nearest.Prompt : null);
        if (nearest != null && Input.GetKeyDown(KeyCode.E)) nearest.Interact(this);
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
