# Visual tests: in-game panel goldens (colony, Europe)

| | |
|---|---|
| **Golden files** | `colony-panel-seed424242.png`, `europe-panel.png` (in `game/tests/visual/goldens/`) |
| **Test** | `game/presentation/tests/UiPanelGoldenTests.cs` (GdUnit4, runs with the L3/L4 suite) |
| **Compare helper** | `game/presentation/tests/GoldenAssert.cs` (shared with the [map](map-goldens.md) and [menu](menu-goldens.md) goldens) |
| **Scene** | `res://scenes/main.tscn` — the live game scene; the panel opened over a deterministic game |
| **Resolution** | 1024×600 window capture (whole viewport) |
| **Fixtures** | `colony-panel`: `StartNewGame(424242)` → `FoundColony(Units[0])` → `OpenColonyPanel` (the exact fixture the L3 `ColonyPanelTests` use). `europe-panel`: a fixed Europe state injected through the public save layer — 1000 gold, a `caravel` in port, a `freeColonist` on the dock — then `OpenEuropePanel` (mirrors the L3 `EuropePanelTests` fixture). |
| **Tolerance** | per-channel Δ ≤ 8; ≤ **2%** of pixels may exceed it (the menu-golden text budget — these are text-heavy frames; font antialiasing varies more across platforms than the map tiles) |
| **Last regenerated** | 2026-06-22 — baseline (first capture of the two in-game management screens; ClickUp `86d3b4653`) |

## What these goldens verify

The two main in-game management screens render as designed — the parchment/wood framing, the Cardo serif, the section layout and the action controls:

- `colony-panel` — the colony screen for a freshly-founded colony (Jamestown): the title + the population / food / defence info line, the Sons-of-Liberty band, the isometric 3×3 surrounding-tiles view with the founding colonist on its worked tile, the buildings grid (Town Hall, Carpenter House, etc.) with staffing controls, the construction panel, and the warehouse bar. The panel paints an **opaque** parchment background, so the whole-viewport capture is stable regardless of the map drawn behind the UI layer.
- `europe-panel` — the Europe screen over the (dimmed, seeded) map: Treasury / immigration / next-recruit header, the three-slot recruitment dock with its **Recruit** buttons, the caravel in port with **Sail to New World**, the colonist on the dock with **Board caravel**, and the Buy/train dropdowns. Driven from an injected save so every dynamic section is populated deterministically.

## Why a fixed injected save for Europe

Europe is only reached by sailing a ship across the ocean over several turns; replaying that would be slow and fragile. The fixture restores a hand-written `SaveGame` (the same approach the L3 `EuropePanelTests` use) so the dock slots, the ship and the waiting colonist are pinned — the golden captures a *fully populated* Europe screen without playing a single turn.

## When they fail, a human should check

- [ ] **Intentional change** (a panel control added/renamed, a layout, label or theme tweak)? → regenerate (`GOLDEN_UPDATE=1`), eyeball the new PNGs, commit them with the change.
- [ ] **Cross-platform font antialiasing only?** A tiny (<~2%) diff on a different OS should be absorbed by the tolerance; if not, regenerate on that platform.
- [ ] **Unintended:** a theme / font / parchment-skin regression, a broken icon/art lookup, or a layout container change leaking into the panel.

## Known acceptable variation

GPU / FreeType rasterisation differences across platforms, absorbed by the 2% tolerance. These goldens were generated on Windows (Godot 4.6.3 mono, headless); a one-time regen on CI's platform may be needed if text AA drifts past tolerance (the L4 suite only runs on PRs, so this never blocks a push to `main`).
