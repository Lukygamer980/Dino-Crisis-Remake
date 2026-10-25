using BSS.PoseBlender;
using UnityEngine;

namespace BSS.PoseBlender.SimpleController
{
    [RequireComponent(typeof(PoseBlenderLite))]

    public class SimpleRotationController : MonoBehaviour
    {
        [Header("Mouse Look Settings")]
        [SerializeField] private float mouseSensitivity = 2f;

        [Header("Pose Blender Settings")]
        [SerializeField] private PoseBlenderLite poseBlenderLite;
        [SerializeField] private float offsetToRootSpeed = 15f;
        [SerializeField] private float leanSpeed = 3f;

        [Header("Root Rotation Settings")]
        [SerializeField] private bool alwaysRotateRoot = false;
        [SerializeField] private float maxOffsetRotationSpeed = 100f;

        [Header("Look Offset Clamps")]
        [SerializeField] private Vector2 verticalClamp = new Vector2(-90f, 90f);
        [SerializeField] private Vector2 horizontalClamp = new Vector2(-45f, 45f);
        [SerializeField] private Vector2 leaningClamp = new Vector2(-30f, 30f);

        [Header("Turn Animations")]
        [SerializeField] private Animator animator;
        [SerializeField] private float turnAnimationBlendTime = 0.1f;

        [Header("Input")]
        [SerializeField] string lookAxisY = "Mouse Y";
        [SerializeField] string lookAxisX = "Mouse X";
        [Space]
        [SerializeField] string moveAxisY = "Horizontal";
        [SerializeField] string moveAxisX = "Vertical";
        [Space]
        [SerializeField] KeyCode leanLeftKey = KeyCode.Q;
        [SerializeField] KeyCode leanRightKey = KeyCode.E;

        // Track offsets
        private float verticalOffset = 0f;
        private float horizontalOffset = 0f;
        private float leaningOffset = 0f;

        // Movement and look input
        Vector2 moveInput;
        Vector2 lookInput;
        float leanInput;

        [HideInInspector] public bool isMoving = false;

        // Previous rotation values for calculations
        private float previousRootYRotation = 0f;

        // Flag to indicate we are in the process of rotating back to zero
        private bool isRotatingToZero = false;
        // Direction of the rotation (positive or negative)
        private float rotationDirection = 0f;

        // Flag to prevent animation from playing multiple times during a single turn
        private bool hasTurnAnimationPlayed = false;

        void Start()
        {
            if (poseBlenderLite == null)
                poseBlenderLite = GetComponent<PoseBlenderLite>();
            if (animator == null)
                animator = GetComponent<Animator>();

            // Initialize values
            if (poseBlenderLite != null)
            {
                poseBlenderLite.lookVerticalOffset = 0f;
                poseBlenderLite.lookHorizontalOffset = 0f;
                poseBlenderLite.leaningOffset = 0f;
            }

            // Store initial rotation
            previousRootYRotation = transform.eulerAngles.y;
        }

        void Update()
        {
            HandleInput();

            HandleMouseLook();

            HandleRootRotation();
        }

        private void HandleInput()
        {
            // Skip updating look input if look-at mode is enabled
            if (!(poseBlenderLite != null && poseBlenderLite.enableLookAtMode && poseBlenderLite.lookAtTarget != null))
            {
                // Capture mouse input
                lookInput.x = Input.GetAxis(lookAxisX) * mouseSensitivity;
                lookInput.y = Input.GetAxis(lookAxisY) * mouseSensitivity;
            }

            moveInput.x = Input.GetAxis(moveAxisX);
            moveInput.y = Input.GetAxis(moveAxisY);

            isMoving = moveInput.magnitude > 0.1f;

            // Capture leaning input
            leanInput = Input.GetKey(leanRightKey) ? 1 : 0;
            leanInput = Input.GetKey(leanLeftKey) ? -1 : leanInput;

            // Skip updating offsets if in look-at mode
            if (!(poseBlenderLite != null && poseBlenderLite.enableLookAtMode && poseBlenderLite.lookAtTarget != null))
            {
                // Update the vertical offset (pitch)
                verticalOffset -= lookInput.y;
                verticalOffset = Mathf.Clamp(verticalOffset, verticalClamp.x, verticalClamp.y);

                // If we're using alwaysRotateRoot, apply horizontal input directly to root rotation
                if (alwaysRotateRoot)
                {
                    // Apply horizontal look directly to root rotation
                    transform.Rotate(0, lookInput.x, 0);

                    // Keep horizontalOffset at zero when always rotating root
                    horizontalOffset = 0f;
                }
                // Otherwise use the standard offset logic
                else
                {
                    // Update the horizontal offset (yaw) - we now allow input even during rotation if it's in opposite direction
                    if (!isMoving)
                    {
                        if (!isRotatingToZero || Mathf.Sign(lookInput.x) != rotationDirection)
                        {
                            horizontalOffset += lookInput.x;
                            horizontalOffset = Mathf.Clamp(horizontalOffset, horizontalClamp.x, horizontalClamp.y);

                            // If we're rotating to zero but got input in opposite direction, cancel the auto-rotation
                            if (isRotatingToZero && Mathf.Sign(lookInput.x) != rotationDirection && Mathf.Abs(lookInput.x) > 0.1f)
                            {
                                isRotatingToZero = false;
                                // Reset animation flag when we cancel a turn
                                hasTurnAnimationPlayed = false;
                            }
                        }
                    }
                }
            }

            // Calculate leaning
            float targetLean = leanInput * leaningClamp.y;
            leaningOffset = Mathf.Lerp(leaningOffset, targetLean, Time.deltaTime * leanSpeed);
        }

        private void HandleRootRotation()
        {
            // If alwaysRotateRoot is enabled, we've already applied rotation in HandleInput
            if (alwaysRotateRoot && !(poseBlenderLite != null && poseBlenderLite.enableLookAtMode && poseBlenderLite.lookAtTarget != null))
            {
                // Just update previous rotation for next frame
                previousRootYRotation = transform.eulerAngles.y;
                return;
            }

            // For look-at mode, handle it like we're at max offset if target is outside our view range
            bool inLookAtMode = poseBlenderLite != null && poseBlenderLite.enableLookAtMode && poseBlenderLite.lookAtTarget != null;

            // Standard root rotation logic when alwaysRotateRoot is false
            // Check if we should start rotating back to zero
            if (!isRotatingToZero && !isMoving)
            {
                bool atMaxPositiveOffset = horizontalOffset >= horizontalClamp.y - 0.1f && (inLookAtMode || lookInput.x > 0);
                bool atMaxNegativeOffset = horizontalOffset <= horizontalClamp.x + 0.1f && (inLookAtMode || lookInput.x < 0);

                if (atMaxPositiveOffset || atMaxNegativeOffset)
                {
                    // Begin the rotation to zero process
                    isRotatingToZero = true;
                    rotationDirection = Mathf.Sign(horizontalOffset);
                    hasTurnAnimationPlayed = false; // Reset animation flag at the start of a new turn

                    // Play turn animation based on rotation direction (only when not moving)
                    PlayTurnAnimation(rotationDirection > 0);
                }
            }

            // If we're in the process of rotating to zero
            if (isRotatingToZero)
            {
                // Apply rotation regardless of other conditions
                float rotationAmount = rotationDirection * maxOffsetRotationSpeed * Time.deltaTime;

                // Apply rotation to the character's root
                transform.Rotate(0, rotationAmount, 0);

                // Counter-rotate the horizontal offset to keep camera steady
                horizontalOffset -= rotationAmount;

                // Check if we've completed the rotation
                if (rotationDirection > 0 && horizontalOffset <= 0 ||
                    rotationDirection < 0 && horizontalOffset >= 0)
                {
                    // We've reached zero or passed it
                    horizontalOffset = 0f;
                    isRotatingToZero = false;
                    hasTurnAnimationPlayed = false; // Reset animation flag when rotation completes
                }

                // Allow additional mouse input to influence the root rotation during this process
                if (Mathf.Abs(lookInput.x) > 0.1f)
                {
                    // If the input is in the opposite direction of our rotation
                    if (Mathf.Sign(lookInput.x) != rotationDirection)
                    {
                        // We allow this to affect the root rotation directly
                        transform.Rotate(0, lookInput.x, 0);
                    }
                    else
                    {
                        // Same direction as before, speed up the rotation
                        transform.Rotate(0, lookInput.x, 0);
                    }
                }
            }
            else if (isMoving)
            {
                // Only transfer horizontal offset to root rotation if we're moving
                // and not in the process of rotating to zero
                float rotationAmount = horizontalOffset * offsetToRootSpeed * Time.deltaTime;

                // Limit rotation amount to prevent overshooting
                rotationAmount = Mathf.Sign(rotationAmount) * Mathf.Min(Mathf.Abs(rotationAmount), Mathf.Abs(horizontalOffset));

                // Apply rotation to the root
                transform.Rotate(0, rotationAmount, 0);

                // Reduce horizontal offset by the amount we rotated
                horizontalOffset -= rotationAmount;

                // If horizontal offset is very small, reset to zero
                if (Mathf.Abs(horizontalOffset) < 0.1f)
                {
                    horizontalOffset = 0f;
                }

                // For mouse input while moving, apply directly to root rotation
                if (!inLookAtMode)
                {
                    transform.Rotate(0, lookInput.x, 0);
                }
            }

            // Update previous rotation for next frame
            previousRootYRotation = transform.eulerAngles.y;
        }

        private void HandleMouseLook()
        {
            // Apply the current offsets to PoseBlenderLite
            if (poseBlenderLite != null)
            {
                poseBlenderLite.lookVerticalOffset = verticalOffset;
                poseBlenderLite.lookHorizontalOffset = horizontalOffset;
                
                // Only apply leaning if look-at mode is not active, allowing manual control in inspector
                if (!(poseBlenderLite.enableLookAtMode && poseBlenderLite.lookAtTarget != null))
                {
                    poseBlenderLite.leaningOffset = leaningOffset;
                }
            }
        }

        private void PlayTurnAnimation(bool turnRight)
        {
            // Only play animation if we haven't already played one for this turn
            if (!hasTurnAnimationPlayed && animator != null && !isMoving)
            {
                // Play appropriate turn animation
                string animationName = turnRight ? "Turn Right" : "Turn Left";
                animator.CrossFade(animationName, turnAnimationBlendTime);

                // Set flag to prevent replaying during this turn
                hasTurnAnimationPlayed = true;
            }
        }
    }
}