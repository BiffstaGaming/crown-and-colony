# Project: Crown & Colony (Colonization Remake in Godot)

**Game name:** Crown & Colony · **GitHub:** https://github.com/BiffstaGaming/crown-and-colony (public)

A from-scratch remake of Sid Meier's Colonization (1994), built natively in **Godot 4 with C#**, using **FreeCol** (the GPL v2 Java reimplementation, cloned at `freecol/`) as the concept base and reference specification. After the base game works, the plan is a **variant scenario set in Australia** (or another country) instead of the USA.

## About the user (Chris)

- Average software development experience — mostly **C# and PHP**. Can read and sanity-check C# code.
- **Zero game development experience** and zero Godot experience. Claude is expected to research and apply game-dev and Godot best practices proactively — do not assume Chris knows engine concepts; explain decisions briefly when they matter.
- No graphical or musical ability. All assets must come from freely-licensed online sources (see Licensing).
- Limited time. Chris reviews and decides; Claude does the legwork.

## Locked decisions (do not re-litigate without being asked)

| Decision | Choice | Why |
|---|---|---|
| Engine | Godot 4.x | Open source (MIT), license-compatible with GPL game code |
| Language | C# (.NET version of Godot) | Large logic-heavy codebase: static typing, refactoring tooling, mature test ecosystem (xUnit/NUnit), AI-turn performance. Chris can read it. Accepted trade-off: no browser export for now. |
| Architecture | Full native Godot remake | No Java server dependency. FreeCol code is a *reference spec*, not a runtime component. |
| Rules/data | Reuse FreeCol's XML data formats (`freecol/data/`) where practical | Inherits the complete ruleset; makes the Australia variant a data change, not a code change |
| Base game first | Faithful Colonization gameplay before any variant work | Variant = new data/scenario on a proven engine |

## Licensing — important constraints

- FreeCol is **GPL v2** (code and most assets; some assets CC BY 4.0). Anything derived from its code or assets makes this project **GPL v2**. Treat this project as GPL v2 from day one.
- Original 1994/2008 Sid Meier game assets, code, and data are **off-limits**. Never copy, extract, or decompile them.
- Third-party assets (art, music, sound) must have licenses compatible with GPL v2 distribution (CC0, CC BY, GPL, OFL etc. — verify each one, record source + license in an asset credits file). When in doubt, ask Chris before including.
- "Colonization" as a trademark: the released game needs its own name eventually — flag this when distribution becomes relevant.

## Local toolchain (Chris's machine — non-standard paths!)

This machine has no winget and the system .NET is runtime-only. The working toolchain:
- **.NET SDK 10 (user-local):** `C:\Users\Chris\.dotnet` — NOT first on PATH by default. Prepend it (and set `DOTNET_ROOT`) before any `dotnet` command, or dot-source `scripts/dev-env.ps1`. The `dotnet` that resolves without this is `C:\Program Files\dotnet` and **cannot build** (no SDKs).
- **Godot 4.6.3 .NET:** `C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\` — use the `_console.exe` for headless work (`--headless --path game --import` / `--build-solutions --quit`).
- Build: `dotnet build game/CrownAndColony.slnx` · Test: `dotnet test game/CrownAndColony.slnx` (solution is `.slnx`, the new format — there is no `.sln`).

## Repository layout

- `CLAUDE.md` — this file
- `freecol/` — read-only reference clone of FreeCol (GPL v2 Java), **gitignored** (not part of our repo; re-clone with `git clone --depth 1 https://github.com/FreeCol/freecol.git freecol`). **Never modify.** Use it to answer "how does the original behave?" — game rules live in `freecol/data/`, logic in `freecol/src/`, dev docs in `freecol/doc/`.
- `game/` — (to be created) the Godot project. All new work happens here.
- `docs/` — code-coupled documentation: `DOCUMENTATION.md` (standards — binding), `systems/` (one dual-audience doc per game system), `modules/` (one doc per code module), `templates/` (the formats to copy from)
- `LICENSE` — GPL v2 (whole project)

## Testing — non-negotiable requirements

Chris does **not** have time to manually test. The full strategy is **binding and lives in `docs/TESTING.md`** — read it before writing tests or CI. Summary:

- **Five-layer pyramid**: L1 unit (xUnit, engine-free `GameLogic`) → L2 scenario simulations (scripted turns, FreeCol cross-checks) → L3 interaction (GdUnit4Net scene runner, simulated input) → L4 visual regression (golden screenshots, custom harness) → L5 nightly smoke/soak (AI autoplay, perf budget).
- **Determinism (ADR-009)**: all randomness through a seeded, injectable RNG — no direct `Random`/`GD.Randf()` anywhere. Treat violations like compile errors.
- Every system doc's Verification section carries the five-layer coverage table; required layers must be green in CI before a feature is "done".
- CI gates: push = L1+L2; PR = +L3+L4; nightly = L5. Visual-golden regeneration must be deliberate and visible in the PR.
- When Claude completes work, it reports test results honestly. "Tests pass" must mean behavior verified at every required layer, not "it compiles."

## Documentation — the no-drift rule (non-negotiable)

Full standards live in `docs/DOCUMENTATION.md` — read it before writing code or docs. The core rule:

**Documentation is part of the change, not a follow-up task.** Any commit that adds or alters game logic, behavior, a formula, or a public API must update the matching documentation **in that same commit**:
- Game behavior changed → update `docs/systems/<system>.md` (create from `docs/templates/TEMPLATE-game-system.md` if missing) — **both layers**: the plain-English section AND the technical section, plus a changelog row.
- Public API added/changed → C# XML doc comments (`///`) and `docs/modules/<module>.md`.
- Every system doc is dual-audience: plain English first (no jargon, worked examples), technical second (exact formulas, code refs, FreeCol references, test list). If the plain-English section can't be written, the design isn't understood yet — stop and understand it.

**Definition of done for any feature:** tests pass (behavior verified) + system doc updated (both layers) + XML doc comments + changelog row + ClickUp updated if plan/decisions/assets changed. A feature missing any of these is not done — do not report it as done.

Split of responsibilities: **repo `docs/` = anything describing code behavior** (same-commit rule applies); **ClickUp = project-level knowledge** (plan, ADRs, session log, assets, engine research).

## Documentation & cross-session knowledge — ClickUp

All project knowledge lives in the ClickUp Space **"Colonization"** (Space ID `90167219053`; connector is configured). Purpose: (a) human-readable project plan, (b) durable memory Claude re-ingests across sessions.

Document IDs (use `clickup_list_document_pages` / `clickup_get_document_pages` to read):
- `2kz0t3mf-836` — 00 Documentation Standards (index — authoritative version is repo `docs/DOCUMENTATION.md`)
- `2kz0t3mf-716` — 01 Project Plan & Roadmap
- `2kz0t3mf-736` — 02 Architecture & Decisions (ADR)
- `2kz0t3mf-756` — 03 Game Design Reference
- `2kz0t3mf-776` — 04 Godot Knowledge Base
- `2kz0t3mf-796` — 05 Asset Register
- `2kz0t3mf-816` — 06 Session Log (newest entry first; add a new page per session)

Workflow each session:
1. **Start**: read the ClickUp Space docs (project plan, architecture decisions, current phase/status) before doing significant work.
2. **During**: track work as ClickUp tasks; keep statuses current.
3. **End of significant work**: update the relevant docs — decisions made, what changed, what's next. Write for a future session with zero conversation memory.

Documentation structure (best practice — keep these as separate documents):
- **Project Plan / Roadmap** — phases, milestones, current status
- **Architecture & Decisions (ADR-style)** — one entry per decision: context, choice, why
- **Game Design Reference** — the Colonization ruleset as we implement it, with FreeCol file references
- **Godot Knowledge Base** — engine patterns/best practices researched and adopted for this project
- **Asset Register** — every asset: source URL, license, attribution requirement
- **Session Log** — dated entries: what was done, state of play, immediate next steps

## How Claude should work on this project

- **No knowledge lives only in chat**: any plan, standard, template, or concept agreed in conversation must be formalized into the appropriate doc (repo `docs/` if code-coupled, ClickUp if project-level, CLAUDE.md if it's a working rule) **in the same session it's agreed** — and committed/pushed. If it isn't written down, it doesn't exist for the next session.
- **Research first**: for any Godot or game-dev pattern, check current best practice online (Godot 4.x specifically — much online material is outdated Godot 3) before implementing.
- **Ask when it matters**: when a decision genuinely needs Chris's input, ask — and always present researched options with a recommendation, not open-ended questions.
- **Don't gold-plate**: faithful-to-FreeCol behavior first; modern features and the Australia variant come after the base game is solid.
- **Keep the separation**: game logic (pure C#, tested) vs. presentation (Godot scenes/nodes). This is the architectural rule that makes the testing requirements achievable.
