# Crown & Colony

An open-source, turn-based colonization strategy game built with **Godot 4** and **C#**, inspired by the classic *Sid Meier's Colonization* (1994).

Guide a European power to the New World: found colonies, work the land, build production chains, trade with Europe and native nations, win your people's hearts toward liberty — then declare independence and defend it.

A planned scenario variant will reframe the game around the colonization of Australia.

## Status

**Pre-alpha / project inception.** Nothing playable yet — toolchain and architecture are being set up.

## Approach

- Game rules follow the classic Colonization ruleset, using [FreeCol](https://github.com/FreeCol/freecol) (the GPL clean-room Java reimplementation) as the reference specification — including reusing its XML rule/data formats where practical.
- Game logic is engine-independent C# (fully unit-testable, headless), with Godot as a thin presentation layer.
- All behavior is verified by automated tests: unit tests plus headless turn-simulation scenarios.

This project contains **no code or assets from the original Sid Meier games**.

## Building from source

Requirements: [.NET SDK](https://dotnet.microsoft.com/download) 8.0+ and [Godot 4.6+ (.NET edition)](https://godotengine.org/download).

```
dotnet build game/CrownAndColony.slnx     # build everything
dotnet test  game/CrownAndColony.slnx     # run the logic test suite
godot --path game                          # open in Godot (editor or play)
```

## License

GPL v2 — see [LICENSE](LICENSE). Art/audio assets may carry their own GPL-compatible licenses (CC0/CC BY etc.); see the asset credits once assets land.
