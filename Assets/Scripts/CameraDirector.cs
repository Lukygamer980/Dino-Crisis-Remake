using UnityEngine;

// Fixed-angle camera: hard cuts between CameraZone shots as the player moves through a room.
[RequireComponent(typeof(Camera))]
public class CameraDirector : MonoBehaviour
{
    public Transform player;

    CameraZone[] zones;
    CameraZone currentZone;
    Camera viewCamera;

    void Awake()
    {
        viewCamera = GetComponent<Camera>();
        zones = FindObjectsByType<CameraZone>(FindObjectsSortMode.None);
    }

    void LateUpdate()
    {
        Vector3 playerCenter = player.position + Vector3.up;

        // Stay on the current shot until the player is clearly past its edge (see stickyMargin).
        if (currentZone == null || !currentZone.Contains(playerCenter, currentZone.stickyMargin))
        {
            foreach (CameraZone zone in zones)
            {
                if (zone.Contains(playerCenter))
                {
                    currentZone = zone;
                    break;
                }
            }
        }
        if (currentZone == null) return;

        viewCamera.fieldOfView = currentZone.fieldOfView;
        Quaternion rotation = currentZone.shot.rotation;
        if (currentZone.trackPlayer)
        {
            rotation = Quaternion.LookRotation(player.position + Vector3.up * 0.9f - currentZone.shot.position);
        }
        transform.SetPositionAndRotation(currentZone.shot.position, rotation);
    }
}
