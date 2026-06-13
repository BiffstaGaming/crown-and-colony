# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 4 slice 3.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–3 are complete; **Phase 4 (Europe & trade) is in progress** — slices 1 (market+treasury), 2 (Founding Fathers), 3 (Europe + high-seas sailing) are shipped. The full produce→load→sail→sell→return trade loop works in code. **166 tests** (151 logic incl. 6 end-to-end journeys + 2 nightly soak + 13 scene/visual), CI green, save format v11, git clean.

**Do this next (in order, unless I redirect):**
1. **Phase 4 slice 4 — immigration & recruitment** (kanban `[P4]`, Ready for Development): religious immigration (crosses → emigrant at threshold 15, +2 each time), a 3-slot recruitment dock (weighted draw: freeColonist 20, experts 1), escalating recruit price (base 200, lower cap 80, +30 per paid recruit), placing recruits in Europe (units already support `InEurope`). Save v12. **Verify the formulas against FreeCol source before encoding — the research agents have hallucinated numbers before** (the founding-father cost sequence was wrong; I caught it by checking `Player.java`). Then add a recruitment E2E journey per `docs/TEST-PLAN.md` §12.
2. After that: the **Europe-screen UI** (sailing/Europe units aren't rendered off-map yet — flagged in `docs/systems/europe.md`), then Founding-Father **effects** (needs a modifier system; bonus-resource yields need the same — see kanban).

**Awaiting Chris's playtest (don't mark done):** four **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then begin slice 4.
