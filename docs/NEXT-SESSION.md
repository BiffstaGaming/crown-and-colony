# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-15 — the **UI-first conflict + founding-father wave is COMPLETE** (naval combat, colony capture, native AI, ambient alarm, Pocahontas/Magellan/Drake). Set up for **continuous, unattended work** through a prioritized queue.

---

You are continuing work on **Crown & Colony**, a faithful Godot 4 / C# remake of Sid Meier's *Colonization* (1994), using **FreeCol** (GPL v2, cloned read-only at `freecol/`) as the reference spec. Repo: `C:\Users\Chris\Code\Colonization` · GitHub `BiffstaGaming/crown-and-colony` · branch `main` · git user `BiffstaGaming`. Read `CLAUDE.md` first (locked decisions, toolchain, working rules).

## Mode: autonomous, continuous — keep shipping, don't pause to ask

Work through the queue below one slice at a time, end-to-end, until told otherwise. Don't pause for approval; when a call is genuinely Chris's, pick the most faithful, reversible, documented option, note it in PostWorkSummary under **Needs you**, and continue. Per-slice push to `main` is authorized. If genuinely blocked, write a blocker note in PostWorkSummary + the Session Log and move to the next queued item. You may use the **Workflow** tool for per-slice adversarial reviews and planning fan-outs (the established process).

## Start of session

1. Read the latest ClickUp **Session Log** (doc `2kz0t3mf-816`), the **kanban** (list `901615382059`), the **Roadmap** (doc `2kz0t3mf-716`), and the top of `PostWorkSummary.md`.
2. `git pull`; confirm a clean `main`.
3. Set the toolchain (below); confirm the build + L1/L2 tests are green.
4. Start the next queued slice.

## Current state (HEAD `f96b408`, 2026-06-15)

- **Save format v21.** Tests: **521 green** = 489 L1+L2 (incl. 10 E2E) + 4 soak + 28 scene (23 L3 + 5 L4).
- Shipped this wave: native AI raids (1b); rival/own-unit rendering + CheckMove colony guard (1c-1); foreign retaliation (1c-2); naval combat (1c-3a), ship damage+repair (1c-3b, v21), cargo loot (1c-3c), privateer piracy (1c-3d-i), Francis Drake (1c-3d-ii); colonial-colony capture (1c-3e, **human-initiated only**); on-map native interaction UI; ambient native alarm (Pocahontas −50% relocated onto it). Fathers applied: Washington, Revere, Cortés, Pocahontas, Magellan, Drake (+ economy: Jefferson, Penn, Paine, Brewster, Hudson).

## Work queue (decompose each into a granular kanban task when you start it — rolling-wave; re-check the kanban for Chris's priority order)

1. **Foreign-AI colony capture** — a power at War with an armed land unit beside an undefended human colony captures it (extend `RunForeignPowerTurn` war branch; reuse `AttackColony` on the power's own stream). Needs a **colony-loss notice channel** so the human is told. FreeCol `csCaptureColony`.
2. **Native-AI follow-ups** (`86d3bkc3w`) — braves pillage/capture an undefended colony (`pillageUnprotectedColony`) + tribute demands (`IndianDemandMission`). Ambient alarm now makes this feel earned.
3. **Building-grant fathers** — Adam Smith (factories), Stuyvesant (custom house), La Salle (free stockade) — contained Congress folds. **La Salle's stockade should also unlock the colony/stockade defence bonus** deferred in colony capture (needs `BuildingType` defence modifiers).
4. **Colony-capture follow-ups** — plunder gold (FreeCol `colony.getPlunder`, only if a faithful formula can be pinned), drydock-colony repair (`model.ability.repairUnits`), Revere auto-equip of a colony's last defender, ships in a falling colony.
5. **AI-initiated piracy** — a foreign power's privateer raids human shipping from peace.
6. **European-diplomacy fathers** (Franklin/de Witt) — **larger**; needs monarch/REF-war + inter-European stance + AI diplomatic-trade. Scope carefully or defer.
7. **Simón Bolívar** (Sons-of-Liberty) — needs an SoL/rebel-sentiment system.

If the queue empties or a front needs design, run a planning Workflow (parallel doc/kanban readers → synthesis → completeness critic) to pick and decompose the next wave.

## Per-slice process (binding)

research (read FreeCol + our code; pin exact numbers from `freecol/`) → implement → **2-lens adversarial review (Workflow)** → fix → **docs in the same commit** → commit & push to `main` → **verify CI green** (`gh run watch`) → mark the kanban task **Shipped** + Session Log page + prepend a `PostWorkSummary.md` entry. **Definition of done = tests green at every required layer + docs synced + CI green.**

## Binding constraints

- **ADR-009 byte-stability (non-negotiable):** the human draws ONLY from RNG stream 0 (`Game._random`); every non-human player ONLY from its own `Player.Rng` via `RandomFor(player)`; AI combat MUST use the internal `Attack(unit, target, IGameRandom)` overload. Stable id/position iteration; no `Random`/`GD.Randf()`/unordered iteration on AI paths. Keep/add a stream-0-untouched test for any AI-touching slice.
- **ADR-006:** rules in engine-free `GameLogic` (xUnit-tested); Godot only present/route. The presentation test project can't set GameLogic internals — inject via the save layer.
- **No-drift docs (same commit):** any behavior/formula/API change updates the matching `docs/systems/<x>.md` (BOTH plain-English + technical layers + a changelog row), XML doc comments, `docs/QA-REPORT.md` (counts + snapshot), the ClickUp Session Log, and `PostWorkSummary.md`.
- **Save format:** new persisted field → additive, default-omitted (nullable + `WhenWritingNull`); bump `SaveGame.CurrentVersion` + the version-pinned tests only when adding a field; prefer reusing existing serialized state.
- **Faithful-to-FreeCol first; don't gold-plate; don't fabricate numbers** — defer a piece (documented) rather than invent a value.

## Toolchain (this machine — gotchas)

- **.NET SDK 10 (user-local)** is NOT first on PATH; the system `dotnet` can't build. Before any dotnet command, either dot-source `scripts/dev-env.ps1` (PowerShell tool) **or** in the Bash tool: `export DOTNET_ROOT="C:\Users\Chris\.dotnet" && export PATH="C:\Users\Chris\.dotnet:$PATH"`.
- **Godot 4.6.3 .NET:** `GODOT_BIN = C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe`. After adding any scene node or `.cs`/scene file, run `--headless --path game --import` before scene tests.
- **Commands:** build `dotnet build game/CrownAndColony.slnx` · L1+L2 `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category!=Soak"` · soak `--filter "Category=Soak"` · scene `dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings` (needs `GODOT_BIN`). `GameLogic.Tests` is deliberately NOT in the `.slnx`.
- **GdUnit4 scene runner cold-start crash** (`-1073741819` / timeout, ADR-015) — just re-run (up to ~3×); CI auto-retries. **CRITICAL: close any running Godot *editor* on this project first** — a live editor collides with the headless runner and causes that crash every time. Visual goldens are committed PNGs; regenerate only deliberately (`GOLDEN_UPDATE=1`).
- **Commits:** bash can't parse PowerShell here-strings — write the message to `.git/COMMIT_x.txt` and `git commit -F`. End every commit message with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Stage the git-tracked `.uid` sidecars import generates.
- **Workflow tool:** inside template-literal prompts use single quotes for inline code (backticks terminate the string); don't put the literal strings `Math.random`/`Date.now`/`new Date` in prompt text (the determinism validator scans for them — say "unseeded randomness").
- Launch the game (detached, if asked): `Start-Process "C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe" -ArgumentList '--path','C:\Users\Chris\Code\Colonization\game'` — **then close it before running scene tests.**

## Reporting (every slice)

Prepend a dated `PostWorkSummary.md` entry (format at the top of that file) **and paste that exact entry into your chat reply**. Write a ClickUp Session Log page (doc `2kz0t3mf-816`) per slice. Kanban = list `901615382059`.

**Begin now:** start-of-session steps, then the first queued slice. Keep going until the queue is empty or you're genuinely blocked.
