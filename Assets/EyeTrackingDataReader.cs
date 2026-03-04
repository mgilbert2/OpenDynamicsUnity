using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Reads and manages eye-tracking data from CSV files.
/// Expected CSV format: time,x,y or time,x,z (where x,y/z are gaze positions)
/// </summary>
public class EyeTrackingDataReader
{
    public struct EyeTrackingSample
    {
        public float time;
        public Vector2 position; // XZ coordinates (x, z)

        public EyeTrackingSample(float t, float x, float z)
        {
            time = t;
            position = new Vector2(x, z);
        }
    }

    private List<EyeTrackingSample> samples = new List<EyeTrackingSample>();
    private bool isValid = false;

    public bool IsValid => isValid;
    public int SampleCount => samples.Count;
    public float Duration => samples.Count > 0 ? samples[samples.Count - 1].time - samples[0].time : 0f;
    public float StartTime => samples.Count > 0 ? samples[0].time : 0f;
    public float EndTime => samples.Count > 0 ? samples[samples.Count - 1].time : 0f;

    /// <summary>
    /// Load eye-tracking data from a CSV file.
    /// Supports formats: "time,x,y" or "time,x,z" (header optional)
    /// </summary>
    public bool LoadFromCSV(string filePath, bool hasHeader = true, bool useZInsteadOfY = true)
    {
        samples.Clear();
        isValid = false;

        if (!File.Exists(filePath))
        {
            Debug.LogError($"[EyeTrackingDataReader] File not found: {filePath}");
            return false;
        }

        // Retry logic for file sharing violations (e.g., file open in editor)
        int maxRetries = 5;
        int retryDelayMs = 100;
        
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                string[] lines = File.ReadAllLines(filePath);
                int startIndex = hasHeader && lines.Length > 0 ? 1 : 0;

                for (int i = startIndex; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split(',');
                    if (parts.Length < 3) continue;

                    if (float.TryParse(parts[0], out float time) &&
                        float.TryParse(parts[1], out float x) &&
                        float.TryParse(parts[2], out float coord))
                    {
                        // Always treat third column as Z coordinate (XZ plane)
                        // The useZInsteadOfY parameter is kept for backwards compatibility but always uses Z
                        float z = coord;
                        samples.Add(new EyeTrackingSample(time, x, z));
                    }
                }

                if (samples.Count > 0)
                {
                    // Sort by time to ensure chronological order
                    samples = samples.OrderBy(s => s.time).ToList();
                    isValid = true;
                    Debug.Log($"[EyeTrackingDataReader] Loaded {samples.Count} samples from {filePath} (duration: {Duration:F2}s)");
                    return true;
                }
                else
                {
                    Debug.LogWarning($"[EyeTrackingDataReader] No valid samples found in {filePath}");
                    return false;
                }
            }
            catch (System.IO.IOException ioEx)
            {
                // File sharing violation or other IO error - retry if attempts remain
                if (retry < maxRetries - 1)
                {
                    string errorMsg = ioEx.Message;
                    if (errorMsg.Contains("Sharing violation") || errorMsg.Contains("being used by another process"))
                    {
                        Debug.LogWarning($"[EyeTrackingDataReader] File is locked (likely open in another program). Retrying in {retryDelayMs}ms... (attempt {retry + 1}/{maxRetries})");
                        System.Threading.Thread.Sleep(retryDelayMs);
                        samples.Clear(); // Clear any partial data before retry
                        continue;
                    }
                }
                
                // Last attempt failed or non-retryable error
                Debug.LogError($"[EyeTrackingDataReader] Error reading file {filePath}: {ioEx.Message}");
                if (ioEx.Message.Contains("Sharing violation") || ioEx.Message.Contains("being used by another process"))
                {
                    Debug.LogError($"[EyeTrackingDataReader] SOLUTION: Close the CSV file in any other programs (Excel, text editors, etc.) and try again.");
                }
                return false;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EyeTrackingDataReader] Error reading file {filePath}: {e.Message}");
                return false;
            }
        }
        
        // If we get here, all retries failed
        Debug.LogError($"[EyeTrackingDataReader] Failed to read file after {maxRetries} attempts: {filePath}");
        return false;
    }

    /// <summary>
    /// Get the position at a specific time, with linear interpolation between samples.
    /// </summary>
    public Vector2 GetPositionAtTime(float time, bool loop = false)
    {
        if (samples.Count == 0) return Vector2.zero;
        if (samples.Count == 1) return samples[0].position;

        // Handle looping
        if (loop && Duration > 0f)
        {
            float relativeTime = time - StartTime;
            time = StartTime + (relativeTime % Duration);
        }

        // Clamp to valid time range
        if (time <= samples[0].time)
            return samples[0].position;
        if (time >= samples[samples.Count - 1].time)
            return samples[samples.Count - 1].position;

        // Find the two samples to interpolate between
        for (int i = 0; i < samples.Count - 1; i++)
        {
            if (time >= samples[i].time && time <= samples[i + 1].time)
            {
                float t = (time - samples[i].time) / (samples[i + 1].time - samples[i].time);
                return Vector2.Lerp(samples[i].position, samples[i + 1].position, t);
            }
        }

        return samples[samples.Count - 1].position;
    }

    /// <summary>
    /// Get the first position (starting position).
    /// </summary>
    public Vector2 GetStartPosition()
    {
        return samples.Count > 0 ? samples[0].position : Vector2.zero;
    }

    /// <summary>
    /// Clear all loaded data.
    /// </summary>
    public void Clear()
    {
        samples.Clear();
        isValid = false;
    }
}




