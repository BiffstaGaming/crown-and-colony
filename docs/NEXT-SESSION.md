# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-14 after Phase 5 slice 5b.

---

You're continuing work on **Crown & Colony** (a Godot 4 / C# remake of Sid Meier's Colonization, FreeCol as reference spec). Read `CLAUDE.md` first — locked decisions, the non-standard toolchain paths, working rules. Then catch up via the ClickUp Space "Colonization": the **Session Log (doc 06, newest entry first)** and the **kanban** (`clickup_filter_tasks` on list `901615382059`). The **Roadmap is doc 01**.

**Where we are (2026-06-14):** Phases 0–4 are complete. **Phase 5 (other powers)** is in progress, built **natives-first / incremental** (Chris's call): a minimal "owner" concept now, the full multi-player refactor deferred to the foreign-European slice. Shipped so far:
- **Variant / game-mode selection layer (ADR-018)** — the transposability backbone. Selecting a `GameVariant` is the *only* thing that swaps the data; a test proves a different ruleset yields a different game. Saves record their variant (v15). *This was Chris's explicit requirement: American/Australian/etc. game modes each define their own Founding Fathers/nations/countries via data, not code.*
- **Native settlements (1)** — 8 nations + camp/village/city types parsed, placed at map-gen, rendered with FreeCol art (fog-gated). Save v14.
- **Fog upgrade (2)** — explored vs. currently-visible; remembered tiles dim.
- **Native interaction (3)** — alarm/tension model, speak-with-chief (gift/tales), learn-skill. Save v16.
- **Native trade (4)** — sell cargo to coastal settlements (wanted-goods premium pricing, no tax, builds goodwill). Save v17.
- **Combat foundation (5a)** — unit offence/defence + terrain defence parsed; pure `CombatModel` (power, odds `att/(att+def)`, graded resolution), pinned to FreeCol's `SimpleCombatModel`.
- **Combat — attack action (5b)** — unit ownership (`OwnerNationId`; `PlayerUnits`/`NativeUnits`) + roles/equipment (`RoleType`, `UnitChange`, `EquipRole`), brave defenders (one per settlement, fog-excluded), `CheckAttack`/`Attack` with the faithful FreeCol loser/winner precedence (slaughter / disarm + equipment-capture / capture-unit / demote / promote), native alarm on attack (+200/+400), George Washington (auto-promote) + Paul Revere (auto-arm). Save v18. *Player-initiated open-field combat vs braves only — settlements/naval/foreign/native-AI are 5c.*

**352 automated tests** (330 L1+L2 incl. 10 E2E + 2 nightly soak + 16 L3 scene + 4 L4 visual), CI green, **save format v18**, git clean.

**Do this next (Phase 5, natives-first order — kanban has the granular tasks):**
1. **Combat 5c** (`86d3bba2z`): settlement attack/plunder/destroy (parsed settlement defence + `<plunder>`), naval combat + evade/sink, foreign-unit combat, **native-initiated attacks (native AI)**, **nation-level tension propagation**. Unblocks **Drake** and **Cortés**, and exercises the capture-unit + Revere paths end-to-end. (Brave-on-settlement-tile settlement-defence bonus, `getSlaughterTension` routing, Revere musket persistence, and the veteran+role percentage path are now all handled or scoped here — see `docs/systems/combat.md` §5.)
2. **Foreign European powers + the full multi-player refactor + basic AI** (`86d3b7qwm`) — the largest chunk; decompose when reached. Then the **deferred Founding-Father effects** (`86d3b7qxr`).
3. Smaller queued: **native-interaction UI** (`86d3bb1wh`, on-map speak/learn panel — makes interaction playable), **native trade buy + inland/wagon trains** (in natives.md TODO), **transposability-tuning migration** (`86d3bb1x3`: move FreeCol-pinned constants — gift range, decay, alarm bands, combat modifiers, learner set — to ruleset data), **apply role movement bonuses** (`86d3bbvv6`).

**Then:** Phase 6 (independence & REF), Phase 7 (polish), Phase 8 (Australia variant = author a data set + register a `GameVariant`, *no engine rewrite* — the whole point of ADR-018).

**How to work here (matters most):**
- **Docs are part of the change** (no-drift): a slice isn't done until its `docs/systems/<x>.md` (both layers + verification + changelog), `save-load.md` if the format changed, the module docs, `QA-REPORT.md` counts, the kanban status, and a Session Log entry are updated *in the same work*.
- **Determinism (ADR-009):** all randomness via the injectable `IGameRandom`. Native placement uses a separate stream (1); interaction/trade/combat use the **main saved RNG** so save/resume stays deterministic.
- **Transposability (ADR-018):** new mechanics read ruleset data; FreeCol-pinned tuning constants are tracked for migration, not hard-coded as American-specific.
- **Verify FreeCol numbers against `freecol/` source** and pin them in tests with the source referenced.
- **Process used this phase (worked well):** for each substantive slice, a **research workflow** (parallel readers over FreeCol + our code) → implement → an **adversarial review workflow** → fix → commit. (Skip the review for pure-math slices already pinned by tests.)
- **Definition of done = tests green at every required layer + docs synced + CI green.** Logic: `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj`. Scene (L3/L4): `dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings` after a clean `godot --build-solutions` (+ `GODOT_BIN`). Toolchain: dot-source `scripts/dev-env.ps1` (prepends the user-local .NET 10 SDK + sets Godot paths). Chris has authorised **commit & push to `main` per slice**.

**Awaiting Chris's playtest (don't mark done):** the In Review kanban items — FreeCol art passes, colony screen, economy UI, Europe screen, native settlements. Native interaction / trade / combat have **no UI yet** (logic + tests only; the native-interaction UI is task `86d3bb1wh`). Launch the game if asked: `& "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" --path "C:\Users\Chris\Code\Colonization\game"` (launch detached — e.g. `Start-Process` — or the harness reaps it).

Start by reading the latest Session Log entry + the open kanban, confirm the build + tests are green, then continue with **Combat 5c** — or ask me if you'd rather reprioritise (e.g. make the native interaction/combat playable with UI first).
