using UnityEngine;

// What the player's clips do, measured by PlayerAnimatorBuilder and written next to the Animator it
// builds. The pack's root motion moves her, so speeds are not here: only what PlayerController needs
// to time its actions to the clips and to lean her into curves. The defaults only matter without a
// built asset.
public class LocomotionProfile : ScriptableObject
{
    [Header("Shooting")]
    // Holding the trigger fires this often, in step with the continuous-fire clip's recoils.
    public float rapidFireInterval = 0.27f;
    // How long a reload takes, standing and crouched; the rounds go in three quarters of the way.
    public float reloadTime = 1.6f;
    public float crouchReloadTime = 1.6f;
    // How long drawing and holstering take.
    public float drawTime = 1f;
    public float holsterTime = 1.47f;

    [Header("Jumping")]
    // How long each jump's start clip winds up before she leaves the ground, by JumpKind.
    public float[] jumpWindUps = { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f };

    [Header("Turning")]
    // How fast the turn-in-place loops turn her, in degrees per second.
    public float turnInPlaceRate = 135f;
    // How fast the circle loops turn: steering this fast leans her fully into the curve.
    public float runLeanRate = 115f;
    public float aimWalkLeanRate = 38f;
    public float aimJogLeanRate = 95f;
}
