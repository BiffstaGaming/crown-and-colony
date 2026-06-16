# FreeCol art — provenance

All files under this directory are copied unmodified from **FreeCol** —
https://github.com/FreeCol/freecol — `data/default/resources/images/`
(© The FreeCol Team), licensed **GPL v2 or later** as part of the FreeCol
package (see `licenses/GPL-2.0.txt`). Adopted under ADR-013/ADR-014; recorded
in the Asset Register (ClickUp doc 05).

Contents: terrain base diamonds (`terrain/<type>/center*.png`, 128×64),
hills/mountains elevation overlays, generic forest overlays
(`forest/<type>/<type>.png`), fog art (`terrain/unexplored/`), unit sprites
(`units/`), settlement sprites (`settlements/`), bonus-resource icons (`bonus/`),
**building images** (`buildings/*.png` — 42 base building sprites for the colony
screen) and **goods icons** (`goods/*.png` — 22 goods icons for the colony
screen's production/warehouse bars). All copied unmodified from the same FreeCol
`data/default/resources/images/{buildings,goods}/` (national/`size2`/`size9`
variants omitted).

**UI skin** (`ui/bg_paper_brown.png`, 291×295) — the colony window's brown
parchment background, tiled as the panel fill so the map can't show through.
Copied unmodified from FreeCol `data/base/resources/images/ui/bg_paper_brown.png`
(GPL v2, ADR-014).

**UI border** (`ui/colony_border.png`, 194×194 nine-patch) — the colony
window's carved-wood frame. **Composited** (not unmodified) from FreeCol's
`data/base/resources/images/ui/border/carvedwood/carvedwoodenborder-{nw,n,ne,w,e,sw,s,se}.png`
edge/corner pieces, assembled into one 23px-margin nine-patch for Godot's
`NinePatchRect` — a mechanical layout of the GPL pieces, itself GPL v2 (ADR-014).
The lighter inner parchment is not yet adopted.

Per ADR-013, any of these may be replaced individually later; keep this file
and the Asset Register current when that happens. FreeCol also ships 4×
high-resolution variants (`*.size9.png`) — adopt when zoom quality calls for it.
