"""De-stagger a FreeCol-derived map grid.

FreeCol maps use a staggered isometric lattice: y counts HALF-rows (N/S moves are
y+-2; odd rows sit half a tile right and half a tile lower). Our engine's grid is a
plain square lattice, so importing FreeCol (x, y) verbatim doubles the map's height.

The fix is a lossless relabel onto a 2W x H/2 square grid:
    new (col, row)  <-  old (x = col // 2, y = 2*row + (col % 2))
Even columns carry the even half-rows, odd columns the odd (right-offset) half-rows —
every source tile keeps exactly one cell and the on-screen aspect matches FreeCol's
(W*128 wide x (H/2)*64 tall with 2:1 diamonds == 2W x H/2 square tiles).
"""
import sys

def load(path):
    with open(path) as f:
        lines = [l.strip() for l in f if l.strip()]
    w, h = map(int, lines[0].split())
    rows = [l.split() for l in lines[1:1 + h]]
    assert len(rows) == h and all(len(r) == w for r in rows), "grid shape mismatch"
    return w, h, rows

def interleave(w, h, rows):
    assert h % 2 == 0, "height must be even (pairs of half-rows)"
    W, H = 2 * w, h // 2
    return W, H, [[rows[2 * r + (c % 2)][c // 2] for c in range(W)] for r in range(H)]

def ascii_art(w, h, rows):
    def ch(t):
        if t == "highSeas": return " "
        if t in ("ocean", "lake"): return "~"
        return "#"
    return "\n".join("".join(ch(t) for t in row) for row in rows)

def save(path, w, h, rows):
    with open(path, "w", newline="\n") as f:
        f.write(f"{w} {h}\n")
        for row in rows:
            f.write(" ".join(row) + "\n")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        sys.exit("usage: destagger-freecol-map.py <staggered-in.txt> <square-out.txt> [preview]")
    src, dst, preview = sys.argv[1], sys.argv[2], len(sys.argv) > 3
    w, h, rows = load(src)
    W, H, out = interleave(w, h, rows)
    if preview:
        print(f"BEFORE {w}x{h}:"); print(ascii_art(w, h, rows))
        print(f"\nAFTER {W}x{H}:"); print(ascii_art(W, H, out))
    save(dst, W, H, out)
    print(f"wrote {dst}: {W}x{H} ({W*H} tiles, from {w}x{h})")
