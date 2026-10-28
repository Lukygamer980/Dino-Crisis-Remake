using System;
using UnityEngine;

// How the player moves, gait by gait: the tuning knobs over the MocapOnline clips. Edit it in the
// Inspector (Assets/Characters/Player/Player Movement.asset), also while playing: everything under
// "Live" takes effect at once and is kept when Play mode ends. Everything under "Animator" is built
// into the Animator, so it needs "Rebuild Animator" (the button on the asset) outside Play mode.
//
// The gaits are used standing up and not aiming, with the pistol down, holstered, or badly hurt.
// The clips' root motion still moves her, so whatever is set here her feet stay planted.
[CreateAssetMenu(menuName = "Dino Demo/Player Movement Settings")]
public class PlayerMovementSettings : ScriptableObject
{
    public const string Path = "Assets/Characters/Player/Player Movement.asset";

    [Serializable]
    public class Gait
    {
        [Header("Live")]
        [Tooltip("Plays this gait's clips faster or slower. Root motion follows, so she covers ground faster or slower with planted feet. 1 is as recorded.")]
        [Range(0.6f, 1.5f)] public float playbackSpeed = 1f;
        [Tooltip("How long the blend takes to move into this gait, in seconds: higher eases into it, lower snaps.")]
        [Range(0f, 1f)] public float blendTime = 0.15f;
        [Tooltip("The fastest she turns toward the stick on the move, in degrees per second.")]
        [Range(30f, 720f)] public float maxTurnRate = 260f;
        [Tooltip("How hard she turns toward the stick: degrees per second for each degree off.")]
        [Range(0.5f, 15f)] public float turnResponse = 5f;
        [Tooltip("How quickly a turn builds up and eases off, in degrees per second squared. Lower is smoother.")]
        [Range(100f, 3000f)] public float turnAcceleration = 700f;
        [Tooltip("How far she leans into a curve, as a share of the pack's circle loop at the same turn rate. 0 never leans.")]
        [Range(0f, 1.5f)] public float lean = 1f;
        [Tooltip("In a sharp turn she slows to this share of the gait, so she curves round instead of running a wide arc.")]
        [Range(0f, 1f)] public float sharpTurnSpeedShare = 0.35f;
        [Tooltip("Holstered, walking or jogging: turning farther off than this plays the pack's 90 degree turn on the move. 180 never does.")]
        [Range(30f, 180f)] public float turnClipAngle = 80f;
        [Tooltip("Sets off from a stand with the pack's start clip; off, she blends straight into the gait.")]
        public bool startClip = true;
        [Tooltip("Stops with the pack's last steps; off, she slows to a stand by the blend.")]
        public bool stopClip = true;

        [Header("Animator (rebuild)")]
        [Tooltip("Starts the start clip this far in (0 to 0.9 of it), skipping its slow first weight shift.")]
        [Range(0f, 0.9f)] public float startSkip = 0f;
        [Tooltip("Blend into the start clip, in seconds.")]
        [Range(0f, 0.5f)] public float startBlend = 0.15f;
        [Tooltip("Waits for the foot the stop clip begins on (it is recorded from a step with the right foot up), so the last steps follow on from the gait instead of cutting in mid-stride.")]
        public bool stopOnFoot = true;
        [Tooltip("Blend into the stop clip, in seconds.")]
        [Range(0f, 0.5f)] public float stopBlend = 0.2f;
    }

    [Header("Stick")]
    [Tooltip("Stick deflection below this counts as letting go. It is the only deadzone: the pad is read before the Input System's own.")]
    [Range(0f, 0.5f)] public float deadzone = 0.12f;
    [Tooltip("Deflection past this counts as pushed all the way.")]
    [Range(0.5f, 1f)] public float outerDeadzone = 0.95f;
    [Tooltip("How far the stick is pushed (0 just past the deadzone, 1 all the way) against the pace it asks for: 0 the slowest creep, 1 the walk, 2 the jog. Between 1 and 2 the walk and the jog blend. Keep it rising.")]
    public AnimationCurve stickPace = Linear((0f, 0f), (0.5f, 1f), (0.8f, 1f), (1f, 2f));
    [Tooltip("The creep at the bottom of the stick's range: the walk played this much slower, so its root motion slows with it and the feet stay planted.")]
    [Range(0.3f, 1f)] public float creepPlayback = 0.6f;

    [Header("Gaits")]
    public Gait walk = new() { blendTime = 0.15f, maxTurnRate = 260f, turnResponse = 5f, turnAcceleration = 700f };
    public Gait jog = new() { blendTime = 0.2f, maxTurnRate = 240f, turnResponse = 5f, turnAcceleration = 700f };
    public Gait sprint = new() { blendTime = 0.25f, maxTurnRate = 200f, turnResponse = 4f, turnAcceleration = 600f, turnClipAngle = 180f };

    [Header("Standing")]
    [Tooltip("With the pistol down and standing, facing farther off than this she turns on the spot before setting off.")]
    [Range(30f, 180f)] public float turnOnSpotAngle = 120f;
    [Tooltip("Holstered and standing, facing farther off than this she turns with the pack's quarter turn...")]
    [Range(30f, 180f)] public float quarterTurnAngle = 60f;
    [Tooltip("...and farther off than this she steps round on the spot instead, since two quarter turns take seconds.")]
    [Range(60f, 180f)] public float quarterTurnMax = 120f;
    [Header("Standing (Animator, rebuild)")]
    [Tooltip("Blend from standing into moving when there is no start clip, in seconds.")]
    [Range(0f, 0.6f)] public float idleToMoveBlend = 0.2f;
    [Tooltip("Blend from moving to standing once she has slowed, in seconds.")]
    [Range(0f, 0.8f)] public float moveToIdleBlend = 0.3f;

    [Header("Debug")]
    [Tooltip("Shows the gait, pace, turn and Animator state in the corner of the game view.")]
    public bool showDebug = false;

    // The settings for gait 1 (walk), 2 (jog) or 3 (sprint).
    public Gait For(int gait) => gait >= 3 ? sprint : gait == 2 ? jog : walk;

    // How far past the deadzone a stick of this magnitude is pushed, 0 to 1.
    public float Deflection(float magnitude) =>
        magnitude < deadzone ? 0f : Mathf.Clamp01((magnitude - deadzone) / Mathf.Max(0.01f, outerDeadzone - deadzone));

    // A curve of straight segments through the points.
    static AnimationCurve Linear(params (float time, float value)[] points)
    {
        var keys = new Keyframe[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float before = i > 0 ? Slope(points[i - 1], points[i]) : 0f;
            float after = i < points.Length - 1 ? Slope(points[i], points[i + 1]) : 0f;
            keys[i] = new Keyframe(points[i].time, points[i].value, i > 0 ? before : after, i < points.Length - 1 ? after : before);
        }
        return new AnimationCurve(keys);
    }

    static float Slope((float time, float value) a, (float time, float value) b) => (b.value - a.value) / (b.time - a.time);
}
