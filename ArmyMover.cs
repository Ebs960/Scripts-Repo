using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Handles army movement on the campaign map
/// </summary>
public class ArmyMover : MonoBehaviour
{
    private Army army;
    private bool isMoving = false;
    private Coroutine movementCoroutine;
    
    void Awake()
    {
        army = GetComponent<Army>();
    }
    
    /// <summary>
    /// Move army along a path to target tile (checks and deducts movement points)
    /// </summary>
    public void MoveToTile(int targetTile, List<int> path, float moveSpeed)
    {
        if (isMoving)
        {
            StopCoroutine(movementCoroutine);
        }
        
        movementCoroutine = StartCoroutine(MoveAlongPath(path, moveSpeed));
    }
    
    private IEnumerator MoveAlongPath(List<int> path, float moveSpeed)
    {
        if (path == null || path.Count < 2 || army == null)
        {
            isMoving = false;
            yield break;
        }
        
        isMoving = true;
        
        for (int i = 1; i < path.Count; i++)
        {
            int targetTile = path[i];
            
            // Check if army can move to this tile (movement points check)
            if (!army.CanMoveTo(targetTile))
            {
                Debug.Log($"[ArmyMover] {army.armyName} out of movement points. Stopped at tile {army.currentTileIndex}");
                isMoving = false;
                yield break; // Stop movement if out of points
            }
            
            // Get movement cost for this tile
            int movementCost = 1; // Default
            if (TileSystem.Instance != null)
            {
                var tileData = TileSystem.Instance.GetTileData(targetTile);
                if (tileData != null)
                {
                    movementCost = BiomeHelper.GetMovementCost(tileData.biome);
                }
            }
            
            // Deduct movement points BEFORE moving
            army.DeductMovementPoints(movementCost);
            
            // Get world position for target tile
            Vector3 targetPos = Vector3.zero;
            if (TileSystem.Instance != null)
            {
                targetPos = TileSystem.Instance.GetTileCenter(targetTile);
            }
            
            // Move towards target position
            float distance = Vector3.Distance(transform.position, targetPos);
            float moveTime = distance / moveSpeed;
            float elapsed = 0f;
            
            Vector3 startPos = transform.position;
            
            while (elapsed < moveTime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveTime);
                transform.position = Vector3.Lerp(startPos, targetPos, t);
                yield return null;
            }
            
            // Update army tile index
            army.currentTileIndex = targetTile;
            transform.position = targetPos;
            
            // Small delay between tiles
            yield return new WaitForSeconds(0.1f);
        }
        
        isMoving = false;
    }
    
    void OnDestroy()
    {
        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
        }
    }
}

