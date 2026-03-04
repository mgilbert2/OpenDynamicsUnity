using UnityEngine;

[DisallowMultipleComponent]
public class Attractor : MonoBehaviour
{
    [Header("Attractor Parameters")]
    [Tooltip("Depth (strength). More negative = stronger pull near the center.")]
    public float depth = 15f;

    [Tooltip("Width (spread) of the Gaussian well.")]
    public float width = 2f;

    /// <summary>
    /// Scalar potential V(x,z) at a world-space point.
    /// </summary>
    public float GetPotentialXZ(Vector3 worldXZ)
    {
        Vector2 a = new Vector2(transform.position.x, transform.position.z);
        Vector2 b = new Vector2(worldXZ.x, worldXZ.z);
        float r2 = (a - b).sqrMagnitude;
        return -depth * Mathf.Exp(-r2 / (2f * width * width));
    }

    /// <summary>
    /// Gradient ∇V (direction of pull in the XZ plane).
    /// </summary>
    public Vector3 GetGradientXZ(Vector3 worldXZ)
    {
        float dx = transform.position.x - worldXZ.x;
        float dz = transform.position.z - worldXZ.z;
        float r2 = dx * dx + dz * dz;
        float factor = (depth / (width * width)) * Mathf.Exp(-r2 / (2f * width * width));
        return new Vector3(factor * dx, 0f, factor * dz);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(new Vector3(transform.position.x, 0f, transform.position.z), width);
    }
#endif
}
