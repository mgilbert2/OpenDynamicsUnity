using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
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

    [Tooltip("Maximum depth any single well can reach. 0 = no limit. Prevents wells from becoming too strong. CRITICAL: Set to 3.0 or lower to prevent stuck ball scenarios.")]
    public float maxWellDepth = 3.0f;

    [Tooltip("If enabled, automatically normalizes all well depths so the maximum depth equals normalizedDepthTarget. Maintains relative strengths while preventing unbounded growth.")]
    public bool normalizeDepth = false;

    [Tooltip("Target maximum depth after normalization (used when normalizeDepth is enabled).")]
    public float normalizedDepthTarget = 1.0f;

    [Tooltip("Minimum depth any well can have (prevents wells from becoming too weak with many patterns). 0 = no minimum.")]
    public float minWellDepth = 0f;

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
    
    // Stuck-ball detection
    Vector3 lastImprintPosition;
    float timeAtSamePosition = 0f;
    const float stuckThreshold = 0.1f; // Distance threshold for "stuck" (Unity units)
    const float stuckTimeThreshold = 0.5f; // Time threshold before considering stuck (seconds)
    bool isStuck = false;

    // Diagnostics tracking
    [System.Serializable]
    public class WellStatistics
    {
        public int wellCount;
        public float averageDepth;
        public float medianDepth;
        public float minDepth;
        public float maxDepth;
        public float depthStdDev;
        public float totalDepthSum;
        public int wellsAtMaxDepth;
        public int wellsAtMinDepth;
        public float averageWidth;
        public bool normalizationEnabled;
        public float normalizedDepthTarget;
        public float maxWellDepthCap;
        public float minWellDepthCap;
        public string timestamp;
        public string patternContext; // Pattern ID or context when this snapshot was taken
    }

    /// <summary>
    /// Get the current number of learned attractors (wells).
    /// </summary>
    public int GetWellCount() => wells.Count;

    /// <summary>
    /// Calculate and return comprehensive well depth statistics.
    /// </summary>
    public WellStatistics GetWellStatistics(string patternContext = "")
    {
        WellStatistics stats = new WellStatistics
        {
            wellCount = wells.Count,
            normalizationEnabled = normalizeDepth,
            normalizedDepthTarget = normalizedDepthTarget,
            maxWellDepthCap = maxWellDepth,
            minWellDepthCap = minWellDepth,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            patternContext = patternContext
        };

        if (wells.Count == 0)
        {
            return stats;
        }

        // Calculate depth statistics
        List<float> depths = wells.Select(w => w.depth).ToList();
        depths.Sort();

        stats.minDepth = depths[0];
        stats.maxDepth = depths[depths.Count - 1];
        stats.totalDepthSum = depths.Sum();
        stats.averageDepth = stats.totalDepthSum / depths.Count;
        
        // Median
        if (depths.Count % 2 == 0)
        {
            stats.medianDepth = (depths[depths.Count / 2 - 1] + depths[depths.Count / 2]) / 2f;
        }
        else
        {
            stats.medianDepth = depths[depths.Count / 2];
        }

        // Standard deviation
        float variance = depths.Sum(d => (d - stats.averageDepth) * (d - stats.averageDepth)) / depths.Count;
        stats.depthStdDev = Mathf.Sqrt(variance);

        // Count wells at limits
        stats.wellsAtMaxDepth = maxWellDepth > 0f ? depths.Count(d => d >= maxWellDepth * 0.99f) : 0;
        stats.wellsAtMinDepth = minWellDepth > 0f ? depths.Count(d => d <= minWellDepth * 1.01f) : 0;

        // Average width
        stats.averageWidth = wells.Select(w => w.width).Average();

        return stats;
    }

    /// <summary>
    /// Log well statistics to console with formatting.
    /// </summary>
    public void LogWellStatistics(string context = "")
    {
        WellStatistics stats = GetWellStatistics(context);
        
        string prefix = string.IsNullOrEmpty(context) ? "[LearningImprint]" : $"[LearningImprint] {context}";
        
        Debug.Log($"{prefix} ═══ Well Depth Diagnostics ═══");
        Debug.Log($"{prefix} Well Count: {stats.wellCount}");
        Debug.Log($"{prefix} Normalization: {(stats.normalizationEnabled ? "ENABLED" : "DISABLED")}");
        if (stats.normalizationEnabled)
        {
            Debug.Log($"{prefix}   Target Depth: {stats.normalizedDepthTarget:F3}");
        }
        if (stats.maxWellDepthCap > 0f)
        {
            Debug.Log($"{prefix} Max Depth Cap: {stats.maxWellDepthCap:F3} (Wells at cap: {stats.wellsAtMaxDepth})");
        }
        if (stats.minWellDepthCap > 0f)
        {
            Debug.Log($"{prefix} Min Depth Cap: {stats.minWellDepthCap:F3} (Wells at min: {stats.wellsAtMinDepth})");
        }
        Debug.Log($"{prefix} ─────────────────────────────");
        Debug.Log($"{prefix} Average Depth: {stats.averageDepth:F4}");
        Debug.Log($"{prefix} Median Depth:  {stats.medianDepth:F4}");
        Debug.Log($"{prefix} Min Depth:     {stats.minDepth:F4}");
        Debug.Log($"{prefix} Max Depth:     {stats.maxDepth:F4}");
        Debug.Log($"{prefix} Std Dev:       {stats.depthStdDev:F4}");
        Debug.Log($"{prefix} Average Width: {stats.averageWidth:F4}");
        Debug.Log($"{prefix} ═══════════════════════════════");
    }

    /// <summary>
    /// Export well statistics to CSV file.
    /// </summary>
    public bool ExportWellStatisticsToCSV(string filePath, string patternContext = "")
    {
        try
        {
            WellStatistics stats = GetWellStatistics(patternContext);
            
            // Check if file exists to determine if we need headers
            bool fileExists = File.Exists(filePath);
            
            using (StreamWriter writer = new StreamWriter(filePath, append: true))
            {
                // Write header if new file
                if (!fileExists)
                {
                    writer.WriteLine("timestamp,patternContext,wellCount,normalizationEnabled,normalizedDepthTarget,maxWellDepthCap,minWellDepthCap," +
                                   "averageDepth,medianDepth,minDepth,maxDepth,depthStdDev,totalDepthSum," +
                                   "wellsAtMaxDepth,wellsAtMinDepth,averageWidth");
                }
                
                // Write data row (escape pattern context if it contains commas)
                string safeContext = string.IsNullOrEmpty(stats.patternContext) ? "" : stats.patternContext.Replace(",", ";");
                writer.WriteLine($"{stats.timestamp},{safeContext},{stats.wellCount}," +
                               $"{stats.normalizationEnabled},{stats.normalizedDepthTarget:F6},{stats.maxWellDepthCap:F6},{stats.minWellDepthCap:F6}," +
                               $"{stats.averageDepth:F6},{stats.medianDepth:F6},{stats.minDepth:F6},{stats.maxDepth:F6},{stats.depthStdDev:F6},{stats.totalDepthSum:F6}," +
                               $"{stats.wellsAtMaxDepth},{stats.wellsAtMinDepth},{stats.averageWidth:F6}");
            }
            
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[LearningImprint] Failed to export well statistics: {e.Message}");
            return false;
        }
    }

    void Update()
    {
        // Imprinting active?
        // Check both learningOn flag and time-based windows
        // Note: When learningOn is explicitly set to false via SetLearningEnabled(),
        // the experiment system should also call ClearLearningWindows() to disable time-based learning
        bool active = learningOn || InsideAnyWindow(Time.time);
        if (!active || statePoint == null) return;

        // Update stuck-ball detection (always, even if not learning)
        UpdateStuckDetection();

        timer += Time.deltaTime;
        if (timer >= sampleInterval)
        {
            timer = 0f;
            ImprintAt(statePoint.position);
        }
    }
    
    void UpdateStuckDetection()
    {
        if (statePoint == null) return;
        
        float distFromLast = Vector3.Distance(statePoint.position, lastImprintPosition);
        
        if (distFromLast < stuckThreshold)
        {
            timeAtSamePosition += Time.deltaTime;
            if (timeAtSamePosition >= stuckTimeThreshold && !isStuck)
            {
                isStuck = true;
                Debug.LogWarning($"[LearningImprint] ⚠️ BALL STUCK DETECTED at {statePoint.position}. Stopping learning at this location to prevent landscape flattening.");
            }
        }
        else
        {
            // Ball moved - reset stuck detection
            timeAtSamePosition = 0f;
            if (isStuck)
            {
                isStuck = false;
                Debug.Log($"[LearningImprint] ✓ Ball unstuck, learning resumed.");
            }
            lastImprintPosition = statePoint.position;
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
        // NOTE: Learning is ALWAYS enabled during learning phase
        // Depth capping (maxWellDepth) prevents wells from getting too deep
        // We rely on the cap checks below to prevent stuck ball scenarios
        
        Vector2 p = new(world.x, world.z);
        
        float incr = learningRate * Mathf.Max(0f, hypoDepth);
        if (incr <= 0f) return;

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
                // Merge: add to existing well's depth
                Well existingWell = wells[closestIdx];
                
                // CRITICAL: Check if adding depth would exceed maxWellDepth BEFORE adding
                // This prevents any well from ever exceeding the cap
                float newDepth = existingWell.depth + incr;
                
                // Apply maxWellDepth cap FIRST (always enforce, regardless of normalization setting)
                if (maxWellDepth > 0f)
                {
                    // If adding depth would exceed max, STOP learning at this well completely
                    if (newDepth > maxWellDepth)
                    {
                        // Well would exceed cap - STOP learning here completely
                        Debug.LogWarning($"[LearningImprint] Well at {existingWell.pos} would exceed max depth ({existingWell.depth:F3} + {incr:F3} = {newDepth:F3} > {maxWellDepth:F3}). Skipping learning to prevent stuck ball.");
                        return; // Skip learning completely
                    }
                    
                    // Cap to max (safety check, though we should never reach here if above check works)
                    newDepth = Mathf.Min(newDepth, maxWellDepth);
                    
                    // If we're at or near the cap (within 5%), warn
                    if (newDepth >= maxWellDepth * 0.95f)
                    {
                        Debug.LogWarning($"[LearningImprint] Well reached near-max depth ({newDepth:F3} / {maxWellDepth:F3}). Future learning at this location may be blocked.");
                    }
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
                
                // Normalize depths if enabled (but only if max exceeds threshold to avoid over-normalization)
                if (normalizeDepth)
                {
                    NormalizeDepths();
                }
                return;
            }
        }
        
        // No nearby well found, create a new one
        // Always apply maxWellDepth cap (prevents new wells from starting too deep)
        float cappedDepth = incr;
        if (maxWellDepth > 0f)
        {
            cappedDepth = Mathf.Min(incr, maxWellDepth);
            
            // Safety check: Don't create new wells if they would start near the cap
            // This prevents creating many capped wells when stuck
            if (cappedDepth >= maxWellDepth * 0.9f)
            {
                Debug.LogWarning($"[LearningImprint] Skipping new well creation - would start at {cappedDepth:F3} which is too close to max {maxWellDepth:F3}");
                return; // Don't create the well
            }
        }
        
        wells.Add(new Well { pos = p, depth = cappedDepth, width = newWidth });
        
        // Normalize depths if enabled (but only if max exceeds threshold to avoid over-normalization)
        if (normalizeDepth)
        {
            NormalizeDepths();
        }
    }

    /// <summary>
    /// Normalizes all well depths so the maximum depth equals normalizedDepthTarget.
    /// Maintains relative strengths between wells while preventing unbounded growth.
    /// ALWAYS enforces maxWellDepth as a hard limit to prevent stuck ball scenarios.
    /// </summary>
    void NormalizeDepths()
    {
        if (wells.Count == 0 || normalizedDepthTarget <= 0f) return;
        
        // Find the maximum depth BEFORE any capping
        float maxDepth = 0f;
        for (int i = 0; i < wells.Count; i++)
        {
            if (wells[i].depth > maxDepth)
            {
                maxDepth = wells[i].depth;
            }
        }
        
        // Determine effective target (use the smaller of normalizedDepthTarget and maxWellDepth if cap is set)
        // CRITICAL: maxWellDepth is a HARD CAP that must always be respected
        // If normalization target exceeds the cap, we must use the cap instead
        float effectiveTarget = normalizedDepthTarget;
        if (maxWellDepth > 0f)
        {
            // Always use the minimum of the two to ensure we never exceed maxWellDepth
            effectiveTarget = Mathf.Min(normalizedDepthTarget, maxWellDepth);
        }
        
        // Always enforce cap first (prevents stuck ball scenarios)
        if (maxWellDepth > 0f)
        {
            int cappedCount = 0;
            for (int i = 0; i < wells.Count; i++)
            {
                var well = wells[i];
                if (well.depth > maxWellDepth)
                {
                    cappedCount++;
                    Debug.LogError($"[LearningImprint] CRITICAL: Well {i} at {well.pos} exceeded maxWellDepth! Depth: {well.depth:F3} > {maxWellDepth:F3}. Capping immediately.");
                    well.depth = maxWellDepth;
                    wells[i] = well;
                }
            }
            if (cappedCount > 0)
            {
                Debug.LogWarning($"[LearningImprint] Capped {cappedCount} wells that exceeded maxWellDepth {maxWellDepth:F3}");
            }
            
            // Recalculate max after capping
            maxDepth = 0f;
            for (int i = 0; i < wells.Count; i++)
            {
                if (wells[i].depth > maxDepth)
                {
                    maxDepth = wells[i].depth;
                }
            }
        }
        
        // Only normalize if max depth significantly exceeds target (prevents constant micro-adjustments)
        // Use a 50% threshold - only normalize when max is 50% above target (less aggressive)
        // This prevents flattening when there are many wells
        float normalizationThreshold = effectiveTarget * 1.5f;
        if (maxDepth <= normalizationThreshold)
        {
            // Max is close to target, just enforce cap if set
            if (maxWellDepth > 0f)
            {
                for (int i = 0; i < wells.Count; i++)
                {
                    var well = wells[i];
                    if (well.depth > maxWellDepth)
                    {
                        well.depth = maxWellDepth;
                        wells[i] = well;
                    }
                }
            }
            return; // Already at or near target
        }
        
        // Additional safety: If we have many wells, be even more conservative
        // Normalize only if max is 2x the target when there are many wells
        if (wells.Count > 100 && maxDepth < effectiveTarget * 2.0f)
        {
            Debug.Log($"[LearningImprint] Many wells ({wells.Count}) detected. Skipping normalization to prevent flattening. Max depth: {maxDepth:F3}, Target: {effectiveTarget:F3}");
            return;
        }
        
        // If max depth is already at or below effective target after capping, we're done
        if (maxDepth <= effectiveTarget) return;
        
        // Calculate normalization factor to scale to effective target
        float scaleFactor = effectiveTarget / maxDepth;
        
        // CRITICAL: If normalization would scale wells above maxWellDepth, don't normalize
        // Instead, just cap all wells to maxWellDepth
        if (maxWellDepth > 0f && (maxDepth * scaleFactor) > maxWellDepth)
        {
            Debug.LogWarning($"[LearningImprint] Normalization would exceed maxWellDepth ({maxDepth * scaleFactor:F3} > {maxWellDepth:F3}). Capping all wells to maxWellDepth instead.");
            for (int i = 0; i < wells.Count; i++)
            {
                var well = wells[i];
                if (well.depth > maxWellDepth)
                {
                    well.depth = maxWellDepth;
                }
                wells[i] = well;
            }
            return; // Don't normalize, just cap
        }
        
        // Scale all depths proportionally
        for (int i = 0; i < wells.Count; i++)
        {
            var well = wells[i];
            well.depth *= scaleFactor;
            
            // Enforce minimum depth (prevents wells from becoming too weak)
            if (minWellDepth > 0f)
            {
                well.depth = Mathf.Max(well.depth, minWellDepth);
            }
            
            // CRITICAL: Final safety check - ensure no well exceeds maxWellDepth EVER
            if (maxWellDepth > 0f)
            {
                if (well.depth > maxWellDepth)
                {
                    Debug.LogError($"[LearningImprint] CRITICAL: Well depth {well.depth:F3} exceeded maxWellDepth {maxWellDepth:F3} after normalization! Capping immediately.");
                    well.depth = maxWellDepth;
                }
            }
            
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
