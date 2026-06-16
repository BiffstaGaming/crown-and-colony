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

Per ADR-013, any of these may be replaced individually later; keep this file
and the Asset Register current when that happens. FreeCol also ships 4×
high-resolution variants (`*.size9.png`) — adopt when zoom quality calls for it.
