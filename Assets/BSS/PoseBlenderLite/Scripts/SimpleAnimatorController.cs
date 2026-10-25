using UnityEngine;

namespace BSS.PoseBlender.SimpleController
{
    public class SimpleAnimatorController : MonoBehaviour
    {
        [SerializeField] CharacterController characterController;
        [SerializeField] Animator animator;

        [Header("Smoothing Settings")]
        [SerializeField] float smoothTime = 0.1f;
        [SerializeField] float speedSmoothTime = 0.15f;

        [Header("Debug Info")]
        public Vector3 worldVelocity;
        public Vector3 localVelocity;
        public Vector3 smoothedLocalVelocity;
        public float speed;
        public float smoothedSpeed;

        // Smoothing variables
        private Vector3 velocitySmoothVelocity;
        private float speedSmoothVelocity;

        private void Start()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
        }

        private void Update()
        {
            // Get world-space velocity from CharacterController
            worldVelocity = characterController.velocity;

            // Convert world velocity to local space relative to character's transform
            localVelocity = transform.InverseTransformDirection(worldVelocity);

            // Calculate overall speed
            speed = worldVelocity.magnitude;

            // Apply smoothing to local velocity
            smoothedLocalVelocity = Vector3.SmoothDamp(
                smoothedLocalVelocity,
                localVelocity,
                ref velocitySmoothVelocity,
                smoothTime
            );

            // Apply smoothing to speed
            smoothedSpeed = Mathf.SmoothDamp(
                smoothedSpeed,
                speed,
                ref speedSmoothVelocity,
                speedSmoothTime
            );

            // Pass smoothed local-space values to animator
            // X = strafe (left/right), Z = forward/backward
            animator.SetFloat("velX", smoothedLocalVelocity.x);
            animator.SetFloat("velZ", smoothedLocalVelocity.z); // Forward/back movement
            animator.SetFloat("speed", smoothedSpeed); // Overall speed for blend trees

            animator.SetFloat("velY", localVelocity.y);
        }
    }
}