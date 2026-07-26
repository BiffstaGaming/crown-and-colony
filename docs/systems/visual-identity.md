# System: Australian visual identity

| | |
|---|---|
| **Status** | In development — WS2.1 art direction + the Australian theme skin implemented. Unit / building / terrain art (WS2.4/2.5), the title and menu art (WS2.2), flags and emblems (WS2.3) are not built. |
| **Last verified** | 2026-07-26 (WS2.1 — the skin re-tones every panel in Australia mode; classic palette bit-for-bit unchanged so existing visual goldens hold) |
| **Code** | `game/presentation/ColonyTheme.cs` (the skin + palette + colony colours), `game/presentation/GameController.cs` (skin selection), `game/presentation/FederationPanel.cs` (colony swatches) |
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

**Deliberately not done here:** unit, building and terrain art (WS2.4/2.5) — those need sourced or drawn assets, not a
palette; the title/logo and menu splash (WS2.2); flags and colony emblems (WS2.3). This is the layer they will sit on.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L3 Interaction | Yes | `ColonyArtTests` — switching to the Australian skin genuinely re-tones the palette (the accent moves from warm-dominant gold to blue-dominant Federation blue) while both skins keep the same structural styleboxes; switching back restores the classic accent exactly; each of the six colonies has a distinct colour and an unknown region falls back rather than throwing. | ✅ green |
| L4 Visual | Existing goldens must be unaffected | The menu and classic-panel goldens are captured with the skin reset to `Classic`. | ✅ unchanged |

## 5. Open issues / TODO

- [ ] WS2.2 game title / logo / menu splash art.
- [ ] WS2.3 nation flag + six colony emblems (the swatches here are the placeholder for those).
- [ ] WS2.4 / WS2.5 unit, building and terrain art — the largest remaining visual gap now that portraits are done.
- [ ] WS6.7 Australian visual goldens, so this skin is regression-guarded at L4 and not only at L3.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-26 | **Initial implementation — WS2.1 art direction + the Australian theme skin.** `ColonyTheme` gained a `Skin` switch (Classic / Australia) with the palette re-toned per the table above — sun-bleached paper, jarrah/red-gum timbers, and the accent moved from gold to Federation blue. Implemented as a palette switch inside the existing builder, so all ~40 `ColonyTheme.Get()` call sites were untouched. Added `ColonyRegionColor` and a per-colony swatch on each Federation-panel region row, replacing six rows distinguishable only by their label. Classic values are bit-for-bit unchanged and the skin resets to `Classic` at scene setup, so every existing visual golden holds. | (this commit) |
