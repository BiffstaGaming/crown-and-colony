# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 4 slice 11.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–3 are complete; **Phase 4 (Europe & trade)** has shipped slices 1–11: market+treasury, Founding Fathers (+effects), Europe + high-seas sailing, immigration & recruitment, unit transport, the Europe screen UI, bonus-resource yields, colonist join/leave, goods trading on the Europe screen, and buying units in Europe. **Phase 4 is essentially complete** — the Europe screen is a full port (recruit, buy units, buy/sell goods, board/sail) and the whole loop is playable. **231 tests** (211 logic incl. 10 end-to-end journeys + 2 nightly soak + 16 L3 scene + 2 L4 visual), CI green, save format v13, git clean.

**Do this next (Phase 4 done — the natural next is a new phase):**
1. **Start `[EPIC P5]` — other powers** (recommended, the big value): decompose natives / foreign European AI / combat. This unblocks most of the **deferred Founding-Father effects** (Revere, Washington, Drake, Pocahontas, Franklin, de Witt, Cortés, …). A sensible first slice: native settlements on the map (placement + rendering + existence), then native interaction (visit/trade), then combat.
2. **Or finish the last Phase 4 nicety:** map-side board/disembark/join UI next to a coastal ship (the only Europe-loop action still API-only on the map). Small.
3. Smaller deferred items: expert-unit production yields (`86d3b6nrz`, needs per-colonist colony identity); Magellan's movement modifier; the calendar / difficulty levels.

**Awaiting Chris's playtest (don't mark done):** four **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then begin Phase 5 (decompose `[EPIC P5]` into granular tasks first) — or ask me if you'd rather do the last Phase 4 nicety.
