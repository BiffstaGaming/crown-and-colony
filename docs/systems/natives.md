# System: Native nations & settlements

| | |
|---|---|
| **Status** | In development (Phase 5 slice 1: nations + settlement data, placement, rendering, persistence. Interaction / trade / tension / combat are later slices.) |
| **Last verified** | 2026-06-13 @ Phase 5 slice 1 |
| **Code** | `game/src/GameLogic/Specification/NativeNationType.cs`, `Natives/NativeSettlement.cs`, `World/NativeSettlementGenerator.cs`; rendering `game/presentation/NativeSettlementMarker.cs` + `GameController.SyncNativeMarkers` |
| **Tests** | `GameLogic.Tests/Specification/NativeNationTypeTests.cs`, `GameLogic.Tests/GameSession/NativeSettlementTests.cs`; visual `presentation/tests/VisualGoldenTests.cs` |
| **FreeCol reference** | `freecol/data/rules/classic/specification.xml` `<indian-nation-types>`; `freecol/src/.../server/generator/SimpleMapGenerator.java` (`makeNativeSettlements`) |
| **Related systems** | [ruleset-data](ruleset-data.md), [map-terrain](map-terrain.md), [fog-of-war](fog-of-war.md), [save-load](save-load.md) |

## 1. How it works (plain English)

The New World is already inhabited. When a game begins, the indigenous nations — the Apache, Sioux, Tupi, Arawak, Cherokee, Iroquois, Inca and Aztec — already have settlements dotted across the map. You discover them as you explore; a settlement you have never seen stays hidden under the fog.

**The rules, in plain words:**
- There are **eight** native nations. Three live in **camps** (Apache, Sioux, Tupi), three in **villages** (Arawak, Cherokee, Iroquois), and two in great **cities** (Inca, Aztec).
- Each nation has **one capital** (bigger, better defended) plus a few ordinary settlements. Some nations spread more widely than others.
- Settlements sit on land, never crowd each other, and never spawn right on top of your landing site — you always get room to settle.
- Each settlement quietly knows a **skill** it could teach a visiting colonist (e.g. expert farmer) — visiting, trading and learning come in later slices.

**Worked example:**
> You sail in and found your first colony. A few turns later your scout walks north-east and the fog peels back to reveal an Iroquois village — a cluster of longhouses with "Iroquois" on its name plate. Press on and you might find their capital, marked with a ★.

**What the player sees and does:** native settlements draw on the map (camp / village / Inca-city / Aztec-city art) with the nation's name beneath, capitals starred. For now they are landmarks you discover — you cannot yet interact with them.

## 2. Detailed rules

**The eight nations** (settlement family · spread · temperament):

| Nation | Settles as | Number of settlements | Aggression |
|---|---|---|---|
| Apache | camp | average | high |
| Sioux | camp | average | average |
| Tupi | camp | high | low |
| Arawak | village | low | high |
| Cherokee | village | average | low |
| Iroquois | village | average | average |
| Inca | city | low | low |
| Aztec | city | high* | high |

\* Aztec `number-of-settlements` is `low` in the classic spec (inherited from the abstract `city` type); only the city *settlement* art/size differs from the Inca. (Aztec are simply the aggressive city nation.)

**Settlement types** (pinned from the spec):

| Type | Size (min–max) | Capital size | Claimable radius | Defence bonus |
|---|---|---|---|---|
| Camp | 4–6 | 6–8 | 1 (+2) | +50% (capital +100%) |
| Village | 6–8 | 8–10 | 1 (+2) | +50% (capital +100%) |
| City (Inca/Aztec) | 8–12 | 10–12 | 2/3 (+2/3) | +100% (capital +200%) |

- Every placed nation gets exactly **one capital**; the rest are non-capital settlements.
- **Per-nation count** (a tuned simplification — see deviations): low band → 1 settlement, average → 2, high → 3. Capitals are placed first (all nations) so every nation appears even when space is tight.
- A settlement tile must be **settleable land** (not water, not mountains/arctic) with **at least half its neighbours land** (no settlements on thin spits/islets).
- Settlements are at least **3 tiles apart** (Chebyshev) and at least 3 tiles from the player's landing tile and its neighbours (so the nearest a settlement can be to the start is 4 tiles — outside the starting colonist's sight).
- Each settlement's **size** is a random value in its type's range; its **taught skill** is a weighted random pick from the nation's skill list (or none if the nation lists no skills).

**Deviations from original 1994 / FreeCol behavior:**
- **Settlement counts & placement regions.** FreeCol assigns each nation a named map *region* and scales settlement counts by landmass and difficulty. Our map has no named regions yet, so we place settlements greedily on suitable tiles with a fixed per-band count (`NativeSettlementGenerator.TargetCount`). The *suitability* rule (settleable + ≥50% land neighbourhood) and the *capital-first* rule are kept. Counts/regions can be tuned toward FreeCol when the map gains regions.
- **Aggression / skills are parsed but not yet used.** They feed the tension and learn-skill slices (P5 slices 3–4); stored now so the data layer is complete.
- **`<plunder>` / `<gifts>` not parsed yet.** Combat plunder and gift-giving arrive with their slices; only the settlement's core attributes + defence modifier are parsed today.

## 3. Technical design

**Domain model:**
- `Specification.NativeNationType` (record) — one per concrete nation: its non-capital + capital `SettlementType` ids, `SettlementNumber` band, `NativeAggression`, the weighted `NativeSkill` list, and preferred region ids. Abstract templates (`default`/`camp`/`village`/`city`) are resolved away, not exposed.
- `Specification.SettlementType` (record) — a camp/village/city template (capital variant is a separate record): sizes, claimable radii, trade bonus, convert threshold, and the parsed `model.modifier.defence` percentage (used by the combat slice).
- `Natives.NativeSettlement` — a placed settlement: id, owning `NationTypeId`, `SettlementTypeId`, `IsCapital`, `Position`, `Size`, `LearnableSkill`. (A minimal "owner" concept — the full multi-player `Player`/`Nation` refactor is deferred to the foreign-European slice.)

**Data sources:** `specification.xml` → `<indian-nation-types>` (parsed in `Ruleset.ParseNativeNationTypes`). `extends` chains are resolved nearest-wins for attributes and the settlement template; **skills and regions accumulate** down the chain (so the Inca inherit the abstract `city` skills and add their own). A null section (minimal test rulesets) yields no native nations.

**Algorithms & formulas:** `World/NativeSettlementGenerator.Place(ruleset, map, random, excluded)` — suitable tiles are gathered and Fisher–Yates-shuffled (seeded), capitals placed first, then non-capitals round-robin; `TakeTile` enforces the min-distance against placed settlements and the excluded set. Sizes/skills drawn from the RNG.

**Integration points:** generated once in `Game.New` on a **dedicated RNG stream** (`NativeStreamId = 1`, ADR-009) so placement cannot shift the economy/father/immigration draws (stream 0). Exposed via `Game.NativeSettlements` and `Game.NativeSettlementAt(pos)`. Rendering: `GameController.SyncNativeMarkers` makes one `NativeSettlementMarker` per settlement **on an explored tile** (fog-gated), in the scene's `MapView/NativeLayer`; art selected per settlement type (FreeCol `indian_camp` / `indian_village` / `inca_city` / `aztec_city`, ADR-014).

**Persistence:** save format **v14** adds `SavedNativeSettlement[]`; settlements round-trip verbatim (placement is not replayed, so the native RNG stream is not saved). Pre-v14 saves load with no native settlements.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `NativeNationTypeTests` (8 nations, settlement sizes/radii/defence pinned to spec, `extends` resolution, no-natives ruleset); `NativeSettlementTests` (size-in-range, determinism, save round-trip, v14) | ✅ |
| L2 Scenario | Always | `NativeSettlementTests`: a new classic game places ≥1 settlement + exactly one capital per nation, all on settleable land, spaced ≥3 apart and clear of the player | ✅ |
| L3 Interaction | No UI yet (read-only landmarks) | — | — |
| L4 Visual | Yes (drawn on the map) | `native-settlement-seed424242` golden (a revealed settlement) | ✅ |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** nation roster, settlement-type sizes, claimable radii and defence modifiers are pinned in `NativeNationTypeTests` against `specification.xml`. Placement *counts/positions* deliberately diverge (see deviations) and are verified by invariant, not by matching FreeCol's numbers.

## 5. Open issues / TODO

- [ ] **Native interaction** (P5 slice 3): visit a settlement — speak-with-chief (gifts/tales), learn the taught skill; the tension/alarm model (happy→hateful).
- [ ] **Native trade** (P5 slice 4): buy/sell at settlements (demand + `trade-bonus`).
- [ ] **Combat** (P5 slice 5): braves, settlement defence (the parsed defence modifier), plunder/destruction (`<plunder>` parsing).
- [ ] **Settlement growth** over turns; `<gifts>` parsing; map *regions* so placement can follow FreeCol's region/landmass counts.
- [ ] Foreign-European players reuse the owner concept introduced here (P5 foreign-powers slice).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Native nation + settlement parsing, `NativeSettlement` domain, placement (capital-first, min-distance, dedicated RNG stream), save v14, FreeCol art rendering (fog-gated) | Phase 5 slice 1 |
