"""Generate the CSP suite icons per design-system.md section 7.

Glyph: two interlocking rings, level. Left ring passes over at the top crossing,
right ring passes over at the bottom.

  Mux       - base, single colour: both rings AccentBrush #72D2B1
  Companion - same glyph coloured: left AccentBrush, right WarningBrush #D8A25E

Sizes >= 24 scale the 256 master and keep the weave. 16 and 20 are hand-hinted
from a separate construction with the weave dropped (7.4) - a knockout gap at a
2px stroke renders as a grey smudge.
"""

import io
import struct
from PIL import Image, ImageDraw

ACCENT = (0x72, 0xD2, 0xB1, 255)   # AccentBrush
WARNING = (0xD8, 0xA2, 0x5E, 255)  # WarningBrush

SS = 16  # supersample factor


def _disc(size, cx, cy, r):
    """Antialiased filled disc as an L mask, drawn at SS then downsampled."""
    img = Image.new("L", (size * SS, size * SS), 0)
    d = ImageDraw.Draw(img)
    d.ellipse(
        [(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS],
        fill=255,
    )
    return img


def _annulus_hi(size, cx, cy, r_center, stroke):
    """Annulus mask at supersampled resolution."""
    outer = r_center + stroke / 2.0
    inner = r_center - stroke / 2.0
    img = Image.new("L", (size * SS, size * SS), 0)
    d = ImageDraw.Draw(img)
    d.ellipse([(cx - outer) * SS, (cy - outer) * SS,
               (cx + outer) * SS, (cy + outer) * SS], fill=255)
    d.ellipse([(cx - inner) * SS, (cy - inner) * SS,
               (cx + inner) * SS, (cy + inner) * SS], fill=0)
    return img


def _disc_hi(size, cx, cy, r):
    img = Image.new("L", (size * SS, size * SS), 0)
    d = ImageDraw.Draw(img)
    d.ellipse([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS], fill=255)
    return img


def _half_hi(size, cy, below=True):
    img = Image.new("L", (size * SS, size * SS), 0)
    d = ImageDraw.Draw(img)
    if below:
        d.rectangle([0, cy * SS, size * SS, size * SS], fill=255)
    else:
        d.rectangle([0, 0, size * SS, cy * SS], fill=255)
    return img


def _mul(a, b):
    return Image.composite(a, Image.new("L", a.size, 0), b)


def _sub(a, b):
    """a AND NOT b"""
    inv = b.point(lambda v: 255 - v)
    return _mul(a, inv)


def woven(size, color_left, color_right):
    """Master construction with the interlock weave (7.2).

    Left ring passes over at the top crossing, right ring at the bottom. Each
    ring is CUT where the other passes over it, with an 8-unit clearance on
    each side of the covering stroke - a 24 + 16 = 40 unit break. The cut is
    what makes the interlock read; in the Mux's monochrome version it is the
    only thing separating "interlocked" from "a figure eight" (7.3).
    """
    k = size / 256.0
    stroke = 24 * k
    r = 56 * k
    ax, ay = 96 * k, 128 * k
    bx, by = 160 * k, 128 * k
    r_inner = r - stroke / 2.0

    # centreline intersections: x = 128k, y = 128k +/- sqrt(r^2 - (d/2)^2)
    d = bx - ax
    dy = (r ** 2 - (d / 2.0) ** 2) ** 0.5
    mx = (ax + bx) / 2.0
    upper = (mx, ay - dy)
    lower = (mx, ay + dy)

    mask_a = _annulus_hi(size, ax, ay, r, stroke)
    mask_b = _annulus_hi(size, bx, by, r, stroke)

    # Each stroke dilated by 8 units per side -> the knockout gap.
    gap = 8 * k
    dil_a = _annulus_hi(size, ax, ay, r, stroke + 2 * gap)
    dil_b = _annulus_hi(size, bx, by, r, stroke + 2 * gap)

    # Localise each cut to its own crossing so the dilated annulus does not
    # nick the ring anywhere else. The two discs do not overlap: the crossings
    # are 2*dy apart (91.9 units at 256) and each disc is r_inner = 44.
    top_zone = _disc_hi(size, upper[0], upper[1], r_inner)
    bot_zone = _disc_hi(size, lower[0], lower[1], r_inner)

    mask_b = _sub(mask_b, _mul(dil_a, top_zone))  # A crosses over B at the top
    mask_a = _sub(mask_a, _mul(dil_b, bot_zone))  # B crosses over A at the bottom

    hi = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    hi.paste(Image.new("RGBA", hi.size, color_right), mask_b)
    hi.paste(Image.new("RGBA", hi.size, color_left), mask_a)
    return hi.resize((size, size), Image.LANCZOS)


def hinted(size, color_left, color_right, r, stroke, ca, cb):
    """Hand-hinted small construction: union of two annuli, weave dropped (7.4)."""
    ax, ay = ca
    bx, by = cb
    mask_a = _annulus_hi(size, ax, ay, r, stroke)
    mask_b = _annulus_hi(size, bx, by, r, stroke)
    hi = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    hi.paste(Image.new("RGBA", hi.size, color_right), mask_b)
    hi.paste(Image.new("RGBA", hi.size, color_left), mask_a)
    return hi.resize((size, size), Image.LANCZOS)


def build(color_left, color_right):
    frames = {}
    for s in (24, 32, 48, 64, 128, 256):
        frames[s] = woven(s, color_left, color_right)
    # 16 and 20 from the separate construction (7.4)
    frames[16] = hinted(16, color_left, color_right, r=4, stroke=2, ca=(5, 8), cb=(11, 8))
    frames[20] = hinted(20, color_left, color_right, r=5, stroke=2, ca=(6, 10), cb=(13, 10))
    return frames


def write_ico(frames, path):
    """Write a multi-resolution .ico with PNG-compressed entries (Vista+)."""
    sizes = sorted(frames)
    blobs = []
    for s in sizes:
        buf = io.BytesIO()
        frames[s].save(buf, format="PNG")
        blobs.append(buf.getvalue())

    out = bytearray()
    out += struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    for s, blob in zip(sizes, blobs):
        dim = 0 if s >= 256 else s
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    for blob in blobs:
        out += blob
    with open(path, "wb") as fh:
        fh.write(bytes(out))
    return len(out), sizes


if __name__ == "__main__":
    import sys
    outdir = sys.argv[1].rstrip("/\\")

    mux = build(ACCENT, ACCENT)
    comp = build(ACCENT, WARNING)

    n, sizes = write_ico(mux, outdir + "/csp-mux.ico")
    print(f"csp-mux.ico        {n:>7} bytes  sizes={sizes}")
    n, sizes = write_ico(comp, outdir + "/csp-palette-companion.ico")
    print(f"csp-palette-companion.ico {n:>7} bytes  sizes={sizes}")

    # contact sheet for visual review, on the window background #1C1D21
    sheet = Image.new("RGBA", (760, 200), (0x1C, 0x1D, 0x21, 255))
    x = 16
    for label, frames in (("mux", mux), ("companion", comp)):
        for s in sorted(frames):
            sheet.alpha_composite(frames[s], (x, 16 if label == "mux" else 120))
            x += s + 12
        x = 16
    sheet = sheet.resize((1520, 400), Image.NEAREST)
    sheet.save(outdir + "/icon_sheet.png")
    print("icon_sheet.png written")
