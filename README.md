# Crown & Colony

A from-scratch, faithful remake of *Sid Meier's Colonization* (1994), built natively in
**Godot 4** with **C#**. Take a European power to the New World: found colonies, work the
land into production chains, trade with Europe and the native nations, win your people's
hearts toward liberty through the Sons of Liberty and Founding Fathers — then declare
independence and defend it against the Royal Expeditionary Force.

The classic ruleset is reproduced from [**FreeCol**](https://github.com/FreeCol/freecol),
the GPL-licensed clean-room Java reimplementation of Colonization, used here as the
**reference specification** — including reusing its XML rule/data formats. FreeCol is *not*
a runtime dependency: Crown & Colony is a full native Godot engine with no Java server.

This project contains **no code, assets, or data from the original Sid Meier games.**

![The colony view — Jamestown, with production buildings, the worked map, and the colony panel](game/tests/visual/goldens/colony-panel-seed424242.png)

![The Europe view — recruit and train colonists, buy goods, and sail to the New World](game/tests/visual/goldens/europe-panel.png)

*(Both images are committed visual-regression goldens from the L4 test suite — they are the
actual rendered game, diffed on every CI run.)*

## Status

**The base game is substantially complete and playable.** All the core Colonization
systems are implemented, documented, and covered by automated tests — see the per-system
docs in [`docs/systems/`](docs/systems/) (37 systems and counting). The current test suite
is **~2000 engine-free logic/scenario tests plus soak runs, all green** (see
[`docs/QA-REPORT.md`](docs/QA-REPORT.md) and the
[GitHub Actions CI](https://github.com/BiffstaGaming/crown-and-colony/actions)).

What remains is release-readiness polish (packaged builds, asset credits, final balancing)
and, after that, a planned **variant scenario set in Australia** — which the data-driven
ruleset is designed to make a content change rather than a code change.

### Feature highlights

- **Map & exploration** — procedurally generated New World, terrain/resources, rivers and
  tile improvements, fog of war, lost-city rumours, and region discovery.
- **Colonies** — settlement founding, building construction, per-colonist work identity and
  on-the-job expert upgrades, schools/education, custom house, Sons of Liberty membership.
- **Economy & trade** — the Europe market with price movements, transport and trade routes,
  treasure trains, and the tax/boycott pressure from the Crown.
- **People & politics** — immigration and recruitment, the Founding Fathers, monarchy
  demands, and difficulty-scaled rules.
- **Natives** — native settlements, alarm/tension, trade, missions, scouting, and warfare.
- **Diplomacy & war** — combat, foreign-power diplomacy and negotiation, the Royal
  Expeditionary Force, the Declaration of Independence, and the War of Independence.
- **Game shell** — main menu, in-game and pause menus, settings, save/load, scoring, and
  multiple game modes — plus sound, music, and combat animation.

## Architecture

- **Engine-independent game logic in pure C#** (`GameLogic`) — fully unit-testable and
  headless, with no Godot dependency. This is where the rules live.
- **Godot as a thin presentation layer** — scenes and nodes render state and forward input;
  they hold no game rules.
- **Deterministic by design** — all randomness flows through a seeded, injectable RNG, so
  games are reproducible and AI turns don't desync (the soak suite asserts byte-stability).

## Building and running

### Toolchain

- **.NET SDK 10** — the .NET edition of Godot needs an SDK to compile the C#.
- **Godot 4.6+ (.NET / Mono edition)** — Crown & Colony is developed against **Godot 4.6.3
  .NET**.

> **On Chris's dev machine** the toolchain is at non-standard paths and is **not** first on
> `PATH`. The user-local SDK lives at `C:\Users\Chris\.dotnet` (set `DOTNET_ROOT` and prepend
> it to `PATH`, or dot-source [`scripts/dev-env.ps1`](scripts/dev-env.ps1), before any
> `dotnet` command), and Godot 4.6.3 .NET lives at
> `C:\Users\Chris\Tools\Godot_v4.6.3-stable_mono_win64\`. The `dotnet` that resolves by
> default is runtime-only and cannot build.

### Build

```sh
dotnet build game/CrownAndColony.slnx        # build the Godot project + logic library
```

### Run

The simplest path on a configured dev machine is the PowerShell launcher, which loads the
toolchain and starts the game in one step:

```powershell
.\scripts\run-game.ps1            # launch (logs visible in the terminal)
.\scripts\run-game.ps1 -Build     # clean-build the C# first, then launch
.\scripts\run-game.ps1 -NoConsole # windowed run, no attached log console
```

Or launch directly through Godot (its .NET build compiles the C# on launch, so no separate
build is needed just to play):

```sh
godot --path game                 # open in the editor, or play the project
```

### Test

The engine-free logic and scenario suites (L1 + L2) run with plain `dotnet test` — no Godot
required:

```sh
dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category!=Soak"   # L1 + L2
dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter "Category=Soak"     # L5 soak
```

The scene-level interaction and visual-regression suites (L3 + L4) run through the Godot
runtime and need `GODOT_BIN` set:

```sh
dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings                  # L3 + L4
```

The full five-layer testing strategy is documented in [`docs/TESTING.md`](docs/TESTING.md);
end-to-end player journeys are in [`docs/TEST-PLAN.md`](docs/TEST-PLAN.md).

> Note: `GameLogic.Tests` is deliberately **not** part of `CrownAndColony.slnx` — CI's
> GdUnit action tests the solution and must only see the Godot-runtime suites.

## License

Crown & Colony is licensed under the **GNU General Public License v2** — see
[`LICENSE`](LICENSE). Because the ruleset is derived from FreeCol (GPL v2), the whole project
is GPL v2.

Art, music, and sound assets carry their own GPL-compatible licenses (GPL v2, CC0, CC BY,
OFL, etc.). Every asset's source and license — including attribution requirements — is
recorded in **[`CREDITS.md`](CREDITS.md)**.

## A note on the name

*Colonization* is a trademark of its original publisher; **Crown & Colony** is this project's
own name and is not affiliated with, endorsed by, or derived from the original game. The 1994
and 2008 Sid Meier titles' code, assets, and data are off-limits and are **not** used here —
the gameplay is reproduced independently from the FreeCol specification.
