// Assets/Scripts/Managers/ResourceInstance.cs
using UnityEngine;

/// <summary>
/// Attached to each spawned resource node, to track its type and tile index.
/// </summary>
public class ResourceInstance : MonoBehaviour
{
    [HideInInspector] public ResourceData data;
    [HideInInspector] public int tileIndex;
}