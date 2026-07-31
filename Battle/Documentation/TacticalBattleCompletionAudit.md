# Tactical Battle Completion Audit

## Baseline

- Audit date: 2026-07-31
- Unity version: 6000.4.9f1
- Repository metadata: local Git metadata has no commits or remotes. Branch, pull, commit, and PR requirements cannot yet be verified or completed from this workspace.
- Compilation baseline: Unity batch mode exited before import or script compilation. No compiler diagnostics were emitted; compile status is **Unverified**.
- Existing battle tests: `BattlePathfinderTests`, `BattleMapValidatorTests`, `BattleLosTests`, and `BattleCommandExecutorTests` exist. Unity Test Runner execution is still required.

## Requirement Status

| Area | Status | Verified finding |
| --- | --- | --- |
| Campaign death cleanup and result placement | Partially implemented | `BattleResultApplier` invokes `KillFromBattle`, but survivor outcomes default to each unit's starting tile and no battle-result placement policy is applied. |
| Theater routing and commitment | Partially implemented | `BattleTheaterResolver` identifies the three theaters and commitment prevents duplicate runtime IDs, but context is not propagated from all campaign attack paths and commitments do not track formation, carrier/transport, or commander identity. |
| Stable formations | Missing | No stable tactical formation identity was found in the battle module. |
| Turn, round, ZOC, objective, wait, retreat | Broken | Land ZOC is treated as an impassable 99-cost path step; objective capture is tested during side turns rather than round end; wait has no delayed queue; retreat accepts adjacent cells rather than designated exits. |
| Deterministic authority and replay | Partially implemented | Sessions contain a seed and map generation uses `System.Random`, but combat derives ad-hoc seeds and no battle-owned RNG/replay snapshot exists. |
| Reinforcement eligibility and entries | Broken | Round deployment falls back to the first compatible deployment cell and has no strategic route, fuel, readiness, or deep-space collection validation. |
| Manual lifecycle and preview choices | Partially implemented | Player-involved attacks remain in preview; runtime controls invoke Manual, Auto-Resolve, Retreat, and Cancel. Pre-battle retreat validates shared campaign placement. Deployment uses current auto-deployment and lacks board placement controls. |
| HUD, input, presentation, result UI | Partially implemented | Runtime HUD selects tactical units/targets and submits authority commands. Presenter renders tactical snapshot state without moving campaign objects. Result screen retains campaign interaction until Continue. Board-click input, animated tactical views, camera, and rich result details remain missing. |
| Planetary joint mechanics | Partially implemented | Domains are represented and planetary maps expose land, naval, air, and orbit cells. Environment/objective generation and combined-arms behavior are incomplete. |
| Underwater mechanics | Partially implemented | Theater/domain gating and detection types exist, but detection is not updated in turn flow and depth/stealth play is not implemented. |
| Deep-space mechanics | Partially implemented | A space-grid map builder exists, but participant/reinforcement collection and full objectives are incomplete. |
| Transport and amphibious operations | Scaffolding only | Command enum values exist, but executor support is absent. |
| Carrier and aircraft operations | Scaffolding only | Command enum values exist, but tactical launch/recovery implementation is absent. |
| Weapon-aware targeting | Partially implemented | Targeting remains unit/domain based and attack commands do not select equipment weapons. |
| Commanders and hierarchy | Missing | No battle commander assignment or character integration was found. |
| Tactical AI | Broken | The evaluator selects one attack, one-cell move, or defend per unit; it has no activation sequencing, theater planning, or hidden-information policy. |
| Active battle save/load | Broken | `CaptureStateJson()` returns `{}` and restore silently clears active battle state. |
| Automated and runtime validation | Unverified | No successful Unity compilation, EditMode execution, PlayMode execution, or runtime scenario evidence is available. |

## Progress Log

- 2026-07-31: Baseline established. First implementation slice targets turn ordering and land ZOC semantics because both defects have direct authority-layer tests.
- 2026-07-31: Added runtime preview choices, pre-battle withdrawal, manual tactical HUD, human/AI side progression, snapshot presenter, and Continue-gated result UI. Unity runtime validation remains blocked by an open editor session.