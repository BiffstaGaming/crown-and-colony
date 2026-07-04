# System: Admin / cheat menu

*A hidden developer/cheat menu, unlocked by a secret code, in the spirit of 90s/2000s game cheats. Presentation-only (ADR-006): it never touches game state, saves, or the RNG.*

## 1. How it works (plain English)

**What it is:** a hidden **Admin menu** you unlock with a secret code, like typing a cheat code in an old game. It currently offers one cheat — **Show all map** — which reveals the whole board.

**How you open it:** while playing, press the **backtick / tilde key** (`` ` ``, top-left of the keyboard, under Esc). A small **"Enter code"** box appears. Type the code — **`eldorado`** (not case-sensitive) — and press **Unlock** (or Enter). If it's right, the **Admin menu** opens. Get it wrong and it just says "That code doesn't work."

**Once unlocked:** for the rest of that game, pressing `` ` `` again opens the Admin menu straight away (no code box). The unlock is **per game session** — start or load a fresh game and you'll need the code again (that was a deliberate choice — it keeps the cheat feeling like a cheat).

**Show all map:** flip the toggle on and the entire map is revealed — all terrain, every rival colony and native settlement, Lost City Rumours, enemy units, and the minimap fills in too. Flip it off and the normal fog of war returns. It's a **view-only** reveal: it doesn't mark tiles as "explored" in the save or tell the AI anything — it only changes what's drawn on your screen, so it can't corrupt a game and toggles freely.

**What the player sees and does:** press `` ` `` → type `eldorado` → Unlock → the Admin menu → tick **Show all map** → the fog lifts. Untick it to restore the fog.

## 2. Detailed rules

- The unlock **code** is `eldorado`, compared case-insensitively after trimming whitespace.
- The **hotkey** is the physical backtick key (`Key.Quoteleft`). It is deliberately **not** a rebindable game action — it's an obscure "cheat console" key, mirroring the era's cheat entry.
- The hotkey is ignored while a text field owns focus (so it never fires mid-typing) and while an Admin dialog is already open (no stacking).
- **Unlock scope:** session-only. The unlocked flag lives on the `GameController` instance, so it resets whenever the game scene reloads (New Game / Quit-to-menu). It is **never persisted** to `settings.cfg`.
- **Show all map** is a view toggle held on the `GameController`; it defaults **off** on every fresh controller. Toggling it redraws immediately.

**Deviations from original 1994 / FreeCol behavior:** this is a project convenience, not a faithful-Colonization feature — the original had no such menu. FreeCol has a separate developer "reveal map" debug option; ours is scoped to a per-session cheat reveal and is presentation-only.

## 3. Technical design

**Domain model:** none — there is no GameLogic type for this. It is entirely presentation.

**Key classes:**
- `GameController.Admin.cs` (partial of `GameController`) — owns the feature: the `_adminUnlocked` / `_revealAll` session flags, the `AdminCode` constant, the code box (`ShowCodePrompt`), the menu (`ShowAdminMenu`), `SetRevealAll`, and the cached `AllMapPositions()`. The dialogs are themed `AcceptDialog`s (parchment via `ColonyTheme`, auto-centred by `PopupCentered` — no custom overlay layout, so no corner-pin risk; see [[godot-overlay-modal-pattern]]).
- `GameController._UnhandledInput` — a dedicated `case` for `Key.Quoteleft` calls `OpenAdminMenu()` before the normal rebindable-hotkey dispatch.
- `GameController.RefreshView` — while `_revealAll` is on, feeds the map/river/improvement layers an **all-tiles** set instead of `_game.Explored` / `_game.CurrentlyVisible`, and the colony/settlement/rumour/unit marker loops bypass their fog checks (`_revealAll ||` / `!_revealAll &&`).
- `MiniMap.ShowState(Game, bool revealAll = false)` — the same reveal flag threaded into the minimap's terrain/entity fog checks.

**Algorithms & formulas:** none. "Show all map" substitutes the fog sets with `AllMapPositions()` (every `(x, y)` on the current map, cached), so the existing draw code renders every tile as fully visible.

**Integration points:** `SetRevealAll` calls `RefreshView` (the universal redraw hook). Nothing else consumes these flags.

**Persistence:** none. Neither the unlock nor the reveal is saved. A save records the real `Explored` set, unaffected by the cheat.

## 4. Verification

*How we know this works — the testing contract for this system (see `docs/TESTING.md` for layer definitions).*

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | No engine-free logic — the feature is presentation glue over `Game` oracles; covered at L3. | ✅ (as L3) |
| L2 Scenario | Always | Reveal is view-only (no game evolution); the underlying visibility oracles are covered by the fog-of-war L2 suite. | ✅ |
| L3 Interaction | If the system has UI | `AdminMenuTests` (5): backtick opens the code box while locked; the correct code unlocks + opens the menu; a wrong code doesn't unlock and reopens the code box; **Show all map** reveals the whole map (`MapView.ExploredTileCount` jumps to `Width×Height`) and toggles back to the real fog; backtick after unlocking opens the menu directly. | ✅ |
| L4 Visual | If the system has a screen | No committed golden (the code box / menu are transient dialogs and the reveal state is player-driven). Verified by throwaway renders during development (code box, menu, and the fogged-vs-revealed board + minimap). | ✅ (verified, not pinned) |
| L5 Soak | Covered by global suite | — (read-only, no game evolution) | — |

- **FreeCol cross-check:** not a numeric cross-check — the cheat adds no rule; "Show all map" reuses the same terrain/marker draw paths, only with the fog set widened to every tile.

## 5. Open issues / TODO

- [ ] More admin actions as needed (e.g. add gold, instant-build, reveal-and-mark-explored, spawn unit) — each a new toggle/button in `ShowAdminMenu`.
- [ ] If a persistent unlock is ever wanted, add an `AdminUnlocked` client pref to `SettingsService` (the per-session choice was deliberate).
- [ ] Localise the dialog strings when the localisation pass (`86d3fq1w6`) reaches the in-game screens.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-04 | Initial implementation + documentation: hidden Admin/cheat menu unlocked by the backtick key + the code `eldorado` (case-insensitive, **session-only**, not persisted); currently offers **Show all map**, a presentation-only reveal (map/river/improvement layers fed an all-tiles set + the colony/settlement/rumour/unit markers and the minimap bypass fog while on). New `GameController.Admin.cs` partial + a `Key.Quoteleft` case in `_UnhandledInput`; `MiniMap.ShowState` gains a `revealAll` flag; `MapView.ExploredTileCount` added as a test seam. `86d3jypd1`. +5 L3 (`AdminMenuTests`). No save/RNG/golden impact. | _(this commit)_ |
