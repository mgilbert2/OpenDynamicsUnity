using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LearningImprint : MonoBehaviour
{
    [Header("Source (ball)")]
    public Transform statePoint;

    [Header("Learning Control")]
    [Tooltip("Master switch. If false, imprinting is off unless time is inside a learning window.")]
    public bool learningOn = false;

    [Tooltip("How often to add/strengthen an imprint while learning (seconds).")]
    public float sampleInterval = 0.05f;

    [Tooltip("Scales how much of the attractor strength is added each sample (0..1 typical).")]
    [Range(0f, 2f)] public float learningRate = 0.2f;

    [Header("Learned Attractor Depth")]
    [Tooltip("Depth (strength) of each learned attractor.")]
    public float hypoDepth = 10f;

    [Tooltip("Width (spread) of each learned attractor.")]
    public float hypoWidth = 1.2f;

    [Tooltip("Maximum depth any single well can reach. 0 = no limit. Prevents wells from becoming too strong.")]
    public float maxWellDepth = 0f;

    [Tooltip("If enabled, automatically normalizes all well depths so the maximum depth equals normalizedDepthTarget. Maintains relative strengths while preventing unbounded growth.")]
    public bool normalizeDepth = false;

    [Tooltip("Target maximum depth after normalization (used when normalizeDepth is enabled).")]
    public float normalizedDepthTarget = 1.0f;

    [Tooltip("Distance threshold for merging nearby wells (fraction of hypoWidth). 0 = no merging.")]
    [Range(0f, 1f)] public float wellMergeDistance = 0.3f;

    [Header("Learning Windows (seconds from Play start)")]
    public List<Vector2> learningWindows = new();

    struct Well
    {
        public Vector2 pos;
        public float depth;
        public float width;
    }

    readonly List<Well> wells = new();
    float timer;

    /// <summary>
    /// Get the current number of learned attractors (wells).
    /// </summary>
    public int GetWellCount() => wells.Count;

    void Update()
    {
        // Imprinting active?
        // Check both learningOn flag and time-based windows
        // Note: When learningOn is explicitly set to false via SetLearningEnabled(),
        // the experiment system should also call ClearLearningWindows() to disable time-based learning
        bool active = learningOn || InsideAnyWindow(Time.time);
        if (!active || statePoint == null) return;

        timer += Time.deltaTime;
        if (timer >= sampleInterval)
        {
            timer = 0f;
            ImprintAt(statePoint.position);
        }
    }

    public void ClearImprint() => wells.Clear();

    /// <summary>
    /// Enable or disable learning programmatically. Takes precedence over time-based windows.
    /// </summary>
    public void SetLearningEnabled(bool enabled)
    {
        learningOn = enabled;
    }

    /// <summary>
    /// Clear all learning windows to prevent time-based learning activation.
    /// </summary>
    public void ClearLearningWindows()
    {
        if (learningWindows != null)
        {
            learningWindows.Clear();
        }
    }

    // ---- Field ----
    public float GetPotentialXZ(Vector3 worldXZ)
    {
        if (wells.Count == 0) return 0f;
        Vector2 p = new(worldXZ.x, worldXZ.z);
        float V = 0f;
        foreach (var w in wells)
        {
            if (w.depth <= 0f || w.width <= 1e-4f) continue;
            float r2 = (w.pos - p).sqrMagnitude;
            V += -w.depth * Mathf.Exp(-r2 / (2f * w.width * w.width));
        }
        return V;
    }

    public Vector3 GetGradientXZ(Vector3 worldXZ)
    {
        if (wells.Count == 0) return Vector3.zero;
        Vector2 p = new(worldXZ.x, worldXZ.z);
        float gx = 0f, gz = 0f;
        foreach (var w in wells)
        {
            if (w.depth <= 0f || w.width <= 1e-4f) continue;
            Vector2 d = new(w.pos.x - p.x, w.pos.y - p.y);
            float factor = (w.depth / (w.width * w.width)) *
                           Mathf.Exp(-d.sqrMagnitude / (2f * w.width * w.width));
            gx += factor * d.x;
            gz += factor * d.y;
        }
        return new Vector3(gx, 0f, gz);
    }

    // ---- Imprinting ----
    void ImprintAt(Vector3 world)
    {
        float incr = learningRate * Mathf.Max(0f, hypoDepth);
        if (incr <= 0f) return;

        Vector2 p = new(world.x, world.z);
        float newWidth = Mathf.Max(1e-4f, hypoWidth);
        
        // Check if we should merge with an existing nearby well
        float mergeThreshold = wellMergeDistance > 0f ? (wellMergeDistance * hypoWidth) : 0f;
        float mergeThresholdSq = mergeThreshold * mergeThreshold;
        
        if (mergeThreshold > 0f && wells.Count > 0)
        {
            // Find the closest well within merge distance
            int closestIdx = -1;
            float closestDistSq = float.MaxValue;
            
            for (int i = 0; i < wells.Count; i++)
            {
                float distSq = (wells[i].pos - p).sqrMagnitude;
                if (distSq < mergeThresholdSq && distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestIdx = i;
                }
            }
            
            if (closestIdx >= 0)
            {
                // Merge: add to existing well's depth, capped at maxWellDepth
                Well existingWell = wells[closestIdx];
                float newDepth = existingWell.depth + incr;
                
                // Apply maximum depth cap if set
                if (maxWellDepth > 0f)
                {
                    newDepth = Mathf.Min(newDepth, maxWellDepth);
                }
                
                // Update the well (average position slightly weighted by depth)
                float totalDepth = existingWell.depth + incr;
                if (totalDepth > 0f)
                {
                    Vector2 mergedPos = Vector2.Lerp(existingWell.pos, p, incr / totalDepth);
                    wells[closestIdx] = new Well 
                    { 
                        pos = mergedPos, 
                        depth = newDepth, 
                        width = Mathf.Max(existingWell.width, newWidth) 
                    };
                }
                
                // Normalize depths if enabled
                if (normalizeDepth)
                {
                    NormalizeDepths();
                }
                return;
            }
        }
        
        // No nearby well found, create a new one (capped at maxWellDepth)
        float cappedDepth = incr;
        if (maxWellDepth > 0f)
        {
            cappedDepth = Mathf.Min(incr, maxWellDepth);
        }
        
        wells.Add(new Well { pos = p, depth = cappedDepth, width = newWidth });
        
        // Normalize depths if enabled
        if (normalizeDepth)
        {
            NormalizeDepths();
        }
    }

    /// <summary>
    /// Normalizes all well depths so the maximum depth equals normalizedDepthTarget.
    /// Maintains relative strengths between wells while preventing unbounded growth.
    /// </summary>
    void NormalizeDepths()
    {
        if (wells.Count == 0 || normalizedDepthTarget <= 0f) return;
        
        // Find the maximum depth
        float maxDepth = 0f;
        for (int i = 0; i < wells.Count; i++)
        {
            if (wells[i].depth > maxDepth)
            {
                maxDepth = wells[i].depth;
            }
        }
        
        // If max depth is already at or below target, no normalization needed
        if (maxDepth <= normalizedDepthTarget) return;
        
        // Calculate normalization factor
        float scaleFactor = normalizedDepthTarget / maxDepth;
        
        // Scale all depths proportionally
        for (int i = 0; i < wells.Count; i++)
        {
            var well = wells[i];
            well.depth *= scaleFactor;
            wells[i] = well;
        }
    }

    bool InsideAnyWindow(float t)
    {
        if (learningWindows == null || learningWindows.Count == 0) return false;
        for (int i = 0; i < learningWindows.Count; i++)
        {
            var w = learningWindows[i];
            if (w.y < w.x)
            {
                if (t >= w.x) return true; // open-ended window starting at w.x
            }
            else if (t >= w.x && t <= w.y) return true;
        }
        return false;
    }

    void OnGUI()
    {
        // Guard against rendering during invalid states
        if (Event.current == null) return;
        
        // Only draw during Repaint events to avoid render texture issues
        if (Event.current.type != EventType.Repaint)
            return;

        // Big, bold runtime overlay for learning indicator + time
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 32;                          // MUCH larger text
        style.fontStyle = FontStyle.Bold;             // bold font
        style.alignment = TextAnchor.UpperLeft;       // top-left corner
        style.normal.textColor = (learningOn || InsideAnyWindow(Time.time))
            ? Color.green
            : Color.red;

        float margin = 20f;
        string status = (learningOn || InsideAnyWindow(Time.time)) ? "LEARNING ON" : "LEARNING OFF";
        string text = $"{status}     Time: {Time.time:F1}s";

        GUI.Label(new Rect(margin, margin, 600, 80), text, style);
    }
}
