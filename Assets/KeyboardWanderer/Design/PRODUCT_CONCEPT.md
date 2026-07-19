# Ninja Adventure — product contract v4

## Product identity

`Ninja Adventure` is a map-first pixel adventure set in Codria (`WORLD_CODRIA`). Nupjukyi (`PROTAGONIST_NUPJUKYI`) awakens the Administrator Keyboard (`ARTIFACT_ADMIN_KEYBOARD`), investigates a systemic collapse, recovers three administrator-access levels, and decides the final deployment inside the Root System.

NinjaAdventure's `NinjaGreen` is the current temporary visual asset. It does not rename, replace, or define Nupjukyi.

The supplied image is a UI-feel reference only: it supports a dominant map, compact top status, right information rail, bottom command deck, dense rhythm, and warm pixel materials. Its world, character, enemies, quests, labels, icons, and level arrangement have no product authority.

## Sealed world

A run generates one deterministic 160×160 world, validates reachability and required content, and seals it with a `layoutHash`. Ordinary turns, save/resume, Restore, and Undo never regenerate or relocate tiles, area boundaries, routes, POIs, or placement slots. Runtime play activates existing slots and moves entities through the sealed graph.

Codria has six fixed region axes:

1. `REGION_BUG_FOREST`
2. `REGION_BUFFER_VILLAGE`
3. `REGION_DEADLOCK_CITY`
4. `REGION_DATA_GRAND_LIBRARY`
5. `REGION_LEGACY_CITADEL`
6. `REGION_ROOT_SYSTEM`

Region axes describe canonical place identity and story function. The six physical biomes describe terrain, palette, traversal, and environment rules:

1. `temperate_forest_field`
2. `river_wetland`
3. `arid_desert`
4. `frost_highland`
5. `subterranean_cavern`
6. `ancient_ruins`

Every world contains all six axes and all six physical biomes, but they remain independent dimensions. A seed may bind axes to compatible areas; it must not turn them into biome aliases or a fixed one-to-one stage order.

## Authoritative campaign shape

The macro progression has exactly nine beats:

1. Arrival and Administrator Keyboard awakening
2. First collapse problem
3. Administrator Access I
4. Administrator Access II
5. Discovery that the internal administrator-control system caused the collapse
6. Technical-debt backflow
7. Administrator Access III
8. Root System entry
9. Final deployment and ending

Authoritative progression uses exactly `ADMIN_ACCESS_LEVEL_1`, `ADMIN_ACCESS_LEVEL_2`, and `ADMIN_ACCESS_LEVEL_3`. Each level must have at least two acquisition candidates located in different areas and resolved through different action contexts. A single failed candidate cannot make completion impossible.

The Root gate requires all three access levels and the essential root-cause clue. Narration, a lucky roll, or a model proposal cannot bypass this gate.

## Actions and turns

The client submits only:

- `MOVE` with a destination. Safe travel changes navigation/discovery without D20 or campaign-turn consumption. Dangerous travel can stage the player and activate an encounter; the later meaningful action consumes the turn.
- `USE_SKILL` with one of `COPY`, `DELETE`, `CONNECT`, `RESTORE`, or `UNDO`, plus validated targets. The server classifies it as `COMBAT`, `INVESTIGATION`, `NEGOTIATION`, or `DEPLOYMENT` and commits exactly one meaningful turn.

Attack, Interact, Negotiate, and Rest are not public skills or new input types. `playerNote` is optional flavor context. It cannot provide coordinates, make an illegal target legal, change a roll, grant access, erase debt, or choose an ending.

The Rule Engine owns legality, paths, occupancy, D20, modifiers, damage, resources, facts, access, progression, debt changes, and endings. Undo appends a compensating event; it does not rewind turn numbers, rolls, immutable facts, or world geometry.

## Persistent consequence

Save and resume preserve the sealed-world identity and the histories that make later callbacks deterministic:

- `majorChoices`
- `regionOutcomes`
- `npcRelationships`
- `canonicalFacts`
- `unresolvedHooks`
- `abilityUsageHistory`
- `adminAccessAcquisitionHistory`
- `technicalDebtEntries`

Technical debt is both a metric and a causal ledger. Each entry identifies its originating turn, operation, target, delta, deferred consequence, and resolution provenance. Normal success never silently reduces prior debt. Only an explicit recovery, acceptance of responsibility, resource payment, or NPC-cooperation action can resolve it.

## Gemini boundary and cost profile

Gemini is non-authoritative. It receives bounded IDs and state, returns small structured proposals or short narration, is schema/semantics validated, retries at most once, and falls back deterministically. It may vary scene phrasing, dialogue, reactions, and epilogue details that agree with committed facts. It may not create geometry, rolls, rewards, access levels, canonical facts, debt resolution, or an ending ID.

The default cost profile is `gemini-2.5-flash-lite`, thinking budget 0, minimal context, small output, one bounded retry, and deterministic fallback. `GEMINI_API_KEY` exists only in the server environment.

The Rule Engine selects `endingId` from committed state. Gemini writes only an epilogue inside that result.

## UI acceptance

- The sealed world is dominant; the interface never implies that the model builds a new map each turn.
- One primary objective, at most two secondary objectives, and two or three recommended actions are visible.
- MOVE and the five skills are explicit; unavailable skills remain disabled with a reason.
- Confirmation shows target, skill, turn consumption, and risk before commit.
- Results appear as judgement, state changes, then two to four sentences of narration.
- Optional `playerNote` never blocks an otherwise valid action.
- Administrator access, essential clue, and causal technical debt remain inspectable.
- The reference screenshot contributes interaction feel only, never product content.
