# Ninja Adventure — Editable HUD Implementation Handoff v5

## Read first

1. `DESIGN.md`
2. `design-contract.md`
3. `KeyboardWandererSceneUIBuilder.cs`
4. `KeyboardWandererSceneUI.cs`

## Binding constraints

- Reference resolution: 1440×900, Scale With Screen Size, match 0.5.
- World remains full-screen behind HUD; edge panels must not create an opaque overlay.
- Left: status `x 2–26%`, objectives below it, minimap at bottom-left.
- Right: skill rail `x 86–98%`, menu hint above, confirm below.
- Bottom story panel: centered, `x 25–82%`, at most 18% screen height.
- Preserve all existing control names used by `KeyboardWandererDemoController`.

## Asset and runtime rules

- Use existing project sprites and font; no reference artwork is imported.
- Panels and buttons are ordinary serialized Unity components.
- Runtime may update Text, interactable/selected state, Copy/Paste keycap and Emote.
- Runtime must not replace authored anchors, panel sprites, button sprites, fonts or colors unless `Apply Runtime Theme` is explicitly enabled.

## Acceptance proof

The first artifact proves the direction when Play Mode shows the same authored layout as Scene view, the world center remains clear, all bound buttons work, and a developer can move every panel directly in the Inspector without editing code.
