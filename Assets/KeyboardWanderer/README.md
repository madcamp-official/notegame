# Keyboard Wanderer vertical slice

This folder contains the first playable Unity implementation derived from the Notion v1.0 documents.

## Run it

1. Open `Assets/Scenes/SampleScene.unity`.
2. Enter Play Mode. The demo bootstraps itself without modifying the scene.
3. Click a tile or entity, select **Move**, **Copy**, or **Delete**, enter an intent, and commit the turn.

The map, actors, props, D20, icons, and pixel font are sourced through `NinjaAdventureAssetManifest.asset`. Rebuild the manifest from **Keyboard Wanderer > Rebuild Ninja Adventure Manifest** if source assets move.

## Implemented specification slices

- Deterministic region generation with a versioned layout hash and guaranteed entry-to-exit path (`MAP-001`–`MAP-004`).
- Integer grid coordinates, dense base tiles, A* pathfinding, and a region/layer-aware occupancy index (`MOVE-001`–`MOVE-005`).
- Server-style D20 resolution, consequence budgets, and three MVP abilities: Move, Copy, Delete (`TURN-005`–`TURN-007`, `ABL-002`–`ABL-004`).
- Clone-before-commit state mutation, run version checks, and idempotency-key replay/conflict behavior (`TURN-001`, `RUN-009`, `TURN-012`).
- Deterministic narrative fallback and campaign convergence gates at 10/5/3/1 remaining turns (`LLM-010`, `CAMP-005`, `TURN-016`).
- Ninja Adventure asset manifest as a replaceable presentation adapter; core game asset IDs remain pack-agnostic.

## Verification

`KeyboardWanderer.Tests.EditMode` covers deterministic layouts, path connectivity, coordinate packing, spatial rollback, idempotent commit, protected-entity rejection, and campaign convergence.

This is an in-process authoritative simulation for the vertical slice. The production follow-up is to replace `LocalTurnService` with the documented HTTP/PostgreSQL/LLM adapters while keeping the Unity-facing contracts.
