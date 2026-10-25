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
    // Once this shot is showing, it stays until the player is this far past the box. Neighbouring
    // shots often face each other, and without it turning around right after a cut walks
    // straight back into the previous shot.
    public float stickyMargin = 1f;

    // margin grows the box on every side, in the zone's local units like size.
    public bool Contains(Vector3 worldPosition, float margin = 0f)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector3 half = size / 2f + Vector3.one * margin;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
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
