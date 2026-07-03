# FreeCol art — provenance

All files under this directory are copied unmodified from **FreeCol** —
https://github.com/FreeCol/freecol — `data/default/resources/images/`
(© The FreeCol Team), licensed **GPL v2 or later** as part of the FreeCol
package (see `licenses/GPL-2.0.txt`). Adopted under ADR-013/ADR-014; recorded
in the Asset Register (ClickUp doc 05).

Contents: terrain base diamonds (`terrain/<type>/center*.png`, 128×64),
hills/mountains elevation overlays, generic forest overlays
(`forest/<type>/<type>.png`), fog art (`terrain/unexplored/`), unit sprites
(`units/` — see below), settlement sprites (`settlements/`), bonus-resource icons (`bonus/`),
**building images** (`buildings/*.png` — 42 base building sprites for the colony
screen) and **goods icons** (`goods/*.png` — 22 goods icons for the colony
screen's production/warehouse bars). All copied unmodified from the same FreeCol
`data/default/resources/images/{buildings,goods}/` (national/`size2`/`size9`
variants omitted).

**Unit sprites** (`units/`) — copied unmodified from FreeCol
`data/default/resources/images/units/`. The **base type** sprites sit flat at
`units/<type>.png` (the `civilian/`, `ship/` and `wagon/` source folders, e.g.
`freeColonist`, `veteranSoldier`, `hardyPioneer`, `caravel`, `galleon`,
`artillery`, `wagonTrain`, `treasureTrain`, and the **native** types `brave`,
`indianConvert`, `nativeColonist`). **Role-specific** sprites
keep FreeCol's per-role subfolders — `units/{role}/<type>.png` for `soldier`,
`dragoon`, `scout`, `pioneer`, `missionary`, `infantry`, `cavalry`, plus the
**native combat roles** `armedBrave`, `mountedBrave`, `nativeDragoon` (each holds a
`brave.png`, since the natives reuse the single `brave` base type across every role)
— so a colonist in the soldier role draws as a soldier, not a plain colonist, and a
brave draws armed/mounted instead of the red disc. The path priority lives engine-free
in `GameLogic` (`Specification/UnitSpriteCatalog.CandidatePaths` —
`units/{role}/{type}.png` → `units/{role}/{role}.png` → `units/{type}.png` → a red
disc when no art exists) and the Godot `UnitMarker` loads the first that exists. The
`*-attack*.sza` attack animations and `size2`/`size9` hi-res variants are omitted.

All native unit art (`brave`, `indianConvert`, `nativeColonist`, and the three
native role folders) is © The FreeCol Team, **GPL v2 or later** — the same license,
source and attribution as the European unit sprites above; nothing here is from the
original 1994/2008 game.

**UI skin** (`ui/bg_paper_brown.png`, 291×295) — the colony window's brown
parchment background, tiled as the panel fill so the map can't show through.
Copied unmodified from FreeCol `data/base/resources/images/ui/bg_paper_brown.png`
(GPL v2, ADR-014).

**UI menu bar** (`ui/bg_menubar.png`, 1166×64) — a dark carved-wood horizontal
strip, used (stretched to width) as the top HUD status-bar background so the
empire-stat text reads as light-on-dark rather than dark-on-parchment (86d3jnbek).
Copied unmodified from FreeCol `data/base/resources/images/ui/bg_menubar.png`
(GPL v2, ADR-014).

**UI border** (`ui/colony_border.png`, 194×194 nine-patch) — the colony
window's carved-wood frame. **Composited** (not unmodified) from FreeCol's
`data/base/resources/images/ui/border/carvedwood/carvedwoodenborder-{nw,n,ne,w,e,sw,s,se}.png`
edge/corner pieces, assembled into one 23px-margin nine-patch for Godot's
`NinePatchRect` — a mechanical layout of the GPL pieces, itself GPL v2 (ADR-014).
The lighter inner parchment is not yet adopted.

**Menu backdrop** (`ui/map.jpg`, 1600×1200) — the antique New-World map shown
full-screen behind the main menu (`scenes/MainMenu.tscn`). Copied unmodified from
FreeCol `data/base/resources/images/ui/map.jpg` (GPL v2, ADR-014). Note: we do
**not** adopt FreeCol's `freecol2.png` wordmark — the title is rendered as our own
"Crown & Colony" text in the shared parchment/wood theme.

**Europe harbour backdrop** (`ui/colonydocks.png`, 486×275) — the sky-and-sea
dockside scene drawn (stretched to fill) behind the Europe screen
(`presentation/EuropePanel.cs` → `ColonyArt.HarbourBackdrop`), with the content
cards on an opaque parchment backing on top so the text stays readable. Copied
**unmodified** from FreeCol `data/base/resources/images/ui/colonydocks.png`
(GPL v2, ADR-013/014). FreeCol also ships `colonydocks-sky.png` and `*.size2`
hi-res variants — not adopted (only the base scene is used).

**Founding-father portraits** (`fathers/<shortName>.jpg`, 200×237) — the
head-and-shoulders painting shown beside each father in the Continental Congress
dialog (`presentation/FoundingFatherPanel.cs`) and the Colopedia Fathers tab
(`presentation/ColopediaPanel.cs`), loaded by `ColonyArt.FatherPortrait`. All 25
classic fathers are covered. Copied **unmodified** from FreeCol
`data/default/resources/images/foundingFathers/*.jpg` (GPL v2, ADR-013/014); the
base `.jpg` (not the `.size6` hi-res variant) is adopted. Files are named for the
father's ruleset short name so the loader is a direct lookup; four fathers FreeCol
stored under a shorter file name were copied to their short name to match (the
mapping is FreeCol's own `image.flavor.model.foundingFather.*` keys in
`data/default/resources.properties`): `jeanDeBrebeuf.jpg` → `fatherJeanDeBrebeuf.jpg`,
`magellan.jpg` → `ferdinandMagellan.jpg`, `cortes.jpg` → `hernanCortes.jpg`,
`brewster.jpg` → `williamBrewster.jpg`. The `.size6` hi-res variants are not adopted.

Per ADR-013, any of these may be replaced individually later; keep this file
and the Asset Register current when that happens. FreeCol also ships 4×
high-resolution variants (`*.size9.png`) — adopt when zoom quality calls for it.
