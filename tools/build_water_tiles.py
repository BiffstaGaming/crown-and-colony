"""Generate ONLY the top-down water tiles (ocean / high seas).

Split out of build_topdown_terrain.py because that script also writes every ground and
canopy tile — running it after installing a sourced terrain set silently overwrites the
sourced art. Water is the one terrain no CC0 library covers (Poly Haven's terrain set has
no sea; ambientCG is a PBR library where water is a shader, not an image), so it stays
procedural: tileable sinusoidal noise, seamless by construction, deliberately low contrast.
Our own work (GPL v2).
"""
import math, os
from PIL import Image

DST = "game/assets/australia/terrain"
WATER = {
    "ocean":    ((30, 68, 100), (35, 76, 110)),
    "highSeas": ((22, 52, 80), (26, 59, 89)),
}

def water_tile(dark, light, side=64, seed=0.0):
    im = Image.new("RGBA", (side, side)); px = im.load()
    waves = [(1, 2, 0.34), (2, -1, 0.30), (3, 1, 0.20), (0, 3, 0.16)]
    for y in range(side):
        for x in range(side):
            v = sum(w * math.sin(2*math.pi*(fx*x/side + fy*y/side) + seed) for fx, fy, w in waves)
            t = (v + 1.0) / 2.0
            px[x, y] = (int(dark[0] + (light[0]-dark[0])*t),
                        int(dark[1] + (light[1]-dark[1])*t),
                        int(dark[2] + (light[2]-dark[2])*t), 255)
    return im

made = 0
for terrain, (dark, light) in WATER.items():
    d = os.path.join(DST, terrain); os.makedirs(d, exist_ok=True)
    for idx in (0, 1):
        water_tile(dark, light, seed=idx * 1.7).save(os.path.join(d, f"top{idx}.png"))
        made += 1
print(f"{made} water tiles written")
