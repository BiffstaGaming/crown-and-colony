# FreeCol sound effects — provenance

A small set of game sound effects (SFX), copied **unmodified** from **FreeCol** —
https://github.com/FreeCol/freecol — into this directory. Used by the
presentation-layer `SoundService` (autoload `/root/Sound`), which resolves a
logical `SoundEvent` to one of these clips via the engine-free
`SoundEventCatalog` and plays it on the `SFX` audio bus. Adopted under ADR-006
(presentation); recorded in the Asset Register (ClickUp doc 05).

## License

FreeCol's source is GPL v2; per FreeCol's `README.md`, **"Most of the content,
like artwork, music and sound effects, are also licensed under GPL v2. Some of
the content is licensed using CC BY 4.0."** These event/attack SFX are part of
the GPL-v2 FreeCol package (© The FreeCol Team) — compatible with our GPL-v2
distribution (see `../../licenses/GPL-2.0.txt`). No CC-BY-only clip is included
here.

## External-author attribution

FreeCol records one external source for the sound-effect set, in
`freecol/data/default/resources/sound/event/README`:

- **`alert.ogg`** — derived from
  https://www.freesound.org/people/acclivity/sounds/32304/ (by *acclivity* on
  Freesound), as carried by FreeCol under its GPL-v2 packaging.

All other clips below carry no separate external author in FreeCol's README and
are covered by the FreeCol GPL-v2 grant.

## Files (all from FreeCol `data/default/resources/sound/`)

| Our file (`game/assets/freecol/sound/`) | FreeCol source path                  | `SoundEvent`                  |
|-----------------------------------------|--------------------------------------|-------------------------------|
| `colony.ogg`                            | `event/colony.ogg`                   | `ColonyFounded`               |
| `building.ogg`                          | `event/building.ogg`                 | `BuildingComplete`            |
| `attack.ogg`                            | `attack/artillery.ogg`               | `Combat`                      |
| `load.ogg`                              | `event/load.ogg`                     | `CargoMoved`                  |
| `sell.ogg`                              | `event/sell.ogg`                     | `CargoSold`                   |
| `sunk.ogg`                              | `event/sunk.ogg`                     | `ShipSunk`                    |
| `illegal.ogg`                           | `event/illegal.ogg`                  | `IllegalMove` (also UI deny)  |
| `alert.ogg`                             | `event/alert.ogg`                    | `Alert`                       |

`attack.ogg` is FreeCol's `attack/artillery.ogg` renamed to a neutral name (the
file content is unmodified). The `.ogg.import` files alongside each clip are
Godot-generated import metadata (committed so the project imports identically on
CI).

Per ADR-013, any of these may be replaced individually later; keep this file and
the Asset Register current when that happens.
