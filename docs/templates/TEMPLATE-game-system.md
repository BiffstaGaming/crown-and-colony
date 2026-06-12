# System: <Name>

| | |
|---|---|
| **Status** | Planned / In development / Implemented / Tuning |
| **Last verified** | <YYYY-MM-DD> @ <short-commit> |
| **Code** | `game/src/GameLogic/<Area>/` |
| **Tests** | `game/tests/<Area>/` |
| **FreeCol reference** | `freecol/<path>` (spec elements / classes consulted) |
| **Related systems** | [<other-system>.md](<other-system>.md), … |

## 1. How it works (plain English)

*Audience: anyone — no jargon, no class names.*

One-paragraph summary of what this system does and why it matters to the player.

**The rules, in plain words:**
- Rule one stated simply.
- Rule two stated simply.

**Worked example:**
> A free colonist working a grassland tile produces 3 food per turn. Put an expert farmer there instead and it doubles to 6. If the colony builds…

**What the player sees and does:** which screens, which clicks, what feedback.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

- Exact values, conditions, and ordering (tables preferred).
- Edge cases and exceptions, each with its expected outcome.

| Input / condition | Result |
|---|---|
| | |

**Deviations from original 1994 / FreeCol behavior:** *(none / list each with rationale — these lines are the most valuable in the doc)*

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** key classes and their single responsibility (e.g. `ColonyProduction` — computes per-turn goods output; pure, no Godot deps).

**Data sources:** which FreeCol XML elements feed this system (`specification.xml` → `<goods-type>`, …) and any parsing notes.

**Algorithms & formulas:** exact formulas with code locations (`GameLogic/Economy/ColonyProduction.cs` → `CalculateTileOutput()`).

**Integration points:** events raised/consumed, which systems call this one, ordering constraints within the turn.

**Persistence:** what state is saved/loaded for this system.

## 4. Verification

*How we know this works — the testing contract for this system.*

- **Unit tests:** `<file>` — what they pin down.
- **Scenario tests:** `<file>` — scripted situations + expected outcomes (e.g. "colony with farmer on grassland, 10 turns → +30 food").
- **FreeCol cross-check:** compared / not yet compared; results and any discrepancies.

## 5. Open issues / TODO

- [ ] …

## Changelog

| Date | Change | Commit |
|---|---|---|
| <YYYY-MM-DD> | Initial documentation | <hash> |
