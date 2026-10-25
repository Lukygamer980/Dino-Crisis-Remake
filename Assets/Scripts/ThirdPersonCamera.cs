using UnityEngine;

// Third-person camera behind the player, turned with the mouse or the right stick. Holding aim
// pulls it in over the right shoulder. It moves in front of walls and props instead of clipping
// through them, and snaps back behind the player after a door teleport.
[RequireComponent(typeof(Camera))]
public class ThirdPersonCamera : MonoBehaviour
{
    public PlayerController player;

    [Header("Framing")]
    public float distance = 3.2f;
    public float pivotHeight = 1.55f;
    public float shoulderOffset = 0.45f;
    public float fieldOfView = 60f;

    [Header("Aiming")]
    public float aimDistance = 1.3f;
    public float aimShoulderOffset = 0.6f;
    public float aimFieldOfView = 45f;
    public float aimBlendSpeed = 6f;

    [Header("Look")]
    // Degrees per unit of the "Mouse X/Y" axes, and per second at full right-stick tilt.
    public float mouseSensitivity = 2.5f;
    public float stickSpeed = 170f;
    public float minPitch = -35f;
    public float maxPitch = 60f;
    public float defaultPitch = 12f;

    [Header("Collision")]
    public float collisionRadius = 0.2f;
    // How fast the camera eases back out once whatever pushed it in is out of the way.
    public float returnSpeed = 5f;
    // Closer than this (back against a wall) the player would fill the screen, so it is hidden.
    public float hidePlayerDistance = 0.8f;

    float yaw;
    float pitch;
    float aimBlend;
    float currentDistance;
    Vector3 lastPlayerPosition;
    bool playerVisible = true;
    Renderer[] playerRenderers;
    Camera viewCamera;
    readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake() => viewCamera = GetComponent<Camera>();

    void Start()
    {
        playerRenderers = player.GetComponentsInChildren<Renderer>();
        SnapBehindPlayer();
        LockCursor();
    }

    void LateUpdate()
    {
        Transform target = player.transform;
        // Doors teleport the player; a jump this big in one frame is never walking.
        if ((target.position - lastPlayerPosition).sqrMagnitude > 9f) SnapBehindPlayer();
        lastPlayerPosition = target.position;

        bool canLook = !player.IsDead && !player.inputLocked;
        if (canLook) ReadLookInput();

        aimBlend = Mathf.MoveTowards(aimBlend, player.IsAiming ? 1f : 0f, aimBlendSpeed * Time.deltaTime);
        float blend = Mathf.SmoothStep(0f, 1f, aimBlend);
        viewCamera.fieldOfView = Mathf.Lerp(fieldOfView, aimFieldOfView, blend);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        Vector3 shoulder = pivot + rotation * Vector3.right * Mathf.Lerp(shoulderOffset, aimShoulderOffset, blend);
        shoulder = pivot + (shoulder - pivot).normalized * FreeDistance(pivot, shoulder - pivot);

        float wantedDistance = Mathf.Lerp(distance, aimDistance, blend);
        float allowedDistance = FreeDistance(shoulder, rotation * Vector3.back * wantedDistance);
        // Snap in at once so walls never cover the player, but ease back out to avoid popping.
        currentDistance = allowedDistance < currentDistance
            ? allowedDistance
            : Mathf.MoveTowards(currentDistance, allowedDistance, returnSpeed * Time.deltaTime);

        transform.SetPositionAndRotation(shoulder + rotation * Vector3.back * currentDistance, rotation);
        SetPlayerVisible(currentDistance > hidePlayerDistance);

        // In LateUpdate, so the click that gives the game the mouse back doesn't also fire a shot.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (canLook && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            LockCursor();
        }
    }

    void ReadLookInput()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        }
        // Right stick axes from the Input Manager. A PlayStation pad on macOS reports the right stick
        // as the 3rd and 4th joystick axes and L2 / R2 as the 5th and 6th (Windows orders them differently).
        yaw += Input.GetAxis("Look X") * stickSpeed * Time.deltaTime;
        pitch -= Input.GetAxis("Look Y") * stickSpeed * Time.deltaTime;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    // How far the camera can move from `from` along `offset` before it would touch level geometry.
    // Characters are ignored, so a raptor walking past doesn't shove the camera into the player.
    float FreeDistance(Vector3 from, Vector3 offset)
    {
        float length = offset.magnitude;
        if (length < 0.001f) return 0f;
        int count = Physics.SphereCastNonAlloc(from, collisionRadius, offset / length, hits, length, ~0, QueryTriggerInteraction.Ignore);
        float free = length;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider is CharacterController) continue;
            free = Mathf.Min(free, hits[i].distance);
        }
        return free;
    }

    void SetPlayerVisible(bool visible)
    {
        if (visible == playerVisible) return;
        playerVisible = visible;
        foreach (Renderer part in playerRenderers) part.enabled = visible;
    }

    void SnapBehindPlayer()
    {
        yaw = player.transform.eulerAngles.y;
        pitch = defaultPitch;
        currentDistance = distance;
        lastPlayerPosition = player.transform.position;
    }

    static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
