using UnityEngine;

// What Eve's clips cover, in metres and degrees per second on her body. PlayerAnimatorBuilder measures
// the clips and writes this asset next to the Animator it builds, so PlayerController moves and turns
// her exactly as far as the feet step. The defaults only matter without a built asset.
public class LocomotionProfile : ScriptableObject
{
    // A turn that leans into the curve: how fast it turns at full lean, and what share of the
    // straight speed the clip keeps while doing so (a tight curve is run slower).
    [System.Serializable]
    public struct LeanTurn
    {
        public float rate;
        public float speedShare;
    }

    [Header("Pistol drawn")]
    public float walk = 1f;
    public float jog = 3.2f;
    public float run = 4.4f;
    public float aimWalk = 1f;
    public float aimJog = 2.6f;
    public float crouchWalk = 1f;
    public float crouchAimWalk = 0.8f;

    // Which unarmed clips the Animator has, and the speeds they cover at their own pace.
    [System.Serializable]
    public struct UnarmedClips
    {
        public float run;
        public float crouchWalk;
        // True while a pistol-pack crouch stands in for the missing unarmed crouch clips.
        public bool crouchStandIn;
        // True when Mixamo quarter turns are in, so standing turns play them.
        public bool quickTurns;
    }

    [Header("Pistol holstered")]
    public float holsteredWalk = 1.13f;
    // The unarmed run at its own pace; PlayerController.unarmedSprintSpeed may go faster.
    public float holsteredRun = 3.07f;
    public UnarmedClips unarmed = new() { run = 3.07f, crouchWalk = 1f, crouchStandIn = true };

    [Header("Badly hurt")]
    public float hurtWalk = 0.6f;
    public float hurtRun = 2.6f;

    [Header("Turning")]
    // The turn-in-place loops step 45 degrees in 0.67 s; faster turns play them faster, up to this.
    public float turnInPlaceRate = 68f;
    public float maxTurnInPlaceRate = 200f;
    public LeanTurn runTurn = new() { rate = 115f, speedShare = 0.45f };
    public LeanTurn aimWalkTurn = new() { rate = 38f, speedShare = 0.78f };
    public LeanTurn aimJogTurn = new() { rate = 95f, speedShare = 0.55f };
}
