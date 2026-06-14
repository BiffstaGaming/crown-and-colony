# System: Colonies

| | |
|---|---|
| **Status** | Implemented (founding + min colony spacing, full colony economy, membership join/leave, bonus-resource yields) |
| **Last verified** | 2026-06-14 @ FP-5 (minimum colony-distance rule) |
| **Code** | `game/src/GameLogic/Colonies/Colony.cs`, `GameSession/Game.cs` (`CheckFoundColony`/`FoundColony`) · rendering: `game/presentation/ColonyMarker.cs` |
| **Tests** | `GameTests.FoundColony_*`, `SaveGameTests.RoundTrip_PreservesColonies` |
| **FreeCol reference** | `Colony.java`, `BuildColonyMessage`, `Player.canClaimToFoundSettlementReason` (adjacent-colony rule, ✅ cross-checked) |
| **Related systems** | [units-movement](units-movement.md), [save-load](save-load.md), [ruleset-data](ruleset-data.md) |

## 1. How it works (plain English)

Select a colonist and press **B** to found a colony where it stands. The colonist settles down and becomes the colony's first inhabitant (it leaves the map). Colonies show as a small house with their name; click one to see its vitals in the status bar. Names come from a classic colonial list (Jamestown, Plymouth, …) in founding order.

## 2. Detailed rules

| Condition | Result |
|---|---|
| Unit type lacks the foundColony ability (e.g. ships) | rejected |
| Terrain is not settleable (mountains, water) | rejected |
| Tile already has a colony | rejected |
| Tile is adjacent to an existing colony | rejected (minimum colony spacing — colony footprints never touch) |
| Otherwise | colony founded, population 1, founding unit consumed |

**Economy tick (every EndTurn, first economy slice):**
| Step | Effect |
|---|---|
| 1. Colony square produces | the tile's unattended yield goes to the stores (plains: grain 3 + cotton 2) |
| 2. Colonists eat | 2 food per colonist; grain drains before fish; stores floor at 0 |
| 3. Growth | at ≥200 stored food: −200 food, +1 population |

Goods enter the warehouse under their spec `stored-as` id — grain/fish/meat all become **food** (one warehouse entry, matching FreeCol; legacy saves normalize on load). **Starvation:** a food shortfall starves one colonist that turn (population floors at 1 — colony destruction is a future rule); assignments shrink to fit, pulling workshop workers before field workers.

**Buildings (economy slice 4):**
- New colonies start with the **free base buildings** (no build cost, not an upgrade): town hall, carpenter's/blacksmith's/artisan houses, pasture, etc. Construction of costed buildings/upgrades is the next slice.
- Building production runs in the tick after tiles: **unattended** entries always run (town hall rings 1 bell/turn; the pasture breeds horses from food **only when ≥2 horses are stabled** — the spec's breeding-number gate); **worker** entries convert warehouse inputs per assigned colonist (carpenter: lumber 3 → hammers 3), scaled down when inputs run short.
- `CheckAssignBuildingWork`/`AssignBuildingWork`/`UnassignBuildingWork` oracles; workplaces cap (default 3); idle accounting spans tiles + buildings.

**Construction (economy slice 5):**
- `SetBuild` queues one buildable (validated: not owned, has a cost, upgrade prerequisite owned, population requirement met; `Buildables` lists legal targets).
- Completion is material-based: when the stores cover the full cost (hammers/tools), materials are consumed and the building appears — upgrades **replace** their predecessor, keeping its staff. No partial-progress tracking (matches accumulate-then-spend; FreeCol's per-turn hammer sink is a future cross-check).

**Tile workers (economy slice 2):**
- Colonists work the 8 tiles around the colony, one colonist per tile, each producing **one chosen goods type** at the terrain's best attended yield (`Game.TileYield`); ocean tiles fish.
- **Bonus-resource yields (slice 8):** a tile's special deposit boosts what's produced there — `TileYield` applies the resource's spec modifiers (e.g. minerals +3 ore, prime sugar ×2), then the player's Founding-Father goods modifiers (Henry Hudson +100% furs), in ascending modifier-index order. A resource never *enables* a good the terrain can't already make. Expert-scoped resource bonuses (an extra bonus when an expert works the tile) are parsed but **not applied** — we don't track which colonist works a tile yet. See [ruleset-data](ruleset-data.md) (`ResourceType`) and [founding-fathers](founding-fathers.md) (the modifier system).
- `CheckAssignWork`/`AssignWork`/`UnassignWork` oracles; rejects: off-map, non-adjacent, tile taken, no idle colonist, terrain can't produce the goods.
- Founding and growth **auto-assign** to the best free grain tile (deterministic tie-break); the player can rearrange (re-assignment UI is the economy-UI slice).
- Idle colonists produce nothing (building jobs are a later slice).

**Colonist membership (slice 9):**
- **Join** — a colonist (any person unit) on or next to a colony can `JoinColony`: population +1, the unit leaves the map, the newcomer is auto-assigned to a food tile. This is the payoff of immigration ([immigration](immigration.md)/[transport](transport.md)): ship a recruit home, disembark by a colony, and it grows the colony.
- **Leave** — `LeaveColony` detaches a colonist onto the colony's own tile as a **free colonist** (our colony stores a population *count*, not individual types, so the detached unit is generic), population −1; a colony must keep ≥ 1 colonist, and a fully-staffed colony vacates one job to fit.

**Deviations from original / FreeCol:** ✅ **minimum colony spacing cross-check done (FP-5).** Founding is now blocked on a tile adjacent to an existing colony, matching FreeCol's `Player.canClaimToFoundSettlementReason` (`tile.getAdjacentColonies()` must be empty) and the original's no-touching-footprints rule. Native settlements do **not** block founding (FreeCol treats native proximity as a land claim/price, not a hard bar; we don't model land price yet).

## 3. Technical design

- `Colony`: id, name, position, population (mutable internal — grows in Phase 3).
- `Game.CheckFoundColony(unit)` → `MoveCheck` oracle; `FoundColony(unit)` enforces and mutates (removes unit, adds colony). Same oracle/command pattern as movement (ADR-006).
- Save format **v3** adds `Colonies`; pre-v3 saves load with none (tested).
- Rendering: `ColonyMarker` (`_Draw` house + name via `ThemeDB.FallbackFont`), one per colony under `MapView/ColonyLayer`, reconciled per refresh.
- UI handles zero units (founding consumes the last one): marker hidden, status shows the latest colony.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | found-on-settleable consumes unit/creates colony; rejections (ship, mountains, occupied tile, **adjacent to an existing colony** — `FoundColony_Rejected_AdjacentToAnExistingColony`); **resource yields** (`ResourceYieldTests`: resource boosts, expert-scope skipped, no-enable guard, Hudson ×2 furs, resource+father stack order) | ✅ |
| L2 Scenario | Always | save/load round-trip preserves colonies; pre-v3 compat; production uses the boosted resource yield (`ResourceYieldTests.Production_UsesTheBoostedYield`); **join/leave** (`ColonyMembershipTests`: grow on join, detach a free colonist, keep ≥1, trim a job; round-trip) + `JourneyTests.Journey9` (ship a recruit home → join → colony grows) | ✅ |
| L3 Interaction | Yes | `InputTests` (B founds), `MainSceneTests` (panel opens/closes), `ColonyPanelTests` (staff/unstaff buttons, release field worker, construction dropdown + stop) | ✅ |
| L4 Visual | Yes (marker) | colony golden (`colony-seed424242`) | ✅ |

## 5. Open issues / TODO

- [x] Minimum colony distance rule (no founding adjacent to an existing colony) — cross-checked vs FreeCol, adopted (FP-5).
- [ ] Real colony screen (kanban [P2b] colony screen skeleton → Phase 3 economy UI).
- [ ] Nation-specific colony name lists when nations exist.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Founding (B key), colony marker, save v3 | Phase 2b |
| 2026-06-14 | Minimum colony spacing: `CheckFoundColony` rejects a tile adjacent to an existing colony (FreeCol `canClaimToFoundSettlementReason`); resolves the long-standing TODO. Applies to the human and the AI | FP-5 |
| 2026-06-13 | FreeCol settlement art; colony panel (click colony → name, population, terrain, colony-square yield; Close button). `GameController.OpenColonyPanel` is the public entry; L3-tested | Phase 2c |
| 2026-06-13 | Economy slice 1: goods stores, colony-square production tick, eat 2/colonist, growth at 200 food (save v4; panel shows stores + growth progress). Consumption/growth values consistent with the original — formal cross-check when goods-types are parsed | Phase 3 |
| 2026-06-13 | Economy slice 2: tile workers (assign/unassign oracles, per-tile chosen goods, ocean fishing, auto-assign on founding/growth, save v5, panel lists workers) | Phase 3 |
| 2026-06-13 | Economy slice 3+4: stored-as goods model; free base buildings, building jobs (input→output conversions, workplaces cap), town-hall bells, breeding-gated pasture; save v6 | Phase 3 |
| 2026-06-13 | Economy slice 5: construction queue (SetBuild/Buildables oracles, material-based completion, upgrades replace + keep staff); save v7 | Phase 3 |
| 2026-06-13 | Economy slice 6: starvation on food shortfall (pop floors at 1, assignments trimmed workshop-first); bare-square boom-bust cycle pinned by long-run test | Phase 3 |
| 2026-06-13 | Economy UI: interactive colony screen (`ColonyPanel.cs`) — staff/unstaff buildings, release field workers, send idle to fields, construction dropdown with costs + stop; all via Game oracles, L3-tested | Phase 3 |
| 2026-06-13 | Bonus-resource yield modifiers in `TileYield` (+ Henry Hudson's +100% furs); expert-scoped resource bonuses parsed but deferred (no per-colonist identity). No save change | Phase 4 slice 8 |
| 2026-06-13 | Colonist membership: `JoinColony` (grow a colony, the immigration payoff) and `LeaveColony` (detach a free colonist). No save change | Phase 4 slice 9 |
