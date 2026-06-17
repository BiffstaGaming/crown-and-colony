# System: Lost City Rumours

| | |
|---|---|
| **Status** | In development — **placement + save (v25)** and the outcome table (explore trigger + nothing / vanish / tribal-chief gold / learn / colonist / Fountain of Youth / **ruins / cibola treasure trains**) done; **strange mounds** + burial ground is the remaining outcome slice |
| **Last verified** | 2026-06-17 @ ruins/cibola treasure finds (`86d3c9t1e`) |
| **Code** | `game/src/GameLogic/World/LostCityRumourGenerator.cs`, `World/GameMap.cs` (`HasRumour`/`Rumours`/`AddRumour`/`RemoveRumour`), `GameSession/Game.cs` (`Game.New` placement step + `LcrStreamId`; `TryExploreRumour`/`ExploreRumour`/`ChooseRumourType`/`WeightedPick`/`GenerateFountainRecruits`; the `MoveUnit`/`Disembark` hooks), `Specification/UnitChange.cs` (`UnitChangeTypeIds.LostCity`), `Persistence/SaveGame.cs` (`Rumours`, v25) |
| **Tests** | `game/tests/GameLogic.Tests/World/LostCityRumourTests.cs` |
| **FreeCol reference** | `SimpleMapGenerator.makeLostCityRumours`, `LostCityRumour.chooseType`, `ServerUnit.csExploreLostCityRumour`, `RandomChoice.getWeightedRandom`/`normalize`, `Tile.hasLostCityRumour`/`removeLostCityRumour`, `EuropeanStartingPositionsGenerator` (start-area removal) |
| **Related systems** | [map-terrain](map-terrain.md), [save-load](save-load.md), [fog-of-war](fog-of-war.md), [units-movement](units-movement.md), [combat](combat.md) |

## 1. How it works (plain English)

Scattered across the new world are **mysterious ruins** — Lost City Rumours. At the start of every game a handful of land tiles are quietly seeded with one (you won't see them through the fog until you get close). They sit out in the wilderness, never on a settlement, never on top of a unit, never right on your doorstep, and never up in the frozen polar fringe.

**Walking a land unit onto a rumour tile investigates it** — and *that's* when the dice are rolled. Right now the possible results are:

- **Nothing** — just an empty ruin.
- **A tribal chief's gift** — a handful of gold (40–119) lands in your treasury.
- **A learned skill** — a plain colonist (or an indentured servant / petty criminal) emerges a **seasoned scout**.
- **New colonists** — a free colonist joins you, standing on the tile.
- **A Fountain of Youth** — a rush of immigrants (eight, at the standard difficulty) crowds onto your dock in Europe, ready to be shipped over. (Rare.)
- **Ancient ruins** — a modest find: a little gold for a small ruin, or, for a richer one, a **treasure train** you escort home.
- **A city of gold (Cibola)** — the dream: a **big treasure train** (thousands of gold) appears on the tile to escort home. (The rarest find.)
- **The expedition vanishes** — the exploring unit is lost for good. (This is the rare bad outcome.)

The one remaining result not wired up yet is **strange mounds** (its own slice). Whatever the result, the rumour is a one-shot: investigating it clears it from the map for good. Only your own colonists explore — a native brave walking over a rumour leaves it untouched.

The more land a map has, the more rumours appear (roughly one per 35 land tiles).

## 2. Detailed rules

- **How many:** target = `width × height × 45% / 35` (FreeCol's "a rumour every `rumourNumber`=35 land tiles", using our generator's ~45%-land estimate in place of FreeCol's `landMass` option). On the default 36×24 map that's **11**. The actual number can be fewer if eligible tiles run out (never more).
- **Where a rumour may sit:** dry **land**; not on a tile that already has a rumour; not on a **settlement** (colony or native); not on a tile holding a **unit**; not in the **polar rows** (FreeCol `Map.isPolar`: the top and bottom three rows); not in the player's **3×3 start area** (FreeCol removes rumours around a starting colony).
- **Placement is once, at game start**, and is **deterministic for a seed** — the same seed always produces the same rumour tiles.
- **Exploring** a rumour removes it (one-shot); a consumed rumour never comes back and is absent from later saves.

### Exploring a rumour (the outcome roll)

- **Trigger:** a **colonial (non-native) land unit** that *moves onto* (or *disembarks onto*) a tile holding a rumour investigates it immediately (FreeCol fires on any move where `newTile.hasLostCityRumour() && owner.isEuropean()`). A native brave never explores; a ship never can (rumours are on land). The roll uses the **owner's** RNG stream (the human's, or an AI power's own) so one player exploring never disturbs another's economy.
- **The weighted table** (FreeCol `LostCityRumour.chooseType`, classic **medium** difficulty — *good 48% / bad 23% / neutral 29%*):
  - **Good** (weight ×48): **Fountain of Youth 2** (always available to a colonial explorer); then a unit that *can learn* splits **Learn 30 / Tribal-chief 30 / Colonist 20**, a unit that cannot (an expert, a soldier, artillery, …) splits **Tribal-chief 50 / Colonist 30**; then, for any explorer, the treasure finds **Ruins 6 / Cibola 4** (Mounds 8 stays deferred to its slice).
  - **Bad** (a flat weight of 100, per FreeCol normalising the bad sub-list to 100): **Expedition vanishes**. (The burial ground needs native-owned tiles, not modelled yet, so the vanishing expedition is the whole bad side — exactly how FreeCol degrades it with no burial available.)
  - **Neutral** (weight ×29): **Nothing**.
- **The outcomes:**
  - **Nothing** — no effect.
  - **Tribal chief** — gold = `random(0…dx·10) + dx·5` with `dx = 10 − rumourDifficulty`; medium `dx = 8`, so **40–119 gold** to the unit's owner.
  - **Learn** — the unit's `model.unitChange.lostCity` change applies (free colonist / indentured servant / petty criminal → **seasoned scout**), keeping its id and remaining movement.
  - **Colonist** — a **free colonist** (the only classic unit with `model.ability.foundInLostCity`) musters on the tile under the explorer's owner.
  - **Fountain of Youth** — `dx` (medium **8**) fresh immigrants are generated onto the owner's Europe dock, each a weighted recruit draw (FreeCol `ServerEurope.generateFountainRecruits`). They arrive as units *in Europe* (not as the three recruit candidates), so the owner still ships them across.
  - **Ruins** — `random(0…dx·2)·300 + 50` gold (medium 50–4550): a small find (**< 500**) is paid straight to the owner's treasury; a larger one spawns a **treasure train** on the tile (see [treasure-train](treasure-train.md)).
  - **Cibola** — a city of gold: `random(0…dx·600) + dx·300` (medium **2400–7199**) as a **treasure train** on the tile.
  - **Expedition vanishes** — the exploring unit is removed from the game.
- A learnable unit can only *learn* if it has a `lostCity` change; an expert has none, so its good rolls fall to Fountain-of-Youth / tribal-chief / colonist (FreeCol's `allowLearn` gate).

**Deviations from original / FreeCol:** the **count estimate** uses our ~45%-land fraction rather than FreeCol's `landMass`=25% option (we have no such option; 45% matches the land our continents actually grow, so the count matches what the player sees — faithful to FreeCol's *intent*, a rumour per 35 land tiles, not to the 25 constant). We skip FreeCol's `SLOSH` edge-inset sampler — our maps already keep a watery margin, so uniform sampling over land tiles gives the same inset effect. **Difficulty constants** are hardcoded to classic **medium** (`badRumour=23`, `goodRumour=48`, `rumourDifficulty=2`→`dx=8`), matching the project's medium baseline (the founding-father factor is likewise medium = 40); the Ruleset parses no difficulty options yet, so a per-level value is a later refinement. We do **not** replicate Java's RNG bit-sequence — faithfulness is to the *rules and weights*, drawn through our seeded RNG (ADR-009). **Fountain of Youth** generates its `dx` immigrants directly (FreeCol's AI path); FreeCol lets the *human* hand-pick each one via a select-recruit prompt, which we don't have a UI for yet, so we generate them like the AI does — documented. **Cibola** always spawns a treasure train: FreeCol draws from a finite list of "Seven Cities of Gold" names and, once they're exhausted, degrades Cibola to a Ruins-style find — we don't model that finite global list (no `NameCache`), so every Cibola is a city of gold (a minor, documented deviation; the amounts match). **Deferred outcomes** (intentionally absent from the table until their slices land): **strange mounds** + native-owned tiles & burial ground (`86d3c9umy`). The **seasoned-scout exploration bonus** and **Hernando de Soto's** always-positive ability (good-outcome bias) are a deferred refinement. While the still-deferred good outcomes (mounds) are absent, the table re-weights slightly toward the shipped ones until they're added.

## 3. Technical design

- **Placement** lives in `Game.New` (not `MapGenerator.Generate`): it runs **after** the map, the starting unit, native settlements and foreign powers are placed, so it can exclude every occupied tile and the start area. `LostCityRumourGenerator.Place(map, excluded, random)` returns the chosen positions (eligible land tiles, shuffled by the seeded RNG, take `target`); `Game.New` folds them in via `map.AddRumour`.
- **Determinism (ADR-009):** placement draws from a **dedicated stream** — `new Pcg32Random(seed, LcrStreamId)` with `LcrStreamId = 100`, a reserved id **above every per-player stream** (`Player.RngStreamId = playerId + 1`, so foreign powers occupy 2,3,4…). Because the scatter never touches the human's stream 0 (`_random`), every economy/combat/immigration draw — and the L5 soak's byte-stability — is unchanged. The stream is **gen-time only**: it is never saved or resumed (like map gen and native placement); a loaded game rebuilds rumours from the saved tile list, not by re-scattering.
- **Tile model:** `GameMap` holds a sparse `HashSet<Position> _rumours`, parallel to `_resources` — `HasRumour(p)`/`Rumours` (read) and `AddRumour`/`RemoveRumour` (internal: place at gen, consume on explore). A rumour is **not** stored on `TerrainType` (immutable rule-data).
- **Save (v25, additive):** `SaveGame.Rumours` is a row-major `int[]` of rumour tile indexes, **omitted when empty** so a rumour-free game stays byte-identical to v24 and pre-v25 saves load with none. `From` writes `game.Map.Rumours.Select(p => p.Y·W + p.X)`; `Restore` decodes them into the `GameMap` ctor's `rumours` param (no value needed — presence only). **No save bump for the outcome slice:** exploring only mutates already-saved state — units (spawned/removed/upgraded), player gold, and the v25 rumour set (a consumed tile drops out) — so a save round-trips unchanged. (The treasure finds will add a per-unit treasure amount → save **v26**, a later slice.)

- **Outcome resolution** lives in `Game.cs` (it touches units, gold and the map, like combat):
  - **Trigger.** `MoveUnit` and `Disembark` call `TryExploreRumour(unit, target)` *after* the move/landing completes. It gates on a colonial (`!IsNative`) **land** (`!IsNaval`) unit on a `Map.HasRumour(target)` tile, then resolves via `ExploreRumour(unit, target, RandomFor(owner))`. Mirroring FreeCol's `csMove`, the explore runs once the unit has arrived and its fog has lifted.
  - **Per-owner RNG (ADR-009).** The reward draws from `RandomFor(owner)` — the human's stream 0, a foreign power's own stream — exactly as combat threads `IGameRandom` through its internal `Attack*` overloads. The human path consumes stream 0 (its *own* exploration), AI paths their own streams, so an AI exploring never shifts the human's economy and vice-versa. The L5 soak (whose autoplay now steps on rumours) stays byte-stable and the determinism-twin runs match.
  - **Choosing + resolving.** `ChooseRumourType` builds the weighted `(type, weight)` list (good/bad/neutral as above; Fountain of Youth is listed first in the good branch, mirroring FreeCol) and `WeightedPick` selects it (FreeCol `getWeightedRandom`: a single entry returns with no draw, otherwise `random.Next(total)` walks the cumulative weights). `ExploreRumour` then applies the effect (`_units.Remove` / `Player.Gold +=` / `UpgradeUnitType` / `SpawnUnit(type, target, ownerId)` / `GenerateFountainRecruits`) and finally `Map.RemoveRumour(target)`. `UpgradeUnitType` replaces the unit object (same id), so callers must treat the passed reference as spent — both hooks call the explore last and touch the unit no further.
  - **Fountain of Youth.** `GenerateFountainRecruits(ownerId, random)` lands `dx` recruits in Europe — `dx` × `CreateEuropeRecruit(owner, DrawRecruitType(owner, random))` (FreeCol `ServerEurope.generateFountainRecruits`). The injected `random` is threaded through a new `DrawRecruitType(Player, IGameRandom)` overload (the legacy `DrawRecruitType(Player)` now delegates to it with `RandomFor(player)`), so the FoY type roll and the `dx` recruit draws run sequentially on **one** owner stream — no hidden `RandomFor` re-entry inside the explore. A no-op for a player with no recruitable unit types (minimal rulesets). The recruits are real units `InEurope` (already saved), so **no save bump**.
  - **Difficulty constants.** `RumourBadPercent=23`, `RumourGoodPercent=48`, `RumourDifficultyDx=8` (`10 − rumourDifficulty(2)`) are private consts pinned to classic medium; `FoundInLostCityUnitTypeId="model.unit.freeColonist"`; the learn change is `UnitChangeTypeIds.LostCity` (`model.unitChange.lostCity`, already parsed by `Ruleset`).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `LostCityRumourTests`: **placement** — scatter on land at the target count, deterministic-for-seed, exclusions (water / polar rows / start 3×3 / settlements / units / no-dup), over-constrained map tolerated, `GameMap` add/has/remove, save round-trip (v25) + token-omitted/back-compat. **Outcomes** — each result pinned by a scripted weighted roll on the FoY-first cumulative boundaries (vanish removes the unit; tribal-chief gold 40/119 by formula; learn upgrades a free colonist → seasoned scout keeping its id; colonist musters a free colonist on the tile; Fountain of Youth lands `dx`=8 recruits in Europe; **ruins** pays 50 gold on a small find or spawns a 1550-gold treasure train on a rich one; **cibola** spawns a 2400-gold treasure train; nothing leaves units+gold untouched), the `allowLearn` gate (a non-learnable expert's low roll gives gold not a skill), and the triggers (move onto a rumour, amphibious disembark onto one, and a native brave leaving it untouched) | ✅ |
| L2 Scenario | Always | the L5 soak now exercises live exploration (autoplay units step on rumours): all invariants hold, gold never negative, save→load→save byte-identical, and the determinism-twin runs match — confirming resolution leaks no non-determinism and each owner's stream stays isolated | ✅ |
| L3 Interaction | No UI yet | rumour markers + the outcome message/prompt on the map are a later presentation slice | — |
| L4 Visual | No screen yet | — | — |

## 5. Open issues / TODO

- [x] **Placement + per-tile flag + save** (`86d3c9uex`).
- [x] **Explore trigger + core outcome table** (`86d3c9uhj`): the move/disembark-onto-tile hook + the weighted reward table (nothing / vanish / tribal-chief gold / learn / colonist), drawn per-owner (`RandomFor(owner)`). Deferred from this slice: burial-ground (needs native tile ownership), the treasure finds, mounds, and the scout / Hernando de Soto good-outcome bias (below).
- [x] **Fountain of Youth** (`86d3c9ujx`): `FOUNTAIN_OF_YOUTH` added to the table → `GenerateFountainRecruits` lands `dx`=8 immigrants on the owner's Europe dock (reuses `DrawRecruitType`/`CreateEuropeRecruit`; no save field).
- [ ] **Scout / De Soto exploration bias** (refinement of `86d3c9uhj`): the seasoned-scout `exploreLostCityRumour` modifier + `expertScout` never-vanish gate, and De Soto's `rumoursAlwaysPositive`. Needs unit-type ability/modifier parsing.
- [ ] **Strange-mounds prompt** (`86d3c9umy`): add `MOUNDS`/`BURIAL_GROUND` to the table + the generation-time MOUNDS pre-set for native-owned tiles. **Native tile ownership now exists** (`GameMap.IsNativeOwned`/`NativeOwnerOf`, see [natives](natives.md)), so the burial-ground gate (`tile is native-owned` → bad sub-list = BURIAL_GROUND 25 + EXPEDITION_VANISHES 75, normalised) is ready to wire; the burial-ground native-war *effect* (hateful alarm + war) is the documented later refinement.
- [x] **Treasure train unit + spawn-on-sack** (`86d3c9ryj`): the treasure-train unit + carried amount (save **v27**); sacking a native settlement now spawns one instead of instant gold; capturable. See [treasure-train](treasure-train.md).
- [x] **Treasure trains from LCR** (`86d3c9t1e`): `RUINS` (gold < 500, else a treasure train) and `CIBOLA` (a big treasure train) are in the table, spawning trains via the v27 amount. See [treasure-train](treasure-train.md).
- [ ] **Cash in a treasure train** (`86d3c9rzu`): King's transport cut (60%, Cortés-free) + tax, or sail it home.
- [ ] Map **rumour markers** + the outcome message/prompt (presentation).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | **Treasure finds — ruins & cibola** (`86d3c9t1e`): `RUINS` (weight 6 ×goodPct → `rand(0…dx·2)·300+50`; < 500 = gold, else a treasure train) and `CIBOLA` (weight 4 → `rand(0…dx·600)+dx·300` treasure train) added to the table, spawning trains via the v27 amount (new `SpawnTreasureTrain` helper, also used by the settlement sack). No save bump. Cumulative test boundaries re-pinned (Vanish 4416 / Nothing 4516); +3 L1; 726 + soak green. Cibola's finite "seven cities" limit not modelled (documented). MOUNDS remains deferred. | Phase 5 (`86d3c9t1e`) |
| 2026-06-17 | **Fountain of Youth** (`86d3c9ujx`): `FOUNTAIN_OF_YOUTH` added to the weighted table (good weight 2, always available to a colonial explorer, listed first); resolving it calls `GenerateFountainRecruits(owner, random)` → `dx`=8 weighted recruits land in Europe (FreeCol `ServerEurope.generateFountainRecruits`), reusing a new `DrawRecruitType(Player, IGameRandom)` overload so the burst stays on the owner's stream. No save bump. Test boundaries shifted FoY-first; +1 L1. 691 + soak green. Mounds/treasure + the scout/De Soto bias remain. | Phase 5 (`86d3c9ujx`) |
| 2026-06-17 | **LCR core outcome resolution** (`86d3c9uhj`): `MoveUnit`/`Disembark` now investigate a rumour a colonial land unit steps onto (`TryExploreRumour`), rolling the FreeCol `chooseType` weighted table (classic medium: good 48 / bad 23 / neutral 29) restricted to the shipped outcomes — **nothing / expedition-vanishes / tribal-chief gold (40–119) / learn (→ seasoned scout) / colonist (found free colonist)**. Draws per-owner via `RandomFor(owner)`; `UnitChangeTypeIds.LostCity` added. No save bump (only already-saved state changes). +10 L1; 690 + soak green (byte-stable, twin-deterministic). FoY/mounds/treasure + the scout/De Soto bias are the next slices. | Phase 5 (`86d3c9uhj`) |
| 2026-06-17 | **LCR placement + model + save** (`86d3c9uex`): `LostCityRumourGenerator` scatters ~`land/35` rumours at `Game.New` from a dedicated RNG stream (`LcrStreamId`=100, off the human's stream 0) clear of settlements/units/start/polar; `GameMap` gains `HasRumour`/`Rumours`/`AddRumour`/`RemoveRumour`; save **v25** adds the additive `Rumours` index list (omitted when none). +7 L1; 684 + soak green (byte-stable). Outcomes/treasure are the next slices. | Phase 5 (`86d3c9uex`) |
