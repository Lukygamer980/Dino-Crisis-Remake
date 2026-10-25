using UnityEngine;
using UnityEngine.AI;

// A double sliding door in a doorway between two rooms. Its halves slide apart into the wall
// whenever the player or a raptor comes close, and close again once nobody is near. Local +Z
// points into the front room. A locked door opens only for a player carrying the required item,
// and a powered one (such as a laser shutter) only once its switch is on. A one-way door starts
// locked and is unlocked by walking up to it from its unlocking side. A door that keeps raptors
// out, like the save room's, never opens for them.
// The panels are left out of the NavMesh bake; only a door raptors can't get through carves the
// doorway out of it, so they path through doors they can open and around ones they can't.
// Opening makes enough noise that a raptor nearby comes to look.
public class SlidingDoor : MonoBehaviour
{
    public Transform leftPanel;
    public Transform rightPanel;
    public float openRadius = 2.5f;
    // How far each half slides, and how much of that per second.
    public float slideDistance = 0.9f;
    public float slideSpeed = 2.5f;
    public float noiseRadius = 9f;
    // Carves the doorway out of the NavMesh while the door is locked shut (assigned by the scene builder).
    public NavMeshObstacle blocker;

    [Header("Rooms on either side")]
    public string frontRoomName = "";
    public string backRoomName = "";

    [Header("Lock")]
    public string requiredItem = "";
    public string lockedMessage = "It's locked.";
    // Walking out through the back of this door finishes the demo.
    public bool endsDemo;
    public PowerSwitch powerSource;
    // 1 or -1: a one-way door, unlocked from the front (+Z) or the back. 0 for a normal door.
    public int unlocksFromSide;
    public bool keepsRaptorsOut;

    PlayerController player;
    Vector3 leftClosed;
    Vector3 rightClosed;
    float open;
    int playerSide;
    bool playerWasNear;
    bool wasOpening;
    bool unlockedFromSide;
    bool finished;

    void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
        leftClosed = leftPanel.localPosition;
        rightClosed = rightPanel.localPosition;
        playerSide = SideOf(player.transform.position);
    }

    void Update()
    {
        bool playerNear = !player.IsDead && IsNear(player.transform.position);
        if (unlocksFromSide != 0 && !unlockedFromSide && playerNear && SideOf(player.transform.position) == unlocksFromSide)
        {
            unlockedFromSide = true;
            GameUI.Instance.ShowMessage("You unlock the door from this side.");
        }
        bool locked = (requiredItem != "" && !player.HasItem(requiredItem))
            || (powerSource != null && !powerSource.IsOn)
            || (unlocksFromSide != 0 && !unlockedFromSide);
        if (locked && playerNear && !playerWasNear) GameUI.Instance.ShowMessage(lockedMessage);
        playerWasNear = playerNear;

        bool wantOpen = !locked && (playerNear || (!keepsRaptorsOut && RaptorNear()));
        if (wantOpen && !wasOpening && playerNear) Raptor.MakeNoise(transform.position, noiseRadius);
        wasOpening = wantOpen;
        open = Mathf.MoveTowards(open, wantOpen ? 1f : 0f, slideSpeed * Time.deltaTime);
        float slide = Mathf.SmoothStep(0f, 1f, open) * slideDistance;
        leftPanel.localPosition = leftClosed + Vector3.left * slide;
        rightPanel.localPosition = rightClosed + Vector3.right * slide;
        if (blocker != null) blocker.enabled = keepsRaptorsOut || (locked && open <= 0f);

        TrackPlayerSide();
    }

    int SideOf(Vector3 position) => transform.InverseTransformPoint(position).z >= 0f ? 1 : -1;

    bool IsNear(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude < openRadius * openRadius;
    }

    // Raptors remove themselves from the list when they die, so corpses don't hold doors open.
    bool RaptorNear()
    {
        foreach (Raptor raptor in Raptor.All)
        {
            if (IsNear(raptor.transform.position)) return true;
        }
        return false;
    }

    // Announces the room the player walks into. The margin keeps someone standing in the
    // doorway from flipping back and forth between the two names.
    void TrackPlayerSide()
    {
        if (finished) return;
        float depth = transform.InverseTransformPoint(player.transform.position).z;
        int side = depth > 0.4f ? 1 : depth < -0.4f ? -1 : playerSide;
        if (side == playerSide) return;
        playerSide = side;

        if (endsDemo && side < 0 && !player.IsDead)
        {
            finished = true;
            player.inputLocked = true;
            GameUI.Instance.DemoComplete();
            return;
        }
        string roomName = side > 0 ? frontRoomName : backRoomName;
        if (roomName != "") GameUI.Instance.ShowMessage(roomName, 2f);
    }
}
