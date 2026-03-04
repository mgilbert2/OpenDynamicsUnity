using System.Collections.Generic;
using UnityEngine;

public enum PathType { Stationary, Circle, Lissajous, Waypoints }

[CreateAssetMenu(fileName = "NewPath", menuName = "Attractor Path", order = 1)]
public class PathAsset : ScriptableObject
{
    public PathType pathType = PathType.Stationary;

    [Header("Circle")]
    public Vector2 circleCenter = Vector2.zero;
    public float circleRadius = 2f;
    public float circleSpeed = 0.25f; // slow for landscape evolution
    public float circlePhase = 0f;

    [Header("Lissajous")]
    public float A = 2f, B = 2f;
    public float aFreq = 0.3f, bFreq = 0.4f;
    public float lissPhase = 0f;

    [Header("Waypoints (world XZ)")]
    public List<Vector3> waypoints = new List<Vector3>();
    public float waypointSpeed = 1.5f;
    public bool loop = true;
}