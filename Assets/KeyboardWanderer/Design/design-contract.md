# Ninja Adventure — Gameplay HUD Reference Contract v5

## Goal and target

- Target artifact: editable PC-first Unity gameplay HUD
- Audience: keyboard-driven top-down adventure players
- Goal: keep the world readable while placing status, objectives, skills, minimap, dialogue and confirmation at stable screen edges.

## Evidence

| Evidence | Confidence | What it establishes |
| --- | --- | --- |
| User-provided 16:9 gameplay screenshot | provided | edge-mounted HUD, large unobstructed world, left information stack, right skill rail, bottom-left minimap |
| User instruction to ignore exact copying | provided | composition may be adapted to existing controls and assets |
| Current Unity hierarchy and controller bindings | observed | existing named controls must remain bindable and editable as normal GameObjects |
| Minimap has no current gameplay data source | inferred | create an editable placeholder without pretending it is functional navigation |

## Keep, change, do not copy

| Keep | Change | Do not copy |
| --- | --- | --- |
| Central world dominance | Fit existing MOVE and eight shortcut controls | Reference character and portraits |
| Left status/objective rhythm | Use current Codria labels and data | Exact icons, map art and quest text |
| Right vertical skill rail | Add Search and Select All while preserving legibility | Exact frame shapes and pixel ornament |
| Bottom-left minimap footprint | Provide a replaceable placeholder | Reference region layout |
| Short bottom dialogue | Retain current speaker and result Emote flow | Exact coordinates and dimensions |

## Final stance

Use a quiet edge-HUD: compact dark panels with warm borders around an unobstructed central world. Every panel is an authored Unity RectTransform, not a runtime-generated visual override. Runtime code may update content and interaction state but may not replace the designer's anchors, sprites or colors.

## Risks and unknowns

- The minimap is visual scaffolding until a map data source is connected.
- Very long Korean narration may require a later scroll or paging decision.
- Narrower than 16:9 resolutions need a separate compact breakpoint.

## Quality gate

- [x] Central world keeps at least 62% unobstructed area.
- [x] Status and objectives occupy the left edge.
- [x] Skills form a readable right rail.
- [x] Dialogue is shorter than 20% of screen height.
- [x] Every layout element remains editable in Unity Inspector.
- [x] Reference content and exact artwork are not copied.
- [x] Runtime theme replacement is disabled by default.
