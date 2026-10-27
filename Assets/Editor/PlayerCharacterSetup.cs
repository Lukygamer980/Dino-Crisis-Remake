using System;
using UnityEngine;

// Everything about the player character that is a choice of asset rather than code: the model, how
// her feet are kept believable, and the handgun and where it sits. Edit it in the Inspector
// (Assets/Characters/Player/Player Character.asset), then run "Dino Demo > Rebuild Character Prefabs"
// and rebuild the scenes.
//
// The clips are the MocapOnline pack's and stay in PlayerAnimatorBuilder: they are one matched set
// whose transitions are tuned to each other. With the pistol holstered she moves on the same clips,
// with empty-handed arms the builder makes from them.
[CreateAssetMenu(menuName = "Dino Demo/Player Character Setup")]
public class PlayerCharacterSetup : ScriptableObject
{
    public const string Path = "Assets/Characters/Player/Player Character.asset";

    [Serializable]
    public struct MaterialSwap
    {
        public Material from;
        public Material to;
    }

    [Header("Model")]
    [Tooltip("A humanoid model or prefab. Its Animator gets the generated controller.")]
    public GameObject model;
    [Tooltip("Uniform scale on the model. Walking speeds follow it, because the feet cover more ground on a bigger body.")]
    public float modelScale = 1f;
    [Tooltip("Children of the model to switch off by name, such as weapons the model ships with.")]
    public string[] hiddenParts = new string[0];
    [Tooltip("Materials to replace on the model, for example a different colour of the same outfit.")]
    public MaterialSwap[] materialSwaps = new MaterialSwap[0];
    [Tooltip("Caps how far the feet bend up toward the shins and twist, for a model in high heels (degrees; Unity's defaults are 50 and 30).")]
    public bool limitFeet;
    public float footUpLimit = 25f;
    public float footTwistLimit = 12f;

    [Tooltip("Plants the feet on steps and slopes with HomebrewIK's foot IK (csHomebrewIK on the model). On flat ground it bends the ankles further than the clips do.")]
    public bool footIK;
    [Tooltip("Unity's Foot IK on the Animator's states: puts the feet exactly where the clip's actor put his. He wore flat shoes, so on a model in heels it bends the ankles; leave it off for one.")]
    public bool stateFootIK;

    [Header("Handgun")]
    public GameObject handgun;
    [Tooltip("Where the muzzle is, in the handgun's own space; shots and the flash start there.")]
    public Vector3 muzzle = new(0f, 0.045f, 0.19f);
    [Tooltip("A transform in the model to hold the gun from, by name. Empty uses the right-hand bone.")]
    public string handSocketParent = "";
    public Vector3 gripPosition;
    public Vector3 gripRotation;
    [Tooltip("A transform in the model to holster the gun on, by name. Empty uses the right thigh bone.")]
    public string holsterSocketParent = "";
    public Vector3 holsterPosition;
    public Vector3 holsterRotation;
}
