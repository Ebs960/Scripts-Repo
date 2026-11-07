# First-Draft Options Menu

Both the Main Menu and the in-game Pause Menu open the same code-built Options overlay through
`FirstDraftOptionsMenu.Open(optionsPanel, backHandler, context)`. The overlay lives on its own
screen-space canvas (1920x1080, scale-with-screen-size, sorting order 900) so the legacy Options
canvases keep their serialized scaling. Legacy sliders/toggles/buttons stay serialized and wired but
are made non-interactable while the overlay is visible, and restored when it closes. Widgets are
built once per host; reopening only refreshes values from `GameOptionsSettings`.

## Layout

Header `OPTIONS` (context label right-aligned), a narrow category column on the left
(Graphics / Audio / Gameplay / Interface), a scrollable settings area on the right with one row
per setting (label left, native Toggle / TMP_Dropdown / Slider right), and a footer with a status
line and `BACK`. Colors: navy `#0A0F14` / `#0D1B2B`, muted gold `#A5763A`, gold `#C89235`,
warm off-white text. Fonts are taken from the host menu (Cinzel for headings/buttons,
Source Sans 3 for body text when present, otherwise the TMP default). Built-in UI sprites are used
for the checkmark, dropdown arrow and slider knob; no new artwork.

## Settings

**Graphics**
- Display Mode (Fullscreen / Borderless / Windowed) and Resolution (`Screen.resolutions`,
  current resolution always present). `APPLY DISPLAY CHANGES` previews; `KEEP CHANGES` persists;
  `REVERT` or 15 seconds without confirmation restores the previous mode/resolution (unscaled time,
  so it works while paused). Closing the menu or switching tabs during the window also reverts.
  `Screen.SetResolution` is skipped in the Editor; test in a standalone player.
- Graphics Quality: the project's real Unity quality levels (`Mobile`, `PC`, each bound to its own
  HDRP asset). Four Low..Ultra labels were not invented because only these two levels exist.
- VSync on/off (`QualitySettings.vSyncCount`), re-applied after quality changes.
- Frame Rate Limit 30 / 60 / 120 / Unlimited (`Application.targetFrameRate`).

**Audio**
- Menu music on/off + volume, gameplay music on/off + volume (existing keys `MenuMusicEnabled`,
  `MenuMusicVolume`, `MusicEnabled`, `GameMusicVolume`). Disabling music stops/fades playback but
  never overwrites the saved volume; re-enabling restores it.

**Gameplay**
- Autosave on/off and frequency (1-10 turns).
- Camera Movement Speed and Camera Zoom Sensitivity (50%-200%). `PlanetaryCameraManager`
  multiplies keyboard pan, mouse drag and scroll zoom at the point of input; serialized
  `panSpeed`/`zoomSpeed` are untouched and scripted underwater/surface/orbit transitions are
  unaffected. Changes arrive through `GameOptionsSettings.CameraSettingsChanged`.

**Interface**
- Tooltip Delay: Instant (0 s) / Short (0.15 s) / Normal (0.3 s). Read by `TooltipSystem`
  (tech, culture, simple tooltips), `PolicyTooltipUI`, `ResearchHoverPopup` and `BattleHoverTip`.

## Persistence

`GameOptionsSettings` is the single store. New keys use the `Options.*.v1` prefix; existing audio
and autosave keys are reused. Setters apply immediately and mark the store dirty;
`GameOptionsSettings.Flush()` writes PlayerPrefs when the overlay closes and on application quit,
so slider drags do not hit the disk per tick. Graphics settings are re-applied on startup via
`RuntimeInitializeOnLoadMethod`.

## Bugs fixed

- `MusicManager.Awake` no longer forces `GameMusicVolume` to 0.75 and overwrites the preference.
- Both legacy menus initialised their toggles *after* listeners were attached; a saved "music off"
  state therefore ran `SetVolume(0)` on load and erased the saved volume. Legacy controls now
  initialise with `SetValueWithoutNotify` / `SetIsOnWithoutNotify` and route through the shared
  setters.
- `PauseMenuManager` loads `AutosaveEnabled` / `AutosaveInterval` from the shared preferences,
  listens to `AutosaveSettingsChanged`, subscribes to turn events even when autosave starts
  disabled, retries the subscription when the game starts, and guards against duplicate
  subscriptions. Save file names and formats are unchanged.

## Defaults that changed (documented)

- Autosave interval now defaults to 3 turns everywhere (the Main Menu default). The Pause Menu
  prefab's serialized `autosaveInterval: 5` is overridden by the preference.
- Frame rate limit defaults to 60 FPS; previously the application ran uncapped.
- `TooltipSystem` tooltips previously appeared instantly; the default delay is now 0.3 s
  (choose *Instant* to restore the old behaviour). Policy (0.3 s), battle (0.4 s) and research
  (0.15 s) tooltips now follow the same preference.
- VSync keeps the project default (off).

## Deferred

- Sound-effects volume: effects are played through ad-hoc `AudioSource.PlayOneShot` calls in five
  scripts and there is no AudioMixer. Needs a shared SFX mixer group or a central SFX service first.
- UI Scale: 46 CanvasScalers across the prefabs, 38 of them Constant Pixel Size with fixed-pixel
  layouts. A global scale would need those canvases migrated to Scale With Screen Size and a layout
  audit; changing one canvas would not be a real global setting.
- No key rebinding, per-effect HDRP controls, multiplayer, AI difficulty or world-generation
  options in this menu.

## Inspector assignments

None required. The overlay attaches itself to the existing `optionsPanel` references
(`Options (Main Menu)` root in the MainMenu scene, `Options Panel` in `UI/Pause Menu.prefab`).

## Required Unity tests

1. Main Menu: open Options, switch all four tabs, change every control, BACK returns to the menu
   without regenerating the historical background or restarting menu music. Reopen and confirm
   values persisted.
2. Pause Menu: with `Time.timeScale = 0`, repeat; the game stays paused, input stays blocked, ESC
   and Resume behave as before, BACK returns to the Pause Menu.
3. Standalone build: change display mode/resolution, let the 15 s window expire (reverts), then
   confirm within the window (persists across restart).
4. Quality (`Mobile`/`PC`), VSync and FPS limit visibly take effect in HDRP.
5. Lower gameplay music, restart: volume stays. Disable music, restart, re-enable: previous volume
   returns.
6. Start with autosave off, enable it in-game: autosaves occur on subsequent eligible turns at the
   chosen interval. Set the interval in the Main Menu and verify in game.
7. Camera speed/zoom sliders change panning, dragging and zooming; underwater/orbit transitions
   are unchanged; values do not compound across scenes.
8. Tooltip Delay changes apply to tech/culture tooltips, policy tooltips, research popups and
   battle tips.
9. Keyboard/controller: tabs, toggles, dropdowns, sliders and BACK are reachable; focused rows
   scroll into view; legacy controls under the overlay are not reachable.
10. Layout at 1920x1080, 2560x1440, 3440x1440, 1366x768 and 1024x768: no clipped labels or
    inaccessible controls.

The implementation compiles with the project's Unity-generated C# project. Runtime behaviour must
be verified in the Unity Editor and a standalone build.
