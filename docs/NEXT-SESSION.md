# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 4 slice 4.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–3 are complete; **Phase 4 (Europe & trade) is in progress** — slices 1 (market+treasury), 2 (Founding Fathers), 3 (Europe + high-seas sailing), **4 (immigration & recruitment)** are shipped. The full produce→load→sail→sell→return trade loop works in code, and Europe now produces immigrants + a paid recruitment dock. **183 tests** (168 logic incl. 7 end-to-end journeys + 2 nightly soak + 13 scene/visual), CI green, save format v12, git clean.

**Do this next (in order, unless I redirect):**
1. **Europe-screen UI** (next P4 task): there is still no Europe screen — sailing/Europe units aren't rendered off-map, and the recruitment dock/price/immigration pool (all on `Game`: `RecruitDock`, `RecruitPrice`, `Immigration`, `ImmigrationRequired`, `UnitsInEurope`, `Recruit(slot)`) have no UI. Flagged in `docs/systems/europe.md` and `docs/systems/immigration.md`. This is the first Europe screen, so it needs L3 interaction + (optionally) L4 visual coverage.
2. After that: **carry recruited colonists home** (load a unit onto a ship as cargo, so emigrants reach the New World), then Founding-Father **effects** (needs a modifier system; bonus-resource yields and William Brewster's recruit-selection need the same — see kanban).

**Awaiting Chris's playtest (don't mark done):** four **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then begin the Europe-screen UI.
