# System: Players & nations

| | |
|---|---|
| **Status** | Implemented (single human; foreign powers/natives become players in later FP slices) |
| **Last verified** | 2026-06-14 @ FP-1 (Player extraction) |
| **Code** | `game/src/GameLogic/GameSession/Player.cs` · `game/src/GameLogic/GameSession/Game.cs` |
| **Tests** | `game/tests/GameLogic.Tests/Persistence/SaveGameTests.cs` (`V19Save_LoadsAsSingleHumanPlayer`), `Scenarios/` (soak/journey acid tests), the economy/founding-father/immigration suites |
| **FreeCol reference** | `freecol/src/net/sf/freecol/common/model/Player.java` (player-scoped state; note FreeCol's `Player.units` is derived, not authoritative) |
| **Related systems** | [save-load](save-load.md), [randomness](randomness.md), [founding-fathers](founding-fathers.md), [trade](trade.md), [game-modes](game-modes.md) |

## 1. How it works (plain English)

*Audience: anyone — no jargon, no class names.*

Every side in the game is a **player**. Today there is exactly one — **you**, the human colonial power — but the game is now built so that foreign European powers and the native nations can join as players too, without rewriting the rules. Each player carries its *own* private things: its treasury and tax rate, its *own* European market (prices it sees when it trades), the liberty bells it has banked and the Founding Fathers it has elected, its immigration progress and the colonists waiting on its Europe dock, and the map it has personally explored (its fog of war). The shared world — the map itself, all the units and colonies on it, the native settlements, whose turn it is, and the game's hidden "dice" — stays common to everyone.

**The rules, in plain words:**
- There is one human player; you reach "your" gold, market, Congress, fog, etc. *through that player*.
- Each player's money, market, fathers, immigration and explored map are its own — they do not leak between players.
- Units and colonies are a single shared list; each one remembers which player owns it (the ownership tag itself is filled in across the next slices — today everything not native is yours).
- Saving the game writes each player's private state; loading an old save (from before players existed) treats all of it as belonging to the single human.

**Worked example:**
> You sell 100 sugar in Europe. The sugar price you get, and the gold you receive, come from *your* market and *your* treasury. When a rival power arrives later, it will have sold into *its own* market at *its own* price — your prices are unaffected.

**What the player sees and does:** nothing new on screen. The Europe panel, the colony panel and the HUD read exactly the same treasury, market, dock, immigration and fog as before — they just read them from the human player now.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

- A `Player` owns **only** player-scoped state: identity (`PlayerId`, `NationId`, `IsHuman`, `PlayerType`), `Gold`, `TaxRate`, its own `Market`, `Liberty`/`Congress`/`CurrentFather`/`OfferedFathers`, `Immigration`/`ImmigrationRequired`/`BaseRecruitPrice`/`RecruitLowerCap`/`RecruitDock`, and `Explored`.
- The world stays on `Game`: the map, ruleset, turn counter, the main RNG, units, colonies, native settlements, and the global id counters.
- The human is **player id 0**, `IsHuman = true`, `PlayerType = Colonial`, `NationId = null` (the classic human has no European nation type until FP-3). It is found via `Game.HumanPlayer` / `IsHuman`, **never by list index** (turn order is spawn/variant-driven).
- **Determinism (ADR-009):** for FP-1 the human draws from the game's single main RNG stream (stream 0); the refactor does not add, remove, or reorder any RNG draw, so seeded games, goldens and save-resume stay byte-identical. Per-player RNG streams arrive with the foreign powers.

| Input / condition | Result |
|---|---|
| Read `game.Gold` / `Market` / `Congress` / `Explored` / `RecruitPrice` … | Pass-through to `game.HumanPlayer.X` (human-only) |
| Sell/Buy/Recruit/Visit/trade (a mutating action) | Public method delegates to an internal `Player`-taking overload, which mutates that player's state |
| `EndTurn` accrues liberty + immigration | Credited to the human player (it owns every colony in FP-1); FP-4/5 loop all players |
| Load a v20+ save | Player state read from the save's `Players[]` |
| Load a v19-and-earlier save | The flat top-level fields fold into one human player (`Players[]` absent) |

**Deviations from FreeCol:** FreeCol keeps each player's `units`/`settlements` as live lists on the player; we keep units and colonies as **flat global lists referenced by an owner id** (FreeCol itself treats those lists as derived, not the source of truth). Founding-father modifier/ability resolution (`ApplyGoodsModifiers`, `HasAbility`) still reads the *human's* Congress in FP-1 — per-player folding lands with AI economy (FP-5). Live visibility (`CurrentlyVisible`/`IsVisible`) is still derived from the non-native units + colonies rather than a per-player owner filter; it becomes per-owner once the owner-id seam lands (FP-2). The **stored** fog (`Explored`) is already per-player.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:**
- `Player` (`GameSession/Player.cs`) — `sealed class` holding player-scoped state. Public read API (identity + scalars with `internal set`; `IReadOnlyList`/`IReadOnlySet` views over private collections), with `internal` mutable accessors (`CongressList`, `OfferedFathersList`, `RecruitDockList`, `ExploredSet`) used by the rules on `Game`. `RecruitPrice` is computed here. `PlayerType` enum = `{ Colonial, Native }`.
- `Game` holds `IReadOnlyList<Player> Players`, a cached `HumanPlayer` (the human established at creation by its `IsHuman` role), and exposes the former player fields as thin pass-through properties to `HumanPlayer`. Mutating seams have an internal `(Player player, …)` overload; the public method delegates to `HumanPlayer`.
- `RestoredPlayer` (`GameSession/Player.cs`) — a domain DTO (positions, dictionaries) handed to `Game.Restore`, keeping `Game` free of persistence-format types.

**Algorithms & formulas:** the turn/economy logic is unchanged — the same methods now read/write a `Player` (`AccumulateLibertyAndElectFathers(player)`, `AccumulateImmigrationAndEmigrate(player)`, `GenerateOffers(player)`, `Init/Draw/Emigrate` recruit-dock helpers, `Reveal(player, …)`/`RevealAround(player, …)`).

**Integration points:** `Game.New` constructs the human `Player` (with its `Market`) and passes it to the private constructor; `Game.Restore` builds each player from a `RestoredPlayer`, tops up its dock, loads the world, then applies fog. `EndTurn` accrues for the human.

**Persistence:** see [save-load](save-load.md). Save **v20** introduced a `Players[]` array (`SavedPlayer` record: identity + gold/tax/market/liberty/Congress/immigration/dock + explored as compact row-major indexes). Load path is keyed on `Version >= 20`; ≤v19 saves fold the flat fields into one human player (`SaveGame.FoldFlatFieldsToHumanPlayer`). A v20 save still writes the flat fields for now (every pre-v20 load path stays exercised); they are dropped at FP-7.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SaveGameTests.V19Save_LoadsAsSingleHumanPlayer` (fold), `LoadedGame_ContinuesIdenticalRandomSequence` (determinism), the economy/father/immigration/market suites (read through `HumanPlayer`) | ✅ |
| L2 Scenario | Always | save-mid-game acid test + soak (interrupted vs uninterrupted runs end byte-identical) | ✅ |
| L3 Interaction | No new UI | Europe/colony/HUD scene tests still green (public surface unchanged) | ✅ |
| L4 Visual | No new screen | golden screenshots unchanged (fog/HUD) | ✅ |
| L5 Soak | Covered by global suite | autoplay soak deterministic | ✅ |

- **FreeCol cross-check:** structural only (no numeric behaviour changed). `Player.java` consulted for the player-scoped/world-scoped split and the "units list is derived" decision (ADR-019).

## 5. Open issues / TODO

- [ ] Owner-id seam: generalise `Unit.OwnerNationId` → authoritative owner id; enemy/fog tests become owner-inequality + stance (FP-2).
- [ ] European nations as variant data (`EuropeanNationType`) + inert rival players (FP-3).
- [ ] Per-player RNG streams; AI explore/move/found, economy, combat/diplomacy (FP-4…FP-6).
- [ ] Per-player founding-father modifier/ability resolution and per-owner live visibility.
- [ ] Save consolidation: drop the legacy flat fields, freeze + verify the format (FP-7).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | FP-1: extracted `Player` (single human, zero behaviour change); player-scoped state off `Game`; save v20 (`Players[]`, v19 folds to one human player) | FP-1 |
