using UnityEngine;

/// <summary>
/// Lightweight service locator to hold references to core runtime services.
/// This is a pragmatic, low-risk step toward real DI: components can read these
/// references instead of FindObject calls.
/// </summary>
public static class ServiceLocator
{
    public static GameManager GameManager;
    public static UIManager UIManager;
}
