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
        [Tooltip("Holstered, walking or jogging: turning farther off than this plays the pack's 90 degree turn on the move. 180 never does, the default: those clips take 2.5 s, so steering turns her instead.")]
        [Range(30f, 180f)] public float turnClipAngle = 180f;
        [Tooltip("Sets off from a stand with the pack's start clip; off, she blends straight into the gait.")]
        public bool startClip = true;
        [Tooltip("Stops with the pack's last steps; off, she slows to a stand by the blend.")]
        public bool stopClip = true;

        [Header("Animator (rebuild)")]
        [Tooltip("Starts the start clip this far in (0 to 0.9 of it), skipping its slow first weight shift. A share of the clip's length, so it is tuned to the Mobility pack's start clips.")]
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
    [Tooltip("How far the stick is pushed (0 just past the deadzone, 1 all the way) against the pace it asks for: 0 the slowest creep, 1 the walk. The stick alone never goes past a walk (the run button jogs and sprints). Keep it rising.")]
    public AnimationCurve stickPace = Linear((0f, 0f), (0.6f, 1f), (1f, 1f));
    [Tooltip("The creep at the bottom of the stick's range: the walk played this much slower, so its root motion slows with it and the feet stay planted.")]
    [Range(0.3f, 1f)] public float creepPlayback = 0.6f;

    [Header("Setting off")]
    [Tooltip("How fast the stick is pushed when she sets off, in deflection per second: this slow or slower is a gentle start...")]
    [Range(0.5f, 10f)] public float gentlePushSpeed = 2f;
    [Tooltip("...and this fast or faster a sharp one. In between, the start is as sharp as the push.")]
    [Range(1f, 40f)] public float sharpPushSpeed = 12f;
    [Tooltip("The start clip plays this much slower than recorded at the gentlest start...")]
    [Range(0.5f, 1f)] public float gentleStartPlayback = 0.85f;
    [Tooltip("...and this much faster at the sharpest. The root motion follows, so the feet stay planted.")]
    [Range(1f, 1.5f)] public float sharpStartPlayback = 1.15f;
    [Header("Setting off (Animator, rebuild)")]
    [Tooltip("A start gentler than this (0 gentlest, 1 sharpest) keeps more of the start clip's first weight shift and blends in slower.")]
    [Range(0f, 1f)] public float softSetOffBelow = 0.4f;
    [Tooltip("A gentle start skips this share of the gait's start skip (0 plays the whole weight shift).")]
    [Range(0f, 1f)] public float softStartSkipShare = 0.3f;
    [Tooltip("A gentle start blends into the start clip over this many seconds.")]
    [Range(0f, 0.6f)] public float softStartBlend = 0.3f;

    [Header("Run button")]
    [Tooltip("A press of the run button held longer than this is a hold: she jogs until it is let go, then walks. Shorter, it is a tap.")]
    [Range(0.1f, 0.5f)] public float holdThreshold = 0.22f;
    [Tooltip("Every press jogs at once. This many taps in a row, each pressed within the tap window of the last, sprints.")]
    [Range(2, 4)] public int sprintTaps = 2;
    [Tooltip("Pressing again within this many seconds of the last tap keeps the jog (after one tap) or the sprint going; once the taps stop she walks again this long after the last one. Holding the button jogs.")]
    [Range(0.2f, 1.2f)] public float sprintTapWindow = 0.5f;
    [Tooltip("Slowing down from one gait to a slower one (sprint to jog, jog to walk) blends over about this many seconds. Speeding up takes the faster gait's blend time.")]
    [Range(0.05f, 1f)] public float slowDownTime = 0.35f;

    [Header("Gaits")]
    // The start skips set off about two frames before each Mobility start clip first moves her.
    public Gait walk = new() { blendTime = 0.15f, maxTurnRate = 260f, turnResponse = 5f, turnAcceleration = 700f, startSkip = 0.23f };
    public Gait jog = new() { blendTime = 0.2f, maxTurnRate = 240f, turnResponse = 5f, turnAcceleration = 700f, startSkip = 0.43f };
    public Gait sprint = new() { blendTime = 0.25f, maxTurnRate = 200f, turnResponse = 4f, turnAcceleration = 600f, turnClipAngle = 180f, startSkip = 0.32f };

    [Header("Aiming")]
    [Tooltip("Aiming, she faces where the camera looks and the left stick moves her that way round: pushed against the camera she backs off without turning. The stick's push sets the pace up to a walk (a creep below it); holding the run button jogs.")]
    [Range(0.5f, 20f)] public float aimTurnResponse = 7.5f;
    [Tooltip("Aiming on the move, the fastest she turns toward the camera, in degrees per second.")]
    [Range(30f, 720f)] public float aimMaxTurnRate = 220f;
    [Tooltip("Aiming, how quickly a turn builds up and eases off, in degrees per second squared.")]
    [Range(100f, 5000f)] public float aimTurnAcceleration = 1200f;
    [Tooltip("Aiming and standing, the camera this far off her facing turns her on the spot, until she is within a third of it. The aim offset points the pistol across what is left.")]
    [Range(5f, 90f)] public float aimTurnAngle = 24f;
    [Tooltip("Aiming and standing, the camera this far off her facing turns her a quarter turn at once.")]
    [Range(30f, 180f)] public float aimQuarterTurnAngle = 80f;

    [Header("Setting off with a turn")]
    [Tooltip("Standing up, holstered or with the pistol down, the stick farther off her facing than this sets her off at once while she turns toward it, instead of turning on the spot first.")]
    [Range(10f, 180f)] public float turnStartAngle = 45f;
    [Tooltip("How fast she turns as she sets off, in degrees per second.")]
    [Range(180f, 1440f)] public float turnStartRate = 900f;
    [Tooltip("How quickly that turn builds up and eases off onto the stick, in degrees per second squared.")]
    [Range(1000f, 20000f)] public float turnStartAcceleration = 8000f;

    [Header("Turning about on the move")]
    [Tooltip("Jogging or sprinting, swinging the stick farther round than this turns her about quickly. The packs have no turn-about clip, so the gait plays on while she turns.")]
    [Range(90f, 180f)] public float pivotAngle = 135f;
    [Tooltip("Only this fast and faster (1 walk, 2 jog, 3 sprint); slower, steering turns her about.")]
    [Range(1f, 3f)] public float pivotMinPace = 1.5f;
    [Tooltip("How fast she turns about, in degrees per second.")]
    [Range(180f, 1440f)] public float pivotTurnRate = 720f;
    [Tooltip("How quickly the turn about builds up and eases off, in degrees per second squared.")]
    [Range(1000f, 20000f)] public float pivotAcceleration = 6000f;
    [Tooltip("While she turns about she slows to this share of her pace, blending down and back up.")]
    [Range(0.2f, 1f)] public float pivotPaceShare = 0.5f;
    [Tooltip("Within this many degrees of the stick, the turn about, eased off by then, hands over to steering.")]
    [Range(1f, 45f)] public float pivotEndAngle = 5f;

    [Header("Standing")]
    [Tooltip("With the pistol down and standing, facing farther off than this she turns on the spot before setting off. Only when the turn start angle is higher: below it she turns as she sets off.")]
    [Range(30f, 180f)] public float turnOnSpotAngle = 120f;
    [Tooltip("The same crouched with the pistol down.")]
    [Range(30f, 180f)] public float crouchTurnOnSpotAngle = 50f;
    [Tooltip("Holstered and crouched (standing too, if the turn start angle is higher), facing farther off than this she turns with the pack's quarter turn...")]
    [Range(30f, 180f)] public float quarterTurnAngle = 60f;
    [Tooltip("...and farther off than this she steps round on the spot instead, since two quarter turns take seconds.")]
    [Range(60f, 180f)] public float quarterTurnMax = 120f;
    [Tooltip("A quarter turn ends as soon as she faces within this of the stick (aiming, of the camera), and she walks on or steers the rest. Letting go, or swinging the stick to the other side, ends it at once too.")]
    [Range(2f, 45f)] public float turnFinishAngle = 15f;
    [Header("Standing (Animator, rebuild)")]
    [Tooltip("Blend from standing into moving when there is no start clip, in seconds.")]
    [Range(0f, 0.6f)] public float idleToMoveBlend = 0.2f;
    [Tooltip("Blend from moving to standing once she has slowed, in seconds.")]
    [Range(0f, 0.8f)] public float moveToIdleBlend = 0.3f;
    [Tooltip("The quarter turns from a stand play this much faster than recorded (the Mobility pack's take about 3 s).")]
    [Range(1f, 2.5f)] public float quarterTurnSpeed = 1.5f;
    [Tooltip("The turn-on-the-spot loops play this much faster than recorded (they step 45 degrees in 0.67 s); the feet still step the whole turn.")]
    [Range(1f, 3f)] public float turnInPlaceSpeed = 2f;

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
