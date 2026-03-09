using UnityEngine;
using System.IO;
using System.Text;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PotentialSurface : MonoBehaviour
{
    [Header("Field")]
    public AttractorField field;

    [Header("Surface Geometry")]
    [Range(16, 256)] public int resolution = 120; // grid subdivisions
    public float size = 20f;                      // world width/length (X/Z)
    public float heightScale = 2.5f;              // scales potential to Y

    public bool liveUpdate = true;
    public bool addMeshCollider = true;

    private Mesh mesh;
    private Vector3[] verts;
    private MeshCollider col;

    // NEW: defer any rebuild triggered by inspector changes
    private bool needsRebuild = false;

    void Awake()
    {
        BuildMesh();
        if (addMeshCollider)
        {
            col = GetComponent<MeshCollider>();
            if (!col) col = gameObject.AddComponent<MeshCollider>();
        }
    }

    void Start()
    {
        UpdateHeights();
    }

    void Update()
    {
        // NEW: handle deferred rebuild here (safe context)
        if (needsRebuild)
        {
            BuildMesh();
            if (addMeshCollider)
            {
                if (!col) col = GetComponent<MeshCollider>();
                if (!col) col = gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = null;
                col.sharedMesh = mesh;
            }
            UpdateHeights();
            needsRebuild = false;
        }

        if (liveUpdate) UpdateHeights();
    }

    void OnValidate()
    {
        resolution = Mathf.Clamp(resolution, 16, 256);
        // IMPORTANT: do NOT touch MeshFilter/sharedMesh here.
        // Just request a rebuild for the next safe frame.
        needsRebuild = true;
    }

    void BuildMesh()
    {
        mesh = new Mesh { name = "PotentialSurface" };
        GetComponent<MeshFilter>().sharedMesh = mesh;

        int n = resolution + 1;
        verts = new Vector3[n * n];
        Vector2[] uvs = new Vector2[n * n];
        int[] tris = new int[resolution * resolution * 6];

        float half = size * 0.5f;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float fx = Mathf.Lerp(-half, half, x / (float)resolution);
                float fz = Mathf.Lerp(-half, half, z / (float)resolution);
                verts[i] = new Vector3(fx, 0f, fz);
                uvs[i] = new Vector2(x / (float)resolution, z / (float)resolution);
            }

        int t = 0;
        for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * (resolution + 1) + x;
                tris[t++] = i;
                tris[t++] = i + resolution + 1;
                tris[t++] = i + 1;
                tris[t++] = i + 1;
                tris[t++] = i + resolution + 1;
                tris[t++] = i + resolution + 2;
            }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    public void UpdateHeights()
    {
        if (field == null || mesh == null || verts == null) return;

        int n = resolution + 1;

        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                Vector3 world = transform.TransformPoint(verts[i]);
                float V = field.GetPotentialXZ(new Vector3(world.x, 0f, world.z));
                verts[i].y = V * heightScale; // negative near attractors -> dips
            }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (col)
        {
            col.sharedMesh = null;
            col.sharedMesh = mesh;
        }
    }

    // sample surface height at world XZ (for the state point)
    public float SampleWorldHeight(Vector3 worldXZ)
    {
        Vector3 local = transform.InverseTransformPoint(worldXZ);
        float half = size * 0.5f;
        float u = Mathf.InverseLerp(-half, half, local.x) * resolution;
        float v = Mathf.InverseLerp(-half, half, local.z) * resolution;

        int x = Mathf.Clamp(Mathf.FloorToInt(u), 0, resolution - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(v), 0, resolution - 1);
        int n = resolution + 1;

        int i00 = z * n + x;
        int i10 = i00 + 1;
        int i01 = i00 + n;
        int i11 = i01 + 1;

        float tx = Mathf.Clamp01(u - x);
        float tz = Mathf.Clamp01(v - z);

        float y0 = Mathf.Lerp(verts[i00].y, verts[i10].y, tx);
        float y1 = Mathf.Lerp(verts[i01].y, verts[i11].y, tx);
        float y = Mathf.Lerp(y0, y1, tz);

        return transform.TransformPoint(new Vector3(0f, y, 0f)).y;
    }

    /// <summary>
    /// Exports the potential surface coordinates to a CSV file.
    /// Columns: X, Z, Y (height), Potential (raw potential value before heightScale)
    /// </summary>
    /// <param name="filePath">Full path to the CSV file to create</param>
    /// <returns>True if export succeeded, false otherwise</returns>
    public bool ExportSurfaceToCSV(string filePath)
    {
        if (field == null || mesh == null || verts == null)
        {
            Debug.LogError("[PotentialSurface] Cannot export: field, mesh, or verts is null. Make sure UpdateHeights() has been called.");
            return false;
        }

        try
        {
            // Ensure heights are up to date
            UpdateHeights();

            int n = resolution + 1;
            StringBuilder csv = new StringBuilder();
            
            // Header
            csv.AppendLine("X,Z,Y,Potential");

            // Export all vertices
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    Vector3 world = transform.TransformPoint(verts[i]);
                    
                    // Get raw potential (Y / heightScale to reverse the scaling)
                    float rawPotential = verts[i].y / heightScale;
                    
                    // Write: X, Z, Y (height), Potential (raw)
                    csv.AppendLine($"{world.x:F6},{world.z:F6},{verts[i].y:F6},{rawPotential:F6}");
                }
            }

            // Write to file
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, csv.ToString());
            Debug.Log($"[PotentialSurface] ✓ Exported {n * n} surface points to: {filePath}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PotentialSurface] Failed to export surface to CSV: {e.Message}");
            return false;
        }
    }
}
