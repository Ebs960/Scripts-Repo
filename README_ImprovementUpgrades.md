# Improvement Upgrade System

## Overview
This system allows players to click on improvements to see and build available upgrades, such as adding a Dance Hut to a Tent Camp when the Dancing culture is researched.

## Features
- ✅ **SpearThrower Combat Unit**: Added to CombatCategory enum
- ✅ **Improvement Upgrades**: Data structure for defining upgrades with tech/culture/resource requirements
- ✅ **Clickable Improvements**: UI system for selecting improvements and viewing upgrade options
- ✅ **Cost System**: Gold and resource costs for upgrades
- ✅ **Persistence**: Built upgrades are tracked in tile data

## Core Components

### ImprovementUpgradeData
- Defines upgrade requirements (tech, culture, resources, gold)
- Specifies yields and visual prefabs
- Handles cost validation and resource consumption

### ImprovementUpgradeUI
- Shows available upgrades when clicking an improvement
- Displays costs and requirements
- Handles upgrade selection and construction

### ImprovementClickHandler
- Attached to improvement prefabs for click detection
- Opens upgrade UI for player-controlled improvements

## Setup Instructions

### 1. Create Improvement Upgrades
In your ImprovementData assets:
1. Expand the "Upgrades" section
2. Set `Available Upgrades` array size
3. Configure each upgrade:
   - **Upgrade Name**: "Dance Hut"
   - **Required Culture**: Dancing Culture asset
   - **Resource Costs**: Wood x3
   - **Gold Cost**: 50
   - **Upgrade Prefab**: Visual model to spawn (used when no runtime improvement instance exists)
   - **Makes Visual Change**: (new) Check this if the upgrade should change the improvement's appearance.
   - **Attach Prefabs**: (new) One or more prefabs to instantiate as children of the improvement when applied (ideal for modular parts like walls, moats, keeps).
   - **Replace Prefab**: (new) Optional prefab to fully replace the base improvement instance when the upgrade is applied (use for complex visual reconstructions).
   - **Upgrade ID**: (optional) A stable unique id string to persist upgrades across renames; if left blank `upgradeName` will be used.

### 2. Example: Tent Camp + Dance Hut
```csharp
// In your Tent Camp ImprovementData
availableUpgrades[0] = {
    upgradeName: "Dance Hut",
    requiredCulture: DancingCultureAsset,
    goldCost: 50,
    resourceCosts: [{ resource: WoodResource, amount: 3 }],
    additionalCulture: 2,
    uniqueUpgrade: true
}
```

### 3. UI Setup
1. Create an ImprovementUpgradeUI prefab in your scene
2. Assign UI references:
   - `upgradePanel`: Main panel GameObject
   - `upgradeButtonContainer`: Transform for upgrade buttons
   - `upgradeButtonPrefab`: Button template
   - `closeButton`: Close panel button

### 4. Testing
1. Build a Tent Camp improvement
2. Research the Dancing culture
3. Gather 3 Wood resources
4. Click on the Tent Camp
5. Select "Dance Hut" from the upgrade menu

### 5. Rehydration After Load
Rehydration of visual upgrade parts is now automatic: `ImprovementManager` subscribes to the game's `OnPlanetReady` event and will attempt to rehydrate (re-apply) saved upgrades for every tile on a planet once that planet finishes generation.

If you need to trigger rehydration manually (for example, when spawning improvements at runtime on a specific planet), use the planet-aware API:

ImprovementManager.Instance.RehydrateTileUpgrades(tileIndex, planetIndex);

You can also rehydrate an entire planet with:

ImprovementManager.Instance.RehydrateAllUpgradesOnPlanet(planetIndex);

Both methods will safely no-op if the runtime GameObject for the improvement is missing.

## Technical Notes

### Upgrade Persistence
Built upgrades are stored in `HexTileData.builtUpgrades` as a list of upgrade names.

### Resource System Integration
Uses existing `Civilization.GetResourceCount()` and `ConsumeResource()` methods.

### UI Integration
Integrates with existing UI systems via `FindObjectOfType<ImprovementUpgradeUI>()`.

## Future Enhancements
- [ ] Enable click handlers on improvement prefabs
- [ ] Add upgrade yield calculations to tile yield computation
- [ ] Visual feedback for upgraded improvements
- [ ] Upgrade chains (prerequisites between upgrades)
- [ ] Removal/downgrade functionality

## Notes
- The new modular approach favors attaching small prefabs to a base improvement prefab. Author attachable prefabs with neutral local transforms so they align when parented. If precise placement is needed, consider adding named attach points (empty transforms) on improvement prefabs and add code to find and parent attachments to those anchors.
- Use `replacePrefab` for upgrades that fundamentally change the object's shape (e.g., castle replaced by fortified castle) since this cleanly swaps the GameObject while preserving upgrade state.
