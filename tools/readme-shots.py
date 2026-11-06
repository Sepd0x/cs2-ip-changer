"""Framed screenshots and crops for the README and docs/FEATURES.md.

Reads docs/screenshots/*.png (captures of the 1240x800 window, at 2x since 5 nov.: 2480x1600)
and writes to docs/assets/shots/, at the captures' scale:
  <name>.png / <name>.webp           every screenshot, framed
  crop-<group>.png / crop-<group>.webp  one panel per feature group, framed

The frame: small rounded corners, a 1 px grey edge and a soft shadow on a transparent
background, so the dark window sits on GitHub's light and dark pages alike.
The WebP files are lossless (same pixels, about a quarter of the size).

Run it again after new captures:  python tools/readme-shots.py
Needs Pillow (pip install pillow). The crop rectangles and the frame are fixed, in pixels of
the 1240x800 window (multiplied by the capture's scale): if the app's layout moves, fix CROPS
below and run it again. node tools/shots.mjs runs this for you.
"""

import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "screenshots"
OUT = ROOT / "docs" / "assets" / "shots"
SIZE = (1240, 800)

# group name -> (screenshot, (left, top, right, bottom)) in the 1240x800 capture
CROPS = {
    "protection": ("connection", (662, 44, 1227, 378)),  # Checks
    "change-ip": ("connection", (94, 44, 659, 274)),  # Status: IP servers see, home IP, Change IP
    "servers": ("servers", (108, 186, 799, 580)),  # an open server with its players and flags
    "accounts": ("accounts", (378, 146, 936, 546)),  # four account cards with bans and cooldowns
    "regions": ("regions", (98, 262, 806, 650)),  # Europe with the ping of each region
    "configs": ("configs", (660, 192, 1227, 486)),  # Compare, with both crosshairs
}

# the social preview's piece of the Connection page, in pixels of the 1x capture (its border and top line included)
STATUS = (82, 0, 662, 296)
STATUS_OUT = ROOT / "docs" / "assets" / "src" / "social-preview-status.png"

# frame for full screenshots, frame for crops
FULL = dict(radius=10, margin=24, blur=12, offset=4, shadow=0.30)
CROP = dict(radius=8, margin=16, blur=8, offset=3, shadow=0.26)
EDGE = (128, 128, 128, 110)  # mid grey, partly transparent: darker than white, lighter than #0d1117
SS = 4  # supersampling for smooth corners


def rounded_mask(size, radius, inset=0):
    w, h = size
    big = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(big).rounded_rectangle(
        (inset * SS, inset * SS, (w - inset) * SS - 1, (h - inset) * SS - 1),
        radius=max(0, radius - inset) * SS,
        fill=255,
    )
    return big.resize(size, Image.LANCZOS)


def frame(img, radius, margin, blur, offset, shadow):
    img = img.convert("RGBA")
    w, h = img.size
    outer = rounded_mask((w, h), radius)
    inner = rounded_mask((w, h), radius, inset=1)

    # the window: clipped to the rounded shape, with a 1 px edge on top
    win = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    win.paste(img, (0, 0), outer)
    ring = ImageChops.subtract(outer, inner)
    edge = Image.new("RGBA", (w, h), EDGE)
    edge.putalpha(ImageChops.multiply(ring, Image.new("L", (w, h), EDGE[3])))
    win = Image.alpha_composite(win, edge)

    # the shadow: the same shape, black, blurred, a little lower
    W, H = w + 2 * margin, h + 2 * margin
    sh = Image.new("L", (W, H), 0)
    sh.paste(outer.point(lambda a: int(a * shadow)), (margin, margin + offset))
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.putalpha(sh)
    canvas.alpha_composite(win, (margin, margin))
    return canvas


def save(img, stem):
    png, webp = OUT / f"{stem}.png", OUT / f"{stem}.webp"
    img.save(png, optimize=True)
    img.save(webp, "WEBP", lossless=True, quality=100, method=6)
    print(f"  {png.relative_to(ROOT).as_posix()}  {img.size[0]}x{img.size[1]}  png {png.stat().st_size // 1024} KB, webp {webp.stat().st_size // 1024} KB")


def scaled(params, k):
    """The frame's sizes at the capture's scale (the shadow's strength stays)."""
    return {n: (v if n == "shadow" else round(v * k)) for n, v in params.items()}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    shots = sorted(SRC.glob("*.png"))
    if not shots:
        sys.exit(f"No screenshots in {SRC}")
    loaded, scale, raw = {}, {}, {}
    for p in shots:
        im = Image.open(p).convert("RGBA")
        k = im.size[0] / SIZE[0]
        if k not in (1, 2, 3) or im.size != (SIZE[0] * k, SIZE[1] * k):
            sys.exit(f"{p.name} is {im.size[0]}x{im.size[1]}: expected {SIZE[0]}x{SIZE[1]} times 1, 2 or 3")
        k = int(k)
        raw[p.stem] = im
        # the capture has a 1 px dark border around the window (k px at k x); the frame draws its own edge
        loaded[p.stem], scale[p.stem] = im.crop((k, k, im.size[0] - k, im.size[1] - k)), k
        save(frame(loaded[p.stem], **scaled(FULL, k)), p.stem)

    missing = []
    for group, (name, (l, t, r, b)) in CROPS.items():
        if name not in loaded:
            missing.append(f"{name}.png (for crop-{group})")
            continue
        # the rectangles are in 1x window pixels; the loaded image is k times bigger and lost k px on each side
        k = scale[name]
        save(frame(loaded[name].crop(((l - 1) * k, (t - 1) * k, (r - 1) * k, (b - 1) * k)), **scaled(CROP, k)), f"crop-{group}")
    if missing:
        sys.exit("Missing: " + ", ".join(missing))

    # the social preview card (docs/assets/src/social-preview.html): the top line, the title bar and Status, unframed
    if "connection" in loaded:
        k = scale["connection"]
        l, t, r, b = STATUS
        status = raw["connection"].crop((l * k, t * k, r * k, b * k)).convert("RGB")
        status.save(STATUS_OUT, optimize=True)
        print(f"  {STATUS_OUT.relative_to(ROOT).as_posix()}  {status.size[0]}x{status.size[1]}")


if __name__ == "__main__":
    main()
