# Next session — continue the 15-item P5 batch (LCR → treasure arc + more)

Copy the block below into a fresh session (or tell Claude Code: *"Read `docs/NEXT-SESSION.md` and carry it out."*). CLAUDE.md auto-loads. **Last updated 2026-06-17.**

---

**Your task:** keep shipping the active 15-item batch in ClickUp List `901615382059`, one faithful GameLogic-first slice at a time (the established rhythm: each slice lands **code + tests + docs in the same commit**, CI-green on push, ClickUp moved to In Review/Shipped). **Ultracode is on** — for any substantive slice, run an *understand* workflow first and an *adversarial review* workflow on the result before committing; solo only the trivial mechanical bits.

## Start here (read first, in order)
1. `PostWorkSummary.md` (top entries) — the last few sessions at a glance.
2. ClickUp **Session Log** (doc `2kz0t3mf-816`, newest page) — the durable cross-session record.
3. `docs/systems/lost-city-rumours.md` **§5** — the full **LCR → treasure arc plan** the understand workflow produced (slice-by-slice sequence). This is your roadmap for ~half the batch.
4. The kanban: `clickup_filter_tasks` on the list, statuses `In Development` + `Ready for Development` = the active batch.

## Immediate next item — `86d3c9uhj` (already In Development)
**LCR outcome resolution** — the move-onto-tile *explore* trigger + the weighted reward table. Plan (from the workflow critic, in lost-city-rumours.md §5):
- Hook **`MoveUnit`** AND **`Disembark`** (an amphibious landing must not skip a rumour) — fire only for a **land unit** owned by a **colonial** player, on a tile where `Map.HasRumour(p)`.
- Draw from **`RandomFor(owner)`** (the human uses stream 0, AI use their own streams — combat is the precedent: every `Attack*` takes an explicit `IGameRandom`). **Never `_random` directly** or an AI exploring desyncs the human's economy.
- Resolver = the good/bad/neutral weighted split (FreeCol `LostCityRumour.chooseType`) with hardcoded **classic-medium** consts (badRumour 17 / goodRumour 62; `dx = 10 − rumourDifficulty(medium)` ≈ 8) — Ruleset parses no difficulty options yet.
- Ship the **cheap outcomes first**: NOTHING, EXPEDITION_VANISHES (consume the unit), TRIBAL_CHIEF (gold), LEARN (`GetUnitChange('model.unitChange.lostCity', type)` + `UpgradeUnitType` — data already parses), COLONIST (spawn a found colonist), BURIAL_GROUND (degrade to NOTHING off native-owned tiles; native war is a later refinement). Call `Map.RemoveRumour(target)` on resolution. **Do NOT** ship treasure outcomes (CIBOLA/RUINS) here — they need the `SavedUnit` treasure-amount field (save v26), which is the **treasure-train slice** (`86d3c9ryj`).
- Scout / Hernando de Soto good-outcome bias is a refinement that can fold in after the core table.

## The batch, in suggested ship order
- ✅ `86d3c9uex` — LCR placement (DONE, shipped, save v25).
- **`86d3c9uhj`** — LCR outcomes (← here).
- `86d3c9ujx` — Fountain of Youth (immigration burst; reuse `DrawRecruitType`/`CreateEuropeRecruit`, no new save field).
- `86d3c9umy` — Strange-mounds prompt (+ the gen-time MOUNDS pre-set for native-owned tiles).
- `86d3c9ryj` → `86d3c9rzu` → `86d3c9t1e` — treasure trains: the unit + `UnitType.CarryTreasure` + a `SavedUnit` treasure-amount field (**save v26**, additive-omit), then cash-in (King's transport cut + tax vs sail-it-home), then spawn-on-sack (replace the instant-gold plunder in `AttackSettlement`).
- `86d3c9ru3` + `86d3c9rx2` — **custom house** (Chris's decision: build BOTH modes — original auto-export-over-50 AND a per-good toggle — selectable via a setting; boycotts wait on the war system).
- `86d3c9tkk` — colony fort/fortress bombards adjacent enemy ships (combat, per-owner RNG, reuses `DamageShip`).
- `86d3c9tha` — land purchase (native tile ownership + pay-or-steal; Peter Minuit makes it free).
- `86d3c9tzv` — amphibious-capture exclusion + DefenderAt power ranking (check: capture-on-amphibious may already be excluded).
- `86d3b6nrz` — **per-colonist work identity** (the big foundational refactor: colonies store a population *count* today; model individual colonists). Use understand+design workflows. **Unlocks** the last two:
- `86d3c9pgj` — on-the-job upgrades (colonist → expert) [needs identity].
- `86d3c9q1z` — indentured-servant / petty-criminal promotion ladder [needs identity].

## Non-negotiable patterns (proven this batch)
- **Determinism (ADR-009):** any new RNG draws go through the seeded stream. Gen-time placement uses a **dedicated stream id** off the human's stream 0 (LCR uses `LcrStreamId = 100`, reserved above every per-player `PlayerId+1` stream — this is how rumour placement stayed soak-byte-stable). Per-player gameplay RNG goes through `RandomFor(owner)`. **Run the soak** (`--filter "Category=Soak"`) on anything touching the turn loop / map gen / RNG — byte-stability is the gate.
- **Saves:** additive, **omitted-when-empty** so a feature-free game stays byte-identical to the prior version; bump the version + update the SaveGame doc-comment + `save-load.md` changelog **in the same commit**. (Now at **v25**; treasure-amount will be v26.)
- **No-drift docs:** every behaviour/formula/API change updates the matching `docs/systems/<system>.md` (both layers + changelog + Last-verified) in the same commit. Prepend a `PostWorkSummary.md` entry and **paste it into the chat reply**.

## Toolchain reminder
- `export DOTNET_ROOT="C:\Users\Chris\.dotnet" && export PATH="C:\Users\Chris\.dotnet:$PATH"` before `dotnet`.
- L1+L2: `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category!=Soak"` · soak: `--filter "Category=Soak"` · build: `dotnet build game/CrownAndColony.slnx`.
- Commits end with the `Co-Authored-By: Claude Opus 4.8 (1M context)` trailer; push to `main` is the workflow.

## Standing decisions / open items (don't re-litigate)
- **Monarch = P6** (re-tagged). **Founding-father factor = 40** (shipped). **Custom house = both modes + setting**.
- Deferred (documented): ship construction + ships-at-colony location; faithful `isCoastland`; REF `ambushPenalty` (P6); seasoned-scout LCR traits (fold into `86d3c9uhj`); rumour **map markers** (presentation).
- The remaining easy P5 leaf slices are mostly exhausted — after this batch, the high-leverage move is the **per-colonist identity** foundation (already in the batch) which unblocks a cluster of economy slices.
