# Keyboard Wanderer — product contract v3

## Product

Keyboard Wanderer is a 30–50 meaningful-turn fantasy adventure played across one sealed, explorable pixel world. A seed establishes the physical world; a schema-validated LLM plan gives existing places their run-specific people, tensions, clues, milestones, consequences, and ending context. The current player presentation is NinjaAdventure's `NinjaGreen`.

The game structure is fixed, but the scenario is not. A new seed must produce more than renamed copies of one plot.

The supplied screenshot is a UI-feel reference only. It can inform the dominant map, top status strip, right narrative/objective/D20 rail, bottom command deck, compact information rhythm, and warm pixel materials. Its world, characters, enemies, quests, economy, copy, and exact placement have no product authority.

## Preview and run creation

Campaign preview derives cheap metadata from the seed and makes no LLM request. Confirming a run performs one creation transaction:

1. Freeze seed, target turn count, world generator version, and campaign planner version.
2. Generate a default 160×160 tile world with six biome families, areas, routes, alternate paths, POIs, encounter slots, and finale candidates.
3. Assign six generic campaign roles to compatible, reachable areas.
4. Give the planner only valid IDs and spatial constraints.
5. Validate schema, references, role coverage, three milestones, recovery paths, turn budget, and finale bindings.
6. Repair deterministically or retry once within a bound.
7. Persist the sealed world, campaign plan, and `layoutHash` atomically.

Ordinary turns never regenerate or relocate tiles, biome boundaries, areas, fixed POIs, or routes. Only validated sparse state can change.

## Six biomes

Every world contains all six `BiomeDescriptor` values:

1. `temperate_forest_field`
2. `river_wetland`
3. `arid_desert`
4. `frost_highland`
5. `subterranean_cavern`
6. `ancient_ruins`

Biomes own environment art, movement costs, palette, and terrain rules. They are not story stages.

## Six campaign roles

Every run assigns these separate narrative functions:

1. `ARRIVAL_CATALYST` — the event that gives the player a reason to intervene.
2. `LOCAL_STAKES` — an immediate regional problem with visible failure costs.
3. `RELATIONSHIP_CONFLICT` — incompatible goals between recurring people or groups.
4. `HIDDEN_TRUTH` — evidence that changes the interpretation of prior events.
5. `CONSEQUENCE_RETURN` — earlier promises, sacrifices, shortcuts, or debt returning.
6. `FINAL_CONVERGENCE` — key people, evidence, and milestones meeting in a final spatial decision.

Names, inhabitants, antagonists, goals, causes, and legal solutions come from the run plan. Roles may be mapped to different biomes and positions per seed.

## Three milestones

Authoritative progression uses `MILESTONE_TOKEN_1`, `MILESTONE_TOKEN_2`, and `MILESTONE_TOKEN_3`. The plan supplies each token's display name, meaning, evidence conditions, eligible role, and at least two recovery routes. Narration alone cannot grant a token. The final role remains gated until all required rule conditions are satisfied.

## Travel and turns

Safe movement over known passable tiles and routes is exploration travel. It can advance travel time and discovery, but consumes neither a D20 nor a meaningful turn. A dangerous route may open an encounter. Combat, investigation, negotiation, defense, puzzle resolution, recovery, and committed keyboard placement are meaningful turns. Tactical `Move` inside an encounter is distinct from world travel.

The six primary commands are Move, Copy, Delete, Connect, Restore, and Undo. Attack, Interact/Investigate, Negotiate, and Rest are subordinate contextual actions. The Rule Engine owns legality, paths, occupancy, D20, modifiers, damage, resources, milestone progression, accumulated metrics, and endings. Undo appends compensation; it does not rewind immutable geometry, rolls, turn numbers, or irreversible facts.

## Constrained Gemini use

The server uses `GEMINI_API_KEY`, defaults to `gemini-2.5-flash-lite`, sets thinking budget to zero, keeps contexts and outputs small, retries at most once, and always has a deterministic fallback.

At run creation Gemini may propose a campaign title, description, tone, and beats/NPCs/quests/endings/area flavors that reference supplied IDs. It may define milestone meaning and propose finale bindings. During play it may propose narration, dialogue, reactions, memory candidates, rumors, hooks, and visual-intent tags for supplied entities.

It may not create coordinates, tiles, biome boundaries, exits, routes, rolls, damage, resources, milestones, metrics, endings, unknown IDs, or asset paths. AssetResolver maps validated visual intent to the NinjaAdventure manifest.

## Generic finale

The final spatial puzzle binds actual run entities to a subset of these semantic components:

- `anchor`: what holds the present world together
- `safeguard`: what limits destructive outcomes
- `memory`: a record, relationship, or testimony worth preserving
- `freedom`: a person, group, or force seeking autonomy
- `threat`: unresolved danger
- `passage`: escape, migration, transition, or return
- `witness`: who remembers and carries the result forward

A versioned recipe DSL evaluates placement, connection, protection/removal, milestone state, and accumulated metrics. The Rule Engine selects a legal ending ID; Gemini writes only the run-specific epilogue inside that result. A stored deterministic recipe completes the run if generation or narration is unavailable, and the hard cap always yields a valid terminal state.

## Persistence and done criteria

Persist seed and versions, `layoutHash`, base world plus sparse overrides, role assignments, campaign plan, milestone evidence, player and NPC state, facts and unresolved threads, travel and meaningful-turn events, complete D20/intent records, provider metadata, and finale bindings. Resume verifies the base hash before applying sparse state.

The v3 contract is complete when:

- the same seed/version reproduces the same sealed 160×160 layout;
- all six biomes and six independent roles are present and required locations are reachable;
- three milestones have separated evidence and recovery paths;
- safe travel consumes no meaningful turn while each valid encounter action commits exactly one idempotent turn;
- different seeds materially vary people, conflicts, secrets, milestone meanings, and ending context;
- provider failure cannot block rules, save/resume, or a hard-cap ending;
- `NinjaGreen` and environmental sprites resolve through the NinjaAdventure manifest;
- the UI carries only the reference image's hierarchy and material feel, never its content; and
- Unity, server, PostgreSQL, tests, and documentation describe the same contract.
