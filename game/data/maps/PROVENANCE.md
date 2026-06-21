# Fixed scenario maps — provenance

## `america.txt`

The **terrain grid** of FreeCol's hand-made map of the Americas, extracted from
**`freecol/data/maps/M_America_Mazim.fsm`** (© The FreeCol Team, by *Mazim*),
licensed **GPL v2 or later** as part of the FreeCol package. Adopted under
ADR-013/ADR-014; recorded in the Asset Register (ClickUp doc 05).

A `.fsm` is a ZIP holding a full FreeCol `savegame.xml`. We extracted **only the
terrain layer** — each `<tile x= y= type="model.tile.…">` — into a compact text
grid (header `WIDTH HEIGHT`, then `HEIGHT` rows of `WIDTH` terrain short names,
row-major). The map is **40 × 180** (a tall N–S strip of the Americas).

Rivers, bonus resources, native settlements and the player's start are **not**
taken from the `.fsm`; our own game-start generators lay those on top (so the
fixed map plays with our gameplay layers). Loaded by
`game/src/GameLogic/World/FixedMap.cs` (via the general `MapImporter`) from the
embedded resource `CrownAndColony.GameLogic.Maps.america.txt` (see `GameLogic.csproj`).
Because `america.txt` declares **only** the terrain grid (no overlay sections),
importing it yields the identical terrain-only map it always has — so the America
game stays byte-identical.

To re-extract (e.g. after a FreeCol update), parse the `<tile>` elements of the
`.fsm`'s `savegame.xml` into a row-major `WIDTH HEIGHT` short-name grid. (You may
*optionally* also emit the importer's overlay sections — see `example-overlays.txt`
below and `MapImporter.cs` — to carry the `.fsm`'s bonuses/rumours/settlements too;
we deliberately don't today, leaving those to the generators.)

## `example-overlays.txt`

An **original, hand-written** tiny example map (not derived from any FreeCol or
1994-game data — so no third-party licensing beyond this project's GPL v2). It
exists solely to **demonstrate and test** the `MapImporter` format: a 6×5 terrain
grid plus one of every optional overlay section (`[resources]`, `[improvements]`,
`[rumours]`, `[settlements]`). It is **not** a playable scenario and is not offered
as a `MapSource`; the importer reads it via the internal `FixedMap.ImportExampleOverlays`
from the embedded resource `CrownAndColony.GameLogic.Maps.example-overlays.txt`.
See `game/src/GameLogic/World/MapImporter.cs` for the full format reference.
