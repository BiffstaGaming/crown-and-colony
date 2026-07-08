# Fixed scenario maps — provenance

## FreeCol's half-row coordinates — the de-stagger conversion (2026-07-08)

FreeCol maps use a **staggered isometric lattice**: the stored `y` counts **half-rows**
(north/south moves are `y±2`; odd rows sit half a tile right and half a tile lower), so a
FreeCol map declared `W × H` **displays** as `W` tiles wide by `H/2` tiles tall. Our engine
uses a plain square grid — importing FreeCol `(x, y)` verbatim therefore rendered every
FreeCol map **twice as tall as its author intended** (Australia looked like a tall strip;
Chris 2026-07-08).

Both shipped grids are now **de-staggered** with the lossless relabel

```
square (col, row)  =  ( 2·x + (y mod 2),  y div 2 )      — and back: x = col div 2, y = 2·row + (col mod 2)
```

so a FreeCol `W × H` becomes our `2W × H/2` (same tile count; even columns carry the even
half-rows, odd columns the right-offset odd half-rows). The on-screen aspect then matches
FreeCol's own rendering exactly. Cost of the relabel: physically-adjacent east/west tiles
are two columns apart (one interleaved column between them), so coastlines gain a one-tile
zigzag texture — invisible at play zoom, and proportions are correct. Any future `.fsm`/`.fsg`
conversion **must apply the same relabel** (see `scripts/` or re-derive from this note).

## `america.txt`

The **terrain grid** of FreeCol's hand-made map of the Americas, extracted from
**`freecol/data/maps/M_America_Mazim.fsm`** (© The FreeCol Team, by *Mazim*),
licensed **GPL v2 or later** as part of the FreeCol package. Adopted under
ADR-013/ADR-014; recorded in the Asset Register (ClickUp doc 05).

A `.fsm` is a ZIP holding a full FreeCol `savegame.xml`. We extracted **only the
terrain layer** — each `<tile x= y= type="model.tile.…">` — into a compact text
grid (header `WIDTH HEIGHT`, then `HEIGHT` rows of `WIDTH` terrain short names,
row-major). The FreeCol source is **40 × 180 in half-row coordinates**; the shipped
grid is the de-staggered **80 × 90** (see the conversion note above — same 7 200 tiles,
FreeCol's true proportions: the Americas, taller than wide but no longer a 1:4.5 strip).

Rivers, bonus resources, native settlements and the player's start are **not**
taken from the `.fsm`; our own game-start generators lay those on top (so the
fixed map plays with our gameplay layers). Loaded by
`game/src/GameLogic/World/FixedMap.cs` (via the general `MapImporter`) from the
embedded resource `CrownAndColony.GameLogic.Maps.america.txt` (see `GameLogic.csproj`).
Because `america.txt` declares **only** the terrain grid (no overlay sections),
importing it yields the identical terrain-only map it always has — so the America
game stays byte-identical.

To re-extract (e.g. after a FreeCol update), parse the `<tile>` elements of the
`.fsm`'s `savegame.xml` into a row-major `WIDTH HEIGHT` short-name grid, **then apply
the de-stagger relabel above** (FreeCol half-rows → our square grid). (You may
*optionally* also emit the importer's overlay sections — see `example-overlays.txt`
below and `MapImporter.cs` — to carry the `.fsm`'s bonuses/rumours/settlements too;
we deliberately don't today, leaving those to the generators.)

## `australia.txt`

The **terrain grid** of a community-made FreeCol map of Australia, converted from the
**FreeCol community map pack by *Euzimar*** (the `Australia.fsg` / `Australia only.fsg`
files, dated 2017–2018), FreeCol saved-game format, licensed **GPL v2 or later** as
FreeCol-package content. Adopted for the **Australian Federation** variant (P8);
recorded in the Asset Register (ClickUp doc 05).

A `.fsg` is a ZIP holding a full FreeCol `savegame.xml`. We extracted **only the
terrain layer** — each `<tile x= y= type="model.tile.…">` — into the same compact grid
as `america.txt` (header `WIDTH HEIGHT`, then `HEIGHT` rows of `WIDTH` terrain short
names, row-major). The FreeCol source is **30 × 80 in half-row coordinates**; the shipped
grid is the de-staggered **60 × 40** (see the conversion note above — same 2 400 tiles,
and the continent finally reads **wider than tall**, as Australia is: arid interior,
forested/temperate coasts, Cape York and the Gulf of Carpentaria in the north, Tasmania
as a southern island). Every terrain id is a standard FreeCol type our
`classic`/`australia` ruleset already defines, so it resolves 1:1 with no remapping.

The two `.fsg` files carry **identical terrain** (they differ only in pre-placed
FreeCol players/units/settlements, which we discard). Rivers, resources, native
settlements and the player's start are laid on by our generators — not taken from the
`.fsg`, exactly as for `america.txt`. Loaded by `FixedMap.ImportAustralia` from the
embedded resource `CrownAndColony.GameLogic.Maps.australia.txt`.

**Distribution note:** this is community FreeCol content (GPL v2 map *data* — standard
terrain only, no custom art); confirm the specific pack's attribution/terms before any
public release.

## `example-overlays.txt`

An **original, hand-written** tiny example map (not derived from any FreeCol or
1994-game data — so no third-party licensing beyond this project's GPL v2). It
exists solely to **demonstrate and test** the `MapImporter` format: a 6×5 terrain
grid plus one of every optional overlay section (`[resources]`, `[improvements]`,
`[rumours]`, `[settlements]`). It is **not** a playable scenario and is not offered
as a `MapSource`; the importer reads it via the internal `FixedMap.ImportExampleOverlays`
from the embedded resource `CrownAndColony.GameLogic.Maps.example-overlays.txt`.
See `game/src/GameLogic/World/MapImporter.cs` for the full format reference.
