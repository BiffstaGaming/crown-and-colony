# System: Australian visual identity

| | |
|---|---|
| **Status** | In development — the theme skin (WS2.1) and the **terrain/forest art pass (WS2.5)** are implemented. Unit and building art (WS2.4), the title and menu art (WS2.2), flags and emblems (WS2.3) are not built. |
| **Last verified** | 2026-07-26 (WS2.1 skin + WS2.5 terrain; before/after captured from the live map — `docs/guide/img/australia-terrain-{before,after}.png`) |
| **Code** | `game/presentation/ColonyTheme.cs` (skin + palette + colony colours), `game/presentation/GameController.cs` (skin selection + terrain reload), `game/presentation/FederationPanel.cs` (colony swatches), `game/presentation/MapView.cs` (terrain through the art seam), `tools/retone_australian_terrain.py` (the terrain transform) |
| **Tests** | `game/presentation/tests/ColonyArtTests.cs` (L3) |
| **Design docs** | `docs/australian_federation_mode_md/ROAD_TO_100.md` §WS2, `19_UI_Text_Renaming_and_Lore.md` |
| **Related systems** | [federation-victory](federation-victory.md) (the panel the colony colours serve) |

## 1. How it works (plain English)

The game inherited FreeCol's look: cream parchment over dark European oak, with gold trim. It's a good look — but it's a
*European colonial* look, and the Australian campaign was wearing it with Australian words pasted on top. Nothing on
screen said "this is Australia" except the captions.

This doesn't throw that away and start again. It **re-tones the same design** for the Australian campaign — same
parchment-over-timber structure, same layout, different palette:

- **The paper is sun-bleached** rather than European cream. The light here is harsher.
- **The timbers turn red.** European dark oak becomes the red-brown of jarrah and red gum — the timbers the colonies
  actually built with.
- **The metal trim turns blue.** Gold becomes **Federation blue**, the field of the Southern Cross. This is the single
  biggest tell: focus rings, title halos and pressed-button text all shift, so an Australian screen reads as Australian
  at a glance without a word being read.

**The six colonies each get their own colour.** The Federation panel used to show six identical rows differing only by
their label. Each now carries a colour swatch — Federation blue for New South Wales, bush green for Victoria,
maroon-brown for Queensland, ochre red for South Australia, cool slate for Tasmania, goldfields yellow for Western
Australia. They're deliberately muted and period-looking rather than bright flat-UI colours, and no two sit next to each
other on the colour wheel, so they stay tellable apart.

**Classic is untouched.** A classic game gets the original palette exactly as before, down to the colour values.

### The terrain (WS2.5)

The map was the loudest thing saying "this is America": vivid emerald grassland, pale beige desert, pine-green forest.
It now reads Australian — **burnt red-ochre desert** (the red centre, the single most recognisable Australian
landscape), dry gold grassland and pasture, and **dusty grey-green eucalypt** instead of pine green. The ocean is
untouched, because the sea is the sea.

#### Top-down gets its own tiles (WS2.5b)

Top-down is expected to become the game's **main view**, and it had no art of its own — it warped each isometric diamond
into a square. Art drawn as diamonds cannot tile as squares, so that showed **visible seams and repeating diagonal
artefacts**, and only part of each tile was ever sampled. Top-down now has **native 64×64 square tiles**, and here
**sourcing did work**: a CC0 seamless ground scan (ambientCG, public domain) as the base — which is what actually fixes
the seams — with the same Australian re-tone on top so both projections read as one country. Two variants per terrain,
because one tile repeated across a biome shows an obvious grid. Terrains without a square tile (ocean, the polar types)
fall back to the de-skew, and classic ships none at all, so its top-down is unchanged.

Why sourcing succeeded here and failed for isometric: in top-down the **entire ground plane** is replaced by one coherent
set, so internal consistency is what matters. In isometric a sourced tile would have sat as a lone photographic square
among FreeCol's painterly diamonds.

**For the isometric view, sourced art was tried first and rejected on the evidence.** The best licence-clean candidate found was Screaming Brain
Studios' CC0 isometric pack — correct 128×64 geometry, genuinely usable licence. It was still wrong: photographic,
high-contrast rock textures beside FreeCol's flat painterly tiles, in generic grey-brown rather than Australian ochre.
Terrain is the whole screen, so a style clash there is maximally visible. (A second candidate, an OpenGameArt seasonal
tileset, was **CC BY-SA 4.0** — one-way compatible with GPL **v3**, not the v2 this project is under, so it was
unusable regardless of how it looked.) The art therefore derives from FreeCol's own tiles, which is licence-clean and
style-consistent by construction.

## 2. Detailed rules

- `ColonyTheme.ActiveSkin` selects the palette (`Classic` — the default — or `Australia`). Assigning a new skin drops
  the cached themes so the next `Get()` / `GetInGame()` rebuilds; panels pick it up the next time they open.
- The skin is set in two places in `GameController`: reset to `Classic` at scene setup (so the menus and their goldens
  are always classic regardless of a prior in-process game) and set from `_variant.ArtRoot` in `StartGame`, alongside
  `ColonyArt.VariantArtRoot`.
- `ColonyTheme.ColonyRegionColor(regionKey)` returns the per-colony colour; an unknown region falls back to the neutral
  timber tone, so a variant that adds regions cannot make it throw.

### The palette

| Role | Classic | Australia |
|---|---|---|
| Parchment | `#E8D9B0` | `#EDE0BC` (sun-bleached) |
| Parchment (dark / edge) | `#D9C290` / `#C2A86A` | `#DCC79A` / `#C6A874` |
| Timber (dark / mid / light) | `#4A2E1A` / `#7A4F30` / `#9A6A42` | `#4A2118` / `#7E3F2C` / `#9E5A3E` (jarrah / red gum) |
| Ink (body / title) | `#2B1D10` / `#3A2410` | `#2A1A12` / `#382012` |
| Accent | `#C9A24B` gold | `#2E5C8A` Federation blue |
| Text on timber | `#F2E2C2` | `#F4E7CC` |

| Colony | Colour | Reading |
|---|---|---|
| New South Wales | `#2E5C8A` | Federation blue |
| Victoria | `#3F6B4A` | bush green |
| Queensland | `#8A5A2E` | maroon-brown |
| South Australia | `#A03A2E` | ochre red |
| Tasmania | `#4A6E74` | cool island slate |
| Western Australia | `#B8862E` | goldfields |

## 3. Technical design

The skin is a **static palette switch inside one theme builder**, not a second theme class. Every palette entry became a
`private static Color X => _skin == Skin.Australia ? … : …` property, so the ~40 existing `ColonyTheme.Get()` /
`GetInGame()` call sites across the presentation layer needed **no change at all** — they keep asking for "the theme"
and get the right one.

**Why the goldens are safe.** The L4 visual goldens are captured on menus and classic-game panels, and the skin is reset
to `Classic` at scene setup. The Australian values are only ever reachable after `StartGame` with the Australia variant,
which no existing golden exercises. The L3 test additionally asserts that switching to Australia and back restores the
classic accent colour exactly.

**How the terrain art is produced.** `tools/retone_australian_terrain.py` reads each FreeCol tile and writes an
Australian copy under `game/assets/australia/`, applying a **hue rotation + saturation/value scale in HSV**: every pixel
keeps its own relative hue variation and all of its luminance, so the painted texture, shading and tile edges survive —
only the centre of the colour range moves. The script is checked in deliberately: **it is the provenance**, and
re-running it regenerates the art byte-for-byte from the FreeCol originals, which are never modified.

**The map had to be wired to the seam first.** `MapView` loaded terrain from a hard-coded `res://assets/freecol/` path
in `_Ready()`, so the largest art surface in the game bypassed the WS1.3 variant seam entirely — Australian terrain
could be supplied in full and the map would still draw FreeCol's. It now loads through `ColonyArt.LoadTexture`, and
because `_Ready()` runs *before* the variant is known, `GameController` calls `MapView.ReloadTerrainArt()` once it has
set the art root.

**Deliberately not done here:** unit and building art (WS2.4) — those need sourced or drawn assets, not a palette; the
title/logo and menu splash (WS2.2); flags and colony emblems (WS2.3). This is the layer they will sit on.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L3 Interaction | Yes | `ColonyArtTests` — switching to the Australian skin genuinely re-tones the palette (the accent moves from warm-dominant gold to blue-dominant Federation blue) while both skins keep the same structural styleboxes; switching back restores the classic accent exactly; each of the six colonies has a distinct colour and an unknown region falls back rather than throwing. | ✅ green |
| L4 Visual | Existing goldens must be unaffected | The menu and classic-panel goldens are captured with the skin reset to `Classic`. | ✅ unchanged |
| L3 (terrain seam) | Yes | `ColonyArtTests` — the Australian terrain resolves *through* the seam (guarding the `MapView` hard-coded-path bug), classic still resolves the FreeCol originals, and a terrain Australia does not re-tone (ocean) still falls back cleanly. | ✅ green |
| Manual | Before/after | `docs/guide/img/australia-terrain-before.png` / `-after.png` — same seed, same computed framing, captured from the live map. | ✅ captured |

## 5. Open issues / TODO

- [ ] WS2.2 game title / logo / menu splash art.
- [ ] WS2.3 nation flag + six colony emblems (the swatches here are the placeholder for those).
- [ ] WS2.4 unit and building art — the largest remaining visual gap now that terrain and portraits are done. Note the
      honest ceiling of the re-tone approach: it changes colour, not silhouette.
- [ ] WS6.7 Australian visual goldens, so this skin is regression-guarded at L4 and not only at L3.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-26 | **WS2.5d — terrain style candidates, and two real bugs fixed.** The photographic (CC0-scan) tiles read as muddy at the 64px game size — real grain averaged down to 64 pixels becomes noise, not detail — so two alternatives were generated at the same geometry for comparison in-game: **flat** (near-solid Australian colour per terrain, board-game legibility) and **soft** (the same palette with gentle low-frequency mottling). `tools/terrain_style.py` builds either; **soft is currently installed** pending Chris's pick. **Bug 1:** the top-down square-tile lookup used the `BaseFor` *mapped* ground, so `mountains` (mapped to `tundra`) drew FreeCol's white SNOW tile in central Australia, and forest canopy tiles were never drawn at all — while the tree overlay had already been suppressed, so forests rendered as bare ground. Lookup now tries the terrain's own name first. **Bug 2:** the overlay-suppression flag now keys on whether a square tile was actually drawn. Added a whole-continent capture mode (`CAPTURE_WHOLEMAP=1`). | (this commit) |
| 2026-07-26 | **WS2.5b — native square terrain tiles for the top-down view, and sourced art finally used.** Top-down (expected to become the main view) had no art of its own: it warped 128×64 isometric diamonds onto 64px squares, which cannot tile — showing seams and repeating diagonal artefacts. `MapView` now prefers `terrain/<name>/top{0,1}.png` and draws it 1:1, falling back to the de-skew when absent (ocean, polar types, and the whole classic ruleset). The tiles are **CC0 ambientCG ground scans** (the base that actually fixes the seams) re-toned to the Australian palette, two variants per terrain to break up visible repetition, built by `tools/build_topdown_terrain.py` from inputs committed under `tools/cc0-ground/`. Also recorded the licences ruled out (CC BY-SA 3.0/4.0 and GPL 3.0 are GPL-v3-only; this project is v2). | (this commit) |
| 2026-07-26 | **WS2.5 — Australian terrain and forest art, and the map wired to the art seam.** 19 tiles re-toned from FreeCol's own art (GPL→GPL) by a checked-in, reproducible HSV transform (`tools/retone_australian_terrain.py`): red-ochre desert, dry gold pasture, dusty grey-green eucalypt; ocean and polar tiles untouched. **Also fixed a real gap:** `MapView` loaded terrain from a hard-coded `res://assets/freecol/` path in `_Ready()`, so the map bypassed the WS1.3 variant seam entirely and could never show variant terrain however much art existed — it now loads through `ColonyArt.LoadTexture`, with `ReloadTerrainArt()` called once the variant is known (`_Ready` runs before that). Sourced art was attempted first and rejected on style and licence grounds (see §1). | (this commit) |
| 2026-07-26 | **Initial implementation — WS2.1 art direction + the Australian theme skin.** `ColonyTheme` gained a `Skin` switch (Classic / Australia) with the palette re-toned per the table above — sun-bleached paper, jarrah/red-gum timbers, and the accent moved from gold to Federation blue. Implemented as a palette switch inside the existing builder, so all ~40 `ColonyTheme.Get()` call sites were untouched. Added `ColonyRegionColor` and a per-colony swatch on each Federation-panel region row, replacing six rows distinguishable only by their label. Classic values are bit-for-bit unchanged and the skin resets to `Classic` at scene setup, so every existing visual golden holds. | (this commit) |
