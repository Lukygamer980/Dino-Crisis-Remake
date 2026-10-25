using System.Collections;
using UnityEngine;

// Rooms are not physically connected: a door fades to black and moves the player to the
// linked door's spawn point, the way the originals hid their loading behind door scenes.
public class Door : Interactable
{
    public string roomName = "";
    public Transform destination;
    public string requiredItem = "";
    public string lockedMessage = "It's locked.";
    public bool endsDemo;

    bool transitioning;

    public override string Prompt => "Open door";

    public override void Interact(PlayerController player)
    {
        if (transitioning) return;
        if (requiredItem != "" && !player.HasItem(requiredItem))
        {
            GameUI.Instance.ShowMessage(lockedMessage);
            return;
        }
        if (endsDemo)
        {
            player.inputLocked = true;
            GameUI.Instance.DemoComplete();
            return;
        }
        StartCoroutine(GoThrough(player));
    }

    IEnumerator GoThrough(PlayerController player)
    {
        transitioning = true;
        player.inputLocked = true;
        yield return GameUI.Instance.Fade(1f, 0.35f);
        player.Teleport(destination.position, destination.rotation);
        yield return new WaitForSeconds(0.7f);
        if (roomName != "") GameUI.Instance.ShowMessage(roomName, 2f);
        yield return GameUI.Instance.Fade(0f, 0.35f);
        player.inputLocked = false;
        transitioning = false;
    }
}
