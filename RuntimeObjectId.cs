using UnityEngine;

/// <summary>
/// Provides an int-sized runtime identifier derived from Unity's EntityId API.
/// </summary>
public static class RuntimeObjectId
{
    public static int GetRuntimeId(this UnityEngine.Object obj)
    {
        return obj != null ? obj.GetEntityId().GetHashCode() : 0;
    }
}
