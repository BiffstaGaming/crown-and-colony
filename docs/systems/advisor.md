# System: Active-unit advisor

| | |
|---|---|
| **Status** | Implemented (bounded / curated — see scope note) |
| **Last verified** | 2026-06-27 @ `Game.Advisor` (`86d3fq1re`) |
| **Code** | `game/src/GameLogic/GameSession/Game.Advisor.cs` (oracle); `game/presentation/AdvisorPanel.cs` (HUD card) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/AdvisorTests.cs` (L1); `game/presentation/tests/AdvisorPanelTests.cs` (L3) |
| **FreeCol reference** | FreeCol surfaces equivalent advice as `ModelMessage`s / report panels + the `InfoPanel`/`GUI` move hints (no advisor *characters*). Each recommendation reuses the same legality oracle FreeCol's action uses (`Unit.canBuildColony`/`getNoBuildReason`, `TileImprovementType` applicability, `learnFromIndianSettlement`'s `NATIVES` unit-change gate). |
| **Related systems** | [units-movement.md](units-movement.md), [colonies.md](colonies.md), [natives.md](natives.md), [hud-input.md](hud-input.md) |

## 1. How it works (plain English)

When you have a unit selected, the **advisor** is a small card in the corner that points out the obvious useful things you could do with that unit *right now* — the kind of nudge the old Colonization advisor characters used to volunteer. It only ever suggests things that are actually allowed, so you never get told to do something the game would then refuse.

The original game had a cast of advisors (a governor, a military advisor, and so on) who would pop up with suggestions. We don't model the characters; we model the useful part — a short, plain list of "here's what you could do".

**The rules, in plain words:**
- The advisor only talks about **your own** units, and only while the unit is **on the map** under its own command (not sitting in a ship's hold, not sailing, not docked in Europe).
- It offers up to **four** suggestions, and only the ones that genuinely apply:
  - **"You could found a colony here."** — when the unit is a colonist standing on a tile where a colony is allowed.
  - **"Build a road / plow this tile / clear the forest here."** — when the unit is a pioneer carrying tools, on terrain that improvement suits. Only the first applicable one is shown (so a pioneer on grassland sees "build a road", not three near-identical lines).
  - **"Learn &lt;skill&gt; at this native settlement."** — when the unit is standing next to (or on) a native settlement that teaches a skill this unit is able to learn.
  - **"No orders — fortify, sentry, or move this unit."** — the catch-all, shown when the unit is just sitting there with moves left and no standing order, in case you forgot to give it something to do.
- If there is nothing worth saying, the card stays hidden.
- You can **dismiss** the card; it is purely a hint and changes nothing in the game.

**Worked example:**
> You move a free colonist onto a patch of grassland next to your first settlement site. The advisor card shows:
> "• You could found a colony here." and "• No orders — fortify, sentry, or move this unit."
> You instead move a tooled pioneer onto a forest tile. Now the card shows "• Clear the forest here for lumber." Move that same pioneer right beside a Sioux village that teaches the expert-farmer skill, and it adds "• Learn expert farmer at this native settlement."

**What the player sees and does:** a compact parchment card (title "Advisor", a bulleted line per suggestion, a "Dismiss" button) that appears near the HUD when a unit is selected and there is advice to give. Dismissing hides it; selecting another unit refreshes it.

> **Scope note (read this):** this is a **deliberately small, curated** advisor — a fixed set of four cheaply-derivable suggestions, not an open-ended advisory engine. A fuller advisor (economy goals, military posture, per-colony "you need more food/tools", proactive build recommendations) is a **follow-up**, not part of this feature. Each suggestion that needs a rule reuses an oracle that already exists; we did **not** add new game rules for the advisor.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

`Game.AdviseUnit(Unit)` returns an ordered `IReadOnlyList<AdvisorRecommendation>` (a `Kind` tag + the display `Text`). It is a **pure read** — no game/unit/save mutation, no RNG.

**Gate (returns empty immediately if either fails):** the unit is owned by the human (`IsHumanOwned`) **and** is on the map (`Unit.IsOnMap`, which is false for a passenger aboard a ship, a unit sailing, or one in Europe).

Each recommendation is then appended only if its condition holds, in this fixed display order:

| # | Kind | Condition (the oracle consulted) | Text |
|---|---|---|---|
| 1 | `FoundColony` | `CheckFoundColony(unit).Allowed` — colonist type, settleable terrain, no colony on/adjacent (spacing) | "You could found a colony here." |
| 2 | `Improve` | first of road→plow→clear-forest with `CheckBuildImprovement(unit, id).Allowed` — tooled pioneer role, improvement applies to terrain, not already present, has moves | "Build a road here." / "Plow this tile to improve its yield." / "Clear the forest here for lumber." |
| 3 | `LearnSkill` | an adjacent (or co-located) native settlement with `CheckLearnSkill(unit, s).Allowed` — has an unconsumed learnable skill, unit type can learn it, settlement not Angry | "Learn &lt;skill&gt; at this native settlement." (skill = humanised `LearnableSkill` short name) |
| 4 | `Idle` | `Orders == Active && !IsImproving && !IsGoingTo && MovementLeft > 0` | "No orders — fortify, sentry, or move this unit." |

**Notes / edge cases:**
- **Only one improvement line** is ever produced (the `foreach` `break`s on the first applicable id), so a pioneer never gets multiple near-duplicate build lines.
- The **idle** hint is offered *in addition to* the action hints when it applies — a unit that can found a colony *and* is idle shows both (found-colony first, idle last). It is the last line because it is the generic fallback.
- A **ship on water** can produce at most the idle hint (it cannot found/improve/learn); a unit **with no moves left** or a **standing order/goto** produces no idle hint.
- The **skill display name** is presentation-friendly humanisation only (last id segment, camelCase split: `model.unit.expertOreMiner` → "expert ore miner"); it carries no rule meaning.
- The advisor **never suggests an illegal move**: every action recommendation is gated by the exact `MoveCheck` the corresponding action method (`FoundColony`/`BuildImprovement`/`LearnSkill`) throws on, so "the advisor said I could" always matches "the game let me".

**Deviations from original 1994 / FreeCol behavior:**
- **No advisor characters / portraits.** Col1 personified advice (governor, military advisor, etc.); we surface the same useful content as a plain text card. *(Cosmetic/UX choice — the rules-relevant content is preserved.)*
- **Bounded set, not an engine.** Both Col1 and FreeCol have broader advisory surfaces (FreeCol's report panels + per-message hints; Col1's multiple advisors). We ship a curated four-recommendation set derived only from existing oracles; a fuller advisor is explicitly a follow-up. *(Scope decision — recorded so the gap is visible.)*
- **No per-good "needs" advice.** The parity line is titled "goods/needs advisor"; we do **not** yet compute "this unit/colony needs X". The selected-unit nudges above are what shipped. *(Follow-up.)*

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:**
- `AdvisorRecommendation` (`readonly record struct`, `AdvisorRecommendationKind Kind`, `string Text`) — one suggestion. The `Kind` enum (`Idle`/`FoundColony`/`Improve`/`LearnSkill`) is the stable, wording-independent tag the UI orders/icons by and the tests assert on.
- `Game.AdviseUnit(Unit)` — the oracle. Lives in the partial `Game.Advisor.cs`; reuses `CheckFoundColony`, `CheckBuildImprovement`, `CheckLearnSkill`, `IsHumanOwned`, `NativeSettlements`, and `Ruleset.Unit(...).ShortName`. No new rule logic, no fields, no state.
- `AdvisorPanel : PanelContainer` (presentation) — a **code-built** card (built in `_Ready`, no `.tscn`), with a parchment skin (`ColonyArt.ParchmentSkin()`) and the in-game theme. `Show(IReadOnlyList<AdvisorRecommendation>)` renders one `Advice_{Kind}` label per item (a "• …" line) and reveals the card (an empty list keeps it hidden); the **Close** button (and `Dismiss()`) hides it and raises the `Dismissed` signal.

**Data sources:** none of its own. Indirectly: the ruleset improvement ids (`TileImprovementType.RoadId`/`PlowId`/`ClearForestId`), unit-type abilities (`model.ability.foundColony`, the pioneer role's `improveTerrain`), the natives unit-change gate (`Ruleset.CanLearnSkillFromNatives`), and live `Game` state (units, tiles, native settlements).

**Algorithms & formulas:** no formulas — it is a short sequence of boolean gates (see the table in §2). Ordering is fixed by the append order in `AdviseUnit`.

**Integration points:**
- The advisor is **read-only** and stand-alone; it raises no events and mutates nothing.
- **The one cross-stream seam (NOT wired in this stream — the integrator adds it):** the selected/active unit lives in `GameController`. On a selection change, the controller should call `_advisorPanel.Show(_game.AdviseUnit(selectedUnit))` and call `_advisorPanel.Hide()` when nothing is selected (or let the empty-list path hide it). Wiring `AdvisorPanel.Dismissed` is optional — handle it only if the controller wants to suppress re-showing the card until the next selection change. The L3 tests drive the panel **directly** (handing it a recommendation list), so the feature is fully verified without this seam.

**Persistence:** none. The advisor holds no game state; the card's dismissed/visible state is transient UI (not saved). **No save-version bump.**

## 4. Verification

*How we know this works — the testing contract for this system (see `docs/TESTING.md` for layer definitions).*

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `AdvisorTests` (13): found-colony on a foundable tile / suppressed next to a colony; improve advice for a tooled pioneer / single line when several apply / none without tools; idle hint for an active-with-moves unit / suppressed when fortified or out of moves; learn-skill next to a teaching settlement / suppressed for an expert; empty for a unit in Europe; a ship gives no land advice; **read-only** (no unit/game mutation) | ✅ |
| L2 Scenario | Always | Covered by the L1 constructed-state cases (the advisor is a pure read over state, with no turn evolution to script); the read-only guarantee is asserted in L1 | ✅ |
| L3 Interaction | If the system has UI | `AdvisorPanelTests` (4): a handed-in list renders one row per recommendation + reveals; an empty list stays hidden; Close hides + raises `Dismissed`; re-show replaces (not appends) the rows. **Render-verified** (temp `SavePng` → inspected → deleted; not committed) | ✅ |
| L4 Visual | If the system has a screen | None committed — the card is hidden by default (no golden churn) and is a small text card whose content is asserted as state in L3; render-verified manually | — |
| L5 Soak | Covered by global suite | — (read-only, no game evolution) | — |

- **FreeCol cross-check:** not a numeric cross-check — the advisor adds no new rule. Each recommendation reuses the same legality oracle the action itself uses (`CheckFoundColony`/`CheckBuildImprovement`/`CheckLearnSkill`), which are individually FreeCol-cross-checked in their own systems ([colonies.md](colonies.md), [units-movement.md](units-movement.md), [natives.md](natives.md)). So the advisor cannot diverge from FreeCol's "is this allowed?" answer.

## 5. Open issues / TODO

- [ ] Fuller advisor (follow-up): economy goals, military posture, per-colony "needs" (more food / tools / a specific building), and proactive build recommendations — surfaced like FreeCol's report hints. Would likely need a thin advisor-goals oracle, not just legality gates.
- [ ] Per-good "needs" advice (the parity line's "goods" half) — e.g. "this colonist would be better assigned to ore" — once a tile-yield/assignment oracle is available to the advisor.
- [ ] Wire the `GameController` selection seam (owned by the integrator) so the card actually appears in play; optionally suppress re-showing after `Dismissed` until the next selection change.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-27 | Initial documentation + implementation: read-only `Game.AdviseUnit` oracle (bounded 4-recommendation set — found-colony / improve / learn-skill / idle) + the code-built dismissible `AdvisorPanel` HUD card. `86d3fq1re` (Parity Wave 9 Stream C). +13 L1, +4 L3 (render-verified). No save bump. | _(this commit)_ |
