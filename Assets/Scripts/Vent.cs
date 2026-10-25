using UnityEngine;

// One end of a piping duct. Using it puts the player at the vent it is linked to, a step in
// front of it. Raptors can't follow, which makes the ducts a way round locked doors and laser
// shutters. Its forward axis points out of the wall into the room.
public class Vent : Interactable
{
    public Vent exit;
    public string prompt = "Crawl into the duct";

    public override string Prompt => prompt;

    public override void Interact(PlayerController player)
    {
        Vector3 arrival = exit.transform.position + exit.transform.forward * 1.1f;
        arrival.y = 0f;
        player.Teleport(arrival, Quaternion.LookRotation(exit.transform.forward));
        ThirdPersonCamera view = FindAnyObjectByType<ThirdPersonCamera>();
        if (view != null) view.SnapBehindPlayer();
    }
}
