using UnityEngine;

// A box-shaped area of a room. While the player stands inside it, the main camera cuts
// to this zone's shot. Uses a plain bounds check instead of a trigger collider, so a
// teleporting player switches zones immediately and raycasts never hit the zone.
public class CameraZone : MonoBehaviour
{
    public Vector3 size = new(8f, 4f, 8f);
    public Transform shot;
    public bool trackPlayer;
    public float fieldOfView = 55f;

    public bool Contains(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return Mathf.Abs(local.x) <= size.x / 2f && Mathf.Abs(local.y) <= size.y / 2f && Mathf.Abs(local.z) <= size.z / 2f;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, size);
        if (shot == null) return;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(shot.position, transform.position);
        Gizmos.DrawWireSphere(shot.position, 0.25f);
    }
}
