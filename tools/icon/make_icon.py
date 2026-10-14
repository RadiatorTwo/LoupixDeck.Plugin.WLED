#!/usr/bin/env python3
"""LoupixDeck WLED plugin icon (a curved LED strip with rainbow-colored LEDs, matte, night blue).

Same look as the Audio and CoolerControl plugin icons: background, colors and shading are
shared; the subject is a flexible LED strip in a gentle wave whose LEDs glow in rainbow colors.

Requires: pip install pillow numpy
Usage:    python make_icon.py [output_dir]
Writes icon_{256,128,64,32,16}.png (RGBA, transparent corners).
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256   # design size (px)
SS = 4       # supersampling
N = SIZE * SS
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)
XX = (XX + 0.5) / SS
YY = (YY + 0.5) / SS


def oklch(L, C, h, a=1.0):
    hr = math.radians(h)
    A, B = C * math.cos(hr), C * math.sin(hr)
    l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3
    m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3
    s = (L - 0.0894841775 * A - 1.2914855480 * B) ** 3
    lin = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
           -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
           -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s]
    out = [12.92 * c if c <= 0.0031308 else 1.055 * max(c, 0) ** (1 / 2.4) - 0.055 for c in lin]
    return (*[min(max(c, 0.0), 1.0) for c in out], a)



# Colors (background shared with the Audio icon)
BG = oklch(0.24, 0.02, 260)
EDGE = oklch(0.32, 0.02, 260)

# ---------- Masks ----------
def _mask(draw_fn):
    im = Image.new("L", (N, N), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im, dtype=np.float32) / 255.0


def circle(cx, cy, r):
    return _mask(lambda d: d.ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS - 1, (cy + r) * SS - 1], fill=255))


def rrect(x, y, w, h, r):
    return _mask(lambda d: d.rounded_rectangle([x * SS, y * SS, (x + w) * SS - 1, (y + h) * SS - 1], radius=r * SS, fill=255))


def polygon(points):
    return _mask(lambda d: d.polygon([(x * SS, y * SS) for x, y in points], fill=255))


def blur(mask, px):
    if px <= 0:
        return mask
    im = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    im = im.filter(ImageFilter.GaussianBlur(px / 2 * SS))  # CSS blur = 2*sigma
    return np.asarray(im, dtype=np.float32) / 255.0


def shift(mask, dx, dy, fill=0.0):
    out = np.full_like(mask, fill)
    sx, sy = int(round(dx * SS)), int(round(dy * SS))
    h, w = mask.shape
    out[max(sy, 0):h + min(sy, 0), max(sx, 0):w + min(sx, 0)] = mask[max(-sy, 0):h + min(-sy, 0), max(-sx, 0):w + min(-sx, 0)]
    return out


# ---------- Compositing ----------
canvas = np.zeros((N, N, 4), dtype=np.float32)  # straight RGBA


def paint(color, alpha):
    """color: RGBA tuple or HxWx3 array; alpha: HxW mask (multiplied by the color's alpha)."""
    global canvas
    if isinstance(color, tuple):
        rgb = np.array(color[:3], dtype=np.float32)[None, None, :]
        a = alpha * color[3]
    else:
        rgb, a = color, alpha
    a = a[..., None]
    ca = canvas[..., 3:4]
    oa = a + ca * (1 - a)
    orgb = (rgb * a + canvas[..., :3] * ca * (1 - a)) / np.maximum(oa, 1e-6)
    canvas = np.concatenate([orgb, oa], axis=-1)


def drop_shadow(shape, dx, dy, blur_px, color, clip):
    paint(color, blur(shift(shape, dx, dy), blur_px) * clip)


def inset_shadow(shape, dx, dy, blur_px, color):
    paint(color, blur(shift(1 - shape, dx, dy, fill=1.0), blur_px) * shape)


def linear_gradient(box, css_deg, stops):
    x, y, w, h = box
    th = math.radians(css_deg)
    dx, dy = math.sin(th), -math.cos(th)
    L = abs(w * dx) + abs(h * dy)
    t = ((XX - (x + w / 2)) * dx + (YY - (y + h / 2)) * dy) / L + 0.5
    t = np.clip(t, 0, 1)
    pos = [s[0] for s in stops]
    return np.stack([np.interp(t, pos, [s[1][i] for s in stops]) for i in range(3)], axis=-1).astype(np.float32)






# ---------- Strip geometry ----------
C = 128
WAVE_AMP = 20      # height of the wave (px)
RISE = 0.42        # the strip rises from lower left to upper right
X0, X1 = 30, 226   # strip ends
STRIP_W = 40       # tape width
LED_COUNT = 7
LED_SIZE = 22


def strip_y(x):
    return C + RISE * (C - x) - WAVE_AMP * math.sin((x - X0) / (X1 - X0) * 2 * math.pi)


def strip_tangent(x):
    h = 0.5
    return math.atan2(strip_y(x + h) - strip_y(x - h), 2 * h)


def strip_mask(width):
    im = Image.new("L", (N, N), 0)
    d = ImageDraw.Draw(im)
    # Stamped discs instead of a polyline: line segments leave seams that the shading turns into ticks.
    r = width / 2
    for x in np.linspace(X0, X1, 1200):
        y = strip_y(x)
        d.ellipse([(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS], fill=255)
    return np.asarray(im, dtype=np.float32) / 255.0


def rotated_square(cx, cy, size, angle, radius_frac=0.25):
    """Square rotated by angle, corners softened by a small blur."""
    h = size / 2
    ca, sa = math.cos(angle), math.sin(angle)
    pts = [(cx + ca * x - sa * y, cy + sa * x + ca * y) for x, y in ((-h, -h), (h, -h), (h, h), (-h, h))]
    return np.clip(blur(polygon(pts), size * radius_frac) * 1.6 - 0.3, 0, 1)


# ---------- Draw ----------
icon = rrect(0, 0, SIZE, SIZE, 58)

# Background + 1px inner edge
paint(BG, icon)
paint(EDGE, icon - rrect(1, 1, SIZE - 2, SIZE - 2, 57))

strip = strip_mask(STRIP_W)
led_xs = np.linspace(X0 + 14, X1 - 14, LED_COUNT)
hues = np.linspace(25, 330, LED_COUNT)  # red to magenta

# Colored halos of the LEDs on the background
for x, hue in zip(led_xs, hues):
    paint(oklch(0.72, 0.20, hue), blur(circle(x, strip_y(x), 20), 36) * 0.85 * icon)

# The tape: white flexible PCB, matte, with a drop shadow
drop_shadow(strip, 0, 10, 18, oklch(0.04, 0.04, 260, 0.70), icon)
drop_shadow(strip, 0, 3, 3, oklch(0.06, 0.03, 260, 0.50), icon)
paint(linear_gradient((0, 0, SIZE, SIZE), 160, [(0, oklch(0.93, 0.006, 260)[:3]), (1, oklch(0.74, 0.01, 260)[:3])]), strip)
inset_shadow(strip, 0, -3, 4, oklch(0.4, 0.02, 260, 0.35))
inset_shadow(strip, 0, 2, 2, (1, 1, 1, 0.45))

# Copper pads between the LEDs
for a, b in zip(led_xs[:-1], led_xs[1:]):
    x = (a + b) / 2
    pad = rotated_square(x, strip_y(x), 7, strip_tangent(x), 0.15)
    paint(oklch(0.70, 0.10, 65), pad)

# The LEDs: white housing with a glowing colored lens
for x, hue in zip(led_xs, hues):
    y, ang = strip_y(x), strip_tangent(x)
    housing = rotated_square(x, y, LED_SIZE, ang)
    drop_shadow(housing, 0, 1.5, 2, oklch(0.2, 0.02, 260, 0.45), strip)
    paint(oklch(0.97, 0.004, 260), housing)
    lens = circle(x, y, LED_SIZE * 0.36)
    paint(oklch(0.80, 0.19, hue), lens)
    paint(oklch(0.97, 0.05, hue), circle(x, y, LED_SIZE * 0.17) * 0.9)
    paint(oklch(0.85, 0.16, hue), blur(lens, 8) * 0.55 * icon)

# Clip to the icon shape
canvas[..., 3] *= icon

# ---------- Export ----------
if __name__ == "__main__":
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out_dir, exist_ok=True)
    big = Image.fromarray((np.clip(canvas, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    for s in (256, 128, 64, 32, 16):
        path = os.path.join(out_dir, f"icon_{s}.png")
        big.resize((s, s), Image.LANCZOS).save(path)
        print("written:", path)
