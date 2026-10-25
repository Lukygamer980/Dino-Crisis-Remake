using UnityEngine;
namespace BSS.PoseBlender.SimpleController
{

    [RequireComponent(typeof(CharacterController))]
    public class SimpleMovementController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private float sprintSpeed = 5f;

        [Header("Smoothing Settings")]
        [SerializeField] private float accelerationTime = 0.2f;
        [SerializeField] private float decelerationTime = 0.15f;
        [SerializeField] private float directionSmoothTime = 0.1f;

        [Header("Character Controller Settings")]
        [SerializeField] float ccHeight = 2.0f;
        [SerializeField] float ccRadius = .25f;
        [SerializeField] float ccSkinWidth = 0.08f;
        [SerializeField] Vector3 ccCenter = new Vector3(0, 1, 0);

        [Header("Input")]
        [SerializeField] string moveAxisY = "Horizontal";
        [SerializeField] string moveAxisX = "Vertical";
        [SerializeField] KeyCode sprintKeyCode = KeyCode.LeftShift;

        [Header("Debug Info")]
        public Vector3 targetDirection;
        public Vector3 smoothedDirection;
        public float targetSpeed;
        public float currentSpeed;

        private CharacterController controller;

        // Smoothing variables
        private Vector3 directionSmoothVelocity;
        private float speedSmoothVelocity;


        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            controller.center = ccCenter;
            controller.height = ccHeight;
            controller.skinWidth = ccSkinWidth;
            controller.radius = ccRadius;
        }

        private void Update()
        {
            // Read inputs
            float h = Input.GetAxis(moveAxisY);
            float v = Input.GetAxis(moveAxisX);
            bool sprint = Input.GetKey(sprintKeyCode);

            // Calculate target direction and speed
            targetDirection = (transform.right * h + transform.forward * v).normalized;
            targetSpeed = (targetDirection.magnitude > 0) ? (sprint ? sprintSpeed : moveSpeed) : 0f;

            // Smooth direction changes
            smoothedDirection = Vector3.SmoothDamp(
                smoothedDirection,
                targetDirection,
                ref directionSmoothVelocity,
                directionSmoothTime
            );

            // Smooth speed changes with different acceleration/deceleration rates
            float smoothTime = (targetSpeed > currentSpeed) ? accelerationTime : decelerationTime;
            currentSpeed = Mathf.SmoothDamp(
                currentSpeed,
                targetSpeed,
                ref speedSmoothVelocity,
                smoothTime
            );

            // Apply movement
            Vector3 movement = smoothedDirection * currentSpeed;
            controller.SimpleMove(movement);
        }
    }
}