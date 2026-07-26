"""Australian top-down terrain, generated as NATURAL ground (WS2.5e).

Why the earlier attempts failed, so this isn't repeated:
  * photographic CC0 scans -> real grain averaged down to a 64px tile stops being detail and
    becomes noise; it reads as mud.
  * flat / softly-mottled colour -> reads as fabric or plastic, not land. Ground looks like
    ground because it has structure at SEVERAL scales at once (broad patches, mid-scale
    clumping, fine speckle), not one uniform wobble.
  * every terrain colour was picked independently, so the tiles did not sit together as one
    landscape.

So this does two things differently:

1. FRACTAL NOISE. Tileable value noise summed over several octaves (broad -> fine), which is
   the standard way to get organic ground. Seamless by construction: each octave's lattice
   wraps modulo its own resolution, so the pattern is continuous across tile edges.

2. ONE HARMONISED PALETTE. Every terrain is defined in HSV inside a deliberately narrow,
   shared family -- land hues held in a warm 22-62 deg band, foliage 70-96, water 198-212 --
   with saturation and value SPACED rather than chosen ad hoc, and a single warm light tint
   applied to everything at the end so the whole map reads as one place under one sun.

Usage: python tools/terrain_natural.py [earth|patchy|painted]
Our own original work (GPL v2).
"""
import colorsys, math, os, sys
import numpy as np
from PIL import Image

DST = "game/assets/australia/terrain"
SIDE = 64

# (hue deg, saturation, value, contrast) — one family, spaced on purpose.
# 'contrast' is how strongly the fractal noise modulates value for that ground.
PALETTE = {
    "desert":          (22, 0.52, 0.62, 0.16),   # red centre
    "plains":          (44, 0.40, 0.70, 0.14),   # straw
    "prairie":         (46, 0.34, 0.74, 0.13),   # bleached grazing country
    "savannah":        (40, 0.46, 0.66, 0.15),   # ochre grass
    "grassland":       (52, 0.42, 0.62, 0.15),   # dry gold pasture
    "marsh":           (62, 0.34, 0.52, 0.14),   # olive wetland
    "swamp":           (66, 0.32, 0.44, 0.14),
    "hills":           (28, 0.44, 0.54, 0.20),   # red-brown rock, more relief
    "mountains":       (26, 0.40, 0.46, 0.24),
    "tundra":          (44, 0.20, 0.68, 0.10),   # not on the Australia map; stops snow leaking through
    "broadleafForest": (78, 0.34, 0.44, 0.18),   # eucalypt: grey-green, never emerald
    "mixedForest":     (76, 0.34, 0.43, 0.18),
    "coniferForest":   (82, 0.32, 0.40, 0.18),
    "borealForest":    (84, 0.30, 0.38, 0.18),
    "scrubForest":     (58, 0.36, 0.54, 0.20),   # mallee: drier, patchier
    "tropicalForest":  (92, 0.40, 0.42, 0.17),   # the far north really is green
    "rainForest":      (96, 0.42, 0.38, 0.17),
    "wetlandForest":   (70, 0.34, 0.46, 0.17),
    "ocean":           (205, 0.58, 0.46, 0.05),  # water: almost flat, it is not ground
    "highSeas":        (210, 0.60, 0.38, 0.04),
}

# A single warm light over the whole map — the main thing that makes tiles read as one place.
LIGHT = (1.03, 1.00, 0.94)

def tileable_noise(side, res, rng):
    """Value noise on a lattice that wraps at `res`, smoothstep-interpolated up to `side`."""
    lat = rng.random((res, res))
    ys, xs = np.meshgrid(np.arange(side), np.arange(side), indexing="ij")
    fy, fx = ys * res / side, xs * res / side
    y0, x0 = np.floor(fy).astype(int) % res, np.floor(fx).astype(int) % res
    y1, x1 = (y0 + 1) % res, (x0 + 1) % res          # wrap -> seamless
    ty, tx = fy - np.floor(fy), fx - np.floor(fx)
    sy, sx = ty*ty*(3-2*ty), tx*tx*(3-2*tx)          # smoothstep
    top = lat[y0, x0]*(1-sx) + lat[y0, x1]*sx
    bot = lat[y1, x0]*(1-sx) + lat[y1, x1]*sx
    return top*(1-sy) + bot*sy

def fractal(side, octaves, rng):
    total = np.zeros((side, side)); amp = 1.0; norm = 0.0
    for res in octaves:
        total += amp * tileable_noise(side, res, rng)
        norm += amp; amp *= 0.5
    return total / norm

def build(style, terrain, spec, seed):
    hue, sat, val, contrast = spec
    rng = np.random.default_rng(seed)
    if style == "patchy":
        octaves = [2, 4, 16]          # broad clumping dominates
    elif style == "painted":
        octaves = [4, 8, 16]
    else:                              # earth
        octaves = [4, 8, 16, 32]      # includes a fine speckle octave

    n = fractal(SIDE, octaves, rng)
    n = (n - n.min()) / (np.ptp(n) + 1e-9) - 0.5        # centre on 0

    if style == "painted":
        n = np.round(n * 5) / 5                        # posterise: hand-painted map look

    # Value carries the structure; saturation drifts slightly the other way so lighter ground
    # also reads as drier, which is what real sun-bleached country does.
    v = np.clip(val + n * contrast * 2.0, 0.05, 0.98)
    s = np.clip(sat - n * contrast * 0.35, 0.03, 0.95)
    h = np.full_like(v, hue / 360.0)

    rgb = np.zeros((SIDE, SIDE, 3))
    for y in range(SIDE):
        for x in range(SIDE):
            rgb[y, x] = colorsys.hsv_to_rgb(h[y, x], s[y, x], v[y, x])
    rgb *= np.array(LIGHT)
    return Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8), "RGB").convert("RGBA")

style = (sys.argv[1] if len(sys.argv) > 1 else "earth").lower()
assert style in ("earth", "patchy", "painted")
made = 0
for terrain, spec in PALETTE.items():
    d = os.path.join(DST, terrain); os.makedirs(d, exist_ok=True)
    for idx in (0, 1):
        build(style, terrain, spec, seed=abs(hash((terrain, idx, style))) % (2**32)).save(
            os.path.join(d, f"top{idx}.png"))
        made += 1
print(f"{made} tiles written in '{style}' style")
