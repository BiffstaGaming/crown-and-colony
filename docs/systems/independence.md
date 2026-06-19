# System: Independence (the War of Independence)

| | |
|---|---|
| **Status** | In progress — declaration + muster (7), REF arrival/war combat (8), the victory (9). The defeat condition (10) follows. |
| **Last verified** | 2026-06-19 @ Win: defeat REF + Spanish Succession (`86d3c9vfn`, save v42) |
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
  - **continental-army muster**: veteran soldiers upgrade to **colonial regulars**, capped per colony with SoL > 50 at `(unitCount + 2) · (SoL − 50) / 100` (the most fervent rise first);
  - the **Royal Expeditionary Force** enters play as a new `RoyalExpeditionaryForce` AI player **at war** with the rebel, its amassed [force](royal-expeditionary-force.md) realised into real units mustering in Europe.
- **The war** (`RunRefTurn`, item 8): each REF turn, units still in Europe **land** (`LandRefUnits`) onto the nearest empty tiles around the rebel's connected-port colonies (land units ashore, ships on the adjacent water), then the REF **prosecutes the war** — because the rebel *is* the human, the REF reuses the existing foreign-power war AI (hunt + assault the rebel's units and colonies). The REF draws entirely from **its own RNG stream**; the rebel attacks the REF through the normal stream-0 combat. The REF↔rebel war stance never cools (the colonial-tension decay touches only `Colonial` players).
- **Winning** (`ResolveWarOfIndependence`, item 9): each turn, the rebel wins (FreeCol `checkForREFDefeat`) when the REF **holds no colonies**, the rebel's **land power ≥ 1.5×** the REF's, and the REF is reduced **below 7 land or 2 naval** units (counting its whole force, so the win can't fire before the army even lands). On winning (`GiveIndependence`): peace with the King, the rebel becomes **`Independent`** with **no tax**, the **surviving redcoats on the map surrender** to the new nation (the fleet sails home), and that nation is the **`Winner`**.
- **Spanish Succession** (`RunSpanishSuccession`, a separate event): once, from **1600**, a fading European AI (SoL < 50) is **absorbed** by the dominant one (SoL > 50) — its colonies and units change hands. (Not the win condition; a late-game consolidation.)

**Deviations from original / FreeCol:** the gate (SoL ≥ 50, ≥ 1 connected port, year cutoff), the muster cap, and the veteran→colonial-regular upgrade match FreeCol. The REF lands as units in "Europe" (a holding state) before the invasion, then ashore near rebel ports. The REF reuses the foreign-power war AI rather than a bespoke amphibious doctrine (faithful-subset). The REF combat ambush-penalty and a dedicated naval transport doctrine are deferred; the last-colonial-year is a code constant pending ruleset routing (`86d3c9rg6`). Native re-stancing on declaration and the on-declaration mercenary offer are deferred.

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
- [x] Win: defeat/expel the REF → independence granted + Spanish Succession (`86d3c9vfn`, save v42).
- [ ] Lose: rebel loses its last connected port (`86d3c9vh1`).
- [ ] Native re-stancing + on-declaration mercenary offer; last-colonial-year via ruleset (`86d3c9rg6`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-19 | **Declare Independence**: `PlayerType` Rebel/Independent/REF; `NationalSonsOfLiberty`; `CheckDeclareIndependence`/`DeclareIndependence` (gate + lose-Europe + continental muster veteran→colonial-regular + REF player at war on its own stream). Save **v41** (`DeclaredIndependenceTurn` + `InterventionBells` + REF player/units). REF landing/combat + win/lose follow | P6 (`86d3c9v28`) |
| 2026-06-19 | **REF arrival + war combat**: `RunRefTurn` lands the REF (`LandRefUnits`) near rebel ports then reuses the foreign-power war AI to assault the rebel — all on the REF's own RNG stream; the REF↔rebel war stance never decays (colonial-only). No save change (REF units persist via v41). Deterministic twin-game + round-trip tested | P6 (`86d3c9v8k`) |
| 2026-06-19 | **Victory**: `ResolveWarOfIndependence` (per-turn) fires `GiveIndependence` on `CheckForRefDefeat` (no REF colonies + rebel land power ≥ 1.5× REF + REF < 7 land/2 naval) → rebel becomes `Independent` (tax 0), redcoats surrender, `Winner` set. `RunSpanishSuccession` (year ≥ 1600, once). Save **v42** (`SpanishSuccession` flag, omit-until-fired) | P6 (`86d3c9vfn`) |
