using System.Collections.Generic;
using UnityEngine;

// Tank controls in the style of classic survival horror: W/S move along the facing
// direction, A/D rotate in place, so input never depends on the current camera angle.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 2.2f;
    public float runSpeed = 4.4f;
    public float backwardSpeed = 1.4f;
    public float turnSpeed = 170f;
    public float quickTurnDuration = 0.3f;

    [Header("Combat")]
    public int maxHealth = 100;
    public int ammo = 15;
    public int shotDamage = 1;
    public float shotRange = 15f;
    public float shotCooldown = 0.6f;
    public float autoAimAngle = 70f;

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
    float verticalVelocity;
    float nextShotTime;
    float quickTurnDegreesLeft;
    Vector3 knockback;
    Vector3 gunRestPosition;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Health = maxHealth;
        if (gunModel != null) gunRestPosition = gunModel.localPosition;
    }

    void Update()
    {
        Vector3 motion = knockback;
        knockback = Vector3.MoveTowards(knockback, Vector3.zero, 12f * Time.deltaTime);

        GameUI.Instance.SetPrompt(null);
        bool canAct = !IsDead && !inputLocked;
        bool aiming = canAct && (Input.GetMouseButton(1) || Input.GetKey(KeyCode.K));

        if (canAct)
        {
            float turnInput = Input.GetAxisRaw("Horizontal");
            float moveInput = Input.GetAxisRaw("Vertical");

            if (quickTurnDegreesLeft <= 0f && Input.GetKeyDown(KeyCode.Q)) quickTurnDegreesLeft = 180f;

            if (quickTurnDegreesLeft > 0f)
            {
                float step = Mathf.Min(quickTurnDegreesLeft, 180f / quickTurnDuration * Time.deltaTime);
                transform.Rotate(0f, step, 0f);
                quickTurnDegreesLeft -= step;
            }
            else if (aiming)
            {
                UpdateAiming(turnInput);
            }
            else
            {
                transform.Rotate(0f, turnInput * turnSpeed * Time.deltaTime, 0f);
                // Badly hurt characters limp, like the "Danger" state in the originals.
                float limp = Status == "DANGER" ? 0.6f : 1f;
                float speed = moveInput > 0f ? (Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed) : backwardSpeed;
                motion += transform.forward * (moveInput * speed * limp);
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

    void UpdateAiming(float turnInput)
    {
        Raptor target = FindAimTarget();
        if (target != null)
        {
            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            Quaternion look = Quaternion.LookRotation(toTarget);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 360f * Time.deltaTime);
        }
        else
        {
            transform.Rotate(0f, turnInput * turnSpeed * 0.6f * Time.deltaTime, 0f);
        }

        bool fire = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space);
        if (fire && Time.time >= nextShotTime) Shoot();
    }

    Raptor FindAimTarget()
    {
        Raptor best = null;
        float bestDistance = shotRange;
        foreach (Raptor raptor in Raptor.All)
        {
            Vector3 toRaptor = raptor.transform.position - transform.position;
            toRaptor.y = 0f;
            float distance = toRaptor.magnitude;
            if (distance < bestDistance && Vector3.Angle(transform.forward, toRaptor) < autoAimAngle)
            {
                best = raptor;
                bestDistance = distance;
            }
        }
        return best;
    }

    void Shoot()
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
        Raptor target = FindAimTarget();
        Vector3 direction = target != null ? (target.transform.position + Vector3.up * 0.9f - origin).normalized : transform.forward;
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
    }
}
