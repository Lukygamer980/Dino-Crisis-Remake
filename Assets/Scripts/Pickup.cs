using UnityEngine;

// Ammo and Med Paks are used up on the spot; an item (a key, an ID card, a code disk) goes into
// the player's inventory under its `item` id, which is what locked doors check for.
public class Pickup : Interactable
{
    public enum Kind { Ammo, MedPak, Item }

    public Kind kind;
    public int amount = 10;

    [Header("Item")]
    public string item = "";
    public string itemName = "";
    // An optional second line, such as what the item opens.
    public string hint = "";

    public override string Prompt => kind switch
    {
        Kind.Ammo => "Pick up handgun bullets",
        Kind.MedPak => "Pick up Med Pak",
        _ => "Pick up " + itemName,
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
            case Kind.Item:
                player.AddItem(item);
                GameUI.Instance.ShowMessage(hint != "" ? $"Picked up the {itemName}.\n{hint}" : $"Picked up the {itemName}.", 3.5f);
                break;
        }
        Destroy(gameObject);
    }

    void Update() => transform.Rotate(0f, 90f * Time.deltaTime, 0f);
}
