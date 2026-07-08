"""Generate the [regions] overlay for the Australia scenario map (task 86d3mm1xr).

The Australian Federation variant needs the six historical colonies as named map
regions (New South Wales / Victoria / Queensland / South Australia / Tasmania /
Western Australia) plus the surrounding sea, so a game on `australia.txt` can name
the land the player settles. Hand-authoring ~2400 tile assignments is infeasible and
unmaintainable, so this DETERMINISTIC generator reads the shipped terrain grid and
partitions every tile into a region:

  * Land tiles -> the nearest of the six colony SEEDS (squared-Euclidean nearest-seed
    over land only), so each colony owns a contiguous-ish coastal-to-interior wedge.
    Tasmania is the southern island; its seed sits on that island, and because the
    island is separated by sea no mainland tile is ever closer to it than a mainland
    seed, so the partition respects the coastline. The arid interior falls to whichever
    coastal colony is nearest — exactly the "quadrant" carve-up the variant design asks
    for (WA = western third, SA = south-central, NSW = mid/SE seaboard, Vic = far SE,
    Qld = NE, Tas = the y>=32 island).
  * Water tiles (`ocean`/`highSeas`/`lake`/`greatRiver`) -> a single `Ocean` region
    (`model.region.australSea`) — the map is one connected sea, and colony regions must
    be RegionType.Land, so all water is one keyed ocean region (score 0), mirroring how
    the RegionGenerator keeps a keyed parent ocean.

The region ids are dense from 0 in declaration order (importer HARD CONSTRAINT):
    0 = Ocean (australSea), 1..6 = the six colonies in fixed order.
Every in-bounds tile is assigned exactly one region (importer HARD CONSTRAINT).

The result is APPENDED as a `[regions]` section to `australia.txt` (after a `[starts]`
section fixing the First-Fleet human landfall at the NSW coastal tile). Re-running the
script regenerates both overlays idempotently (it strips any existing `[starts]`/
`[regions]` first). See `game/data/maps/PROVENANCE.md` (regions layer) and
`game/src/GameLogic/World/MapImporter.cs` for the format.

Usage:
    python scripts/generate-australia-regions.py game/data/maps/australia.txt
"""
import sys

WATER = {"ocean", "highSeas", "lake", "greatRiver"}

# The six colony region seeds: a valid, settleable, COASTAL land tile in each colony's
# quadrant (verified against australia.txt; nudged to the nearest valid coastal tile
# where the design's candidate was sea/inland). Order fixes the dense region ids 1..6.
# (id, freecol-i18n-key, seed-x, seed-y). The key's camelCase suffix humanises to the
# colony's display name (Naming.Humanize: newSouthWales -> "New South Wales").
COLONIES = [
    ("model.region.newSouthWales",   50, 24),  # SE seaboard prairie — the First-Fleet landfall
    ("model.region.victoria",        46, 30),  # far SE grassland
    ("model.region.queensland",      49, 10),  # NE savannah
    ("model.region.southAustralia",  36, 28),  # south-central plains
    ("model.region.tasmania",        41, 33),  # the southern island (y>=32)
    ("model.region.westernAustralia", 12, 20), # western third prairie
]

# The single keyed ocean region covering every water tile (score 0). Region id 0.
OCEAN_KEY = "model.region.australSea"

# The First-Fleet human landfall — Sydney Cove / New South Wales (the NSW seed tile).
FIRST_FLEET = (50, 24)


def load(path):
    with open(path) as f:
        raw = f.readlines()
    # Keep only the terrain grid: header + HEIGHT rows. Any trailing overlay sections
    # (a previous [starts]/[regions]) are dropped so regeneration is idempotent.
    content = [l.rstrip("\n") for l in raw]
    # Find first non-blank, non-comment line = header.
    idx = 0
    while idx < len(content) and (not content[idx].strip() or content[idx].lstrip().startswith("#")):
        idx += 1
    header = content[idx]
    w, h = map(int, header.split())
    rows = []
    y = idx + 1
    while len(rows) < h:
        line = content[y]
        if line.strip() and not line.lstrip().startswith("#"):
            rows.append(line.split())
        y += 1
    assert all(len(r) == w for r in rows), "grid shape mismatch"
    return w, h, rows


def assign(w, h, rows):
    """Return a dense row-major list of region ids (0 = ocean, 1..6 = colonies)."""
    ids = [0] * (w * h)
    for y in range(h):
        for x in range(w):
            t = rows[y][x]
            if t in WATER:
                ids[y * w + x] = 0  # ocean
                continue
            # Nearest colony seed by squared Euclidean distance (deterministic; ties
            # broken by the lower colony index — the COLONIES declaration order).
            best_i, best_d = 0, None
            for i, (_key, sx, sy) in enumerate(COLONIES):
                d = (x - sx) ** 2 + (y - sy) ** 2
                if best_d is None or d < best_d:
                    best_d, best_i = d, i
            ids[y * w + x] = best_i + 1  # colonies are region ids 1..6
    return ids


def emit(path, w, h, rows, ids):
    fx, fy = FIRST_FLEET
    with open(path, "w", newline="\n") as f:
        f.write(f"{w} {h}\n")
        for row in rows:
            f.write(" ".join(row) + "\n")
        f.write("\n")
        f.write("# --- overlays generated by scripts/generate-australia-regions.py (86d3mm1xr) ---\n")
        f.write("\n[starts]\n")
        f.write("# The First-Fleet landfall: Sydney Cove, New South Wales (the NSW colony seed tile).\n")
        f.write(f"human {fx} {fy}\n")
        f.write("\n[regions]\n")
        f.write("# The six historical colonies as named Land regions + one keyed ocean region.\n")
        f.write("# Region ids dense from 0 in declaration order (importer constraint):\n")
        f.write("#   0 = the surrounding sea; 1..6 = NSW / Vic / Qld / SA / Tas / WA.\n")
        # Region table. Ocean carries a key so it is a fixed (non-discoverable) parent
        # sea; colonies are keyed Land regions (each a distinct colony name).
        f.write(f"region 0 Ocean 0 {OCEAN_KEY}\n")
        for i, (key, _sx, _sy) in enumerate(COLONIES):
            f.write(f"region {i + 1} Land 0 {key}\n")
        f.write("\n")
        # Per-tile assignments, row-major.
        for y in range(h):
            for x in range(w):
                f.write(f"{x} {y} {ids[y * w + x]}\n")


def summarise(w, h, ids):
    counts = {}
    for v in ids:
        counts[v] = counts.get(v, 0) + 1
    names = ["ocean"] + [c[0].split(".")[-1] for c in COLONIES]
    for i, n in enumerate(names):
        print(f"  region {i} {n}: {counts.get(i, 0)} tiles")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit("usage: generate-australia-regions.py <australia.txt>")
    path = sys.argv[1]
    w, h, rows = load(path)
    ids = assign(w, h, rows)
    emit(path, w, h, rows, ids)
    print(f"wrote {path}: {w}x{h} terrain + [starts] + [regions] (7 regions, {w*h} tiles)")
    summarise(w, h, ids)
