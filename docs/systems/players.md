# System: Players & nations

| | |
|---|---|
| **Status** | Implemented (multi-player: the human + inert foreign powers + native nations as players; AI lands in FP-4+) |
| **Last verified** | 2026-06-14 @ FP-3b (inert multi-player) |
| **Code** | `game/src/GameLogic/GameSession/Player.cs` · `game/src/GameLogic/GameSession/Game.cs` · `Units/Unit.cs` (`OwnerId`) · `Colonies/Colony.cs` (`OwnerId`) |
| **Tests** | `game/tests/GameLogic.Tests/Persistence/SaveGameTests.cs` (`V19Save_LoadsAsSingleHumanPlayer`), `Scenarios/` (soak/journey acid tests), the economy/founding-father/immigration suites |
| **FreeCol reference** | `freecol/src/net/sf/freecol/common/model/Player.java` (player-scoped state; note FreeCol's `Player.units` is derived, not authoritative) |
| **Related systems** | [save-load](save-load.md), [randomness](randomness.md), [founding-fathers](founding-fathers.md), [trade](trade.md), [game-modes](game-modes.md) |

## 1. How it works (plain English)

*Audience: anyone — no jargon, no class names.*

Every side in the game is a **player**. Today there is exactly one — **you**, the human colonial power — but the game is now built so that foreign European powers and the native nations can join as players too, without rewriting the rules. Each player carries its *own* private things: its treasury and tax rate, its *own* European market (prices it sees when it trades), the liberty bells it has banked and the Founding Fathers it has elected, its immigration progress and the colonists waiting on its Europe dock, and the map it has personally explored (its fog of war). The shared world — the map itself, all the units and colonies on it, the native settlements, whose turn it is, and the game's hidden "dice" — stays common to everyone.

**The rules, in plain words:**
- The game now has several players: **you** (the human), three **foreign powers** (rival European nations), and the **native nations**. Today only you actually do anything — the others are placed but sit still ("inert") until their computer-player brains are added in later steps. Each foreign power starts with its colonists and ship waiting in its own Europe.
- A turn goes around the players in a ring: you take your turn, then each other player takes theirs (empty, for now), then the world ticks over (alarm cools, the turn counter advances) and it's your turn again.
- There is one human player; you reach "your" gold, market, Congress, fog, etc. *through that player*.
- Each player's money, market, fathers, immigration and explored map are its own — they do not leak between players.
- Units and colonies are a single shared list; each one remembers which player owns it (an owner id — the human is 0). Whether two units are friends or enemies, and whose fog a unit lifts, is now decided by *who owns them*, not by "is it native" — so when foreign powers arrive their units are correctly treated as rivals with no extra rules.
- Saving the game writes each player's private state; loading an old save (from before players existed) treats all of it as belonging to the single human.

**Worked example:**
> You sell 100 sugar in Europe. The sugar price you get, and the gold you receive, come from *your* market and *your* treasury. When a rival power arrives later, it will have sold into *its own* market at *its own* price — your prices are unaffected.

**What the player sees and does:** nothing new on screen. The Europe panel, the colony panel and the HUD read exactly the same treasury, market, dock, immigration and fog as before — they just read them from the human player now.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

- A `Player` owns **only** player-scoped state: identity (`PlayerId`, `NationId`, `IsHuman`, `PlayerType`), `Gold`, `TaxRate`, its own `Market`, `Liberty`/`Congress`/`CurrentFather`/`OfferedFathers`, `Immigration`/`ImmigrationRequired`/`BaseRecruitPrice`/`RecruitLowerCap`/`RecruitDock`, and `Explored`.
- The world stays on `Game`: the map, ruleset, turn counter, the main RNG, units, colonies, native settlements, and the global id counters.
- **Ownership (FP-2):** `Unit.OwnerId` and `Colony.OwnerId` are the authoritative colonial-owner ids (the human is 0; foreign powers have their own ids). Native units carry `OwnerNationId` (the native nation id). The rules resolve ownership through `Game` helpers: `IsHumanOwned`, `IsOwnedBy(unit, player)`, `SameOwner(a, b)`, and the enemy test `AreEnemies(a, b)` = different owner (the single **stance hook** — diplomacy plugs in here in FP-6, stubbed today to "different owner = enemy"). Founding-father abilities resolve to the unit's owning player (`AbilityForUnit`), so a foreign power never uses the human's fathers.
- **Players (FP-3b):** `Game.New` creates the human (id 0), `ForeignPowerCount` (3) inert foreign colonial powers (the first selectable non-REF European nations — their starting units begin docked in Europe), and one `PlayerType.Native` player per distinct native nation present. Ids are dense (0, then natives, then foreign powers). `Game.Players` is the uniform list; `Game.CurrentPlayer` is the ring pointer (the human between turns). **Inert** = the foreign powers/natives draw no RNG and take no turn, so the human's RNG stream 0 — and every seeded game/golden — is byte-stable.
- **Turn ring (FP-3b):** `EndTurn` walks `_players` from `_currentPlayerIndex` (via `NextPlayerIndex`), calling `RunPlayerTurn` on each — which only acts for the human (its colonies produce; it accrues liberty/immigration) and is a no-op for inert players — then runs the world steps (sailing, native alarm decay, movement reset, `Turn++`) once and returns to the human.
- **Per-player RNG (ADR-009):** each player reserves a deterministic PCG stream id (`Player.RngStreamId`: human 0; others `PlayerId + 1`, avoiding the native-placement stream 1). The actual streams are created when the AI needs them (FP-4+).
- The human is **player id 0**, `IsHuman = true`, `PlayerType = Colonial`, `NationId = null` (the classic human has no European nation type until FP-3). It is found via `Game.HumanPlayer` / `IsHuman`, **never by list index** (turn order is spawn/variant-driven).
- **Determinism (ADR-009):** for FP-1 the human draws from the game's single main RNG stream (stream 0); the refactor does not add, remove, or reorder any RNG draw, so seeded games, goldens and save-resume stay byte-identical. Per-player RNG streams arrive with the foreign powers.

| Input / condition | Result |
|---|---|
| Read `game.Gold` / `Market` / `Congress` / `Explored` / `RecruitPrice` … | Pass-through to `game.HumanPlayer.X` (human-only) |
| Sell/Buy/Recruit/Visit/trade (a mutating action) | Public method delegates to an internal `Player`-taking overload, which mutates that player's state |
| `EndTurn` accrues liberty + immigration | Credited to the human player (it owns every colony in FP-1); FP-4/5 loop all players |
| Load a v20+ save | Player state read from the save's `Players[]` |
| Load a v19-and-earlier save | The flat top-level fields fold into one human player (`Players[]` absent) |

**Deviations from FreeCol:** FreeCol keeps each player's `units`/`settlements` as live lists on the player; we keep units and colonies as **flat global lists referenced by an owner id** (FreeCol itself treats those lists as derived, not the source of truth). Founding-father *production* modifiers (`ApplyGoodsModifiers`, `HasAbility`) still read the *human's* Congress in FP-1/FP-2 — per-player folding lands with AI economy (FP-5); combat abilities already resolve per-owner (`AbilityForUnit`, FP-2). Live visibility (`CurrentlyVisible`/`IsVisible`) is filtered to the **human's own** units + colonies (FP-2); it's still a single human fog (one viewer) — true per-player fog arrives with the AI. Natives are `Player` rows from FP-3b, but a native unit's owner is still its `OwnerNationId` (its `OwnerId` is unused, 0); wiring native units to their player id is deferred (not needed while natives are inert here). The foreign powers begin **in Europe** rather than landing on the map — on-map landing comes with the AI explore slice (FP-4).

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
| L1 Unit | Always | `OwnerTests` (owner-id seam: enemy/fog isolation, owner round-trip), `MultiPlayerTests` (inert foreign powers + native players, ring turn, multi-element save), `SaveGameTests.V19Save_LoadsAsSingleHumanPlayer` (fold → one human), `LoadedGame_ContinuesIdenticalRandomSequence` (determinism), the economy/father/immigration/market suites (read through `HumanPlayer`) | ✅ |
| L2 Scenario | Always | save-mid-game acid test + soak (interrupted vs uninterrupted runs end byte-identical) | ✅ |
| L3 Interaction | No new UI | Europe/colony/HUD scene tests still green (public surface unchanged) | ✅ |
| L4 Visual | No new screen | golden screenshots unchanged (fog/HUD) | ✅ |
| L5 Soak | Covered by global suite | autoplay soak deterministic | ✅ |

- **FreeCol cross-check:** structural only (no numeric behaviour changed). `Player.java` consulted for the player-scoped/world-scoped split and the "units list is derived" decision (ADR-019).

## 5. Open issues / TODO

- [x] Owner-id seam: authoritative `Unit.OwnerId`/`Colony.OwnerId`; enemy/fog/abilities resolve by owner + a stance hook (FP-2 ✅).
- [x] European nations as variant data (`EuropeanNationType`/`EuropeanNation` parsed into the ruleset, FP-3a ✅) — see [ruleset-data](ruleset-data.md). [x] Inert foreign-power + native players using that data, ring-buffer turn, multi-element save (FP-3b ✅).
- [ ] AI turns: explore/move/found (FP-4), economy (FP-5), combat + diplomacy (FP-6) — give the inert players behaviour and their own RNG streams; wire native units to their player id; land the foreign powers on the map.
- [ ] FP-4 carry-overs flagged by review (latent, harmless while rivals are inert): synthesize native `Player` rows when loading a pre-FP-3b save (a fresh game has them, an upgraded save doesn't); persist `_currentPlayerIndex` once turns can be saved mid-ring; harden foreign-power selection with an explicit `OrderBy` (it relies on ruleset insertion order today).
- [ ] Per-player RNG streams; AI explore/move/found, economy, combat/diplomacy (FP-4…FP-6).
- [ ] Per-player founding-father modifier/ability resolution and per-owner live visibility.
- [ ] Save consolidation: drop the legacy flat fields, freeze + verify the format (FP-7).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | FP-1: extracted `Player` (single human, zero behaviour change); player-scoped state off `Game`; save v20 (`Players[]`, v19 folds to one human player) | FP-1 |
| 2026-06-14 | FP-2: owner-id seam — `Unit.OwnerId`/`Colony.OwnerId`; enemy/fog/ability rules resolve by owner + a stance hook; save v20 gains optional owner ids (additive, null = human). Zero behaviour change with human + natives | FP-2 |
| 2026-06-14 | FP-3a: parsed European nations + nation-types into the ruleset (`EuropeanNation`/`EuropeanNationType`/`EuropeanStartingUnit`) — the four classic powers + REFs, advantages, starting units, per-nation classic colony names. Data only (no players, nothing saved); `FoundColony` adopts per-nation names in FP-3b | FP-3a |
| 2026-06-14 | FP-3b: multi-player — `Game.New` spawns 3 inert foreign colonial powers (units in Europe) + native nations as `Player` rows; ring-buffer `EndTurn` (`CurrentPlayer`/`NextPlayerIndex`, only the human acts); reserved per-player RNG streams (`RngStreamId`); save v20 persists multi-element `Players[]`; `FoundColony` uses per-nation colony names (human keeps the default). Byte-stable: seeded games/goldens/soak unchanged | FP-3b |
