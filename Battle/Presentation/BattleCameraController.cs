using UnityEngine;
using System.Collections.Generic;

/// <summary>Owns the world-space camera while a manual tactical battle is open.</summary>
public sealed class BattleCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float panSpeed = 24f;
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private float zoomSpeed = 20f;
    [SerializeField] private float minimumHeight = 8f;
    [SerializeField] private float maximumHeight = 80f;
    [SerializeField] private Vector2 padding = new(6f, 6f);

    private Camera tacticalCamera;
    private Camera campaignCamera;
    private AudioListener campaignListener;
    private bool campaignCameraEnabled;
    private bool campaignListenerEnabled;
    private Vector3 campaignPosition;
    private Quaternion campaignRotation;
    private float campaignFieldOfView;
    private float campaignOrthographicSize;
    private bool campaignOrthographic;
    private float campaignNear, campaignFar, campaignDepth;
    private Rect campaignRect;
    private CameraClearFlags campaignClearFlags;
    private Color campaignBackground;
    private int campaignCullingMask;
    private bool campaignHdr, campaignMsaa;
    private RenderTexture campaignTargetTexture;
    private readonly List<AudioListener> suspendedListeners=new();
    private Bounds limits;

    public bool IsActive { get; private set; }
    public Camera TacticalCamera => tacticalCamera;

    public void FocusBattle(BattleMap map)
    {
        if (map == null) return;
        if (!IsActive) CaptureCampaignCamera();
        EnsureTacticalCamera();

        int count = Mathf.Max(1, map.CellCount);
        float width = Mathf.Max(8f, Mathf.Ceil(Mathf.Sqrt(count)) * 2f);
        float depth = Mathf.Max(8f, Mathf.Ceil(count / Mathf.Max(1f, Mathf.Sqrt(count))) * 1.75f);
        float minY = 0f, maxY = 0f;
        for (int i = 0; i < map.CellCount; i++)
        {
            minY = Mathf.Min(minY, map.Cells[i].ElevationLevel);
            maxY = Mathf.Max(maxY, map.Cells[i].ElevationLevel);
        }
        limits = new Bounds(new Vector3(0f, (minY + maxY) * .5f, 0f),
            new Vector3(width + padding.x * 2f, Mathf.Max(4f, maxY - minY + 4f), depth + padding.y * 2f));
        float height = Mathf.Clamp(Mathf.Max(width, depth) * .85f, minimumHeight, maximumHeight);
        tacticalCamera.transform.SetPositionAndRotation(limits.center + new Vector3(0f, height, -height * .55f), Quaternion.Euler(58f, 0f, 0f));
        tacticalCamera.enabled = true;
        IsActive = true;
    }

    public void RouteInput(Vector2 pan, float zoom, float rotation, float unscaledDeltaTime)
    {
        if (!IsActive || tacticalCamera == null) return;
        Transform t = tacticalCamera.transform;
        Vector3 right = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
        t.position += (right * pan.x + forward * pan.y) * panSpeed * unscaledDeltaTime;
        t.RotateAround(limits.center, Vector3.up, rotation * rotationSpeed * unscaledDeltaTime);
        t.position += t.forward * zoom * zoomSpeed * unscaledDeltaTime;
        Vector3 p = t.position;
        p.y = Mathf.Clamp(p.y, minimumHeight, maximumHeight);
        p.x = Mathf.Clamp(p.x, limits.min.x, limits.max.x);
        p.z = Mathf.Clamp(p.z, limits.min.z - maximumHeight, limits.max.z + maximumHeight);
        t.position = p;
    }

    public void NudgeZoom(float direction) => RouteInput(Vector2.zero, direction, 0f, .1f);

    public void RestoreCampaignCamera()
    {
        if (!IsActive) return;
        if (tacticalCamera != null) tacticalCamera.enabled = false;
        var tacticalListener = tacticalCamera != null ? tacticalCamera.GetComponent<AudioListener>() : null;
        if (tacticalListener != null) tacticalListener.enabled = false;
        if (campaignCamera != null)
        {
            campaignCamera.transform.SetPositionAndRotation(campaignPosition, campaignRotation);
            campaignCamera.fieldOfView = campaignFieldOfView;
            campaignCamera.orthographic = campaignOrthographic;
            campaignCamera.orthographicSize = campaignOrthographicSize;
            campaignCamera.enabled = campaignCameraEnabled;
            campaignCamera.nearClipPlane=campaignNear; campaignCamera.farClipPlane=campaignFar; campaignCamera.depth=campaignDepth;
            campaignCamera.rect=campaignRect; campaignCamera.clearFlags=campaignClearFlags; campaignCamera.backgroundColor=campaignBackground;
            campaignCamera.cullingMask=campaignCullingMask; campaignCamera.allowHDR=campaignHdr; campaignCamera.allowMSAA=campaignMsaa;
            campaignCamera.targetTexture=campaignTargetTexture;
        }
        for(int i=0;i<suspendedListeners.Count;i++)if(suspendedListeners[i]!=null)suspendedListeners[i].enabled=true;
        suspendedListeners.Clear();
        IsActive = false;
    }

    private void CaptureCampaignCamera()
    {
        suspendedListeners.Clear();
        var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != null && listeners[i].enabled)
            {
                suspendedListeners.Add(listeners[i]);
                listeners[i].enabled = false;
            }
        }
        campaignCamera = Camera.main;
        if (campaignCamera == tacticalCamera) campaignCamera = null;
        if (campaignCamera == null) return;
        campaignPosition = campaignCamera.transform.position;
        campaignRotation = campaignCamera.transform.rotation;
        campaignFieldOfView = campaignCamera.fieldOfView;
        campaignOrthographic = campaignCamera.orthographic;
        campaignOrthographicSize = campaignCamera.orthographicSize;
        campaignNear=campaignCamera.nearClipPlane; campaignFar=campaignCamera.farClipPlane; campaignDepth=campaignCamera.depth;
        campaignRect=campaignCamera.rect; campaignClearFlags=campaignCamera.clearFlags; campaignBackground=campaignCamera.backgroundColor;
        campaignCullingMask=campaignCamera.cullingMask; campaignHdr=campaignCamera.allowHDR; campaignMsaa=campaignCamera.allowMSAA;
        campaignTargetTexture=campaignCamera.targetTexture;
        campaignCameraEnabled = campaignCamera.enabled;
        campaignListener = campaignCamera.GetComponent<AudioListener>();
        campaignListenerEnabled = campaignListener != null && campaignListener.enabled;
        campaignCamera.enabled = false;
    }

    private void EnsureTacticalCamera()
    {
        if (tacticalCamera != null) return;
        var go = new GameObject("Tactical Battle Camera");
        go.transform.SetParent(transform, false);
        tacticalCamera = go.AddComponent<Camera>();
        tacticalCamera.nearClipPlane = .1f;
        tacticalCamera.farClipPlane = 2000f;
        tacticalCamera.gameObject.AddComponent<AudioListener>().enabled = true;
    }
}
