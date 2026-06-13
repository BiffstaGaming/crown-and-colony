# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-13 after Phase 5 slice 1.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — it has the locked decisions, the non-standard toolchain paths, and the working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-13):** Phases 0–4 are complete. **Phase 5 (other powers)** has begun — `[EPIC P5]` is decomposed (natives-first, incremental: Chris's call). **Slice 1 (native settlements) is done**: the 8 indigenous nations + camp/village/city settlement types parse from the spec, settlements are placed at map-gen (capital-first, min-distance, on a dedicated RNG stream) and render on the map with FreeCol indian art (fog-gated — discover them by exploring), save format **v14**. **250 tests** (229 logic incl. 10 E2E + 2 soak + 16 L3 scene + 3 L4 visual), all green, git clean. See `docs/systems/natives.md`.

**Do this next (Phase 5, natives-first order — kanban has the granular tasks):**
1. **`[P5] Fog-of-war upgrade: explored vs. visible`** (`86d3b7qn8`): add per-turn visibility on top of permanent "explored"; dim explored-but-unseen tiles; the doc-flagged upgrade in `docs/systems/fog-of-war.md`.
2. **`[P5] Native interaction`** (`86d3b7qpf`): tension/alarm model (FreeCol `Tension.java` thresholds) + visit a settlement (gifts/tales, learn the taught skill). Unblocks Pocahontas.
3. **`[P5] Native trade`** (`86d3b7qre`), then **`[P5] Combat — land`** (`86d3b7qvd`) — note combat data is **not parsed yet** (offence/defence/role modifiers must be added to `Ruleset`/`UnitType` first). Then the big **foreign-European + multi-player refactor** (`86d3b7qwm`, decompose when reached) and the **deferred father effects** (`86d3b7qxr`).

**Awaiting Chris's playtest (don't mark done):** the **In Review** kanban items — the FreeCol art passes, the colony screen, the economy UI, and the Europe screen. Launch the game for him if he asks (`& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"`).

**Working rules that matter most here:**
- **Docs are part of the change** (no-drift rule): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification table + changelog), save-load changelog, kanban status, and session log are updated in the same work.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`; no direct `Random`/`GD.Randf()`.
- **Verify FreeCol numbers against source**, don't trust recall — and pin them in tests with the source line referenced.
- **Definition of done = tests green at every required layer + docs synced + CI green.** Run `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj` for logic; scene tests need a clean `godot --build-solutions` first (local discovery quirk — see Godot KB doc 04). Toolchain: prepend `C:\Users\Chris\.dotnet` to PATH + set `DOTNET_ROOT` (or dot-source `scripts/dev-env.ps1`).
- **QA:** `docs/TEST-PLAN.md` (E2E journeys) and `docs/QA-REPORT.md` (results + screenshots) are the QA surfaces — keep them current.

Start by reading the latest Session Log entry and the open kanban, confirm the build + tests are green, then continue Phase 5 with the next slice (fog-of-war upgrade, or native interaction) — or ask me if you'd rather reprioritise.
