# Documentation Standards — Crown & Colony

This file defines what documentation exists, where it lives, who it's written for, and when it must be updated. It is binding: **a change is not done until its documentation is updated** (see CLAUDE.md).

## The doc map — what lives where

| Location | Contents | Why there |
|---|---|---|
| **Repo `docs/systems/`** | One doc per game system (map, units, colonies, economy, combat…) — dual-audience, from `TEMPLATE-game-system.md` | Must change in the same commit as code → can never drift |
| **Repo `docs/modules/`** | One doc per code module/namespace — technical audience, from `TEMPLATE-code-module.md` | Same-commit rule |
| **Repo `docs/templates/`** | The templates themselves | Versioned with the standards |
| **Repo — inline** | C# XML doc comments (`///`) on all public types/members | Lives closest to the code; IDE & generated reference |
| **Repo `CREDITS.md`** (root) | Consolidated asset license/attribution for GPL-v2 distribution — aggregates the per-folder `PROVENANCE.md` files | Ships with the distributed game; mirrors the Asset Register (doc 05) |
| **ClickUp doc 01** | Project Plan & Roadmap | Project-level, not code-coupled |
| **ClickUp doc 02** | Architecture Decision Records | Decision history |
| **ClickUp doc 03** | Game Design Reference + index of repo system docs | High-level design; links down to `docs/systems/` |
| **ClickUp doc 04** | Godot Knowledge Base | Engine practice, research findings |
| **ClickUp doc 05** | Asset Register | License compliance |
| **ClickUp doc 06** | Session Log | Cross-session memory for Claude |

Rule of thumb: **if it describes code behavior, it lives in the repo. If it describes the project, it lives in ClickUp.**

## The two audiences — every system doc serves both

1. **Plain English** — readable by anyone (Chris in a hurry, a player, a contributor's first day). No jargon, no class names. Worked examples with real numbers.
2. **Technical** — exact rules, formulas, data sources, class responsibilities, integration points, and the tests that prove it. Written so a future Claude session (or developer) can modify the system confidently without reading the whole codebase.

The plain section always comes first in the document. If you can't explain the system plainly, the design isn't understood yet.

## When documentation MUST be updated (the no-drift rule)

In the **same commit** as the code change:
- New game logic / system → create `docs/systems/<system>.md` from the template.
- Changed behavior, rule, or formula → update the system doc's affected sections **in both layers** + add a changelog row.
- New/changed public API → XML doc comments + module doc.
- New module/namespace → `docs/modules/<module>.md`.

At the **end of each work session** (ClickUp):
- Session Log: new entry (what/state/next).
- Roadmap: tick/adjust current phase status.
- ADR doc: any decision made.
- Asset Register: any asset added (before the asset is committed, not after).

## Definition of done (every feature)

- [ ] Tests written and passing (behavior verified, not just compiling)
- [ ] System doc updated — plain-English layer
- [ ] System doc updated — technical layer (incl. Verification section lists the new tests)
- [ ] XML doc comments on public API
- [ ] Changelog row in the system doc (date, change, commit)
- [ ] ClickUp updated if plan/decisions/assets were touched

## Style

- Markdown, sentence-case headings, one file per system/module, kebab-case filenames (`colony-production.md`).
- Plain layer: short sentences, concrete examples, no acronyms without expansion.
- Technical layer: link code as `path/File.cs` + method name; cite FreeCol references as `freecol/<path>` (file + element/method).
- Mark intentional deviations from original/FreeCol behavior explicitly — these are the most valuable lines in the whole doc set.
- Every system doc carries `Last verified: <date> @ <commit>` in its header; update it whenever you confirm doc-vs-code accuracy.
