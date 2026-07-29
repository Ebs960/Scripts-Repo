using System;
using UnityEngine;

public sealed class GameInteractionStateService : MonoBehaviour
{
    public static GameInteractionStateService Instance { get; private set; }

    [SerializeField]
    private GameInteractionMode mode = GameInteractionMode.Campaign;

    public GameInteractionMode Mode => mode;
    public bool IsCampaignInteractive => mode == GameInteractionMode.Campaign;

    public event Action<GameInteractionMode> OnInteractionModeChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetMode(GameInteractionMode newMode)
    {
        if (mode == newMode)
            return;

        mode = newMode;
        OnInteractionModeChanged?.Invoke(mode);
    }

    public static GameInteractionStateService GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        var existing = FindAnyObjectByType<GameInteractionStateService>();
        if (existing != null)
            return existing;

        var go = new GameObject("GameInteractionStateService");
        return go.AddComponent<GameInteractionStateService>();
    }
}
