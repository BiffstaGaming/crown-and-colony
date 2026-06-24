# System: Release & Build (export presets, release CI, app icon)

| | |
|---|---|
| **Status** | In development (added 2026-06-24) |
| **Last verified** | 2026-06-24 @ release-scaffold (`86d3dzdtp`/`86d3f0w6e`/`86d3f0w8q`/`86d3f0w4w`) |
| **Code** | `game/export_presets.cfg`, `.github/workflows/release.yml`, `game/project.godot` (`[application]`), `game/icon.svg`; version source `game/src/GameLogic/App/AppInfo.cs` + `<Version>` in `game/src/GameLogic/GameLogic.csproj` & `game/CrownAndColony.csproj` |
| **Tests** | No automated layer (config + workflow + asset). Validated by: `dotnet build` clean, headless `--import` parses `project.godot` + `export_presets.cfg`, preset name resolves in `--export-release` (template-missing error only). Real binaries verified on CI / a machine with export templates. |
| **FreeCol reference** | `build.xml` / Ant `dist` targets (FreeCol packages per-platform zips + installers); `FreeCol.getVersion()` (version string baked into the build) |
| **Related systems** | [about.md](about.md) (shows `AppInfo.Version` — same version source), [settings.md](settings.md) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

This system is the plumbing that turns the project's source code into **downloadable
game files** for Windows, Linux and macOS, plus the **branded icon** the game shows in
the taskbar/dock and in its window. None of it changes how the game plays — it is about
shipping the game and how it looks before you've even started a session.

**The rules, in plain words:**
- The game can be exported for **three platforms** — Windows, Linux and macOS — using
  three saved "export presets". Each preset knows the output file name and the icon.
- Exports are produced **automatically** when a version is tagged (e.g. `v0.1.0`): a
  GitHub Action builds all three, names the files with the version, and attaches them to
  a **GitHub Release** you can download from.
- The game's **name** is "Crown & Colony" (this is also the window title), and its
  **icon** is an original crown-and-ship picture made for this project.
- The version number shown everywhere (`0.1.0` today) comes from **one place** in the
  code, so the About screen, the file names and the release all agree.

**Worked example:**
> A maintainer pushes a tag `v0.2.0`. GitHub spins up the *Release* workflow: it installs
> Godot 4.6.3 (.NET) and the export templates, builds the C# solution, then exports
> `Crown-and-Colony.exe` (+ data), the Linux binary, and a macOS app. It zips each one as
> `Crown-and-Colony-0.2.0-windows.zip` etc. and publishes a *Crown & Colony 0.2.0* release
> with those three zips attached. A player downloads the Windows zip, unzips it, and sees
> the game with a crown-and-ship icon titled "Crown & Colony".

**What the player/maintainer sees and does:** a maintainer tags a release (or clicks
"Run workflow"); a player downloads a platform zip from the Releases page. The window and
taskbar show the crown-and-ship icon and the title "Crown & Colony".

## 2. Detailed rules

*Audience: designers/testers.*

### Export presets (`game/export_presets.cfg`)

| Preset (name) | Godot platform | Output (`export_path`) | Notable options |
|---|---|---|---|
| `windows_desktop` | `Windows Desktop` | `build/windows/Crown-and-Colony.exe` | `binary_format/architecture="x86_64"`, `embed_pck=false`, console wrapper on, `application/icon=res://icon.svg`, file/product version `0.1.0.0`, company/product name "Crown & Colony", GPL-v2 copyright |
| `linuxbsd` | `Linux/X11` | `build/linux/Crown-and-Colony.x86_64` | `architecture="x86_64"`, `embed_pck=false`, console wrapper on |
| `macos` | `macOS` | `build/macos/Crown-and-Colony.zip` | `architecture="universal"`, `application/bundle_identifier="org.crownandcolony.game"`, `application/name="Crown & Colony"`, `short_version`/`version` `0.1.0`, `application/icon=res://icon.svg`, codesign **not** configured (unsigned), `app_category="Games"` |

- **PCK packaging:** `binary_format/embed_pck=false` for all three — the `.pck` ships
  beside the executable rather than embedded. Rationale: for a .NET (mono) project the
  embedded-PCK path has historically been fragile (the .NET assemblies are packed
  separately from `embed_pck`); a side-by-side `.pck` is the safe, standard default. The
  release workflow zips the whole platform folder, so the loose `.pck` (and the macOS
  `.app`) travels with the binary regardless.
- **Texture formats:** S3TC + BPTC on (desktop GPUs); ETC/ETC2 off (mobile-only).
- **Icons:** all presets point at `res://icon.svg`. Godot synthesises the platform icon
  formats at export time — Windows `.ico` and macOS `.icns` are generated from the SVG;
  **no `.ico`/`.icns` is committed and no extra tooling is required.**
- **Versions:** the preset version fields (`0.1.0` / `0.1.0.0`) mirror `AppInfo.Version`
  and the `<Version>` in both csprojs. **Bump all of them together** when releasing.
- **Signing/notarization:** Windows codesign disabled; macOS codesign present-but-empty
  (produces an **unsigned** `.app` — fine for source distribution; real signing needs an
  Apple Developer cert + notarization, deferred). Secrets, if ever added, live in
  `.godot/export_credentials.cfg` (gitignored), never in this file.

### Release workflow (`.github/workflows/release.yml`)

| Aspect | Value |
|---|---|
| **Trigger** | push of a tag matching `v[0-9]+.[0-9]+.[0-9]+` (e.g. `v0.1.0`), **or** manual `workflow_dispatch` |
| **Permissions** | `contents: write` (to create the Release + upload assets) |
| **Runner / engine** | `ubuntu-latest`; Godot `4.6.3-stable` mono + **export templates** (cached); .NET SDK 10 — Godot install mirrors `ci.yml`'s scene-tests job |
| **Version source** | tag → strip leading `v`; manual run → read `<Version>` from `GameLogic.csproj` (same value `AppInfo.Version` compiles in) |
| **Steps** | build solution (Release) → `--headless --import` → `--export-release` for each of the 3 presets → zip each platform folder version-stamped (`Crown-and-Colony-<ver>-<platform>.zip`) → upload as workflow artifact → (tag only) publish a GitHub Release with the 3 zips via `softprops/action-gh-release@v2` |
| **macOS note** | the macOS export already emits a `.zip` (zipped `.app`); it is renamed to the version-stamped name rather than re-zipped |
| **Dispatch vs tag** | a manual run produces and uploads the artifacts but **skips** the Release step (no tag to attach to) |

**Export-templates path (verified):** the editor looks for templates under
`<godot-data>/export_templates/4.6.3.stable.mono/` (version with **dots**, `.stable.mono`
suffix). The workflow installs the `.tpz` pack into exactly that directory. This was
verified locally — a Windows export with templates *absent* fails with precisely
"No export template found at .../4.6.3.stable.mono/windows_release_x86_64.exe", confirming
both the preset name resolves and the expected template directory.

### Project / window / icon (`game/project.godot`)

| Key | Value | Effect |
|---|---|---|
| `application/config/name` | `"Crown & Colony"` | product name **and** window title |
| `application/config/icon` | `res://icon.svg` | editor + default runtime icon |
| `application/boot_splash/show_image` | `false` | no splash image shown at startup |
| `application/boot_splash/bg_color` | `Color(0.04,0.06,0.1,1)` | splash/clear colour = brand navy (matches `default_clear_color`) |

**Deviations from original 1994 / FreeCol behavior:** none gameplay-relevant. FreeCol
distributes via Ant + an installer (IzPack) and a single cross-platform jar; we use
Godot's native per-platform export + a GitHub Release, which is the idiomatic Godot/CI
approach. The boot splash is deliberately **disabled** (no branded splash asset yet) —
the brand navy fill is shown instead.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Single version source.** `<Version>` in `GameLogic.csproj` is the canonical release
number; `AppInfo.Version` reads it at runtime (About screen), `CrownAndColony.csproj`
mirrors it for the Godot assembly's metadata, the export presets hard-code it for the
binary file metadata, and the release workflow re-derives it (tag or csproj). All four
must be bumped together — this is the one drift risk in the system.

**Why three presets, three names.** Godot exports per preset; the headless
`--export-release "<preset name>" <path>` call references the preset **by its `name`**,
so the names (`windows_desktop`, `linuxbsd`, `macos`) are an API the workflow depends on —
do not rename a preset without updating `release.yml`.

**Icon (`game/icon.svg`).** Original hand-authored SVG (crown + ship on navy). Godot
imports it as a texture (`icon.svg.import`) and rasterises platform icon formats on
export. Recorded as project-original work in `CREDITS.md`.

**Integration points:** consumes the build output of `CrownAndColony.slnx`; the
`config/name` feeds both branding and the window title; the version chain ties into
`about.md`. No game-state, no persistence — pure build/release infrastructure.

**Persistence:** none (no save-game involvement).

## 4. Verification

*What could and could not be verified in this environment.* The export **templates** for
Godot 4.6.3 are **not installed in the dev sandbox**, so actual exported binaries cannot
be produced or verified here. Final binary verification runs on **CI** (the release
workflow) or on a machine with the 4.6.3 export templates installed.

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a | version accessor covered by `AppInfoTests` (see about.md) | — |
| L2 Scenario | n/a (no game logic) | — | — |
| L3 Interaction | n/a (no in-game UI) | — | — |
| L4 Visual | n/a | — | — |
| L5 Soak | n/a | — | — |

**Manual / tooling checks performed (2026-06-24):**
- `dotnet build game/CrownAndColony.slnx --configuration Release` → **clean (0 warnings, 0 errors)**.
- Headless `--import` on the real Godot 4.6.3 mono build → **exit 0** (so `project.godot`
  and `export_presets.cfg` parse; the new `icon.svg` reimports cleanly).
- `--export-release "windows_desktop"` → fails **only** with "No export template found at
  .../4.6.3.stable.mono/..." (confirms the preset is valid and resolved by name; the only
  blocker is the absent template, as expected).
- `release.yml` parsed with a YAML loader → **valid**. `icon.svg` parsed as XML → **valid**.

**Could NOT verify here:** real exported `.exe`/Linux binary/macOS `.app` (no export
templates); the GitHub Release publish path (needs a tag push on CI); macOS code-signing
(no Apple cert — intentionally unsigned).

## 5. Open issues / TODO

- [ ] First real run of `release.yml` (tag or dispatch) to confirm the three exports
      produce binaries and the Release publishes — do this on CI / a templates-equipped box.
- [ ] macOS signing + notarization (needs an Apple Developer account); currently ships an
      unsigned `.app`.
- [ ] Optional: a branded boot-splash image (currently disabled; brand-navy fill only).
- [ ] Trademark: "Colonization" — keep the released product name as "Crown & Colony"
      (already the case); revisit if/when distribution scales.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-24 | Release/build scaffolding: new `export_presets.cfg` (windows_desktop / linuxbsd / macos, .NET, side-by-side PCK, version 0.1.0, `org.crownandcolony.game` bundle id); new `release.yml` (tag/`workflow_dispatch` → install Godot 4.6.3 + export templates → export 3 presets → version-stamped zips → GitHub Release); original branded `icon.svg` (crown + ship) wired into `project.godot`; boot splash disabled (brand-navy fill); window title confirmed "Crown & Colony"; CREDITS.md icon-attribution row. Build clean; presets/project parse; export-binary verification deferred to CI (no export templates in sandbox). | release-scaffold |
