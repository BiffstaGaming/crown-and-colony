# FreeCol music — provenance

Background-music tracks and per-nation national anthems, copied **unmodified**
from **FreeCol** — https://github.com/FreeCol/freecol — into this directory.
Used by the presentation-layer `MusicService` (autoload `/root/Music`), which
resolves a logical `MusicContext` (and a nation id, for anthems) to one of these
tracks via the engine-free `MusicTrackCatalog` and plays it, looping, on the
`Music` audio bus. Adopted under ADR-006 (presentation); recorded in the Asset
Register (ClickUp doc 05).

## License — TWO licenses here, by sub-folder

FreeCol's `README.md` states: *"Most of the content, like artwork, music and
sound effects, are also licensed under GPL v2. Some of the content is licensed
using CC BY 4.0."* For music the split is explicit in FreeCol's own READMEs:

- **Background tracks** (this folder, `*.ogg`) — **CC BY 4.0**. FreeCol's
  `data/default/resources/music/default/README.md` says: *"All files in this
  directory is licensed using CC-BY 4.0"*, by **Alexander Zhelanov** (most) and
  **Stian Grenborgen** (`musicbox`). See `../../licenses/CC-BY-4.0.txt`.
- **Anthems** (`anthem/*.ogg`) — part of FreeCol's **GPL v2** package
  (© The FreeCol Team); no separate CC-BY README accompanies them. See
  `../../licenses/GPL-2.0.txt`.

Both licenses are compatible with this project's GPL-v2 distribution (CC BY 4.0
is one-way compatible into GPL v2). Attribution for the CC-BY tracks is recorded
below and in the Asset Register.

## External-author attribution (CC BY 4.0)

Per FreeCol's `data/default/resources/music/default/README.md`:

- **Alexander Zhelanov** — `el-dorado.ogg`, `fearless-sailors.ogg`,
  `founders.ogg`, `settlers-routine.ogg`, `sunrise.ogg`, `tailwind.ogg`
  (the background tracks we ship). His other CC-BY tracks in FreeCol
  (`free-colonist`, `lost-city`, `rainy-day`, `royal-troops`, `wagon-wheels`)
  are not shipped here.
- **Stian Grenborgen** — `musicbox.ogg` (CC-BY; not shipped here).

The anthems carry no separate external author in FreeCol's packaging and are
covered by the FreeCol GPL-v2 grant.

## Background-playlist files (CC BY 4.0 — from FreeCol `data/default/resources/music/default/`)

These form the looping background playlist (the shared `MusicContext.Menu` /
`MusicContext.InGamePeace` bed; the interim `MusicContext.InGameWar` playlist is a
subset of the same files — no separate war asset is shipped). A faithful
subset of FreeCol's shuffled "default" playlist (`sound.music.playlist.default`).

| Our file (`game/assets/freecol/music/`) | FreeCol source path           | Author             |
|-----------------------------------------|-------------------------------|--------------------|
| `el-dorado.ogg`                         | `default/el-dorado.ogg`       | Alexander Zhelanov |
| `founders.ogg`                          | `default/founders.ogg`        | Alexander Zhelanov |
| `settlers-routine.ogg`                  | `default/settlers-routine.ogg`| Alexander Zhelanov |
| `sunrise.ogg`                           | `default/sunrise.ogg`         | Alexander Zhelanov |
| `tailwind.ogg`                          | `default/tailwind.ogg`        | Alexander Zhelanov |
| `fearless-sailors.ogg`                  | `default/fearless-sailors.ogg`| Alexander Zhelanov |

## Anthem files (GPL v2 — from FreeCol `data/default/resources/sound/anthem/`)

The 8 European powers FreeCol ships an anthem for. `MusicTrackCatalog.TryGetAnthem`
maps each `model.nation.<x>` id to `anthem/<x>.ogg`, mirroring FreeCol's
`sound.anthem.model.nation.<x>` resource keys (resource type `"music"`).

| Our file (`game/assets/freecol/music/anthem/`) | FreeCol source path       | Nation id                  |
|------------------------------------------------|---------------------------|----------------------------|
| `dutch.ogg`                                    | `anthem/dutch.ogg`        | `model.nation.dutch`       |
| `english.ogg`                                  | `anthem/english.ogg`      | `model.nation.english`     |
| `french.ogg`                                   | `anthem/french.ogg`       | `model.nation.french`      |
| `spanish.ogg`                                  | `anthem/spanish.ogg`      | `model.nation.spanish`     |
| `danish.ogg`                                   | `anthem/danish.ogg`       | `model.nation.danish`      |
| `portuguese.ogg`                               | `anthem/portuguese.ogg`   | `model.nation.portuguese`  |
| `russian.ogg`                                  | `anthem/russian.ogg`      | `model.nation.russian`     |
| `swedish.ogg`                                  | `anthem/swedish.ogg`      | `model.nation.swedish`     |

The `.ogg.import` files alongside each clip are Godot-generated import metadata
(committed so the project imports identically on CI).

Per ADR-013, any of these may be replaced individually later; keep this file and
the Asset Register current when that happens.
