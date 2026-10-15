"""Draws the app icon and the installer's images (neon on near-black), so they can be redrawn.

    python3 installer/art/make-art.py

Writes src/DriverUpdater/app.ico and installer/art/*.bmp. Needs Pillow; the text uses a bold
system font found with fc-match (Linux), or Pillow's default font.
"""
import os
import subprocess

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
CYAN, VIOLET = (0, 229, 255), (180, 77, 255)
VOID, PANEL, EDGE = (7, 9, 15), (18, 25, 39), (31, 42, 63)
BOLT = [(0.57, 0.12), (0.27, 0.55), (0.47, 0.55), (0.40, 0.88), (0.74, 0.42), (0.54, 0.42), (0.63, 0.12)]


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def gradient(size, start, end, diagonal=True):
    w, h = size
    img = Image.new("RGB", size)
    px = img.load()
    for y in range(h):
        for x in range(w):
            t = (x / w + y / h) / 2 if diagonal else y / h
            px[x, y] = mix(start, end, t)
    return img


def bolt(canvas, box, glow=True):
    """A neon lightning bolt with its glow, inside box (x, y, size) of an RGBA canvas."""
    x0, y0, s = box
    mask = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(mask).polygon([(x0 + px * s, y0 + py * s) for px, py in BOLT], fill=255)
    if glow:
        halo = mask.filter(ImageFilter.GaussianBlur(s * 0.06))
        canvas.alpha_composite(Image.merge("RGBA", (*Image.new("RGB", canvas.size, CYAN).split(), halo.point(lambda v: v * 0.8))))
    fill = gradient(canvas.size, CYAN, VIOLET).convert("RGBA")
    canvas.paste(fill, (0, 0), mask)


def icon(size):
    s = size * 4  # drawn large, then scaled down for clean edges
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    plate = Image.new("L", (s, s), 0)
    ImageDraw.Draw(plate).rounded_rectangle((0, 0, s - 1, s - 1), radius=s * 0.22, fill=255)
    img.paste(gradient((s, s), PANEL, VOID, diagonal=False).convert("RGBA"), (0, 0), plate)
    ring = Image.new("L", (s, s), 0)
    width = max(4, s // 40)
    ImageDraw.Draw(ring).rounded_rectangle((width, width, s - 1 - width, s - 1 - width), radius=s * 0.2, outline=255, width=width)
    img.paste(gradient((s, s), CYAN, VIOLET).convert("RGBA"), (0, 0), ring)
    bolt(img, (s * 0.08, s * 0.06, s * 0.86))
    return img.resize((size, size), Image.LANCZOS)


def font(size):
    for name in ("Cantarell:extrabold", "Adwaita Sans:black", "DejaVu Sans:bold:condensed"):
        try:
            path = subprocess.run(["fc-match", "-f", "%{file}", name], capture_output=True, text=True, check=True).stdout
            if path:
                return ImageFont.truetype(path, size)
        except (OSError, subprocess.CalledProcessError):
            pass
    return ImageFont.load_default(size)


def wizard(scale):
    """The tall image on the setup's first and last pages (164 x 314 at 100 %)."""
    w, h = 164 * scale, 314 * scale
    img = gradient((w, h), PANEL, VOID, diagonal=False).convert("RGBA")
    lines = ImageDraw.Draw(img)
    for i, offset in enumerate((-0.2, 0.15, 0.5)):  # faint diagonal neon streaks
        color = (*mix(CYAN, VIOLET, i / 2), 70)
        lines.line([(0, h * (0.62 + offset)), (w, h * (0.38 + offset))], fill=color, width=max(1, scale))
    logo = icon(96 * scale)
    img.alpha_composite(logo, ((w - logo.width) // 2, int(h * 0.16)))
    draw = ImageDraw.Draw(img)
    big = font(26 * scale)
    for text, y, color in (("DRIVER", 0.62, CYAN), ("UPDATER", 0.71, (232, 238, 247))):
        width = draw.textlength(text, font=big)
        draw.text(((w - width) / 2, h * y), text, font=big, fill=color)
    return img.convert("RGB")


def small(scale):
    """The small image at the top right of the setup's inner pages (55 x 55 at 100 %)."""
    s = 55 * scale
    img = Image.new("RGBA", (s, s), (*PANEL, 255))
    logo = icon(s - 6 * scale)
    img.alpha_composite(logo, (3 * scale, 3 * scale))
    return img.convert("RGB")


if __name__ == "__main__":
    big = icon(256)
    big.save(os.path.join(ROOT, "src", "DriverUpdater", "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    for scale, suffix in ((1, ""), (2, "-200")):
        wizard(scale).save(os.path.join(HERE, f"wizard{suffix}.bmp"))
        small(scale).save(os.path.join(HERE, f"small{suffix}.bmp"))
    print("art written")
