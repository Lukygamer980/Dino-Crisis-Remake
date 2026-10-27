using UnityEngine;

// The raptor's model and clips (Assets/Characters/Raptor/Raptor Setup.asset). Edit it in the
// Inspector, then run "Dino Demo > Rebuild Character Prefabs" and rebuild the scenes. Its patrol,
// sight and attack numbers live on the Raptor component of the prefab, Assets/Prefabs/Velociraptor.
[CreateAssetMenu(menuName = "Dino Demo/Raptor Setup")]
public class RaptorSetup : ScriptableObject
{
    public const string Path = "Assets/Characters/Raptor/Raptor Setup.asset";

    [Header("Model")]
    [Tooltip("An animated raptor model or prefab, facing +Z. Empty builds the primitive raptor.")]
    public GameObject model;
    [Tooltip("Uniform scale on the model. Its walk and run speeds follow it.")]
    public float modelScale = 0.85f;

    [Header("Clips (in place, without root motion)")]
    public AnimationClip idle;
    public AnimationClip walk;
    [Tooltip("The trot it investigates and searches at.")]
    public AnimationClip run;
    [Tooltip("The sprint it chases at.")]
    public AnimationClip sprint;
    [Tooltip("The screech when it spots the player.")]
    public AnimationClip alert;
    [Tooltip("The lunge; it plays over the attack's wind-up and recovery.")]
    public AnimationClip attack;
    public AnimationClip hit;
    [Tooltip("Sniffing about while it looks around.")]
    public AnimationClip search;
    public AnimationClip death;
    public AnimationClip deathAlt;

    [Header("Measured speeds (m/s at the model scale), filled in by the builder")]
    public float walkSpeed;
    public float runSpeed;
    public float sprintSpeed;
}
