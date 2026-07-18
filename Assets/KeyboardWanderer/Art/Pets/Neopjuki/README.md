# Neopjuki animated sprite asset

`NeopjukiUnityAtlas.png` is a transparent 8 × 8 sprite atlas. Every cell is
192 × 208 pixels, and the complete atlas is 1536 × 1664 pixels.

Unity imports and slices the atlas automatically after script compilation. The
editor tool configures point filtering, disables compression and mipmaps, and
creates named sprite rectangles for every used animation frame. To reimport it
manually, run **Keyboard Wanderer > Import Neopjuki Pet Atlas**.

Animation rows:

| Row | State | Frames |
| --- | --- | ---: |
| 0 | Idle + neutral look | 6 + 1 |
| 1 | Move right | 8 |
| 2 | Move left | 8 |
| 3 | Wave | 4 |
| 4 | Jump | 5 |
| 5 | Failed | 8 |
| 6 | Waiting | 6 |
| 7 | Review | 6 |

The atlas uses bottom-centered pivots and 192 pixels per Unity unit. The
preview image is documentation only and should not be used at runtime. The
Codex-only active-task and look-direction rows are intentionally omitted.
