using System.Collections.Generic;
using UnityEngine;

public class PathRecallTester : MonoBehaviour
{
    [Header("References")]
    public Transform magnet; // magnet path (during learning)
    public Transform ball;   // red ball

    [Header("Recording Parameters")]
    public bool recordDuringLearning = true;
    public float sampleInterval = 0.05f; // seconds

    [Header("Recall Test Parameters")]
    [Tooltip("Distance threshold for being considered inside the learned groove.")]
    public float pathRadius = 1.5f;
    [Tooltip("Required percentage of time ball stays within groove to pass.")]
    [Range(0f, 100f)] public float requiredPercent = 80f;
    [Tooltip("How long to monitor recall after learning (seconds).")]
    public float testDuration = 10f;

    [Header("Results (read-only)")]
    public bool testRunning = false;
    public bool testPassed = false;
    public float percentInGroove = 0f;

    private List<Vector3> learnedPath = new();
    private float timer = 0f;
    private float elapsed = 0f;
    private int totalSamples = 0;
    private int insideSamples = 0;

    // During learning, call this externally to store magnet’s path
    public void RecordLearningPoint()
    {
        if (magnet != null)
            learnedPath.Add(magnet.position);
    }

    // Stop learning → call this to start recall test
    public void StartRecallTest()
    {
        if (learnedPath.Count == 0)
        {
            Debug.LogWarning("[PathRecallTester] No learned path to test against!");
            return;
        }

        elapsed = 0f;
        timer = 0f;
        totalSamples = 0;
        insideSamples = 0;
        percentInGroove = 0f;
        testPassed = false;
        testRunning = true;

        Debug.Log("[PathRecallTester] Recall test started.");
    }

    void Update()
    {
        // --- Record phase ---
        if (recordDuringLearning && magnet != null)
        {
            timer += Time.deltaTime;
            if (timer >= sampleInterval)
            {
                timer = 0f;
                learnedPath.Add(magnet.position);
            }
        }

        // --- Recall test phase ---
        if (!testRunning || ball == null) return;

        elapsed += Time.deltaTime;
        if (elapsed >= testDuration)
        {
            EndRecallTest();
            return;
        }

        totalSamples++;
        if (IsWithinLearnedPath(ball.position))
            insideSamples++;
    }

    bool IsWithinLearnedPath(Vector3 point)
    {
        // Find nearest learned path point
        float minDist = float.MaxValue;
        foreach (var p in learnedPath)
        {
            float dist = Vector3.Distance(p, point);
            if (dist < minDist) minDist = dist;
        }
        return (minDist <= pathRadius);
    }

    void EndRecallTest()
    {
        testRunning = false;
        percentInGroove = (totalSamples > 0)
            ? 100f * insideSamples / totalSamples
            : 0f;

        testPassed = (percentInGroove >= requiredPercent);

        Debug.Log($"[PathRecallTester] Recall complete: {percentInGroove:F1}% in groove. " +
                  (testPassed ? "PASS ✅" : "FAIL ❌"));
    }

    public void ClearLearnedPath()
    {
        learnedPath.Clear();
        Debug.Log("[PathRecallTester] Cleared learned path.");
    }
}
