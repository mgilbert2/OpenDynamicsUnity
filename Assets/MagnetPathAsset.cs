using UnityEngine;

[CreateAssetMenu(fileName = "New Magnet Path", menuName = "Magnet/Path Asset")]
public class MagnetPathAsset : ScriptableObject
{
    public enum PathType { Circle, Lissajous, Waypoints }

    [Header("Path Type")]
    public PathType pathType = PathType.Circle;

    [Header("Circle")]
    public Vector2 center = Vector2.zero;
    public float radius = 6f;
    public float angularSpeed = 1.5f;

    [Header("Lissajous")]
    public float A = 6f, B = 4f;
    public float aFreq = 1.5f, bFreq = 2.3f;
    public float phase = 1.2f;

    [Header("Waypoints")]
    public Vector3[] waypoints;
    public float waypointSpeed = 6f;
    public bool loop = true;
}
