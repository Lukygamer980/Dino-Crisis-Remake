using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;

namespace BSS.PoseBlender.SimpleController
{
    public class SimpleCameraController : MonoBehaviour
    {
        [SerializeField] PoseBlenderLite poseBlenderLite;
        [SerializeField] Transform LookAtTarget;
        [SerializeField] Transform cameraHolder;
        [SerializeField] StabilizerLite stabilizer;
        [SerializeField] SkinnedMeshRenderer headMesh;

        [Header("CameraHolder Offsets")]
        public Vector3 fpsPosition;
        public Vector3 tpsPosition;
        public float changeSpeedInSeconds = 1f;
        public CameraModus camModus;

        [Header("Input")]
        [SerializeField] KeyCode cameraChangeKey = KeyCode.V;

        public enum CameraModus { FPS, TPS }

        private Vector3 targetPos;
        Coroutine cameraCoroutine;

        private void Start()
        {
            if (poseBlenderLite == null)
                poseBlenderLite = GetComponent<PoseBlenderLite>();

            ToggleCameraView(camModus);
        }

        private void Update()
        {
            if (Input.GetKeyDown(cameraChangeKey))
            {
                ToggleCameraView();
            }
        }

        private void ToggleCameraView(CameraModus requestedMode)
        {
            if (requestedMode == CameraModus.TPS)
            {
                camModus = CameraModus.TPS;

                targetPos = tpsPosition;

                if (cameraCoroutine != null)
                    StopCoroutine(cameraCoroutine);

                cameraCoroutine = StartCoroutine(MoveCameraPosition(targetPos));
            }
            else if (camModus == CameraModus.FPS)
            {
                camModus = CameraModus.FPS;

                // local head position is set in Stabilizer Lite
                targetPos = fpsPosition;

                if (cameraCoroutine != null)
                    StopCoroutine(cameraCoroutine);

                cameraCoroutine = StartCoroutine(MoveCameraPosition(targetPos));
            }
        }

        private void ToggleCameraView()
        {
            if (camModus == CameraModus.FPS)
            {
                camModus = CameraModus.TPS;

                targetPos = tpsPosition;

                if (cameraCoroutine != null)
                    StopCoroutine(cameraCoroutine);

                cameraCoroutine = StartCoroutine(MoveCameraPosition(targetPos));
            }
            else if (camModus == CameraModus.TPS)
            {
                camModus = CameraModus.FPS;

                // local head position is set in Stabilizer Lite
                targetPos = fpsPosition;

                if (cameraCoroutine != null)
                    StopCoroutine(cameraCoroutine);

                cameraCoroutine = StartCoroutine(MoveCameraPosition(targetPos));
            }
        }

        IEnumerator MoveCameraPosition(Vector3 targetPosition)
        {
            // figure out where look weight should end up
            float endLookWeight = camModus == CameraModus.TPS && headMesh != null
                                  ? 1f
                                  : 0f;

            // **only** turn the mesh ON when going into TPS
            if (camModus == CameraModus.TPS && headMesh != null)
                headMesh.shadowCastingMode = ShadowCastingMode.On;

            Vector3 startPos = cameraHolder.transform.localPosition;
            float elapsed = 0f;
            float duration = changeSpeedInSeconds;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // lerp camera position
                stabilizer.cameraHolderOffset = Vector3.Lerp(startPos, targetPosition, t);

                yield return null;
            }

            // snap into final state
            stabilizer.cameraHolderOffset = targetPosition;

            if (camModus == CameraModus.FPS && headMesh != null)
                headMesh.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }
}