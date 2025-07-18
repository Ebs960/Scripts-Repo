
using UnityEngine;
[ExecuteAlways]
public class SunBillboard : MonoBehaviour
{
    // runtime assigned references
    public Light   sun;         // directional light in the scene
    public Camera  targetCam;   // camera to face

    // distance from the planet centre the flare should sit at
    public float   baseRadius = 25f;
    [Range(1.02f, 1.2f)]
    public float   radiusFactor = 1.06f; // 1 = planet surface

    void LateUpdate()
    {
        if (!sun || !targetCam) return;

        // direction from planet centre to sun (world space)
        Vector3 dir = -sun.transform.forward;
        transform.position = dir * baseRadius * radiusFactor;

        // always face camera
        transform.rotation = Quaternion.LookRotation(transform.position - targetCam.transform.position);

        // optional: scale fades with distance so flare size appears constant
        float dist = Vector3.Distance(transform.position, targetCam.transform.position);
        float size = dist * 0.05f;   // tune 0.05-0.1
        transform.localScale = Vector3.one * size;
    }
}
