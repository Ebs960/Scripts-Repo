# Technology and Culture UI Modernization

This guide includes the panoramic modernization and the full-screen shell/hover-popup integration. The runtime trees contain only a compact progress header, murals, connection lines, icon/name nodes, and a floating details popup. No navigation controls are created.

## Custom Artwork Assignments

The remote `feature/tech-culture-hover-popups` branch and draft PR #545 were checked. The local shared popup/model already improves on that draft by computing details without permanent labels and scrolling rather than truncating them. Existing helpers are reused; no new C# scripts or dependencies were added in this integration.

The four requested blue/gold sprites were not identified in the current project's imported files. The similarly named `New UI Icons/Research and Culture Progress Bar.png` and `UI/Tech Entry Template.png` are older parchment artwork and are deliberately NOT assigned as substitutes. Import the actual new assets and assign the same four sprites on BOTH `TechUI` and `CultureUI`:

| Controller Field | Required Sprite | Shared Component Destination |
| --- | --- | --- |
| `mainShellSprite` | Tech/Culture Main Shell, transparent center | `TreePanelView.mainShellSprite` |
| `nodeButtonSprite` | Tech/Culture Node Button, no baked labels/icons | `TreeNodeVisual.backgroundSprite` on existing runtime nodes |
| `researchProgressSprite` | Research Progress Bar Background, blue/gold channel | `TreePanelView.researchProgressSprite` |
| `hoverPopupSprite` | Technology/Culture Hover Popup Background, title and divider | `ResearchHoverPopup.backgroundSprite` |

Inspector tooltips repeat the intended artwork for each slot. Missing shell art leaves the center transparent; missing node/progress/popup art uses plain functional blue UI, not unrelated artwork. Final visual matching requires the actual sprites.

Import all four as **Sprite (2D and UI)**, Single, **Full Rect**, with alpha transparency enabled and sufficient texture resolution. Preserve alpha in the source files; do not flatten the shell's transparent opening. Keep the existing Canvas/CanvasScaler settings. In Sprite Editor, measure borders from the artwork rather than guessing pixel values:

- Main shell: protect the corner/frame strips and include the WHOLE header band in the top border. The transparent central region is the sliced center. A sliced shell fills its parent; an unsliced shell is aspect-fitted with the viewport inset to its actual visible opening.
- Node: protect gold frame/corners; only the plain interior should stretch. Unsliced artwork preserves its aspect ratio. Outer node sizes and saved positions remain configurable and unchanged.
- Progress: protect the thin decorative frame. The fill lives in a separate clipped inner channel, never on top of the frame. `TreePanelView.progressChannelInsets` is normalized left/bottom/right/top padding; sliced borders and unsliced aspect fitting are also respected. Adjust these values if the painted channel has extra interior padding.
- Popup: protect the corners and include the WHOLE title band AND divider in its top border. `titleSectionHeight` can reserve additional room. Unsliced artwork uses a fixed aspect-correct popup size; adjust `unslicedTitleFraction` to the actual title-band proportion. Body content always scrolls below the title/divider.

Do not add permanent node progress bars, costs, turn labels, descriptions or unlock lists. Runtime nodes retain the existing background, icon, name and Button. Queue commands remain functional; legacy queue badges are hidden in modern presentation.

## Changed Scripts

Full-screen/hover-popup follow-up:
- `TechUI.cs`, `CultureUI.cs`: four explicit artwork slots, live header observations, player-civilization switching, separate unit-unlock details, legacy panel hiding, and existing popup lifecycle. Gameplay selection, prerequisite and Shift-queue methods are unchanged.
- `TechButtonUI.cs`, `CultureButtonUI.cs`: assign shared node art, retain pointer/selection/click handlers, and hide legacy queue badges in modern presentation.
- `TreePanelView.cs`: responsive native header, clipped normalized progress channel, artwork-aware shell/viewport layout, transparent non-raycasting decoration, legacy material removal, no generated navigation UI, and retained serialized fields.
- `TreeNodeVisual.cs`: one universal sprite, subdued locked/completed tints, restrained active/selected outlines, and separate icon/name regions.
- `ResearchPopupInfo.cs`: existing data model now exposes a separate optional Units section.
- `ResearchHoverPopup.cs`: existing shared popup now respects the sprite's slice borders/title band or unsliced aspect ratio, without recoloring supplied art or adding an extra artwork border.
- `Tech/Technology UI.prefab`, `Culture/Culture UI.prefab`: additive `legacyInformationPanels` references hide `Technology Title` and `Cultural Development` on Show. Existing object IDs, sprite GUIDs, controller references, content, layouts and close buttons are retained.
- This existing guide is updated. No new scripts, components, artwork, packages or `.meta` files were created for this integration.

Original panorama modernization:

Modified:
- `TechTreeBackgroundData.cs`, `CultureTreeBackgroundData.cs`: configurable multi-age mural segments and legacy fallback.
- `TechUI.cs`, `CultureUI.cs`: shared background rendering, final union bounds, navigation, optional shared shell, progress/actions, defensive references, right-click inspection entry points, rebuild controls.
- `TechTreeBuilder.cs`, `CultureTreeBuilder.cs`: shared panoramas and bounds after editing. Culture layout loading also fixes an existing conditional brace/load-path defect.
- `TechButtonUI.cs`, `CultureButtonUI.cs`: shared node styling and right-click inspection, retaining existing left-click actions, eligibility, queue badges and tooltips.
- `TechPaletteItem.cs`, `CulturePaletteItem.cs`: convert pointer coordinates into the original layout coordinate system after content padding is added.

Created:
- `TreePanorama.cs`: serializable segments, resolved sprite rectangles, age-range lookup, shared rendering, optional era labels.
- `TreeContentBounds.cs`: final padded union bounds and stable layout-coordinate conversion.
- `TreeNavigation.cs`: cursor-centered wheel zoom, normal ScrollRect panning, middle-button pan, reset and focus.
- `TreeNodeVisual.cs`: common locked/available/active/completed surfaces and independent selected outline; optional state sprites.
- `TreePanelView.cs`: shared configurable shell layout, dark surfaces, header progress and control binding, now extended as described above.
- Corresponding C# `.meta` files and this guide.

No modifications to TechManager, CultureManager, ResearchDatabase, UIManager, gameplay data classes, either TreeLayoutData asset format, builders, murals, connection logic or JSON position formats. Existing scene instances and serialized field names/references are retained; only the two additive prefab heading-disable lists above change prefab YAML.

## Panorama Architecture

Both background assets now expose `segments`. Each entry has a sprite, inclusive `startAge`/`endAge`, and an unscaled `displayWidth`. Populated segment arrays override `ageBackgrounds`; an empty/null array uses the old age-based configuration. Segments are sorted by starting age, not rendered once for every age. One mural can span multiple ages.

With `displayWidth = 0`, width is calculated from `displayHeight` and the sprite's actual rect aspect ratio. All such murals share the same displayed height and global `backgroundScale`. Explicit widths are respected and height is derived from that width and the actual aspect ratio. No cropping, stretching, or letterboxing within adjacent image rectangles is introduced. Segments are top-aligned. For different sprite ratios and a common height, leave widths at zero, or set each width to `displayHeight * sprite.rect.width / sprite.rect.height`. Arbitrary independent widths necessarily produce different heights when aspect ratios differ; identical heights AND arbitrary widths cannot both preserve entire images without cropping or distortion.

`imageSpacing` defaults to zero and negative spacing is clamped to zero. No trailing spacer is added. Missing sprites/null entries are skipped safely. Legacy entries keep their previous 1792-pixel default width and custom-width overrides; height is now aspect-correct instead of assumed to be 1024 pixels.

Age lookups divide a multi-age segment into equal chronological slots for grid fallback and optional age navigation. This never relocates nodes loaded from JSON. Use ascending, non-overlapping age ranges that cover the intended timeline; missing historical ranges have no independent image or reserved chronological slot.

The renderer creates `BackgroundContainer` behind nodes and lines. Images and era labels have raycasts disabled. Backgrounds, labels, lines and nodes are all inside the same scroll content, so they pan/zoom together.

## Assign the Ten Murals

1. Import each mural as Sprite (2D and UI), preferably Full Rect. Avoid transparent gutters at the adjoining image edges. Choose sufficient texture resolution/max size and filtering in Unity; the code cannot eliminate seams already painted into the artwork or introduced by texture import/compression.
2. Open the TechTreeBackgroundData asset already referenced by Technology UI and Tech Tree Builder. Set `segments` size to **5** and assign the five Technology sprites in chronological order.
3. Repeat on the CultureTreeBackgroundData asset, assigning the five Culture sprites. Do not share the same asset between the independent systems.
4. Set start/end ages to the history actually depicted by each image. The following is an example covering the existing twenty enum values, not an automatic or required mapping:

| Segment | Start | End |
| --- | --- | --- |
| 1 | PaleolithicAge | BronzeAge |
| 2 | IronAge | FeudalAge |
| 3 | CastleAge | EnlightenmentAge |
| 4 | SteamAge | InformationAge |
| 5 | NanoAge | GalacticAge |

5. Set `imageSpacing = 0`, `backgroundScale = 1`, and a common `displayHeight` (for example 1000). For exact 3:1 murals, `displayWidth = 0` then resolves to 3000 per segment. Alternatively assign each segment its own width using the aspect-correct rule above.
6. Retain the old age entries as a reversible fallback. Clearing `segments` returns to them. Do not duplicate a single multi-age mural across its constituent ages.
7. Optionally enable `showEraMarkers`. This adds noninteractive range labels 40 layout units above the mural using TMP's default font, or omits them if no default font exists. Final bounds include that label band. Keep saved nodes clear of it; labels default off to preserve existing compositions.

## Inspector and Prefab Setup

The existing runtime panels and node prefabs are reused. The only required prefab data additions have been made: the old static headings are listed as legacy objects to hide on Show. Header/progress/popup objects are created when absent, not serialized as duplicate hierarchies. Assign the four controller sprites above; preserve existing serialized references and any scene-instance overrides. No builder-prefab changes or JSON migration are needed.

For each runtime panel:

1. Keep all existing controller references, including ScrollRect, content, button prefab, layout JSON, background data, selected-node fields, icon and close button. Ensure the ScrollRect's Content points to that controller's content. Do not put layout groups/ContentSizeFitter on the manually positioned tree content; the bounds helper disables those components on that content only.
2. Keep the existing ScrollRect, content, scrollbars and both pan axes. No new TreeNavigation is added by the controllers. An already-assigned navigation component remains compatible, but no zoom, reset, focus, search, minimap or other navigation controls are created. Keep the viewport Image raycastable; shell setup makes legacy scroll/viewport surfaces transparent and hides Mask graphics, preserving empty-space drag input.
3. Keep `useFloatingPopups` enabled on TechUI/CultureUI. Their legacy name/icon/description/cost/prerequisite/unlock/economic field objects are hidden on Show, not deleted. Detail generation no longer writes those fields in floating mode or changes their active state during RefreshUI. They remain available for compatibility if a project explicitly opts out of floating mode.
4. Add/assign `TreePanelView` on the panel root to configure its values before Play Mode. When unassigned, the controller creates/reuses the existing panel's shell component. Assign `panelShell` to the panel root and `treeViewport` to the WHOLE existing ScrollRect object, not its nested masked viewport. Keep this ScrollRect and the header as direct children of the shell so its layout helper can arrange them.
5. Enable `fullScreen`, `arrangeAssignedRegions` and `hidePermanentDetails`. The root stretches to its existing parent Canvas region; CanvasScaler is not replaced. Header layout is 52 units at wide sizes, 80 at widths below 900, and 112 below 480, enlarged if a sliced header band requires more room. Unsliced shells preserve aspect ratio. The viewport occupies the remaining opening, without a bottom/right information region. Disable arrangement only for an equivalent custom full-screen prefab layout.
6. Keep the old `selectedNodeArea` and `infoSurface` assignments if present; the shared view hides them. Populate `legacyInformationPanels` with any other opaque legacy detail/sidebar backgrounds and obsolete static title objects not covered by the controller's field references. The helper does not guess which unreferenced decorative siblings are safe to hide. Never assign the main shell, tree viewport or an ancestor containing the tree to this list.
7. Assign the four controller artwork slots. Optional component-level overrides are available on TreePanelView, TreeNodeVisual and ResearchHoverPopup. `surfaceSprite` and other previous fields remain serialized for compatibility, but the main shell uses `mainShellSprite`. Supplied art renders white-tinted with no extra shell frame; legacy shell materials are cleared.
8. For a prebuilt header, assign `titleText`, `focusText`, `researchProgress`, `progressBackgroundImage`, `progressFillImage`, `progressText`, and `turnsRemainingText`. Otherwise the view creates them. Use a separate FillChannel/RectMask2D below the background; directly assigned fills are wrapped automatically when necessary. The normalized fill is clamped accumulated progress/cost, with zero-cost and no-income handling. There is one header progress bar, not one per node.
9. Legacy `navigationGroup`, zoom/reset/focus controls and permanent selection/research controls are hidden in the default layout; no replacement navigation UI is introduced.
10. Assign the same existing close Button to the controller and view. It is moved into the header without changing its controller/UIManager click wiring. A missing close button is created and bound to Hide. Retain existing panel names and opening/closing integrations.

Suggested hierarchy (reuse objects where possible):

```text
Existing Canvas [existing CanvasScaler, GraphicRaycaster]
  Technology UI / Culture UI [existing controller]
    Technology List / Culture List [TreePanelView]
      Scroll View [existing ScrollRect]
        Viewport [mask, transparent raycastable Image]
          Content [existing murals, lines, icon/name nodes]
        Existing scrollbars
      ShellArtwork [shared shell Image, no raycasts, above viewport]
      Tree Header
        Title, Current Focus, Existing Close Button
        ResearchProgress
          ProgressBackground [Image]
            FillChannel [RectMask2D]
              ProgressFill [Image]
            ProgressLabel [TMP]
          TurnsRemainingLabel [TMP]
      Legacy Details / Static Heading [inactive; references retained]
  ResearchHoverPopup [dedicated host, outside tree zoom hierarchy]
    Floating Details [Image, CanvasGroup]
      Icon, Name, Age and Status
      Scrollable Details [ScrollRect, transparent Image, RectMask2D]
        Details [single TMP text, ScrollRect content]
```

## Shared Popup Setup

Quick runtime setup: leave `hoverPopup` blank on both controllers. On first hover, the controller finds/creates one ResearchHoverPopup under its root Canvas; both trees on that Canvas reuse it. Existing TechButtonUI/CultureButtonUI automatically connect pointer enter/exit and selection events. Do not add duplicate EventTrigger callbacks. A missing Canvas means the existing global tooltip remains the fallback; the regular tree panel requires its existing Canvas/EventSystem.

For a configurable shared prefab:

1. Create a dedicated RectTransform host under the existing root Canvas, add `ResearchHoverPopup`, and save it as a prefab. Leave view references blank to let it create its plain functional child view. Assign the SAME prefab asset or dedicated scene instance to both controllers' `hoverPopup` fields. Prefab assets are instantiated under the Canvas; scene hosts found under a zoom/content hierarchy are moved outside it. Do not put the component on a tree node or on a gameplay controller's own GameObject.
2. Keep the host enabled/active; the component handles its child view visibility. Put any background Image on the popup child, NOT a full-screen host Image that would intercept every click. The existing Canvas must have a GraphicRaycaster and the existing EventSystem/InputSystemUIInputModule must remain enabled. A CanvasGroup on the popup child controls visibility and raycasts.
3. Defaults are width 360 Canvas/UI units, height up to 70% of visible available area, show delay 0.15 seconds, hide delay 0.15 seconds, 12-unit node gap and 8-unit screen margin. Adjust `preferredWidth` (approximately 340-380), `maximumHeightFraction`, delays, colors, `font`, and optional `backgroundSprite` in the Inspector. No new art is required; it uses TMP's default font or an existing Canvas TMP font when unassigned.
4. If designing the child view yourself, assign `popupRect`, `backgroundImage`, `iconImage`, `nameText`, `summaryText`, `bodyText`, `detailsScroll`, and `popupGroup`. Use a compact header approximately 80 units high. Put bodyText as the sole scrolling content under the assigned detailsScroll viewport; the component owns its measured height. Use a RectMask2D and a transparent raycastable viewport Image. Do not attach layout groups or ContentSizeFitter that override the popup/content sizing. Optional ScrollRect scrollbar references can be assigned normally.
5. Body text stays wrapped at a readable fixed font size and is never shortened or auto-shrunk to fit a long unlock list. The popup height is capped and the body scrolls by wheel or drag. The pointer can move from the node into the popup during the short hide grace period to read/scroll. Configure your existing gamepad scroll bindings separately if needed; this update does not introduce a new global input map.
6. Blank description/unlock/yield/economic sections are omitted. Name/icon, historical age, status, cost, active progress, estimated turns, prerequisites, reverse-looked-up buildings/improvements/other unlocks, direct unlocks, yields, economic preview and legacy combat bonuses come from the controller's existing calculations. No duplicate technology/culture unlock scanner was added to the popup or tooltip system. The legacy economic preview formula is reused, not changed into a new economy mechanic.
7. Node corners are projected through screen space into the popup's Canvas, so tree zoom does not scale the popup. The layout flips beside an edge, tries above/below on narrow canvases, and clamps to available Canvas/safe-area bounds. Only one pending/visible research popup is active at a time. Ensure any separate popup Canvas uses appropriate sorting and a GraphicRaycaster; test camera/safe-area behavior in Unity.
8. Right-click inspection now pins the floating details using the previous info-only selection path. Keyboard/controller EventSystem node selection can also pin without starting research; disable `pinOnNavigationSelection` to opt out. Mouse selection events do not trigger that keyboard pin path. Escape, a new node hover/request, main-panel Hide/close/disable, and tree rebuild close or replace pinned content. A custom dismiss control can call `ResearchHoverPopup.HideImmediately()` or the controller's `ClosePopup()`.
9. `RefreshUI()` updates the header and visible popup. While a panel is open, lightweight observations detect active-item/progress/completion changes and player-civilization replacement; existing yield calculations refresh the header every 0.25 seconds. Unlock scanners are NOT run on every placement frame. The popup never invokes research/adoption actions.

## Scrolling and Selection

- Hover only builds information; it does not select/highlight a node or change research/adoption/queues. Locked/completed nodes still receive hover events despite the existing Button interaction eligibility.
- Left click retains the existing research/adoption and Shift-queue behavior. Locked/completed buttons retain their previous click eligibility and state handling. The popup is beside the node; if clamping overlaps the hovered node, its raycasts pass through that overlap to keep the node clickable.
- Right click pins inspection, including locked/completed nodes, without starting research. Existing public research/adoption methods remain available, but no new research/adoption mechanic or action is introduced by hovering or pinning.
- Left drag and existing scrollbars/wheel use the existing ScrollRect. Existing assigned pan components are retained; none are added.
- Wheel/drag over the popup scrolls its details. Retain existing EventSystem/InputSystemUIInputModule configuration and test overlay raycasts in Unity.
- No zoom, reset, focus, age-navigation, search or minimap controls are generated. Older serialized control references remain available but are hidden.
- During Play Mode, use the controller's **Rebuild Tree** context menu/public `RebuildTree()` after changing background/layout references; normal reopening retains the existing build-once cache and pan position. Re-entering Play Mode also rebuilds.

## Builder and Layout Compatibility

Assign the same respective background assets to the TechTreeBuilder and CultureTreeBuilder Inspector fields. No required builder hierarchy changes; they reuse their existing grid, palette, node prefabs, connections and buttons. Navigation is not auto-added to builders, avoiding changes to editor drag/input semantics.

Final bounds include the complete mural, actual node RectTransforms, rotated line rectangles, grid extents, optional label band and padding on every edge. Nodes extending left of x=0 or above y=0 are also included. Child anchors are adjusted to a shared padded layout origin while anchoredPosition values remain unchanged. Pointer-to-layout conversion uses that origin in both palette scripts, so padding is not saved into JSON. Repeated fitting does not accumulate padding. Keep new manually-created tree children aligned through `TreeContentBounds.AlignChild` if extending the builders further.

No JSON/layout migration or re-saving is required. Existing asset-name/display-name matching, node positions, prerequisite arrays, saved grid information, and public builder/controller methods remain available. New asset arrays are additive; neither TreeLayoutData script was changed. Existing builder grid snapping/occupancy and auto-layout limits are retained, not redesigned.

## Verification and Remaining Work

- Current full gameplay source compiled successfully using Unity 6000.4.9f1's actual Bee response file and compiler, with output redirected to temporary files. No Unity player build was performed.
- A temporary geometry harness executed the actual panorama/background/bounds source using lightweight Unity stand-ins. It checked five-versus-twenty segment rendering, exact adjoining bounds, custom widths and aspect ratios, multi-age coordinates, missing sprites, legacy fallback, union bounds beyond the mural, idempotent resizing and unchanged saved coordinates. It does not emulate Unity rendering, native RectTransform behavior in every configuration, raycasts or ScrollRect physics.
- Managed tests ran the compiled popup/model and shell math using actual Unity math structs. They cover both currencies' progress/turns, completion, zero income, omitted empty sections, unit details, 1000-entry unlock lists, edge/offscreen flips, header progress clamping and shell aspect fitting across 390x844, 844x390, 1280x720, 1920x1080 and 2560x1080. These do not invoke native gameplay, TMP rendering or EventSystem input.
- **No visual testing or Play Mode interaction testing was performed in Unity.** After assigning the actual sprites, verify both open/close paths, shared shell, active items, turn/progress updates, player switching, icon/name fit, all five node states, left click and Shift queues, hover delays, popup scrolling and edges, hidden legacy objects, murals, connection lines and both pan axes. Confirm no navigation controls appear and existing scene overrides reference the intended assets.
- Final art import/slicing/assignment and scene-instance testing remain Unity Editor work. No final artwork was fabricated and no unrelated parchment asset was substituted. Builders, layouts, JSON coordinates and gameplay systems were not changed in this integration.

### Required Unity Checks

| Check | Repository Verification | Still Requires Unity |
| --- | --- | --- |
| Technology hover information | Controller extraction inspected; shared code compiled | Hover actual technology assets; compare unlocks and economics |
| Culture hover information | Equivalent extraction compiled | Hover actual culture assets and age-gated entries |
| Locked prerequisites | Model text retention tested; controller requirements included | Disabled Button raycasts and actual prerequisite data |
| Active progress/turns | Compiled model arithmetic tested for both currencies | Turn refresh through existing managers/UI calls |
| Long lists | 1000-entry model output retained; scrolling code compiled | TMP sizing, wheel/drag/optional scrollbar behavior |
| Screen boundaries | Compiled flip/clamp math tested | Screen projection, CanvasScaler, cameras and safe areas |
| Hide on panel close | Hide, close-button, OnDisable, rebuild and owner cleanup inspected | Close/reopen, rapid switching and pinned lifecycle |
| Click interactions | Existing click/queue methods retained; hover model has no research calls | Left click, Shift queues, right click and keyboard/controller selection |
| Tree scrolling | Existing ScrollRect retained; no navigation UI generated | Both pan axes, popup scrolling and modal overlays |
| Layouts/gameplay intact | Editor/player slice compilation and existing panorama/layout geometry checks passed | Builder save/load/dragging plus in-game research/adoption progression |