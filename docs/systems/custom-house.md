# System: Custom house

| | |
|---|---|
| **Status** | In development — **slice 1: the building's export ability + per-colony export settings + the game-wide auto-export mode + save (v28)** done; the **per-turn auto-sell** that actually ships goods to Europe is the next slice (`86d3c9rx2`) |
| **Last verified** | 2026-06-17 @ custom house slice 1 (`86d3c9ru3`) |
| **Code** | `game/src/GameLogic/Specification/BuildingType.cs` (`GrantsExport`) + `Ruleset.cs` (parse `model.ability.export`); `Colonies/Colony.cs` (`ExportSetting`, `Exports`/`ExportOf`/`SetExport`, `DefaultExportLevel`); `GameSession/AutoExportMode.cs`; `GameSession/Game.cs` (`AutoExportMode`/`SetAutoExportMode`, `ColonyHasExportAbility`, `SetColonyExport`); `Persistence/SaveGame.cs` (`SavedColony.Exports`, top-level `AutoExportMode`, v28) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/CustomHouseTests.cs` |
| **FreeCol reference** | `model.building.customHouse` + `model.ability.export`, `ExportData` (per-good exported flag + export level), `Colony.getExportData`, `ServerColony.csNewTurn` custom-house sale |
| **Related systems** | [trade](trade.md), [colony](colony.md), [save-load](save-load.md), [founding-fathers](founding-fathers.md) |

## 1. How it works (plain English)

A **custom house** is a colony building that quietly sells your surplus to Europe **for you, every turn — even when your ports are blockaded or you've declared independence**. Once a colony has built one, you can mark individual goods as **"export this"** and set how much of each you want to **keep in the warehouse** before the rest is shipped off. The custom house then drip-sells anything above that line at the going market price (minus tax), so you stop babysitting galleons just to offload spare sugar or ore.

This first slice puts the **switches** in place: the custom house now *grants* a colony the export ability, each colony remembers **which goods are flagged for export and the keep-level for each** (default: keep 50), and the game has an **auto-export mode** that's **off by default** ("per-good" — only goods you explicitly flag). The part that actually *sells* the goods each turn arrives in the next slice — for now nothing ships, so a game with no export toggles plays exactly as before.

There's also a global mode for players who don't want to flip switches colony-by-colony: **"export everything over the keep-level"**, which (once the selling slice lands) will auto-export every sellable good above its line in every colony that has a custom house. The default stays **per-good** so nothing sells unless you ask.

## 2. Detailed rules

- The **custom house** (`model.building.customHouse`) carries the **export ability** (`model.ability.export`). A colony "has export" when it contains **any** building whose type grants that ability (today only the custom house). No other building grants it. The custom house has **no work slots** (`workplaces` 0) — it's infrastructure, not a workshop.
- Each colony holds a set of **per-good export settings**, each a pair: **exported** (bool) + **export level** (the warehouse amount to keep before selling the rest). The **default** for any good not explicitly set is **not exported, keep 50** (`Colony.DefaultExportLevel`). Setting a good back to exactly that default **forgets** it (keeps saves clean).
- Export can be flagged for any good that is **storable and tradeable** (has a European market) — **food included** (FreeCol's custom house can export food; it just defaults *off*). You can't export hammers/bells/crosses (not goods you trade in Europe). Flagging anything non-tradeable is refused. *(The bulk "export everything over the keep-level" mode below deliberately leaves food alone — auto-dumping food would stop colonies growing — so food only ever auto-sells if you turn it on for that good by hand.)*
- The game carries a single **auto-export mode**:
  - **PerGood** (default) — only goods individually flagged in a colony are eligible (FreeCol behaviour; food is eligible if you flag it).
  - **ExportAllOverLevel** — every sellable good above its keep-level in any custom-house colony is eligible, regardless of the per-good flag, **except food** (protected, so the colony keeps growing).
- **This slice ships no goods.** It is purely the data model + persistence; the per-turn sale (`AutoSellExports` in the colony turn) is the next slice. With the default PerGood mode and no toggles, behaviour — and the save bytes — are unchanged.

**Deviations from original / FreeCol:** FreeCol stores the export level and a per-good "exported" flag in `ExportData`, plus a colony-level "high water mark"; we model the per-good pair and defer the high-water nicety. FreeCol's custom house can be **boycotted**/penalised after independence and has a smuggling penalty; those interact with systems we don't have yet (boycotts, independence) and are deferred. The **auto-export mode** ("export all over level") is our own convenience switch over FreeCol's per-good model — it defaults off so it changes nothing until used; in that bulk mode we **exclude food** (FreeCol has no such mode, and auto-dumping food would halt colony growth). **Food per-good:** FreeCol's custom house *can* export food (food is storable and has a market; the only gate is the export ability + boycotts), and our PerGood mode matches that — food is exportable if explicitly flagged, default off. The original 1994 game never auto-sold food; that behaviour is what our ExportAllOverLevel mode's food exclusion preserves. *(Flagged for Chris: PerGood follows FreeCol here, not the 1994 original — see the session's needs-you note.)*

## 3. Technical design

- **Parse:** `BuildingType.GrantsExport` (`model.ability.export`, parsed in `Ruleset` alongside `BombardsShips`). The custom house inherits nothing relevant from a parent chain, so the ability sits directly on its type.
- **State (colony):** `Colony.ExportSetting(bool Exported, int ExportLevel)` (a `record struct`); a private `Dictionary<string, ExportSetting> _exports` exposed read-only as `Exports`; `ExportOf(goodsId)` returns the stored pair or `new(false, DefaultExportLevel)`. Internal `SetExport(goodsId, exported, level)` floors the level at 0 and **removes** the entry when it equals the default (`!exported && level == 50`) so the dictionary only holds non-default goods.
- **State (game):** `AutoExportMode` enum (`PerGood = 0`, `ExportAllOverLevel = 1`); `Game.AutoExportMode` (default `PerGood`) + `SetAutoExportMode`. `ColonyHasExportAbility(colony)` = `colony.Buildings.Any(b => Ruleset.Building(b).GrantsExport)`. `SetColonyExport(colony, goodsId, exported, exportLevel?)` validates the good is storable+tradeable (food allowed — FreeCol-faithful; else `InvalidMoveException`) and folds to `Colony.SetExport` (default level when `exportLevel` is null).
- **Save (v28, additive):** `SavedColony.Exports` is `IReadOnlyDictionary<string, SavedExport(bool Exported, int Level)>`, **omitted when empty**; the top-level `AutoExportMode?` is **omitted when PerGood**. So a game with no toggles and the default mode serializes **byte-identically to v27** (only the version field differs); pre-v28 saves load with no exports and PerGood. `Restore` folds each saved export via `colony.SetExport(...)` and threads the mode through a new optional `Game.Restore(..., AutoExportMode autoExportMode = PerGood)` parameter.
- **Determinism (ADR-009):** no RNG anywhere in this slice; the L5 soak is byte-stable because the default mode sells nothing and the save tokens are omitted at defaults.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `CustomHouseTests`: the custom house **grants export** (town hall doesn't; 0 workplaces); a colony's export setting **round-trips** and a back-to-default set **forgets** it; `SetColonyExport` **allows food** but **rejects** non-tradeable goods; the auto-export mode **defaults to PerGood**; a toggled export + `ExportAllOverLevel` **round-trips through save v28**; a default game **omits both tokens** | ✅ |
| L2 Scenario | Always | the L5 soak is byte-stable (PerGood default sells nothing; save tokens omitted at defaults) | ✅ |
| L3 Interaction | No UI yet | the custom-house export panel (per-good toggles + level sliders) is a later presentation slice | — |
| L4 Visual | No screen yet | — | — |

## 5. Open issues / TODO

- [x] **Custom house slice 1** (`86d3c9ru3`): building export ability + per-colony export settings + game auto-export mode + save v28. No behaviour change yet.
- [ ] **Auto-sell exports** (`86d3c9rx2`): each colony turn, after warehouse overflow and before food, every eligible good above its keep-level in a custom-house colony is sold to Europe at market price (minus tax), reusing the trade sale path. Eligibility per the mode. (The selling slice.)
- [ ] Custom-house **sale notice** (turn report line: what sold, for how much).
- [ ] Boycott / post-independence penalty interaction (with the boycott + independence systems).
- [ ] Custom-house **export panel** UI (presentation).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | **Custom house slice 1** (`86d3c9ru3`): `model.building.customHouse` grants `model.ability.export` (`BuildingType.GrantsExport`); `Colony` holds per-good `ExportSetting`s (exported + keep-level, default keep 50, default forgotten); `Game.AutoExportMode` (`PerGood` default / `ExportAllOverLevel`) + `SetColonyExport`/`SetAutoExportMode`; save **v28** adds `SavedColony.Exports` (omitted when empty) + top-level `AutoExportMode` (omitted when PerGood) — byte-identical to v27 at defaults. No behaviour change (nothing sells yet). +6 L1; 741 + soak green. Auto-sell is the next slice. | Phase 5 (`86d3c9ru3`) |
