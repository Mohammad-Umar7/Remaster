# REMASTER

*A game about an unfinished game.* Made for the SHIFT Game Jam 2026 (theme: **Between Two Worlds**).

It is 3:47 AM. You are remaking a 1989 pixel game, TINY KEEP, in 3D. You fall asleep at your desk and wake up inside the unfinished remake. The old game is still running in a little window floating beside you, and it is the only thing that knows what is really there.

## The two worlds

| | The 3D remake (what you see) | The 1989 original (the truth) |
|---|---|---|
| Bridges nobody modelled yet | invisible | there, and solid |
| Fancy decoration walls | look solid | not in the old game, walk through |
| Slimes and coins | never modelled | there, they bite, they count |
| The screen-wrap bug | space just repeats | exit left, appear right |

You walk in first person through five short rooms. Each room has one note from your past self and one thing the remake gets wrong.

1. **The Missing Bridge** - trust the old game and walk across thin air.
2. **Placeholder Wall** - some walls are only decoration.
3. **Unmodelled Enemies** - slimes you can only see in the old game.
4. **Screen Wrap** - use the 1989 bug to get behind a gate.
5. **Ship It** - the unfinished floor deletes itself behind you. Run.

The soundtrack is one tune in two arrangements. It starts as a chip tune, and every room you clear adds a "remastered" instrument, so finishing the game finishes its soundtrack.

## Controls

| Key | Action |
|---|---|
| W A S D / arrows | move |
| Mouse | look |
| Shift | run |
| Tab | resize the floating old-game window |
| Esc | pause |
| In the pause menu: R | restart the room |
| In the pause menu or on the title: 1 to 5 | jump to a room (for demos) |
| F11 | toggle full screen |

A gamepad also works (left stick move, right stick look).

## Run it

- **Windows build:** unzip and run `Remaster.exe`.
- **From source:** open the folder in Unity `6000.3.26f1`, open `Assets/Scenes/Main.unity`, press Play. The scene is empty on purpose: `Assets/Scripts/Boot.cs` builds everything in code.

## How it is built

- `Assets/Scripts/Maps.cs` holds the five rooms as text grids. One grid describes both games, so the 3D world and the pixel game can never disagree by accident.
- `World3D.cs` builds the 3D rooms; collision comes from the old game's truth, not from what is drawn.
- `PixelGame.cs` / `PixelScreen.cs` draw the 1989 game into a 208x192 software framebuffer from `Assets/Resources/pixelart.txt`.
- `Sketch.shader` and `SketchPost.shader` give the hand-drawn look: marker colour, pencil hatching, cast shadows drawn as hatching, wobbly double outlines that "boil".
- `Doodle.cs` / `TexLib.cs` draw every texture and UI frame in code at start-up.
- `AudioDirector.cs` runs the eight synced music layers.

## Tests

- `MapValidator.RunAll()` checks every room can be finished and really needs its trick.
- `Remaster.exe -autopilot` walks the whole game by itself and logs `[AUTOPILOT] DONE ...`. Add `-batchmode` to run it without a window.
- `tools/pt.sh` drives the same checks inside the open Unity Editor.

See [CREDITS.md](CREDITS.md) for every tool and asset used.
