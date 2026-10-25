using UnityEngine;

namespace BSS.PoseBlender.SimpleController
{
    [RequireComponent(typeof(PoseBlenderLite))]
    [RequireComponent(typeof(SimpleMovementController))]
    [RequireComponent(typeof(SimpleRotationController))]
    [RequireComponent(typeof(SimpleAnimatorController))]
    [RequireComponent(typeof(SimpleWeaponController))]
    [RequireComponent(typeof(SimpleCameraController))]
    public class SimpleCharacterController : MonoBehaviour
    {
        // Empty class holding all the components for the simple controller
    }
}