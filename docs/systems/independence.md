# System: Independence (the War of Independence)

| | |
|---|---|
| **Status** | Implemented (GameLogic) — declaration + continental muster, REF arrival + War-of-Independence combat, victory (defeat the REF) + Spanish Succession, and defeat (lose your last port). The war UI is P7. |
| **Last verified** | 2026-06-20 @ REF fixed entry tile near the human start, save v47 (`86d3c9w5n`) |
| **Code** | `game/src/GameLogic/GameSession/Game.Independence.cs`, `Force.cs`; `Player.PlayerType`/`DeclaredIndependenceTurn` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/IndependenceTests.cs` |
| **FreeCol reference** | `InGameController.csDeclareIndependence`, `Player.getSoL`, `model.event.declareIndependence` |
| **Related systems** | [monarchy](monarchy.md), [royal-expeditionary-force](royal-expeditionary-force.md), [sons-of-liberty](sons-of-liberty.md), [combat](combat.md) |

## 1. How it works (plain English)

Once your colonies are fired up enough — at least **half your colonists are rebels** (Sons of Liberty ≥ 50% nationwide) and you hold a **port** — you can **declare independence**. When you do:

- your nation becomes a **rebel** fighting for freedom;
- everything you had **waiting in Europe is lost**, and Europe closes its doors (no more recruiting or trade with the mother country);
- your **veteran soldiers rise up as Continental regulars** — the more fervent your colonies, the more of them;
- and the **King sends his army** — the Royal Expeditionary Force he's been building all game — to crush you. From now on it's war.

From the next turn the **King's army sails in and lands near your ports**, then marches on your colonies and hunts your troops. Winning means defeating or driving off that army; losing means being driven from your last port (those resolutions are the next pieces).

**What the player sees and does:** nothing yet — the Declaration screen and the war UI are P7. The GameLogic decision is exposed for that UI to call.

## 2. Detailed rules

- **The gate** (`CheckDeclareIndependence`, FreeCol `model.event.declareIndependence` limits): still a plain **colonial** power, **national Sons of Liberty ≥ 50%** (rebels across all colonies ÷ total population), **≥ 1 connected-port colony**, and the **year ≤ the last colonial year** (1800).
- **Declaring** (`DeclareIndependence`):
  - the player becomes a **`Rebel`** and records the turn (drives the score and the intervention force later);
  - every unit **in or bound for Europe is removed**; the **recruit dock is cleared**;
  - **continental-army muster**: veteran soldiers upgrade to **colonial regulars**, the cap summed over colonies with SoL > 50 at `(unitCount + 2) · (SoL − 50) / 100` (the earliest rise first);
  - the **Royal Expeditionary Force** enters play as a new `RoyalExpeditionaryForce` AI player **at war** with the rebel, its amassed [force](royal-expeditionary-force.md) realised into real units mustering in Europe.
- **The war** (`RunRefTurn`, item 8): each REF turn, units still in Europe **land** (`LandRefUnits`) — the King's fleet makes landfall at its **fixed entry tile** (a water tile near the human's start, chosen deterministically at game creation and persisted, FreeCol `Player.entryTile`/`86d3c9w5n`): each unit takes the nearest empty tile to that beachhead (`FindLandingTileNear`, land ashore / ships on water), falling back to the ring around the rebel's connected-port colonies when the beachhead is full or unset (a pre-v47 save). Then the REF **prosecutes the war** — because the rebel *is* the human, the REF reuses the existing foreign-power war AI (hunt + assault the rebel's units and colonies). The REF draws entirely from **its own RNG stream**; the rebel attacks the REF through the normal stream-0 combat. The REF↔rebel war stance never cools (the colonial-tension decay touches only `Colonial` players).
- **Winning** (`ResolveWarOfIndependence`, item 9): each turn, the rebel wins (FreeCol `checkForREFDefeat`) when the REF **holds no colonies**, the rebel's **land power ≥ 1.5×** the REF's, and the REF is reduced **below 7 land or 2 naval** units (counting its whole force, so the win can't fire before the army even lands). On winning (`GiveIndependence`): peace with the King, the rebel becomes **`Independent`** with **no tax**, the **surviving redcoats on the map surrender** to the new nation (the fleet sails home), and that nation is the **`Winner`**.
- **Spanish Succession** (`RunSpanishSuccession`, a separate event): once, from **1600**, a fading European AI (SoL < 50) is **absorbed** by the dominant one (SoL > 50) — its colonies and units change hands. (Not the win condition; a late-game consolidation.)
- **Losing** (`IsRebelDefeated`, item 10): once you've declared, holding **no connected port** (`GetNumberOfPorts == 0` — the REF has taken them all) means you've **lost** the War of Independence. This is a flag the presentation reads for the defeat screen; the turn loop keeps running regardless (a defeated human must not freeze stream 0 — ADR-009). A plain colony with no port is never "rebel-defeated".

**Deviations from original / FreeCol:** the gate (SoL ≥ 50, ≥ 1 connected port, year cutoff), the veteran→colonial-regular upgrade, the win thresholds (1.5× / 7 land / 2 naval) and the REF realisation match FreeCol. **Faithful-subset simplifications (documented, not yet faithful):** (1) the **continental muster** caps and draws *nationwide* (the rebel's whole unit count + any on-map veteran), not per-colony from each colony's own residents as FreeCol does — our colony workers aren't in the unit list; this can over-muster in a multi-colony rebellion (per-colony port is a follow-up). (2) the **Spanish Succession** ranks the absorbed/absorbing pair by **Sons-of-Liberty** (we have no overall game-score yet) rather than FreeCol's score, and only an AI (not the human) can satisfy its strong-limit trigger. (3) the REF reuses the foreign-power war AI rather than a bespoke amphibious doctrine; the REF combat ambush-penalty and a dedicated naval transport doctrine are deferred; the last-colonial-year is a code constant pending ruleset routing (`86d3c9rg6`). Native re-stancing on declaration and the on-declaration mercenary offer are deferred.

## 3. Technical design

- `PlayerType` gains `Rebel`, `Independent`, `RoyalExpeditionaryForce` (the lifecycle Colonial → Rebel → Independent; the REF is its own type). `RunPlayerTurn` routes Rebel/Independent through the colonial economy path and the REF through `RunRefTurn` (a stub until item 8).
- `Game.NationalSonsOfLiberty(player)` = `sum(colony.RebelCount) · 100 / max(1, sum(Population))`.
- `CheckDeclareIndependence`/`DeclareIndependence` (ADR-006). `MusterContinentalArmy` upgrades veterans via the existing `UpgradeUnitType` swap (units are immutable in their type). `CreateRefPlayer` adds a non-human `RoyalExpeditionaryForce` player with its **own RNG stream** (seeded off the human's current state, read non-destructively — stream 0 untouched), sets the WAR stance both ways, and realises `_refForce` into units via `SpawnInEurope`.
- **Determinism (ADR-009):** the REF runs on its own stream (like a foreign power); the human/rebel on stream 0; the monarch tick stops once the player is no longer `Colonial`.
- **Save v41:** `SavedPlayer.DeclaredIndependenceTurn` + `InterventionBells` (both omitted before independence); the `PlayerType` ordinals (Rebel/Independent/REF) and the REF **player row** persist for free, and the REF's units via `SavedUnit.OwnerId`; a pre-independence game is byte-identical to v40.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `IndependenceTests`: `NationalSonsOfLiberty`; the declare gate (SoL); declaring turns Rebel + loses Europe units + starts the REF at war + realises ≥ 60 REF units; continental muster upgrades veterans (conservation + ≥ 1); pre-independence save omits the tokens | ✅ |
| L2 Scenario | Always | `IndependenceTests`: EndTurn runs cleanly after declaration (REF stub + rebel colonial path); the full rebellion (Rebel + REF + war stance + REF units) round-trips save/load | ✅ |

- **FreeCol cross-check:** ✅ gate limits + muster cap + REF realisation match `csDeclareIndependence`.

## 5. Open issues / TODO

- [x] Declare Independence + continental muster + REF takes the field (`86d3c9v28`, save v41).
- [x] REF arrival/landing + War-of-Independence combat (`86d3c9v8k`) — `RunRefTurn`/`LandRefUnits`, reuses the war AI on the REF stream.
- [x] REF **fixed entry tile** near the human start (`86d3c9w5n`, save v47) — `Game.RefEntryTile` set at `Game.New` (`NearestWaterTile`), `LandRefUnits` lands the fleet there via `FindLandingTileNear` (falls back to rebel ports / pre-v47 saves).
- [x] Win: defeat/expel the REF → independence granted + Spanish Succession (`86d3c9vfn`, save v42).
- [x] Lose: rebel loses its last connected port (`86d3c9vh1`) — `IsRebelDefeated` (derived; EndTurn doesn't short-circuit).
- [ ] Native re-stancing + on-declaration mercenary offer; last-colonial-year via ruleset (`86d3c9rg6`).
- [ ] The War-of-Independence UI (declaration screen, defeat/victory screens, REF-arrival warning) — P7.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-20 | **REF fixed entry tile** (`86d3c9w5n`, FreeCol `Player.entryTile`): `Game.New` fixes the REF's entry tile to the water tile nearest the human's start (`NearestWaterTile`, deterministic — no RNG draw), stored on `Game.RefEntryTile` and persisted (save **v47**, omitted when unset → pre-v47 loads with none). `LandRefUnits` now lands the King's fleet at that beachhead first (`FindLandingTileNear`), falling back to the rebel's connected-port colonies when it's full/unset — so the REF always makes landfall at a fixed, faithful spot. No RNG draw → stream 0 byte-stable. +3 L1 (`MultiPlayerTests`: entry tile set on water nearest the start, save round-trip, omit-when-unset/pre-v47 load); 1328 L1/L2 + soak green. See [save-load](save-load.md) | P5 (`86d3c9w5n`) |
| 2026-06-19 | **Declare Independence**: `PlayerType` Rebel/Independent/REF; `NationalSonsOfLiberty`; `CheckDeclareIndependence`/`DeclareIndependence` (gate + lose-Europe + continental muster veteran→colonial-regular + REF player at war on its own stream). Save **v41** (`DeclaredIndependenceTurn` + `InterventionBells` + REF player/units). REF landing/combat + win/lose follow | P6 (`86d3c9v28`) |
| 2026-06-19 | **REF arrival + war combat**: `RunRefTurn` lands the REF (`LandRefUnits`) near rebel ports then reuses the foreign-power war AI to assault the rebel — all on the REF's own RNG stream; the REF↔rebel war stance never decays (colonial-only). No save change (REF units persist via v41). Deterministic twin-game + round-trip tested | P6 (`86d3c9v8k`) |
| 2026-06-19 | **Victory**: `ResolveWarOfIndependence` (per-turn) fires `GiveIndependence` on `CheckForRefDefeat` (no REF colonies + rebel land power ≥ 1.5× REF + REF < 7 land/2 naval) → rebel becomes `Independent` (tax 0), redcoats surrender, `Winner` set. `RunSpanishSuccession` (year ≥ 1600, once). Save **v42** (`SpanishSuccession` flag, omit-until-fired) | P6 (`86d3c9vfn`) |
| 2026-06-19 | **Defeat**: `GetNumberOfPorts` + `IsRebelDefeated` — a declared nation with no connected port has lost; a derived presentation flag, EndTurn never short-circuits (ADR-009). No save change | P6 (`86d3c9vh1`) |
