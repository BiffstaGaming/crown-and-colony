# System: Independence (the War of Independence)

| | |
|---|---|
| **Status** | In progress — declaring independence (gate + continental muster + the REF takes the field), item 7. The REF landing/combat (8) and win/lose (9-10) follow. |
| **Last verified** | 2026-06-19 @ Declare Independence + continental muster (`86d3c9v28`, save v41) |
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

Winning means defeating or driving off that army; losing means being driven from your last port (those resolutions are the next pieces).

**What the player sees and does:** nothing yet — the Declaration screen and the war UI are P7. The GameLogic decision is exposed for that UI to call.

## 2. Detailed rules

- **The gate** (`CheckDeclareIndependence`, FreeCol `model.event.declareIndependence` limits): still a plain **colonial** power, **national Sons of Liberty ≥ 50%** (rebels across all colonies ÷ total population), **≥ 1 connected-port colony**, and the **year ≤ the last colonial year** (1800).
- **Declaring** (`DeclareIndependence`):
  - the player becomes a **`Rebel`** and records the turn (drives the score and the intervention force later);
  - every unit **in or bound for Europe is removed**; the **recruit dock is cleared**;
  - **continental-army muster**: veteran soldiers upgrade to **colonial regulars**, capped per colony with SoL > 50 at `(unitCount + 2) · (SoL − 50) / 100` (the most fervent rise first);
  - the **Royal Expeditionary Force** enters play as a new `RoyalExpeditionaryForce` AI player **at war** with the rebel, its amassed [force](royal-expeditionary-force.md) realised into real units mustering in Europe (they sail and land next).

**Deviations from original / FreeCol:** the gate (SoL ≥ 50, ≥ 1 connected port, year cutoff), the muster cap, and the veteran→colonial-regular upgrade match FreeCol. The REF lands as units in "Europe" (a holding state) before the invasion. The last-colonial-year is a code constant pending ruleset routing (`86d3c9rg6`). Native re-stancing on declaration and the on-declaration mercenary offer are deferred.

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
- [ ] REF arrival/landing + War-of-Independence combat (`86d3c9v8k`).
- [ ] Win: defeat/expel the REF → independence granted + Spanish Succession (`86d3c9vfn`).
- [ ] Lose: rebel loses its last connected port (`86d3c9vh1`).
- [ ] Native re-stancing + on-declaration mercenary offer; last-colonial-year via ruleset (`86d3c9rg6`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-19 | **Declare Independence**: `PlayerType` Rebel/Independent/REF; `NationalSonsOfLiberty`; `CheckDeclareIndependence`/`DeclareIndependence` (gate + lose-Europe + continental muster veteran→colonial-regular + REF player at war on its own stream). Save **v41** (`DeclaredIndependenceTurn` + `InterventionBells` + REF player/units). REF landing/combat + win/lose follow | P6 (`86d3c9v28`) |
