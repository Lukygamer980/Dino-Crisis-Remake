using UnityEngine;

// Sits next to the player's Animator. With root motion on, Unity hands each frame's animated step to
// OnAnimatorMove instead of moving the model, and this passes it on to PlayerController, which moves
// the CharacterController by it: her feet carry her exactly as far as the clips step. It passes on
// the IK pass the same way, for PlayerController to set the feet and the head.
[RequireComponent(typeof(Animator))]
public class RootMotionRelay : MonoBehaviour
{
    // The frame's animated step along the ground, and its turn.
    public event System.Action<Vector3, Quaternion> Moved;
    // An IK pass, by layer.
    public event System.Action<int> IK;

    Animator animator;

    void Awake() => animator = GetComponent<Animator>();

    void OnAnimatorMove() => Moved?.Invoke(animator.deltaPosition, animator.deltaRotation);

    void OnAnimatorIK(int layer) => IK?.Invoke(layer);
}
