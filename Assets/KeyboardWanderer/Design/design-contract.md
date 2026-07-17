# Keyboard Wanderer — reference design contract v3

## Target

- Artifact: PC-first Unity gameplay screen for Keyboard Wanderer
- Audience: Korean players seeking a compact, replayable 30–50 meaningful-turn generative adventure in a large pixel world
- Product boundary: current product documents define game content; the supplied image defines only interface feeling
- Current player art: NinjaAdventure `NinjaGreen`

## Evidence

| Evidence | Confidence | What it establishes |
| --- | --- | --- |
| User-provided 1680×945 image | observed | Dense 16:9 HUD, dominant map, top status, right information rail, bottom action deck, warm pixel material |
| User clarification about the image | explicit | No scenario, biome, enemy, quest, character, copy, or worldbuilding may be inferred from it |
| Linked Notion product collection and latest owner corrections | authoritative | Seed + validated LLM campaigns, 30–50 meaningful turns, one sealed large world, six biomes, generic progression |
| NinjaAdventure pack in the workspace | observed | Top-down fantasy environment vocabulary and the current `NinjaGreen` protagonist |
| Server and Unity contracts | implemented | Six generic roles, three milestone tokens, generic finale components, authoritative travel/turn separation |

## Keep, reinterpret, exclude

| Keep as interface feel | Reinterpret for this project | Exclude completely |
| --- | --- | --- |
| Dominant center-left map | A camera over the sealed generated world and local encounter | Image's village and exact object placement |
| Compact top status | Place/biome, phase, meaningful turn, milestone `0/3`, metrics | Image's title, currencies, labels, icons |
| Right log/objective/D20 rhythm | Rule result, optional narration, generic role objective, normalized attempt | Image's encounter log and collection quest |
| Bottom action deck | Six keyboard commands, context actions, target, intent, commit | Image's navigation labels and icon meanings |
| Warm wood/metal pixels | A neutral fantasy command artifact supporting every biome | Literal frame pixels, logo, measured composition |
| High information density | Readable Korean type and 44 px interactions | Tiny text and screenshot-perfect scaling |

## Design stance

The stable HUD surrounds a world whose palette, inhabitants, conflict, secrets, and objectives vary by seed. It exposes rule authority—legal target, route, cost, D20, normalized intent, milestone evidence, and persistent consequence—without resembling a developer dashboard. Generated narration is visibly supplemental to the committed result.

The screenshot never supplies content. A reviewer should be able to replace it with a different map-first fantasy HUD reference without changing the game's scenario or data model.

## Risks

- Korean pixel-font licensing and complete glyph coverage require a release gate.
- Frost/highland art may need palette-safe reuse and restrained VFX because dedicated snow coverage is limited.
- A 160×160 world needs zoom, minimap, visibility culling, and detail levels; fitting every tile into one tactical board is unusable.
- Generic generated objectives need fixed UI labels and predictable truncation so unfamiliar proper nouns do not break panels.
- The six biome descriptors and six campaign roles must remain separate even when a seed maps one role to one biome.

## Quality gate

- [ ] Only hierarchy, density, and material cues survive from the reference image.
- [ ] `NinjaGreen` is the visible protagonist and resolves through the NinjaAdventure manifest.
- [ ] All six biome families are distinguishable in world data and the map.
- [ ] The sealed world, a selected travel route, and a local encounter are inspectable.
- [ ] Three `MilestoneToken` states and the current generic objective are always discoverable.
- [ ] Safe exploration visibly differs from a D20 meaningful action.
- [ ] Move, Copy, Delete, Connect, Restore, and Undo remain the primary command tier.
- [ ] Free-form intent, normalized attempt, roll breakdown, and divergence explanation fit without debug UI.
- [ ] Finale UI can display run-specific component bindings without assuming fixed props or a fixed ending.
- [ ] Sprites remain pixel-snapped at all supported zoom levels.
- [ ] Layout remains usable at 16:9 and a narrower desktop window.
- [ ] No modern IDE, cyberpunk, glass, fixed-scenario, or reference-content styling remains.
