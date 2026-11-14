#!/usr/bin/env python3
"""Validate and preview Assets/Resources/pixelart.txt (the TINY KEEP sprite set).

Usage:
    python tools/pixel_preview.py [--file PATH] [--out DIR]

Writes two PNGs into the output folder:
    sheet.png  every sprite at 6x on a checkerboard, with its name
    mock.png   a mock 13x11-tile room (208x176) plus a 16 px HUD bar, at 4x

Exits with code 1 if the file fails validation.
Needs Pillow (pip install pillow).
"""
import argparse
import os
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_FILE = os.path.join(HERE, "..", "Assets", "Resources", "pixelart.txt")

PALETTE_KEYS = ["K", "D", "P", "T", "G", "Y", "R", "W"]

TILES = ["floor_a", "floor_b", "wall", "wall_top", "pit", "pit_edge", "bridge_v", "bridge_h",
         "gate", "door_exit", "door_start", "crumble_1", "crumble_2", "crumble_3"]
OBJECTS = ["note", "coin_1", "coin_2", "coin_3", "coin_4",
           "hero_down_1", "hero_down_2", "hero_up_1", "hero_up_2", "hero_side_1", "hero_side_2",
           "slime_1", "slime_2", "slime_hit",
           "poof_1", "poof_2", "poof_3", "sparkle_1", "sparkle_2", "sparkle_3",
           "arrow_left", "arrow_right", "warp_1", "warp_2", "warp_3"]
HUD = [("heart_full", 9, 8), ("heart_empty", 9, 8), ("coin_icon", 8, 8)]
GLYPH_KEYS = list("abcdefghijklmnopqrstuvwxyz") + list("0123456789") + \
    ["dot", "comma", "excl", "quest", "colon", "dash", "plus", "slash", "apos", "x_small", "percent"]
LOGO = ("logo_tinykeep", 96, 24)

REQUIRED = {}
for _n in TILES + OBJECTS:
    REQUIRED[_n] = (16, 16)
for _n, _w, _h in HUD:
    REQUIRED[_n] = (_w, _h)
for _k in GLYPH_KEYS:
    REQUIRED["glyph_" + _k] = (5, 7)
REQUIRED[LOGO[0]] = (LOGO[1], LOGO[2])

CHAR_TO_GLYPH = {".": "dot", ",": "comma", "!": "excl", "?": "quest", ":": "colon", "-": "dash",
                 "+": "plus", "/": "slash", "'": "apos", "*": "x_small", "%": "percent"}


def parse(path):
    """Returns (palette, sprites, errors). sprites: name -> (w, h, rows)."""
    palette, sprites, errors = {}, {}, []
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
    i, n = 0, len(lines)
    in_palette = False
    while i < n:
        raw = lines[i].rstrip("\r\n")
        s = raw.strip()
        i += 1
        if not s or s.startswith("#"):
            continue
        if s == "@palette":
            in_palette = True
            continue
        if s.startswith("@sprite"):
            in_palette = False
            parts = s.split()
            if len(parts) != 4:
                errors.append(f"line {i}: bad sprite header: {s!r}")
                continue
            name = parts[1]
            try:
                w, h = int(parts[2]), int(parts[3])
            except ValueError:
                errors.append(f"line {i}: bad sprite size: {s!r}")
                continue
            rows = []
            for _ in range(h):
                if i >= n:
                    break
                row = lines[i].rstrip("\r\n")
                if row.startswith("@"):
                    break
                rows.append(row)
                i += 1
            if len(rows) != h:
                errors.append(f"{name}: has {len(rows)} rows, header says {h}")
            for j, row in enumerate(rows):
                if len(row) != w:
                    errors.append(f"{name}: row {j} is {len(row)} wide, header says {w}")
            if name in sprites:
                errors.append(f"{name}: defined twice")
            sprites[name] = (w, h, rows)
            continue
        if in_palette:
            parts = s.split()
            if len(parts) != 2 or len(parts[0]) != 1 or len(parts[1]) != 6:
                errors.append(f"line {i}: bad palette entry: {s!r}")
                continue
            try:
                rgb = tuple(int(parts[1][k:k + 2], 16) for k in (0, 2, 4))
            except ValueError:
                errors.append(f"line {i}: bad palette hex: {s!r}")
                continue
            palette[parts[0]] = rgb
            continue
        errors.append(f"line {i}: unexpected text: {s!r}")
    return palette, sprites, errors


def validate(palette, sprites):
    errors = []
    for k in PALETTE_KEYS:
        if k not in palette:
            errors.append(f"palette: missing colour {k}")
    for k in palette:
        if k not in PALETTE_KEYS:
            errors.append(f"palette: extra colour {k} (only 8 colours are allowed)")
    allowed = set(palette.keys()) | {"."}
    for name, (w, h, rows) in sprites.items():
        for j, row in enumerate(rows):
            bad = sorted(set(row) - allowed)
            if bad:
                errors.append(f"{name}: row {j} uses characters not in the palette: {bad}")
    for name, (w, h) in REQUIRED.items():
        if name not in sprites:
            errors.append(f"missing required sprite: {name}")
            continue
        sw, sh, _ = sprites[name]
        if (sw, sh) != (w, h):
            errors.append(f"{name}: is {sw}x{sh}, required {w}x{h}")
    for name in TILES:
        if name in sprites and any("." in r for r in sprites[name][2]):
            errors.append(f"{name}: tiles must be opaque (found transparent pixels)")
    extras = sorted(set(sprites) - set(REQUIRED))
    return errors, extras


def sprite_image(palette, sprite):
    w, h, rows = sprite
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = img.load()
    for y, row in enumerate(rows[:h]):
        for x, ch in enumerate(row[:w]):
            if ch != "." and ch in palette:
                px[x, y] = palette[ch] + (255,)
    return img


def checker(w, h, cell=6, a=(92, 92, 104), b=(112, 112, 126)):
    img = Image.new("RGBA", (w, h), a + (255,))
    d = ImageDraw.Draw(img)
    for y in range(0, h, cell):
        for x in range(0, w, cell):
            if ((x // cell) + (y // cell)) % 2:
                d.rectangle([x, y, x + cell - 1, y + cell - 1], fill=b + (255,))
    return img


def draw_text(dst, images, text, x, y, spacing=1):
    """Draws text with the 5x7 glyph sprites onto a 1x RGBA image."""
    for ch in text:
        if ch == " ":
            x += 4
            continue
        key = CHAR_TO_GLYPH.get(ch, ch.lower())
        g = images.get("glyph_" + key)
        if g is not None:
            dst.alpha_composite(g, (x, y))
        x += 5 + spacing
    return x


def render_sheet(palette, sprites, images, out_path, scale=6):
    font = ImageFont.load_default()
    pad, label_h = 14, 16
    cell_w, cell_h = 16 * scale + pad, 16 * scale + label_h + pad
    per_row = 10
    groups = [("TILES", TILES), ("OBJECTS", OBJECTS), ("HUD", [n for n, _, _ in HUD])]
    blocks, y = [], pad
    for title, names in groups:
        blocks.append(("title", title, y))
        y += 18
        for k, name in enumerate(names):
            if k and k % per_row == 0:
                y += cell_h
            blocks.append(("sprite", name, y, k % per_row))
        y += cell_h + 6
    blocks.append(("title", "FONT (5x7)", y))
    y += 18
    font_y = y
    gcell_w, gcell_h = 5 * scale + 14, 7 * scale + label_h + 8
    g_per_row = 24
    y += gcell_h * ((len(GLYPH_KEYS) + g_per_row - 1) // g_per_row) + 6
    sample_y = y
    y += 2 * (9 * 4) + 16
    blocks.append(("title", "LOGO", y))
    y += 18
    logo_y = y
    y += LOGO[2] * scale + pad
    width = pad * 2 + per_row * cell_w
    sheet = Image.new("RGBA", (width, y), (40, 40, 48, 255))
    d = ImageDraw.Draw(sheet)
    for b in blocks:
        if b[0] == "title":
            d.text((pad, b[2]), b[1], fill=(255, 255, 255, 255), font=font)
        else:
            _, name, by, col = b
            x = pad + col * cell_w
            if name in images:
                im = images[name]
                big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
                bg = checker(big.width, big.height)
                bg.alpha_composite(big)
                sheet.paste(bg, (x, by))
            else:
                d.rectangle([x, by, x + 16 * scale, by + 16 * scale], outline=(255, 0, 255, 255))
            d.text((x, by + 16 * scale + 2), name, fill=(220, 220, 230, 255), font=font)
    for k, key in enumerate(GLYPH_KEYS):
        x = pad + (k % g_per_row) * gcell_w
        gy = font_y + (k // g_per_row) * gcell_h
        name = "glyph_" + key
        if name in images:
            im = images[name]
            big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
            bg = Image.new("RGBA", big.size, (20, 18, 31, 255))
            bg.alpha_composite(big)
            sheet.paste(bg, (x, gy))
        d.text((x, gy + 7 * scale + 1), key[:5], fill=(200, 200, 210, 255), font=font)
    # sample lines set with the font, at 4x, on the dark HUD colour
    sample = Image.new("RGBA", (width // 4, 2 * 9 + 2), palette.get("K", (0, 0, 0)) + (255,))
    draw_text(sample, images, "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG", 2, 1)
    draw_text(sample, images, "0123456789 .,!?:-+/'*% ROOM 1-3  COINS 02/15", 2, 10)
    sheet.paste(sample.resize((sample.width * 4, sample.height * 4), Image.NEAREST), (pad, sample_y))
    if LOGO[0] in images:
        im = images[LOGO[0]]
        big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
        bg = checker(big.width, big.height)
        bg.alpha_composite(big)
        sheet.paste(bg, (pad, logo_y))
    sheet.convert("RGB").save(out_path)


ROOM = [
    "######X######",
    "#..c........#",
    "#.......s...#",
    "###G#########",
    "#.....n.....#",
    "#~~~~V~~~~~~#",
    "#~~~~V~~~~~~#",
    "#.......~~..#",
    "#.c.....HH.c#",
    "#..@....~~s.#",
    "######S######",
]
FLOORISH = set(".csn@")
WALLISH = set("#XSG")


def render_mock(palette, sprites, images, out_path, scale=4):
    cols, rows = 13, 11
    hud_h = 16
    img = Image.new("RGBA", (cols * 16, rows * 16 + hud_h), palette.get("K", (0, 0, 0)) + (255,))

    def at(x, y):
        if 0 <= y < rows and 0 <= x < cols:
            return ROOM[y][x]
        return "#"  # outside the room counts as wall

    def put(name, tx, ty):
        if name in images:
            img.alpha_composite(images[name], (tx * 16, hud_h + ty * 16))

    coin_frame = 0
    slime_frame = 0
    for y in range(rows):
        for x in range(cols):
            c = at(x, y)
            if c == "#":
                put("wall_top" if at(x, y + 1) in WALLISH else "wall", x, y)
            elif c == "X":
                put("door_exit", x, y)
            elif c == "S":
                put("door_start", x, y)
            elif c == "G":
                put("gate", x, y)
            elif c == "~":
                put("pit_edge" if at(x, y - 1) in FLOORISH else "pit", x, y)
            elif c == "V":
                put("bridge_v", x, y)
            elif c == "H":
                put("bridge_h", x, y)
            else:
                put("floor_b" if (x * 7 + y * 13) % 5 == 0 else "floor_a", x, y)
    for y in range(rows):
        for x in range(cols):
            c = at(x, y)
            if c == "c":
                put(f"coin_{coin_frame % 4 + 1}", x, y)
                coin_frame += 1
            elif c == "s":
                put("slime_1" if slime_frame % 2 == 0 else "slime_2", x, y)
                slime_frame += 1
            elif c == "n":
                put("note", x, y)
            elif c == "@":
                put("hero_down_1", x, y)
    # HUD: hearts, coin counter, room label
    hx = 4
    for name in ("heart_full", "heart_full", "heart_empty"):
        if name in images:
            img.alpha_composite(images[name], (hx, 4))
        hx += 11
    if "coin_icon" in images:
        img.alpha_composite(images["coin_icon"], (46, 4))
    draw_text(img, images, "*03", 56, 5)
    draw_text(img, images, "TINY KEEP", 92, 5)
    draw_text(img, images, "ROOM 1", 166, 5)
    img.resize((img.width * scale, img.height * scale), Image.NEAREST).convert("RGB").save(out_path)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--file", default=DEFAULT_FILE, help="path to pixelart.txt")
    ap.add_argument("--out", default=os.path.join(tempfile.gettempdir(), "remaster_pixel_preview"),
                    help="folder for the preview PNGs")
    args = ap.parse_args()

    palette, sprites, errors = parse(args.file)
    v_errors, extras = validate(palette, sprites)
    errors += v_errors
    print(f"{len(sprites)} sprites, {len(palette)} colours in {os.path.abspath(args.file)}")
    if extras:
        print("extra sprites (not required):", ", ".join(extras))
    for e in errors:
        print("ERROR:", e)

    os.makedirs(args.out, exist_ok=True)
    images = {name: sprite_image(palette, spr) for name, spr in sprites.items()}
    sheet_path = os.path.join(args.out, "sheet.png")
    mock_path = os.path.join(args.out, "mock.png")
    render_sheet(palette, sprites, images, sheet_path)
    render_mock(palette, sprites, images, mock_path)
    print("wrote", sheet_path)
    print("wrote", mock_path)
    if errors:
        print(f"VALIDATION FAILED: {len(errors)} error(s)")
        return 1
    print("VALIDATION OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
