using UnityEngine;
using UnityEngine.UI;

public class MenuPlanetPreviewRenderTexture : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField] private RawImage targetRawImage;
    [SerializeField] private int textureWidth = 1024;
    [SerializeField] private int textureHeight = 1024;
    [SerializeField] private int antiAliasing = 4;
    [SerializeField] private bool useHDR = true;

    private RenderTexture previewTexture;

    private void Awake()
    {
        SetupRenderTexture();
    }

    private void SetupRenderTexture()
    {
        if (previewCamera == null || targetRawImage == null) return;

        if (previewTexture == null)
        {
            var format = useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32;
            previewTexture = new RenderTexture(textureWidth, textureHeight, 24, format)
            {
                name = "MenuPlanetPreview_RT",
                antiAliasing = Mathf.Max(1, antiAliasing)
            };
            previewTexture.Create();
        }

        previewCamera.targetTexture = previewTexture;
        targetRawImage.texture = previewTexture;
    }

    private void OnDestroy()
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
}
