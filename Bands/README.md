# Band content migration

`Units/Paleolithic Units/Band.asset` is retained as legacy `WorkerUnitData` so old serialized references do not become missing objects. New games use `CivilizationManager.startingBandData` when it is assigned; `pioneerData` is now only the normal settler and compatibility fallback.

`Improvements/Camp.asset` and `Improvements/Tent Camp.asset` are also retained. They remain permanent campsite/waystation improvements, but are not created by `Band` and are not its internal progression. Use **Tools > Campaign > Migrate Paleolithic Band Data** to create a `BandData`, then create `BandStructureData` assets for Foraging Tent, Story Circle, Burial Pit, Stone Pile, Tool Maker, and Fishing Tent after copying the project-specific costs, icons, yields, technology, and culture references from the current Camp upgrade content.

Inspector setup still required: assign a prefab containing `Band`, packed/encamped visual children, allowed structures/recruits, actual starting garrison entries, and the resulting data to `CivilizationManager.startingBandData`. This explicit step avoids destructive automated guesses about existing asset GUIDs.
