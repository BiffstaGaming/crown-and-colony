"""Generate a COMPLETE Australian top-down terrain set in a chosen visual style (WS2.5d).

Usage:  python tools/terrain_style.py flat|soft

The photographic style (build_topdown_terrain.py, CC0 ambientCG scans re-toned) reads as
muddy and busy at the 64px game tile — real photographic grain averaged down to 64 pixels
becomes noise rather than detail. These are two alternatives at the same geometry, so all
three can be compared in-game on identical ground:

  flat  — stylised board-game / strategy-map look: a solid Australian colour per terrain with
          only a whisper of grain. Maximum legibility at small zoom, no mud.
  soft  — painterly: the same palette with gentle low-frequency mottling, closer in feel to
          FreeCol's hand-painted tiles but drawn square for top-down.

Both are our own original work (GPL v2) and seamless by construction — every varying term is
a sinusoid at an INTEGER frequency over the tile, so the pattern wraps exactly at the edges.
"""
import math, os, random, sys
from PIL import Image

DST = "game/assets/australia/terrain"
SIDE = 64

# One Australian palette, shared by both styles, so a style choice is about TEXTURE not colour.
PALETTE = {
    "desert":          (166,  78,  52),   # the red centre
    "plains":          (198, 172, 114),   # straw
    "prairie":         (206, 186, 133),   # bleached grazing country
    "savannah":        (188, 156,  86),   # ochre grass
    "grassland":       (170, 158,  84),   # dry gold pasture
    "marsh":           (134, 145,  94),   # olive wetland
    "swamp":           (108, 120,  76),
    "hills":           (150, 104,  72),   # red-brown rock
    "mountains":       (124,  84,  62),
    "ocean":           ( 44,  92, 128),
    "highSeas":        ( 32,  72, 106),
    "tundra":          (176, 166, 132),   # never on the Australia map, but stops a snow tile leaking through
    "broadleafForest": ( 96, 112,  70),   # eucalypt canopy: grey-green
    "mixedForest":     ( 92, 108,  68),
    "coniferForest":   ( 84, 100,  64),
    "borealForest":    ( 80,  96,  62),
    "scrubForest":     (140, 132,  82),   # mallee: drier, sparser
    "tropicalForest":  ( 74, 116,  62),   # the far north really is green
    "rainForest":      ( 66, 106,  56),
    "wetlandForest":   ( 88, 110,  74),
}

def clamp(v): return max(0, min(255, int(v)))

def make(base, style, seed):
    im = Image.new("RGBA", (SIDE, SIDE))
    px = im.load()
    rng = random.Random(seed)
    if style == "flat":
        waves, amp, grain = [], 0.0, 4          # near-solid: just a whisper of grain
    else:                                        # soft
        waves = [(1, 2, 0.45), (2, -1, 0.32), (3, 2, 0.23)]
        amp, grain = 16.0, 5
    phase = rng.uniform(0, math.tau)
    for y in range(SIDE):
        for x in range(SIDE):
            v = 0.0
            for fx, fy, w in waves:
                v += w * math.sin(math.tau * (fx * x / SIDE + fy * y / SIDE) + phase)
            n = rng.uniform(-grain, grain)
            px[x, y] = (clamp(base[0] + v * amp + n),
                        clamp(base[1] + v * amp * 0.92 + n),
                        clamp(base[2] + v * amp * 0.78 + n), 255)
    return im

style = (sys.argv[1] if len(sys.argv) > 1 else "flat").lower()
assert style in ("flat", "soft"), "style must be flat or soft"
made = 0
for terrain, base in PALETTE.items():
    d = os.path.join(DST, terrain)
    os.makedirs(d, exist_ok=True)
    for idx in (0, 1):
        make(base, style, seed=hash((terrain, idx)) & 0xffff).save(os.path.join(d, f"top{idx}.png"))
        made += 1
print(f"{made} tiles written in '{style}' style")
