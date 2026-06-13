# System: Colonies

| | |
|---|---|
| **Status** | Implemented (founding only; economy is Phase 3) |
| **Last verified** | 2026-06-13 @ Phase 2b |
| **Code** | `game/src/GameLogic/Colonies/Colony.cs`, `GameSession/Game.cs` (`CheckFoundColony`/`FoundColony`) · rendering: `game/presentation/ColonyMarker.cs` |
| **Tests** | `GameTests.FoundColony_*`, `SaveGameTests.RoundTrip_PreservesColonies` |
| **FreeCol reference** | `Colony.java`, `BuildColonyMessage` — minimum-distance rule pending cross-check |
| **Related systems** | [units-movement](units-movement.md), [save-load](save-load.md), [ruleset-data](ruleset-data.md) |

## 1. How it works (plain English)

Select a colonist and press **B** to found a colony where it stands. The colonist settles down and becomes the colony's first inhabitant (it leaves the map). Colonies show as a small house with their name; click one to see its vitals in the status bar. Names come from a classic colonial list (Jamestown, Plymouth, …) in founding order.

## 2. Detailed rules

| Condition | Result |
|---|---|
| Unit type lacks the foundColony ability (e.g. ships) | rejected |
| Terrain is not settleable (mountains, water) | rejected |
| Tile already has a colony | rejected |
| Otherwise | colony founded, population 1, founding unit consumed |

**Economy tick (every EndTurn, first economy slice):**
| Step | Effect |
|---|---|
| 1. Colony square produces | the tile's unattended yield goes to the stores (plains: grain 3 + cotton 2) |
| 2. Colonists eat | 2 food per colonist; grain drains before fish; stores floor at 0 |
| 3. Growth | at ≥200 stored food: −200 food, +1 population |

Goods enter the warehouse under their spec `stored-as` id — grain/fish/meat all become **food** (one warehouse entry, matching FreeCol; the earlier grain+fish shortcut is gone and legacy saves normalize on load). **Starvation is deliberately deferred** — a food shortfall currently just floors at 0.

**Tile workers (economy slice 2):**
- Colonists work the 8 tiles around the colony, one colonist per tile, each producing **one chosen goods type** at the terrain's best attended yield (`Game.TileYield`); ocean tiles fish.
- `CheckAssignWork`/`AssignWork`/`UnassignWork` oracles; rejects: off-map, non-adjacent, tile taken, no idle colonist, terrain can't produce the goods.
- Founding and growth **auto-assign** to the best free grain tile (deterministic tie-break); the player can rearrange (re-assignment UI is the economy-UI slice).
- Idle colonists produce nothing (building jobs are a later slice).

**Deviations from original / FreeCol — PENDING CROSS-CHECK:** FreeCol enforces a minimum distance between colonies and the original restricts founding adjacent to existing colonies; we currently only block the same tile. Cross-check and adopt when colony spacing starts to matter (Phase 3).

## 3. Technical design

- `Colony`: id, name, position, population (mutable internal — grows in Phase 3).
- `Game.CheckFoundColony(unit)` → `MoveCheck` oracle; `FoundColony(unit)` enforces and mutates (removes unit, adds colony). Same oracle/command pattern as movement (ADR-006).
- Save format **v3** adds `Colonies`; pre-v3 saves load with none (tested).
- Rendering: `ColonyMarker` (`_Draw` house + name via `ThemeDB.FallbackFont`), one per colony under `MapView/ColonyLayer`, reconciled per refresh.
- UI handles zero units (founding consumes the last one): marker hidden, status shows the latest colony.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | found-on-settleable consumes unit/creates colony; rejections (ship, mountains, occupied tile) | ✅ |
| L2 Scenario | Always | save/load round-trip preserves colonies; pre-v3 compat | ✅ |
| L3 Interaction | Yes (B key, click) | TODO — covered by kanban task [QA] L3 input tests | ⬜ |
| L4 Visual | Yes (marker) | TODO with visual harness | ⬜ |

## 5. Open issues / TODO

- [ ] Minimum colony distance rule (cross-check vs FreeCol/original).
- [ ] Real colony screen (kanban [P2b] colony screen skeleton → Phase 3 economy UI).
- [ ] Nation-specific colony name lists when nations exist.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Founding (B key), colony marker, save v3 | Phase 2b |
| 2026-06-13 | FreeCol settlement art; colony panel (click colony → name, population, terrain, colony-square yield; Close button). `GameController.OpenColonyPanel` is the public entry; L3-tested | Phase 2c |
| 2026-06-13 | Economy slice 1: goods stores, colony-square production tick, eat 2/colonist, growth at 200 food (save v4; panel shows stores + growth progress). Consumption/growth values consistent with the original — formal cross-check when goods-types are parsed | Phase 3 |
| 2026-06-13 | Economy slice 2: tile workers (assign/unassign oracles, per-tile chosen goods, ocean fishing, auto-assign on founding/growth, save v5, panel lists workers) | Phase 3 |
