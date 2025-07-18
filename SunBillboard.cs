
using UnityEngine;
[ExecuteAlways]
public class SunBillboard : MonoBehaviour
{
    public Light sun;          // assign your directional light
    public Camera targetCam;   // MainCamera
    public float radiusFactor = 1.05f; // how far from centre ( >1 means just outside surface )
    public float sizeDeg = 3f;         // apparent size

    void LateUpdate()
    {
        if (!sun || !targetCam) return;

        // Place billboard on a sphere around origin pointing toward sun dir
        Vector3 dir = -sun.transform.forward.normalized;
        float planetRadius = 1f;                       // planet model has radius 1 (scaled later)
        transform.position = dir * planetRadius * radiusFactor;
        transform.rotation = Quaternion.LookRotation(-targetCam.transform.forward, dir); // face camera

        // scale quad so it covers `sizeDeg` in view space
        float dist = (transform.position - targetCam.transform.position).magnitude;
        float halfSize = Mathf.Tan(sizeDeg * Mathf.Deg2Rad * 0.5f) * dist;
        transform.localScale = Vector3.one * halfSize * 2f;
    }
}
