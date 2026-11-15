# Credits, tools and assets

Everything in the game was made during the SHIFT Game Jam (13 to 15 November 2026), after the theme reveal.

## Team

- TEAM NAME: _fill in_
- Members: _fill in_

## Engine and packages

- Unity 6000.3.26f1 (Built-in Render Pipeline), with the Unity Input System and Unity UI packages.

## Art

- **3D props** (desk, monitor, lamp, pillar, gate, banner, signpost, the guard (six jointed parts, animated in code), the key, the padlock and others): modelled in Blender 5.1 by script during the jam. Source: `tools/gen_props_blender.py`, `tools/remaster_props.blend`.
- **Pixel art** for the 1989 game: drawn by hand as text grids during the jam. Source: `Assets/Resources/pixelart.txt`.
- **Textures, UI frames and doodles**: drawn by our own code when the game starts (`Assets/Scripts/Doodle.cs`, `TexLib.cs`). There are no image files.
- **Shaders**: written for this game (`Assets/Resources/Shaders`).

## Audio

- All music and sound effects are synthesized by our own script (`tools/gen_audio.py`, Python with NumPy and SciPy). No samples and no audio libraries were used. See `tools/audio_manifest.md`.

## Fonts (third-party, open licence)

- **Patrick Hand** by Patrick Wagesreiter, SIL Open Font License 1.1.
- **Gochi Hand** by Huerta Tipografica, SIL Open Font License 1.1.
- Both from Google Fonts. Licence texts are in `Assets/Resources/Fonts`.

## AI tools

- **Claude (Anthropic), through Claude Code**: wrote the game code and shaders, and generated the pixel-art grids, the audio synthesis script and the Blender modelling script, under the team's direction.
- **Gemini**: used once for a concept video while choosing the idea. Nothing from it is in the game.

## Reference material

- `gamedev-skills/awesome-gamedev-agent-skills` (Apache-2.0): game-jam and game-feel guidance notes read during development. Not shipped with the game.

## Inspiration

- The idea of seeing a 2D and a 3D version of one place at the same time was sparked by a clip of *Screenbound* (Crescent Moon Games). REMASTER has its own story, rules, levels, art, audio and code.
