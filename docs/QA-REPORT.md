# QA Report — Crown & Colony

> **Snapshot** taken 2026-06-15, latest on `main` (*native AI (1b); rival rendering + CheckMove colony guard (1c-1); foreign-power retaliation (1c-2); Pocahontas + Magellan founding-father effects; naval combat (1c-3a); ship damage + repair (1c-3b, save v21); naval cargo loot (1c-3c); native interaction UI (click a settlement → speak/learn/attack); privateer piracy (1c-3d-i); + **Francis Drake (1c-3d-ii)** — +50% offence/defence for privateers, the first scoped founding-father combat modifier; ADR-019*).
> This is a committed, point-in-time QA snapshot combining **test results** and the **visual goldens** (screenshots) in one place.
> **End-to-end journeys:** the connected player-journey coverage is specified in [TEST-PLAN.md](TEST-PLAN.md).
> Regenerate after a green run with `dotnet test` + a `GOLDEN_UPDATE=1` golden pass (see [TESTING.md](TESTING.md)); the goldens below always show the *committed expected* render.
> **Live, always-current results:** the [GitHub Actions CI runs](https://github.com/BiffstaGaming/crown-and-colony/actions) — every push is gated on these same suites.

## Test results (this snapshot)

| Layer | What it checks | Tooling | Count | Status | Where it runs |
|---|---|---|---:|:--:|---|
| **L1 Unit** | Rules, formulas, state transitions (engine-free) | xUnit | included in 481 | ✅ | every push |
| **L2 Scenario** | Scripted multi-turn games, FreeCol cross-checks | xUnit | included in 481 | ✅ | every push |
| **L1+L2 total** | (the engine-free `GameLogic` suite) | xUnit | **481** | ✅ | every push |
| ↳ of which **E2E journeys** | Connected player journeys, milestone-asserted ([TEST-PLAN.md](TEST-PLAN.md)) | xUnit `[Trait E2E]` | 10 | ✅ | every push |
| **L3 Interaction** | Real scenes driven by simulated input/signals (incl. 1 scene E2E + the Europe screen + click-to-attack + native-raid notice + unit rendering + owner colour + ship-under-repair + native-settlement panel) | GdUnit4 | 23 | ✅ | every push (CI) |
| **L4 Visual** | Golden-screenshot diff of the rendered map | GdUnit4 + custom diff | 5 | ✅ | every push (CI) |
| **L5 Soak** | 25-seed × 200-turn runs (active foreign economies + native AI) + provoked native-raid & foreign-war invariants + per-turn perf budget | xUnit | 4 | ✅ | nightly |
| | | | **513** | **all green** | |

Reproduce locally (toolchain in [CLAUDE.md](../CLAUDE.md)):
```
dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category!=Soak"   # L1+L2 (481)
dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category=Soak"    # L5 (4)
dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings                 # L3+L4 (27), needs GODOT_BIN
```

## Visual goldens (committed screenshots)

These are the reference images the L4 suite diffs every render against. A push that changes the rendered map fails CI unless the golden is deliberately regenerated.

### Seeded world — `map-seed424242`
What it verifies: isometric terrain art, fog of war, the colonist sprite, bonus-resource icons, and elevation/forest overlays render identically for the pinned seed.

![Seeded world golden](../game/tests/visual/goldens/map-seed424242.png)

### Founded colony — `colony-seed424242`
What it verifies: the same world after founding a colony — settlement art + name plate, and the founding unit consumed.

![Founded colony golden](../game/tests/visual/goldens/colony-seed424242.png)

### Native settlement — `native-settlement-seed424242`
What it verifies: a discovered native settlement renders — FreeCol indian-settlement art with the nation's name plate, drawn only once its tile is explored (fog-gated).

![Native settlement golden](../game/tests/visual/goldens/native-settlement-seed424242.png)

### Remembered fog — `remembered-fog-seed424242`
What it verifies: after the colonist moves on, the tiles it leaves render **dimmed** (explored but no longer in sight) while the tiles around the unit stay full-bright.

![Remembered fog golden](../game/tests/visual/goldens/remembered-fog-seed424242.png)

### Rendered units — `rendered-units-seed424242`
What it verifies: the slice 1c-1 unit rendering — two of the human's own units draw (the old HUD showed only the first), one selected (gold ring, no owner ring); an in-sight native brave draws with its owner ring (red-disc fallback until native unit art lands). Proves the reconciled unit layer + the visibility gate + owner distinction.

![Rendered units golden](../game/tests/visual/goldens/rendered-units-seed424242.png)

Definitions (scene, seed, tolerance, human-check list): [docs/visual-tests/map-goldens.md](visual-tests/map-goldens.md).

## Per-system coverage

Each system doc carries a five-layer verification table; this is the index:

| System | Doc | L1 | L2 | L3 | L4 |
|---|---|:--:|:--:|:--:|:--:|
| Randomness | [randomness.md](systems/randomness.md) | ✅ | ✅ | — | — |
| Ruleset data | [ruleset-data.md](systems/ruleset-data.md) | ✅ | ✅ | — | — |
| Map & terrain | [map-terrain.md](systems/map-terrain.md) | ✅ | ✅ | ⚠️ | ✅ |
| Fog of war | [fog-of-war.md](systems/fog-of-war.md) | ✅ | ✅ | ✅ | ✅ |
| Units & movement | [units-movement.md](systems/units-movement.md) | ✅ | ✅ | ✅ | ⬜ |
| Colonies & economy | [colonies.md](systems/colonies.md) | ✅ | ✅ | ✅ | ⬜ |
| Market & treasury | [market.md](systems/market.md) | ✅ | ✅ | ✅ | — |
| Founding Fathers | [founding-fathers.md](systems/founding-fathers.md) | ✅ | ✅ | — | — |
| Europe & sailing | [europe.md](systems/europe.md) | ✅ | ✅ | ✅ | — |
| Immigration & recruitment | [immigration.md](systems/immigration.md) | ✅ | ✅ | ✅ | — |
| Unit transport | [transport.md](systems/transport.md) | ✅ | ✅ | ✅ | — |
| Turns | [turns.md](systems/turns.md) | ✅ | ✅ | ✅ | ⬜ |
| Save/load | [save-load.md](systems/save-load.md) | ✅ | ✅ | ✅ | — |
| Natives & settlements | [natives.md](systems/natives.md) | ✅ | ✅ | ✅ | ✅ |
| Game modes / variants | [game-modes.md](systems/game-modes.md) | ✅ | ✅ | ✅ | — |
| Combat | [combat.md](systems/combat.md) | ✅ | ✅ | — | — |
| Players & nations | [players.md](systems/players.md) | ✅ | ✅ | ✅ | ✅ |
| Diplomacy (stance/tension) | [diplomacy.md](systems/diplomacy.md) | ✅ | ✅ | — | — |

## Honest coverage gaps

- **UI screens have no visual golden.** L4 captures hide the UI layer for cross-platform font stability, so the interactive colony screen and the Europe screen are covered only by L3 interaction tests, not screenshots. Tracked: [task 86d3b4653](https://app.clickup.com/t/86d3b4653).
- **Mostly start-state goldens.** The map/colony goldens capture turn 1; the native-settlement golden reveals one settlement but the map is otherwise unexplored. No fully-explored / late-game map golden yet.
- **This report is a manual snapshot.** Auto-generating it from CI (so results + goldens are always current) is a candidate improvement — see below.

---
*Want this always-current? A short CI step could regenerate this file's results table and attach the latest goldens on every run. Flag it and I'll add it.*
