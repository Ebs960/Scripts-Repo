using UnityEngine;
using UnityEngine.UI;

public class MenuPlanetPreviewRenderTexture : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private int textureWidth = 2048;
    [SerializeField] private int textureHeight = 2048;
    [SerializeField] private int antiAliasing = 4;
    [SerializeField] private bool useHDR = true;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
    [SerializeField] private bool useMipMap = false;
    [SerializeField] private Color cameraBackgroundColor = new Color(0.005f, 0.008f, 0.018f, 1f);
    [SerializeField] private CameraClearFlags clearFlags = CameraClearFlags.SolidColor;

    private RenderTexture previewTexture;

    private void OnEnable() => RebuildRenderTexture();

    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        RebuildRenderTexture();
    }

    public void RebuildRenderTexture()
    {
        ReleaseTexture();
        if (previewCamera == null || targetRawImage == null) return;

        var format = useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32;
        previewTexture = new RenderTexture(textureWidth, textureHeight, 24, format)
        {
            name = "MenuPlanetPreview_RT",
            antiAliasing = Mathf.Clamp(antiAliasing, 1, 8),
            filterMode = filterMode,
            useMipMap = useMipMap,
            autoGenerateMips = useMipMap
        };
        previewTexture.Create();

        previewCamera.clearFlags = clearFlags;
        previewCamera.backgroundColor = cameraBackgroundColor;
        previewCamera.targetTexture = previewTexture;
        targetRawImage.texture = previewTexture;
    }

    private void ReleaseTexture()
    {
        if (previewCamera != null && previewCamera.targetTexture == previewTexture)
            previewCamera.targetTexture = null;

        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
            previewTexture = null;
        }
    }

    private void OnDestroy() => ReleaseTexture();
}
