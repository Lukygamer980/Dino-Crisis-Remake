using UnityEngine;
using UnityEngine.AI;

// A double sliding door in a doorway between two rooms. Its halves slide apart into the wall
// whenever the player or a raptor comes close, and close again once nobody is near. Local +Z
// points into the front room. A locked door opens only for a player carrying the required item.
// The panels are left out of the NavMesh bake; only a locked, shut door carves the doorway out
// of it, so raptors path through doors they can open and around ones they can't. Opening makes
// enough noise that a raptor nearby comes to look.
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

    PlayerController player;
    Vector3 leftClosed;
    Vector3 rightClosed;
    float open;
    int playerSide;
    bool playerWasNear;
    bool wasOpening;
    bool finished;

    void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
        leftClosed = leftPanel.localPosition;
        rightClosed = rightPanel.localPosition;
        playerSide = transform.InverseTransformPoint(player.transform.position).z >= 0f ? 1 : -1;
    }

    void Update()
    {
        bool locked = requiredItem != "" && !player.HasItem(requiredItem);
        bool playerNear = !player.IsDead && IsNear(player.transform.position);
        if (locked && playerNear && !playerWasNear) GameUI.Instance.ShowMessage(lockedMessage);
        playerWasNear = playerNear;

        bool wantOpen = !locked && (playerNear || RaptorNear());
        if (wantOpen && !wasOpening && playerNear) Raptor.MakeNoise(transform.position, noiseRadius);
        wasOpening = wantOpen;
        open = Mathf.MoveTowards(open, wantOpen ? 1f : 0f, slideSpeed * Time.deltaTime);
        float slide = Mathf.SmoothStep(0f, 1f, open) * slideDistance;
        leftPanel.localPosition = leftClosed + Vector3.left * slide;
        rightPanel.localPosition = rightClosed + Vector3.right * slide;
        if (blocker != null) blocker.enabled = locked && open <= 0f;

        TrackPlayerSide();
    }

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

        if (endsDemo && side < 0)
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
