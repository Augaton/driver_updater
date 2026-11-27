"""Draws the app icon and the installer's images, so they can be redrawn.

    python3 installer/art/make-art.py

Writes src/DriverUpdater/app.ico, docs/logo.png and installer/art/*.bmp. Needs Pillow. The text uses Segoe UI
Semibold on Windows, a bold system font found with fc-match on Linux, or Pillow's default font.

The look follows Windows 11, as the app does: a flat tile in Windows' default blue, a white
lightning bolt, no gradient and no glow; the installer's images on its dark neutral gray.
"""
import os
import subprocess

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
BLUE = (0, 103, 192)  # Windows 11's default accent, as its own buttons draw it
WHITE = (255, 255, 255)
VOID, INK = (32, 32, 32), (243, 243, 243)  # the app's dark palette
BOLT = [(0.57, 0.12), (0.27, 0.55), (0.47, 0.55), (0.40, 0.88), (0.74, 0.42), (0.54, 0.42), (0.63, 0.12)]


def icon(size):
    """The tile and its bolt, drawn four times larger, then scaled down for clean edges."""
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle((0, 0, s - 1, s - 1), radius=s * 0.22, fill=(*BLUE, 255))
    # The bolt a little smaller than its box, centered: it keeps a margin of blue at 16 pixels.
    x0, y0, side = s * 0.12, s * 0.10, s * 0.78
    draw.polygon([(x0 + px * side, y0 + py * side) for px, py in BOLT], fill=(*WHITE, 255))
    return img.resize((size, size), Image.LANCZOS)


def font(size):
    for path in (r"C:\Windows\Fonts\seguisb.ttf", r"C:\Windows\Fonts\segoeuib.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    for name in ("Cantarell:bold", "Adwaita Sans:semibold", "DejaVu Sans:bold"):
        try:
            path = subprocess.run(["fc-match", "-f", "%{file}", name], capture_output=True, text=True, check=True).stdout
            if path:
                return ImageFont.truetype(path, size)
        except (OSError, subprocess.CalledProcessError):
            pass
    return ImageFont.load_default(size)


def centered(draw, text, y, size, color, width):
    face = font(size)
    draw.text(((width - draw.textlength(text, font=face)) / 2, y), text, font=face, fill=color)


def wizard(scale):
    """The tall image on the setup's first and last pages (164 x 314 at 100 %)."""
    w, h = 164 * scale, 314 * scale
    img = Image.new("RGBA", (w, h), (*VOID, 255))
    logo = icon(80 * scale)
    img.alpha_composite(logo, ((w - logo.width) // 2, int(h * 0.22)))
    draw = ImageDraw.Draw(img)
    centered(draw, "Driver Updater", h * 0.55, 18 * scale, INK, w)
    return img.convert("RGB")


def small(scale):
    """The small image at the top right of the setup's inner pages (55 x 55 at 100 %)."""
    s = 55 * scale
    img = Image.new("RGBA", (s, s), (*VOID, 255))
    logo = icon(s - 10 * scale)
    img.alpha_composite(logo, (5 * scale, 5 * scale))
    return img.convert("RGB")


if __name__ == "__main__":
    big = icon(256)
    big.save(os.path.join(ROOT, "src", "DriverUpdater", "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    big.save(os.path.join(ROOT, "docs", "logo.png"))  # the README's
    for scale, suffix in ((1, ""), (2, "-200")):
        wizard(scale).save(os.path.join(HERE, f"wizard{suffix}.bmp"))
        small(scale).save(os.path.join(HERE, f"small{suffix}.bmp"))
    print("art written")
