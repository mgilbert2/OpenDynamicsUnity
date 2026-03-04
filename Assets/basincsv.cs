using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class AttractorCSVExporter : MonoBehaviour
{
    public AttractorField field;

    [Header("Output")]
    public string fileName = "attractors.csv";
    public bool logTimeSeries = true;     // true = write (id,time,x,z) every interval; false = single snapshot on Start
    public float logInterval = 0.1f;      // seconds (used if logTimeSeries = true)

    [Header("Extras")]
    public bool includeDepthWidth = false; // add depth,width columns

    private string csvPath;
    private float timer;

    void Start()
    {
        if (field == null) field = GetComponent<AttractorField>();

        csvPath = Path.Combine(Application.persistentDataPath, fileName);

        if (logTimeSeries)
        {
            // time-series header
            var sb = new StringBuilder();
            sb.Append("id,time,x,z");
            if (includeDepthWidth) sb.Append(",depth,width");
            sb.AppendLine();
            File.WriteAllText(csvPath, sb.ToString());
        }
        else
        {
            // single snapshot
            WriteSnapshot();
            Debug.Log($"[AttractorCSVExporter] Snapshot written to: {csvPath}");
        }

        Debug.Log($"[AttractorCSVExporter] Logging to: {csvPath}");
    }

    void Update()
    {
        if (!logTimeSeries) return;
        timer += Time.deltaTime;
        if (timer >= logInterval)
        {
            timer = 0f;
            AppendRow(Time.time);
        }
    }

    void WriteSnapshot()
    {
        var sb = new StringBuilder();
        if (logTimeSeries)
        {
            // handled in Start()
            return;
        }
        else
        {
            // static positions: columns x,z (+ opt depth,width)
            if (includeDepthWidth) sb.AppendLine("x,z,depth,width");
            else sb.AppendLine("x,z");

            if (field != null && field.attractors != null)
            {
                foreach (var a in field.attractors)
                {
                    if (!a) continue;
                    var p = a.transform.position;
                    if (includeDepthWidth)
                        sb.AppendLine($"{p.x:F6},{p.z:F6},{a.depth:F6},{a.width:F6}");
                    else
                        sb.AppendLine($"{p.x:F6},{p.z:F6}");
                }
            }
            File.WriteAllText(csvPath, sb.ToString());
        }
    }

    void AppendRow(float t)
    {
        if (field == null || field.attractors == null) return;

        var sb = new StringBuilder();
        foreach (var a in field.attractors)
        {
            if (!a) continue;
            var p = a.transform.position;
            // use the GameObject name as id
            sb.Append($"{a.name},{t:F4},{p.x:F6},{p.z:F6}");
            if (includeDepthWidth) sb.Append($",{a.depth:F6},{a.width:F6}");
            sb.AppendLine();
        }
        try
        {
            File.AppendAllText(csvPath, sb.ToString());
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[AttractorCSVExporter] CSV write failed: {e.Message}");
            enabled = false;
        }
    }

    // Optional right-click menu to write a snapshot in Edit mode
    [ContextMenu("Write Snapshot Now (Edit Mode)")]
    void EditorWriteSnapshot()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(csvPath))
            csvPath = Path.Combine(Application.persistentDataPath, fileName);
        WriteSnapshot();
        Debug.Log($"[AttractorCSVExporter] Snapshot written to: {csvPath}");
#endif
    }
}
