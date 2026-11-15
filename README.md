# REMASTER

*A game about an unfinished game.* Made for the SHIFT Game Jam 2026 (theme: **Between Two Worlds**).

It is 3:47 AM. You are remaking a 1989 pixel game, TINY KEEP, in 3D. You fall asleep at your desk and wake up inside the unfinished remake. The old game is still running on the handheld in your left hand, and it knows what is really there. But the remake has things the old game never had, so you need both.

## The two worlds

| | The 3D remake (what you see) | The 1989 original (the truth) |
|---|---|---|
| Bridges nobody modelled yet | invisible | there, and solid |
| Fancy decoration walls | look solid | not in the old game, walk through |
| Slimes and coins | never modelled | there, they bite, they count |
| The screen-wrap bug | space just repeats | exit left, appear right |
| **Keys and guards** (new in the remake) | there, in plain sight | the old game has never heard of them |

Neither world is enough on its own. The old game knows everything that is **old**: where the floor really is, where the slimes are, which planks are about to blink out. The remake knows everything that is **new**: every exit is locked until you find that room's key, and guards patrol the rooms. Keys and guards are never drawn in the old game, so you find the key and dodge the guards with your own eyes, in 3D. A guard notices you when you come close: it chirps and shouts "HALT!" in a crunchy 1989 speech sample, a "!" pops over its head, it turns its face to you and lowers its spear, and its footsteps get louder the nearer it is. Step inside the red ring at its feet and it strikes: it cocks the spear back, lunges, and you are knocked back with a BONK and lose a heart. The shove never pushes you into a pit. And from then on it is after you: it leaves its patrol and follows you for a few tiles, striking again if it catches up. Get far enough away and it gives up, walks back the way it came and carries on guarding. Sprint out from under the spear and it misses. Your hearts are shown in the 3D view too, next to the room checklist, so you never have to look away from a guard to see how many are left.

You walk in first person through seven short rooms. Each room has one note from your past self and one thing the remake gets wrong.

1. **The Missing Bridge** - trust the old game and walk across thin air.
2. **Placeholder Wall** - some walls are only decoration.
3. **Unmodelled Enemies** - slimes you can only see in the old game.
4. **Screen Wrap** - use the 1989 bug to get behind a gate.
5. **Sprite Flicker** - two groups of planks take turns existing. You can hear the beat.
6. **One-Way Maze** - old planks snap after one step. Three ways in, one true route, and every dead end is final, so solve the maze in the old game before you walk it.
7. **The Captain** - the last room is a fight. The Captain of the guards is new in the remake, so the old game shows an empty hall.

The keys get harder to reach and the guards get faster as you go: in room 1 the key hangs in plain sight with one slow guard circling it; in room 6 you fetch it from one end of the start floor before you can solve the maze from the other.

### The Captain

He drops in front of the last exit, the camera pulls out of your head, and you see yourself for the first time: the 1989 game's little hero, in 3D. The two of you have words, shot like a film: a short voiced scene where he acts every line (he points his spear at you, laughs at you, stamps), his mouth moves to the sound of his own voice, and the camera cuts between the two of you. The remake never modelled a sword, so you draw the old game's one. Every coin you picked up in the old game takes a little off his health.

Then the fight, in third person, with the view locked on him and a health bar each:

- **left click** - a three-hit combo: slash, slash, overhead smash.
- **space** - a dodge roll. Nothing can hit you while you roll.

He blocks while he is ready ("CLANG!"), so you dodge first and hit him while he is stuck or dizzy. His health bar comes in three parts, and each part has its own attack. Where each one will land is drawn on the floor in red before it does:

- **the charge** - a red lane. Step out of it.
- **the spin** - a red circle round him. Back away, then hit him while he is dizzy.
- **the leap** - a red circle where he will come down. Run or roll out.

When his health is gone he comes apart, and every piece tumbles away under real physics. If you lose your hearts you start again from the door, but the parts of his health you had already beaten stay beaten.

The soundtrack is one tune in two arrangements. It starts as a chip tune, and every room you clear adds a "remastered" instrument, so finishing the game finishes its soundtrack.

## Controls

| Key | Action |
|---|---|
| Start screen: W / S or arrows, then Enter (or click a note) | choose START or QUIT |
| W A S D / arrows | move |
| Mouse | look |
| Shift | run |
| Left click (or F) | sword, in the last room: keep clicking for the three-hit combo |
| Space | dodge roll, in the last room |
| Enter or click | next line, in the scene before the last fight |
| Tab | raise or lower the handheld (the old game) |
| Esc | pause |
| In the pause menu: R | restart the room |
| N | skip to the next room, if you are stuck |
| B | skip straight to the last fight (also works on the start screen) |
| In the pause menu or on the title: 1 to 7 | jump to a room (for demos) |
| F11 | toggle full screen |

A gamepad also works (left stick move, right stick look, X or the right bumper to swing, B to dodge).

## Run it

- **Windows build:** unzip and run `Remaster.exe`.
- **From source:** open the folder in Unity `6000.3.26f1`, open `Assets/Scenes/Main.unity`, press Play. The scene is empty on purpose: `Assets/Scripts/Boot.cs` builds everything in code.

## How it is built

- `Assets/Scripts/Maps.cs` holds the seven rooms as text grids. One grid describes both games, so the 3D world and the pixel game can never disagree by accident.
- `World3D.cs` builds the 3D rooms; collision comes from the old game's truth, not from what is drawn.
- `PixelGame.cs` / `PixelScreen.cs` draw the 1989 game into a 208x192 software framebuffer from `Assets/Resources/pixelart.txt`.
- `HandheldConsole.cs` builds the handheld and the hand that holds it out of simple shapes, as part of the 3D view, and shows the old game on its screen.
- `Sketch.shader` and `SketchPost.shader` give the hand-drawn look: marker colour, pencil hatching, cast shadows drawn as hatching, wobbly double outlines that "boil".
- `Doodle.cs` / `TexLib.cs` draw every texture and UI frame in code at start-up.
- `AudioDirector.cs` runs the eight synced music layers.
- `GuardRig.cs` poses the guard (six Blender parts) in code every frame: there are no animation clips, so the animation can never drift away from the rules.
- `BossFight.cs` is the last room: the scene and its camera shots, the Captain's three attacks and their red floor markers, your combo and dodge, and the pieces he breaks into.
- `HeroRig.cs` is you in that room: the old game's hero and sword, built out of blocks and posed in code like the guard.

## Tests

- `MapValidator.RunAll()` checks every room can be finished and really needs its trick, that its key can be reached, and that guards only walk on real floor and never over the spawn point.
- `Remaster.exe -autopilot` plays the whole game by itself and logs `[AUTOPILOT] DONE ...`. It fetches each key and then leaves, planning in space and time (every hazard follows a fixed pattern), so it also proves the guard, slime and flicker timings are beatable. It sidesteps while it turns, so a corner costs it no time and its half-second-a-tile plan holds in the tightest windows. In the last room it walks up to the Captain and keeps swinging; his hits do not count against the test driver, so that part proves the fight can be finished, not that it can be dodged. Add `-batchmode` to run it without a window.
- `tools/pt.sh` drives the same checks inside the open Unity Editor.

See [CREDITS.md](CREDITS.md) for every tool and asset used.
