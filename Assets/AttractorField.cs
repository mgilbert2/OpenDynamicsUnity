using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class AttractorField : MonoBehaviour
{
    [Header("Attractors")]
    [Tooltip("If ON, this collects all Attractor components in children (at Play and when values change).")]
    public bool autoCollectFromChildren = true;

    [Tooltip("Attractors used by the field. If autoCollectFromChildren is ON, this is refreshed automatically.")]
    public Attractor[] attractors;

    [Header("Learning (optional)")]
    [Tooltip("Optional learning layer that adds a small Gaussian well at the ball over time.")]
    public LearningImprint imprint;

    [Header("Debug")]
    public bool drawGizmos = true;
    public Color gizmoColor = Color.cyan;
    public float gizmoRadius = 0.12f;
    public bool labelAttractors = false;

    // ---------- Lifecycle ----------

    void Awake()
    {
        if (autoCollectFromChildren) RefreshAttractors();
    }

    void OnValidate()
    {
        if (autoCollectFromChildren) RefreshAttractors();
    }

    // Manually callable from other scripts or a context menu if desired
    [ContextMenu("Refresh Attractors")]
    public void RefreshAttractors()
    {
        attractors = GetComponentsInChildren<Attractor>(includeInactive: false);
    }

    // ---------- Field API ----------

    // Scalar potential V(x,z)
    public float GetPotentialXZ(Vector3 worldXZ)
    {
        float V = 0f;

        // sum explicit attractors
        if (attractors != null)
        {
            for (int i = 0; i < attractors.Length; i++)
            {
                var a = attractors[i];
                if (!a) continue;
                V += a.GetPotentialXZ(worldXZ);
            }
        }

        // add learning imprint (small, possibly many wells)
        if (imprint != null)
            V += imprint.GetPotentialXZ(worldXZ);

        return V;
    }

    // Gradient ∇V in XZ plane (points toward more negative potential)
    public Vector3 GetGradientXZ(Vector3 worldXZ)
    {
        Vector3 g = Vector3.zero;

        // sum explicit attractor gradients
        if (attractors != null)
        {
            for (int i = 0; i < attractors.Length; i++)
            {
                var a = attractors[i];
                if (!a) continue;
                g += a.GetGradientXZ(worldXZ);
            }
        }

        // add learning imprint gradient
        if (imprint != null)
            g += imprint.GetGradientXZ(worldXZ);

        return g;
    }

    // ---------- Gizmos ----------

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawGizmos || attractors == null) return;

        Gizmos.color = gizmoColor;
        foreach (var a in attractors)
        {
            if (!a) continue;
            Vector3 p = a.transform.position;
            p.y = 0f;
            Gizmos.DrawSphere(p, gizmoRadius);

            if (labelAttractors)
                Handles.Label(p + Vector3.up * 0.05f, a.name);
        }
    }
#endif
}
