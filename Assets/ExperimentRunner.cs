using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ExperimentRunner : MonoBehaviour
{
    [System.Serializable]
    public class ExperimentConfig
    {
        [Header("Experiment Settings")]
        [Tooltip("Enable/disable this experiment")]
        public bool enabled = true;

        [Tooltip("Name for this experiment run (used in folder names)")]
        public string experimentName = "Experiment1";

        [Tooltip("Random seed for deterministic behavior")]
        public int randomSeed = 42;

        [Header("Patterns")]
        [Tooltip("Patterns to learn during training phase")]
        public List<PatternAsset> trainingPatterns = new List<PatternAsset>();

        [Tooltip("Patterns to test recall during testing phase")]
        public List<PatternAsset> testingPatterns = new List<PatternAsset>();

        [Header("Timing")]
        [Tooltip("Time to wait after resetting before starting next pattern (seconds)")]
        public float resetStabilizationTime = 0.5f;

        [Header("Noise Control")]
        [Tooltip("Delay after training before enabling noise (seconds). Set to 0 for immediate noise before recall.")]
        public float noiseDelayAfterTraining = 0f;

        [Tooltip("Noise strength when enabled")]
        public float noiseStrength = 2.0f;

        [Tooltip("Use white noise (true) or smoothed noise (false)")]
        public bool whiteNoise = true;

        [Tooltip("Noise smoothing factor (only used if whiteNoise is false)")]
        public float noiseSmoothing = 2.0f;

        [Header("Magnet Force Control (Recall Testing)")]
        [Tooltip("Magnet force strength multiplier during recall testing. Set to -1 to use magnet's default value. Lower values make noise more visible.")]
        public float recallMagnetForceMultiplier = -1f;

        [Header("Recall Testing")]
        [Tooltip("Distance threshold for ball to be considered 'in range' of magnet (Unity units)")]
        public float recallRadiusThreshold = 1.5f;

        [Tooltip("Percent of time (0-100) ball must stay within threshold to pass recall test")]
        [Range(0f, 100f)]
        public float recallRequiredPercent = 80f;

        [Tooltip("Sampling interval for recall testing (seconds)")]
        public float recallSampleInterval = 0.05f;
    }

    [Header("References")]
    [Tooltip("Reference to the ball (StatePointController)")]
    public StatePointController ball;

    [Tooltip("Reference to the magnet (ExternalForceSource)")]
    public ExternalForceSource magnet;

    [Tooltip("Reference to the learning system (LearningImprint)")]
    public LearningImprint learningImprint;

    [Header("Experiment Configuration")]
    [Tooltip("List of experiment configurations")]
    public List<ExperimentConfig> experiments = new List<ExperimentConfig>();

    [Header("Logging")]
    [Tooltip("Subfolder name for logs (default: ExperimentLogs)")]
    public string logFolder = "ExperimentLogs";

    [Header("Starting Position")]
    [Tooltip("Starting position for the ball (XZ coordinates, Y will be set by surface)")]
    public Vector3 ballStartPosition = Vector3.zero;

    private string currentExperimentFolder;
    private float experimentStartTime;
    
    // Recall testing state
    private bool recallTestRunning = false;
    private float recallTimer = 0f;
    private int recallTotalSamples = 0;
    private int recallInRangeSamples = 0;
    private float recallPercentInRange = 0f;
    private bool recallTestPassed = false;
    private System.Random experimentRandom;
    private bool noiseActive = false;
    private int currentExperimentSeed = 0;
    private string currentRecallPatternName = "";

    void Start()
    {
        // Find first enabled experiment and run it
        foreach (var config in experiments)
        {
            if (config.enabled)
            {
                StartCoroutine(RunExperiment(config));
                return;
            }
        }

        Debug.LogWarning("[ExperimentRunner] No enabled experiments found!");
    }

    IEnumerator RunExperiment(ExperimentConfig config)
    {
        Debug.Log($"[ExperimentRunner] Starting experiment: {config.experimentName} (seed: {config.randomSeed})");

        // Initialize random seed for deterministic behavior
        UnityEngine.Random.InitState(config.randomSeed);
        currentExperimentSeed = config.randomSeed;
        experimentRandom = new System.Random(currentExperimentSeed);
        noiseActive = false;

        // Setup logging folder
        string folderName = $"{config.experimentName}_seed{config.randomSeed}";
        currentExperimentFolder = Path.Combine(Application.persistentDataPath, logFolder, folderName);
        Directory.CreateDirectory(currentExperimentFolder);

        // Write experiment summary
        WriteExperimentSummary(config);

        experimentStartTime = Time.time;

        // Reset system to initial state (learning cleared, noise off)
        ResetSystem();
        if (ball != null)
            ball.SetNoiseEnabled(false);

        yield return new WaitForSeconds(config.resetStabilizationTime);

        // === TRAINING PHASE ===
        Debug.Log($"[ExperimentRunner] Starting TRAINING phase with {config.trainingPatterns.Count} patterns");
        learningImprint.SetLearningEnabled(true);
        ball?.SetNoiseEnabled(false);

        foreach (var pattern in config.trainingPatterns)
        {
            if (pattern == null)
            {
                Debug.LogWarning("[ExperimentRunner] Skipping null training pattern");
                continue;
            }

            // Load pattern first so we can get its start position
            magnet.LoadPattern(pattern, loopWaypoints: false);
            magnet.ResetPatternProgress();
            
            // Now reset ball to pattern's start position
            if (ball != null && magnet != null)
            {
                Vector3 patternStartPos = magnet.GetPatternStartPosition();
                ball.ResetState(patternStartPos);
                ball.SetNoiseEnabled(false);
            }
            
            yield return new WaitForSeconds(config.resetStabilizationTime);
            yield return StartCoroutine(RunTrainingPattern(pattern, config));
        }

        learningImprint.SetLearningEnabled(false);
        learningImprint.ClearLearningWindows();
        Debug.Log("[ExperimentRunner] TRAINING phase complete - learned landscape preserved");

        // Optional delay before noise is added
        float noiseDelay = Mathf.Max(0f, config.noiseDelayAfterTraining);
        if (noiseDelay > 0f)
        {
            Debug.Log($"[ExperimentRunner] Waiting {noiseDelay:F2}s before enabling noise for recall");
            yield return new WaitForSeconds(noiseDelay);
        }

        EnableNoise(config);

        // === RECALL TESTING PHASE ===
        Debug.Log($"[ExperimentRunner] Starting RECALL TESTING phase with {config.testingPatterns.Count} patterns");
        foreach (var pattern in config.testingPatterns)
        {
            if (pattern == null)
            {
                Debug.LogWarning("[ExperimentRunner] Skipping null testing pattern");
                continue;
            }

            // Load pattern first so we can get its start position
            magnet.LoadPattern(pattern, loopWaypoints: false);
            magnet.ResetPatternProgress();
            
            // Now reset ball to pattern's start position (preserve noise state)
            if (ball != null && magnet != null)
            {
                Vector3 patternStartPos = magnet.GetPatternStartPosition();
                ball.ResetState(patternStartPos);
                // Noise state is already set in EnableNoiseAfterTraining, so preserve it
            }
            
            yield return new WaitForSeconds(config.resetStabilizationTime);
            yield return StartCoroutine(RunRecallPattern(pattern, config));
        }

        // Cleanup
        magnet?.SetForceStrengthMultiplier(-1f);
        ball?.SetNoiseEnabled(false);
        Debug.Log($"[ExperimentRunner] Experiment '{config.experimentName}' complete! Logs saved to: {currentExperimentFolder}");
    }

    IEnumerator RunTrainingPattern(PatternAsset pattern, ExperimentConfig config)
    {
        string patternName = string.IsNullOrEmpty(pattern.patternName) ? pattern.name : pattern.patternName;
        Debug.Log($"[ExperimentRunner] Training pattern: {patternName}");

        SetupLogging(config.experimentName, config.randomSeed, "train", patternName);

        // Pattern is already loaded and ball is already positioned in the calling code
        // Just ensure learning is enabled
        learningImprint.SetLearningEnabled(true);

        yield return StartCoroutine(WaitForPatternCompletion(patternName));

        // Disable logging
        ball.EnableLogging(false);
    }

    IEnumerator RunRecallPattern(PatternAsset pattern, ExperimentConfig config)
    {
        string patternName = string.IsNullOrEmpty(pattern.patternName) ? pattern.name : pattern.patternName;
        currentRecallPatternName = patternName;
        Debug.Log($"[ExperimentRunner] Recall test for pattern: {patternName}");

        SetupLogging(config.experimentName, config.randomSeed, "recall", patternName);

        if (magnet != null && config.recallMagnetForceMultiplier >= 0f)
            magnet.SetForceStrengthMultiplier(config.recallMagnetForceMultiplier);
        else
            magnet?.SetForceStrengthMultiplier(-1f);

        // Pattern is already loaded and ball is already positioned in the calling code
        // Just ensure everything is ready for recall testing
        recallTestRunning = true;
        recallTimer = 0f;
        recallTotalSamples = 0;
        recallInRangeSamples = 0;
        recallPercentInRange = 0f;
        recallTestPassed = false;

        yield return StartCoroutine(CollectRecallSamples(patternName, config));

        // Calculate results
        if (recallTotalSamples > 0)
            recallPercentInRange = 100f * recallInRangeSamples / recallTotalSamples;
        recallTestPassed = (recallPercentInRange >= config.recallRequiredPercent);

        string result = recallTestPassed ? "PASS ✅" : "FAIL ❌";
        Debug.Log($"[ExperimentRunner] Recall '{patternName}': {recallPercentInRange:F1}% in range ({recallInRangeSamples}/{recallTotalSamples}) - {result}");

        ball.EnableLogging(false);
        recallTestRunning = false;
    }

    IEnumerator WaitForPatternCompletion(string patternName)
    {
        if (magnet == null)
        {
            Debug.LogWarning("[ExperimentRunner] Magnet reference missing; cannot wait for pattern completion.");
            yield break;
        }

        const float timeout = 600f; // safety timeout
        float elapsed = 0f;
        while (!magnet.PatternCompleted)
        {
            elapsed += Time.deltaTime;
            if (elapsed > timeout)
            {
                Debug.LogWarning($"[ExperimentRunner] Pattern '{patternName}' timed out after {timeout:F0}s without completion.");
                break;
            }
            yield return null;
        }
    }

    IEnumerator CollectRecallSamples(string patternName, ExperimentConfig config)
    {
        const float timeout = 600f;
        float elapsed = 0f;
        while (magnet != null && !magnet.PatternCompleted)
        {
            elapsed += Time.deltaTime;
            recallTimer += Time.deltaTime;

            if (recallTimer >= config.recallSampleInterval)
            {
                recallTimer = 0f;
                SampleRecallDistance(config);
            }

            if (elapsed > timeout)
            {
                Debug.LogWarning($"[ExperimentRunner] Recall sampling for '{patternName}' timed out after {timeout:F0}s.");
                break;
            }

            yield return null;
        }

        if (recallTotalSamples == 0)
            SampleRecallDistance(config);
    }

    void SampleRecallDistance(ExperimentConfig config)
    {
        if (magnet == null || ball == null) return;

        recallTotalSamples++;
        float dist = Vector3.Distance(magnet.transform.position, ball.transform.position);
        if (dist <= config.recallRadiusThreshold)
            recallInRangeSamples++;
    }

    void EnableNoise(ExperimentConfig config)
    {
        if (ball == null || noiseActive) return;

        // Note: Random seed is already set globally in RunExperiment() via UnityEngine.Random.InitState()
        ball.SetNoiseParameters(config.noiseStrength, config.whiteNoise, config.noiseSmoothing);
        ball.SetNoiseEnabled(true);
        noiseActive = true;
        Debug.Log($"[ExperimentRunner] Noise enabled for recall (strength={config.noiseStrength}, white={config.whiteNoise})");
    }

    void ResetSystem()
    {
        // Clear learned imprints (use this at experiment start only)
        if (learningImprint != null)
        {
            learningImprint.ClearImprint();
            learningImprint.SetLearningEnabled(false);
        }

        // Reset ball position and velocity
        if (ball != null)
        {
            ball.ResetState(ballStartPosition);
            ball.SetNoiseEnabled(false);
        }

        // Reset magnet (will be set when pattern loads, but reset waypoint index)
        if (magnet != null)
        {
            magnet.ResetPatternProgress();
        }

        noiseActive = false;
    }

    void ResetPositionOnly(bool preserveNoise)
    {
        // Reset position and velocity only - KEEP learned imprints
        if (ball != null && magnet != null)
        {
            // Use pattern's starting position if pattern is loaded, otherwise use default
            Vector3 startPos = magnet.GetPatternStartPosition();
            
            ball.ResetState(startPos);
            if (!preserveNoise)
            {
                ball.SetNoiseEnabled(false);
                noiseActive = false;
            }
        }
        else if (ball != null)
        {
            ball.ResetState(ballStartPosition);
            if (!preserveNoise)
            {
                ball.SetNoiseEnabled(false);
                noiseActive = false;
            }
        }

        // Reset magnet (will be set when pattern loads, but reset waypoint index)
        if (magnet != null)
        {
            magnet.ResetPatternProgress();
        }
    }

    void SetupLogging(string experimentName, int seed, string phase, string patternName)
    {
        if (ball == null) return;

        // Generate filename with timestamp
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safePatternName = SanitizeFileName(patternName);
        string fileName = $"{phase}_{safePatternName}_{timestamp}.csv";
        string filePath = Path.Combine(currentExperimentFolder, fileName);

        ball.SetLogPath(filePath);
        ball.EnableLogging(true);
    }

    void WriteExperimentSummary(ExperimentConfig config)
    {
        string summaryPath = Path.Combine(currentExperimentFolder, "experiment_summary.txt");
        using (StreamWriter writer = new StreamWriter(summaryPath))
        {
            writer.WriteLine("=== Experiment Summary ===");
            writer.WriteLine($"Experiment Name: {config.experimentName}");
            writer.WriteLine($"Random Seed: {config.randomSeed}");
            writer.WriteLine($"Reset Stabilization Time: {config.resetStabilizationTime}s");
            writer.WriteLine("Pattern Timing: Single traversal per pattern");
            writer.WriteLine($"Noise Delay After Training: {(config.noiseDelayAfterTraining > 0f ? config.noiseDelayAfterTraining.ToString("F2") + "s" : "Immediate")}");
            writer.WriteLine("Noise Settings:");
            writer.WriteLine($"  Strength: {config.noiseStrength}");
            writer.WriteLine($"  Type: {(config.whiteNoise ? "White" : "Smoothed")}");
            if (!config.whiteNoise)
            {
                writer.WriteLine($"  Smoothing: {config.noiseSmoothing}");
            }
            writer.WriteLine();
            writer.WriteLine("=== Recall Testing Parameters ===");
            writer.WriteLine($"Radius Threshold: {config.recallRadiusThreshold} units");
            writer.WriteLine($"Required Percent: {config.recallRequiredPercent}%");
            writer.WriteLine($"Sample Interval: {config.recallSampleInterval}s");
            writer.WriteLine();
            writer.WriteLine("=== Training Patterns ===");
            for (int i = 0; i < config.trainingPatterns.Count; i++)
            {
                var pattern = config.trainingPatterns[i];
                if (pattern != null)
                {
                    string name = string.IsNullOrEmpty(pattern.patternName) ? pattern.name : pattern.patternName;
                    writer.WriteLine($"  {i + 1}. {name} ({pattern.waypoints?.Count ?? 0} waypoints)");
                }
                else
                {
                    writer.WriteLine($"  {i + 1}. [NULL]");
                }
            }
            writer.WriteLine();
            writer.WriteLine("=== Testing Patterns ===");
            for (int i = 0; i < config.testingPatterns.Count; i++)
            {
                var pattern = config.testingPatterns[i];
                if (pattern != null)
                {
                    string name = string.IsNullOrEmpty(pattern.patternName) ? pattern.name : pattern.patternName;
                    writer.WriteLine($"  {i + 1}. {name} ({pattern.waypoints?.Count ?? 0} waypoints)");
                }
                else
                {
                    writer.WriteLine($"  {i + 1}. [NULL]");
                }
            }
            writer.WriteLine();
            writer.WriteLine($"=== Logs ===");
            writer.WriteLine($"All CSV logs saved in: {currentExperimentFolder}");
            writer.WriteLine($"Format: [train|recall]_PatternName_timestamp.csv");
        }

        Debug.Log($"[ExperimentRunner] Experiment summary written to: {summaryPath}");
    }

    void OnGUI()
    {
        // Guard against rendering during invalid states
        if (Event.current == null) return;
        if (Event.current.type != EventType.Repaint) return;

        // Display recall test results if a test is running or just completed
        if (!recallTestRunning && recallTotalSamples == 0) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 28;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.UpperRight;
        
        float margin = 20f;
        float yPos = 100f; // Start below learning status display
        
        // Recall test status
        if (recallTestRunning)
        {
            style.normal.textColor = Color.yellow;
            float currentPercent = recallTotalSamples > 0 ? (100f * recallInRangeSamples / recallTotalSamples) : 0f;
            string status = $"RECALL TEST: {currentRecallPatternName}\n" +
                           $"Current: {currentPercent:F1}%";
            GUI.Label(new Rect(Screen.width - 400 - margin, yPos, 400, 100), status, style);
        }
        else if (recallTotalSamples > 0)
        {
            // Show final results
            style.normal.textColor = recallTestPassed ? Color.green : Color.red;
            string result = $"RECALL TEST: {currentRecallPatternName}\n" +
                           $"{recallPercentInRange:F1}% - {(recallTestPassed ? "PASS ✅" : "FAIL ❌")}";
            GUI.Label(new Rect(Screen.width - 400 - margin, yPos, 400, 100), result, style);
        }
    }

    string SanitizeFileName(string fileName)
    {
        // Remove invalid filename characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }

    int GetNextDeterministicSeed()
    {
        if (experimentRandom == null)
            experimentRandom = new System.Random(currentExperimentSeed);
        return experimentRandom.Next(0, int.MaxValue);
    }
}

