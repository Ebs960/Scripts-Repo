# Tactical Battle Completion Audit

## Local-board architecture (format version 2)

Tactical maps are now local axial hex boards contained within one strategic anchor. Planetary and deep-space builders create radius 3/4/5 boards (37/61/91 cells); every cell retains the anchor as provenance but uses `LocalQ`/`LocalR` as its spatial identity. Neighbors, range, path heuristics, line of sight, and board presentation consume those local coordinates. The validator rejects duplicate axial coordinates and maps spanning strategic tiles. Old active-battle previews are explicitly rejected rather than guessed during migration.

## Terrain

`BattleTerrainFeature` is authoritative gameplay state. Deterministic local clusters distinguish broad biome from forest, marsh, and rough ground. Roads and rivers form crossing corridors and their intersection becomes a bridge. Strategic flat terrain produces only level/depression cells, hills produce coherent high ground, and mountains produce peak/ridge structure. Presentation hashes include tactical identity so repeated anchor provenance does not duplicate every prop or surface variant.

## Deployment, withdrawal, and objectives

Deployment uses opposing bands in tactical axial space while leaving ordinary environmental decoration intact. Boundary metadata and strategic exit fields are serialized for withdrawal. Generic capture objectives have been retired: compatibility objectives are `Elimination` with cell `-1`, the preview describes rout/destruction, and standing on a former objective cannot resolve a battle. The max-round defender-held safeguard remains.

## Fortifications

Walls and gates can identify the pair of tactical cells whose shared edge they protect. Attacker pathfinding queries the crossed edge; an intact edge blocks crossing and a breached edge does not. Strongpoints retain cell identity. Edge endpoints are persisted in preview data.

## Preserved systems and follow-up

Occupancy, commands, turns, manual deployment, reinforcement groups, transports/carriers, underwater depth, commander multipliers, deterministic RNG, result application, and campaign consequences remain on the existing battle infrastructure. Tactical fuel/endurance was removed without removing carrier launch/recovery. Further authored environment prefabs and balance tuning can be supplied through existing profiles; gameplay features do not depend on decorative prefab availability.
