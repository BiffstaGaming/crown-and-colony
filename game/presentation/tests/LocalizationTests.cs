using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the localization foundation (86d3fq1w6): they drive Godot's real
/// <see cref="TranslationServer"/> through the <see cref="Loc"/> wrapper to prove the i18n pipeline end-to-end —
/// English by default, a working locale switch to French, English fallback for an uncovered locale, and the
/// Main Menu rendering its looked-up (not hard-coded) strings in both languages.
/// </summary>
/// <remarks>
/// The <see cref="TranslationServer"/> is process-global, so every test resets the active locale back to English in
/// a <c>finally</c> — otherwise a later suite (notably the main-menu golden) would capture French text.
/// </remarks>
[TestSuite]
[RequireGodotRuntime]
public class LocalizationTests
{
    private const string MenuScene = "res://scenes/MainMenu.tscn";

    [TestCase]
    public void Loc_DefaultsToEnglish()
    {
        Loc.SetLocale(Loc.DefaultLocale);
        try
        {
            AssertThat(Loc.CurrentLocale).IsEqual("en");
            AssertThat(Loc.T("menu.new_game")).IsEqual("New Game");
            AssertThat(Loc.T("menu.title")).IsEqual("Crown & Colony");
            AssertThat(Loc.T("menu.quit")).IsEqual("Quit");
        }
        finally
        {
            Loc.SetLocale(Loc.DefaultLocale);
        }
    }

    [TestCase]
    public void Loc_UnknownKey_ReturnsTheKeyUnchanged()
    {
        Loc.SetLocale(Loc.DefaultLocale);
        try
        {
            // A key with no entry in any table resolves to itself (Godot's TranslationServer contract) — makes a
            // missing translation obvious on screen rather than blank.
            AssertThat(Loc.T("menu.definitely_not_a_real_key")).IsEqual("menu.definitely_not_a_real_key");
        }
        finally
        {
            Loc.SetLocale(Loc.DefaultLocale);
        }
    }

    [TestCase]
    public void Loc_SwitchingToFrench_ReturnsTheFrenchStrings()
    {
        try
        {
            Loc.SetLocale("fr");
            AssertThat(Loc.CurrentLocale).IsEqual("fr");
            AssertThat(Loc.T("menu.new_game")).IsEqual("Nouvelle partie");
            AssertThat(Loc.T("menu.quit")).IsEqual("Quitter");
            AssertThat(Loc.T("menu.settings")).IsEqual("Paramètres");
        }
        finally
        {
            Loc.SetLocale(Loc.DefaultLocale); // never leave the global server in French for later suites
        }
    }

    [TestCase]
    public void Loc_UncoveredLocale_FallsBackToEnglish()
    {
        try
        {
            // German has no table; every key falls back to the project's fallback locale (en), so the app still
            // renders readable English rather than raw keys.
            Loc.SetLocale("de");
            AssertThat(Loc.T("menu.new_game")).IsEqual("New Game");
            AssertThat(Loc.T("menu.load_game")).IsEqual("Load Game");
        }
        finally
        {
            Loc.SetLocale(Loc.DefaultLocale);
        }
    }

    [TestCase]
    public async Task MainMenu_RendersLookedUpEnglishText_ByDefault()
    {
        Loc.SetLocale(Loc.DefaultLocale);
        try
        {
            ISceneRunner runner = ISceneRunner.Load(MenuScene);
            await runner.SimulateFrames(2);
            var scene = runner.Scene();

            // Byte-identical to the old hard-coded English (so the main-menu golden is unchanged), but now sourced
            // from the translation table via Loc, not baked into the scene.
            AssertThat(scene.GetNode<Label>("Panel/VBox/Title").Text).IsEqual("Crown & Colony");
            AssertThat(scene.GetNode<Label>("Panel/VBox/Subtitle").Text).IsEqual("A faithful Colonization remake");
            AssertThat(scene.GetNode<Button>("Panel/VBox/NewGameButton").Text).IsEqual("New Game");
            AssertThat(scene.GetNode<Button>("Panel/VBox/LoadGameButton").Text).IsEqual("Load Game");
            AssertThat(scene.GetNode<Button>("Panel/VBox/SettingsButton").Text).IsEqual("Settings");
            AssertThat(scene.GetNode<Button>("Panel/VBox/HelpButton").Text).IsEqual("Help");
            AssertThat(scene.GetNode<Button>("Panel/VBox/AboutButton").Text).IsEqual("About");
            AssertThat(scene.GetNode<Button>("Panel/VBox/QuitButton").Text).IsEqual("Quit");
        }
        finally
        {
            Loc.SetLocale(Loc.DefaultLocale);
        }
    }

    [TestCase]
    public async Task MainMenu_RendersFrenchText_WhenTheLocaleIsFrench()
    {
        try
        {
            Loc.SetLocale("fr");
            ISceneRunner runner = ISceneRunner.Load(MenuScene);
            await runner.SimulateFrames(2);
            var scene = runner.Scene();

            // The proof: selecting French re-renders the same menu with translated button labels.
            AssertThat(scene.GetNode<Button>("Panel/VBox/NewGameButton").Text).IsEqual("Nouvelle partie");
            AssertThat(scene.GetNode<Button>("Panel/VBox/LoadGameButton").Text).IsEqual("Charger une partie");
            AssertThat(scene.GetNode<Button>("Panel/VBox/SettingsButton").Text).IsEqual("Paramètres");
            AssertThat(scene.GetNode<Button>("Panel/VBox/QuitButton").Text).IsEqual("Quitter");
        }
        finally
        {
            Loc.SetLocale(Loc.DefaultLocale); // restore English so the golden suite captures English
        }
    }
}
