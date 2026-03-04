using UnityEngine;

public class RecallTester : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The magnet or driver whose path defines the target trajectory.")]
    public Transform magnet;
    [Tooltip("The red ball or state point whose recall we’re testing.")]
    public Transform ball;
    [Tooltip("Optional link to the ball's controller (used for noise control between tests).")]
    public StatePointController controller;

    [Header("Test Parameters")]
    [Tooltip("Distance threshold (Unity units = meters). Ball center must stay within this radius to count as in-range.")]
    public float radiusThreshold = 1.5f;
    [Tooltip("Percent of time (0–100) required within threshold to pass.")]
    [Range(0f, 100f)] public float requiredPercent = 80f;
    [Tooltip("Duration of one full magnet cycle (seconds).")]
    public float testDuration = 10f;
    [Tooltip("Sampling interval (seconds).")]
    public float sampleInterval = 0.05f;

    [Header("Noise Control")]
    [Tooltip("Add noise between recall runs.")]
    public bool addNoiseBetweenTests = true;
    public float noiseDuration = 5f;
    public float noiseStrengthDuringPause = 3f;

    [Header("Results (read-only)")]
    public bool testRunning = false;
    public bool testPassed = false;
    [Range(0f, 100f)] public float percentInRange = 0f;
    public int testIndex = 0;

    private float timer = 0f;
    private float elapsed = 0f;
    private int totalSamples = 0;
    private int inRangeSamples = 0;

    private bool inNoisePhase = false;
    private float noiseTimer = 0f;

    void Update()
    {
        // allow manual restart using R key (if using legacy Input)
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.R) && !testRunning && !inNoisePhase)
        {
            StartTest();
        }
#endif

        if (inNoisePhase)
        {
            noiseTimer += Time.deltaTime;
            if (noiseTimer >= noiseDuration)
            {
                EndNoisePhase();
            }
            return;
        }

        if (!testRunning) return;
        if (magnet == null || ball == null) return;

        timer += Time.deltaTime;
        elapsed += Time.deltaTime;

        if (timer >= sampleInterval)
        {
            timer = 0f;
            totalSamples++;

            float dist = Vector3.Distance(magnet.position, ball.position);
            if (dist <= radiusThreshold)
                inRangeSamples++;
        }

        if (elapsed >= testDuration)
        {
            EndTest();
        }
    }

    public void StartTest()
    {
        ResetTest();
        testIndex++;
        testRunning = true;
        Debug.Log($"[RecallTester] 🧠 Recall test #{testIndex} started.");
    }

    public void EndTest()
    {
        testRunning = false;
        if (totalSamples > 0)
            percentInRange = 100f * inRangeSamples / totalSamples;

        testPassed = (percentInRange >= requiredPercent);

        Debug.Log($"[RecallTester] Test #{testIndex} complete: {percentInRange:F1}% in range. " +
                  (testPassed ? "PASS ✅" : "FAIL ❌"));

        // trigger optional noise phase
        if (addNoiseBetweenTests)
            BeginNoisePhase();
    }

    void BeginNoisePhase()
    {
        inNoisePhase = true;
        noiseTimer = 0f;

        if (controller != null)
        {
            controller.addNoise = true;
            controller.noiseStrength = noiseStrengthDuringPause;
        }

        Debug.Log($"[RecallTester] 🌫 Noise phase started ({noiseDuration:F1}s, strength={noiseStrengthDuringPause}).");
    }

    void EndNoisePhase()
    {
        inNoisePhase = false;

        if (controller != null)
            controller.addNoise = false;

        Debug.Log("[RecallTester] Noise phase ended. Ready for next recall test.");

        // automatically start a new recall test after noise ends
        StartTest();
    }

    public void ResetTest()
    {
        timer = 0f;
        elapsed = 0f;
        totalSamples = 0;
        inRangeSamples = 0;
        percentInRange = 0f;
        testPassed = false;
        testRunning = false;
    }
}
