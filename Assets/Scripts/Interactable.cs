using System.Collections.Generic;
using UnityEngine;

// Anything the player can use with E. The player picks the nearest one in front of them.
public abstract class Interactable : MonoBehaviour
{
    public static readonly List<Interactable> All = new();

    public abstract string Prompt { get; }

    public abstract void Interact(PlayerController player);

    protected virtual void OnEnable() => All.Add(this);

    protected virtual void OnDisable() => All.Remove(this);
}
