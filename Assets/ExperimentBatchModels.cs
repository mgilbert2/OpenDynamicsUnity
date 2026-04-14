using System;
using UnityEngine;

/// <summary>
/// JSON batch file written by R (or by hand) for CSVExperimentRunner.
/// Top-level shape: { "entries": [ { ... }, ... ] }
/// Place under StreamingAssets, e.g. experiment_batch.json
/// Default field values match CSVExperimentRunner.CSVExperimentConfig (seed 1702 / StimB scene baseline).
/// </summary>
[Serializable]
public class ExperimentBatchWrapper
{
    public ExperimentBatchEntry[] entries;
}

/// <summary>
/// Mirrors CSVExperimentRunner.CSVExperimentConfig fields for JsonUtility deserialization.
/// recallNoiseTarget: 0=Ball, 1=Magnet, 2=Both, 3=None
/// </summary>
[Serializable]
public class ExperimentBatchEntry
{
    public bool enabled = true;
    public string experimentName = "CSV_Experiment1";
    public int randomSeed = 1702;

    public string stimulusCsvFileName = "";
    public string[] patternIdsToRun = new string[]
    {
        "test_01", "test_02", "test_03", "test_04", "test_05", "test_06", "test_07"
    };

    public bool runRecallAfterEachPattern = false;
    public bool cumulativeRecallMode = true;
    public bool randomizeRecallOrder = false;

    public float resetStabilizationTime = 0f;
    public float noiseDelayAfterTraining = 0f;

    public int recallNoiseTarget = 1;
    public float noiseStrength = 100f;
    public bool whiteNoise = false;
    public float noiseSmoothing = 0f;
    public float magnetNoiseStrength = 100f;
    public bool magnetNoiseWhite = false;
    public float magnetNoiseSmoothing = 0.04f;
    public float magnetNoiseMeanReversion = 2.57f;

    public bool enableCueFadeSystem = true;
    public bool neverTurnOffMagnetDuringRecall = false;
    public float cueOffAtProgress = 0.05f;

    public float recallMagnetForceMultiplier = 1f;
    public float recallRadiusThreshold = 1.5f;
    public float recallRequiredPercent = 90.5f;
    public float recallSampleInterval = 0.5f;

    public int trainingPassesPerPattern = 1;
    public float delayBetweenTrainingPasses = 0f;

    public float maxWellDepth = 1f;
    public bool normalizeDepth = false;
    public float normalizedDepthTarget = 1f;
    public float hypoWidth = 0.2f;
    public float wellMergeDistance = 0.343f;

    public float ballDamping = 2.55f;
    public float ballMaxSpeed = 6.3f;
    public float ballVelocityMultiplier = 0.62f;
    public float landscapeGain = 4.05f;
    public float externalGain = 57.4f;
}
