# New-session prompt

Copy the block below into a fresh session (or just tell Claude Code: *"Read `docs/NEXT-SESSION.md` and carry it out."*). CLAUDE.md auto-loads. **Last updated 2026-06-16.** This session's job changed: instead of shipping features, it **builds the complete, FreeCol-grounded development backlog in ClickUp** so Chris can see the entire planned scope of the game in one place.

---

You are working on **Crown & Colony**, a faithful Godot 4 / C# remake of Sid Meier's *Colonization* (1994), using **FreeCol** (the GPL-v2 Java reimplementation, cloned read-only at `freecol/`) as the **authoritative concept/behaviour spec**. Repo `C:\Users\Chris\Code\Colonization` · GitHub `BiffstaGaming/crown-and-colony` · branch `main`. Read `CLAUDE.md` first (locked decisions, conventions, the docs-vs-ClickUp split, toolchain).

## Session goal

Create a ClickUp task for **every remaining piece of development work**, derived by systematically comparing **what FreeCol does** against **what Crown & Colony currently implements** — and produce a single master index doc — so Chris can open the board (and that one doc) and see the **ENTIRE planned scope**.

**This is a PLANNING session: you create tasks + an index. Do NOT implement features or touch game code.**

## The one non-negotiable rule

**Nothing is assumed, invented, or "designed" by you. Every feature/behaviour task MUST cite a FreeCol source** — a Java class/method in `freecol/src/...`, an element in `freecol/data/rules/classic/specification.xml`, or a `freecol/doc/` reference. If a behaviour is not in FreeCol, it is **not** a feature task. If you can't confirm FreeCol does X, do not create the task — list it under **"Open questions for Chris"** instead. (The only non-FreeCol tasks allowed are the already-decided **engine / QA / process** work and the **Australia variant** — both trace to existing project decisions in CLAUDE.md / the ClickUp ADR doc, not to your imagination.)

## Orient yourself first (read, in order)

1. `CLAUDE.md` — locked decisions, kanban statuses/prefixes, the ClickUp IDs.
2. `docs/systems/*.md` — every implemented system, each with its FreeCol references, **documented deviations**, and a verification table. This is your "what's done / partial" ground truth.
3. `docs/QA-REPORT.md`, `docs/TEST-PLAN.md`, and the top of `PostWorkSummary.md` (capture its "Follow-ups" lines as tasks too).
4. ClickUp Space **"Colonization"** (`90167219053`): Project Plan/Roadmap doc `2kz0t3mf-716`, Architecture/ADR doc `2kz0t3mf-736`, Game Design Reference `2kz0t3mf-756`, Asset Register `2kz0t3mf-796`, Session Log `2kz0t3mf-816` (latest entry).
5. **The existing board** — List `901615382059`. Filter ALL tasks (all statuses). Do **not** duplicate existing tasks; reconcile (update / tighten / add the FreeCol citation) instead.
6. The FreeCol source: `freecol/src/net/sf/freecol/` (logic), `freecol/data/rules/classic/specification.xml` (the ruleset), `freecol/data/` (other data), `freecol/doc/`.

## Method (use the Workflow tool — fan out the audit, then create tasks)

1. **Decompose FreeCol into its complete set of game systems** and run a parallel subagent per system. Each subagent reads the relevant FreeCol source + spec exhaustively, enumerates **every** mechanic / rule / data element / screen, cross-checks each against our `docs/systems/` + the existing board tasks, and returns a structured list: `{feature, FreeCol citation, status: Done|Partial|Missing, what's left}`.
2. **Adversarial completeness pass** (a critic subagent): given the union of all gap lists + a walk of `freecol/src/` package names and every top-level element type in `specification.xml`, answer *"what FreeCol systems / mechanics are missing from this audit?"* Feed anything it finds back in.
3. **Create the tasks** from the consolidated, deduped gap list (rules below).
4. **Build the master index** (deliverable below).

### Starting system checklist (verify completeness against FreeCol's actual source — NOT exhaustive)

Colonies & production (work locations, production chains, warehouse limits, auto-production, upgrades) · the full **building** set & levels (factories, custom house, schoolhouse→college→university, fort/fortress, magazine/arsenal, shipyard/drydock, newspaper…) · the full **unit** set (specialists/experts, treasure trains, wagon trains, artillery, ships, the King's REF units, roles/equipment) · **education / schools** · **founding fathers** (the COMPLETE roster, each one's exact effect) · **immigration / recruitment / religious unrest** · **Europe** (recruit/train/buy, docks, repair) · **the market** (price movement, **boycotts**, the **monarch**: tax raises, mercenary offers, war declarations, REF build-up) · **trade routes** · **custom house** (auto-export, smuggling) · **transport / cargo / high-seas** · **natives** (all nations & settlement types, **missions/Jesuit**, native trade & demands, alarm/tension, skill-teaching, conversion, land purchase, burial grounds) · **combat** (land/naval/bombardment/ambush, capture/sink, plunder, fortification, the odds model, auto-promotion/equipment) · **diplomacy** (stances, treaties, peace/war/alliance, trade agreements) · **spies/scouts, lost-city rumours, treasure & galleon transport, disasters** · **the endgame**: **Sons of Liberty → declare independence → War of Independence → Royal Expeditionary Force → foreign Intervention Force → win/lose conditions & scoring** · **the AI** (colonial economy & military, native AI, monarch AI) · **map generation** (terrain, rivers/lakes, resources, regions, starting positions) · **fog of war** · **sound & music** · remaining **UI screens** (report screens, build queue, trade reports, end/high-score screen) · **save/load** completeness · plus **QA/test** gaps (e.g. L4 goldens for UI screens, the InputTests window-leak flake) and **tech debt** (difficulty-driven constants…). And **[EPIC P8] Australia variant**.

## Task-creation rules

- Create in List `901615382059`, status **Backlog** (or `Scoping`/`In Design` if it needs a design/ADR first). Name prefix per CLAUDE.md (`[P5]`, `[P6]`, `[P7]`, `[QA]`, `[ART]`, `[ARCH]`, `[EPIC Pn]`).
- Each task's description MUST contain: (a) the **FreeCol citation** (file/class/method and/or spec element), (b) one-line plain-English of the behaviour, (c) "done means …", (d) current status (missing vs partial — and if partial, exactly what's left, linking the relevant `docs/systems/<system>.md`).
- **Phase mapping:** finishing P5 and the **P6 endgame** are the near-term, high-value gaps — make those **granular** (one work block each). Later phases (P7 polish, P8 variant) may use `[EPIC Pn]` umbrellas, BUT capture the full itemised detail in the index doc so nothing is invisible. Chris explicitly wants the COMPLETE plan visible, so err toward more granular tasks than the usual rolling-wave rule while still grouping under epics.
- Don't duplicate existing tasks; reconcile instead. Respect the locked decisions (Godot/C#, GPL-v2, FreeCol-as-spec, faithful base game first, classic/**medium**-difficulty ruleset) — don't re-litigate.

## The master index (so Chris sees EVERYTHING at a glance)

Create a new ClickUp doc page (in Project Plan doc `2kz0t3mf-716`, or a new doc in the Space) titled **"Full Development Backlog (FreeCol gap analysis)"**: a structured table/outline of **every** task you created (plus the major already-done systems for context), grouped by **system** and **phase**, each row with its FreeCol citation and a link to the board task. This is the single artefact Chris reads to review the whole plan; keep it consistent with the tasks.

## Definition of done

- Every remaining FreeCol behaviour + every known follow-up is a ClickUp task (Backlog), each with a FreeCol citation; no duplicates; existing tasks reconciled.
- The master index doc exists and covers the complete scope.
- A new **Session Log** entry (doc `2kz0t3mf-816`) summarising the methodology, the coverage (which FreeCol systems were audited + the critic-pass result), the task count created, and any **"Open questions for Chris"** (things you couldn't confirm in FreeCol — do NOT turn those into tasks).
- A `PostWorkSummary.md` entry (prepend, per CLAUDE.md) and it pasted into your final reply.

## Toolchain (only if you need to confirm current state — no code changes this session)

.NET SDK 10 is user-local and not first on PATH: in Bash, `export DOTNET_ROOT="C:\Users\Chris\.dotnet" && export PATH="C:\Users\Chris\.dotnet:$PATH"` before any `dotnet`. End any commit message with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

**Remember: planning only, FreeCol-grounded, nothing invented. If it's not in FreeCol, it's a question for Chris — not a task. Begin with the orient-yourself reads, then the audit workflow.**
