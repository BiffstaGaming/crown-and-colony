#!/usr/bin/env python3
"""Generate docs/QA-REPORT.md from real CI test results.

The QA report used to be a hand-maintained snapshot that drifted out of date
(test counts, save version, commit). This script regenerates the results section
from the actual `dotnet test` TRX files produced by the CI jobs, so the committed
report is always current after a run. See task 86d3c9y4r and CLAUDE.md's testing
strategy ("Results + screenshots: docs/QA-REPORT.md ... always-current").

Inputs (all optional — the script degrades gracefully when a TRX is absent, so it
can run on the push job with only the logic TRX, or the nightly job with all of
them):

  --logic-trx PATH   TRX from the engine-free GameLogic suite (L1 unit + L2
                     scenario + E2E, filter Category!=Soak).
  --soak-trx PATH    TRX from the L5 soak run (filter Category=Soak).
  --scene-trx PATH   TRX from the Godot scene runner (L3 interaction + L4 visual).
  --commit SHA       Commit the results were produced from (defaults to git HEAD).
  --date YYYY-MM-DD  Snapshot date (defaults to today, UTC).
  --repo-root PATH   Repo root (defaults to two levels up from this script).
  --out PATH         Output file (defaults to <repo-root>/docs/QA-REPORT.md).

TRX is the standard VSTest result XML; `dotnet test --logger "trx;LogFileName=x.trx"`
emits it. We read the <Counters> for totals and the <TestDefinitions> to classify
tests (E2E by the JourneyTests class, L3 vs L4 by the visual test class names).

The script intentionally has no third-party dependencies — only the Python 3
standard library, which is preinstalled on GitHub's ubuntu-latest runners.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field

# TRX documents are namespaced; strip the namespace so tag lookups stay simple.
_NS_RE = re.compile(r"\{.*?\}")

# Set in main() so render() can enumerate the committed goldens directory.
_REPO_ROOT_FOR_GOLDENS = "."


def _localname(tag: str) -> str:
    return _NS_RE.sub("", tag)


@dataclass
class TrxSummary:
    """Parsed totals from one TRX file."""

    total: int = 0
    passed: int = 0
    failed: int = 0
    # test-method name -> fully-qualified class name (from <TestDefinitions>)
    test_classes: dict[str, str] = field(default_factory=dict)
    present: bool = False

    @property
    def all_green(self) -> bool:
        return self.present and self.failed == 0


def parse_trx(path: str | None) -> TrxSummary:
    summary = TrxSummary()
    if not path or not os.path.isfile(path):
        return summary
    try:
        tree = ET.parse(path)
    except ET.ParseError as exc:  # corrupt/empty TRX -> treat as absent, warn
        print(f"::warning::could not parse TRX {path}: {exc}", file=sys.stderr)
        return summary

    root = tree.getroot()
    summary.present = True

    for el in root.iter():
        name = _localname(el.tag)
        if name == "Counters":
            # ResultSummary/Counters carries the authoritative totals.
            summary.total = int(el.get("total", "0"))
            summary.passed = int(el.get("passed", "0"))
            summary.failed = int(el.get("failed", "0"))
        elif name == "UnitTest":
            # <UnitTest name="..."><TestMethod className="..." name="..."/></UnitTest>
            method = None
            for child in el:
                if _localname(child.tag) == "TestMethod":
                    method = child
                    break
            if method is not None:
                m_name = method.get("name", el.get("name", ""))
                class_name = method.get("className", "")
                if m_name:
                    summary.test_classes[m_name] = class_name
    return summary


def count_e2e(logic: TrxSummary) -> int:
    """E2E journeys are the tests in the JourneyTests class (Trait Category=E2E).

    TRX does not carry xUnit traits, but every E2E test lives in JourneyTests
    (docs/TEST-PLAN.md convention), so we classify by class name.
    """
    return sum(
        1 for cls in logic.test_classes.values() if cls.endswith("JourneyTests")
    )


def split_scene_layers(scene: TrxSummary) -> tuple[int, int]:
    """Split the Godot scene suite into L3 (interaction) and L4 (visual golden).

    L4 visual tests carry "visual" or "golden" in their class name; everything
    else in the scene suite is L3 interaction.
    """
    if not scene.present:
        return (0, 0)
    l4 = 0
    for cls in scene.test_classes.values():
        low = cls.lower()
        if "visual" in low or "golden" in low:
            l4 += 1
    l3 = scene.total - l4
    return (l3, l4)


def read_save_version(repo_root: str) -> str:
    """Read SaveGame.CurrentVersion from source so the report never goes stale."""
    path = os.path.join(
        repo_root, "game", "src", "GameLogic", "Persistence", "SaveGame.cs"
    )
    try:
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
    except OSError:
        return "?"
    m = re.search(r"CurrentVersion\s*=\s*(\d+)", text)
    return m.group(1) if m else "?"


def git_head(repo_root: str) -> str:
    try:
        out = subprocess.run(
            ["git", "-C", repo_root, "rev-parse", "--short", "HEAD"],
            capture_output=True,
            text=True,
            check=True,
        )
        return out.stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return "unknown"


def _status_cell(summary: TrxSummary) -> str:
    if not summary.present:
        return "—"
    return "✅" if summary.all_green else "❌"


def render(
    *,
    logic: TrxSummary,
    soak: TrxSummary,
    scene: TrxSummary,
    save_version: str,
    commit: str,
    date: str,
    seeded: bool = False,
) -> str:
    e2e = count_e2e(logic)
    l3, l4 = split_scene_layers(scene)

    # Engine-free L1+L2 total is the non-soak GameLogic suite (E2E folded in).
    l12_total = logic.total
    l5_total = soak.total

    grand_total = l12_total + l5_total + scene.total
    overall_green = all(
        s.all_green for s in (logic, soak, scene) if s.present
    ) and (logic.present or soak.present or scene.present)
    overall_word = "all green" if overall_green else "see failures"

    logic_status = _status_cell(logic)
    soak_status = _status_cell(soak)
    scene_status = _status_cell(scene)

    lines: list[str] = []
    lines.append("# QA Report — Crown & Colony")
    lines.append("")
    lines.append(
        f"> **Auto-generated from CI** on {date} at commit `{commit}` "
        f"(save format **v{save_version}**). "
        "Do not hand-edit — this file is rewritten by "
        "[`scripts/generate-qa-report.py`](../scripts/generate-qa-report.py) "
        "on every CI run from the actual `dotnet test` results."
    )
    lines.append(
        "> This is a committed, point-in-time QA snapshot combining the "
        "**test results** (below) and the **visual goldens** (screenshots) in one "
        "place."
    )
    lines.append(
        "> **End-to-end journeys:** the connected player-journey coverage is "
        "specified in [TEST-PLAN.md](TEST-PLAN.md)."
    )
    lines.append(
        "> **Live, always-current results:** the "
        "[GitHub Actions CI runs](https://github.com/BiffstaGaming/crown-and-colony/actions) "
        "— every push is gated on these same suites."
    )
    if seeded:
        lines.append(
            "> _This committed copy was seeded from a representative results set "
            "(the L1+L2 and soak counts are real; the L3/L4 scene split is "
            "illustrative). The **nightly** workflow overwrites it with the exact "
            "figures from a live run — see [How this is generated](#how-this-is-"
            "generated) below._"
        )
    lines.append("")
    lines.append("## Test results (this snapshot)")
    lines.append("")
    lines.append(
        "| Layer | What it checks | Tooling | Count | Status | Where it runs |"
    )
    lines.append("|---|---|---|---:|:--:|---|")
    lines.append(
        "| **L1 Unit** | Rules, formulas, state transitions (engine-free) | "
        f"xUnit | included in {l12_total} | {logic_status} | every push |"
    )
    lines.append(
        "| **L2 Scenario** | Scripted multi-turn games, FreeCol cross-checks | "
        f"xUnit | included in {l12_total} | {logic_status} | every push |"
    )
    lines.append(
        "| **L1+L2 total** | (the engine-free `GameLogic` suite) | xUnit | "
        f"**{l12_total}** | {logic_status} | every push |"
    )
    lines.append(
        "| ↳ of which **E2E journeys** | Connected player journeys, "
        "milestone-asserted ([TEST-PLAN.md](TEST-PLAN.md)) | xUnit `[Trait E2E]` "
        f"| {e2e} | {logic_status} | every push |"
    )
    lines.append(
        "| **L3 Interaction** | Real scenes driven by simulated input/signals | "
        f"GdUnit4 | {l3} | {scene_status} | every push (CI) |"
    )
    lines.append(
        "| **L4 Visual** | Golden-screenshot diff of the rendered map/UI | "
        f"GdUnit4 + custom diff | {l4} | {scene_status} | every push (CI) |"
    )
    lines.append(
        "| **L5 Soak** | Multi-seed long runs + per-turn perf budget | xUnit | "
        f"{l5_total} | {soak_status} | nightly |"
    )
    lines.append(
        f"| | | | **{grand_total}** | **{overall_word}** | |"
    )
    lines.append("")
    lines.append("Reproduce locally (toolchain in [CLAUDE.md](../CLAUDE.md)):")
    lines.append("```")
    lines.append(
        "dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj "
        f'--filter "Category!=Soak"   # L1+L2 ({l12_total})'
    )
    lines.append(
        "dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj "
        f'--filter "Category=Soak"    # L5 ({l5_total})'
    )
    lines.append(
        "dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings"
        f"                 # L3+L4 ({l3 + l4}), needs GODOT_BIN"
    )
    lines.append("```")
    lines.append("")
    lines.append("## Visual goldens (committed screenshots)")
    lines.append("")
    lines.append(
        "These are the reference images the L4 suite diffs every render against. A "
        "push that changes the rendered output fails CI unless the golden is "
        "deliberately regenerated. See "
        "[map-goldens.md](visual-tests/map-goldens.md) and "
        "[menu-goldens.md](visual-tests/menu-goldens.md) for the per-golden "
        "definitions (scene, seed, tolerance, human-check list)."
    )
    lines.append("")

    goldens_dir = os.path.join("game", "tests", "visual", "goldens")
    abs_goldens = os.path.join(_REPO_ROOT_FOR_GOLDENS, goldens_dir)
    if os.path.isdir(abs_goldens):
        for fname in sorted(os.listdir(abs_goldens)):
            if not fname.lower().endswith(".png"):
                continue
            name = os.path.splitext(fname)[0]
            rel = ("../" + goldens_dir + "/" + fname).replace(os.sep, "/")
            lines.append(f"### `{name}`")
            lines.append(f"![{name} golden]({rel})")
            lines.append("")

    lines.append("## Per-system coverage")
    lines.append("")
    lines.append(
        "Each system doc carries its own five-layer verification table; that is the "
        "authoritative per-system status. Browse [docs/systems/](systems/) for the "
        "current matrix per game system."
    )
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## How this is generated")
    lines.append("")
    lines.append(
        "This file is **machine-generated** — do not hand-edit it; your edits are "
        "overwritten on the next CI run. To change its layout, edit "
        "[`scripts/generate-qa-report.py`](../scripts/generate-qa-report.py)."
    )
    lines.append("")
    lines.append(
        "- The **nightly** workflow ([`.github/workflows/nightly.yml`]"
        "(../.github/workflows/nightly.yml)) runs the full logic suite + the L5 "
        "soak run, emits `dotnet test` **TRX** result files, pulls the latest L3/L4 "
        "scene TRX from the most recent successful CI run, then runs the generator "
        "and commits the refreshed report back to the branch."
    )
    lines.append(
        "- The **push/PR** CI workflow ([`.github/workflows/ci.yml`]"
        "(../.github/workflows/ci.yml)) uploads the L1+L2 and L3+L4 TRX as "
        "artifacts (`trx-logic`, `trx-scene`) so the report can be regenerated or "
        "inspected for any run."
    )
    lines.append(
        "- Counts come straight from the TRX `<Counters>`; the **save version** is "
        "read live from `SaveGame.CurrentVersion`; **E2E** is the `JourneyTests` "
        "class count; the **L3/L4** split classifies scene tests by class name "
        "(`*Visual*`/`*Golden*` → L4). The generator degrades gracefully — a "
        "missing TRX renders that layer as `—` rather than failing."
    )
    lines.append(
        "- Regenerate locally: `python scripts/generate-qa-report.py --logic-trx "
        "<path> [--soak-trx <path>] [--scene-trx <path>]`."
    )
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--logic-trx")
    parser.add_argument("--soak-trx")
    parser.add_argument("--scene-trx")
    parser.add_argument("--commit")
    parser.add_argument("--date")
    parser.add_argument("--repo-root")
    parser.add_argument("--out")
    parser.add_argument(
        "--seeded",
        action="store_true",
        help="Mark the report as seeded from representative (not live) results.",
    )
    args = parser.parse_args(argv)

    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = args.repo_root or os.path.dirname(script_dir)

    global _REPO_ROOT_FOR_GOLDENS
    _REPO_ROOT_FOR_GOLDENS = repo_root

    logic = parse_trx(args.logic_trx)
    soak = parse_trx(args.soak_trx)
    scene = parse_trx(args.scene_trx)

    commit = args.commit or git_head(repo_root)
    date = args.date or _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%d")
    save_version = read_save_version(repo_root)

    out_path = args.out or os.path.join(repo_root, "docs", "QA-REPORT.md")

    markdown = render(
        logic=logic,
        soak=soak,
        scene=scene,
        save_version=save_version,
        commit=commit,
        date=date,
        seeded=args.seeded,
    )

    out_dir = os.path.dirname(out_path)
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(markdown)

    print(
        f"Wrote {out_path}: "
        f"L1+L2={logic.total} (E2E={count_e2e(logic)}), "
        f"soak={soak.total}, scene={scene.total} "
        f"(L3/L4 split {split_scene_layers(scene)}), "
        f"save v{save_version}, commit {commit}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
