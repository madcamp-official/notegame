# Keyboard Wanderer — UI design system v3

## 1. Visual direction

The interface is a tactile 16-bit fantasy command console: warm carved wood, aged metal, parchment, compact pixel controls, and a clearly framed tactical map. It must support a different generated campaign on every seed without visually implying one permanent kingdom, village, enemy, or quest.

The supplied screenshot informs UI feeling only. Use its map-first hierarchy, compact rhythm, and warm material cues. Do not copy its scenario content, exact geometry, characters, icons, text, or level arrangement.

The current player sprite is NinjaAdventure `NinjaGreen`. Environmental sprites are resolved through the same asset manifest. Never stretch pixel art or mix it with painterly high-resolution icons.

## 2. Color and material

- Frame background: warm near-black `#17130F`
- Main wood: `#3A291C`; raised wood: `#543820`; recessed wood: `#261B14`
- Metal edge: `#C98B35`; highlight gold: `#F0BA55`; disabled metal: `#715630`
- Parchment: `#D8B77A`; primary text: `#F2DFC0`; secondary text: `#BFA57B`
- Success: moss `#77A94B`; warning: amber `#D98A35`; danger: muted vermilion `#C7513E`
- Milestone/link accent: pale crystal blue `#73C6D8`
- Consequence accent: restrained violet `#9866B5`

The surrounding HUD remains stable while each biome owns its map palette. Use hard 1–3 px ramps, integer edges, and dithered texture instead of gradients, blur, glass, or neon bloom.

## 3. Typography and spacing

- Korean-first body copy at a readable 14–17 px equivalent with 1.35–1.5 line height
- Headings at 16–20 px; one-line title at 28–36 px
- Tabular numbers for turns, metrics, D20, costs, and distances
- 8 px base unit; 8–12 px outer frame; 8 px panel gap; 12–16 px inner padding
- Minimum 44×44 logical pointer target even when the visible pixel icon is smaller
- Integer-positioned strokes and discrete map zoom steps

Do not shrink Korean text to imitate the screenshot. Dense information should be grouped, not made illegible.

## 4. Desktop composition

The 16:9 baseline has four stable zones:

1. Top status strip: generated place and biome, campaign phase, meaningful turn/limit, milestone `0/3`, compact world metrics, settings.
2. Center-left viewport: at least 60% of usable width, showing either the sealed 160×160 world through a camera or a zoomed local encounter.
3. Right rail: authoritative result and generated narration, current objective, then D20 and normalized-intent explanation.
4. Bottom command deck: six primary keyboard commands, contextual actions, target/destination, free-form intent, resource strip, and commit control.

On narrower desktop windows the right rail may become tabs, but current objective, intent, and commit must remain reachable. The six primary commands may not disappear behind inventory navigation.

## 5. Components

- `WorldViewport`: sealed world camera, square tiles, six biome palettes, routes, POIs, discovery, player, and selected destination
- `LocalEncounterOverlay`: exact occupancy, legal range, hazards, entity selection, and placement preview
- `TopRunStatus`: place, biome, phase, meaningful turn/limit, `마일스톤 0/3`, and inspectable metrics
- `NarrativeLog`: chronological rule result and generated narration with visibly distinct sources, without exposing prompts
- `ObjectiveCard`: current generic role, required evidence, deadline window, recovery route, and travel target
- `D20Panel`: raw roll, modifiers, difficulty, outcome tier, normalized attempt, alignment, and divergence explanation
- `AbilityBar`: Move, Copy, Delete, Connect, Restore, Undo in that order, each with shortcut and cost
- `ContextActionBar`: Attack, Interact/Investigate, Negotiate, Rest in a subordinate visual tier
- `IntentComposer`: selected target/destination chips, multiline intent, validation state, and commit
- `MilestoneToken`: three distinct fantasy seals with locked, lead-known, earned, and committed states
- `MetricPips`: compact inspectable world-state indicators defined by the current run
- `TravelRouteCard`: safe path, risky shortcut, travel cost, possible encounter, and explicit “no meaningful turn” label for safe travel
- `FinaleBindings`: the current run's valid `anchor`, `safeguard`, `memory`, `freedom`, `threat`, `passage`, and `witness` candidates without showing unavailable internals

## 6. Biome readability

The map must visibly distinguish all six descriptors without changing the HUD vocabulary:

- `temperate_forest_field`: layered greens, soil, timber, crops
- `river_wetland`: cool water, reeds, wet ground, bridges
- `arid_desert`: sand, ochre stone, sparse vegetation
- `frost_highland`: pale ground, cold rock, cliffs, wind markers
- `subterranean_cavern`: dark rock, mineral contrast, tight corridors
- `ancient_ruins`: weathered masonry, relic structures, corrupted accents

Campaign roles are not colors. A relationship conflict or hidden truth can occur in any compatible biome.

## 7. Motion and feedback

- Map pan/zoom: 120–180 ms, eased, pixel-snapped at rest
- Destination: one short pulse followed by a persistent outline
- D20: 450–700 ms, reveal raw value before modifiers and consequence
- Commit: lock only the submitted command and preserve map position
- Command response: 160–260 ms; Copy echoes with an unstable offset, Delete fragments locally, Connect draws a temporary rune, Restore reconstructs locally, Undo adds a visible compensation mark
- Provider delay: keep the authoritative result visible and show narration as optional follow-up
- Reduced motion: replace rolls and pulses with immediate stepped states

No ambient parallax or floating decoration may obscure the grid.

## 8. Voice

Korean-first copy is concise, concrete, and lightly mythic. Generated proper nouns may vary per run, while system labels remain stable: `마일스톤`, `의미 턴`, `탐색 이동`, `정규화된 시도`, `월드 봉인`, and the six command names.

Rule explanations must name the selected target, cost, roll, difficulty, legal change, and consequence. Generated narration can be evocative but cannot disguise the authoritative result.

## 9. Anti-patterns

- Do not copy any place, character, enemy, quest, economy, text, or object arrangement from the screenshot.
- Do not present a seed as a renamed version of one fixed scenario.
- Do not confuse the six biome descriptors with the six generic campaign roles.
- Do not regenerate the map per turn or let the LLM invent coordinates, exits, routes, POIs, or asset paths.
- Do not consume a meaningful turn for each safe map step.
- Do not hide milestone progress, current objective, free-form intent, normalized attempt, roll breakdown, or remaining meaningful turns.
- Do not use a modern IDE, cyberpunk dashboard, glassmorphism, purple brand gradient, oversized rounded cards, or sparse landing-page spacing.
- Do not fractional-scale sprites or show a substitute hero when `NinjaGreen` resolves successfully.
