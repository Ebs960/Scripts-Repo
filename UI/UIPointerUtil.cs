using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Unified utility for retrieving current pointer screen position across mouse/touch/pen.
/// Keeps pointer access in one place so Input System migration is consistent across UI code.
/// </summary>
public static class UIPointerUtil
{
    public static bool TryGetScreenPosition(out Vector2 position)
    {
        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }

        if (Pointer.current != null)
        {
            position = Pointer.current.position.ReadValue();
            return true;
        }

        position = default;
        return false;
    }

    public static Vector2 GetScreenPositionOrCenter()
    {
        return TryGetScreenPosition(out var pos)
            ? pos
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }
}
