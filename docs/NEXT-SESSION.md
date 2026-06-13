# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 4 slice 7.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–3 are complete; **Phase 4 (Europe & trade) is in progress** — slices 1 (market+treasury), 2 (Founding Fathers), 3 (Europe + high-seas sailing), 4 (immigration & recruitment), 5 (unit transport), 6 (the Europe screen UI), **7 (Founding-Father effects — a modifier + ability system)** are shipped. The full loop is playable, and elected fathers now grant the bonuses that touch existing systems (Jefferson +50% bells, Penn +50% crosses, Paine +tax% bells, Brewster's recruit ban). **206 tests** (189 logic incl. 9 end-to-end journeys + 2 nightly soak + 13 L3 scene + 2 L4 visual), CI green, save format v13, git clean.

**Do this next (in order, unless I redirect):**
1. **Bonus-resource yield modifiers + expert units** (kanban `[P3] 86d3b465k`): the modifier infrastructure (`FatherModifier`/`ApplyGoodsModifiers`) now exists; this slice applies *per-source* production modifiers (expert-farmer +grain, bonus-resource tiles, and the deferred **Henry Hudson +100% furs** / **Magellan** movement father effects). It needs the production path to compute per-source totals and to evaluate modifier **scopes** (person/unit-type) — the one piece slice 7 deferred.
2. Then candidates: **buying units/ships in Europe**; **colonist in/out of a colony**; map-side board/disembark UI; or start an `[EPIC P5]` decomposition (natives/foreign powers/combat) which unblocks most remaining father effects. Pick per the kanban.

**Awaiting Chris's playtest (don't mark done):** four **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then begin the bonus-resource yield modifiers (reusing the slice-7 modifier system).
