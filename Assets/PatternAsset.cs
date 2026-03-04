using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Pattern", menuName = "Attractor Pattern", order = 1)]
public class PatternAsset : ScriptableObject
{
    public enum PatternType { Waypoints, Circle }

    [Header("Pattern Info")]
    [Tooltip("Identifier for this pattern (used in logs)")]
    public string patternName = "Unnamed Pattern";

    [Header("Pattern Type")]
    [Tooltip("Type of pattern: Waypoints or Circle")]
    public PatternType patternType = PatternType.Waypoints;

    [Header("Waypoints")]
    [Tooltip("List of waypoints defining the path (world XZ coordinates). Used when patternType is Waypoints.")]
    public List<Vector3> waypoints = new List<Vector3>();

    [Header("Circle")]
    [Tooltip("Center of the circle (XZ coordinates). Used when patternType is Circle.")]
    public Vector2 circleCenter = Vector2.zero;

    [Tooltip("Radius of the circle. Used when patternType is Circle.")]
    public float circleRadius = 6f;

    [Tooltip("Angular speed for circle motion (radians per second). Used when patternType is Circle.")]
    public float circleAngularSpeed = 1.5f;
}

