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

## 2026-06-14 — Stream-0 byte-stability regression guard [autonomous]

**Requested:** Overnight autonomous run — work through backlog tasks, full process each.
**Did:** Added the review-flagged human-stream-0 baseline guard (`ForeignPowerEconomyTests.HumanStream0_IsUnaffectedByHowMuchTheRivalsDo`): runs two same-seed games, one with the rivals' treasuries funded +1000/turn so they recruit/trade far more, and asserts the whole game diverged (rivals richer) **yet** the human's `RandomState` (stream 0) + dock + immigration + gold are byte-identical. Magic-number-free; decisively pins the ADR-009 contract that no foreign path touches stream 0.
**Status:** Pure test addition (no production change). **393 tests green** (371 L1+L2 + 2 soak + 20 scene). Committed; CI running.
**Changed:** `ForeignPowerEconomyTests.cs` (+1 test).
**Decisions:** Perturb-and-compare rather than pin literals (robust to legitimate future changes; only a real stream-0 leak fails it).
**Scheduled next:** **FP-6 — AI combat + diplomacy basics** (`86d3bex51`).
**Needs you:** Nothing.

## 2026-06-14 — Minimum colony-distance rule [autonomous]

**Requested:** Overnight autonomous run — work through backlog tasks, full process each.
**Did:** Resolved the long-standing `CheckFoundColony` TODO: a colony can no longer be founded on a tile **adjacent to an existing colony** (FreeCol `Player.canClaimToFoundSettlementReason` — `tile.getAdjacentColonies()` must be empty; the original's no-touching-footprints rule). Native settlements don't block founding (FreeCol treats that as a land claim, not a hard bar; land price unmodelled). Applies to the human and the AI.
**Status:** **392 tests green** (370 L1+L2 + 2 soak + 16 L3 + 4 L4); goldens unchanged. Byte-stable — foreign powers/human found far apart, so the soak/replay are unperturbed (verified). Committed with this entry; CI running.
**Changed:** `Game.cs` (`CheckFoundColony`), `GameTests.cs` (`FoundColony_Rejected_AdjacentToAnExistingColony`), `colonies.md`.
**Decisions:** Adjacent-colony block only (the in-scope FreeCol rule); native-adjacency deferred (needs the land-price model).
**Scheduled next:** **FP-6 — AI combat + diplomacy basics** (`86d3bex51`) — the headline next slice; scope locked by ADR-019 (stance/tension primitives).
**Needs you:** Nothing.

## 2026-06-14 — Role movement bonuses (dragoon/scout +9) [autonomous]

**Requested:** Overnight autonomous run — work through backlog tasks, full process each.
**Did:**
- Applied role movement bonuses (`86d3bbvv6`): new `Game.InitialMovement(unit)` = unit-type base + role `movementBonus` (mounted +9, missionary +3; resolved null-safely from `Ruleset.Roles` so minimal test rulesets just get the base). Used at the `EndTurn` movement reset and as the "near full movement" reference in `CheckMove`'s partial-move rule. Removed the now-unused `Unit.ResetMovement` (it can't see the ruleset).
- Faithful to FreeCol `getInitialMovesLeft` (bonus at turn reset, not on equip).
**Status:** **391 tests green** (369 L1+L2 + 2 soak + 16 L3 + 4 L4); goldens unchanged. Byte-stable — every existing seeded unit is default/soldier/pioneer role (bonus 0), so soak/replay/goldens are unaffected. Pushed `22a87c3`; CI running.
**Changed:** `Game.cs` (`InitialMovement` + reset + partial-move), `Unit.cs` (removed `ResetMovement`), `RoleMovementTests.cs` (new), `units-movement.md`.
**Decisions:** Apply only at turn reset (FreeCol equip doesn't refund moves). Nation-type (naval +3) / Magellan (+3) movement bonuses left deferred (scoped modifiers → scope evaluation / father effects).
**Scheduled next:** Minimum colony-distance rule (the `Game.cs` `CheckFoundColony` TODO) — then FP-6.
**Follow-ups:** review nit — `InitialMovement` does a per-call LINQ scan of ~12 roles (fine at the 2 ms budget; switch to a dict lookup if ever profiled).
**Needs you:** Nothing.

## 2026-06-14 — Module-docs refresh to FP-5 (no-drift) [autonomous]

**Requested:** Overnight autonomous run (see above).
**Did:** Brought the two stale module docs current (a real no-drift gap surfaced by the backlog audit): `docs/modules/game-logic.md` (was save v19 / 346 tests, no `Player`/owner-id/per-player-market API) and `docs/modules/presentation.md` (was through slice 5a) now reflect the FP-1→FP-5 wave (Player/owner-id/per-player markets/AI economy; save v20; 368 tests; FP-4 rival owner/fog-gating).
**Status:** Docs only, no code change. Pushed `5f5de12`; **CI ✓** (run `27496917625`).
**Changed:** `docs/modules/game-logic.md`, `docs/modules/presentation.md`.
**Decisions:** `map-goldens.md` needed no change (audit's stale-version flag was a false positive).
**Scheduled next:** Role movement bonuses (done next, above).
**Needs you:** Nothing.

## 2026-06-14 — FP-5: foreign-power AI economy (trade + immigration + recruit)

**Requested:** Continue the foreign-powers wave → FP-5 (`86d3bex4w`): give the foreign powers an economy, per-player and on their own RNG streams, so the human's stream 0 stays byte-stable.
**Did:**
- **Foreign economy runs:** `RunPlayerTurn` is unified — every colonial player now runs `RunColonyTurn` for its colonies + `AccumulateLibertyAndElectFathers`/`AccumulateImmigrationAndEmigrate` (FP-4 only ran the human's). A foreign power then runs the new **`RunForeignPowerEconomy`**: pursue a Founding Father (pick from its offers), **sell each colony's tradeable surplus — never food — to its OWN market** (`SellColonyGoods(power,…)`), and **recruit** while affordable up to a Europe cap (`AiMaxEuropeRecruits` = 2; no AI shipping yet), then the FP-4 unit AI.
- **Determinism (the crux):** the economy RNG helpers (`GenerateOffers`, `DrawRecruitType`, the emigrate draw, `RefreshDockForRecruitability` via dock draws, + the new father-pick/recruit) now draw from **`RandomFor(player)`** — the human keeps stream 0, foreign powers draw from `Player.Rng`. Founding-father production modifiers fold **per player** (`ApplyGoodsModifiers(player,…)`/`HasAbilityFor(player,…)`; public no-arg overloads delegate to the human, byte-identical). Foreign powers get their **own recruit dock** at `New` (drawn from their own streams) + topped up on load.
- **Owner-id fix:** `CreateEuropeRecruit`/`BuyUnit` (and via the review, `LeaveColony`) now stamp `OwnerId = player.PlayerId` — previously a foreign power's recruits would have wrongly belonged to the human (id 0). Auto-emigration is correctly owned too.
- **Tests:** new `ForeignPowerEconomyTests` (full economy on own stream/market; per-player market **independence**; per-player market **save round-trip**; recruit + owner-id; whole-economy byte-stability) + rewrote the FP-4 "no economy" test; extended the **soak** (no negative treasury, economy active + bounded, 200-turn active-economy round-trip byte-identical, 2 ms budget).
- **Process:** manual deep-read research → implement → 1 thorough adversarial-review agent (**no blockers**; all 5 hard invariants held). Applied its 4 low-sev items: 2 latent seam fixes (`LeaveColony` owner, `Visit` per-player RNG — both byte-identical for the human), ordinal goods sort, DRY `OwnPersonsInEurope`.
**Status:** **388 tests green** (366 L1+L2 incl. 10 E2E +5 new, 2 soak, 16 L3, 4 L4); builds 0/0; goldens unchanged; save **v20**. **Human stream 0 / market / goldens byte-stable**; soak round-trips 25 seeds × 200 turns of active rival economies byte-identically within the 2 ms budget. Pushed to `main` (`2472074`); **CI ✓** (run `27495670975`).
**Changed:** `Game.cs` (economy + per-player RNG/modifier folding + owner stamps + foreign dock), tests `ForeignPowerEconomyTests` (new), `MultiPlayerTests`, `SoakTests`; docs `players.md`/`market.md`/`turns.md`/`immigration.md`/`save-load.md`/`founding-fathers.md`/`QA-REPORT.md`.
**Decisions:** Minimal economy — the AI sells the colony **centre tile's** unattended cash-crop output (it doesn't staff cash-crop tiles or refine); food excluded explicitly (it **is** a tradeable market good in the classic spec, so an `IsTradeable`-only filter would starve colonies). Recruit (not goods-buy) is the Europe action — goods-buying needs a docked AI ship (FP-6). Save stays **v20** (additive; frozen at FP-7).
**Scheduled next:** **FP-6 — AI combat + diplomacy basics** (`86d3bex51`): stance/tension primitives (contact→peace, attack→war, tension→stance) replacing the stubbed `AreEnemies` hook; new `docs/systems/diplomacy.md`; foreign/naval combat lands here.
**Follow-ups (review-flagged, latent):** a human-only baseline regression test pinning stream 0 (architecturally enforced + replay-stable today); the FP-4 carry-overs still open (frontier cache for `StepTowardNearestUnexplored`; min-distance-between-colonies rule; synthesize native `Player` rows on pre-FP-3b load; persist `_currentPlayerIndex`; wire native units to their player id); AI goods-buying + shipping recruits home (FP-6).
**Needs you:** Nothing — no human-facing gameplay/UI change (rival economies are off-screen under your fog). Say the word for FP-6.

## 2026-06-14 — FP-4: minimal foreign-power AI (explore / move / found)

**Requested:** Continue the foreign-powers wave → FP-4 (`86d3bex4u`), the first active AI.
**Did:**
- The 3 foreign powers go **active**: at `Game.New` they **land on the map** far from the human (`LandForeignPower` — deterministic, no RNG) instead of docking in Europe; each non-human player gets its **own PCG stream** (`Player.Rng`, saved/restored; save v20 additive `RngState`/`RngIncrement`).
- **AI** (`RunForeignPowerTurn`): per unit by-id — a colonist founds where it stands while under `MaxAiColonies`, else steps toward the nearest tile it hasn't explored (`StepTowardNearestUnexplored`); ships idle. A flat priority switch, **not** a planner. All choices draw from the player's **own** stream (`RandomFor`) — never the human's stream 0.
- Fog generalised to the unit's owner (`RevealForOwner`): foreign powers explore under their own fog; the human can't see/manage a foreign colony until discovered (presentation **fog-gates** colony markers + only the human's own colonies are clickable/HUD/camera — closed 2 owner-leaks the review flagged).
- Process: 3-reader research workflow (test blast-radius + AI helpers/fog + per-player-RNG design) → implement → adversarial review (clean on determinism/save/AI-correctness; fixed its 2 presentation findings).
**Status:** **384 tests green** (363 logic incl. new AI + replay-stability tests, 20 scene/golden, 2 soak); CI ✓ (run `27494340531`); save **v20**; git clean on `main`. **Byte-stable + replay-stable**: human stream 0 / RNG-resume / L4 goldens unchanged; **soak round-trips 25 seeds × 200 turns of active rivals byte-identically within the 2 ms turn budget**; two same-seed games byte-identical after 20 AI turns.
**Changed:** `Game.cs` (landing + AI + per-player RNG + owner-reveal), `Player.cs` (`Rng`), `SaveGame.cs` (per-player RNG), `GameController.cs` (fog-gate + owner-gated click/HUD/focus); tests `MultiPlayerTests`/`OwnerTests`/`JourneyTests`; docs `players.md`/`turns.md`/`save-load.md`. Commit `0ad7cef`.
**Decisions:** Foreign powers **land on the map** (not Europe) — required for the explore/found AI; far landing + the colony-marker fog-gate keep the goldens stable (no regeneration). AI is **deterministic + per-player-streamed** (replay-stable, stream 0 untouched). Natives stay **inert** (their AI is a later slice). Save stays **v20** (additive through the wave, frozen at FP-7).
**Scheduled next:** **FP-5 — AI economy: trade + immigration + recruit** (`86d3bex4w`) — give the foreign powers an economy on their own streams/markets.
**Follow-ups (review-flagged, latent):** owner-gate the rest of the presentation when rivals become discoverable; `StepTowardNearestUnexplored` is O(map)/unit/turn (fine at the 2 ms budget, cache a frontier when the AI grows); min-distance-between-colonies rule (the `CheckFoundColony` TODO); synthesize native `Player` rows on a pre-FP-3b load; wire native units to their player id.
**Needs you:** Nothing — no human-facing gameplay/UI change yet (rivals are off-screen under your fog). Say the word for FP-5.

## 2026-06-14 — FP-2 + FP-3: owner-id seam → European nation data → inert multi-player

**Requested:** Do **FP-2 and FP-3** (the next foreign-powers slices), in order.
**Did:**
- **FP-2 — owner-id seam (`2a1e79c`):** added authoritative `Unit.OwnerId`/`Colony.OwnerId` (human = 0); converted the binary human-vs-native enemy test + fog filters + per-owner founding-father abilities to **owner-based** helpers (`AreEnemies` = the single stance hook, `IsHumanOwned`, `AbilityForUnit`); save v20 gained optional owner ids (additive). Behaviourally identical with human + natives. +5 `OwnerTests` (inject a simulated foreign unit → prove enemy/fog/ability isolation before real rivals).
- **FP-3a — European nation data (`9e5ac20`):** new `EuropeanNation`/`EuropeanNationType`/`EuropeanStartingUnit` + parser (the four classic powers + REFs, advantages, starting units, ref flag); embedded the 173 classic per-nation colony names (extracted from FreeCol's strings) + parsed them. Data only. +6 parse tests.
- **FP-3b — inert multi-player (`632327e`):** `Game.New` spawns 3 inert foreign powers (starting units in Europe) + native nations as `Player` rows; **ring-buffer `EndTurn`** (`CurrentPlayer`/`NextPlayerIndex`, only the human acts); reserved per-player RNG streams; save v20 persists multi-element `Players[]`; `FoundColony` uses per-nation names (human keeps the default). +4 `MultiPlayerTests`.
- **Process:** research workflow (FreeCol spec + our parser + a two-faction-breakage scan) → implement → **3-lens adversarial-review workflow** (determinism / save / owner-integration) — came back clean; closed the 2 latent seam gaps it flagged (`AdvanceSailing` + presentation tile-click now resolve the human by owner).
**Status:** **382 tests green** (362 logic + 20 scene/golden); CI ✓ on all three pushes (FP-3b run `27492840276`); save **v20**; git clean on `main`. Determinism/goldens/soak byte-stable (rivals draw no RNG, are inert).
**Changed:** `Game.cs`, `Player.cs`, `Unit.cs`, `Colony.cs`, `Ruleset.cs`, new `EuropeanNationType.cs`, `SaveGame.cs`, `GameController.cs`, `GameLogic.csproj` + embedded `european-nation-names.properties`; tests `OwnerTests`/`MultiPlayerTests`/`EuropeanNationTypeTests` (new) + edits; docs `players.md`/`save-load.md`/`ruleset-data.md`/`game-modes.md`. Commits `2a1e79c`, `9e5ac20`, `632327e`.
**Decisions:** Natives + foreign powers become real `Player` rows now (uniform list); foreign powers start **inert in Europe** (no map placement → no fog/golden impact, no RNG); the human stays nation-less (so its colony names/economy are byte-stable); save stays **v20** (additive through the wave, frozen at FP-7). The 4 classic powers = the first selectable non-REF European nations (the human is the nation-less 4th).
**Scheduled next:** **FP-4 — minimal AI turn: explore / move / found** (`86d3bex4u`): give the inert players behaviour + their own RNG streams; land the foreign powers on the map. (Then FP-5 economy, FP-6 combat/diplomacy, FP-7 save consolidation.)
**Follow-ups (review-flagged, latent until FP-4):** synthesize native `Player` rows when loading a pre-FP-3b save; persist `_currentPlayerIndex` once turns can be saved mid-ring; harden foreign-power selection with an explicit `OrderBy`; wire native units to their player id.
**Needs you:** Nothing — no gameplay/UI change yet (rivals are invisible/inert). Say the word for FP-4 (where rivals start moving and become visible).

## 2026-06-14 — FP-1: Extract Player (single human, zero behaviour change)

**Requested:** Start the foreign-powers wave with FP-1 (`86d3bex4a`) — pure refactor: move player-scoped state off `Game` onto a new `Player`, save v20, all tests stay green.
**Did:**
- New `sealed class Player` (`GameSession/Player.cs`) owns player-scoped state: identity (`PlayerId`/`NationId`/`IsHuman`/`PlayerType`), treasury+tax, its **own Market** (per-player), liberty/Congress/fathers, immigration/recruit-dock + `RecruitPrice`, and explored fog. `Game` holds `IReadOnlyList<Player> Players` + a cached `HumanPlayer`; the former fields are now **thin pass-through props** to the human, so presentation + tests barely change.
- Mutating seams (`Sell*`/`Buy*`/`Recruit`/`Visit`/`SellToNatives`/accumulate/`Reveal`) got internal `Player`-taking overloads; the public method delegates to `HumanPlayer`. Collapsed the 23-param `Game.Restore` to take a `RestoredPlayer` list (one element).
- **Save v20:** new `Players[]` array (`SavedPlayer` record). Load keyed on `Version >= 20` (else fold the legacy flat fields into one human player). A v20 save still writes the flat fields too (every pre-v20 load path stays exercised; dropped at FP-7). New test `V19Save_LoadsAsSingleHumanPlayer`; determinism/goldens/soak untouched (human = RNG stream 0).
- Docs: new `docs/systems/players.md`; `save-load.md` synced (v20 row + behaviour). Ran an adversarial review workflow over the diff.
**Status:** **367 tests green** (347 logic +1 new, 20 scene); solution builds 0/0; CI pending on push.
**Changed:** `Player.cs` (new), `Game.cs`, `SaveGame.cs`; tests (`CombatTests`/`SailingTests` father+gold injection → `Players[]`, version pins 19→20, new fold test); docs `players.md` (new) + `save-load.md`. Commit `e241056`.
**Decisions:** Keep flat save fields in v20 + key the load path on `Version` (not on `Players != null`) so the 12+ version-downgrade tests stay byte-identical (interim duplication, removed at FP-7). Founding-father modifier/ability resolution + live `CurrentlyVisible` stay human-scoped in FP-1 (need the owner-id seam — FP-2); only the **stored** fog (`Explored`) is per-player now.
**Scheduled next:** **FP-2 — owner-id seam + stance-ready enemy/fog** (next kanban slice; decompose/confirm id from the list). Do not start until FP-1 is merged green on CI.
**Follow-ups:** FP-3 (European nations as variant data + inert rivals) … FP-7 (save consolidation: drop flat fields, freeze format); deferred naval/foreign-unit combat (`86d3bek5r`).
**Needs you:** Nothing — pure refactor, no gameplay/UI change. FP-2 begins once CI is green.

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
