"""Re-tone FreeCol's terrain art for the Australian variant (WS2.5, tier 1).

Reads game/assets/freecol/<terrain|forest>/... and writes recoloured copies to
game/assets/australia/... where the variant art seam picks them up automatically.
FreeCol's originals are never modified.

The transform is a HUE ROTATION + saturation/value scale in HSV: every pixel keeps its
own relative hue variation and all of its luminance structure (so the painted texture,
shading and tile edges survive intact) — only the centre of the colour range moves.
"""
import colorsys, os
from PIL import Image

SRC = "game/assets/freecol"
DST = "game/assets/australia"

# terrain -> (target hue degrees, saturation multiplier, value multiplier)
TERRAIN = {
    "grassland": (58, 0.72, 1.00),   # emerald -> dry olive-gold pasture
    "plains":    (48, 0.78, 1.02),   # straw
    "prairie":   (45, 0.72, 1.03),   # bleached grazing country
    "savannah":  (38, 0.85, 1.00),   # ochre grass
    "desert":    (14, 2.05, 0.80),   # THE RED CENTRE - burnt ochre, not pale sand (the FreeCol original is a very desaturated beige, so it needs a big saturation lift)
    "marsh":     (62, 0.70, 0.98),   # muted olive wetland
    "swamp":     (68, 0.68, 0.95),
    "hills":     (22, 1.05, 0.98),   # red-brown rock
    "mountains": (20, 1.05, 0.96),
    # ocean / highSeas / arctic / tundra / unexplored deliberately untouched:
    # the sea is the sea, and the polar tiles never appear on the Australia map.
}

# forest -> eucalypt: grey-green, desaturated. FreeCol's boreal/conifer are wrong for
# Australia but re-pointing the DATA is a separate job; re-toning them at least stops
# them reading as North American pine.
FOREST = {
    # Eucalypt is a DUSTY GREY-GREEN. The FreeCol originals already sit at ~82 deg hue, so a hue target near that
    # does nothing visible - the change that actually reads is dropping saturation hard and lifting value.
    "broadleaf": (72, 0.52, 0.98),
    "mixed":     (70, 0.50, 0.98),
    "conifer":   (74, 0.48, 0.96),
    "boreal":    (76, 0.46, 0.96),
    "scrub":     (58, 0.55, 1.00),   # mallee / dry scrub - the driest of them
    "tropical":  (92, 0.62, 1.00),   # the far north stays genuinely green
    "rain":      (95, 0.66, 0.98),
    "wetland":   (68, 0.45, 1.00),
}

def retone(img, target_hue, sat_mul, val_mul):
    img = img.convert("RGBA")
    px = img.load()
    w, h = img.size
    # mean hue of the opaque, coloured pixels -> how far to rotate
    hs, n = 0.0, 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 16: continue
            hh, ss, vv = colorsys.rgb_to_hsv(r/255, g/255, b/255)
            if ss < 0.08: continue          # near-grey carries no usable hue
            hs += hh; n += 1
    if n == 0:
        return img
    mean_hue = hs / n
    shift = (target_hue / 360.0) - mean_hue

    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0: continue
            hh, ss, vv = colorsys.rgb_to_hsv(r/255, g/255, b/255)
            hh = (hh + shift) % 1.0
            ss = max(0.0, min(1.0, ss * sat_mul))
            vv = max(0.0, min(1.0, vv * val_mul))
            nr, ng, nb = colorsys.hsv_to_rgb(hh, ss, vv)
            px[x, y] = (int(nr*255), int(ng*255), int(nb*255), a)
    return img

def run(kind, table):
    made = 0
    for name, (hue, sat, val) in table.items():
        srcdir = os.path.join(SRC, kind, name)
        if not os.path.isdir(srcdir):
            print("  skip (absent):", kind, name); continue
        dstdir = os.path.join(DST, kind, name)
        os.makedirs(dstdir, exist_ok=True)
        for f in sorted(os.listdir(srcdir)):
            if not f.endswith(".png"): continue
            out = retone(Image.open(os.path.join(srcdir, f)), hue, sat, val)
            out.save(os.path.join(dstdir, f))
            made += 1
        print(f"  {kind}/{name}: retoned")
    return made

print("terrain:"); n1 = run("terrain", TERRAIN)
print("forest:");  n2 = run("forest", FOREST)
print(f"\n{n1 + n2} tiles written to {DST}")
