using UnityEngine;

public class Pickup : Interactable
{
    public enum Kind { Ammo, MedPak, KeyCard }

    public Kind kind;
    public int amount = 10;

    public override string Prompt => kind switch
    {
        Kind.Ammo => "Pick up handgun bullets",
        Kind.MedPak => "Pick up Med Pak",
        _ => "Pick up key card",
    };

    public override void Interact(PlayerController player)
    {
        switch (kind)
        {
            case Kind.Ammo:
                player.ammo += amount;
                GameUI.Instance.ShowMessage($"Picked up {amount} handgun bullets.");
                break;
            case Kind.MedPak:
                player.Heal(amount);
                GameUI.Instance.ShowMessage("Used a Med Pak. You feel better.");
                break;
            case Kind.KeyCard:
                player.AddItem("KeyCard");
                GameUI.Instance.ShowMessage("Picked up the key card. The exit can now be opened.");
                break;
        }
        Destroy(gameObject);
    }

    void Update() => transform.Rotate(0f, 90f * Time.deltaTime, 0f);
}
