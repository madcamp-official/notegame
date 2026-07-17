# Keyboard Wanderer — UI implementation handoff v3

Read in this order:

1. `SOURCE_OF_TRUTH.md`
2. `PRODUCT_CONCEPT_KO.md`
3. `DESIGN.md`
4. `design-contract.md`

## Build constraints

- Desktop Unity, Korean-first, 16:9 baseline; world viewport uses at least 60% of usable width.
- Use the warm wood/metal HUD tokens and integer pixel ramps from `DESIGN.md`; no gradients, blur, or glass.
- Treat the reference image as layout/material mood only. Do not use any of its scenario content.
- Render a camera over the sealed 160×160 world plus a local tactical encounter. Never squeeze the entire world into an encounter-sized board.
- Keep the six biome descriptors separate from the six seed-assigned campaign roles.
- Top: place/biome, phase, meaningful turn/limit, `마일스톤 0/3`, compact metrics.
- Right: authoritative result, optional narration, current generic objective, D20 and normalized attempt.
- Bottom: Move, Copy, Delete, Connect, Restore, Undo; subordinate context actions; target/destination; free-form intent; commit.
- Resolve the protagonist as NinjaAdventure `NinjaGreen` and all environment sprites through the manifest. Pixel-snap tiles, sprites, and icons.
- Distinguish safe travel with an explicit no-turn state from meaningful D20 actions.
- Finale UI must accept run-specific `anchor`, `safeguard`, `memory`, `freedom`, `threat`, `passage`, and `witness` bindings instead of hard-coded objects.

## First playable proof

The first artifact must show all of the following in one coherent run:

1. One sealed generated world containing all six visibly distinct biome descriptors.
2. Six generic campaign roles assigned to reachable areas without using role names as biome names.
3. A route selected between two POIs, including safe/risky travel differences.
4. A local encounter with exact occupancy and valid placement feedback.
5. All six primary keyboard commands plus subordinate contextual actions.
6. Three milestone slots with at least locked, lead-known, and earned visual states.
7. One complete intent result showing original intent, normalized legal attempt, D20, modifiers, difficulty, outcome, and divergence.
8. `NinjaGreen` as the player sprite.
9. A finale preview or test fixture that renders generic component bindings without fixed scenario props.
10. No world, character, enemy, objective, copy, or level arrangement taken from the reference image.

## Acceptance checks

- Changing only the seed materially changes campaign title, inhabitants, conflict, secret, milestone meanings, and ending context while preserving the UI vocabulary.
- Ordinary turns, Restore, Undo, save, and resume leave `layoutHash` unchanged.
- Provider failure falls back cleanly without hiding the rule result or blocking completion.
- Long Korean generated names truncate or wrap without overlapping the map or commit controls.
- At every supported zoom, `NinjaGreen`, terrain, selection outlines, and occupancy remain pixel-aligned.
