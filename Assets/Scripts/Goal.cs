using UnityEngine;

// Where the prototype stops. Using it says that the game doesn't go on from here; the player
// keeps control and there is no ending screen.
public class Goal : Interactable
{
    public string prompt = "Use";
    [TextArea] public string message = "";

    public override string Prompt => prompt;

    public override void Interact(PlayerController player) => GameUI.Instance.ReachGoal(message);
}
