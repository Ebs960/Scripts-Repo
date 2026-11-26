# Main Menu Layout A

`UI/Game Startup.prefab` attaches `MainMenuPresentation` and
`MainMenuBackgroundRotator` to the existing main-menu panel. The prefab saves
the title screen hierarchy and keeps the original four buttons:

```text
MainMenuRoot
  Historical Background
  Left Menu Shell
    Game Title
    New Game
    Load Game
    Options
    Quit
    Version
```

The shell occupies the left 35% of the screen. Buttons retain their original
references and callbacks. The root canvas
uses Scale With Screen Size, 1920x1080, with a 0.5 width/height match. Backgrounds
cover the viewport without stretching. Decorative images and TMP labels do not
receive raycasts. Up/down navigation wraps between the four buttons; returning
to the menu restores its last selected button.

## Artwork

The prefab's Images reference the supplied main menu shell and generic button art.
The historical Image has a saved preview sprite and is bound to the rotator.
The shared `UI/Historical Background Catalog.asset` contains all 25 numbered
background sprites and a fallback. Only the background choice, version text,
and focus restoration are handled at runtime.

## Rotation And Persistence

Selection logic lives in the static `HistoricalBackgroundSelector`; the rotator and
the regular loading screen (`LoadingHistoricalBackground` on
`UI/Loading UI Manager.prefab`) are thin consumers of it and share one history.
The rotator chooses once per loaded menu instance, not whenever the menu panel is
re-enabled; the loading screen chooses once per loading session (first show
through hide), never on progress or status updates. Null and duplicate sprite
entries are ignored. A shuffled bag is
stored immediately in PlayerPrefs under `MainMenu.HistoricalBackgrounds.v1`,
including the remaining order, last index, and a catalog signature. Each valid
image appears once per cycle; when multiple images exist, a new cycle does not
immediately repeat the previous image. A new menu instance resumes the saved
order. Invalid state or a changed catalog resets the bag. A dedicated
`System.Random` leaves world-generation randomness untouched.

Because the signature is derived from the catalog contents, both screens must
reference the same `HistoricalBackgroundCatalog` asset for the sequence to
continue between them (menu shows image 7, loading shows image 18, the next menu
visit continues the same cycle).

## Regular Loading Screen

`UI/Loading UI Manager.prefab` mirrors the menu split: a navy information panel
(35% width, minimum 320 canvas units) with the title, status text, thin gold
progress bar and percentage, and a `RectMask2D` viewport on the right showing one
historical background with the same cover-fit sizing. The canvas uses Scale With
Screen Size, 1920x1080, 0.5 match. The space-travel loading prefab is unchanged.

Options and Back navigation never draw another background. Clear the configured
PlayerPrefs key to reset the order; use a different key only for a separate
independent catalog.

## Existing Behavior

The shared [confirmation dialog](./UI/Resources/MenuUI/Confirmation%20Dialog.prefab)
is an authored Resources prefab used by both the campaign's Pause/Save/Load
actions and the main menu's load-error messages. Its controller supplies the
message and callbacks; the Canvas, backdrop, artwork, and buttons are saved in
the prefab. Scene-specific diplomacy and government confirmations are separate.

The campaign's [notification panel](./UI/Notification%20Panel.prefab) is already
assigned in the campaign scene. Its card is non-interactive, and `UIManager`
still owns notification timing and the queue used while loading.

The campaign's pause panel is saved in the [Pause Menu prefab](./UI/Pause%20Menu.prefab):
`Pause Panel` holds the dimmed backdrop, the `Pause Shell` artwork, the title, the
six original buttons, and the save-restriction text. `PauseMenuPresentation` only
shows and hides it; `PauseMenuManager` still owns every button action.

The Options and Save/Load screens are saved prefabs loaded from Resources, shared
by the Main Menu and the Pause Menu:

- [Options Menu](./UI/Resources/MenuUI/Options%20Menu.prefab) holds the shell, the four
  category tabs, and every settings row on four pages (only the Graphics page is
  active in the prefab; enable another page object to edit it). `OptionsMenuView`
  lists the controls and `FirstDraftOptionsMenu` supplies values and behaviour.
- [Save Load Browser](./UI/Resources/MenuUI/Save%20Load%20Browser.prefab) holds the shell,
  the list with one `Save Slot Row` template, the details frame, and the three
  buttons. `SaveLoadMenuUI` clones the template once per save.

The main menu's four buttons, like every button here, show their artwork on an
`Art` child because the button sprites have transparent padding.

`MainMenuManager.cs` and `MenuPlanetPreview` are unchanged. Civilization
Selection, Leader Selection, Game Setup, and their Back navigation retain the
existing controller paths. The MainMenu scene's Options panel and Options Back
button remain wired through their existing scene overrides.

`OnLoadGameClicked()` currently contains only a TODO. The redesign does not
implement loading or introduce a simulated save-slot panel. Quit retains the
existing Editor/player behavior.

## Verification

- Full game-source compilation passed using Unity 6000.4.9f1's compiler and
  current response file, including the two new scripts.
- The compiled production shuffle bag passed 91,810 assertions covering cycles,
  saved-order reconstruction, cycle-boundary repeats, and invalid saved state.
  The external runner used System.Text.Json for reconstruction, not Unity's
  native JsonUtility or an actual application relaunch.
- The production cover-sizing method passed at 1920x1080, 2560x1440, 3440x1440,
  and 1024x768 with landscape and portrait artwork. Layout arithmetic fits at
  these resolutions. Prefab component IDs, metadata newlines, and unchanged
  controller/preview sources were checked.
- Native Unity preview validation could not execute in the open editor. Actual
  rendering, text fit, pointer/gamepad input, Quit, and a real cross-launch
  PlayerPrefs roundtrip remain unverified.

Before release, run the MainMenu scene at the resolutions above. Check each main
button, Options and Back, New Game through Civilization Selection, Leader
Selection and Game Setup, and every Back route. Verify one background remains
selected during navigation, then restart the menu/application to verify the
remaining shuffle order persists. Verify cover cropping and the noninteractive
background with assigned art. Check Quit in the Editor and in a player build.
## New campaign selection artwork

- [Civilization artwork and emblems](./UI/Selection%20UI/Civilizations) are organized separately from [leader artwork](./UI/Selection%20UI/Leaders). Keep each image's `.meta` file with it when moving or renaming it.
- The civilization view reads `CivData.factionSelectionArtwork`. The leader view reads `leaderSelectionEntries` in the [Game Startup prefab](./UI/Game%20Startup.prefab), matching by `LeaderData` reference. The artwork area stays empty until a selection is made.
- All 20 playable civilizations and their 35 selectable leaders have selection illustrations assigned. The images in both selection folders are imported as Sprite (2D and UI), Single.
- Both 1672 x 941 shells are assigned directly to the panel Images in the startup prefab. The roster, artwork slot, scrollable overview and bonuses, and Back/Next controls are saved in that hierarchy, so they can be inspected without Play Mode. At runtime the presenter uses these saved controls to populate the roster and show the selected artwork.

## World setup

`Setup UI Panel` in the [Game Startup prefab](./UI/Game%20Startup.prefab) saves the Create Your World layout; no script builds or moves it.

- `World Setup Layout` holds everything and uses an Aspect Ratio Fitter (1672 x 941) so the controls stay aligned with the shell on wider or narrower screens.
- `Preview Raw Image` sits behind `World Creation Shell` and shows through its circular window. Its UV rect crops the centre of the 2:1 planet render texture, so the planet stays round. If the preview camera, its distance, or the render texture size changes, adjust that UV rect.
- `Civilizations & Advanced`, `Map Description Stuff`, and `Geography & Climate` match the shell's three framed regions. Back and Begin sit at the ends of the title bar.
- The control artwork has transparent padding, so each button, dropdown, and input keeps an invisible Image as its exact click area and shows the padded artwork on an `Art` child.
- `MainMenuManager` still references the same dropdowns, toggles, inputs, and buttons.
