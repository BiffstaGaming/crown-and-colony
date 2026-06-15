# New-session prompt

Copy the block below into a fresh session. (CLAUDE.md auto-loads, so this focuses on *where we are* and *what to do next*.) Last updated 2026-06-16 — the **colonies-under-threat arc is COMPLETE** (foreign-AI capture, native pillage, fortification defence, La Salle, capture plunder, AI besiege, human defeat). Set up for **continuous, unattended work** through a prioritized queue.

---

You are continuing work on **Crown & Colony**, a faithful Godot 4 / C# remake of Sid Meier's *Colonization* (1994), using **FreeCol** (GPL v2, cloned read-only at `freecol/`) as the reference spec. Repo: `C:\Users\Chris\Code\Colonization` · GitHub `BiffstaGaming/crown-and-colony` · branch `main` · git user `BiffstaGaming`. Read `CLAUDE.md` first (locked decisions, toolchain, working rules).

## Mode: autonomous, continuous — keep shipping, don't pause to ask

Work through the queue below one slice at a time, end-to-end, until told otherwise. Don't pause for approval; when a call is genuinely Chris's, pick the most faithful, reversible, documented option, note it in PostWorkSummary under **Needs you**, and continue. Per-slice push to `main` is authorized. If genuinely blocked, write a blocker note in PostWorkSummary + the Session Log and move to the next queued item — but be sure the blocker is genuine (FreeCol is a complete, working game, so ~99% of "how should this behave?" questions are answered in `freecol/`; only things invented in our own discussions are truly open). You may use the **Workflow** tool for per-slice adversarial reviews and planning fan-outs (the established process).

## Start of session

1. Read the latest ClickUp **Session Log** (doc `2kz0t3mf-816`, newest first), the **kanban** (list `901615382059`), the **Roadmap** (doc `2kz0t3mf-716`), and the top of `PostWorkSummary.md`.
2. `git pull`; confirm a clean `main`.
3. Set the toolchain (below); confirm the build + L1/L2 tests are green.
4. Start the next queued slice.

## Current state (HEAD `f04281b`, 2026-06-16)

- **Save format v21.** Tests: **557 green** = 523 L1+L2 (incl. 10 E2E) + 4 soak + 30 scene (25 L3 + 5 L4).
- **Shipped (the conflict/combat + founding-father wave):** native AI raids (1b); rival rendering + CheckMove colony guard (1c-1); foreign retaliation (1c-2); naval combat / damage+repair / cargo loot / privateer piracy / Drake (1c-3a–d); colonial-colony capture (1c-3e); on-map native interaction UI; ambient native alarm; **+ this session's arc:** foreign-AI colony capture (1c-3f, two-directional) + a `ColonyLossNotice` channel; native colony pillage (goods-loot) + a `ColonyRaidNotice` channel; **colony fortification defence bonus** (`BuildingType.DefenceBonus` / `Game.ColonyDefenceBonus` — stockade +100 / fort +150 / fortress +200, applied to a colony's defender in capture/pillage/field combat, terrain suppressed in-colony); **La Salle** (free stockade at pop 3, `model.event.freeBuilding` → `Game.ApplyFreeBuildings`); **colony capture plunder** (`Game.PlunderColony`/`ColonyPlunderAmount`, FreeCol `getPlunderRange` — now pinned); **AI besiege pathing** (a war-power land unit with no field prey marches on `NearestHumanColony`); **human defeat** (`Game.IsHumanDefeated` computed + a HUD banner).
- **Fathers applied:** Washington, Revere, Cortés, Pocahontas, Magellan, Drake, **La Salle** (+ economy: Jefferson, Penn, Paine, Brewster, Hudson). Deferred fathers need new systems (see queue).

## Things this session established (reuse, don't rebuild)

- **Colony defence bonus exists** (`Game.ColonyDefenceBonus(colony)`; `BuildingType.DefenceBonus`) — applied via `DefenceContext.SettlementDefenceBonus` in `Attack`/`AttackColony`/`PillageColony`. Terrain is suppressed for a unit defending in a colony (FreeCol settlement branch).
- **Colony plunder formula pinned:** `ColonyPlunderAmount` = `rnd[0, upper]+1`, `upper = ownerGold×(colonyPop+1)/(coloniesPop+1)` (FreeCol `getPlunderRange`); capped at the victim's purse. Native-pillage gold-steal (`getPlunder/5`) is the documented, un-built sibling.
- **Transient UI notice channels** (cleared each `EndTurn`, never saved): `CombatNotices` (raids), `ColonyLossNotices` (AI captures), `ColonyRaidNotices` (native pillage). Add to these for new AI-vs-human events; the presentation drains them in `OnEndTurnPressed`.
- **Human defeat: do NOT short-circuit `EndTurn` on defeat.** It freezes the human's stream 0 and breaks ADR-009 byte-stability (a wiped-out game diverges from a surviving one — this broke two stream-0 tests). *Stopping* the game on defeat is a **presentation** concern (task `86d3c0x3f`).
- **A concurrent doc-audit agent may also be committing to `main`.** Stage **explicit paths** (never `git add -A`), `git pull` before editing `PostWorkSummary.md`/`NEXT-SESSION.md`, and handle a rejected push with `git pull --rebase` then re-push.

## Work queue (decompose each into a granular kanban task when you start it — rolling-wave; re-check the kanban for Chris's priority order)

1. **Native tribute demands** (`IndianDemandMission`, `86d3bkc3w`) — a brave demands goods/gold from a colony; the human accepts (pays, alarm eases) or refuses (alarm rises / attack). The last native-AI follow-up. **Needs a player accept/refuse decision flow** (a new interaction modality — design it: a pending-demand prompt surfaced after the AI phase, resolved next input).
2. **Game-over flow on defeat** (`86d3c0x3f`) — *presentation only*: when `Game.IsHumanDefeated`, disable/relabel the End Turn button and/or show a game-over screen. **Small.** Do NOT touch `EndTurn` logic (see above).
3. **Native-pillage gold-steal** — extend `PillageColony` so the uniform loot pick includes gold (`max(1, ColonyPlunderAmount/5)`) alongside goods. Small; mind the loot-pick index in the existing `NativePillageTests`.
4. **AI colony-vs-unit target scoring** — refine the besiege fallback into FreeCol-style scored targeting (`UnitSeekAndDestroyMission`: score colony paths vs unit paths and pick the best) instead of "fallback only when no field prey".
5. **Building-grant fathers** — **Adam Smith** (factory-tier buildings) and **Peter Stuyvesant** (custom house). Need **ability-gated buildables** (our buildable set doesn't gate on `required-ability` yet) + the building *effects* (factory production / custom-house auto-sell). La Salle is done.
6. **Drydock-colony repair** (`model.ability.repairUnits`) — a damaged ship repairs at the nearest own colony with a drydock, else Europe (today Europe is the only repair location).
7. **AI-initiated piracy** — a foreign power's privateer raids human shipping from peace (the piracy mechanic + `ColonyLossNotice`-style feedback exist; this needs foreign powers to *acquire* privateers + a peace-time privateer-raid branch — pin a faithful provisioning rule).
8. **European-diplomacy fathers** (Franklin/de Witt) — **larger**; needs monarch/REF-war + inter-European stance + AI diplomatic-trade. Scope carefully or defer.
9. **Simón Bolívar** (Sons-of-Liberty bump) — needs an SoL/rebel-sentiment system.
10. **Native/brave unit sprites** (`86d3bmfcx`, ART) — red-disc fallback today; freely-licensed art only (record source + license in the Asset Register).

If the queue empties or a front needs design, run a planning Workflow (parallel doc/kanban readers → synthesis → completeness critic) to pick and decompose the next wave.

## Per-slice process (binding)

research (read FreeCol + our code; pin exact numbers from `freecol/`) → implement → **2-lens adversarial review (Workflow: fidelity + determinism/safety; structured findings; triage)** → fix → **docs in the same commit** → commit & push to `main` → **verify CI green** (`gh run watch`) → mark the kanban task **Shipped** + Session Log page + prepend a `PostWorkSummary.md` entry. **Definition of done = tests green at every required layer + docs synced + CI green.**

## Binding constraints

- **ADR-009 byte-stability (non-negotiable):** the human draws ONLY from RNG stream 0 (`Game._random`); every non-human player ONLY from its own `Player.Rng` via `RandomFor(player)`; AI combat MUST use the internal `Attack`/`AttackColony`/`PillageColony(…, IGameRandom)` overloads. Stable id/position iteration; no `Random`/`GD.Randf()`/unordered iteration on AI paths. Keep/add a stream-0-untouched test for any AI-touching slice. (Reminder: a "stop the game" guard inside `EndTurn` violates this — see human defeat.)
- **ADR-006:** rules in engine-free `GameLogic` (xUnit-tested); Godot only present/route. The presentation test project can't set GameLogic internals — inject via the save layer.
- **No-drift docs (same commit):** any behavior/formula/API change updates the matching `docs/systems/<x>.md` (BOTH plain-English + technical layers + a changelog row), XML doc comments, `docs/QA-REPORT.md` (counts + snapshot), the ClickUp Session Log, and `PostWorkSummary.md`.
- **Save format:** new persisted field → additive, default-omitted (nullable + `WhenWritingNull`); bump `SaveGame.CurrentVersion` + the version-pinned tests only when adding a field; prefer reusing existing serialized state (most of this session's slices needed no bump).
- **Faithful-to-FreeCol first; don't gold-plate; don't fabricate numbers** — defer a piece (documented) rather than invent a value.

## Toolchain (this machine — gotchas)

- **.NET SDK 10 (user-local)** is NOT first on PATH; the system `dotnet` can't build. Before any dotnet command, either dot-source `scripts/dev-env.ps1` (PowerShell tool) **or** in the Bash tool: `export DOTNET_ROOT="C:\Users\Chris\.dotnet" && export PATH="C:\Users\Chris\.dotnet:$PATH"`.
- **Godot 4.6.3 .NET:** `GODOT_BIN = C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe`. After adding any scene node or `.cs`/scene file, run `--headless --path game --import` before scene tests (this also generates the git-tracked `.cs.uid` sidecars — stage them).
- **Commands:** build `dotnet build game/CrownAndColony.slnx -clp:ErrorsOnly` · L1+L2 `dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category!=Soak"` · soak `--filter "Category=Soak"` · scene `dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings` (needs `GODOT_BIN`). `GameLogic.Tests` is deliberately NOT in the `.slnx`.
- **GdUnit4 scene runner cold-start crash** (`-1073741819` / timeout, ADR-015) — just re-run (up to ~3×); CI auto-retries. **Close any running Godot *editor* on this project first.** Visual goldens are committed PNGs; regenerate only deliberately (`GOLDEN_UPDATE=1`).
- **Commits:** bash can't parse PowerShell here-strings — write the message to `.git/COMMIT_x.txt` and `git commit -F`. End every commit message with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Workflow tool:** inside template-literal prompts use single quotes for inline code (backticks terminate the string) — or build the prompt with an array `.join('\n')` to dodge it; don't put the literal strings `Math.random`/`Date.now`/`new Date` in prompt text (the determinism validator scans for them — say "unseeded randomness").

## Reporting (every slice)

Prepend a dated `PostWorkSummary.md` entry (format at the top of that file) **and paste that exact entry into your chat reply** (it must appear in the response, not only the file). Write a ClickUp Session Log page (doc `2kz0t3mf-816`) per slice. Kanban = list `901615382059` (statuses: Backlog → Ready for Development → In Development → … → Shipped).

**Begin now:** start-of-session steps, then the first queued slice. Keep going until the queue is empty or you're genuinely blocked.
