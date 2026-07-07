# Credits & Asset Attribution — Crown & Colony

**Crown & Colony** is licensed under the **GNU General Public License, version 2**
(GPL v2) — see [`LICENSE`](LICENSE). This file is the single authoritative record of
every third-party asset and data file distributed with the game: its source, copyright
holder, license, and the attribution required when the game is redistributed.

It aggregates the per-folder `PROVENANCE.md` files (which remain in place as the
detailed, file-by-file record):

- `game/assets/freecol/PROVENANCE.md` — fonts context, unit/native/terrain/UI sprites
- `game/assets/freecol/sound/PROVENANCE.md` — sound effects
- `game/assets/freecol/music/PROVENANCE.md` — background music + national anthems
- `game/assets/fonts/PROVENANCE.md` — the UI font
- `game/data/maps/PROVENANCE.md` — the fixed America and Australia map terrain grids
- `game/data/README.md` — the FreeCol ruleset XML and string data

Adopted under ADR-006 (audio), ADR-013 (placeholder-asset policy) and ADR-014
(art adoption); also recorded in the ClickUp **Asset Register** (doc 05).

## No original Sid Meier's Colonization assets are used

This project contains **no code, art, audio, or data from the original 1994 or 2008
*Sid Meier's Colonization* games**. Every asset below comes from **FreeCol** (the
GPL-v2 clean-room Java reimplementation, https://github.com/FreeCol/freecol), from
an independently, freely-licensed source (the Cardo font), or is **original work
authored for this project** (the app icon). A scan of the asset directories
(`game/assets/`, `game/data/maps/`, `game/data/rules/`) confirms only the
FreeCol-sourced, OFL-font, and project-original files catalogued here are present.

## License summary

| License | Covers | Compatible with GPL-v2 distribution |
|---|---|---|
| **GPL v2 or later** | FreeCol art (sprites, terrain, UI, settlements, bonus/goods/building icons), sound effects, national anthems, ruleset XML + string data, America + Australia map terrain | Yes — same license as the game |
| **CC BY 4.0** | FreeCol background-music tracks | Yes — one-way compatible into GPL v2; attribution required (below) |
| **SIL Open Font License 1.1 (OFL)** | The Cardo UI font | Yes — font is a data asset under OFL; game code stays GPL v2 |

Full license texts: [`game/assets/licenses/GPL-2.0.txt`](game/assets/licenses/GPL-2.0.txt),
[`game/assets/licenses/CC-BY-4.0.txt`](game/assets/licenses/CC-BY-4.0.txt),
and [`game/assets/fonts/Cardo-OFL.txt`](game/assets/fonts/Cardo-OFL.txt).

---

## Fonts

### Cardo (`game/assets/fonts/Cardo-Regular.ttf`)

The project's UI font (used by `ColonyTheme` for all menu/screen text) — a classical,
period-appropriate serif.

- **Source:** Google Fonts — https://github.com/google/fonts/tree/main/ofl/cardo
  (`ofl/cardo/Cardo-Regular.ttf`)
- **© / author:** David J. Perry / Scholars Fonts. Copyright (c) 2002–2011,
  David J. Perry (hospes02@scholarsfonts.net).
- **License:** SIL Open Font License 1.1 (`game/assets/fonts/Cardo-OFL.txt`).
- **Attribution:** "Cardo font © David J. Perry, used under the SIL Open Font License 1.1."

---

## Application icon — original project work

### Crown & Colony icon (`game/icon.svg`)

The app/window icon and boot-splash brand mark — a heraldic gold **crown** above a
colonial **sailing ship** on a navy field. Used as the Godot `config/icon`, the
Windows/macOS export-preset icon, and the editor icon.

- **Source:** **original work**, authored from scratch for this project as a hand-written
  SVG (no third-party art, clip-art, AI-generated asset, or original-game material). It
  replaced the earlier placeholder crown-only `icon.svg`.
- **© / author:** The Crown & Colony contributors.
- **License:** the project's own **GPL v2** (an original asset, so it carries the project
  licence; equivalently dedicatable to CC0 — no attribution is *required* for our own work).
- **Attribution:** none required (project-original). Recorded here for provenance.

> Note on platform icon formats: Godot generates Windows `.ico` and macOS `.icns`
> automatically from this SVG at export time (no `.ico`/`.icns` file is committed, and no
> extra tooling is needed) — the export presets reference `res://icon.svg` directly.

---

## Unit & world sprites

All image assets under `game/assets/freecol/` are copied unmodified from FreeCol's
`data/default/resources/images/` and `data/base/resources/images/` (one exception
noted below), © **The FreeCol Team**, **GPL v2 or later**.

- **Required attribution:** "Game art © The FreeCol Team, used under GPL v2 or later,
  from https://github.com/FreeCol/freecol."

Covered groups:

| Group | Path | Notes |
|---|---|---|
| Terrain base diamonds + elevation overlays | `freecol/terrain/<type>/` | 128×64 center tiles, hills/mountains overlays, `unexplored/` fog art |
| Forest overlays | `freecol/forest/<type>/` | generic per-type forest sprites |
| European unit sprites | `freecol/units/<type>.png`, `units/<role>/` | colonists, soldiers, dragoons, scouts, pioneers, missionaries, infantry, cavalry, ships, wagon/treasure trains |
| **Native unit sprites** | `freecol/units/{brave,indianConvert,nativeColonist}.png`, `units/{armedBrave,mountedBrave,nativeDragoon}/` | same FreeCol Team © / GPL-v2 grant as the European sprites |
| Settlement sprites | `freecol/settlements/` | colony + native settlement art |
| Bonus-resource icons | `freecol/bonus/` | 12 tile-resource icons |
| Goods icons | `freecol/goods/` | 22 colony-screen goods icons |
| Building images | `freecol/buildings/` | 42 base building sprites |
| UI skin — parchment fill | `freecol/ui/bg_paper_brown.png` | from FreeCol `data/base/.../ui/` |
| UI skin — carved-wood frame | `freecol/ui/colony_border.png` | **composited** (the one modified asset) from FreeCol's `ui/border/carvedwood/` edge/corner pieces into one nine-patch — a mechanical layout of GPL pieces, itself GPL v2 |
| Menu backdrop | `freecol/ui/map.jpg` | 1600×1200 antique-map backdrop, from FreeCol `data/base/.../ui/map.jpg` |

FreeCol's `freecol2.png` wordmark is **not** adopted — the title is our own
"Crown & Colony" text. Hi-res `*.size9.png` / `*.size2.png` variants and the
`*-attack*.sza` animations are omitted.

---

## Sound effects

A small set of SFX under `game/assets/freecol/sound/`, copied unmodified from FreeCol's
`data/default/resources/sound/`, © **The FreeCol Team**, **GPL v2 or later**
(no CC-BY-only clip is included).

| Our file | FreeCol source | `SoundEvent` |
|---|---|---|
| `colony.ogg` | `event/colony.ogg` | ColonyFounded |
| `building.ogg` | `event/building.ogg` | BuildingComplete |
| `attack.ogg` | `attack/artillery.ogg` (renamed; content unmodified) | Combat |
| `load.ogg` | `event/load.ogg` | CargoMoved |
| `sell.ogg` | `event/sell.ogg` | CargoSold |
| `sunk.ogg` | `event/sunk.ogg` | ShipSunk |
| `illegal.ogg` | `event/illegal.ogg` | IllegalMove |
| `alert.ogg` | `event/alert.ogg` | Alert |

- **External-author note:** `alert.ogg` is derived from
  https://www.freesound.org/people/acclivity/sounds/32304/ (by *acclivity* on Freesound),
  as carried by FreeCol under its GPL-v2 packaging.
- **Required attribution:** "Sound effects © The FreeCol Team, used under GPL v2 or later
  (https://github.com/FreeCol/freecol); `alert.ogg` derived from a Freesound clip by *acclivity*."

---

## Background music — CC BY 4.0

The looping background playlist under `game/assets/freecol/music/`, copied unmodified
from FreeCol's `data/default/resources/music/default/`, licensed **CC BY 4.0**
(FreeCol's own `music/default/README.md` states this directory is CC-BY 4.0).

| Our file | FreeCol source | Author |
|---|---|---|
| `el-dorado.ogg` | `default/el-dorado.ogg` | Alexander Zhelanov |
| `founders.ogg` | `default/founders.ogg` | Alexander Zhelanov |
| `settlers-routine.ogg` | `default/settlers-routine.ogg` | Alexander Zhelanov |
| `sunrise.ogg` | `default/sunrise.ogg` | Alexander Zhelanov |
| `tailwind.ogg` | `default/tailwind.ogg` | Alexander Zhelanov |
| `fearless-sailors.ogg` | `default/fearless-sailors.ogg` | Alexander Zhelanov |

- **© / author:** Alexander Zhelanov (all six shipped tracks). FreeCol also credits
  **Stian Grenborgen** for `musicbox.ogg` (CC-BY), which we do not ship.
- **License:** CC BY 4.0 (`game/assets/licenses/CC-BY-4.0.txt`).
- **Required attribution:** "Background music by Alexander Zhelanov, licensed under
  CC BY 4.0, via the FreeCol project."

---

## National anthems — GPL v2

The eight per-nation anthems under `game/assets/freecol/music/anthem/`, copied unmodified
from FreeCol's `data/default/resources/sound/anthem/`, © **The FreeCol Team**,
**GPL v2 or later** (no separate CC-BY README accompanies them in FreeCol).

| Our file | Nation |
|---|---|
| `dutch.ogg` | model.nation.dutch |
| `english.ogg` | model.nation.english |
| `french.ogg` | model.nation.french |
| `spanish.ogg` | model.nation.spanish |
| `danish.ogg` | model.nation.danish |
| `portuguese.ogg` | model.nation.portuguese |
| `russian.ogg` | model.nation.russian |
| `swedish.ogg` | model.nation.swedish |

- **Required attribution:** "National anthems © The FreeCol Team, used under GPL v2 or
  later, from https://github.com/FreeCol/freecol."

---

## Game data (FreeCol-derived)

Not "assets" in the art/audio sense, but third-party content distributed with the game,
all © **The FreeCol Team**, **GPL v2 or later**:

| File | FreeCol source | Notes |
|---|---|---|
| `game/data/rules/classic/specification.xml` | `data/rules/classic/specification.xml` | the classic ruleset, copied verbatim (© 2002–2022 The FreeCol Team) |
| `game/data/rules/classic/european-nation-names.properties` | extracted from `data/strings/FreeColMessages.properties` | per-nation colony name lists |
| `game/data/maps/america.txt` | terrain layer extracted from `data/maps/M_America_Mazim.fsm` (by *Mazim*) | terrain grid only; our generators add overlays |
| `game/data/maps/australia.txt` | terrain layer converted from the FreeCol community map pack by *Euzimar* (`Australia.fsg`, 2017–18) | 30×80 terrain grid only (the Australian Federation variant, P8); standard FreeCol terrain ids; generators add overlays |

`game/data/maps/example-overlays.txt` is **original** to this project (hand-written
test fixture), under the project's own GPL v2 — no third-party licensing.

---

## When assets change

Per ADR-013, any individual asset may be replaced later. When that happens, update the
matching per-folder `PROVENANCE.md`, **this file**, and the ClickUp Asset Register
(doc 05) in the same change — they must stay in sync.
