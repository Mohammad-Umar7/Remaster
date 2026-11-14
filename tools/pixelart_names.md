# TINY KEEP sprite names

All sprites live in `Assets/Resources/pixelart.txt` (plain text, LF line endings; trim any trailing carriage return when parsing).
Palette letters: `K` 14121f, `D` 3b2f5c, `P` 7a4fd6, `T` 2fb5a8, `G` 7bd65a, `Y` ffd24a, `R` ef5d5d, `W` f4efe1, `.` transparent.
Validate and preview with `python tools/pixel_preview.py --out <folder>`.

## Tiles (opaque)

Auto-tile rules used in the mock: draw `wall_top` when the tile below is also wall, door or gate (or is outside the room), otherwise `wall`. Draw `pit_edge` when the tile above is floor, otherwise `pit`. Bridges replace pit tiles. `floor_b` is an occasional variation of `floor_a`.

| Name | Size |
|---|---|
| `floor_a` | 16x16 |
| `floor_b` | 16x16 |
| `wall` | 16x16 |
| `wall_top` | 16x16 |
| `pit` | 16x16 |
| `pit_edge` | 16x16 |
| `bridge_v` | 16x16 |
| `bridge_h` | 16x16 |
| `gate` | 16x16 |
| `door_exit` | 16x16 |
| `door_start` | 16x16 |
| `crumble_1` | 16x16 |
| `crumble_2` | 16x16 |
| `crumble_3` | 16x16 |

## Objects (transparent background)

`hero_side_*` faces right; mirror it for left. Two walk frames per direction. Coins spin 1-2-3-4. `poof`, `sparkle` and `warp` are three-frame one-shots.

| Name | Size |
|---|---|
| `note` | 16x16 |
| `coin_1` | 16x16 |
| `coin_2` | 16x16 |
| `coin_3` | 16x16 |
| `coin_4` | 16x16 |
| `hero_down_1` | 16x16 |
| `hero_down_2` | 16x16 |
| `hero_up_1` | 16x16 |
| `hero_up_2` | 16x16 |
| `hero_side_1` | 16x16 |
| `hero_side_2` | 16x16 |
| `slime_1` | 16x16 |
| `slime_2` | 16x16 |
| `slime_hit` | 16x16 |
| `poof_1` | 16x16 |
| `poof_2` | 16x16 |
| `poof_3` | 16x16 |
| `sparkle_1` | 16x16 |
| `sparkle_2` | 16x16 |
| `sparkle_3` | 16x16 |
| `arrow_left` | 16x16 |
| `arrow_right` | 16x16 |
| `warp_1` | 16x16 |
| `warp_2` | 16x16 |
| `warp_3` | 16x16 |

## HUD (transparent background)

Meant for a `K` (or `D`) HUD bar. `heart_empty` has a `K` outline and a `D` fill so it shows on either.

| Name | Size |
|---|---|
| `heart_full` | 9x8 |
| `heart_empty` | 9x8 |
| `coin_icon` | 8x8 |

## Font (5x7, `W` on transparent)

Advance 6 px per glyph, 4 px for a space. `glyph_a`..`glyph_z` are uppercase shapes. Punctuation: dot `.`, comma `,`, excl `!`, quest `?`, colon `:`, dash `-`, plus `+`, slash `/`, apos `'`, x_small (multiplication sign), percent `%`.

| Name | Size |
|---|---|
| `glyph_a` | 5x7 |
| `glyph_b` | 5x7 |
| `glyph_c` | 5x7 |
| `glyph_d` | 5x7 |
| `glyph_e` | 5x7 |
| `glyph_f` | 5x7 |
| `glyph_g` | 5x7 |
| `glyph_h` | 5x7 |
| `glyph_i` | 5x7 |
| `glyph_j` | 5x7 |
| `glyph_k` | 5x7 |
| `glyph_l` | 5x7 |
| `glyph_m` | 5x7 |
| `glyph_n` | 5x7 |
| `glyph_o` | 5x7 |
| `glyph_p` | 5x7 |
| `glyph_q` | 5x7 |
| `glyph_r` | 5x7 |
| `glyph_s` | 5x7 |
| `glyph_t` | 5x7 |
| `glyph_u` | 5x7 |
| `glyph_v` | 5x7 |
| `glyph_w` | 5x7 |
| `glyph_x` | 5x7 |
| `glyph_y` | 5x7 |
| `glyph_z` | 5x7 |
| `glyph_0` | 5x7 |
| `glyph_1` | 5x7 |
| `glyph_2` | 5x7 |
| `glyph_3` | 5x7 |
| `glyph_4` | 5x7 |
| `glyph_5` | 5x7 |
| `glyph_6` | 5x7 |
| `glyph_7` | 5x7 |
| `glyph_8` | 5x7 |
| `glyph_9` | 5x7 |
| `glyph_dot` | 5x7 |
| `glyph_comma` | 5x7 |
| `glyph_excl` | 5x7 |
| `glyph_quest` | 5x7 |
| `glyph_colon` | 5x7 |
| `glyph_dash` | 5x7 |
| `glyph_plus` | 5x7 |
| `glyph_slash` | 5x7 |
| `glyph_apos` | 5x7 |
| `glyph_x_small` | 5x7 |
| `glyph_percent` | 5x7 |

## Title

| Name | Size |
|---|---|
| `logo_tinykeep` | 96x24 |

Total: 90 sprites.
