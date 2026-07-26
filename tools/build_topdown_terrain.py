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
    # ocean / high seas / arctic / tundra deliberately absent: they fall back to the
    # de-skewed FreeCol tile, which is fine for water and never appears for the polar types.
}

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
print(f"\n{made} top-down tiles written")
