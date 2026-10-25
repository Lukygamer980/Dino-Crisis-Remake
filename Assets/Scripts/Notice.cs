using UnityEngine;

// Something the player can examine that only shows a line of text, such as a sealed stairway.
public class Notice : Interactable
{
    public string prompt = "Examine";
    [TextArea] public string message = "";

    public override string Prompt => prompt;

    public override void Interact(PlayerController player) => GameUI.Instance.ShowMessage(message, 3.5f);
}
