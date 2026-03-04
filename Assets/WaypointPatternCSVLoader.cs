using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads waypoint patterns from a CSV in StreamingAssets.
/// Supports two CSV formats:
/// 1. Multi-pattern format: pattern_id, point_index, x, z
/// 2. Single pattern format: time, x, z (or just x, z)
/// </summary>
public class WaypointPatternCSVLoader : MonoBehaviour
{
    [Tooltip("CSV file name located in StreamingAssets (recommended).")]
    public string csvFileName = "waypoint_patterns_30.csv";
    
    [Tooltip("Pattern ID to use when loading single-pattern CSV (time,x,z format). Defaults to filename without extension.")]
    public string defaultPatternId = "";

    private Dictionary<string, List<Vector3>> patterns;

    public bool IsLoaded => patterns != null && patterns.Count > 0;

    public void Load()
    {
        if (IsLoaded) return;

        patterns = new Dictionary<string, List<Vector3>>();

        string path = Path.Combine(Application.streamingAssetsPath, csvFileName);

        if (!File.Exists(path))
        {
            Debug.LogError($"[WaypointPatternCSVLoader] CSV not found: {path}");
            return;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            Debug.LogWarning($"[WaypointPatternCSVLoader] CSV file is empty: {path}");
            return;
        }

        CultureInfo ci = CultureInfo.InvariantCulture;

        // Detect format by examining the header or first data line
        string header = lines[0].ToLower().Trim();
        bool hasHeader = false;
        int startLine = 0;
        
        // Check if first line is a header
        if (header.Contains("pattern_id") || header.Contains("time") || 
            (header.Contains("x") && header.Contains("z")) || 
            (header.Contains("x") && !float.TryParse(lines[0].Split(',')[0], System.Globalization.NumberStyles.Float, ci, out _)))
        {
            hasHeader = true;
            startLine = 1;
        }

        // Determine format based on header or first data line
        string firstDataLine = startLine < lines.Length ? lines[startLine] : "";
        string[] firstParts = firstDataLine.Split(',');
        
        bool isMultiPatternFormat = hasHeader && header.Contains("pattern_id");
        bool isTimeXZFormat = hasHeader && (header.Contains("time") && header.Contains("x") && header.Contains("z"));
        bool isXZFormat = hasHeader && header.Contains("x") && header.Contains("z") && !header.Contains("time");
        
        // If no header, try to infer from column count
        if (!hasHeader)
        {
            if (firstParts.Length >= 4)
            {
                // Could be pattern_id,point_index,x,z format
                isMultiPatternFormat = true;
            }
            else if (firstParts.Length == 3)
            {
                // Could be time,x,z format
                isTimeXZFormat = true;
            }
            else if (firstParts.Length == 2)
            {
                // Could be x,z format
                isXZFormat = true;
            }
        }

        // Load based on detected format
        if (isMultiPatternFormat)
        {
            LoadMultiPatternFormat(lines, startLine, ci);
        }
        else if (isTimeXZFormat || isXZFormat)
        {
            LoadSinglePatternFormat(lines, startLine, ci, isTimeXZFormat);
        }
        else
        {
            Debug.LogWarning($"[WaypointPatternCSVLoader] Could not determine CSV format. Trying multi-pattern format as fallback.");
            LoadMultiPatternFormat(lines, startLine, ci);
        }

        Debug.Log($"[WaypointPatternCSVLoader] Loaded {patterns.Count} patterns from {path}");
    }

    private void LoadMultiPatternFormat(string[] lines, int startLine, CultureInfo ci)
    {
        // Original format: pattern_id,point_index,x,z
        // Use a temporary dictionary to store waypoints with their indices for sorting
        Dictionary<string, List<(int index, Vector3 waypoint)>> tempPatterns = 
            new Dictionary<string, List<(int, Vector3)>>();
        
        for (int i = startLine; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] parts = lines[i].Split(',');
            if (parts.Length < 4) continue;

            string patternId = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), out int pointIndex)) continue;
            if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, ci, out float x)) continue;
            if (!float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, ci, out float z)) continue;

            if (!tempPatterns.TryGetValue(patternId, out var list))
            {
                list = new List<(int, Vector3)>();
                tempPatterns[patternId] = list;
            }

            list.Add((pointIndex, new Vector3(x, 0f, z)));
        }

        // Sort by point_index and convert to final format
        foreach (var kvp in tempPatterns)
        {
            kvp.Value.Sort((a, b) => a.index.CompareTo(b.index));
            List<Vector3> sortedWaypoints = new List<Vector3>();
            foreach (var item in kvp.Value)
            {
                sortedWaypoints.Add(item.waypoint);
            }
            patterns[kvp.Key] = sortedWaypoints;
        }
    }

    private void LoadSinglePatternFormat(string[] lines, int startLine, CultureInfo ci, bool hasTimeColumn)
    {
        // Format: time,x,z or x,z
        // Extract all waypoints as a single pattern
        List<Vector3> waypoints = new List<Vector3>();
        
        for (int i = startLine; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] parts = lines[i].Split(',');
            if (parts.Length < 2) continue;

            int xIndex = hasTimeColumn ? 1 : 0;
            int zIndex = hasTimeColumn ? 2 : 1;

            if (parts.Length <= zIndex) continue;

            if (float.TryParse(parts[xIndex].Trim(), System.Globalization.NumberStyles.Float, ci, out float x) &&
                float.TryParse(parts[zIndex].Trim(), System.Globalization.NumberStyles.Float, ci, out float z))
            {
                waypoints.Add(new Vector3(x, 0f, z));
            }
        }

        // Use default pattern ID or filename
        string patternId = string.IsNullOrEmpty(defaultPatternId) 
            ? Path.GetFileNameWithoutExtension(csvFileName) 
            : defaultPatternId;
        
        if (waypoints.Count > 0)
        {
            patterns[patternId] = waypoints;
            Debug.Log($"[WaypointPatternCSVLoader] Loaded single pattern '{patternId}' with {waypoints.Count} waypoints from time,x,z format");
        }
        else
        {
            Debug.LogWarning($"[WaypointPatternCSVLoader] No valid waypoints found in single-pattern format CSV");
        }
    }

    public List<Vector3> GetPattern(string patternId = null)
    {
        if (!IsLoaded) Load();

        // If patternId is null or empty, try to get the default pattern
        if (string.IsNullOrEmpty(patternId))
        {
            patternId = string.IsNullOrEmpty(defaultPatternId) 
                ? Path.GetFileNameWithoutExtension(csvFileName) 
                : defaultPatternId;
        }

        // Try exact match first
        if (patterns != null && patterns.TryGetValue(patternId, out var list))
            return new List<Vector3>(list); // return a copy

        // Try to match with zero-padded format (e.g., "pat_1" -> "pat_01")
        if (patterns != null)
        {
            string normalizedPatternId = NormalizePatternId(patternId);
            if (normalizedPatternId != patternId && patterns.TryGetValue(normalizedPatternId, out list))
            {
                Debug.Log($"[WaypointPatternCSVLoader] Pattern '{patternId}' normalized to '{normalizedPatternId}'");
                return new List<Vector3>(list);
            }
        }

        string availablePatterns = patterns != null ? string.Join(", ", patterns.Keys) : "none";
        Debug.LogWarning($"[WaypointPatternCSVLoader] Pattern not found: '{patternId}'. Available patterns: {availablePatterns}");
        return null;
    }

    /// <summary>
    /// Normalize pattern ID to match CSV format (e.g., "pat_1" -> "pat_01", "pat_2" -> "pat_02")
    /// </summary>
    private string NormalizePatternId(string patternId)
    {
        if (string.IsNullOrEmpty(patternId)) return patternId;

        // Check if it matches pattern like "pat_1", "pat_2", etc.
        if (patternId.StartsWith("pat_") && patternId.Length > 4)
        {
            string numberPart = patternId.Substring(4);
            if (int.TryParse(numberPart, out int num))
            {
                // Format with leading zero (e.g., 1 -> "01", 2 -> "02", 10 -> "10")
                return $"pat_{num:D2}";
            }
        }

        return patternId;
    }

    public List<string> GetAllPatternIds()
    {
        if (!IsLoaded) Load();
        return new List<string>(patterns.Keys);
    }
    
    /// <summary>
    /// Get the default pattern ID (for single-pattern CSVs).
    /// </summary>
    public string GetDefaultPatternId()
    {
        if (!IsLoaded) Load();
        
        if (patterns == null || patterns.Count == 0)
            return null;
            
        // If there's only one pattern, return it
        if (patterns.Count == 1)
        {
            var enumerator = patterns.Keys.GetEnumerator();
            enumerator.MoveNext();
            return enumerator.Current;
        }
        
        // Otherwise return the default pattern ID or filename
        string patternId = string.IsNullOrEmpty(defaultPatternId) 
            ? Path.GetFileNameWithoutExtension(csvFileName) 
            : defaultPatternId;
            
        return patterns.ContainsKey(patternId) ? patternId : null;
    }
}
