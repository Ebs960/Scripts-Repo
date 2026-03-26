using TMPro;
using UnityEngine;

public class ContinentWorldLabel : MonoBehaviour
{
    private TileSystem tileSystem;
    private int centerTileIndex = -1;
    private float heightOffset;
    private Camera mainCamera;

    public void Initialize(TileSystem sourceTileSystem, int tileIndex, float labelHeightOffset)
    {
        tileSystem = sourceTileSystem;
        centerTileIndex = tileIndex;
        heightOffset = labelHeightOffset;
        mainCamera = Camera.main;
        UpdateTransform();
    }

    public void SetText(string label)
    {
        var text = GetComponent<TextMeshPro>();
        if (text != null)
            text.text = label;
    }

    private void LateUpdate()
    {
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        if (tileSystem == null || centerTileIndex < 0)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        transform.position = tileSystem.GetTileSurfacePosition(centerTileIndex, heightOffset);

        if (mainCamera != null)
            transform.rotation = mainCamera.transform.rotation;
    }
}