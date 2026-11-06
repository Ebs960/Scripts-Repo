# First-Draft Options Menu

The main menu and the in-game pause menu now open the same code-built Options overlay through \`FirstDraftOptionsMenu\`. The overlay is placed on top of the original Options Panel; existing serialized UI objects and scene overrides remain intact.

## Included settings

**Graphics**
- Display mode (exclusive fullscreen, borderless, windowed), resolution, with 15-second confirmation and rollback **in standalone builds**.
- Existing Unity quality presets (using \`QualitySettings.names\` and \`SetQualityLevel\`).
- VSync on/off and FPS limit (30, 60, 120, unlimited). Note that VSync can override the FPS cap.

**Audio**
- Enable menu music, menu music volume.
- Enable gameplay music, gameplay music volume.
- Preserves the existing separate PlayerPrefs keys.

**Gameplay**
- Autosave on/off and interval (1 to 10 turns).
- Camera pan-speed and zoom-speed multipliers (0.5–2.0). \`PlanetaryCameraManager\` applies the saved multipliers at startup and reapplies changes without compounding them.

## Intentional omissions

- No sound-effects slider until a shared AudioMixer or equivalent bus controls *all* game sound effects.
- No UI-scale or tooltip-delay control until the affected canvases and tooltip controllers use a single validated setting.
- No per-effect HDRP graphics controls or key-rebinding system in the first draft.
- World-generation options remain in New Game setup, not this menu.

## Existing bugs fixed

- \`MusicManager.Awake\` previously overwrote \`GameMusicVolume\` with 0.75 every time; now reads the saved value.
- \`PauseMenuManager.InitializeAutosaveSystem\` now reads \`AutosaveEnabled\` and \`AutosaveInterval\` preferences from the Main Menu. It subscribes to turn events even when autosave starts disabled, so enabling later works.

## Implementation notes

- The first opening of either existing Options Panel adds \`FirstDraftOptionsMenu\` to that panel and creates the overlay once.
- Opening a second time refreshes the controls without recreating the root.
- The original Options panel stays wired through MainMenuManager/PauseMenuManager.
- The overlay's BACK button calls the existing back handler.
- Display mode and resolution cannot be tested in Unity Editor because \`Screen.SetResolution\` is intentionally skipped there; test in a standalone player.
- Graphics quality options reflect existing quality levels in the Unity project. HDRP quality assets still need manual verification in the Editor.
- The current implementation does **not** require new sprites or prefab references.

## Required Unity smoke tests

1. Open Options from the Main Menu, switch tabs, edit values, close, reopen and confirm persistence.
2. Repeat from the in-game Pause Menu, with timeScale at zero.
3. Confirm graphics quality and VSync actually take effect in HDRP (and the relevant Quality Settings assets exist).
4. Build and launch a standalone player; verify display mode/resolution confirmation and automatic revert after 15 seconds without confirmation.
5. Start a new game with autosave disabled; enable it in-game and verify subsequent eligible turns generate autosaves.
6. Set autosave interval from the Main Menu; verify the gameplay manager loads it.
7. Change camera speed, switch worlds/areas, and verify multipliers do not compound.
8. Lower gameplay music volume, restart, and verify it stays at the saved value rather than resetting to 75%.
9. Verify all buttons/sliders at 1920×1080, 2560×1440, 3440×1440, 1366×768, and 1024×768.
10. Confirm old Main Menu, Pause Menu, New Game, loading-screen, and world-generation interactions remain functional.

Native Unity compilation and runtime testing are not available in this editing environment. The above tests are required before merging or releasing.
