# Post-Work Summary

A running, at-a-glance log of what Claude completed after each prompt / area of work — Chris's quick read of "what just happened" and **what's scheduled next.** **Newest entry first.** The durable, full cross-session record lives in the ClickUp **Session Log** (doc 06); this file is the lightweight TL;DR.

> **Entry format** (copy this for each new entry; prepend it directly under this block):
>
> ```
> ## YYYY-MM-DD — <short title>
>
> **Requested:** <one line — what was asked>
> **Did:** <2–5 bullets — what was actually done>
> **Status:** <build / tests / CI — e.g. "352 tests green, CI ✓, pushed (abc1234)">
> **Changed:** <key files or areas; commit hash(es)>
> **Decisions:** <choices made + why; or "—">
> **Scheduled next:** <the single specific work item to start next — name it + its kanban task id>
> **Follow-ups:** <other queued / recommended items; or "—">
> **Needs you:** <questions / playtest / approvals; or "Nothing">
> ```

---

## 2026-06-14 — Foreign-powers wave: plan + decompose (ADR-019)

**Requested:** Begin the next item (foreign European powers) — then Chris asked me to **plan the entire phase and provide a new-session kickoff prompt** (not start coding now), and chose a **per-player market**.
**Did:**
- Ran a planning workflow (3 readers over our `Game`, FreeCol's `Player`/turn loop, save/presentation seams) → a full architecture + decomposition; surfaced the per-player-vs-shared market fork to Chris (he chose per-player).
- Recorded **ADR-019** (Player/Nation model) in the ClickUp Architecture doc; refreshed the **Roadmap** (5b/5c ✅, foreign powers planned, 366 tests, v19).
- Decomposed the epic (`86d3b7qwm`) into **7 ordered, test-green kanban slices** FP-1…FP-7 (FP-1 → Ready for Development; rest Backlog); created the deferred naval/foreign-combat task (`86d3bek5r`).
- Rewrote `docs/NEXT-SESSION.md` as the **foreign-powers kickoff prompt** (the deliverable), and added a Session Log entry.
**Status:** **Planning only — no code changed.** Repo still 366 tests green, save v19, git clean. Docs/ClickUp updated; committing the repo handoff (`NEXT-SESSION.md`, `PostWorkSummary.md`).
**Changed:** `docs/NEXT-SESSION.md`, `PostWorkSummary.md`; ClickUp ADR-019 + Roadmap + Session Log + 8 kanban tasks.
**Decisions:** Per-player market (Chris); `Player` owns player-scoped state, `Game` keeps the global world + a players list + ring-buffer turns; units/colonies global, referenced by owner id; human via `HumanPlayer` not index; one RNG stream per player (human = stream 0); European nations = variant data; minimal AI (priority switch, not missions); REF/full-AI/rich-diplomacy/naval deferred.
**Scheduled next:** **FP-1 — Extract `Player`** (`86d3bex4a`): the pure-refactor foundation (single human, zero behaviour change, all tests stay green, save v20). Do it first and alone.
**Follow-ups:** FP-2…FP-7 (in order, only after FP-1 merges green); deferred Founding-Father effects (`86d3b7qxr`); naval/foreign-unit combat (`86d3bek5r`).
**Needs you:** Nothing to decide — the plan is sign-off'd (per-player market confirmed). Next session: paste `docs/NEXT-SESSION.md` and build FP-1.

## 2026-06-14 — Combat 5c: native settlement assault (Phase 5 slice 5c)

**Requested:** Move onto the next item of work (Combat 5c).
**Did:**
- Native **settlement assault**: `CheckAttackSettlement`/`AttackSettlement` — a settlement defends with an implicit garrison (brave defence × its settlement defence bonus); a win sacks it (destroy + plunder gold), a loss disarms/demotes the attacker. Can't walk onto a native settlement.
- `<plunder>` ranges parsed (`SettlementPlunder`); `ComputePlunder` ≈ FreeCol `RandomRange`; **Hernán Cortés** (`plunderNatives`) → the richer "extra" range. Save **v19** (marker only — a destroyed settlement is just absent; plunder folds into gold).
- Process: research workflow (4 readers, numbers verified vs `freecol/`) → implement → adversarial review workflow → fixed both confirmed findings.
- **Review fix + a deeper unification:** reworked combat tension to FreeCol's `defenderTension` across **both** 5b (open-field) and 5c (settlement): a win raises the nation's alarm nation-wide (open kill +500; non-capital sack +900; burning a capital → the nation **surrenders**, settlements set to 350); a **repelled** attack *lowers* it (−100, or −300 if your unit is slain). This corrects 5b's flat +200/+400 (wrong-signed on a loss) and a false "destroy adds none" doc claim.
**Status:** **366 tests green** (346 logic incl. 2 soak + 20 scene); CI running; pushed to `main` (`d8d6631`).
**Changed:** `Game.cs`, `NativeNationType.cs` (+`SettlementPlunder`), `Ruleset.cs`, `NativeSettlement.cs`, `SaveGame.cs`; tests `CombatTests.cs` (+ version pins); 7 docs synced (combat, natives, save-load, ruleset-data, founding-fathers, game-logic, QA-REPORT).
**Decisions:** Scoped 5c to **settlement assault only** — naval + foreign-European + native-initiated (AI) combat have no targets natives-only, so they move to the foreign-powers slice; **implicit-garrison** defender (vs braves-adjacent), justified by avoiding an in-settlement unit-list + save-schema change.
**Scheduled next:** **Foreign European powers + multi-player refactor + basic AI** (`86d3b7qwm`) — the largest Phase 5 chunk; decompose when reached. (It also unlocks naval/foreign-unit combat + Drake, tracked at `86d3bek5r`.)
**Follow-ups:** Naval + foreign-unit combat (+ Drake); native-initiated raids (AI); apply role movement bonuses (`86d3bbvv6`); deferred Founding-Father effects (`86d3b7qxr`).
**Needs you:** Still no combat UI (logic + tests only). Phase 5's remaining big piece (foreign powers + AI) is large — say if you'd rather I decompose it into a plan first, or push on.

## 2026-06-14 — Add PostWorkSummary + working rule

**Requested:** Update CLAUDE.md and create a `PostWorkSummary.md` template to summarize completed work after every prompt / area of work — including the item scheduled next.
**Did:**
- Created this file (repo root) with a per-entry format and a newest-first log.
- Added a working rule to `CLAUDE.md` ("How Claude should work") requiring a prepended entry here after each prompt / area of work.
- Added an explicit **Scheduled next** field to the entry format (the specific next work item + its kanban task id), separate from broader follow-ups.
- Back-filled the Combat 5b entry below so the log starts in use.
**Status:** Docs only — no code/tests affected.
**Changed:** `CLAUDE.md`, `PostWorkSummary.md` (new).
**Decisions:** Put the file at repo root (next to CLAUDE.md) for visibility; newest-first rolling log; it complements, not replaces, the ClickUp Session Log.
**Scheduled next:** **Combat 5c** (`86d3bba2z`) — settlement assault/plunder, naval, foreign-unit combat, native-initiated attacks (AI), nation-level tension.
**Follow-ups:** Apply role movement bonuses (`86d3bbvv6`); queued niceties (native-interaction UI `86d3bb1wh`, native trade buy + inland/wagon, transposability-tuning migration `86d3bb1x3`).
**Needs you:** Nothing — flag if you'd prefer a different location, format, or single-overwrite (vs. rolling log).

## 2026-06-14 — Combat 5b: attack action + roles/equipment + braves (Phase 5 slice 5b)

**Requested:** Continue Phase 5; implement Combat 5b (the attack action). You chose to **include roles/equipment** in the slice.
**Did:**
- Unit ownership (`OwnerNationId`; `PlayerUnits`/`NativeUnits`) + roles/equipment (`RoleType`, `UnitChange`, `EquipRole`); combat power folds the role at the correct index (veteran +50% applies to base **and** role).
- Brave defenders (one per settlement, fog-excluded, no RNG perturbation); `CheckAttack`/`Attack` with FreeCol's loser/winner precedence (slaughter / disarm + equipment-capture / capture-unit / demote / promote) + native alarm (+200 attack, +400 kill).
- George Washington (auto-promote) + Paul Revere (auto-arm a colony defender); save format **v18** (default role omitted → byte-identical to v17 per unit).
- Process: research workflow (6 readers, numbers verified vs `freecol/`) → implement → adversarial review workflow → fixed all 5 confirmed findings.
**Status:** **352 tests green** (332 logic incl. 2 soak + 20 scene); **CI ✓** (run `27480663094`); pushed to `main` (`6425a9c`); handoff refresh (`0f1a42e`).
**Changed:** `Game.cs`, `Unit.cs`, `UnitType.cs`, `Ruleset.cs`, `NativeSettlement.cs`, `SaveGame.cs`, new `RoleType.cs`/`UnitChange.cs`, `GameController.cs`; tests `CombatTests.cs`/`RoleTests.cs` (+ others); 9 docs synced (combat, natives, units-movement, save-load, ruleset-data, fog-of-war, founding-fathers, modules, QA-REPORT).
**Decisions:** Included roles (your call); braves placed *adjacent to* (not on) settlements so attacking is clean open-field combat and settlement assault defers cleanly to 5c; combat uses the main saved RNG (internal RNG-injecting overload for tests).
**Scheduled next:** **Combat 5c** (`86d3bba2z`) — settlement assault/plunder, naval, foreign-unit combat, native-initiated attacks (AI), nation-level tension (also exercises capture-unit + Revere end-to-end).
**Follow-ups:** Apply role movement bonuses (`86d3bbvv6`).
**Needs you:** No combat UI yet (logic + tests only) — say if you'd rather make natives/combat playable with a UI before 5c. Existing In Review playtest items still await your look.
