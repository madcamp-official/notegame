# Keyboard Wanderer PostgreSQL database

Production-oriented persistence for the authoritative generative TRPG server. It requires vanilla PostgreSQL 15+ and `pgcrypto`; no hosted-platform extension is required.

## Install

Apply migrations in lexical order, then the idempotent reference seed and transactional smoke test:

```bash
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/001_core_schema.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/002_row_security_and_views.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/003_campaign_director_state.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/004_codria_world_and_travel.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/migrations/005_generative_run_state.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/seeds/001_reference_catalogs.sql
psql -v ON_ERROR_STOP=1 "$DATABASE_URL" -f Database/tests/smoke.sql
```

The smoke script rolls back all rows. The migration role needs permission to create a schema and install `pgcrypto`.

## Files

- `001_core_schema.sql` — identities, campaigns/worlds, runs/entities, turns/events, facts/rumors/memories/quests, saves, LLM observability, and invariants.
- `002_row_security_and_views.sql` — owner RLS and client-safe run/turn views.
- `003_campaign_director_state.sql` — campaign beats, immutable semantic placement slots, an earlier director projection, and compensating-action ledger.
- `004_codria_world_and_travel.sql` — historical v4 filename; adds six-biome catalogs, immutable area/POI/route metadata, area-local coordinate validation, and append-only safe travel.
- `005_generative_run_state.sql` — sealed run-scoped generation plans and provenance, plan-to-slot bindings, generic progress/rule state, authoritative D20 resolutions, and deep-resume validation.
- `001_reference_catalogs.sql` — the enabled generative campaign template, six biomes, six generic roles, ten abilities, assets/items, entity kinds, endings, and generic events.
- `smoke.sql` — run-scoped world and plan, generic progress, authoritative D20 resolution, deep snapshot/resume audit, and world/slot immutability.

The files under `Database/migrations/` are the only executable schema authority and must be applied in lexical order. ERD exports or SQL files supplied separately (including files in a local Downloads folder) are logical-design references only; do not execute them over these migrations.

## Authority model

The Rule Engine owns rolls, legality, coordinates, paths, health/damage, focus, difficulty, rewards, story-beat evidence, and endings. A model may propose only bounded narrative-domain operations using IDs and slots supplied by the server. Before commit, proposals pass schema and semantic validation for:

- per-turn operation allowlist;
- existing entity/quest IDs;
- pre-generated slot ID and allowed asset ID;
- consequence budget;
- canonical-fact conflicts;
- affinity bounds and memory TTL;
- quest/hook convergence horizon.

Validated mechanics and narrative state are written in one authoritative turn transaction. Model timeout or invalid output yields a deterministic fallback with zero model operations, so a legal turn still commits. The model is called outside the database transaction and never receives database credentials or server-only `resolution_seed`.

## Data model

| Concern | Authoritative objects |
| --- | --- |
| Identity/ownership | `profiles`; indexed `owner_id` on tenant rows |
| Campaign catalog | `campaign_template_catalog`; the enabled template is scenario-agnostic and generative |
| Run generation plan | immutable `run_generation_plans` with seed, plan hash, model/fallback provenance, validation evidence, and canonical plan JSON |
| World scopes | at most one `campaign_preview` world per campaign; one immutable `run` world per preallocated run UUID |
| Sealed run world | `worlds`, `regions`, `areas`, `area_connections`, `world_area_descriptors`, `world_pois`, immutable `placement_slots` |
| Run mechanics | `runs.world_state`, `entities`, `actors`, `entity_positions`, inventories/items |
| Dynamic placement | `run_slot_bindings`; plan nodes move through or activate pre-generated slots without regenerating the map |
| Campaign progress | generic `run_progress_states`; scenario-defined node keys, convergence, ending candidates, progress, and Rule Engine state |
| Earlier schema projections | retained only as applied migration history; new runs never write or read them |
| Reversibility | `reversible_actions`; append/consume compensation snapshots |
| Lore detail | `world_events`, `world_facts`, `rumors`, `rumor_knowledge` |
| NPC/quest detail | `npc_memories`, `npc_relationships`, `quests`, `quest_objectives` |
| Turn/travel ledger | `turn_records`, authoritative `turn_rule_resolutions`, append-only `turn_events`/`turn_logs` and `safe_travels`, safe request/result views |
| Saves | `save_slots`, append-only checksummed `save_snapshots`, and append-only `resume_validation_records` |
| Model audit | append-only, redacted `llm_logs` |

`runs.world_state` is the complete replay/resume state. `run_progress_states` is the scenario-neutral, owner-scoped projection updated in the same transaction. New campaign logic never depends on the earlier fixed projection tables or vocabularies left in applied migration history.

## Generate once, move thereafter

At run start, the server preallocates the run UUID, generates one large deterministic `world_scope = 'run'` world with `run_scope_key` equal to that UUID, inserts the matching run, and seals its generation plan in the same transaction. The world contains all six terrain biomes, a validated area graph, POIs, and typed semantic placement slots. A deferred constraint permits world-before-run insertion but rejects an unpaired or mismatched run world at commit. Layout hash, generator metadata, dimensions, map/area geometry, routes, POIs, descriptors, and placement slots are immutable.

The separately sealed `run_generation_plans` row uses a validated seed/model composition to choose campaign beats, NPC roles, quests, hooks, and ending candidates and bind them to that run world's existing slots. Ordinary turns move or activate those bindings; the model never rebuilds the map turn by turn. A campaign may also have one `world_scope = 'campaign_preview'` row for selection UI only. Campaign selection queries must filter that scope (supported by the partial unique/index contract) instead of assuming every campaign has only one world row.

The HTTP API and `runs.world_state` always use global world coordinates. Normalized rows owned by an area use area-local coordinates: `area_connections.from_*` is local to `from_area_id`, `area_connections.to_*` is local to `to_area_id`, and `world_pois`, `placement_slots`, and `entity_positions` are local to `area_id`. Their SQL triggers therefore validate `0 <= x < area.width` and `0 <= y < area.height`. The server first resolves exact irregular area membership from immutable `world.areaMap`, then subtracts `areas.origin_x/origin_y` at the persistence boundary. `safe_travels` is an append-only cross-area audit ledger and intentionally retains global requested/committed endpoints and global path coordinates.

Ordinary turns change only run, entity, binding, and generic progress state. Even `Restore` and `Undo` append compensating events and never rewrite:

- world tiles, areas, exits, or placement slots;
- turn number or historical D20;
- canonical facts, NPC memories, or irreversible story events.

Deleting an owned profile/campaign is the reviewed lifecycle path and may cascade immutable children. Direct route, area-descriptor, POI, or placement-slot mutation is rejected.

Safe travel is a separate serializable command. It increments `runs.version`, synchronizes the player's global `world_state` position, area-local `entity_positions` row, active area, and generic progress/navigation state, and appends one `safe_travels` row, but it must leave `runs.current_turn` unchanged. The ledger retains requested and committed endpoints, path/cost, discovery, encounter, layout hash, and before/after campaign turn for audit and idempotent replay. A dangerous destination commits only a safe staging position and active encounter; the subsequent meaningful D20 action consumes the campaign turn.

## Turn transaction

1. Outside a transaction, normalize input, read the expected run, and deterministically preflight mechanics.
2. Outside a transaction, call the configured director profile with bounded context; retry once or use deterministic fallback.
3. Validate all proposed operations and form a `TurnCommitPlan`.
4. Begin a short `serializable` owner-scoped transaction and lock the run.
5. Return an existing record only when `(run_id, idempotency_key)` and request fingerprint match.
6. Verify `expected_run_version`, active status, and deterministic commit invariants.
7. Update the run by exactly one version/turn; synchronize entity position/state and `run_progress_states`.
8. Finalize `turn_records`, insert its immutable `turn_rule_resolutions`, update slot bindings, insert/consume `reversible_actions`, append `turn_events`, and add a redacted `llm_logs` row.
9. Commit and return the authoritative turn/run DTO.

No network call occurs while a run lock is held. A concurrent update returns `409` and must be retried with fresh state. A provider failure is already represented by fallback and does not roll back legal mechanics.

## Restore and Undo

`reversible_actions.inverse_ops` contains only server-produced allowlisted compensation operations.

- `Restore` may restore permitted recent entity removal or active-target damage/state fields. It consumes focus and marks the source snapshot consumed.
- `Undo` may compensate only the immediately preceding reversible turn. It never decrements `runs.current_turn` or changes the old `turn_records` row.
- Canonical facts, memories, model logs, map geometry, reward history, and roll history are not inverse operations.

The ledger stores source turn, ability, inverse operations, and consumed turn. A consumed snapshot cannot be replayed.

## Run generation and deep resume

At run creation, the server canonicalizes and hashes exactly one validated plan. `source`, `provider`, `model`, prompt identifiers, fallback state, and structured validation evidence remain attached to that immutable row; raw prompts, API keys, and `resolution_seed` do not. Invalid model output is logged and replaced by a deterministic fallback plan before any plan becomes authoritative.

A new-format snapshot records the exact world/layout hash, generation plan/hash, latest committed turn and event cursor, canonical state checksum, and projection/schema cursors. Resume code must recompute those values, append an accepted or rejected `resume_validation_records` row, and only hydrate the run after acceptance. Older rows remain readable as `snapshot_kind = 'legacy'`, but they do not satisfy the deep-resume contract.

## Owner context and RLS

Every request transaction sets the verified profile UUID locally:

```sql
begin;
select set_config('app.user_id', $1::text, true);
select * from keyboard_wanderer.run_summaries where id = $2;
commit;
```

Never use session-scoped `SET app.user_id` with a connection pool. Missing or malformed context fails closed. Policies use `FORCE ROW LEVEL SECURITY`; production traffic must not use a superuser or `BYPASSRLS` role.

RLS controls rows, not columns. `runs.resolution_seed` is server-only and is excluded from `run_summaries`, snapshots, logs, Unity responses, and model contexts. Unity must use the HTTP server rather than direct database access.

Provision least-privilege grants explicitly. In addition to schema/table/view privileges, the application role must receive `EXECUTE` on `keyboard_wanderer.current_app_user_id()` so RLS policies can evaluate its transaction-local owner context. The migrations grant nothing to `PUBLIC` and revoke access to safe views/catalogs until infrastructure assigns it.

## LLM observability and secrets

`llm_logs` stores model/profile metadata, prompt/schema version, hashes, status, fallback flag, usage/latency when available, and redacted input/output summaries. It must never contain:

- API keys or authorization headers;
- `resolution_seed`;
- raw system prompts;
- unredacted player PII;
- another owner's data.

The legacy `/v1/gm/narrate` endpoint is stateless and does not write authoritative run state. Full campaign play uses `/v1/runs/:id/turns`, whose validated operations are captured in the committed turn, Rule Engine resolution, slot bindings, and generic progress projection.
