using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class MagnetCSVExporter : MonoBehaviour
{
    public string fileName = "magnet.csv";
    public float logInterval = 0.05f;

    private string csvPath;
    private float timer;

    void Start()
    {
        csvPath = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllText(csvPath, "time,x,z\n");
        Debug.Log($"[MagnetCSVExporter] Logging to: {csvPath}");
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= logInterval)
        {
            timer = 0f;
            var p = transform.position;
            try
            {
                File.AppendAllText(csvPath, $"{Time.time:F4},{p.x:F6},{p.z:F6}\n");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[MagnetCSVExporter] CSV write failed: {e.Message}");
                enabled = false;
            }
        }
    }
}
