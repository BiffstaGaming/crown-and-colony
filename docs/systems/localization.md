# System: Localization (i18n / multi-language strings)

| | |
|---|---|
| **Status** | In development (foundation + Main Menu proof slice shipped — the full string sweep is a follow-up) |
| **Last verified** | 2026-07-03 @ i18n foundation (`86d3fq1w6`) |
| **Code** | `game/presentation/Loc.cs` (the wrapper/autoload), `game/localization/*.csv` (translation tables), `game/project.godot` `[internationalization]`, `game/presentation/MainMenu.cs` (the proof slice) |
| **Tests** | `game/presentation/tests/LocalizationTests.cs` (L3 — mechanism + MainMenu render, EN + FR) |
| **FreeCol reference** | `freecol/data/strings/FreeColMessages*.properties` (~40 locales, Java `key=value` format; consulted for the French menu wording — `newAction.name`, `openAction.name`, `preferencesAction.name`, `quitAction.name`, `aboutAction.name`) |
| **Related systems** | [main-menu.md](main-menu.md) (the converted screen), [settings.md](settings.md) (where a language picker would live) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

The game is built to be shown in more than one language. Instead of the on-screen wording being baked into the code, each piece of text has a short **key** (like `menu.new_game`), and the actual words for each language live in a simple spreadsheet-style table. When the game needs to show a button, it looks up the key in the table for the currently-selected language and displays that. English is the default; if a language is missing a particular line, the game shows the English one so nothing ever comes up blank.

Right now only the **Main Menu** (the title screen) has been converted as a working proof — its title, subtitle and all six buttons come from the table. A second language, **French**, is included for those same items, so we can flip the game to French and watch the menu re-render in French. Converting the rest of the game's screens is a planned follow-up; the foundation is in place so it can be done a screen at a time.

**The rules, in plain words:**
- Every converted piece of text has a **key**; the words live in a translation table, one column per language.
- **English is the default and the safety net** — a missing translation falls back to English, never to a blank or a raw key.
- Selecting a language changes only what words are shown. It changes nothing about the game's rules, saves, or randomness.
- Adding a language = add a column to the table(s). Adding a new piece of text = add a row (a key) and use it in code.

**Worked example:**
> The Main Menu asks for the text of the "New Game" button. In English it looks up `menu.new_game` and shows **New Game**. Switch the game to French and the same lookup returns **Nouvelle partie**, so the button now reads that instead — with no code change, just a different language selected.

**What the player sees and does:** today, nothing changes in normal play — the default English text is identical to before. A future language-picker (a follow-up) would let the player choose their language; for now the pipeline is proven by tests and the project's default locale.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| App starts | The `Loc` autoload merges every `res://localization/*.csv` table into Godot's `TranslationServer` and sets the active locale to English (`en`). |
| `Loc.T("menu.new_game")`, locale `en` | Returns `New Game` (the `en` column). |
| `Loc.T("menu.new_game")`, locale `fr` | Returns `Nouvelle partie` (the `fr` column). |
| `Loc.T(key)` for a key with **no `fr` entry**, locale `fr` | Falls back to the `en` column (project fallback locale `en`). |
| `Loc.T(key)` for a **completely unknown** key | Returns the key string unchanged (so a missing translation is obvious on screen). |
| Locale set to one with **no table at all** (e.g. `de`) | Every key falls back to English. |
| Main Menu opens (any locale) | `MainMenu.ApplyLocalizedText()` sets the title, subtitle and six buttons from `menu.*` keys via `Loc.T`. |

**Locales covered today:** `en` (default, complete for the menu keys) and `fr` (complete for the menu keys). All eight `menu.*` keys are present in both.

**Deviations from original 1994 / FreeCol behavior:** Colonization (1994) shipped as separate single-language retail builds; FreeCol carries ~40 locales in Java `.properties` files. We use Godot's own `TranslationServer` with CSV tables instead of porting FreeCol's `.properties` files, and we ship a runtime-switchable locale rather than a per-build language. Only the Main Menu is keyed so far — this is a foundation, not a full translation.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Mechanism chosen — and why.** Godot 4's built-in `TranslationServer` + `tr()`/`TranslationServer.Translate()` with CSV translation tables is the idiomatic Godot localization path, so we use it. `game/presentation/Loc.cs` is a thin static wrapper (`Loc.T(key)`, `Loc.SetLocale(locale)`, `Loc.CurrentLocale`) plus an autoload that loads the tables once at boot. The wrapper exists so call-sites read `Loc.T("menu.quit")` and so the whole pipeline is testable through one seam.

**Why load the CSV programmatically (not via imported `*.translation`).** Godot's editor importer turns each `res://localization/*.csv` into per-locale `*.translation` binaries, but those binaries are **gitignored** (`.gitignore`: `*.translation`) and the headless CI/test workflow never runs the importer. So `Loc` builds the `Translation` objects itself: it opens the raw CSV with `FileAccess.GetCsvLine()` (same quoting rules as the importer), creates one `Godot.Translation` per locale column, `AddMessage(key, value)` for each row, and `TranslationServer.AddTranslation(...)`. This makes the strings resolve identically in the editor, an exported build, and every test runner — no import step required. (The committed `main_menu.csv.import` just lets the editor recognise the file; the generated `.translation` sidecars remain gitignored and unused at runtime.)

**Default / fallback locale.** `project.godot` `[internationalization] locale/fallback="en"`. Godot 4 has **no** runtime fallback setter (there is no `TranslationServer.SetFallbackLocale` in 4.6), so the fallback is configured only through that project setting; `Translate` then falls back to English automatically for any key a non-English table lacks. `Loc._Ready()`/`EnsureLoaded()` also calls `TranslationServer.SetLocale("en")` so the app boots English.

**Idempotent, lazy loading.** The `TranslationServer` is process-global (shared across scenes and the whole test run). `Loc.EnsureLoaded()` guards on a static `_loaded` flag so the tables merge exactly once no matter how many `MainMenu` scenes or `Loc` autoloads spin up; and every public method calls `EnsureLoaded()` first, so `Loc.T(...)` works even in a bare headless test scene that has no `/root/Loc` autoload.

**The proof slice.** `MainMenu.ApplyLocalizedText()` (called at the end of `_Ready()`) sets `Title`/`Subtitle` and the six buttons from `menu.*` keys. The English values in `main_menu.csv` are byte-identical to the strings previously baked into `MainMenu.tscn`, so the `main-menu` L4 golden is **unchanged**. (The `.tscn` still carries the old English text as inert defaults; the script overwrites them from the table on load.)

**Autoload order.** `Loc` is registered first in `[autoload]` (before `Settings`/`Sound`/`Music`) so translations are available before any other service or scene runs.

**Translation table format** (`game/localization/main_menu.csv`):

```
keys,en,fr
menu.new_game,New Game,Nouvelle partie
…
```

Column 0 is the key; each further column is a locale. Add a language → add a column; add a string → add a row.

**Persistence:** none. Locale selection is presentation state; if/when a language picker is added it belongs on `SettingsService`/`SettingsModel` as a client preference (ADR-006, **no save-version bump**), exactly like the other client prefs.

**Integration points:** `project.godot` (`[autoload] Loc`, `[internationalization] locale/fallback`), `MainMenu.cs` (`ApplyLocalizedText`).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a | The mechanism wraps Godot's `TranslationServer` (a `Godot.Node`), so it can't run in the engine-free L1 (xUnit) project; it is covered at L3 instead. | — |
| L2 Scenario | n/a (no game logic) | — | — |
| L3 Interaction | Yes | `LocalizationTests` — default English; unknown key returns the key; switch to French returns French; uncovered locale (`de`) falls back to English; **MainMenu renders looked-up English by default**; **MainMenu renders French when the locale is French**. | ✅ (6/6) |
| L4 Visual | Yes (touches a screen) | `main-menu` golden (`MenuGoldenTests`) — **unchanged**: English is byte-identical, so converting to keys must not move a pixel. Verified green. | ✅ |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** the French menu wording follows FreeCol's `FreeColMessages_fr.properties` (Nouveau/Ouvrir/Préférences/Quitter/À propos), adapted to our fuller button labels ("Nouvelle partie", "Charger une partie").
- **Full L3 run:** 350/350 green (was 344; +6 here). Full L1/L2: 2704/2704 green.

## 5. Open issues / TODO (the follow-up sweep)

- [ ] **Full string sweep** — convert the remaining hard-coded English across every screen (colony, Europe, HUD, dialogs, notices, tooltips) to `Loc.T` keys, table by table. This is the large follow-up; the foundation here makes it incremental.
- [ ] **Language picker UI** — a Settings dropdown that calls `Loc.SetLocale` and persists the choice on `SettingsService`/`SettingsModel` (client pref, no save bump). Optional; the pipeline is already proven.
- [ ] **More locales / import path from FreeCol** — decide whether to mechanically convert FreeCol's `.properties` locales into our CSV tables (licence-compatible, GPL v2) once the key set stabilises.
- [ ] **Runtime re-translation on live locale change** — currently a screen reads keys when it builds; a live language switch while a screen is open would need each screen to re-run its `ApplyLocalizedText` (or use Godot's auto-translate) — revisit when the picker lands.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-03 | **i18n foundation + Main Menu proof slice** (`86d3fq1w6`): new `Loc` wrapper/autoload over Godot's `TranslationServer`, loading CSV tables (`game/localization/main_menu.csv`, `en`+`fr`) programmatically; `project.godot` `[internationalization] locale/fallback="en"` + `Loc` autoload; `MainMenu` title/subtitle/buttons now render from `menu.*` keys (English byte-identical → `main-menu` golden unchanged). +6 L3 (`LocalizationTests`). Full string sweep + language picker are documented follow-ups. | `86d3fq1w6` |
