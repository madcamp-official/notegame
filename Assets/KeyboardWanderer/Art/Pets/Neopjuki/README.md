# Neopjuki animated sprite asset

`NeopjukiCodexAtlas.png` is a transparent 8 × 11 sprite atlas. Every cell is
192 × 208 pixels, and the complete atlas is 1536 × 2288 pixels.

In Unity, run **Keyboard Wanderer > Import Neopjuki Pet Atlas** once. The editor
tool configures point filtering, disables compression and mipmaps, and creates
named sprite rectangles for every used animation frame.

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
| 7 | Active task | 6 |
| 8 | Review | 6 |
| 9 | Look 000°–157.5° | 8 |
| 10 | Look 180°–337.5° | 8 |

The atlas uses bottom-centered pivots and 192 pixels per Unity unit. The
preview image is documentation only and should not be used at runtime.
