using UnityEngine;

[DisallowMultipleComponent]
public class AttractorPathAssetDriver : MonoBehaviour
{
    public PathAsset pathAsset;

    // runtime for waypoint traversal
    private int _wpIndex;

    private void Start()
    {
        // keep on XZ plane
        var p = transform.position;
        p.y = 0f;
        transform.position = p;
    }

    private void Update()
    {
        if (pathAsset == null) return;

        Vector3 pos = transform.position;
        pos.y = 0f;

        switch (pathAsset.pathType)
        {
            case PathType.Stationary:
                // do nothing
                break;

            case PathType.Circle:
                {
                    float t = Time.time;
                    float x = pathAsset.circleCenter.x +
                              pathAsset.circleRadius * Mathf.Cos((t + pathAsset.circlePhase) * pathAsset.circleSpeed);
                    float z = pathAsset.circleCenter.y +
                              pathAsset.circleRadius * Mathf.Sin((t + pathAsset.circlePhase) * pathAsset.circleSpeed);
                    pos = new Vector3(x, 0f, z);
                    break;
                }

            case PathType.Lissajous:
                {
                    float t = Time.time + pathAsset.lissPhase;
                    float x = pathAsset.A * Mathf.Sin(pathAsset.aFreq * t);
                    float z = pathAsset.B * Mathf.Sin(pathAsset.bFreq * t);
                    pos = new Vector3(x, 0f, z);
                    break;
                }

            case PathType.Waypoints:
                {
                    var wps = pathAsset.waypoints;
                    if (wps != null && wps.Count > 0)
                    {
                        Vector3 target = wps[_wpIndex];
                        target.y = 0f;
                        pos = Vector3.MoveTowards(pos, target, pathAsset.waypointSpeed * Time.deltaTime);

                        if ((pos - target).sqrMagnitude < 0.0004f)
                        {
                            _wpIndex++;
                            if (_wpIndex >= wps.Count)
                                _wpIndex = pathAsset.loop ? 0 : wps.Count - 1;
                        }
                    }
                    break;
                }
        }

        transform.position = pos;
    }

#if UNITY_EDITOR
    // Draw path gizmos in Scene view for convenience
    private void OnDrawGizmosSelected()
    {
        if (pathAsset == null) return;

        Gizmos.color = Color.yellow;

        if (pathAsset.pathType == PathType.Circle)
        {
            // approximate circle
            const int seg = 64;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 p = new Vector3(
                    pathAsset.circleCenter.x + Mathf.Cos(a) * pathAsset.circleRadius,
                    0f,
                    pathAsset.circleCenter.y + Mathf.Sin(a) * pathAsset.circleRadius
                );
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
        else if (pathAsset.pathType == PathType.Waypoints && pathAsset.waypoints != null)
        {
            for (int i = 0; i < pathAsset.waypoints.Count; i++)
            {
                Vector3 p = pathAsset.waypoints[i];
                p.y = 0f;
                Gizmos.DrawSphere(p, 0.1f);
                if (i + 1 < pathAsset.waypoints.Count)
                    Gizmos.DrawLine(p, new Vector3(pathAsset.waypoints[i + 1].x, 0f, pathAsset.waypoints[i + 1].z));
                else if (pathAsset.loop && pathAsset.waypoints.Count > 1)
                    Gizmos.DrawLine(p, new Vector3(pathAsset.waypoints[0].x, 0f, pathAsset.waypoints[0].z));
            }
        }
    }
#endif
}
