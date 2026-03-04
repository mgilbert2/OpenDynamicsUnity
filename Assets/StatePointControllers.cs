using System.IO;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class StatePointController : MonoBehaviour
{
    [Header("References")]
    public PotentialSurface surface;     // height projection
    public AttractorField field;         // ∇V from attractors
    public ExternalForceSource driver;   // external magnet or force source

    [Header("Dynamics")]
    [Tooltip("Weight on the potential landscape gradient.")]
    public float landscapeGain = 10f;
    [Tooltip("Weight on the external magnet force.")]
    public float externalGain = 1.8f;
    [Tooltip("Friction coefficient (velocity damping).")]
    public float damping = 4f;
    [Tooltip("Maximum allowed speed (world units/s).")]
    public float maxSpeed = 12f;

    [Header("Noise")]
    public bool addNoise = false;
    public float noiseStrength = 2.0f;
    public bool whiteNoise = true;
    public float noiseSmoothing = 2.0f;
    private Vector3 smoothNoise;

    [Header("Visuals")]
    public bool colorBallRed = true;

    [Header("Trail (Path Drawing)")]
    public bool drawTrail = true;
    public float trailTime = 6f;
    public float trailWidth = 0.05f;
    public Material trailMaterial;

    [Header("CSV Logging")]
    public bool logToCSV = true;
    public string fileName = "trajectory.csv";
    public float logInterval = 0.05f;

    [Header("Manual Control")]
    public bool allowDragging = false;

    // Internal state
    private Vector3 vel;
    private TrailRenderer trail;
    private string csvPath;
    private float logTimer = 0f;

    // ------------------------------------------------------------------------
    void Start()
    {
        // --- Set ball color ---
        if (colorBallRed)
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                var instanced = new Material(renderer.material);
                instanced.color = Color.red;
                renderer.material = instanced;
            }
        }

        // --- Setup trail ---
        if (drawTrail)
        {
            trail = GetComponent<TrailRenderer>();
            if (!trail) trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = trailTime;
            trail.startWidth = trailWidth;
            trail.endWidth = trailWidth;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (trailMaterial) trail.material = trailMaterial;
        }
        else
        {
            trail = GetComponent<TrailRenderer>();
            if (trail) trail.enabled = false;
        }

        // --- Setup CSV logging (only if path not already set externally) ---
        if (logToCSV && string.IsNullOrEmpty(csvPath))
        {
            csvPath = Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                File.WriteAllText(csvPath, "time,x,y,z,vx,vy,vz\n");
                Debug.Log($"[StatePointController] Logging to: {csvPath}");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[StatePointController] Could not open CSV for writing: {e.Message}");
                logToCSV = false;
            }
        }

        // --- Align vertically to surface only (no magnet logic) ---
        SnapToSurface();
    }

    // ------------------------------------------------------------------------
    void Update()
    {
        // Manual dragging
        if (allowDragging && HandleDragging())
        {
            LogTick();
            return;
        }

        bool canSim = (surface != null && field != null && driver != null);
        if (canSim)
        {
            Vector3 pos = transform.position;

            // --- Compute forces ---
            Vector3 grad = field.GetGradientXZ(pos);
            Vector3 ext = driver.GetForce(pos);
            Vector3 accel = landscapeGain * grad + externalGain * ext;

            // --- Optional noise ---
            if (addNoise)
            {
                if (whiteNoise)
                {
                    accel += new Vector3(
                        Random.Range(-1f, 1f),
                        0f,
                        Random.Range(-1f, 1f)
                    ) * noiseStrength;
                }
                else
                {
                    Vector3 target = new Vector3(
                        Random.Range(-1f, 1f),
                        0f,
                        Random.Range(-1f, 1f)
                    ) * noiseStrength;
                    smoothNoise = Vector3.Lerp(smoothNoise, target, Time.deltaTime * noiseSmoothing);
                    accel += smoothNoise;
                }
            }

            // --- Integrate motion ---
            vel += accel * Time.deltaTime;
            vel -= vel * damping * Time.deltaTime;
            if (vel.magnitude > maxSpeed)
                vel = vel.normalized * maxSpeed;

            // --- Move and project onto surface ---
            Vector3 nextXZ = pos + new Vector3(vel.x, 0f, vel.z) * Time.deltaTime;
            float y = surface.SampleWorldHeight(new Vector3(nextXZ.x, 0f, nextXZ.z));
            float r = GetComponent<SphereCollider>().radius;
            transform.position = new Vector3(nextXZ.x, y + r, nextXZ.z);
        }

        LogTick();
    }

    // ------------------------------------------------------------------------
    public void SnapToSurface()
    {
        if (surface == null) return;
        float y = surface.SampleWorldHeight(new Vector3(transform.position.x, 0f, transform.position.z));
        float r = GetComponent<SphereCollider>().radius;
        transform.position = new Vector3(transform.position.x, y + r, transform.position.z);
        vel = Vector3.zero;
    }

    private bool HandleDragging()
    {
        if (!Input.GetMouseButton(0) || Camera.main == null || surface == null)
            return false;

        Vector3 m = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        float y = surface.SampleWorldHeight(new Vector3(m.x, 0f, m.z));
        float r = GetComponent<SphereCollider>().radius;
        transform.position = new Vector3(m.x, y + r, m.z);
        vel = Vector3.zero;
        return true;
    }

    // ------------------------------------------------------------------------
    private void LogTick()
    {
        if (!logToCSV) return;
        logTimer += Time.deltaTime;
        if (logTimer >= logInterval)
        {
            logTimer = 0f;
            AppendCSV(Time.time, transform.position, vel);
        }
    }

    private void AppendCSV(float t, Vector3 p, Vector3 v)
    {
        if (!logToCSV || string.IsNullOrEmpty(csvPath)) return;
        try
        {
            File.AppendAllText(csvPath,
                $"{t:F4},{p.x:F6},{p.y:F6},{p.z:F6},{v.x:F6},0,{v.z:F6}\n");
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[StatePointController] CSV write failed: {e.Message}");
            logToCSV = false;
        }
    }

    // ------------------------------------------------------------------------
    // Programmatic control methods for experiment system
    // ------------------------------------------------------------------------

    /// <summary>
    /// Set the CSV log file path programmatically. Creates a new file with header.
    /// </summary>
    public void SetLogPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[StatePointController] SetLogPath called with empty path");
            return;
        }

        csvPath = path;
        try
        {
            // Create directory if it doesn't exist
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(csvPath, "time,x,y,z,vx,vy,vz\n");
            Debug.Log($"[StatePointController] Logging to: {csvPath}");
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[StatePointController] Could not open CSV for writing: {e.Message}");
            logToCSV = false;
        }
    }

    /// <summary>
    /// Reset ball position and velocity. Position is snapped to surface.
    /// </summary>
    public void ResetState(Vector3 position)
    {
        transform.position = position;
        vel = Vector3.zero;
        SnapToSurface();
    }

    /// <summary>
    /// Enable or disable CSV logging programmatically.
    /// </summary>
    public void EnableLogging(bool enable)
    {
        logToCSV = enable;
    }

    /// <summary>
    /// Enable or disable noise programmatically.
    /// </summary>
    public void SetNoiseEnabled(bool enable)
    {
        addNoise = enable;
    }

    /// <summary>
    /// Configure noise parameters programmatically.
    /// </summary>
    public void SetNoiseParameters(float strength, bool white, float smoothing = 2.0f)
    {
        noiseStrength = strength;
        whiteNoise = white;
        noiseSmoothing = smoothing;
    }
}
