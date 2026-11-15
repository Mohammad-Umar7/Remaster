# REMASTER

*A game about an unfinished game.* Made for the SHIFT Game Jam 2026 (theme: **Between Two Worlds**).

It is 3:47 AM. You are remaking a 1989 pixel game, TINY KEEP, in 3D. You fall asleep at your desk and wake up inside the unfinished remake. The old game is still running in a little window floating beside you, and it knows what is really there. But the remake has things the old game never had, so you need both.

## The two worlds

| | The 3D remake (what you see) | The 1989 original (the truth) |
|---|---|---|
| Bridges nobody modelled yet | invisible | there, and solid |
| Fancy decoration walls | look solid | not in the old game, walk through |
| Slimes and coins | never modelled | there, they bite, they count |
| The screen-wrap bug | space just repeats | exit left, appear right |
| **Keys and guards** (new in the remake) | there, in plain sight | the old game has never heard of them |

Neither world is enough on its own. The old game knows everything that is **old**: where the floor really is, where the slimes are, when a laser is coming. The remake knows everything that is **new**: every exit is locked until you find that room's key, and guards patrol the rooms. Keys and guards are never drawn in the pixel window, so you find the key and dodge the guards with your own eyes, in 3D. A guard notices you when you come close: it chirps, a "!" pops over its head, it turns its face to you and lowers its spear, and its footsteps get louder the nearer it is. Step inside the red ring at its feet and it strikes: it cocks the spear back, lunges, and you are knocked back with a BONK and lose a heart. The shove never pushes you into a pit. Your hearts are shown in the 3D view too, next to the room checklist, so you never have to look away from a guard to see how many are left.

You walk in first person through eight short rooms. Each room has one note from your past self and one thing the remake gets wrong.

1. **The Missing Bridge** - trust the old game and walk across thin air.
2. **Placeholder Wall** - some walls are only decoration.
3. **Unmodelled Enemies** - slimes you can only see in the old game.
4. **Screen Wrap** - use the 1989 bug to get behind a gate.
5. **Sprite Flicker** - two groups of planks take turns existing. You can hear the beat.
6. **Three Seconds Late** - the old game is three seconds ahead: it shows each laser before the remake fires it.
7. **One-Way Maze** - old planks snap after one step. Three ways in, one true route, and every dead end is final, so solve the maze in the old game before you walk it.
8. **Ship It** - the unfinished floor deletes itself behind you. Run.

The keys get harder to reach and the guards get faster as you go: in room 1 the key hangs in plain sight with one slow guard circling it; by room 6 it sits inside a guard's ring under the lasers, and in the last room it is a detour while the floor is deleting itself.

The soundtrack is one tune in two arrangements. It starts as a chip tune, and every room you clear adds a "remastered" instrument, so finishing the game finishes its soundtrack.

## Controls

| Key | Action |
|---|---|
| Start screen: W / S or arrows, then Enter (or click a note) | choose START or QUIT |
| W A S D / arrows | move |
| Mouse | look |
| Shift | run |
| Tab | resize the floating old-game window |
| Esc | pause |
| In the pause menu: R | restart the room |
| In the pause menu or on the title: 1 to 8 | jump to a room (for demos) |
| F11 | toggle full screen |

A gamepad also works (left stick move, right stick look).

## Run it

- **Windows build:** unzip and run `Remaster.exe`.
- **From source:** open the folder in Unity `6000.3.26f1`, open `Assets/Scenes/Main.unity`, press Play. The scene is empty on purpose: `Assets/Scripts/Boot.cs` builds everything in code.

## How it is built

- `Assets/Scripts/Maps.cs` holds the eight rooms as text grids. One grid describes both games, so the 3D world and the pixel game can never disagree by accident.
- `World3D.cs` builds the 3D rooms; collision comes from the old game's truth, not from what is drawn.
- `PixelGame.cs` / `PixelScreen.cs` draw the 1989 game into a 208x192 software framebuffer from `Assets/Resources/pixelart.txt`.
- `Sketch.shader` and `SketchPost.shader` give the hand-drawn look: marker colour, pencil hatching, cast shadows drawn as hatching, wobbly double outlines that "boil".
- `Doodle.cs` / `TexLib.cs` draw every texture and UI frame in code at start-up.
- `AudioDirector.cs` runs the eight synced music layers.

## Tests

- `MapValidator.RunAll()` checks every room can be finished and really needs its trick, that its key can be reached, and that guards only walk on real floor and never over the spawn point.
- `Remaster.exe -autopilot` plays the whole game by itself and logs `[AUTOPILOT] DONE ...`. It fetches each key and then leaves, planning in space and time (every hazard follows a fixed pattern), so it also proves the guard, slime, flicker and laser timings are beatable. It sidesteps while it turns, so a corner costs it no time and its half-second-a-tile plan holds in the tightest windows. Add `-batchmode` to run it without a window.
- `tools/pt.sh` drives the same checks inside the open Unity Editor.

See [CREDITS.md](CREDITS.md) for every tool and asset used.
