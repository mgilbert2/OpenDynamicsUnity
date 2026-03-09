using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Runs training and recall experiments for CSV waypoint patterns.
/// For each pattern: trains it, then tests recall with noise.
/// </summary>
public class CSVExperimentRunner : MonoBehaviour
{
    [System.Serializable]
    public class CSVExperimentConfig
    {
        [Header("Experiment Settings")]
        [Tooltip("Enable/disable this experiment")]
        public bool enabled = true;

        [Tooltip("Name for this experiment run (used in folder names)")]
        public string experimentName = "CSV_Experiment1";

        [Tooltip("Random seed for deterministic behavior")]
        public int randomSeed = 42;

        [Header("CSV Patterns")]
        [Tooltip("Pattern IDs to run (empty = run all patterns from CSV). Example: pat_01, pat_02")]
        public List<string> patternIdsToRun = new List<string>();
        
        [Tooltip("Run recall test after each individual pattern (true) or skip recall tests (false)")]
        public bool runRecallAfterEachPattern = true;
        
        [Tooltip("Cumulative/Stacked recall mode: After learning each pattern, test recall for ALL patterns learned so far (1, then 1+2, then 1+2+3, etc.). Learning stays ON throughout.")]
        public bool cumulativeRecallMode = false;
        
        [Tooltip("Randomize recall test order (only applies when runRecallAfterEachPattern = false). Training order stays the same.")]
        public bool randomizeRecallOrder = false;

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
        [Tooltip("Distance threshold for ball to be considered 'in range' of magnet (Unity units). Increase for easier recall (try 2.0-3.0 for better results).")]
        public float recallRadiusThreshold = 2.0f;

        [Tooltip("Percent of time (0-100) ball must stay within threshold to pass recall test")]
        [Range(0f, 100f)]
        public float recallRequiredPercent = 80f;

        [Tooltip("Sampling interval for recall testing (seconds)")]
        public float recallSampleInterval = 0.05f;

        [Header("Training Optimization")]
        [Tooltip("Number of times to train each pattern before recall test. More passes = stronger learning (try 2-3 for better recall).")]
        [Range(1, 5)]
        public int trainingPassesPerPattern = 1;

        [Tooltip("Delay between training passes (seconds). Allows system to stabilize.")]
        public float delayBetweenTrainingPasses = 0.2f;

        [Header("Learning Parameters")]
        [Tooltip("Maximum depth any single well can reach. CRITICAL: Set to 3.0 or lower to prevent stuck ball. 0 = no limit (NOT RECOMMENDED). Applied to LearningImprint component.")]
        public float maxWellDepth = 3.0f;

        [Tooltip("If enabled, automatically normalizes all well depths so the maximum depth equals normalizedDepthTarget. Applied to LearningImprint component.")]
        public bool normalizeDepth = false;

        [Tooltip("Target maximum depth after normalization (used when normalizeDepth is enabled). Applied to LearningImprint component.")]
        public float normalizedDepthTarget = 1.0f;

        [Header("Ball Physics")]
        [Tooltip("Friction/damping coefficient for the ball. Lower = less friction (try 0.5-1.0 for minimal friction). WARNING: Setting to 0 may cause unstable physics or prevent movement. Default: 4.0. Applied to StatePointController component.")]
        [Range(0f, 10f)]
        public float ballDamping = 4f;
    }

    [Header("References")]
    [Tooltip("Reference to the ball (StatePointController)")]
    public StatePointController ball;

    [Tooltip("Reference to the magnet (ExternalForceSource)")]
    public ExternalForceSource magnet;

    [Tooltip("Reference to the learning system (LearningImprint)")]
    public LearningImprint learningImprint;

    [Tooltip("Reference to the potential surface (optional, will try to get from ball if not set)")]
    public PotentialSurface potentialSurface;

    [Tooltip("Reference to the waypoint CSV loader")]
    public WaypointPatternCSVLoader waypointLoader;

    [Header("Experiment Configuration")]
    [Tooltip("List of experiment configurations")]
    public List<CSVExperimentConfig> experiments = new List<CSVExperimentConfig>();

    [Header("Logging")]
    [Tooltip("Subfolder name for logs (default: CSVExperimentLogs)")]
    public string logFolder = "CSVExperimentLogs";

    [Header("Starting Position")]
    [Tooltip("Starting position for the ball (XZ coordinates, Y will be set by surface)")]
    public Vector3 ballStartPosition = Vector3.zero;

    private string currentExperimentFolder;
    private float experimentStartTime;
    
    // Recall testing state
    private float recallTimer = 0f;
    private int recallTotalSamples = 0;
    private int recallInRangeSamples = 0;
    private float recallPercentInRange = 0f;
    private bool recallTestPassed = false;
    private System.Random experimentRandom;
    private bool noiseActive = false;
    private int currentExperimentSeed = 0;
    private string currentRecallPatternName = "";
    private List<string> allPatternIds = new List<string>();
    
    // Recall results tracking
    private Dictionary<string, float> recallResults = new Dictionary<string, float>();  // Final recall rate
    private Dictionary<string, float> recallResultsBest = new Dictionary<string, float>();  // Best recall rate
    private Dictionary<string, bool> recallPassed = new Dictionary<string, bool>();
    private Dictionary<string, int> recallTestCount = new Dictionary<string, int>();  // How many times each pattern was tested
    
    // Recall history for forgetting curves
    private List<RecallHistoryEntry> recallHistory = new List<RecallHistoryEntry>();
    private int currentLearningStage = 0;  // Tracks how many patterns have been learned so far
    
    [System.Serializable]
    private class RecallHistoryEntry
    {
        public string patternId;
        public int stage;  // Number of patterns learned when this test occurred
        public float recallPercent;
        public int testNumber;  // Which test this is for this pattern (1st, 2nd, etc.)
    }

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

        Debug.LogWarning("[CSVExperimentRunner] No enabled experiments found!");
    }

    IEnumerator RunExperiment(CSVExperimentConfig config)
    {
        Debug.Log($"[CSVExperimentRunner] Starting experiment: {config.experimentName} (seed: {config.randomSeed})");

        // Apply learning parameters from config to LearningImprint
        if (learningImprint != null)
        {
            learningImprint.maxWellDepth = config.maxWellDepth;
            learningImprint.normalizeDepth = config.normalizeDepth;
            learningImprint.normalizedDepthTarget = config.normalizedDepthTarget;
            
            Debug.Log($"[CSVExperimentRunner] Applied learning parameters: maxWellDepth={config.maxWellDepth}, normalizeDepth={config.normalizeDepth}, normalizedDepthTarget={config.normalizedDepthTarget}");
        }
        else
        {
            Debug.LogWarning("[CSVExperimentRunner] LearningImprint is null - cannot apply learning parameters from config!");
        }

        // Apply ball physics parameters from config
        if (ball != null)
        {
            ball.damping = config.ballDamping;
            if (config.ballDamping == 0f)
            {
                Debug.LogWarning("[CSVExperimentRunner] ⚠️ ballDamping is set to 0 - this may cause unstable physics or prevent the ball from moving properly!");
            }
            else if (config.ballDamping < 0.5f)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ ballDamping ({config.ballDamping}) is very low - may cause unstable physics. Consider using 0.5-1.0 for minimal friction.");
            }
            Debug.Log($"[CSVExperimentRunner] Applied ball damping: {config.ballDamping}");
        }
        else
        {
            Debug.LogWarning("[CSVExperimentRunner] Ball is null - cannot apply damping parameter!");
        }

        // Load patterns from CSV
        if (waypointLoader == null)
        {
            Debug.LogError("[CSVExperimentRunner] WaypointLoader is not assigned!");
            yield break;
        }

        waypointLoader.Load();
        allPatternIds = waypointLoader.GetAllPatternIds();

        if (allPatternIds.Count == 0)
        {
            Debug.LogError("[CSVExperimentRunner] No patterns found in CSV!");
            yield break;
        }

        // Filter patterns if specific ones are requested
        if (config.patternIdsToRun.Count > 0)
        {
            List<string> validPatterns = new List<string>();
            foreach (string patternId in config.patternIdsToRun)
            {
                string normalizedId = NormalizePatternId(patternId);
                string matchedId = null;
                
                // Try exact match first
                if (allPatternIds.Contains(patternId))
                {
                    matchedId = patternId;
                }
                // Try normalized version (e.g., "pat_1" -> "pat_01")
                else if (normalizedId != patternId && allPatternIds.Contains(normalizedId))
                {
                    matchedId = normalizedId;
                    Debug.Log($"[CSVExperimentRunner] Pattern '{patternId}' normalized to '{normalizedId}'");
                }
                
                if (matchedId != null)
                {
                    validPatterns.Add(matchedId);
                    Debug.Log($"[CSVExperimentRunner] ✓ Pattern '{matchedId}' found and added.");
                }
                else
                {
                    Debug.LogWarning($"[CSVExperimentRunner] ✗ Pattern '{patternId}' not found in CSV. Available patterns: {string.Join(", ", allPatternIds.Take(5))}... (showing first 5)");
                }
            }
            allPatternIds = validPatterns;
        }

        Debug.Log($"[CSVExperimentRunner] Running {allPatternIds.Count} patterns: {string.Join(", ", allPatternIds)}");

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
        {
            ball.SetNoiseEnabled(false);
            
            // Verify ball has all required references for movement
            if (ball.driver == null)
            {
                Debug.LogError("[CSVExperimentRunner] ❌ Ball's 'Driver' (ExternalForceSource) is NOT assigned! The ball won't move. Assign the magnet GameObject in the StatePointController component.");
            }
            else if (ball.driver != magnet)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ Ball's 'Driver' is assigned to '{ball.driver.name}', but magnet is '{magnet.name}'. They should be the same!");
            }
            
            if (ball.field == null)
            {
                Debug.LogError("[CSVExperimentRunner] ❌ Ball's 'Field' (AttractorField) is NOT assigned! The ball won't move.");
            }
            
            if (ball.surface == null)
            {
                Debug.LogError("[CSVExperimentRunner] ❌ Ball's 'Surface' (PotentialSurface) is NOT assigned! The ball won't move.");
            }
            
            if (ball.driver != null && ball.field != null && ball.surface != null)
            {
                Debug.Log("[CSVExperimentRunner] ✓ Ball has all required references (Driver, Field, Surface) - ball should move correctly.");
            }
        }

        yield return new WaitForSeconds(config.resetStabilizationTime);

        // === TRAINING PHASE: Train all patterns first ===
        Debug.Log($"[CSVExperimentRunner] ===== TRAINING ALL PATTERNS =====");
        if (config.cumulativeRecallMode)
        {
            Debug.Log($"[CSVExperimentRunner] CUMULATIVE RECALL MODE: After each pattern, testing recall for ALL patterns learned so far");
        }
        
        // Track patterns learned so far (for cumulative recall mode)
        List<string> patternsLearnedSoFar = new List<string>();
        
        for (int patternIdx = 0; patternIdx < allPatternIds.Count; patternIdx++)
        {
            string patternId = allPatternIds[patternIdx];
            Debug.Log($"[CSVExperimentRunner] ===== Pattern {patternIdx + 1}/{allPatternIds.Count}: {patternId} =====");

            // === TRAINING PHASE ===
            Debug.Log($"[CSVExperimentRunner] ===== TRAINING PHASE for pattern: {patternId} ({config.trainingPassesPerPattern} pass(es)) =====");
            
            // Ensure noise is OFF during training
            if (ball != null)
            {
                ball.SetNoiseEnabled(false);
                Debug.Log("[CSVExperimentRunner] Noise disabled for training");
            }
            
            // Enable learning BEFORE loading pattern
            if (learningImprint != null)
            {
                if (learningImprint.statePoint == null && ball != null)
                {
                    learningImprint.statePoint = ball.transform;
                }
                learningImprint.SetLearningEnabled(true);
                Debug.Log("[CSVExperimentRunner] ✓ Learning ENABLED for training");
            }
            else
            {
                Debug.LogError("[CSVExperimentRunner] LearningImprint is null! Learning will not work.");
            }

            // Load pattern
            magnet.LoadWaypointPatternFromCSV(patternId, loopWaypoints: false, snapToStart: true);
            magnet.ResetPatternProgress();

            // Reset ball to pattern's start position
            if (ball != null && magnet != null)
            {
                // Verify ball has all required references
                if (ball.driver == null)
                {
                    Debug.LogError("[CSVExperimentRunner] Ball's 'driver' (ExternalForceSource) is not assigned! The ball won't move. Assign the magnet in the StatePointController component.");
                }
                if (ball.field == null)
                {
                    Debug.LogError("[CSVExperimentRunner] Ball's 'field' (AttractorField) is not assigned! The ball won't move.");
                }
                if (ball.surface == null)
                {
                    Debug.LogError("[CSVExperimentRunner] Ball's 'surface' (PotentialSurface) is not assigned! The ball won't move.");
                }
                
                Vector3 patternStartPos = magnet.GetPatternStartPosition();
                ball.ResetState(patternStartPos);
                ball.SetNoiseEnabled(false); // Ensure noise stays off
            }

            yield return new WaitForSeconds(config.resetStabilizationTime);

            // Run multiple training passes if configured
            for (int pass = 0; pass < config.trainingPassesPerPattern; pass++)
            {
                if (pass > 0)
                {
                    Debug.Log($"[CSVExperimentRunner] Training pass {pass + 1}/{config.trainingPassesPerPattern} for pattern: {patternId}");
                    // Reload pattern and reset for next pass
                    magnet.LoadWaypointPatternFromCSV(patternId, loopWaypoints: false, snapToStart: true);
                    magnet.ResetPatternProgress();
                    if (ball != null && magnet != null)
                    {
                        Vector3 patternStartPos = magnet.GetPatternStartPosition();
                        ball.ResetState(patternStartPos);
                    }
                    yield return new WaitForSeconds(config.delayBetweenTrainingPasses);
                }
                
                // Run training pattern - learning should be ON during this entire run through all 60 waypoints
                yield return StartCoroutine(RunTrainingPattern(patternId, config));
            }

            // Keep learning ON during training phase (will be disabled after all patterns trained)
            // Don't disable learning here if we're doing batch recall - keep cumulative learning
            
            // Log well count after each pattern training
            if (learningImprint != null)
            {
                int wellCount = learningImprint.GetWellCount();
                Debug.Log($"[CSVExperimentRunner] After training pattern '{patternId}': {wellCount} total attractors (wells) in memory");
            }

            // Add this pattern to the learned list (for cumulative recall mode)
            if (config.cumulativeRecallMode)
            {
                patternsLearnedSoFar.Add(patternId);
            }

            // === RECALL TESTING PHASE ===
            if (config.cumulativeRecallMode)
            {
                // CUMULATIVE RECALL MODE: Test all patterns learned so far
                Debug.Log($"[CSVExperimentRunner] ===== CUMULATIVE RECALL TEST: Testing {patternsLearnedSoFar.Count} pattern(s) learned so far =====");
                
                // Optional delay before noise is added
                float noiseDelay = Mathf.Max(0f, config.noiseDelayAfterTraining);
                if (noiseDelay > 0f)
                {
                    Debug.Log($"[CSVExperimentRunner] Waiting {noiseDelay:F2}s before enabling noise for cumulative recall");
                    yield return new WaitForSeconds(noiseDelay);
                }

                // Disable learning during recall tests (but will re-enable for next pattern training)
                if (learningImprint != null)
                {
                    learningImprint.SetLearningEnabled(false);
                    learningImprint.ClearLearningWindows();
                    Debug.Log("[CSVExperimentRunner] ✓ Learning DISABLED for cumulative recall tests (will re-enable for next pattern)");
                }
                
                // Enable noise for recall tests
                EnableNoise(config);
                Debug.Log("[CSVExperimentRunner] ✓ Noise ENABLED for cumulative recall tests");

                // Update current learning stage (number of patterns learned so far)
                currentLearningStage = patternsLearnedSoFar.Count;

                // Test each pattern learned so far
                for (int recallIdx = 0; recallIdx < patternsLearnedSoFar.Count; recallIdx++)
                {
                    string recallPatternId = patternsLearnedSoFar[recallIdx];
                    Debug.Log($"[CSVExperimentRunner] ===== Cumulative recall test [{recallIdx + 1}/{patternsLearnedSoFar.Count}]: {recallPatternId} =====");

                    // Load pattern for recall test
                    magnet.LoadWaypointPatternFromCSV(recallPatternId, loopWaypoints: false, snapToStart: true);
                    magnet.ResetPatternProgress();

                    // Reset ball to pattern's start position
                    if (ball != null && magnet != null)
                    {
                        Vector3 patternStartPos = magnet.GetPatternStartPosition();
                        ball.ResetState(patternStartPos);
                    }

                    yield return new WaitForSeconds(config.resetStabilizationTime);

                    // Run recall test - learning OFF (testing learned patterns), noise ON
                    yield return StartCoroutine(RunRecallPattern(recallPatternId, config));
                    
                    // Brief pause between recall tests
                    if (recallIdx < patternsLearnedSoFar.Count - 1)
                    {
                        yield return new WaitForSeconds(config.resetStabilizationTime);
                    }
                }
                
                // Disable noise after all cumulative recall tests
                if (ball != null)
                {
                    ball.SetNoiseEnabled(false);
                    Debug.Log("[CSVExperimentRunner] ✓ Noise disabled after cumulative recall tests");
                }
                noiseActive = false;
                
                // Re-enable learning for next pattern (cumulative learning continues)
                if (learningImprint != null && patternIdx < allPatternIds.Count - 1)
                {
                    learningImprint.SetLearningEnabled(true);
                    Debug.Log("[CSVExperimentRunner] ✓ Learning RE-ENABLED for next pattern (cumulative mode)");
                }
            }
            else if (config.runRecallAfterEachPattern)
            {
                // Optional delay before noise is added
                float noiseDelay = Mathf.Max(0f, config.noiseDelayAfterTraining);
                if (noiseDelay > 0f)
                {
                    Debug.Log($"[CSVExperimentRunner] Waiting {noiseDelay:F2}s before enabling noise for recall");
                    yield return new WaitForSeconds(noiseDelay);
                }

                Debug.Log($"[CSVExperimentRunner] ===== RECALL TEST PHASE for pattern: {patternId} =====");
                
                // Ensure learning is OFF before recall test
                if (learningImprint != null)
                {
                    learningImprint.SetLearningEnabled(false);
                    learningImprint.ClearLearningWindows();
                    Debug.Log("[CSVExperimentRunner] ✓ Learning DISABLED for recall test (should be off)");
                }
                
                // Enable noise for recall test
                EnableNoise(config);
                Debug.Log("[CSVExperimentRunner] ✓ Noise ENABLED for recall test");

                // Reload pattern for recall test
                magnet.LoadWaypointPatternFromCSV(patternId, loopWaypoints: false, snapToStart: true);
                magnet.ResetPatternProgress();

                // Reset ball to pattern's start position (noise is already enabled)
                if (ball != null && magnet != null)
                {
                    Vector3 patternStartPos = magnet.GetPatternStartPosition();
                    ball.ResetState(patternStartPos);
                    // Noise is already enabled by EnableNoise()
                }

                yield return new WaitForSeconds(config.resetStabilizationTime);

                // Run recall test - learning should be OFF, noise should be ON
                yield return StartCoroutine(RunRecallPattern(patternId, config));
                
                // Disable noise after recall test
                if (ball != null)
                {
                    ball.SetNoiseEnabled(false);
                    Debug.Log("[CSVExperimentRunner] ✓ Noise disabled after recall test");
                }
            }
            else
            {
                // If not doing recall after each pattern, keep learning ON for cumulative learning
                // Learning will be disabled after all training is complete
                Debug.Log($"[CSVExperimentRunner] Recall test skipped for pattern: {patternId} - continuing with next pattern training");
            }

            // Clear learning windows before next pattern only if doing recall after each (not cumulative mode)
            if (config.runRecallAfterEachPattern && !config.cumulativeRecallMode && learningImprint != null)
            {
                learningImprint.ClearLearningWindows();
                learningImprint.SetLearningEnabled(false);
            }
            
            // In cumulative mode, keep learning ON (don't disable it)
            if (config.cumulativeRecallMode && learningImprint != null)
            {
                // Ensure learning stays ON for next pattern
                learningImprint.SetLearningEnabled(true);
            }
            
            // Ensure noise is off before next training pattern
            if (ball != null)
            {
                ball.SetNoiseEnabled(false);
            }
            noiseActive = false;

            // Delay before next pattern
            if (patternIdx < allPatternIds.Count - 1)
            {
                yield return new WaitForSeconds(config.resetStabilizationTime);
            }
        }

        // === DISABLE LEARNING after all training is complete ===
        // (Only disable if not in cumulative mode, as cumulative mode already tested everything)
        if (learningImprint != null)
        {
            int totalWells = learningImprint.GetWellCount();
            
            // In cumulative mode, learning was kept ON throughout, so disable it now
            if (config.cumulativeRecallMode)
            {
                learningImprint.SetLearningEnabled(false);
                learningImprint.ClearLearningWindows();
                Debug.Log($"[CSVExperimentRunner] ===== CUMULATIVE RECALL MODE COMPLETE: All patterns trained and tested, learning DISABLED =====");
            }
            else
            {
                learningImprint.SetLearningEnabled(false);
                learningImprint.ClearLearningWindows();
                Debug.Log($"[CSVExperimentRunner] ===== TRAINING PHASE COMPLETE: All patterns trained, learning DISABLED =====");
            }
            
            Debug.Log($"[CSVExperimentRunner] Total learned attractors (wells): {totalWells}");
            
            // Log final well statistics
            learningImprint.LogWellStatistics("FINAL - After all training");
            LogWellStatisticsToCSV(config, "FINAL");
            
            // Warn if too many wells (potential performance/interference issue)
            if (totalWells > 5000)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ WARNING: Very high well count ({totalWells}). This may cause performance issues or pattern interference!");
            }
        }

        // === BATCH RECALL TESTING PHASE (if recall after each pattern was disabled AND not cumulative mode) ===
        if (!config.runRecallAfterEachPattern && !config.cumulativeRecallMode && allPatternIds.Count > 0)
        {
            // Create recall order list (shuffled if requested)
            List<string> recallOrder = new List<string>(allPatternIds);
            
            if (config.randomizeRecallOrder)
            {
                // Shuffle using Fisher-Yates algorithm with experiment random seed
                for (int i = recallOrder.Count - 1; i > 0; i--)
                {
                    int j = experimentRandom.Next(i + 1);
                    string temp = recallOrder[i];
                    recallOrder[i] = recallOrder[j];
                    recallOrder[j] = temp;
                }
                
                Debug.Log($"[CSVExperimentRunner] ===== BATCH RECALL TESTING: Testing all {allPatternIds.Count} patterns in RANDOM ORDER =====");
                Debug.Log($"[CSVExperimentRunner] Recall order: {string.Join(", ", recallOrder)}");
            }
            else
            {
                Debug.Log($"[CSVExperimentRunner] ===== BATCH RECALL TESTING: Testing all {allPatternIds.Count} patterns in TRAINING ORDER =====");
            }
            
            // Optional delay before noise is added
            float noiseDelay = Mathf.Max(0f, config.noiseDelayAfterTraining);
            if (noiseDelay > 0f)
            {
                Debug.Log($"[CSVExperimentRunner] Waiting {noiseDelay:F2}s before enabling noise for batch recall");
                yield return new WaitForSeconds(noiseDelay);
            }

            // Ensure learning is OFF
            if (learningImprint != null)
            {
                learningImprint.SetLearningEnabled(false);
                Debug.Log("[CSVExperimentRunner] ✓ Learning DISABLED for batch recall tests");
            }

            // Enable noise for recall tests
            EnableNoise(config);
            Debug.Log("[CSVExperimentRunner] ✓ Noise ENABLED for batch recall tests");

            // Log well count before batch recall
            if (learningImprint != null)
            {
                int wellCount = learningImprint.GetWellCount();
                Debug.Log($"[CSVExperimentRunner] Before batch recall: {wellCount} total attractors (wells) in memory");
            }
            
            // Test each pattern with recall (using recallOrder list)
            for (int patternIdx = 0; patternIdx < recallOrder.Count; patternIdx++)
            {
                string patternId = recallOrder[patternIdx];
                Debug.Log($"[CSVExperimentRunner] ===== Recall test [{patternIdx + 1}/{recallOrder.Count}]: {patternId} =====");

                // Load pattern for recall test
                magnet.LoadWaypointPatternFromCSV(patternId, loopWaypoints: false, snapToStart: true);
                magnet.ResetPatternProgress();

                // Reset ball to pattern's start position
                if (ball != null && magnet != null)
                {
                    Vector3 patternStartPos = magnet.GetPatternStartPosition();
                    ball.ResetState(patternStartPos);
                }

                yield return new WaitForSeconds(config.resetStabilizationTime);

                // Run recall test - learning OFF, noise ON
                yield return StartCoroutine(RunRecallPattern(patternId, config));

                // Disable noise after each recall test
                if (ball != null && patternIdx < recallOrder.Count - 1)
                {
                    ball.SetNoiseEnabled(false);
                    noiseActive = false;
                }

                // Delay before next recall test
                if (patternIdx < recallOrder.Count - 1)
                {
                    yield return new WaitForSeconds(config.resetStabilizationTime);
                    
                    // Re-enable noise for next recall test
                    EnableNoise(config);
                }
            }

            // Disable noise after all recall tests
            if (ball != null)
            {
                ball.SetNoiseEnabled(false);
                noiseActive = false;
                Debug.Log("[CSVExperimentRunner] ✓ Noise disabled after all recall tests");
            }
        }

        // Cleanup
        magnet?.SetForceStrengthMultiplier(-1f);
        ball?.SetNoiseEnabled(false);
        
        // Print recall rate summary
        PrintRecallSummary(config);
        
        // Export potential surface to CSV
        ExportPotentialSurface(config);
        
        Debug.Log($"[CSVExperimentRunner] ✅ Experiment '{config.experimentName}' complete! Logs saved to: {currentExperimentFolder}");
    }

    IEnumerator RunTrainingPattern(string patternId, CSVExperimentConfig config)
    {
        Debug.Log($"[CSVExperimentRunner] Running training pattern: {patternId} (learning should be ON)");

        SetupLogging(config.experimentName, config.randomSeed, "train", patternId);

        // Verify learning is still enabled (it should have been enabled before calling this)
        if (learningImprint != null && learningImprint.statePoint == null && ball != null)
        {
            learningImprint.statePoint = ball.transform;
        }
        
        if (learningImprint == null || learningImprint.statePoint == null)
        {
            Debug.LogError("[CSVExperimentRunner] LearningImprint or statePoint is null! Learning will not work.");
        }
        else
        {
            // Double-check learning is enabled
            learningImprint.SetLearningEnabled(true);
            Debug.Log($"[CSVExperimentRunner] Training: Learning is ON, running through all waypoints...");
        }
        
        // Verify noise is OFF
        if (ball != null && ball.addNoise)
        {
            Debug.LogWarning("[CSVExperimentRunner] WARNING: Noise is ON during training! Disabling...");
            ball.SetNoiseEnabled(false);
        }

        // Wait for pattern to complete (through all 60 waypoints with learning ON)
        yield return StartCoroutine(WaitForPatternCompletion(patternId));

        // Log well statistics after this pattern
        if (learningImprint != null)
        {
            learningImprint.LogWellStatistics($"After training pattern: {patternId}");
            LogWellStatisticsToCSV(config, patternId);
        }

        // Disable logging
        ball.EnableLogging(false);
        Debug.Log($"[CSVExperimentRunner] Training pattern '{patternId}' complete. Learning will be disabled after this.");
    }

    IEnumerator RunRecallPattern(string patternId, CSVExperimentConfig config)
    {
        currentRecallPatternName = patternId;
        Debug.Log($"[CSVExperimentRunner] Running recall test for pattern: {patternId} (learning should be OFF, noise should be ON)");

        SetupLogging(config.experimentName, config.randomSeed, "recall", patternId);

        // Verify learning is OFF
        if (learningImprint != null)
        {
            learningImprint.SetLearningEnabled(false);
            Debug.Log("[CSVExperimentRunner] Recall: Learning confirmed OFF");
        }
        
        // Verify noise is ON
        if (ball != null)
        {
            if (!ball.addNoise)
            {
                Debug.LogWarning("[CSVExperimentRunner] WARNING: Noise is OFF during recall! Enabling...");
                ball.SetNoiseEnabled(true);
            }
            Debug.Log("[CSVExperimentRunner] Recall: Noise confirmed ON");
        }

        if (magnet != null && config.recallMagnetForceMultiplier >= 0f)
            magnet.SetForceStrengthMultiplier(config.recallMagnetForceMultiplier);
        else
            magnet?.SetForceStrengthMultiplier(-1f);

        // Pattern is already loaded and ball is already positioned
        recallTimer = 0f;
        recallTotalSamples = 0;
        recallInRangeSamples = 0;
        recallPercentInRange = 0f;
        recallTestPassed = false;

        yield return StartCoroutine(CollectRecallSamples(patternId, config));

        // Calculate results
        if (recallTotalSamples > 0)
            recallPercentInRange = 100f * recallInRangeSamples / recallTotalSamples;
        recallTestPassed = (recallPercentInRange >= config.recallRequiredPercent);

        // Diagnostic: Check final gradient strength
        if (learningImprint != null && ball != null && ball.field != null)
        {
            Vector3 ballPos = ball.transform.position;
            Vector3 learnedGradient = learningImprint.GetGradientXZ(ballPos);
            float gradientMagnitude = learnedGradient.magnitude;
            int wellCount = learningImprint.GetWellCount();
            
            Debug.Log($"[CSVExperimentRunner] Recall '{patternId}': Learned gradient magnitude: {gradientMagnitude:F4}, Total wells: {wellCount}");
            
            if (gradientMagnitude < 0.1f && wellCount > 100)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ Very weak learned gradient ({gradientMagnitude:F4}) with {wellCount} wells. This suggests pattern interference!");
            }
        }

        string result = recallTestPassed ? "PASS ✅" : "FAIL ❌";
        Debug.Log($"[CSVExperimentRunner] Recall '{patternId}': {recallPercentInRange:F1}% in range ({recallInRangeSamples}/{recallTotalSamples}) - {result}");

        // Store results for summary
        // Track final result (overwrites previous)
        recallResults[patternId] = recallPercentInRange;
        recallPassed[patternId] = recallTestPassed;
        
        // Track best result (only updates if better)
        if (!recallResultsBest.ContainsKey(patternId) || recallPercentInRange > recallResultsBest[patternId])
        {
            recallResultsBest[patternId] = recallPercentInRange;
        }
        
        // Track test count
        if (!recallTestCount.ContainsKey(patternId))
            recallTestCount[patternId] = 0;
        recallTestCount[patternId]++;

        // Log to recall history for forgetting curves (only in cumulative mode)
        if (config.cumulativeRecallMode)
        {
            recallHistory.Add(new RecallHistoryEntry
            {
                patternId = patternId,
                stage = currentLearningStage,
                recallPercent = recallPercentInRange,
                testNumber = recallTestCount[patternId]
            });
        }

        // Compute path comparison metrics (intended vs actual)
        ComputePathComparison(patternId, phase: "recall");

        ball.EnableLogging(false);
    }
    
    void ComputePathComparison(string patternId, string phase)
    {
        if (waypointLoader == null || ball == null) return;
        
        // Get intended waypoints
        List<Vector3> intendedWaypoints = waypointLoader.GetPattern(patternId);
        if (intendedWaypoints == null || intendedWaypoints.Count == 0) return;
        
        // Find the actual path CSV file (most recent one for this pattern and phase)
        string safePatternName = SanitizeFileName(patternId);
        string searchPattern = $"{phase}_{safePatternName}_*.csv";
        string[] files = Directory.GetFiles(currentExperimentFolder, searchPattern);
        
        // Filter out intended path files
        var actualPathFiles = files.Where(f => !f.Contains("_intended_")).OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
        if (actualPathFiles.Count == 0) return;
        
        string actualPathFile = actualPathFiles[0];
        
        try
        {
            // Read actual path
            List<Vector3> actualPath = new List<Vector3>();
            string[] lines = File.ReadAllLines(actualPathFile);
            bool headerSkipped = false;
            
            foreach (string line in lines)
            {
                if (!headerSkipped && (line.Contains("time") || line.Contains("x")))
                {
                    headerSkipped = true;
                    continue;
                }
                
                string[] parts = line.Split(',');
                if (parts.Length >= 4 && float.TryParse(parts[1], out float x) && 
                    float.TryParse(parts[3], out float z))
                {
                    actualPath.Add(new Vector3(x, 0f, z));
                }
            }
            
            if (actualPath.Count == 0) return;
            
            // Compute metrics
            float avgDistance = 0f;
            float maxDistance = 0f;
            float minDistance = float.MaxValue;
            int samplesWithinThreshold = 0;
            float threshold = 2.0f; // Same as recall threshold typically
            
            // For each actual path point, find closest intended waypoint
            foreach (Vector3 actualPos in actualPath)
            {
                float closestDist = float.MaxValue;
                foreach (Vector3 intendedPos in intendedWaypoints)
                {
                    float dist = Vector3.Distance(actualPos, intendedPos);
                    if (dist < closestDist) closestDist = dist;
                }
                
                avgDistance += closestDist;
                if (closestDist > maxDistance) maxDistance = closestDist;
                if (closestDist < minDistance) minDistance = closestDist;
                if (closestDist <= threshold) samplesWithinThreshold++;
            }
            
            avgDistance /= actualPath.Count;
            float percentWithinThreshold = 100f * samplesWithinThreshold / actualPath.Count;
            
            // Log comparison results
            string comparisonFile = actualPathFile.Replace(".csv", "_path_comparison.txt");
            using (StreamWriter writer = new StreamWriter(comparisonFile))
            {
                writer.WriteLine("=== Path Comparison: Intended vs Actual ===");
                writer.WriteLine($"Pattern: {patternId}");
                writer.WriteLine($"Phase: {phase}");
                writer.WriteLine($"Intended Waypoints: {intendedWaypoints.Count}");
                writer.WriteLine($"Actual Path Samples: {actualPath.Count}");
                writer.WriteLine();
                writer.WriteLine("Distance Metrics (actual path to nearest intended waypoint):");
                writer.WriteLine($"  Average Distance: {avgDistance:F4} units");
                writer.WriteLine($"  Minimum Distance: {minDistance:F4} units");
                writer.WriteLine($"  Maximum Distance: {maxDistance:F4} units");
                writer.WriteLine($"  Samples Within {threshold} units: {samplesWithinThreshold}/{actualPath.Count} ({percentWithinThreshold:F1}%)");
                writer.WriteLine();
                writer.WriteLine($"Actual Path File: {Path.GetFileName(actualPathFile)}");
                writer.WriteLine($"Intended Path File: {phase}_{safePatternName}_intended_*.csv");
            }
            
            Debug.Log($"[CSVExperimentRunner] Path comparison for '{patternId}': Avg distance: {avgDistance:F4}, Max: {maxDistance:F4}, Within threshold: {percentWithinThreshold:F1}%");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CSVExperimentRunner] Failed to compute path comparison: {e.Message}");
        }
    }

    IEnumerator WaitForPatternCompletion(string patternName)
    {
        if (magnet == null)
        {
            Debug.LogWarning("[CSVExperimentRunner] Magnet reference missing; cannot wait for pattern completion.");
            yield break;
        }

        const float timeout = 600f; // safety timeout
        float elapsed = 0f;
        while (!magnet.PatternCompleted)
        {
            elapsed += Time.deltaTime;
            if (elapsed > timeout)
            {
                Debug.LogWarning($"[CSVExperimentRunner] Pattern '{patternName}' timed out after {timeout:F0}s without completion.");
                break;
            }
            yield return null;
        }
    }

    IEnumerator CollectRecallSamples(string patternName, CSVExperimentConfig config)
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
                Debug.LogWarning($"[CSVExperimentRunner] Recall sampling for '{patternName}' timed out after {timeout:F0}s.");
                break;
            }

            yield return null;
        }

        if (recallTotalSamples == 0)
            SampleRecallDistance(config);
    }

    void SampleRecallDistance(CSVExperimentConfig config)
    {
        if (magnet == null || ball == null) return;

        recallTotalSamples++;
        float dist = Vector3.Distance(magnet.transform.position, ball.transform.position);
        if (dist <= config.recallRadiusThreshold)
            recallInRangeSamples++;
        
        // Diagnostic: Check gradient strength from learned attractors (every 20 samples to avoid spam)
        if (recallTotalSamples % 20 == 0 && learningImprint != null && ball != null && ball.field != null)
        {
            Vector3 ballPos = ball.transform.position;
            Vector3 learnedGradient = learningImprint.GetGradientXZ(ballPos);
            float gradientMagnitude = learnedGradient.magnitude;
            
            // Log if gradient is suspiciously weak (might indicate interference)
            if (gradientMagnitude < 0.1f && learningImprint.GetWellCount() > 100)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ Weak learned gradient detected: {gradientMagnitude:F4} (wells: {learningImprint.GetWellCount()}). Possible pattern interference!");
            }
        }
    }

    void EnableNoise(CSVExperimentConfig config)
    {
        if (ball == null) return;

        ball.SetNoiseParameters(config.noiseStrength, config.whiteNoise, config.noiseSmoothing);
        ball.SetNoiseEnabled(true);
        noiseActive = true;
        Debug.Log($"[CSVExperimentRunner] ✓ Noise enabled for recall (strength={config.noiseStrength}, white={config.whiteNoise})");
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

        // Reset magnet
        if (magnet != null)
        {
            magnet.ResetPatternProgress();
        }

        noiseActive = false;
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
        
        // Also log the intended waypoint path for comparison
        LogIntendedPath(patternName, phase);
    }
    
    void LogIntendedPath(string patternId, string phase)
    {
        if (waypointLoader == null) return;
        
        List<Vector3> waypoints = waypointLoader.GetPattern(patternId);
        if (waypoints == null || waypoints.Count == 0) return;
        
        // Generate filename for intended path
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string safePatternName = SanitizeFileName(patternId);
        string fileName = $"{phase}_{safePatternName}_intended_{timestamp}.csv";
        string filePath = Path.Combine(currentExperimentFolder, fileName);
        
        try
        {
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("point_index,x,y,z");
                for (int i = 0; i < waypoints.Count; i++)
                {
                    Vector3 wp = waypoints[i];
                    writer.WriteLine($"{i},{wp.x:F6},{wp.y:F6},{wp.z:F6}");
                }
            }
            Debug.Log($"[CSVExperimentRunner] Intended path logged to: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CSVExperimentRunner] Failed to log intended path: {e.Message}");
        }
    }

    void WriteExperimentSummary(CSVExperimentConfig config)
    {
        string summaryPath = Path.Combine(currentExperimentFolder, "experiment_summary.txt");
        using (StreamWriter writer = new StreamWriter(summaryPath))
        {
            writer.WriteLine("=== CSV Experiment Summary ===");
            writer.WriteLine($"Experiment Name: {config.experimentName}");
            writer.WriteLine($"Random Seed: {config.randomSeed}");
            writer.WriteLine($"Reset Stabilization Time: {config.resetStabilizationTime}s");
            writer.WriteLine($"Number of Patterns: {allPatternIds.Count}");
            writer.WriteLine($"Pattern IDs: {string.Join(", ", allPatternIds)}");
            writer.WriteLine();
            writer.WriteLine("Pattern Timing: Single traversal per pattern (60 waypoints each)");
            writer.WriteLine($"Training Passes Per Pattern: {config.trainingPassesPerPattern}");
            writer.WriteLine($"Delay Between Training Passes: {config.delayBetweenTrainingPasses}s");
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
            writer.WriteLine($"Magnet Force Multiplier (Recall): {(config.recallMagnetForceMultiplier >= 0f ? config.recallMagnetForceMultiplier.ToString("F2") : "Default")}");
            writer.WriteLine();
            writer.WriteLine("=== Experiment Structure ===");
            writer.WriteLine("For each pattern:");
            writer.WriteLine($"  1. Training phase: Run pattern {config.trainingPassesPerPattern} time(s) with learning enabled");
            writer.WriteLine("  2. Recall test phase: Run pattern again with noise enabled");
            writer.WriteLine();
        }
    }

    string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }

    void PrintRecallSummary(CSVExperimentConfig config)
    {
        if (recallResults.Count == 0)
        {
            Debug.Log("[CSVExperimentRunner] No recall tests were run.");
            return;
        }

        Debug.Log("");
        Debug.Log("═══════════════════════════════════════════════════════");
        Debug.Log("           RECALL RATE SUMMARY");
        Debug.Log("═══════════════════════════════════════════════════════");
        
        int totalTests = recallResults.Count;
        int passedTests = 0;
        float totalRecallRate = 0f;
        float minRecall = 100f;
        float maxRecall = 0f;
        
        // Sort patterns by ID for consistent output
        var sortedPatterns = new List<string>(recallResults.Keys);
        sortedPatterns.Sort();
        
        foreach (string patternId in sortedPatterns)
        {
            float recallRate = recallResults[patternId];
            bool passed = recallPassed[patternId];
            string status = passed ? "PASS ✅" : "FAIL ❌";
            
            // Show progression info for cumulative recall mode
            if (config.cumulativeRecallMode && recallTestCount.ContainsKey(patternId) && recallTestCount[patternId] > 1)
            {
                float bestRate = recallResultsBest.ContainsKey(patternId) ? recallResultsBest[patternId] : recallRate;
                if (bestRate > recallRate)
                {
                    Debug.Log($"  {patternId}: {recallRate:F1}% (best: {bestRate:F1}%, tested {recallTestCount[patternId]}x) - {status}");
                }
                else
                {
                    Debug.Log($"  {patternId}: {recallRate:F1}% (tested {recallTestCount[patternId]}x) - {status}");
                }
            }
            else
            {
                Debug.Log($"  {patternId}: {recallRate:F1}% - {status}");
            }
            
            if (passed) passedTests++;
            totalRecallRate += recallRate;
            if (recallRate < minRecall) minRecall = recallRate;
            if (recallRate > maxRecall) maxRecall = recallRate;
        }
        
        float averageRecall = totalRecallRate / totalTests;
        float passRate = 100f * passedTests / totalTests;
        
        Debug.Log("═══════════════════════════════════════════════════════");
        Debug.Log($"  Total Patterns Tested: {totalTests}");
        Debug.Log($"  Passed: {passedTests}  |  Failed: {totalTests - passedTests}");
        Debug.Log($"  Pass Rate: {passRate:F1}%");
        Debug.Log($"  Average Recall Rate: {averageRecall:F1}%");
        Debug.Log($"  Min Recall Rate: {minRecall:F1}%");
        Debug.Log($"  Max Recall Rate: {maxRecall:F1}%");
        Debug.Log($"  Required Recall: {config.recallRequiredPercent:F1}%");
        Debug.Log("═══════════════════════════════════════════════════════");
        Debug.Log("");
        
        // Save summary to file
        SaveRecallSummaryToFile(config, sortedPatterns, averageRecall, passRate, minRecall, maxRecall);
        
        // Save recall history for forgetting curves
        if (config.cumulativeRecallMode && recallHistory.Count > 0)
        {
            SaveRecallHistoryToFile();
        }
    }
    
    void SaveRecallHistoryToFile()
    {
        try
        {
            string historyPath = Path.Combine(currentExperimentFolder, "recall_history.csv");
            using (StreamWriter writer = new StreamWriter(historyPath))
            {
                writer.WriteLine("patternId,stage,recallPercent,testNumber");
                foreach (var entry in recallHistory)
                {
                    writer.WriteLine($"{entry.patternId},{entry.stage},{entry.recallPercent:F1},{entry.testNumber}");
                }
            }
            Debug.Log($"[CSVExperimentRunner] Saved recall history to: {historyPath} ({recallHistory.Count} entries)");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CSVExperimentRunner] Failed to save recall history: {e.Message}");
        }
    }

    void SaveRecallSummaryToFile(CSVExperimentConfig config, List<string> sortedPatterns, float averageRecall, float passRate, float minRecall, float maxRecall)
    {
        try
        {
            string summaryPath = Path.Combine(currentExperimentFolder, "recall_summary.txt");
            using (StreamWriter writer = new StreamWriter(summaryPath))
            {
                writer.WriteLine("═══════════════════════════════════════════════════════");
                writer.WriteLine("           RECALL RATE SUMMARY");
                writer.WriteLine("═══════════════════════════════════════════════════════");
                writer.WriteLine($"Experiment: {config.experimentName}");
                writer.WriteLine($"Random Seed: {config.randomSeed}");
                writer.WriteLine($"Date: {System.DateTime.Now}");
                if (config.cumulativeRecallMode)
                {
                    writer.WriteLine($"Mode: CUMULATIVE RECALL (patterns tested after each new pattern learned)");
                }
                else if (config.runRecallAfterEachPattern)
                {
                    writer.WriteLine($"Mode: PER-PATTERN RECALL (each pattern tested immediately after training)");
                }
                else
                {
                    writer.WriteLine($"Mode: BATCH RECALL (all patterns tested after all training complete)");
                }
                writer.WriteLine();
                
                writer.WriteLine("Individual Pattern Results:");
                writer.WriteLine("  (Final recall rate after all patterns learned)");
                foreach (string patternId in sortedPatterns)
                {
                    float recallRate = recallResults[patternId];
                    bool passed = recallPassed[patternId];
                    string status = passed ? "PASS" : "FAIL";
                    
                    // Show progression info for cumulative recall mode
                    if (config.cumulativeRecallMode && recallTestCount.ContainsKey(patternId) && recallTestCount[patternId] > 1)
                    {
                        float bestRate = recallResultsBest.ContainsKey(patternId) ? recallResultsBest[patternId] : recallRate;
                        if (bestRate > recallRate)
                        {
                            writer.WriteLine($"  {patternId}: {recallRate:F1}% (best: {bestRate:F1}%, tested {recallTestCount[patternId]}x) - {status}");
                        }
                        else
                        {
                            writer.WriteLine($"  {patternId}: {recallRate:F1}% (tested {recallTestCount[patternId]}x) - {status}");
                        }
                    }
                    else
                    {
                        writer.WriteLine($"  {patternId}: {recallRate:F1}% - {status}");
                    }
                }
                
                writer.WriteLine();
                writer.WriteLine("═══════════════════════════════════════════════════════");
                writer.WriteLine($"  Total Patterns Tested: {recallResults.Count}");
                writer.WriteLine($"  Passed: {recallPassed.Count(kvp => kvp.Value)}  |  Failed: {recallPassed.Count(kvp => !kvp.Value)}");
                writer.WriteLine($"  Pass Rate: {passRate:F1}%");
                writer.WriteLine($"  Average Recall Rate: {averageRecall:F1}%");
                writer.WriteLine($"  Min Recall Rate: {minRecall:F1}%");
                writer.WriteLine($"  Max Recall Rate: {maxRecall:F1}%");
                writer.WriteLine($"  Required Recall: {config.recallRequiredPercent:F1}%");
                writer.WriteLine("═══════════════════════════════════════════════════════");
            }
            
            Debug.Log($"[CSVExperimentRunner] Recall summary saved to: {summaryPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CSVExperimentRunner] Failed to save recall summary: {e.Message}");
        }
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

    /// <summary>
    /// Exports the potential surface coordinates to CSV after experiment completion.
    /// </summary>
    private void ExportPotentialSurface(CSVExperimentConfig config)
    {
        // Try to find PotentialSurface
        PotentialSurface surface = potentialSurface;
        
        if (surface == null && ball != null)
        {
            // Try to get from ball's surface reference
            surface = ball.surface;
        }
        
        if (surface == null)
        {
            // Try to find in scene
            surface = FindObjectOfType<PotentialSurface>();
        }
        
        if (surface == null)
        {
            Debug.LogWarning("[CSVExperimentRunner] PotentialSurface not found. Skipping surface export.");
            return;
        }
        
        // Create export path in experiment folder
        string fileName = $"potential_surface_{config.experimentName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string exportPath = Path.Combine(currentExperimentFolder, fileName);
        
        if (surface.ExportSurfaceToCSV(exportPath))
        {
            Debug.Log($"[CSVExperimentRunner] ✓ Potential surface exported to: {exportPath}");
        }
        else
        {
            Debug.LogWarning($"[CSVExperimentRunner] Failed to export potential surface to: {exportPath}");
        }
    }

    /// <summary>
    /// Logs well statistics to CSV file for comparison analysis.
    /// </summary>
    private void LogWellStatisticsToCSV(CSVExperimentConfig config, string patternId)
    {
        if (learningImprint == null) return;

        try
        {
            string fileName = $"well_statistics_{config.experimentName}.csv";
            string filePath = Path.Combine(currentExperimentFolder, fileName);
            
            // Add pattern ID as a comment/metadata in the export
            // We'll use a separate column for pattern context
            string context = patternId;
            
            // Export with pattern context
            if (learningImprint.ExportWellStatisticsToCSV(filePath, patternId))
            {
                Debug.Log($"[CSVExperimentRunner] ✓ Well statistics logged to: {filePath} (Pattern: {patternId})");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CSVExperimentRunner] Failed to log well statistics: {e.Message}");
        }
    }
}
