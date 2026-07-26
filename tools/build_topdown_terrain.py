"""Build the Australian TOP-DOWN terrain tiles (WS2.5b).

Top-down is the projection Chris expects to be the game's main view, and it does NOT have
its own art: MapView warps the inscribed diamond of each 128x64 isometric tile onto a 64px
square. That has two unavoidable defects — art drawn as diamonds cannot tile as squares, so
the de-skew shows SEAMS and repeating diagonal artefacts, and only part of each source image
is ever sampled.

So top-down gets native square tiles instead, built in two stages:

  1. a CC0 seamless ground texture (ambientCG, public domain) downscaled to the 64px game
     tile — this is what actually fixes the seams; and
  2. the SAME Australian hue/saturation transform used for the isometric art, so the two
     projections read as the same country.

Inputs live in tools/cc0-ground/ (the CC0 tiles, committed so this runs offline). They are built by
cropping a 256px region of ambientCG's seamless 1K colour map and scaling 4:1 to the 64px game tile —
a straight 1024->64 downscale averages away all the grain and the tile reads as a flat colour block
on screen. The crop stays seamless because the source tiles and 256 divides 1024 exactly.
Outputs go to game/assets/australia/terrain/<name>/top0.png, which MapView prefers in
top-down and falls back from cleanly when absent.
"""
import colorsys, os
from PIL import Image

SRC = "tools/cc0-ground"
DST = "game/assets/australia/terrain"

# terrain -> (CC0 source tile, target hue deg, saturation multiplier, value multiplier)
# The hue/sat/val numbers mirror retone_australian_terrain.py so both projections match.
PLAN = {
    "desert":    ("Ground054", 12, 2.0, 0.86),   # speckled dirt -> the red centre (Ground080 was too smooth: it read as a flat colour block in game)
    "plains":    ("Ground078", 48, 1.1, 1.02),   # dry stubble -> straw
    "prairie":   ("Ground078", 45, 1.0, 1.08),   # same base, lighter
    "savannah":  ("Ground078", 38, 1.3, 0.98),   # ochre grass
    "grassland": ("Grass004",  58, 1.0, 0.92),   # green pasture -> dry gold
    "marsh":     ("Ground048", 62, 1.1, 1.15),   # dark soil -> olive wetland
    "swamp":     ("Ground048", 68, 1.0, 1.05),
    "hills":     ("Rock061",   22, 1.9, 1.25),   # grey rock -> red-brown
    "mountains": ("Rock061",   20, 1.8, 1.10),
    # arctic / tundra deliberately absent: they never appear on the Australia map, so they fall
    # back to the de-skewed FreeCol tile.
}

# Water gets PROCEDURAL square tiles rather than sourced ones. ambientCG is a PBR material library
# and has no usable sea texture (water is normally a shader, not an image), and the de-skewed
# FreeCol ocean shows obvious diagonal wave artefacts in top-down — over a big share of the screen,
# since the Australia map is mostly coastline. These are our own original work (GPL v2, no third-party
# licence involved): tileable sinusoidal noise, so they are seamless by construction.
# Kept DELIBERATELY subtle. A first attempt with a wide trough/crest range produced strong diagonal
# banding that read worse than the artefacts it replaced — at strategy-map scale the sea should be a
# near-flat colour with only a hint of movement, not a wave pattern.
WATER = {
    "ocean":    ((30, 68, 100), (35, 76, 110)),  # (trough, crest) — coastal sea
    "highSeas": ((22, 52, 80), (26, 59, 89)),    # darker open ocean
}

def water_tile(dark, light, side=64, seed=0):
    """A seamless 64px sea tile: a sum of sinusoids at INTEGER frequencies over the tile, so the
    pattern wraps exactly at the edges and no seam can appear however it is tiled."""
    import math
    im = Image.new("RGBA", (side, side))
    px = im.load()
    # Mixed opposing diagonals + one axis-aligned term, so no single direction dominates and the
    # result reads as mottling rather than as banding.
    waves = [(1, 2, 0.34), (2, -1, 0.30), (3, 1, 0.20), (0, 3, 0.16)]  # (fx, fy, weight)
    for y in range(side):
        for x in range(side):
            v = 0.0
            for fx, fy, w in waves:
                v += w * math.sin(2*math.pi*(fx*x/side + fy*y/side) + seed)
            t = (v + 1.0) / 2.0
            px[x, y] = (
                int(dark[0] + (light[0]-dark[0])*t),
                int(dark[1] + (light[1]-dark[1])*t),
                int(dark[2] + (light[2]-dark[2])*t),
                255)
    return im

def retone(im, target_hue, sat_mul, val_mul):
    im = im.convert("RGBA")
    px = im.load(); w, h = im.size
    hs, n = 0.0, 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 16: continue
            hh, ss, _ = colorsys.rgb_to_hsv(r/255, g/255, b/255)
            if ss < 0.05: continue
            hs += hh; n += 1
    shift = (target_hue/360.0) - (hs/n) if n else 0.0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0: continue
            hh, ss, vv = colorsys.rgb_to_hsv(r/255, g/255, b/255)
            hh = (hh + shift) % 1.0
            ss = max(0.0, min(1.0, ss*sat_mul))
            vv = max(0.0, min(1.0, vv*val_mul))
            nr, ng, nb = colorsys.hsv_to_rgb(hh, ss, vv)
            px[x, y] = (int(nr*255), int(ng*255), int(nb*255), a)
    return im

made = 0
for terrain, (source, hue, sat, val) in PLAN.items():
    if not os.path.exists(os.path.join(SRC, f"{source}_0.png")):
        print("  MISSING source:", source); continue
    d = os.path.join(DST, terrain)
    os.makedirs(d, exist_ok=True)
    # TWO variants per terrain, from different crops of the same source. MapView picks per tile by
    # seed; a single tile repeated across a biome shows an obvious grid of identical clumps.
    for idx in (0, 1):
        variant = os.path.join(SRC, f"{source}_{idx}.png")
        if not os.path.exists(variant):
            continue
        retone(Image.open(variant), hue, sat, val).save(os.path.join(d, f"top{idx}.png"))
        made += 1
    print(f"  {terrain:10} <- {source} (2 variants)")

for terrain, (dark, light) in WATER.items():
    d = os.path.join(DST, terrain)
    os.makedirs(d, exist_ok=True)
    for idx2 in (0, 1):
        water_tile(dark, light, seed=idx2 * 1.7).save(os.path.join(d, f"top{idx2}.png"))
        made += 1
    print(f"  {terrain:10} <- procedural seamless water (2 variants)")

print(f"\n{made} top-down tiles written")
