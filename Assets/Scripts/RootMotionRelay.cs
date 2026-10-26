using UnityEngine;

// Sits next to Eve's Animator. With root motion on, Unity hands each frame's animated step to
// OnAnimatorMove instead of moving the model, so the model stays put under the player and
// PlayerController can read how fast the current clip steps. The start-walking step uses it.
[RequireComponent(typeof(Animator))]
public class RootMotionRelay : MonoBehaviour
{
    // Speed of the animated step along the ground, in metres per second.
    public float Speed { get; private set; }
    // How fast the animated step turns, in degrees per second (positive is to the right).
    public float TurnSpeed { get; private set; }

    Animator animator;

    void Awake() => animator = GetComponent<Animator>();

    void OnAnimatorMove()
    {
        Vector3 velocity = animator.velocity;
        velocity.y = 0f;
        Speed = velocity.magnitude;
        TurnSpeed = animator.angularVelocity.y * Mathf.Rad2Deg;
    }
}
