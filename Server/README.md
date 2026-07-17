# Keyboard Wanderer authoritative server — v3

This service runs a seed-composed, 30–50 meaningful-turn keyboard-fantasy TRPG roguelike. Every run owns a newly generated campaign genome and one sealed world. There is no fixed kingdom, named protagonist, authority ladder, or predetermined finale scenario.

The server owns all rules: world generation, coordinates, paths, occupancy, D20, difficulty, damage, focus, metrics, milestone evidence, ending recipes, idempotency, persistence, and convergence. Gemini is a low-cost constrained proposal engine. It never becomes a rules engine.

## Quick start

Node.js 20.11 or newer is required.

```bash
cd Server
npm install
npm test
npm start
```

Memory storage and deterministic model fallbacks are enabled by default. PostgreSQL and Gemini are optional for local development.

```bash
cp .env.example .env
node --env-file=.env src/index.js
```

Never commit `.env`. `GEMINI_API_KEY` is server-only, sent in the `x-goog-api-key` header, and excluded from URLs, prompts, logs, database plan JSON, health responses, and Unity DTOs.

## Campaign preview versus run creation

`POST /v1/campaigns` creates a deterministic preview using `generative-keyboard-fantasy`. It does not call Gemini. The preview gives selection UI a reproducible title, premise, genome, quest seeds, ending subset, world, and content hash.

`POST /v1/campaigns/:id/runs` creates the playable content:

1. choose or validate a run `worldSeed` and 30–50 `turnLimit`;
2. generate and validate exactly one complete world;
3. create a deterministic campaign blueprint from the same seed;
4. expose a read-only world summary to `CAMPAIGN_PLAN`;
5. accept only validated flavor fields keyed by immutable IDs, or use deterministic fallback;
6. create the run and seal its world, generation plan, hashes, entities, and progress state.

The optional `themeHint` is bounded untrusted text. It may influence flavor but cannot widen the output schema or change rules.

## Campaign genome

The deterministic blueprint composes a world name, title, motif, crisis, hidden cause, community, dilemma, companion temperament, NPC display content, three quest seeds, beat copy, token display meaning, and an ending subset. Same seed/version/turn limit gives the same blueprint and content hash; different seeds vary multiple narrative dimensions.

The six invariant campaign roles are:

1. `ARRIVAL_CATALYST`
2. `LOCAL_STAKES`
3. `RELATIONSHIP_CONFLICT`
4. `HIDDEN_TRUTH`
5. `CONSEQUENCE_RETURN`
6. `FINAL_CONVERGENCE`

Roles are narrative functions, not terrain types. A seed assigns each role once to a generated area and compatible semantic slots.

Progress uses stable server IDs with seed-specific display names and meanings:

- `MILESTONE_TOKEN_1`
- `MILESTONE_TOKEN_2`
- `MILESTONE_TOKEN_3`

Every beat requires a successful or partially successful allowed action, the correct role area, and designated evidence. A deadline does not auto-complete a beat. Unresolved beats become explicit convergence costs at the hard limit.

## One sealed world

The production world is 160×160, contains 12 areas, and includes all six terrain biomes exactly twice as area anchors:

- `temperate_forest_field`
- `river_wetland`
- `arid_desert`
- `frost_highland`
- `subterranean_cavern`
- `ancient_ruins`

World generation is logical-first:

1. build the six-node progression DAG and three-milestone finale gate;
2. place twelve constrained area anchors and assign all six biomes;
3. connect anchors with a minimum spanning tree plus seeded alternate routes;
4. rasterize clustered terrain, multi-tile roads, clearings, POIs, and semantic slots;
5. validate staged reachability, clearance, uniqueness, campaign candidates, and finale locking;
6. apply deterministic local repairs, seal the generation report, and hash every immutable layer.

Turns mutate only run/entity/narrative state. They never rebuild tiles, biome boundaries, areas, routes, exits, POIs, or placement slots.

Generate an inspectable JSON world and SVG preview, or validate a seed batch:

```bash
npm run world:generate -- --seed 20260717 --output ./generated/world-20260717
npm run world:generate -- --seed 100 --count 20 --output ./generated/batch
npm run world:validate
```

For multi-world output, the CLI writes `world-<seed>.json` and `world-<seed>.svg` under the selected directory. Finale POIs are highlighted by the generic `FINAL_CONVERGENCE` role.

## Abilities, travel, and turns

The six product abilities are `move`, `copy`, `delete`, `connect`, `restore`, and `undo`. Attack, Interact/Investigate, Negotiate, and Rest are contextual actions.

- Safe travel uses `/v1/runs/:id/travel`; it increments run version and travel state without a D20 or campaign turn.
- Entering danger commits only a safe staging point and opens an encounter.
- A local encounter action validates targets and paths, resolves one D20, and consumes exactly one meaningful turn.
- `restore` replays a permitted recent removal or damage snapshot.
- `undo` appends compensation for the previous reversible result.
- Neither operation rewinds turn numbers, rolls, immutable geometry, canonical facts, memories, or irreversible events.

The six accumulated metrics are `worldStability`, `worldAutonomy`, `publicTrust`, `technicalDebt`, `companionBond`, and `turnPressure`. Only the Rule Engine changes them.

## Generic finale recipes

Each seed selects four server-defined ending candidates plus `ENDING_EMERGENCY_WITHDRAWAL`. Candidate text varies; IDs and mechanics do not. Recipes use this DSL:

```json
{
  "requiredLinks": [["anchor", "safeguard"]],
  "requiredRemoved": ["threat"],
  "requiredActive": ["memory", "witness"],
  "forbiddenLinks": [["player", "freedom"]],
  "metricConditions": {
    "publicTrust": { "min": 45 },
    "technicalDebt": { "max": 65 }
  }
}
```

The sealed finale cluster contains `anchor`, `safeguard`, `memory`, `freedom`, `threat`, `passage`, and `witness`, plus the player. The Rule Engine evaluates connections, removal/active state, and metrics. The model can rewrite only validated titles/descriptions; it cannot add candidates or alter a recipe.

## `CAMPAIGN_PLAN` boundary

The planner receives no tile array or coordinates. Its read-only context includes:

- seed, turn limit, optional untrusted theme hint;
- immutable beat/NPC/quest/ending/area IDs;
- deterministic genome summary;
- layout hash, dimensions, six biome descriptors, area-to-biome IDs, and capacity counts;
- an explicit allowlist and forbidden-content list.

The only valid response shape is:

```json
{
  "campaign": {
    "title": "...",
    "description": "...",
    "tone": ["..."]
  },
  "beats": [{ "id": "existing-id", "title": "...", "description": "..." }],
  "npcs": [{ "id": "existing-id", "title": "display name", "description": "flavor" }],
  "quests": [{ "id": "existing-id", "title": "...", "description": "..." }],
  "endings": [{ "id": "existing-id", "title": "...", "description": "..." }],
  "areaFlavors": [{ "areaId": "existing-id", "flavor": "..." }]
}
```

Unknown fields, IDs, mechanics, coordinates, slots, asset paths, numeric rewards, and recipe language reject the entire proposal. The deterministic blueprint remains playable.

## Turn narration boundary

A meaningful turn is mechanically preflighted before the model call. The bounded context includes only relevant IDs, facts, loops, quests, relationships, allowed scene operations, and consequence budget. After the call, schema and semantic validation run again while building the commit plan.

Allowed proposals can add bounded narrative facts, rumors, memories, relationship changes, short quest hooks, visual intent, or a scene entity binding to one supplied ambient slot and one asset already allowed by that slot. The model cannot choose raw coordinates, arbitrary assets, mechanics, rewards, progress, or endings.

## Gemini cost profile

Defaults use the lightweight profile:

```dotenv
GEMINI_FAST_MODEL=gemini-2.5-flash-lite
GEMINI_FAST_OUTPUT_TOKENS=384
GEMINI_QUALITY_MODEL=gemini-2.5-flash-lite
GEMINI_QUALITY_OUTPUT_TOKENS=640
```

Campaign preview uses zero model calls. Run planning uses the fast model, 384 output tokens by default, one JSON candidate, `thinkingBudget: 0`, and at most one retry. Ordinary turn narration uses the same bounded settings. Any timeout, transport failure, invalid JSON, or invalid semantics returns deterministic fallback.

## REST API

All endpoints except `/health` use the authenticated UUID in `x-user-id`. `AUTH_MODE=local` supplies `DEFAULT_USER_ID` for development.

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Process, storage, authority, and model profile health |
| `POST` | `/v1/campaigns` | Create deterministic campaign preview |
| `GET` | `/v1/campaigns` | List owned campaign previews |
| `GET` | `/v1/campaigns/:id` | Get one preview and its world |
| `POST` | `/v1/campaigns/:id/runs` | Generate and seal a playable run |
| `GET` | `/v1/runs/:id` | Get current authoritative run DTO |
| `POST` | `/v1/runs/:id/travel` | Perform safe exploration travel |
| `POST` | `/v1/runs/:id/turns` | Resolve and commit one meaningful turn |
| `GET` | `/v1/runs/:id/turns` | List committed turns |
| `GET` | `/v1/runs/:id/turns/:turnNo` | Get one committed turn |
| `POST` | `/v1/runs/:id/abandon` | Abandon at an expected version |
| `POST` | `/v1/runs/:id/resume` | Resume at an expected version |
| `POST` | `/v1/gm/narrate` | Stateless compatibility narration; never mutates a run |

Create a preview:

```json
{
  "title": "선택적 표시 제목",
  "worldSeed": 20260717,
  "turnLimit": 40,
  "archetype": "generative-keyboard-fantasy"
}
```

Create a run:

```json
{
  "worldSeed": 20260718,
  "turnLimit": 40,
  "themeHint": "유리비와 오래된 약속"
}
```

A normal turn preserves free-form intent while selecting explicit mechanical scope:

```json
{
  "idempotencyKey": "unity-turn-0001",
  "expectedRunVersion": 1,
  "ability": "connect",
  "targetEntityId": "UUID",
  "secondaryTargetEntityId": "UUID",
  "destination": null,
  "intent": "두 증언이 공유하는 약속을 잠시 연결해 확인한다"
}
```

Identical key/payload replay returns the original response. Reusing a key with different input or submitting a stale version returns `409`.

## Commit workflow

1. Normalize action, targets, destination, and natural-language intent; hash the request.
2. Read the expected run and deterministically preflight legality, D20, difficulty, normalized attempt, consequence budget, and before/after state hashes.
3. Call the configured narrator outside a database transaction.
4. Validate JSON, IDs, assets, slots, canonical facts, convergence horizon, and budget.
5. Open a short owner-scoped serializable transaction, lock the run, and recheck idempotency/version.
6. Force the preflight roll and atomically persist mechanics, validated narrative state, generic progress, event ledger, reversibility data, and turn record.
7. Return the authoritative run/turn DTO.

Provider failure does not reject a legal turn. Concurrent state change returns `409` and must be retried from fresh state.

## PostgreSQL

Apply the files in [`../Database/migrations`](../Database/migrations) in lexical order, then configure:

```dotenv
STORAGE=postgres
DATABASE_URL=postgresql://keyboard_wanderer:change-me@127.0.0.1:5432/keyboard_wanderer
DATABASE_SSL=false
```

The adapter persists one optional campaign-preview world and one run-scoped world per run, immutable generation plans, generic progress state, RLS owner context, idempotent travel/turn ledgers, complete `world_state`, normalized positions, reversible actions, and deep-resume evidence. Unity calls this HTTP API and never connects directly to PostgreSQL.

## Tests

```bash
npm test
TEST_DATABASE_URL=postgresql://... npm test
```

The suite covers seed determinism/diversity, six biomes and six roles, 30/40/50-turn convergence, immutable world layers, staged finale reachability, semantic slot clearance, run-per-world generation, natural-language ability inference, legal/illegal ability paths, Restore/Undo, ending recipes, invalid model fallback, retry/token/thinking settings, ownership, idempotency, optimistic versions, and the optional PostgreSQL adapter.
