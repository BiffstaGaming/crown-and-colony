using System.Collections.Generic;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The game's localization (i18n) entry point — a thin, testable wrapper over Godot's built-in
/// <see cref="TranslationServer"/>. It loads the project's translation tables (CSV files under
/// <c>res://localization/</c>) into the server once at startup, then resolves a translation <b>key</b>
/// (e.g. <c>"menu.new_game"</c>) to the display string for the active locale via <see cref="T"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this design.</b> Godot's <see cref="TranslationServer"/> + <c>tr()</c> is the idiomatic Godot 4
/// localization mechanism; this class is only a convenience seam so game code says <c>Loc.T("menu.quit")</c>
/// instead of poking the server directly, and so the whole pipeline can be unit-tested without a scene tree.
/// Translations are loaded <b>programmatically from the CSV</b> (rather than relying on Godot's editor import
/// step that produces <c>*.translation</c> binaries) because those binaries are gitignored and the headless
/// CI/test workflow never runs the importer — building the <see cref="Translation"/> objects in code makes the
/// strings resolve identically in the editor, the exported game, and the test runners.
/// </para>
/// <para>
/// <b>Default locale.</b> English (<c>"en"</c>) is the fallback, set in <c>project.godot</c>'s
/// <c>[internationalization]</c> section (<c>locale/fallback="en"</c> — Godot 4 has no runtime fallback setter),
/// so a key with no entry for the active locale — or an app that never switches locale — always shows the English
/// string. English is kept byte-identical to the old hard-coded text.
/// </para>
/// <para>
/// <b>Scope (foundation slice).</b> Today only the Main Menu strings are keyed (see
/// <c>res://localization/main_menu.csv</c>). Converting the remaining hard-coded English across every screen is a
/// documented follow-up; see <c>docs/systems/localization.md</c>. Add a language by appending a column to each CSV;
/// add a key by appending a row.
/// </para>
/// <para>
/// Registered as the <c>/root/Loc</c> autoload in <c>project.godot</c> so translations are loaded before the first
/// scene runs; the loading is also idempotent and lazy, so <see cref="T"/> works in bare headless test scenes that
/// have no autoloads.
/// </para>
/// </remarks>
public partial class Loc : Node
{
    /// <summary>The default / fallback locale code. English — kept byte-identical to the original hard-coded strings.</summary>
    public const string DefaultLocale = "en";

    /// <summary>The translation-table CSV files loaded into the <see cref="TranslationServer"/> (relative to <c>res://localization/</c>).</summary>
    private static readonly string[] TableFiles = { "res://localization/main_menu.csv" };

    // Whether the CSV tables have already been merged into the (process-global) TranslationServer. The server is a
    // singleton shared across every scene and test, so loading must happen exactly once regardless of how many
    // MainMenu scenes / Loc autoloads spin up during a test run.
    private static bool _loaded;

    /// <summary>Autoload hook: ensure the translation tables are loaded before any scene renders, and default to English.</summary>
    public override void _Ready() => EnsureLoaded();

    /// <summary>
    /// Translates a localization <paramref name="key"/> to the display string for the active locale (falling back to
    /// English, then to the key itself if it is unknown). Loads the translation tables on first use, so this is safe
    /// to call from bare test scenes that have no <c>/root/Loc</c> autoload.
    /// </summary>
    /// <param name="key">The translation key, e.g. <c>"menu.new_game"</c>.</param>
    /// <returns>The localized string, or <paramref name="key"/> unchanged if no translation exists.</returns>
    public static string T(string key)
    {
        EnsureLoaded();
        return TranslationServer.Translate(key);
    }

    /// <summary>
    /// Switches the active locale (e.g. <c>"fr"</c>). Keys with no entry for that locale fall back to English via the
    /// server's fallback. Presentation-only: this changes what strings render, nothing about game rules or saves.
    /// </summary>
    /// <param name="locale">The locale code to activate; pass <see cref="DefaultLocale"/> to return to English.</param>
    public static void SetLocale(string locale)
    {
        EnsureLoaded();
        TranslationServer.SetLocale(locale);
    }

    /// <summary>The active locale code (e.g. <c>"en"</c>, <c>"fr"</c>).</summary>
    public static string CurrentLocale
    {
        get
        {
            EnsureLoaded();
            return TranslationServer.GetLocale();
        }
    }

    // Merge every CSV table into the TranslationServer exactly once, set English as the fallback, and default the
    // active locale to English. Idempotent: repeated calls (from multiple autoloads / test scenes) do nothing after
    // the first. Building Translation objects by hand keeps this working headless (no editor import step).
    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;

        // The fallback locale is the project setting `internationalization/locale/fallback` ("en" — set in
        // project.godot); Godot 4 has no runtime setter for it, so TranslationServer.Translate falls back to English
        // automatically for any key a non-English table is missing.
        foreach (string path in TableFiles)
        {
            LoadCsvTable(path);
        }
        // Default to English on boot; a caller (or a locale picker) may switch afterwards.
        TranslationServer.SetLocale(DefaultLocale);
    }

    // Parse a `keys,<locale>,<locale>,…` CSV into one Translation per locale column and register each with the server.
    // Uses Godot's FileAccess.GetCsvLine so quoted fields / embedded commas are handled the same as the editor importer.
    private static void LoadCsvTable(string resPath)
    {
        using FileAccess file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushWarning($"[Loc] translation table not found: {resPath}");
            return;
        }

        string[] header = file.GetCsvLine();
        if (header.Length < 2 || header[0] != "keys")
        {
            GD.PushWarning($"[Loc] malformed translation table (expected a 'keys' header): {resPath}");
            return;
        }

        // One Translation per locale column (columns 1..n; column 0 is the key).
        var perLocale = new Translation[header.Length];
        for (int col = 1; col < header.Length; col++)
        {
            perLocale[col] = new Translation { Locale = header[col].Trim() };
        }

        while (!file.EofReached())
        {
            string[] row = file.GetCsvLine();
            if (row.Length == 0 || string.IsNullOrEmpty(row[0]))
            {
                continue; // skip blank trailing lines
            }
            string key = row[0];
            for (int col = 1; col < header.Length && col < row.Length; col++)
            {
                perLocale[col].AddMessage(key, row[col]);
            }
        }

        for (int col = 1; col < header.Length; col++)
        {
            TranslationServer.AddTranslation(perLocale[col]);
        }
    }
}
