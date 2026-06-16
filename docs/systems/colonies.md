# System: Colonies

| | |
|---|---|
| **Status** | Implemented (founding + min colony spacing, full colony economy, membership join/leave, bonus-resource yields, ownership + **capture/pillage/plunder**, **fortification defence bonus** [stockade/fort/fortress], **La Salle free stockade**) |
| **Last verified** | 2026-06-16 @ FreeCol-look colony screen (production bar + isometric tile view + buildings grid with real building images + warehouse bar; stages 2–5 of the visual rebuild) |
| **Code** | `game/src/GameLogic/Colonies/Colony.cs` (incl. `OwnerId`), `GameSession/Game.cs` (`CheckFoundColony`/`FoundColony`; `ColonyDefenceBonus`/`ColonyDefenceBonusAt`; `ApplyFreeBuildings`; capture/plunder `CapturePlayerColony`/`AdjacentCapturableHumanColony`/`PlunderColony`/`ColonyPlunderAmount`), `Specification/BuildingType.cs` (`DefenceBonus`) · rendering: `game/presentation/ColonyMarker.cs` |
| **Tests** | `GameTests.FoundColony_*`, `SaveGameTests.RoundTrip_PreservesColonies`, `ColonyDefenceBonusTests`, `LaSalleTests`, `ColonyCaptureTests`, `ForeignColonyCaptureTests`, `ColonyPlunderTests` |
| **FreeCol reference** | `Colony.java` (+ `getPlunderRange`/`canBePillaged`), `BuildColonyMessage`, `Player.canClaimToFoundSettlementReason` (adjacent-colony rule, ✅ cross-checked); building `model.modifier.defence` (fortification bonus); `model.event.freeBuilding`/`csFreeBuilding` (La Salle) |
| **Related systems** | [units-movement](units-movement.md), [save-load](save-load.md), [ruleset-data](ruleset-data.md) |

## 1. How it works (plain English)

Select a colonist and press **B** to found a colony where it stands. The colonist settles down and becomes the colony's first inhabitant (it leaves the map). Colonies show as a small house with their name; click one to see its vitals in the status bar. Names come from a classic colonial list (Jamestown, Plymouth, …) in founding order.

**The colony screen.** Click your colony to open it. It fills the screen and is laid out like the original Colonization's (FreeCol's) colony view, drawn in **real FreeCol art**: a title and summary line (population, idle colonists, food toward the next colonist, defence bonus); a **production bar** along the top showing each good the colony makes or eats per turn (its icon + net amount); on the **left**, an **isometric view of the surrounding tiles** — the colony's own settlement at the centre with the eight neighbours fanned out as overlapping diamonds, a colonist standing on each tile being worked (with the good and yield it produces, and a *✕* to release it) and a *Work…* picker on a free tile — sitting above the **construction** panel (what's being built, with its progress and a *Stop*, or a menu to choose a building); on the **right**, the colony's **buildings** as a grid of their real images, each showing how many of its work slots are filled and a **+ / −** to staff or unstaff it; a row of the **units standing outside** the colony (join / arm / send-out); and a **warehouse bar** along the bottom showing every stored good as its icon and count.

**Putting colonists to work.** Each colonist either works one of the eight surrounding tiles (growing food, or a raw good — lumber, ore, cotton, furs) or staffs a building (turning raw goods into bells, hammers, cloth, …). New colonists report to the best food tile automatically; to change that, **Release** a worker to free it, then either **send idle colonists to the fields** (a food shortcut) or pick a specific tile and choose **what** it should produce — that's how you put someone on **lumber** (which the carpenter then turns into hammers for building) rather than food.

**Moving colonists in and out.** The colony screen's **Colonists** section moves people across the colony wall: **Send a colonist out** detaches a free colonist onto the colony's tile (click that tile to select it and march it off, or arm it), and any of your colonists standing on or beside the colony gets a **Join colony** button to fold into its population (the unit leaves the map, +1 population). A colony must always keep at least one colonist.

**Arming colonists.** A colonist standing **in** the colony can be equipped from the colony's own stores — **Arm soldier** (50 muskets), **Arm dragoon** (50 muskets + 50 horses), **Arm scout** (50 horses) or **Arm pioneer** (tools) — and **Disarm** back to a plain colonist (refunding the gear). The buttons appear only when the colony actually holds the goods. That's how you raise a defender at home: send a colonist out onto the colony tile, then arm it.

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
- `CheckAssignWork`/`AssignWork`/`UnassignWork` oracles; rejects: off-map, non-adjacent, tile taken, no idle colonist, terrain can't produce the goods. **`TileWorkOptions(tile)`** lists what a tile can produce when worked — its terrain's attended outputs that yield > 0 (lumber/furs/grain on forest, ore on hills, grain/cotton on plains, fish on ocean), each with its yield, sorted by yield — feeding the colony screen's per-tile work picker (a rules query, ADR-006).
- Founding and growth **auto-assign** to the best free grain tile (deterministic tie-break); the player can **rearrange via the colony screen** — release a worker, send idle colonists to food (the auto-assign shortcut), **or assign an idle colonist to a specific surrounding tile for a chosen good** (the per-tile picker — lumber/ore/cotton/furs/…, not just food). This unblocks the raw → refined chain (e.g. lumber → carpenter → hammers).
- Idle colonists produce nothing (building jobs are a later slice).

**Colonist membership (slice 9):**
- **Join** — a colonist (any person unit) on or next to a colony can `JoinColony`: population +1, the unit leaves the map, the newcomer is auto-assigned to a food tile. This is the payoff of immigration ([immigration](immigration.md)/[transport](transport.md)): ship a recruit home, disembark by a colony, and it grows the colony. **Wired into the colony screen** (a *Join colony* button per eligible adjacent/on-tile unit).
- **Leave** — `LeaveColony` detaches a colonist onto the colony's own tile as a **free colonist** (our colony stores a population *count*, not individual types, so the detached unit is generic), population −1; a colony must keep ≥ 1 colonist, and a fully-staffed colony vacates one job to fit. **Wired into the colony screen** (the *Send a colonist out* button); the freed unit is selectable on the map (a tile-click selects a unit on the colony tile before it opens the panel).

**Deviations from original / FreeCol:** ✅ **minimum colony spacing cross-check done (FP-5).** Founding is now blocked on a tile adjacent to an existing colony, matching FreeCol's `Player.canClaimToFoundSettlementReason` (`tile.getAdjacentColonies()` must be empty) and the original's no-touching-footprints rule. Native settlements do **not** block founding (FreeCol treats native proximity as a land claim/price, not a hard bar; we don't model land price yet).

## 3. Technical design

- `Colony`: id, name, position, population (mutable internal — grows in Phase 3), `OwnerId` (`{ get; internal set; }` — which player holds it; reassigned on **capture**, see [combat](combat.md)).
- `Game.CheckFoundColony(unit)` → `MoveCheck` oracle; `FoundColony(unit)` enforces and mutates (removes unit, adds colony). Same oracle/command pattern as movement (ADR-006).
- Save format **v3** adds `Colonies`; pre-v3 saves load with none (tested).
- Rendering: `ColonyMarker` (`_Draw` house + name via `ThemeDB.FallbackFont`), one per colony under `MapView/ColonyLayer`, reconciled per refresh.
- UI handles zero units (founding consumes the last one): marker hidden, status shows the latest colony.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | found-on-settleable consumes unit/creates colony; rejections (ship, mountains, occupied tile, **adjacent to an existing colony** — `FoundColony_Rejected_AdjacentToAnExistingColony`); **resource yields** (`ResourceYieldTests`: resource boosts, expert-scope skipped, no-enable guard, Hudson ×2 furs, resource+father stack order) | ✅ |
| L2 Scenario | Always | save/load round-trip preserves colonies; pre-v3 compat; production uses the boosted resource yield (`ResourceYieldTests.Production_UsesTheBoostedYield`); **join/leave** (`ColonyMembershipTests`: grow on join, detach a free colonist, keep ≥1, trim a job; round-trip) + `JourneyTests.Journey9` (ship a recruit home → join → colony grows) | ✅ |
| L3 Interaction | Yes | `InputTests` (B founds), `MainSceneTests` (panel opens/closes), `ColonyPanelTests` (the **surrounding tiles render as FreeCol terrain art + the buildings as a 4-wide image grid**, staff/unstaff buttons, release field worker, construction dropdown + stop, per-tile work picker assigns a colonist to a non-food good, Join button folds an adjacent colonist into the population, Send-a-colonist-out detaches a free colonist, Arm button equips a colonist as a soldier from the colony's muskets) | ✅ |
| L4 Visual | Yes (marker) | colony golden (`colony-seed424242`) | ✅ |

## 5. Open issues / TODO

- [x] Minimum colony distance rule (no founding adjacent to an existing colony) — cross-checked vs FreeCol, adopted (FP-5).
- [x] Colony **ownership + capture/pillage/plunder** (`Colony.OwnerId`; a colony changes hands or loses goods/gold to attack). Rules in [combat](combat.md).
- [x] Colony **fortification defence bonus** (stockade/fort/fortress → +100/+150/+200%; `Game.ColonyDefenceBonus`). See [combat](combat.md).
- [x] **La Salle** free stockade at population ≥ 3 (`Game.ApplyFreeBuildings`). See [founding-fathers](founding-fathers.md).
- [ ] Real colony screen (kanban [P2b] colony screen skeleton → Phase 3 economy UI).
- [ ] Nation-specific colony name lists when nations exist.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-16 | **FreeCol-look colony screen** (stages 2–5 of the visual rebuild): `ColonyPanel` re-laid-out after FreeCol's New Amsterdam — a net-**production** goods-icon bar, a left **isometric tile view** (enlarged 160×80 FreeCol diamonds; settlement at centre, a colonist + yield on each worked tile) over the **construction** panel, a right 4-wide **buildings grid** of real `ColonyArt.BuildingImage` cells with `+`/`−` staffing, an outside-the-colony units row, and a bottom **warehouse** goods-icon bar. Stage 1 was the terrain art; this completes the look with building images + goods icons (`ColonyArt.BuildingImage`/`GoodsIcon`; 42 building + 22 goods FreeCol sprites). Pure presentation (ADR-006) — every named control preserved, no logic/save/RNG change. The 3×3-grid L3 test → an art/structure test. All 37 scene + 552 logic green. **Deferred:** FreeCol's parchment UI *theme/skin* + the Rebels/Royalists SoL bar (no Sons-of-Liberty data in the model yet). | Phase 5 colony UI |
| 2026-06-16 | **Colony tile-work picker**: `Game.TileWorkOptions(tile)` (a tile's attended producible goods + yields, sorted) feeds a new per-tile picker in `ColonyPanel` — an idle colonist can now be assigned to a **specific surrounding tile for a chosen good** (lumber/ore/cotton/furs/…), not just the food auto-assign. Closes the deferred re-assignment UI; unblocks the raw → refined chain (lumber → hammers). Presentation + a pure oracle (ADR-006); no save/RNG change. +1 L1 `TileWorkerTests` + 1 L3 `ColonyPanelTests`. | Phase 5 colony UI |
| 2026-06-16 | **Join/leave colony in the UI**: `ColonyPanel` gains a *Colonists* section — a **Join colony** button per eligible on/adjacent human unit (`JoinColony`, out→in) and a **Send a colonist out** button (`LeaveColony`, in→out, detaches a free colonist onto the colony tile). Wires the already-shipped join/leave logic (no logic change); `GameController.RefreshView` now drops a stale `_selectedUnit` (a joined unit is removed). Presentation only (ADR-006); no save/RNG change. +2 L3 `ColonyPanelTests`. | Phase 5 colony UI |
| 2026-06-16 | **Arm colonists in the UI**: the *Colonists* section's per-unit row now also offers **Arm soldier/dragoon/scout/pioneer** + **Disarm** for a colonist standing in the colony (`CheckEquipRole`/`EquipRole`, gated on the colony holding the goods) — wires the already-shipped equip logic. Presentation only (ADR-006); no save/RNG change. +1 L3 `ColonyPanelTests`. | Phase 5 colony UI |
| 2026-06-16 | **Full colony screen**: `ColonyPanel` rebuilt from a text list into a laid-out screen — a summary line (population / food→growth / defence), a **3×3 surrounding-tiles grid** (colony at centre; each tile shows terrain + its worked good/yield with a *Release*, or a *Work…* picker on a free tile), then buildings / warehouse / colonists / construction, in a scrollable enlarged panel. Pure presentation (ADR-006) — every control preserved (all existing L3 tests green) and every action a Game oracle; no logic/save/RNG change. +1 L3 `ColonyPanelTests` (the 3×3 grid renders). | Phase 5 colony UI |
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
| 2026-06-15 | `Colony.OwnerId` added; a colony can change hands by **capture** (winning a land attack on an undefended colony reassigns ownership). Rules/save in [combat](combat.md) | Phase 5 (#6 colony capture) |
| 2026-06-16 | `BuildingType.DefenceBonus` added (parses `model.modifier.defence`): a colony's **stockade/fort/fortress** now grants its defender +100/+150/+200% defence (`Game.ColonyDefenceBonus`) against capture, pillage and field attack. Combat rules in [combat](combat.md) | Phase 5 colony defence bonus |
| 2026-06-16 | **La Salle** grants a free `model.building.stockade` to each colony at population ≥ 3 (`Game.ApplyFreeBuildings`, FreeCol `model.event.freeBuilding`). RNG-free; no save change (rides the building list). See [founding-fathers](founding-fathers.md) | Phase 5 (La Salle) |
