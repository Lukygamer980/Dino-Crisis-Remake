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
    // How long a reload takes, standing, aiming and crouched; the rounds go in three quarters of the way.
    public float reloadTime = 2f;
    public float aimReloadTime = 2f;
    public float crouchReloadTime = 1.67f;
    // How long drawing and holstering take, standing and crouched.
    public float drawTime = 1f;
    public float crouchDrawTime = 1f;
    public float holsterTime = 1.47f;
    public float crouchHolsterTime = 1.47f;

    [Header("Jumping")]
    // How long each jump's start clip winds up before she leaves the ground, by JumpKind.
    public float[] jumpWindUps = { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f };

    [Header("Turning")]
    // How fast the circle loops turn: steering this fast leans her fully into the curve.
    public float runLeanRate = 115f;
    public float aimWalkLeanRate = 38f;
    public float aimJogLeanRate = 95f;
    // The same without the pistol, walking, jogging and running.
    public float unarmedWalkLeanRate = 40f;
    public float unarmedJogLeanRate = 95f;
    public float unarmedRunLeanRate = 115f;

    [Header("Stopping")]
    // One cycle of the walk, the jog, the run and the crouched walk: the longest a stop waits for the
    // foot its clip starts on.
    public float[] gaitCycles = { 1.2f, 0.8f, 0.6f, 1.2f };
}
