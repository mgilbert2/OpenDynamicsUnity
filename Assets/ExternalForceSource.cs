using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
public class ExternalForceSource : MonoBehaviour
{
    public enum PathMode { Circle, Lissajous, Waypoints, EyeTracking }

    [Header("Visibility")]
    public Transform visual;   // assign your magnet visual
    [Tooltip("X offset for visual position (manual adjustment).")]
    public float visualX = 0f;
    [Tooltip("Y offset for visual position (height above magnet).")]
    public float visualY = 0.5f;
    [Tooltip("Z offset for visual position (manual adjustment).")]
    public float visualZ = 0f;
    [Tooltip("Optional surface reference for height-aware visual positioning. If null, uses fixed Y offset.")]
    public PotentialSurface surface;

    [Header("Force Profile")]
    public float forceStrength = 50f;
    private float originalForceStrength = 50f;
    public float falloff = 3f;

    [Header("Path Parameters (fallback if no asset assigned)")]
    public PathMode path = PathMode.Circle;
    public Vector2 center = Vector2.zero;
    public float radius = 6f;
    public float angularSpeed = 1.5f;

    [Header("Lissajous Params")]
    public float A = 6f, B = 4f;
    public float aFreq = 1.5f, bFreq = 2.3f;
    public float phase = 1.2f;

    [Header("Waypoint Params")]
    public List<Vector3> waypoints = new List<Vector3>();
    public float waypointSpeed = 6f;
    public bool loop = true;

    [Header("Eye-Tracking Data")]
    [Tooltip("Path to CSV file containing eye-tracking data (format: time,x,z or time,x,y). Can be relative to StreamingAssets or absolute path.")]
    public string eyeTrackingCSVPath = "";
    [Tooltip("Whether the CSV file has a header row.")]
    public bool eyeTrackingHasHeader = true;
    [Tooltip("If true, treats third column as Z; if false, treats it as Y (and uses 0 for Z).")]
    public bool eyeTrackingUseZ = true;
    [Tooltip("Whether to loop the eye-tracking data playback.")]
    public bool eyeTrackingLoop = false;
    [Tooltip("Time scale multiplier for eye-tracking playback (1.0 = normal speed, 2.0 = 2x speed).")]
    public float eyeTrackingTimeScale = 1.0f;
    [Tooltip("Coordinate scale multiplier to adjust eye-tracking data to world space.")]
    public Vector2 eyeTrackingScale = Vector2.one;
    [Tooltip("Offset to apply to eye-tracking positions (world space).")]
    public Vector2 eyeTrackingOffset = Vector2.zero;

    // ------------------------------------------------------------------------
    // ✅ ADDED: Waypoint CSV loader hook
    // ------------------------------------------------------------------------
    [Header("Waypoint CSV (Stimulus Set)")]
    [Tooltip("Assign a WaypointPatternCSVLoader to load waypoint patterns from a CSV stimulus set.")]
    public WaypointPatternCSVLoader waypointLoader;

    private bool patternCompleted = false;
    public bool PatternCompleted => patternCompleted;

    [Header("Path Assets (Hot-Swap List)")]
    [Tooltip("Assign several MagnetPathAssets here and use number keys (1�9) to switch paths during play.")]
    public List<MagnetPathAsset> pathPresets = new List<MagnetPathAsset>();
    private MagnetPathAsset currentPathAsset;

    private int currentWaypoint = 0;
    private float t; // timer for circle/lissajous motion

    // Eye-tracking data
    private EyeTrackingDataReader eyeTrackingReader = new EyeTrackingDataReader();
    private float eyeTrackingStartTime = 0f;
    private bool eyeTrackingInitialized = false;

    void Start()
    {
        originalForceStrength = forceStrength; // Store original value

        // Ensure visual is NOT static so it can move
        if (visual != null)
        {
            GameObject visualGO = visual.gameObject;
            if (visualGO.isStatic)
            {
                visualGO.isStatic = false;
                Debug.Log("[ExternalForceSource] MagnetVisual was static - set to non-static so it can move");
            }
        }

        Vector3 p = transform.position;
        p.y = 0f;
        transform.position = p;
        UpdateVisualPosition();

        // Initialize eye-tracking if enabled (check BEFORE pathPresets to avoid override)
        if (path == PathMode.EyeTracking)
        {
            Debug.Log($"[ExternalForceSource] Path mode is EyeTracking, initializing... CSV Path: '{eyeTrackingCSVPath}'");
            InitializeEyeTracking();
        }
        // Load first path if provided (only if not using eye-tracking)
        else if (pathPresets.Count > 0)
        {
            Debug.Log($"[ExternalForceSource] Path mode is {path}, loading path preset.");
            ApplyPathAsset(pathPresets[0]);
        }
    }

    void Update()
    {
        HandleHotSwapInput();

        Vector3 newPos = transform.position;

        // Debug: Log path mode occasionally to verify it's set correctly
        if (Time.frameCount % 300 == 0) // Every ~5 seconds at 60fps
        {
            Debug.Log($"[ExternalForceSource] Update: path={path}, eyeTrackingInitialized={eyeTrackingInitialized}, CSV Path='{eyeTrackingCSVPath}'");
        }

        switch (path)
        {
            case PathMode.Circle:
                if (!loop && patternCompleted)
                {
                    // Stay at final position if pattern completed and not looping
                    newPos = transform.position;
                }
                else
                {
                    t += Time.deltaTime * angularSpeed;
                    newPos = new Vector3(
                        center.x + radius * Mathf.Cos(t),
                        0f,
                        center.y + radius * Mathf.Sin(t)
                    );

                    // Check if we've completed one full revolution (2π radians)
                    if (!loop && t >= 2f * Mathf.PI)
                    {
                        patternCompleted = true;
                    }
                }
                break;

            case PathMode.Lissajous:
                t += Time.deltaTime;
                newPos = new Vector3(
                    A * Mathf.Sin(aFreq * t + phase),
                    0f,
                    B * Mathf.Sin(bFreq * t)
                );
                break;

            case PathMode.Waypoints:
                if (waypoints.Count > 0)
                    newPos = MoveAlongWaypoints();
                break;

            case PathMode.EyeTracking:
                // If not initialized, try to initialize now (in case path was changed at runtime)
                if (!eyeTrackingInitialized)
                {
                    Debug.LogWarning("[ExternalForceSource] Eye-tracking path mode active but not initialized. Attempting to initialize now...");
                    InitializeEyeTracking();
                }
                newPos = UpdateEyeTrackingPosition();
                break;
        }

        // CRITICAL: Ensure Y is always 0 for force calculations
        newPos.y = 0f;
        transform.position = newPos;

        // Update visual position immediately after setting magnet position to ensure perfect sync
        UpdateVisualPosition();
    }

    void HandleHotSwapInput()
    {
        // Listen for number key presses 1�9
        for (int i = 0; i < pathPresets.Count; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                ApplyPathAsset(pathPresets[i]);
                Debug.Log($"[ExternalForceSource] Switched to path: {pathPresets[i].name}");
                break;
            }
        }
    }

    void ApplyPathAsset(MagnetPathAsset asset)
    {
        if (asset == null) return;
        currentPathAsset = asset;
        t = 0f; // reset timer for clean motion

        switch (asset.pathType)
        {
            case MagnetPathAsset.PathType.Circle:
                path = PathMode.Circle;
                center = asset.center;
                radius = asset.radius;
                angularSpeed = asset.angularSpeed;
                break;

            case MagnetPathAsset.PathType.Lissajous:
                path = PathMode.Lissajous;
                A = asset.A;
                B = asset.B;
                aFreq = asset.aFreq;
                bFreq = asset.bFreq;
                phase = asset.phase;
                break;

            case MagnetPathAsset.PathType.Waypoints:
                path = PathMode.Waypoints;
                waypoints = new List<Vector3>(asset.waypoints);
                waypointSpeed = asset.waypointSpeed;
                loop = asset.loop;
                currentWaypoint = 0;
                break;
        }
    }

    Vector3 MoveAlongWaypoints()
    {
        if (waypoints.Count == 0) return transform.position;

        if (!loop && patternCompleted)
            return transform.position;

        Vector3 current = transform.position;
        current.y = 0f;
        Vector3 target = waypoints[currentWaypoint];
        target.y = 0f;

        // Use MoveTowards for smoother, more reliable movement
        Vector3 newPos = Vector3.MoveTowards(current, target, waypointSpeed * Time.deltaTime);

        // Check if we've reached the current waypoint
        float dist = Vector3.Distance(newPos, target);
        if (dist < 0.01f) // Smaller threshold for more precise arrival
        {
            // Snap to waypoint to avoid jitter
            newPos = target;

            // Advance to next waypoint
            if (currentWaypoint < waypoints.Count - 1)
            {
                currentWaypoint++;
            }
            else if (loop)
            {
                currentWaypoint = 0;
            }
            else
            {
                patternCompleted = true;
            }
        }

        return newPos;
    }

    void InitializeEyeTracking()
    {
        Debug.Log($"[ExternalForceSource] InitializeEyeTracking called. CSV Path: '{eyeTrackingCSVPath}'");

        if (string.IsNullOrEmpty(eyeTrackingCSVPath))
        {
            Debug.LogWarning("[ExternalForceSource] Eye-tracking path is set but no CSV file path provided.");
            eyeTrackingInitialized = false;
            return;
        }

        // Clean up the path - remove "Assets/StreamingAssets/" prefix if user included it
        string cleanPath = eyeTrackingCSVPath;
        if (cleanPath.StartsWith("Assets/StreamingAssets/", System.StringComparison.OrdinalIgnoreCase))
        {
            cleanPath = cleanPath.Substring("Assets/StreamingAssets/".Length);
            Debug.Log($"[ExternalForceSource] Stripped 'Assets/StreamingAssets/' prefix. Using: '{cleanPath}'");
        }
        else if (cleanPath.StartsWith("StreamingAssets/", System.StringComparison.OrdinalIgnoreCase))
        {
            cleanPath = cleanPath.Substring("StreamingAssets/".Length);
            Debug.Log($"[ExternalForceSource] Stripped 'StreamingAssets/' prefix. Using: '{cleanPath}'");
        }

        List<string> attemptedPaths = new List<string>();

        // Try StreamingAssets if path is relative
        if (!Path.IsPathRooted(cleanPath))
        {
            // Try Application.streamingAssetsPath (works in builds)
            string fullPath = Path.Combine(Application.streamingAssetsPath, cleanPath);
            attemptedPaths.Add(fullPath);

            // Also try Assets/StreamingAssets directly (for editor - more reliable)
            string editorPath = Path.Combine(Application.dataPath, "StreamingAssets", cleanPath);
            attemptedPaths.Add(editorPath);
        }
        else
        {
            attemptedPaths.Add(cleanPath);
        }

        // Also try persistent data path as fallback (using cleaned path)
        if (!Path.IsPathRooted(cleanPath))
        {
            string persistentPath = Path.Combine(Application.persistentDataPath, cleanPath);
            attemptedPaths.Add(persistentPath);
        }

        // Also try the original path as-is if it's different from cleanPath
        if (eyeTrackingCSVPath != cleanPath && File.Exists(eyeTrackingCSVPath))
        {
            attemptedPaths.Add(eyeTrackingCSVPath);
        }

        // Try each path until we find the file
        bool loaded = false;
        foreach (string testPath in attemptedPaths)
        {
            if (File.Exists(testPath))
            {
                Debug.Log($"[ExternalForceSource] Found eye-tracking file at: {testPath}");
                Debug.Log($"[ExternalForceSource] Attempting to load CSV (hasHeader={eyeTrackingHasHeader}, useZ={eyeTrackingUseZ})...");

                bool loadResult = eyeTrackingReader.LoadFromCSV(testPath, eyeTrackingHasHeader, eyeTrackingUseZ);
                Debug.Log($"[ExternalForceSource] LoadFromCSV returned: {loadResult}, IsValid: {eyeTrackingReader.IsValid}, SampleCount: {eyeTrackingReader.SampleCount}");

                if (loadResult && eyeTrackingReader.IsValid)
                {
                    eyeTrackingStartTime = Time.time;
                    eyeTrackingInitialized = true;

                    // Set initial position
                    Vector2 startPos = eyeTrackingReader.GetStartPosition();
                    startPos = new Vector2(startPos.x * eyeTrackingScale.x + eyeTrackingOffset.x,
                                           startPos.y * eyeTrackingScale.y + eyeTrackingOffset.y);
                    transform.position = new Vector3(startPos.x, 0f, startPos.y);
                    UpdateVisualPosition();

                    Debug.Log($"[ExternalForceSource] ✓✓✓ Eye-tracking data loaded successfully! Duration: {eyeTrackingReader.Duration:F2}s, Samples: {eyeTrackingReader.SampleCount}");
                    Debug.Log($"[ExternalForceSource] Initial position set to: {transform.position}");
                    loaded = true;
                    break;
                }
                else
                {
                    Debug.LogError($"[ExternalForceSource] File found but failed to load/parse CSV. LoadFromCSV={loadResult}, IsValid={eyeTrackingReader.IsValid}");
                }
            }
        }

        if (!loaded)
        {
            Debug.LogError($"[ExternalForceSource] Failed to load eye-tracking data. Tried paths:");
            foreach (string testPath in attemptedPaths)
            {
                Debug.LogError($"  - {testPath} (exists: {File.Exists(testPath)})");
            }
            Debug.LogError($"[ExternalForceSource] StreamingAssets path: {Application.streamingAssetsPath}");
            Debug.LogError($"[ExternalForceSource] Data path: {Application.dataPath}");
            eyeTrackingInitialized = false;
        }
    }

    Vector3 UpdateEyeTrackingPosition()
    {
        if (!eyeTrackingInitialized || !eyeTrackingReader.IsValid)
        {
            // Only log this warning once per second to avoid spam
            if (!eyeTrackingInitialized && Time.frameCount % 60 == 0)
            {
                Debug.LogWarning($"[ExternalForceSource] Eye-tracking not initialized! Path mode: {path}, CSV Path: '{eyeTrackingCSVPath}'");
                Debug.LogWarning($"[ExternalForceSource] This means InitializeEyeTracking() either wasn't called or failed. Check console for earlier messages.");
            }
            return transform.position;
        }

        // Calculate elapsed time since start
        float elapsedTime = (Time.time - eyeTrackingStartTime) * eyeTrackingTimeScale;
        float dataTime = eyeTrackingReader.StartTime + elapsedTime;

        // Check if we've reached the end
        if (!eyeTrackingLoop && dataTime >= eyeTrackingReader.EndTime)
        {
            patternCompleted = true;
            // Stay at last position
            Vector2 lastPos = eyeTrackingReader.GetPositionAtTime(eyeTrackingReader.EndTime, false);
            lastPos = new Vector2(lastPos.x * eyeTrackingScale.x + eyeTrackingOffset.x,
                                  lastPos.y * eyeTrackingScale.y + eyeTrackingOffset.y);
            return new Vector3(lastPos.x, 0f, lastPos.y);
        }

        // Get interpolated position from eye-tracking data
        Vector2 dataPos = eyeTrackingReader.GetPositionAtTime(dataTime, eyeTrackingLoop);

        // Apply scale and offset
        Vector2 worldPos = new Vector2(
            dataPos.x * eyeTrackingScale.x + eyeTrackingOffset.x,
            dataPos.y * eyeTrackingScale.y + eyeTrackingOffset.y
        );

        return new Vector3(worldPos.x, 0f, worldPos.y);
    }

    public Vector3 GetForce(Vector3 worldPos)
    {
        Vector3 delta = transform.position - worldPos;
        float dist = delta.magnitude + 1e-4f;
        float strength = forceStrength / Mathf.Pow(dist, falloff);
        return delta.normalized * strength;
    }

    // ------------------------------------------------------------------------
    // Programmatic control methods for experiment system
    // ------------------------------------------------------------------------

    /// <summary>
    /// Load eye-tracking data from a CSV file path.
    /// </summary>
    public void LoadEyeTrackingData(string csvPath, bool hasHeader = true, bool useZ = true, bool loop = false, float timeScale = 1.0f, Vector2 scale = default, Vector2 offset = default)
    {
        path = PathMode.EyeTracking;
        eyeTrackingCSVPath = csvPath;
        eyeTrackingHasHeader = hasHeader;
        eyeTrackingUseZ = useZ;
        eyeTrackingLoop = loop;
        eyeTrackingTimeScale = timeScale;
        eyeTrackingScale = scale == default ? Vector2.one : scale;
        eyeTrackingOffset = offset;

        ResetPatternProgress();
        InitializeEyeTracking();
    }

    /// <summary>
    /// ✅ ADDED: Load a waypoint pattern by ID from the CSV stimulus set.
    /// Example patternId: "pat_01"
    /// </summary>
    public void LoadWaypointPatternFromCSV(string patternId, bool loopWaypoints = false, bool snapToStart = true)
    {
        if (waypointLoader == null)
        {
            Debug.LogWarning("[ExternalForceSource] waypointLoader is null. Assign a WaypointPatternCSVLoader in the Inspector.");
            return;
        }

        waypointLoader.Load();
        List<Vector3> pts = waypointLoader.GetPattern(patternId);

        if (pts == null || pts.Count == 0)
        {
            Debug.LogWarning($"[ExternalForceSource] No waypoints found for pattern '{patternId}'.");
            return;
        }

        path = PathMode.Waypoints;
        waypoints = new List<Vector3>(pts);
        loop = loopWaypoints;

        ResetPatternProgress(snapToStart);

        Debug.Log($"[ExternalForceSource] Loaded waypoint CSV pattern '{patternId}' with {waypoints.Count} waypoints");
    }

    /// <summary>
    /// ✅ ADDED: Load the default waypoint pattern from CSV (useful for single-pattern CSVs like time,x,z format).
    /// Automatically detects the pattern ID from the CSV filename or defaultPatternId setting.
    /// </summary>
    public void LoadDefaultWaypointPatternFromCSV(bool loopWaypoints = false, bool snapToStart = true)
    {
        if (waypointLoader == null)
        {
            Debug.LogWarning("[ExternalForceSource] waypointLoader is null. Assign a WaypointPatternCSVLoader in the Inspector.");
            return;
        }

        waypointLoader.Load();
        
        // Try to get the default pattern ID
        string patternId = waypointLoader.GetDefaultPatternId();
        if (string.IsNullOrEmpty(patternId))
        {
            Debug.LogWarning("[ExternalForceSource] Could not determine default pattern ID from CSV. Try using LoadWaypointPatternFromCSV with a specific pattern ID.");
            return;
        }

        List<Vector3> pts = waypointLoader.GetPattern(patternId);

        if (pts == null || pts.Count == 0)
        {
            Debug.LogWarning($"[ExternalForceSource] No waypoints found for default pattern '{patternId}'.");
            return;
        }

        path = PathMode.Waypoints;
        waypoints = new List<Vector3>(pts);
        loop = loopWaypoints;

        ResetPatternProgress(snapToStart);

        Debug.Log($"[ExternalForceSource] Loaded default waypoint CSV pattern '{patternId}' with {waypoints.Count} waypoints");
    }

    public void LoadPattern(PatternAsset pattern, bool loopWaypoints = false)
    {
        if (pattern == null)
        {
            Debug.LogWarning("[ExternalForceSource] LoadPattern called with null pattern");
            return;
        }

        ResetPatternProgress();

        switch (pattern.patternType)
        {
            case PatternAsset.PatternType.Waypoints:
                if (pattern.waypoints == null || pattern.waypoints.Count == 0)
                {
                    Debug.LogWarning($"[ExternalForceSource] Pattern '{pattern.patternName}' has no waypoints");
                    return;
                }

                path = PathMode.Waypoints;
                waypoints = new List<Vector3>(pattern.waypoints);
                loop = loopWaypoints;

                // Set position to first waypoint
                if (waypoints.Count > 0)
                {
                    Vector3 firstWaypoint = waypoints[0];
                    firstWaypoint.y = 0f;
                    transform.position = firstWaypoint;
                    UpdateVisualPosition();
                }

                Debug.Log($"[ExternalForceSource] Loaded waypoint pattern '{pattern.patternName}' with {waypoints.Count} waypoints");
                break;

            case PatternAsset.PatternType.Circle:
                path = PathMode.Circle;
                center = pattern.circleCenter;
                radius = pattern.circleRadius;
                angularSpeed = pattern.circleAngularSpeed;
                loop = loopWaypoints;

                // Set position to starting point on circle
                Vector3 startPos = new Vector3(
                    center.x + radius,
                    0f,
                    center.y
                );
                transform.position = startPos;
                UpdateVisualPosition();

                Debug.Log($"[ExternalForceSource] Loaded circle pattern '{pattern.patternName}' (center: {center}, radius: {radius}, speed: {angularSpeed})");
                break;
        }
    }

    /// <summary>
    /// Get the starting position of the current pattern (first waypoint or circle start point).
    /// </summary>
    public Vector3 GetPatternStartPosition()
    {
        switch (path)
        {
            case PathMode.Waypoints:
                if (waypoints != null && waypoints.Count > 0)
                {
                    Vector3 firstWaypoint = waypoints[0];
                    firstWaypoint.y = 0f;
                    return firstWaypoint;
                }
                break;

            case PathMode.Circle:
                // Starting point on circle (angle = 0)
                return new Vector3(
                    center.x + radius,
                    0f,
                    center.y
                );

            case PathMode.Lissajous:
                // Starting point for Lissajous (t = 0)
                return new Vector3(
                    A * Mathf.Sin(phase),
                    0f,
                    0f
                );

            case PathMode.EyeTracking:
                if (eyeTrackingReader.IsValid)
                {
                    Vector2 startPos = eyeTrackingReader.GetStartPosition();
                    startPos = new Vector2(startPos.x * eyeTrackingScale.x + eyeTrackingOffset.x,
                                           startPos.y * eyeTrackingScale.y + eyeTrackingOffset.y);
                    return new Vector3(startPos.x, 0f, startPos.y);
                }
                break;
        }

        // Fallback to current position
        return transform.position;
    }

    /// <summary>
    /// Reset waypoint index to 0 and optionally move to first waypoint.
    /// </summary>
    public void ResetToStart()
    {
        ResetPatternProgress();
    }

    public void ResetPatternProgress(bool snapToFirstWaypoint = true)
    {
        currentWaypoint = 0;
        t = 0f; // reset timer
        patternCompleted = false;

        // Reset eye-tracking playback
        if (path == PathMode.EyeTracking)
        {
            eyeTrackingStartTime = Time.time;
        }

        if (snapToFirstWaypoint)
        {
            switch (path)
            {
                case PathMode.Waypoints:
                    // Move to first waypoint if available
                    if (waypoints != null && waypoints.Count > 0)
                    {
                        Vector3 firstWaypoint = waypoints[0];
                        firstWaypoint.y = 0f;
                        transform.position = firstWaypoint;
                        UpdateVisualPosition();
                    }
                    break;

                case PathMode.Circle:
                    // Move to starting point on circle (angle = 0)
                    Vector3 startPos = new Vector3(
                        center.x + radius,
                        0f,
                        center.y
                    );
                    transform.position = startPos;
                    UpdateVisualPosition();
                    break;

                case PathMode.EyeTracking:
                    if (eyeTrackingReader.IsValid)
                    {
                        Vector2 startPos2 = eyeTrackingReader.GetStartPosition();
                        startPos2 = new Vector2(startPos2.x * eyeTrackingScale.x + eyeTrackingOffset.x,
                                                 startPos2.y * eyeTrackingScale.y + eyeTrackingOffset.y);
                        transform.position = new Vector3(startPos2.x, 0f, startPos2.y);
                        UpdateVisualPosition();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Update the visual position to follow the surface height (if surface is assigned) or use fixed Y offset.
    /// The visual must be positioned directly above the magnet's transform.position (where force is calculated).
    /// </summary>
    void UpdateVisualPosition()
    {
        if (visual == null) return;

        Vector3 magnetWorldPos = transform.position;

        float targetWorldY;
        if (surface != null)
        {
            targetWorldY = surface.SampleWorldHeight(new Vector3(magnetWorldPos.x, 0f, magnetWorldPos.z)) + visualY;
        }
        else
        {
            targetWorldY = visualY;
        }

        Vector3 targetWorldPos = new Vector3(magnetWorldPos.x + visualX, targetWorldY, magnetWorldPos.z + visualZ);

        Transform originalParent = visual.parent;
        bool wasChild = (originalParent == transform);

        if (wasChild)
        {
            visual.SetParent(null);
            visual.position = targetWorldPos;
            visual.SetParent(originalParent);
            visual.localPosition = transform.InverseTransformPoint(targetWorldPos);
        }
        else
        {
            visual.position = targetWorldPos;
        }
    }

    public void SetForceStrengthMultiplier(float multiplier)
    {
        if (multiplier < 0f)
        {
            forceStrength = originalForceStrength;
        }
        else
        {
            forceStrength = originalForceStrength * multiplier;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Vector3 forcePos = transform.position;
        Gizmos.DrawSphere(forcePos, 0.3f);

        Gizmos.color = Color.yellow;

        switch (path)
        {
            case PathMode.Circle:
                DrawCircleGizmo(center, radius);
                break;
            case PathMode.Lissajous:
                DrawLissajousGizmo();
                break;
            case PathMode.Waypoints:
                for (int i = 0; i < waypoints.Count; i++)
                {
                    Vector3 wp = waypoints[i];
                    Gizmos.DrawSphere(wp, 0.2f);
                    if (i < waypoints.Count - 1)
                        Gizmos.DrawLine(wp, waypoints[i + 1]);
                    else if (loop && waypoints.Count > 1)
                        Gizmos.DrawLine(wp, waypoints[0]);
                }
                break;
            case PathMode.EyeTracking:
                if (eyeTrackingReader.IsValid && eyeTrackingReader.SampleCount > 0)
                {
                    Gizmos.color = Color.magenta;
                    Vector2 prevPos = eyeTrackingReader.GetStartPosition();
                    prevPos = new Vector2(prevPos.x * eyeTrackingScale.x + eyeTrackingOffset.x,
                                          prevPos.y * eyeTrackingScale.y + eyeTrackingOffset.y);
                    Vector3 prevWorld = new Vector3(prevPos.x, 0f, prevPos.y);

                    float duration = eyeTrackingReader.Duration;
                    int segments = Mathf.Min(100, eyeTrackingReader.SampleCount);
                    for (int i = 1; i <= segments; i++)
                    {
                        float tt = (float)i / segments;
                        float dataTime = eyeTrackingReader.StartTime + tt * duration;
                        Vector2 pos = eyeTrackingReader.GetPositionAtTime(dataTime, false);
                        pos = new Vector2(pos.x * eyeTrackingScale.x + eyeTrackingOffset.x,
                                         pos.y * eyeTrackingScale.y + eyeTrackingOffset.y);
                        Vector3 world = new Vector3(pos.x, 0f, pos.y);
                        Gizmos.DrawLine(prevWorld, world);
                        prevWorld = world;
                    }
                }
                break;
        }
    }

    void DrawCircleGizmo(Vector2 center, float radius)
    {
        const int segments = 40;
        Vector3 prev = new Vector3(center.x + radius, 0f, center.y);
        for (int i = 1; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = new Vector3(center.x + radius * Mathf.Cos(angle), 0f, center.y + radius * Mathf.Sin(angle));
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    void DrawLissajousGizmo()
    {
        const int segments = 100;
        Vector3 prev = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float tt = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = new Vector3(
                A * Mathf.Sin(aFreq * tt + phase),
                0f,
                B * Mathf.Sin(bFreq * tt)
            );
            if (i > 0) Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
