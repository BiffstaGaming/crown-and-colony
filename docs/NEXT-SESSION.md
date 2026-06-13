# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 4 slice 6.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–3 are complete; **Phase 4 (Europe & trade) is in progress** — slices 1 (market+treasury), 2 (Founding Fathers), 3 (Europe + high-seas sailing), 4 (immigration & recruitment), 5 (unit transport — ships carry colonists), **6 (the Europe screen UI)** are shipped. The full loop now works end-to-end *and* is playable: produce → trade, recruit/immigrate → board a ship → sail home → disembark → found/grow. **198 tests** (181 logic incl. 8 end-to-end journeys + 2 nightly soak + 13 L3 scene + 2 L4 visual), CI green, save format v13, git clean.

**Do this next (in order, unless I redirect):**
1. **Founding-Father effects** — a **modifier system** so elected fathers actually grant their bonuses (currently election is cosmetic). This also unblocks **William Brewster** (choose the emigrating recruit slot) and **bonus-resource yield modifiers + expert units** (kanban `[P3] 86d3b465k`), which want the same machinery. Pure logic → L1/L2.
2. Then candidates: **buying units/ships in Europe**; **colonist in/out of a colony** (disembark straight into a colony, embark from one); map-side board/disembark UI (next to a coastal ship). Pick per the kanban.

**Awaiting Chris's playtest (don't mark done):** four **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then begin the Founding-Father effects / modifier system.
