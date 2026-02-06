using UnityEngine;

// MonoBehaviour wrapper around the plain HexGrid utility so it can be placed in the scene
[AddComponentMenu("DeepSeek/HexGridComponent")]
public class HexGridComponent : MonoBehaviour
{
    public HexGrid grid = new HexGrid();

    [Header("Grid Settings (use Generate to rebuild)")]
    public int tilesX = 128;
    public int tilesZ = 64;
    public float mapWidth = 1024f;
    public float mapHeight = 512f;

    [ContextMenu("Generate Grid")]
    public void Generate()
    {
        grid.GenerateFlatGrid(tilesX, tilesZ, mapWidth, mapHeight);
        Debug.Log("HexGridComponent: Generated grid: " + grid.TileCount + " tiles");
    }

    public void Generate(int tilesX, int tilesZ, float mapWidth, float mapHeight)
    {
        this.tilesX = tilesX;
        this.tilesZ = tilesZ;
        this.mapWidth = mapWidth;
        this.mapHeight = mapHeight;
        Generate();
    }

    public int GetTileAtPosition(Vector3 worldPos)
    {
        if (grid == null) return -1;
        return grid.GetTileAtPosition(worldPos);
    }
}
