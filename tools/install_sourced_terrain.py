"""Install one of the four SOURCED terrain candidate sets (WS2.5f).

All art is Poly Haven (https://polyhaven.com), CC0 / public domain — unambiguously
GPL-v2-compatible and attribution-free. Nothing is recoloured: these are the source
textures, cut to the 64px game tile, so what is on screen is the art itself.

Four candidates were sourced for EVERY terrain type on the Australia map; this installs
the Nth candidate for each. Usage:  python tools/install_sourced_terrain.py 1|2|3|4

Two variants per terrain come from two different crops of the same seamless source, so a
biome does not show a grid of identical tiles. Tiles are a 512px crop scaled 8:1 rather than
a whole-image downscale — averaging a 1K photo down to 64px destroys the grain and reads as
mud (learned the hard way).

Water is the exception: Poly Haven's terrain library has no sea, so ocean/highSeas keep the
procedural tiles from build_topdown_terrain.py.
"""
import json, os, sys
from PIL import Image

SRC = "tools/cc0-terrain"   # vendored 64px cuts of the CC0 sources (commit-sized; runs offline)
DST = "game/assets/australia/terrain"
SIDE = 64

pick = int(sys.argv[1]) if len(sys.argv) > 1 else 1
assert 1 <= pick <= 4, "candidate must be 1-4"
CANDIDATES = json.load(open(os.path.join(SRC, "candidates.json")))

made = 0
for terrain, options in CANDIDATES.items():
    tex = options[pick - 1]
    d = os.path.join(DST, terrain)
    os.makedirs(d, exist_ok=True)
    for idx in (0, 1):
        src = os.path.join(SRC, f"{tex}_{idx}.png")
        if not os.path.exists(src):
            print(f"  MISSING {src}"); continue
        Image.open(src).save(os.path.join(d, f"top{idx}.png"))
        made += 1
    print(f"  {terrain:16} <- {tex}")
print(f"\n{made} tiles installed from candidate set {pick}")
