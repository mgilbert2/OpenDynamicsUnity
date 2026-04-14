using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public int randomSeed = 1702;

        [Header("CSV Patterns")]
        [Tooltip("Pattern IDs to run (empty = run all patterns from CSV). Example: pat_01, pat_02")]
        public List<string> patternIdsToRun = new List<string>
        {
            "test_01", "test_02", "test_03", "test_04", "test_05", "test_06", "test_07"
        };

        [Tooltip("If set, overrides WaypointPatternCSVLoader.csvFileName for this experiment only (lets you switch stimulus sets per run).")]
        public string stimulusCsvFileName = "";
        
        [Tooltip("Run recall test after each individual pattern (true) or skip recall tests (false)")]
        public bool runRecallAfterEachPattern = false;
        
        [Tooltip("Cumulative/Stacked recall mode: After learning each pattern, test recall for ALL patterns learned so far (1, then 1+2, then 1+2+3, etc.). Learning stays ON throughout.")]
        public bool cumulativeRecallMode = true;
        
        [Tooltip("Randomize recall test order (only applies when runRecallAfterEachPattern = false). Training order stays the same.")]
        public bool randomizeRecallOrder = false;

        [Header("Timing")]
        [Tooltip("Time to wait after resetting before starting next pattern (seconds)")]
        public float resetStabilizationTime = 0f;

        [Header("Noise Control")]
        [Tooltip("Delay after training before enabling noise (seconds). Set to 0 for immediate noise before recall.")]
        public float noiseDelayAfterTraining = 0f;

        public enum RecallNoiseTarget
        {
            Ball,
            Magnet,
            Both,
            None
        }

        [Tooltip("Where to apply noise during recall. Use Magnet to avoid jittery ball dynamics while still making recall noisy.")]
        public RecallNoiseTarget recallNoiseTarget = RecallNoiseTarget.Magnet;

        [Tooltip("Noise strength when enabled")]
        public float noiseStrength = 100f;

        [Tooltip("Use white noise (true) or smoothed noise (false)")]
        public bool whiteNoise = false;

        [Tooltip("Noise smoothing factor (only used if whiteNoise is false)")]
        public float noiseSmoothing = 0f;

        [Tooltip("Magnet noise strength (world units) when recallNoiseTarget includes Magnet.")]
        public float magnetNoiseStrength = 100f;

        [Tooltip("Use white noise (true) or smoothed noise (false) for magnet noise.")]
        public bool magnetNoiseWhite = false;

        [Tooltip("Scales driving jitter when magnetNoiseWhite is false (OU diffusion).")]
        public float magnetNoiseSmoothing = 0.04f;

        [Tooltip("Mean reversion (1/s) for smoothed magnet noise. Higher = cue snaps back toward the path; reduces sideways drift of the whole pattern.")]
        public float magnetNoiseMeanReversion = 2.57f;

        [Header("Cue Fade System")]
        [Tooltip("If enabled, uses the cue fade system: full cue at start, then magnet fades off after cueOffAtProgress so ball rolls on momentum + wells.")]
        public bool enableCueFadeSystem = true;

        [Tooltip("If true, magnet strength will not fade down to 0 during recall (keeps a clean cue). Turn off to allow full cue fade.")]
        public bool neverTurnOffMagnetDuringRecall = false;

        [Tooltip("Waypoint progress (0–1) at which magnet begins to turn off. 0.25 = first quarter, 0.5 = halfway. Ball relies on momentum + learned grooves after this. Higher = more partial cue, more reliance on wells.")]
        [Range(0.05f, 0.95f)]
        public float cueOffAtProgress = 0.05f;
        
        [Header("Legacy Magnet Force Control (Recall Testing)")]
        [Tooltip("LEGACY: Magnet force strength multiplier during recall testing when cue fade system is DISABLED. Set to -1 to use magnet's default value.")]
        public float recallMagnetForceMultiplier = 1f;

        [Header("Recall Testing")]
        [Tooltip("Distance threshold for ball to be considered 'in range' of magnet (Unity units). Increase for easier recall (try 2.0-3.0 for better results).")]
        public float recallRadiusThreshold = 1.5f;

        [Tooltip("Percent of time (0-100) ball must stay within threshold to pass recall test")]
        [Range(0f, 100f)]
        public float recallRequiredPercent = 90.5f;

        [Tooltip("Sampling interval for recall testing (seconds)")]
        public float recallSampleInterval = 0.5f;

        [Header("Training Optimization")]
        [Tooltip("Number of times to train each pattern before recall test. More passes = stronger learning (try 2-3 for better recall).")]
        [Range(1, 5)]
        public int trainingPassesPerPattern = 1;

        [Tooltip("Delay between training passes (seconds). Allows system to stabilize.")]
        public float delayBetweenTrainingPasses = 0f;

        [Header("Learning Parameters")]
        [Tooltip("Maximum depth any single well can reach. CRITICAL: Set to 3.0 or lower to prevent stuck ball. 0 = no limit (NOT RECOMMENDED). Applied to LearningImprint component.")]
        public float maxWellDepth = 1f;

        [Tooltip("If enabled, automatically normalizes all well depths so the maximum depth equals normalizedDepthTarget. Applied to LearningImprint component.")]
        public bool normalizeDepth = false;

        [Tooltip("Target maximum depth after normalization (used when normalizeDepth is enabled). Applied to LearningImprint component.")]
        public float normalizedDepthTarget = 1.0f;

        [Tooltip("Width (spread) of each learned well. Larger = smoother gradients; smaller = tighter wells. Applied to LearningImprint. Default: 1.2. Avoid going below ~0.3.")]
        [Range(0.2f, 3f)]
        public float hypoWidth = 0.2f;

        [Tooltip("Merge nearby wells within this fraction of hypoWidth (0 = no merge). Slight merge (e.g. 0.2–0.4) smooths the learned groove. Applied to LearningImprint.")]
        [Range(0f, 1f)]
        public float wellMergeDistance = 0.343f;

        [Header("Ball Physics")]
        [Tooltip("Friction/damping coefficient for the ball. Lower = less friction (try 0.5-1.0 for minimal friction). WARNING: Setting to 0 may cause unstable physics or prevent movement. Default: 4.0. Applied to StatePointController component.")]
        [Range(0f, 10f)]
        public float ballDamping = 2.55f;
        
        [Tooltip("Maximum speed the ball can reach (world units/s). Higher = ball can maintain more momentum and roll further. Default: 12.0. Applied to StatePointController component.")]
        [Range(1f, 50f)]
        public float ballMaxSpeed = 6.3f;
        
        [Tooltip("Velocity multiplier applied each frame throughout the simulation (1.0 = normal, >1.0 = boost velocity, <1.0 = reduce velocity). Applied after damping but before maxSpeed cap. Useful for fine-tuning ball momentum. Default: 1.0. Applied to StatePointController component.")]
        [Range(0.1f, 5f)]
        public float ballVelocityMultiplier = 0.62f;
        
        [Tooltip("Weight on the potential landscape gradient (how strongly wells pull the ball). Higher = stronger well attraction, ball follows grooves more tightly. Lower = weaker attraction, ball can wander more. Default: 10.0. Applied to StatePointController component.")]
        [Range(1f, 30f)]
        public float landscapeGain = 4.05f;
        
        [Tooltip("Weight on the external magnet force. Higher = magnet pulls ball more strongly during the cued part of recall (until cueOffAtProgress). Lower = gentler magnet pull. CRITICAL: Must be above 55 for magnet to work effectively. Default: 60.0. Applied to StatePointController component.")]
        [Range(0f, 200f)]
        public float externalGain = 57.4f;

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

    [Header("Batch queue (optional)")]
    [Tooltip("If non-empty and the file exists under StreamingAssets, runs all enabled JSON entries in order (see experiment_master.R). Inspector list is skipped unless this fails.")]
    public string batchFileName = "";

    [Tooltip("If true and no batch file is used (or missing), runs every enabled experiment in the Inspector list sequentially instead of only the first.")]
    public bool runAllEnabledInspectorExperiments = false;

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

    // Recall HUD (OnGUI): pattern index, magnet on/off, pass/fail
    private bool recallTestRunning = false;
    private int currentRecallPatternNumber = 1;
    private int totalPatternsForGui = 0;
    private bool lastRecallMagnetCueOn = true;
    
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
        if (!string.IsNullOrWhiteSpace(batchFileName))
        {
            string batchPath = Path.Combine(Application.streamingAssetsPath, batchFileName.Trim());
            if (File.Exists(batchPath))
            {
                Debug.Log($"[CSVExperimentRunner] Running experiment batch from: {batchPath}");
                StartCoroutine(RunExperimentBatchFromFile(batchPath));
                return;
            }

            Debug.LogWarning($"[CSVExperimentRunner] batchFileName is '{batchFileName}' but file not found at {batchPath}. Falling back to Inspector list.");
        }

        if (runAllEnabledInspectorExperiments)
        {
            StartCoroutine(RunAllEnabledInspectorExperiments());
            return;
        }

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

    IEnumerator RunAllEnabledInspectorExperiments()
    {
        bool any = false;
        foreach (var config in experiments)
        {
            if (config == null || !config.enabled)
                continue;
            any = true;
            yield return StartCoroutine(RunExperiment(config));
        }

        if (!any)
            Debug.LogWarning("[CSVExperimentRunner] runAllEnabledInspectorExperiments: no enabled entries in experiments list.");
        else
            Debug.Log("[CSVExperimentRunner] Finished all enabled Inspector experiments.");
    }

    IEnumerator RunExperimentBatchFromFile(string batchPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(batchPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CSVExperimentRunner] Failed to read batch file: {ex.Message}");
            yield break;
        }

        ExperimentBatchWrapper wrapper = JsonUtility.FromJson<ExperimentBatchWrapper>(json);
        if (wrapper == null || wrapper.entries == null || wrapper.entries.Length == 0)
        {
            Debug.LogError("[CSVExperimentRunner] Batch JSON has no entries.");
            yield break;
        }

        for (int i = 0; i < wrapper.entries.Length; i++)
        {
            ExperimentBatchEntry entry = wrapper.entries[i];
            if (entry == null || !entry.enabled)
                continue;

            CSVExperimentConfig config = ConfigFromBatchEntry(entry);
            Debug.Log($"[CSVExperimentRunner] Batch item {i + 1}/{wrapper.entries.Length}: {config.experimentName} (seed {config.randomSeed})");
            yield return StartCoroutine(RunExperiment(config));
        }

        Debug.Log("[CSVExperimentRunner] Batch file complete.");
    }

    static CSVExperimentConfig ConfigFromBatchEntry(ExperimentBatchEntry e)
    {
        var c = new CSVExperimentConfig
        {
            enabled = e.enabled,
            experimentName = e.experimentName,
            randomSeed = e.randomSeed,
            stimulusCsvFileName = e.stimulusCsvFileName ?? "",
            runRecallAfterEachPattern = e.runRecallAfterEachPattern,
            cumulativeRecallMode = e.cumulativeRecallMode,
            randomizeRecallOrder = e.randomizeRecallOrder,
            resetStabilizationTime = e.resetStabilizationTime,
            noiseDelayAfterTraining = e.noiseDelayAfterTraining,
            recallNoiseTarget = (CSVExperimentConfig.RecallNoiseTarget)Mathf.Clamp(e.recallNoiseTarget, 0, 3),
            noiseStrength = e.noiseStrength,
            whiteNoise = e.whiteNoise,
            noiseSmoothing = e.noiseSmoothing,
            magnetNoiseStrength = e.magnetNoiseStrength,
            magnetNoiseWhite = e.magnetNoiseWhite,
            magnetNoiseSmoothing = e.magnetNoiseSmoothing,
            magnetNoiseMeanReversion = e.magnetNoiseMeanReversion,
            enableCueFadeSystem = e.enableCueFadeSystem,
            neverTurnOffMagnetDuringRecall = e.neverTurnOffMagnetDuringRecall,
            cueOffAtProgress = e.cueOffAtProgress,
            recallMagnetForceMultiplier = e.recallMagnetForceMultiplier,
            recallRadiusThreshold = e.recallRadiusThreshold,
            recallRequiredPercent = e.recallRequiredPercent,
            recallSampleInterval = e.recallSampleInterval,
            trainingPassesPerPattern = Mathf.Clamp(e.trainingPassesPerPattern, 1, 5),
            delayBetweenTrainingPasses = e.delayBetweenTrainingPasses,
            maxWellDepth = e.maxWellDepth,
            normalizeDepth = e.normalizeDepth,
            normalizedDepthTarget = e.normalizedDepthTarget,
            hypoWidth = e.hypoWidth,
            wellMergeDistance = e.wellMergeDistance,
            ballDamping = e.ballDamping,
            ballMaxSpeed = e.ballMaxSpeed,
            ballVelocityMultiplier = e.ballVelocityMultiplier,
            landscapeGain = e.landscapeGain,
            externalGain = e.externalGain
        };

        c.patternIdsToRun = new List<string>();
        if (e.patternIdsToRun != null)
        {
            foreach (string id in e.patternIdsToRun)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    c.patternIdsToRun.Add(id.Trim());
            }
        }

        return c;
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
            
            Debug.Log($"[CSVExperimentRunner] Applied learning parameters: maxWellDepth={config.maxWellDepth}, normalizeDepth={config.normalizeDepth}");
        }
        else
        {
            Debug.LogWarning("[CSVExperimentRunner] LearningImprint is null - cannot apply learning parameters from config!");
        }

        // Apply ball physics parameters from config
        if (ball != null)
        {
            ball.damping = config.ballDamping;
            ball.maxSpeed = config.ballMaxSpeed;
            ball.velocityMultiplier = config.ballVelocityMultiplier;
            ball.landscapeGain = config.landscapeGain;
            ball.externalGain = config.externalGain;
            
            
            if (config.ballDamping == 0f)
            {
                Debug.LogWarning("[CSVExperimentRunner] ⚠️ ballDamping is set to 0 - this may cause unstable physics or prevent the ball from moving properly!");
            }
            else if (config.ballDamping < 0.5f)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ ballDamping ({config.ballDamping}) is very low - may cause unstable physics. Consider using 0.5-1.0 for minimal friction.");
            }
            Debug.Log($"[CSVExperimentRunner] Applied ball physics: damping={config.ballDamping}, maxSpeed={config.ballMaxSpeed}, velocityMultiplier={config.ballVelocityMultiplier}, landscapeGain={config.landscapeGain}, externalGain={config.externalGain}");
        }
        else
        {
            Debug.LogWarning("[CSVExperimentRunner] Ball is null - cannot apply physics parameters!");
        }

        // Load patterns from CSV
        if (waypointLoader == null)
        {
            Debug.LogError("[CSVExperimentRunner] WaypointLoader is not assigned!");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(config.stimulusCsvFileName))
        {
            waypointLoader.csvFileName = config.stimulusCsvFileName.Trim();
            Debug.Log($"[CSVExperimentRunner] Using stimulus CSV: {waypointLoader.csvFileName}");
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
            magnet?.SetRecallNoiseEnabled(false);
            
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
            magnet?.SetRecallNoiseEnabled(false);
            
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

            // Load pattern (hold magnet at waypoint 0 during stabilization so it does not advance along the path)
            magnet.SetWaypointMovementHold(true);
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
                magnet?.SetRecallNoiseEnabled(false);
            }

            yield return new WaitForSeconds(config.resetStabilizationTime);

            // Run multiple training passes if configured
            for (int pass = 0; pass < config.trainingPassesPerPattern; pass++)
            {
                if (pass > 0)
                {
                    Debug.Log($"[CSVExperimentRunner] Training pass {pass + 1}/{config.trainingPassesPerPattern} for pattern: {patternId}");
                    // Reload pattern and reset for next pass
                    magnet.SetWaypointMovementHold(true);
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
                magnet.SetWaypointMovementHold(false);
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
                    magnet.SetWaypointMovementHold(true);
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
                    magnet.SetWaypointMovementHold(false);
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
                magnet?.SetRecallNoiseEnabled(false);
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
                magnet.SetWaypointMovementHold(true);
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
                magnet.SetWaypointMovementHold(false);
                yield return StartCoroutine(RunRecallPattern(patternId, config));
                
                // Disable noise after recall test
                if (ball != null)
                {
                    ball.SetNoiseEnabled(false);
                    Debug.Log("[CSVExperimentRunner] ✓ Noise disabled after recall test");
                }
                magnet?.SetRecallNoiseEnabled(false);
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
            magnet?.SetRecallNoiseEnabled(false);
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
                magnet.SetWaypointMovementHold(true);
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
                magnet.SetWaypointMovementHold(false);
                yield return StartCoroutine(RunRecallPattern(patternId, config));

                // Disable noise after each recall test
                if (ball != null && patternIdx < recallOrder.Count - 1)
                {
                    ball.SetNoiseEnabled(false);
                    noiseActive = false;
                }
                if (patternIdx < recallOrder.Count - 1)
                    magnet?.SetRecallNoiseEnabled(false);

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
            magnet?.SetRecallNoiseEnabled(false);
        }

        // Cleanup
        magnet?.SetWaypointMovementHold(false);
        magnet?.SetForceStrengthMultiplier(-1f);
        ball?.SetNoiseEnabled(false);
        magnet?.SetRecallNoiseEnabled(false);
        
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
        magnet?.SetRecallNoiseEnabled(false);

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
        totalPatternsForGui = allPatternIds != null ? allPatternIds.Count : 0;
        int patIdx = allPatternIds != null ? allPatternIds.IndexOf(patternId) : -1;
        currentRecallPatternNumber = patIdx >= 0 ? patIdx + 1 : 1;
        recallTestRunning = true;
        lastRecallMagnetCueOn = true;
        Debug.Log($"[CSVExperimentRunner] Running recall test for pattern: {patternId} (learning should be OFF, noise should be ON)");

        SetupLogging(config.experimentName, config.randomSeed, "recall", patternId);

        // Verify learning is OFF
        if (learningImprint != null)
        {
            learningImprint.SetLearningEnabled(false);
            Debug.Log("[CSVExperimentRunner] Recall: Learning confirmed OFF");
        }

        // Verify noise targets during recall match config.
        bool wantBallNoise = (config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Ball ||
                              config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Both);
        bool wantMagnetNoise = (config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Magnet ||
                                config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Both);

        if (ball != null)
        {
            if (wantBallNoise)
            {
                if (!ball.addNoise)
                    ball.SetNoiseEnabled(true);
                Debug.Log("[CSVExperimentRunner] Recall: Ball noise confirmed ON");
            }
            else
            {
                if (ball.addNoise)
                    ball.SetNoiseEnabled(false);
                Debug.Log("[CSVExperimentRunner] Recall: Ball noise confirmed OFF (magnet-only recall noise)");
            }
        }

        magnet?.SetRecallNoiseEnabled(wantMagnetNoise);
        Debug.Log($"[CSVExperimentRunner] Recall: Magnet noise confirmed {(wantMagnetNoise ? "ON" : "OFF")}, target={config.recallNoiseTarget}");

        // Note: Magnet force is now controlled by the cue fade system in CollectRecallSamples
        // Only set it here if cue fade system is disabled (legacy behavior)
        if (!config.enableCueFadeSystem)
        {
            if (magnet != null && config.recallMagnetForceMultiplier >= 0f)
                magnet.SetForceStrengthMultiplier(config.recallMagnetForceMultiplier);
            else
                magnet?.SetForceStrengthMultiplier(-1f);
        }

        // Pattern is already loaded and ball is already positioned
        recallTimer = 0f;
        recallTotalSamples = 0;
        recallInRangeSamples = 0;
        recallPercentInRange = 0f;
        recallTestPassed = false;

        yield return StartCoroutine(CollectRecallSamples(patternId, config));

        recallTestRunning = false;
        magnet?.SetMagnetVisualVisible(true);

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
        float originalMagnetForceMultiplier = -1f; // Track original for restoration
        
        // Initialize cue fade system
        if (config.enableCueFadeSystem)
        {
            // Start with full cue force
            magnet.SetForceStrengthMultiplier(1.0f);
            if (config.neverTurnOffMagnetDuringRecall)
                Debug.Log("[CSVExperimentRunner] Cue fade system enabled, but neverTurnOffMagnetDuringRecall=true: keeping cue strength constant (no fade to 0).");
            else
                Debug.Log($"[CSVExperimentRunner] Cue fade system enabled: Full cue until {config.cueOffAtProgress:P0} progress, then fade to 0");
        }
        else
        {
            // Use legacy behavior if fade system is disabled
            if (config.recallMagnetForceMultiplier >= 0f)
                magnet.SetForceStrengthMultiplier(config.recallMagnetForceMultiplier);
        }

        magnet?.SetMagnetVisualVisible(true);
        lastRecallMagnetCueOn = true;
        
        while (magnet != null && !magnet.PatternCompleted)
        {
            elapsed += Time.deltaTime;
            recallTimer += Time.deltaTime;

            // Cue fade: update force; HUD + visual turn "off" as soon as fade *starts* (progress >= cueOffAtProgress),
            // not when strength reaches ~0 (which is only near the end of the pattern).
            if (magnet != null && config.enableCueFadeSystem && !config.neverTurnOffMagnetDuringRecall)
            {
                float progress = magnet.GetWaypointProgress();
                float cueStrength = 1f;
                float offAt = config.cueOffAtProgress;
                if (progress >= 0f)
                {
                    if (progress >= offAt)
                    {
                        float fadeSpan = 1f - offAt;
                        if (fadeSpan > 0.001f)
                        {
                            float fadeProgress = (progress - offAt) / fadeSpan;
                            cueStrength = Mathf.SmoothStep(1f, 0f, fadeProgress);
                        }
                        else
                            cueStrength = 0f;
                    }
                }
                magnet.SetForceStrengthMultiplier(cueStrength);

                bool fadeStarted = progress >= 0f && progress >= offAt;
                lastRecallMagnetCueOn = !fadeStarted;
                magnet.SetMagnetVisualVisible(!fadeStarted);
            }
            else if (magnet != null)
            {
                lastRecallMagnetCueOn = true;
                magnet.SetMagnetVisualVisible(true);
            }

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

        // Restore magnet force after recall test
        if (config.enableCueFadeSystem)
        {
            magnet.SetForceStrengthMultiplier(-1f); // Restore to default
            Debug.Log($"[CSVExperimentRunner] 🔌 Cue fade system: Magnet force restored to default after recall test");
        }
        else if (originalMagnetForceMultiplier >= 0f)
        {
            magnet.SetForceStrengthMultiplier(originalMagnetForceMultiplier);
        }
        else
        {
            magnet.SetForceStrengthMultiplier(-1f); // Restore to default
        }

        magnet?.SetMagnetVisualVisible(true);
        lastRecallMagnetCueOn = true;

        if (recallTotalSamples == 0)
            SampleRecallDistance(config);
    }

    void OnGUI()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        if (!recallTestRunning && recallTotalSamples == 0) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 55;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.UpperRight;
        style.wordWrap = true;

        float margin = 20f;
        float yPos = 80f;
        float width = 980f;
        float height = 480f;

        string patternLine = totalPatternsForGui > 0
            ? $"Pattern {currentRecallPatternNumber}/{totalPatternsForGui}\n"
            : "";
        string magnetLine = $"Magnet: {(lastRecallMagnetCueOn ? "ON" : "OFF")}\n";

        if (recallTestRunning)
        {
            style.normal.textColor = Color.yellow;
            float currentPercent = recallTotalSamples > 0 ? (100f * recallInRangeSamples / recallTotalSamples) : 0f;
            string status = patternLine + magnetLine +
                           $"RECALL: {currentRecallPatternName}\n" +
                           $"Current: {currentPercent:F1}%";
            GUI.Label(new Rect(Screen.width - width - margin, yPos, width, height), status, style);
        }
        else if (recallTotalSamples > 0)
        {
            style.normal.textColor = recallTestPassed ? Color.green : Color.red;
            string result = patternLine + magnetLine +
                           $"RECALL: {currentRecallPatternName}\n" +
                           $"{recallPercentInRange:F1}% - {(recallTestPassed ? "PASS ✅" : "FAIL ❌")}";
            GUI.Label(new Rect(Screen.width - width - margin, yPos, width, height), result, style);
        }
    }

    void SampleRecallDistance(CSVExperimentConfig config)
    {
        if (ball == null) return;

        recallTotalSamples++;
        float dist = GetDistanceToIntendedPath(currentRecallPatternName, ball.transform.position);
        if (dist <= config.recallRadiusThreshold)
            recallInRangeSamples++;
        
        // Diagnostic: Check gradient strength from learned attractors (every 20 samples to avoid spam)
        if (recallTotalSamples % 20 == 0 && learningImprint != null && ball != null && ball.field != null)
        {
            Vector3 ballPos = ball.transform.position;
            Vector3 learnedGradient = learningImprint.GetGradientXZ(ballPos);
            float gradientMagnitude = learnedGradient.magnitude;
            
            // Log if gradient is suspiciously weak (might indicate interference)
            // Only warn if gradient is truly zero or very weak with many wells
            if (gradientMagnitude < 0.01f && learningImprint.GetWellCount() > 50)
            {
                Debug.LogWarning($"[CSVExperimentRunner] ⚠️ Weak learned gradient detected: {gradientMagnitude:F4} (wells: {learningImprint.GetWellCount()}). Possible pattern interference!");
            }
        }
    }

    /// <summary>
    /// Compute distance from current ball position to the intended pattern path
    /// (nearest waypoint). Falls back to cue position if waypoint data is unavailable.
    /// </summary>
    float GetDistanceToIntendedPath(string patternId, Vector3 ballPos)
    {
        if (waypointLoader != null && !string.IsNullOrEmpty(patternId))
        {
            List<Vector3> intended = waypointLoader.GetPattern(patternId);
            if (intended != null && intended.Count > 0)
            {
                float minDist = float.MaxValue;
                for (int i = 0; i < intended.Count; i++)
                {
                    float d = Vector3.Distance(ballPos, intended[i]);
                    if (d < minDist) minDist = d;
                }
                return minDist;
            }
        }

        // Fallback: if intended waypoints are unavailable, use cue distance so sampling still works.
        if (magnet != null)
            return Vector3.Distance(magnet.GetEffectiveCuePosition(), ballPos);

        return float.MaxValue;
    }

    void EnableNoise(CSVExperimentConfig config)
    {
        // Ball noise
        if (ball != null && (config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Ball || config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Both))
        {
            ball.SetNoiseParameters(config.noiseStrength, config.whiteNoise, config.noiseSmoothing);
            ball.SetNoiseEnabled(true);
        }
        else
        {
            ball?.SetNoiseEnabled(false);
        }

        // Magnet noise (cue noise)
        if (magnet != null && (config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Magnet || config.recallNoiseTarget == CSVExperimentConfig.RecallNoiseTarget.Both))
        {
            magnet.SetRecallNoiseParameters(
                config.magnetNoiseStrength,
                config.magnetNoiseWhite,
                config.magnetNoiseSmoothing,
                config.magnetNoiseMeanReversion,
                currentExperimentSeed);
            magnet.SetRecallNoiseEnabled(true);
        }
        else
        {
            magnet?.SetRecallNoiseEnabled(false);
        }

        noiseActive = (config.recallNoiseTarget != CSVExperimentConfig.RecallNoiseTarget.None);
        Debug.Log($"[CSVExperimentRunner] ✓ Recall noise enabled (target={config.recallNoiseTarget}, ballStrength={config.noiseStrength}, magnetStrength={config.magnetNoiseStrength})");
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
            magnet.SetRecallNoiseEnabled(false);
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
            if (!string.IsNullOrWhiteSpace(config.stimulusCsvFileName))
                writer.WriteLine($"Stimulus CSV (override): {config.stimulusCsvFileName}");
            else if (waypointLoader != null)
                writer.WriteLine($"Stimulus CSV (loader default): {waypointLoader.csvFileName}");
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
            if (config.enableCueFadeSystem)
            {
                writer.WriteLine($"Cue Fade System: ENABLED");
                writer.WriteLine($"  - Full cue at start, begins fading at 25% recall");
                writer.WriteLine($"  - Smooth fade from 1.0 → 0.0 over 25%-100% recall");
            }
            else
            {
                writer.WriteLine($"Cue Fade System: DISABLED (using legacy behavior)");
            }
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

        if (patternId.StartsWith("pat_") && patternId.Length > 4)
        {
            string numberPart = patternId.Substring(4);
            if (int.TryParse(numberPart, out int num))
                return $"pat_{num:D2}";
        }

        if (patternId.StartsWith("test_", System.StringComparison.OrdinalIgnoreCase) && patternId.Length > 5)
        {
            string numberPart = patternId.Substring(5);
            if (int.TryParse(numberPart, out int num))
                return $"test_{num:D2}";
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
