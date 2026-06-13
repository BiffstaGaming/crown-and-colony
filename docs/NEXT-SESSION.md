# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 4 slice 9.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–3 are complete; **Phase 4 (Europe & trade)** has shipped slices 1–9: market+treasury, Founding Fathers, Europe + high-seas sailing, immigration & recruitment, unit transport, the Europe screen UI, Founding-Father effects (modifier/ability system), bonus-resource yields, and colonist join/leave. The whole loop is playable end-to-end: produce → trade, recruit/immigrate → board a ship → sail home → disembark → found **or grow** a colony; elected fathers grant their bonuses. **222 tests** (205 logic incl. 10 end-to-end journeys + 2 nightly soak + 13 L3 scene + 2 L4 visual), CI green, save format v13, git clean.

**Do this next (Phase 4's core is done — pick a direction, I'll recommend):**
1. **Finish Phase 4 polish** (smaller, logic-light): buying units/ships in Europe (spec `price`); a goods buy/sell UI on the Europe screen (logic exists, no buttons); map-side board/disembark/join UI next to a coastal ship. Each is a self-contained slice.
2. **Start `[EPIC P5]` — other powers** (bigger, higher value): decompose natives / foreign European AI / combat. This unblocks most of the **deferred Founding-Father effects** (Revere, Washington, Drake, Pocahontas, Franklin, de Witt, Cortés, …) and is the natural next phase.
3. Smaller deferred items on the kanban: expert-unit production yields (`86d3b6nrz`, needs per-colonist colony identity); Magellan's movement modifier; the calendar / difficulty levels.

**Awaiting Chris's playtest (don't mark done):** four **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then pick a direction above (ask me if unsure — Phase 4's core is complete, so this is a real fork).
