using UnityEngine;

namespace BSS.PoseBlender
{
    [ExecuteInEditMode]
    public class StabilizerLite : MonoBehaviour
    {
        [SerializeField] PoseBlenderLite poseBlender;
        
        [Tooltip("Used for the position of the Stabilizer Component (usually the Spine bone which is attached to the arms of the character)")]
        public Transform stabilizationTransform;
        [Tooltip("The head transform for the CameraHolder to follow.")]
        public Transform head;

        public Transform cameraHolder;

        [Tooltip("Local offset (in this GameObject's rotation space) relative to the head position.")]
        public Vector3 cameraHolderOffset = Vector3.zero;

        void LateUpdate()
        {
            if (head == null || stabilizationTransform == null || cameraHolder == null || poseBlender == null)
                return;

            if (!poseBlender.previewInEditor && !Application.isPlaying)
                return;

            // Place the stabilizer at the spine's position.
            transform.position = stabilizationTransform.position;

            // Set the stabilizer's rotation based on the root's rotation and poseEditor offsets.
            Transform root = transform.root;
            transform.rotation = root.rotation * Quaternion.Euler(poseBlender.lookVerticalOffset * poseBlender.masterWeight,
                                                                  poseBlender.lookHorizontalOffset * poseBlender.masterWeight,
                                                                  -poseBlender.leaningOffset * poseBlender.masterWeight);

            // Calculate the desired world position for the cameraHolder:
            // head.position plus the rest offset applied in the stabilizer's rotation space.
            Vector3 desiredCameraWorldPos = head.position + transform.rotation * cameraHolderOffset;

            // Update cameraHolder's local position so that its world position equals desiredCameraWorldPos.
            cameraHolder.localPosition = transform.InverseTransformPoint(desiredCameraWorldPos);
        }

        public void Initialize()
        {
            poseBlender = GetComponentInParent<PoseBlenderLite>();
        }
    }
}
