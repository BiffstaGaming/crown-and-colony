using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// New-game options overlay (<c>86d3c9w9c</c> + <c>86d3c9y08</c> + the America scenario map + <c>86d3drn5x</c> +
/// the game-options section <c>86d3drn64</c> + the options-surface widening <c>86d3e4bu0</c>): the player picks the
/// <b>scenario / variant</b> (the ruleset that defines the world — Colonial America today; the seam a future Australia
/// variant plugs into, FreeCol's "rules" dropdown on its New-game panel), the <b>map</b> (a procedurally generated
/// random New World, or FreeCol's fixed America), the world <b>size</b>, how much of the map is <b>land</b> (FreeCol's
/// <c>model.option.mapWidth</c>/<c>mapHeight</c> + <c>model.option.landMass</c>), the landmass <b>style</b>
/// (<c>model.option.landGeneratorType</c>), the <b>difficulty</b> level (FreeCol's five classic levels), the
/// <b>nation</b> the human plays (the ruleset's selectable European powers — Dutch/French/English/Spanish — or "No
/// nation" for the classic nation-less start), and the honoured base <b>game options</b>, grouped as FreeCol's
/// <c>GameOptionsDialog</c> groups them: the three alternative <b>victory conditions</b>
/// (<c>gameOptions.victoryConditions</c>: defeat the REF / be the last European / be the last human standing),
/// <b>fog of war</b> (<c>gameOptions.map</c> → <c>model.option.fogOfWar</c>) and <b>custom-house boycott smuggling</b>
/// (<c>gameOptions.colony</c> → <c>model.option.customIgnoreBoycott</c>). The size/land/style choices only apply to the
/// random map (a fixed map sets its own dimensions), so they are disabled while America is selected. It collects the
/// choices and hands the size/land/difficulty/map back to the host via the <c>onStart</c> callback; the variant, nation,
/// victory conditions, fog-of-war and custom-house options are forwarded separately through the matching
/// <c>GameController.Pending*</c> statics (<see cref="GameController.PendingVariant"/> /
/// <see cref="GameController.PendingNation"/> / <see cref="GameController.PendingVictoryConditions"/> /
/// <see cref="GameController.PendingFogOfWar"/> / <see cref="GameController.PendingCustomIgnoreBoycott"/>) — these are
/// GameLogic state / ruleset config, not presentation world-options (ADR-006). The map generation, difficulty balance,
/// national advantage and the win checks all live in GameLogic (<see cref="MapGenerator"/> /
/// <see cref="GameLogic.GameSession.Game.New"/> / <see cref="GameLogic.GameSession.Game.Winner"/>, forwarded by
/// <see cref="GameController"/>).
/// <para><b>Faithful subset — honoured options only (ADR-009).</b> Only the options our engine actually <em>reads</em>
/// are surfaced, so a shown toggle never implies a behaviour the engine ignores. Honoured-but-deliberately-omitted, and
/// why: the other <c>gameOptions.map</c>/<c>.colony</c> toggles FreeCol shows (exploration points, amphibious moves,
/// enhanced missionaries, customs-on-coast, …) are not consulted by the engine yet — surface each once it is honoured.
/// The richer <b>map-generator counts</b> FreeCol exposes (<c>model.option.riverNumber</c>/<c>mountainNumber</c>/
/// <c>rumourNumber</c>/<c>bonusNumber</c>) are <em>not</em> surfaced either: they are still hard-coded constants in
/// <see cref="MapGenerator"/> / <see cref="GameLogic.World.LostCityRumourGenerator"/>, not plumbed through
/// <see cref="GameLogic.GameSession.Game.New"/>, so a dial here would be inert — the only wired map-generator options
/// (size / land mass / landmass style) <em>are</em> surfaced. The difficulty <em>level</em> is surfaced (its own
/// dropdown); the per-level difficulty option <em>values</em> (FreeCol's editable <c>DifficultyDialog</c>) are not, as
/// the engine reads them from the level's spec, not a live override.</para>
/// Presentation-only (ADR-006). Built programmatically (no scene file) and added as a child of the main menu like the
/// other overlays; shares the parchment/wood look via <see cref="ColonyTheme"/>.
/// </summary>
public partial class NewGameDialog : Control
{
    /// <summary>Emitted when the dialog should be dismissed (Back, or after Start).</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    /// <summary>The offered map sources, in dropdown order (index ↔ <see cref="MapSource"/>); Random is the default.</summary>
    private static readonly (MapSource Source, string Label)[] MapChoices =
    {
        (MapSource.Random, "Random New World"),
        (MapSource.America, "America (fixed)"),
    };

    /// <summary>The offered rival-power counts (FreeCol's <c>NationOptions</c> roster size; 86d3fq1df), in dropdown order. Index 3 (= 3 rivals) is the classic default, pre-selected so an untouched Start is byte-identical.</summary>
    private static readonly int[] RivalCountChoices = { 0, 1, 2, 3, 4, 5, 6, 7 };

    /// <summary>The default rival-count dropdown index (3 rivals — the classic four powers minus the human's slot).</summary>
    private const int RivalCountDefaultIndex = 3;

    /// <summary>The offered start years (FreeCol <c>model.option.startingYear</c>; 86d3fq1fd), in dropdown order. Index 0 (1492) is the classic default, pre-selected so an untouched Start is byte-identical.</summary>
    private static readonly int[] StartYearChoices = { 1492, 1500, 1550, 1600, 1650, 1700 };

    /// <summary>The offered temperature bands (FreeCol <c>model.option.temperature</c>; 86d3fq13u): a label + a degrees bias on the latitude climate. Index 2 (Temperate, bias 0) is the byte-identical default.</summary>
    private static readonly (string Label, int Bias)[] TemperatureChoices =
    {
        ("Cold", -15), ("Cool", -8), ("Temperate", 0), ("Warm", 8), ("Hot", 15),
    };

    /// <summary>The offered humidity bands (FreeCol <c>model.option.humidity</c>; 86d3fq13u): a label + a 0–100 bias on the smoothed humidity. Index 2 (Normal, bias 0) is the byte-identical default.</summary>
    private static readonly (string Label, int Bias)[] HumidityChoices =
    {
        ("Very dry", -40), ("Dry", -20), ("Normal", 0), ("Wet", 20), ("Very wet", 40),
    };

    /// <summary>The default index of the no-bias climate band (Temperate / Normal) in <see cref="TemperatureChoices"/>/<see cref="HumidityChoices"/>.</summary>
    private const int ClimateDefaultIndex = 2;

    /// <summary>The offered river densities (FreeCol <c>model.option.riverNumber</c>; 86d3fq18b): a label + the riverNumber value. Index 1 (Normal = classic 15) is the byte-identical default.</summary>
    private static readonly (string Label, int Value)[] RiverNumberChoices =
    {
        ("Few", 5), ("Normal", 15), ("Many", 30),
    };

    /// <summary>The offered mountain densities (FreeCol <c>model.option.mountainNumber</c>; 86d3fq18b): a label + the mountainNumber value (higher = fewer). Index 1 (Normal = classic 10) is the byte-identical default.</summary>
    private static readonly (string Label, int Value)[] MountainNumberChoices =
    {
        ("Few", 20), ("Normal", 10), ("Many", 5),
    };

    /// <summary>The offered forest densities (FreeCol <c>model.option.forestNumber</c>; 86d3fq18b): a label + the forest chance. Index 1 (Normal = classic 0.45) is the byte-identical default.</summary>
    private static readonly (string Label, double Value)[] ForestNumberChoices =
    {
        ("Sparse", 0.25), ("Normal", 0.45), ("Dense", 0.65),
    };

    /// <summary>The offered bonus-resource densities (FreeCol <c>model.option.bonusNumber</c>; 86d3fq18b): a label + the land bonus chance. Index 1 (Normal = classic 0.10) is the byte-identical default.</summary>
    private static readonly (string Label, double Value)[] BonusNumberChoices =
    {
        ("Few", 0.05), ("Normal", 0.10), ("Many", 0.20),
    };

    /// <summary>The default index of the "Normal" (classic) entry in each map-gen-count dropdown.</summary>
    private const int MapCountDefaultIndex = 1;

    /// <summary>The offered lost-city-rumour densities (FreeCol <c>model.option.rumourNumber</c>; 86d3fq1b8): a label + the rumourNumber value (higher = fewer). Index 1 (Normal = classic 35) is the byte-identical default.</summary>
    private static readonly (string Label, int Value)[] RumourNumberChoices =
    {
        ("Few", 70), ("Normal", 35), ("Many", 18),
    };

    /// <summary>The default index of the "Normal" (classic 35) rumour-count entry.</summary>
    private const int RumourNumberDefaultIndex = 1;

    /// <summary>The offered national-advantages modes (FreeCol <c>model.option.nationalAdvantages</c>; 86d3fq0za), in dropdown order. Index 0 (Selectable) is the byte-identical default.</summary>
    private static readonly (string Label, NationalAdvantages Mode)[] AdvantagesChoices =
    {
        ("Selectable (default)", NationalAdvantages.Selectable),
        ("Fixed", NationalAdvantages.Fixed),
        ("None (off)", NationalAdvantages.None),
    };

    // ---- Forwarding statics (companions to the existing GameController.Pending* set; consumed by the new-game start
    // path, 86d3fq1df / 86d3fq1fd / 86d3fq13u / 86d3fq18b / 86d3fq1b8 / 86d3fq0za). Static so they survive the scene
    // change like the other Pending* picks. Null = no pick → the shipped default (byte-identical). Declared here on the
    // dialog (the collecting surface); the new-game host reads + clears them when it builds Game.New. ----

    /// <summary>The chosen rival-power count (null = the classic 3); threaded into <see cref="GameLogic.GameSession.Game.New"/>'s <c>foreignPowerCount</c>.</summary>
    public static int? PendingRivalCount { get; set; }

    /// <summary>The chosen start year (null = the classic 1492); applied via <see cref="Ruleset.WithStartingYear"/> on the loaded ruleset.</summary>
    public static int? PendingStartYear { get; set; }

    /// <summary>The chosen tunable map-gen counts + climate bands (null = <see cref="MapGenerationOptions.Classic"/>); threaded into <see cref="GameLogic.GameSession.Game.New"/>'s <c>mapOptions</c>.</summary>
    public static MapGenerationOptions? PendingMapOptions { get; set; }

    /// <summary>The chosen lost-city-rumour number (null = the classic 35); threaded into <see cref="GameLogic.GameSession.Game.New"/>'s <c>rumourNumber</c>.</summary>
    public static int? PendingRumourNumber { get; set; }

    /// <summary>The chosen national-advantages mode (null = <see cref="NationalAdvantages.Selectable"/>); threaded into <see cref="GameLogic.GameSession.Game.New"/>'s <c>nationalAdvantages</c>.</summary>
    public static NationalAdvantages? PendingNationalAdvantages { get; set; }

    private OptionButton _variantOption = null!;
    private OptionButton _mapOption = null!;
    private OptionButton _sizeOption = null!;
    private OptionButton _landOption = null!;
    private OptionButton _landStyleOption = null!;
    private OptionButton _difficultyOption = null!;
    private OptionButton _nationOption = null!;
    private OptionButton _rivalCountOption = null!;
    private OptionButton _startYearOption = null!;
    private OptionButton _temperatureOption = null!;
    private OptionButton _humidityOption = null!;
    private OptionButton _riverNumberOption = null!;
    private OptionButton _mountainNumberOption = null!;
    private OptionButton _forestNumberOption = null!;
    private OptionButton _bonusNumberOption = null!;
    private OptionButton _rumourNumberOption = null!;
    private OptionButton _advantagesOption = null!;
    // The alternative-victory-condition toggles (FreeCol's gameOptions.victoryConditions group): defeat the REF,
    // defeat all other Europeans, defeat all other humans. Initialised to the ruleset's parsed spec defaults so an
    // untouched Start is byte-identical (REF on, Europeans on, Humans off).
    private CheckBox _victoryRefCheck = null!;
    private CheckBox _victoryEuropeansCheck = null!;
    private CheckBox _victoryHumansCheck = null!;
    // The fog-of-war toggle (FreeCol's model.option.fogOfWar, gameOptions.map group). Initialised to the ruleset's
    // parsed spec default (classic on) so an untouched Start is byte-identical; unticking it keeps every explored tile
    // permanently visible.
    private CheckBox _fogOfWarCheck = null!;
    // The custom-house boycott-smuggling toggle (FreeCol's model.option.customIgnoreBoycott, gameOptions.colony group).
    // Initialised to the ruleset's parsed spec default (classic on) so an untouched Start is byte-identical; unticking it
    // makes a colony's custom house skip a boycotted good instead of smuggling it.
    private CheckBox _customIgnoreBoycottCheck = null!;
    private Action<WorldSize, LandMass, DifficultyLevel, MapSource>? _onStart;

    /// <summary>
    /// The variant each <see cref="_variantOption"/> dropdown row maps to, by item index — the shipped
    /// <see cref="GameVariants.All"/> in menu order. The selected one becomes the new game's ruleset source (forwarded
    /// through <see cref="GameController.PendingVariant"/>). The Classic variant is index 0 and the byte-identical
    /// default; a future variant (e.g. Australia) appears here automatically once registered (no dialog change).
    /// </summary>
    private readonly List<GameVariant> _variantByIndex = new();

    /// <summary>
    /// The nation each <see cref="_nationOption"/> dropdown row maps to, by item index. Index 0 is "No nation" (null →
    /// the classic nation-less human, byte-identical default); the rest are the ruleset's selectable European powers, in
    /// ruleset order. Populated in <see cref="_Ready"/> from the default-variant ruleset's <see cref="EuropeanNation"/>s.
    /// </summary>
    private readonly List<string?> _nationByIndex = new() { null };

    /// <summary>Builds the overlay (dim + parchment panel + the two dropdowns + Start/Back) and starts hidden.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        SetAnchorsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f), Name = "Dim" };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var panel = new PanelContainer { Name = "Panel" };
        panel.AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        centre.AddChild(panel);

        // The full set of setup dials is taller than a small window, so the options scroll inside a bounded viewport
        // (the panel keeps its parchment frame; the Start/Back buttons sit inside the scroll so they're always reachable
        // by scrolling). A generous max height keeps every row visible on a normal screen without clipping.
        var scroll = new ScrollContainer { Name = "Scroll", CustomMinimumSize = new Vector2(0, 0) };
        scroll.SetCustomMinimumSize(new Vector2(360, 560));
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);

        var vbox = new VBoxContainer { Name = "VBox", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(vbox);

        vbox.AddChild(new Label
        {
            Name = "Title",
            Text = "New Game",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // Scenario / variant picker (the ruleset that defines the world) — FreeCol's "rules" dropdown. The shipped
        // variants are read from the registry (data-driven, ADR-018), so a future variant becomes a row here for free.
        // Classic (index 0) is the byte-identical default. This is the seam a variant like Australia plugs into.
        _variantOption = new OptionButton { Name = "VariantOption" };
        foreach (GameVariant variant in GameVariants.All)
        {
            _variantOption.AddItem(variant.DisplayName);
            _variantByIndex.Add(variant);
        }
        _variantOption.Selected = VariantDefaultIndex(); // the default variant (Classic) — byte-identical
        vbox.AddChild(LabeledRow("Scenario", _variantOption));

        _mapOption = new OptionButton { Name = "MapOption" };
        foreach ((MapSource _, string label) in MapChoices)
        {
            _mapOption.AddItem(label);
        }
        _mapOption.Selected = 0; // Random — the historical default world
        _mapOption.ItemSelected += _ => UpdateWorldSizeEnabled();
        vbox.AddChild(LabeledRow("Map", _mapOption));

        _sizeOption = new OptionButton { Name = "SizeOption" };
        foreach (WorldSize s in WorldSizeOptions.Sizes)
        {
            _sizeOption.AddItem($"{s.Name} ({s.Width}×{s.Height})");
        }
        _sizeOption.Selected = WorldSizeOptions.DefaultSizeIndex;
        vbox.AddChild(LabeledRow("World size", _sizeOption));

        _landOption = new OptionButton { Name = "LandOption" };
        foreach (LandMass l in WorldSizeOptions.LandMasses)
        {
            _landOption.AddItem($"{l.Name} ({(int)Math.Round(l.Fraction * 100)}% land)");
        }
        _landOption.Selected = WorldSizeOptions.DefaultLandMassIndex;
        vbox.AddChild(LabeledRow("Land mass", _landOption));

        // Landmass style (FreeCol landGeneratorType): one continent (default), a few big islands, or many small ones.
        _landStyleOption = new OptionButton { Name = "LandStyleOption" };
        foreach (LandStyleOption s in WorldSizeOptions.LandStyles)
        {
            _landStyleOption.AddItem(s.Name);
        }
        _landStyleOption.Selected = WorldSizeOptions.DefaultLandStyleIndex; // Continent — the historical default
        vbox.AddChild(LabeledRow("Landmass", _landStyleOption));

        _difficultyOption = new OptionButton { Name = "DifficultyOption" };
        foreach (DifficultyLevel d in DifficultyLevels.All)
        {
            _difficultyOption.AddItem(d.Name);
        }
        _difficultyOption.Selected = DifficultyLevels.DefaultIndex;
        vbox.AddChild(LabeledRow("Difficulty", _difficultyOption));

        // Nation picker: the ruleset's selectable European powers, preceded by "No nation" (the byte-identical default).
        // Each chosen nation gives the human its national advantage + colony names in GameLogic (Game.New). The nations
        // are read from the default-variant ruleset (data, not hard-coded), so a variant's powers list drives the menu.
        _nationOption = new OptionButton { Name = "NationOption" };
        _nationOption.AddItem("No nation (default)"); // index 0 → _nationByIndex[0] == null
        foreach (EuropeanNation nation in SelectableNations())
        {
            _nationOption.AddItem(NationLabel(nation));
            _nationByIndex.Add(nation.Id);
        }
        _nationOption.Selected = 0; // No nation — the classic nation-less human (byte-identical default)
        vbox.AddChild(LabeledRow("Nation", _nationOption));

        // Rival European powers (FreeCol's NationOptions roster size; 86d3fq1df). 3 (the classic four minus the human's
        // slot) is the byte-identical default.
        _rivalCountOption = new OptionButton { Name = "RivalCountOption" };
        foreach (int n in RivalCountChoices)
        {
            _rivalCountOption.AddItem(n.ToString());
        }
        _rivalCountOption.Selected = RivalCountDefaultIndex;
        vbox.AddChild(LabeledRow("Rival powers", _rivalCountOption));

        // Starting year (FreeCol model.option.startingYear; 86d3fq1fd). 1492 is the historical, byte-identical default.
        _startYearOption = new OptionButton { Name = "StartYearOption" };
        foreach (int y in StartYearChoices)
        {
            _startYearOption.AddItem(y.ToString());
        }
        _startYearOption.Selected = 0; // 1492 — the classic default
        vbox.AddChild(LabeledRow("Starting year", _startYearOption));

        // Climate bands (FreeCol model.option.temperature / humidity; 86d3fq13u). Temperate / Normal (bias 0) is the
        // byte-identical default; the size/land dials disable on a fixed map, but climate only shapes the random map too.
        _temperatureOption = AddChoiceRow(vbox, "Temperature", "TemperatureOption", TemperatureChoices.Select(c => c.Label), ClimateDefaultIndex);
        _humidityOption = AddChoiceRow(vbox, "Humidity", "HumidityOption", HumidityChoices.Select(c => c.Label), ClimateDefaultIndex);

        // Map-generator counts (FreeCol model.option.riverNumber / mountainNumber / forestNumber / bonusNumber;
        // 86d3fq18b). Each "Normal" entry is the classic value, so an untouched Start is byte-identical.
        _riverNumberOption = AddChoiceRow(vbox, "Rivers", "RiverNumberOption", RiverNumberChoices.Select(c => c.Label), MapCountDefaultIndex);
        _mountainNumberOption = AddChoiceRow(vbox, "Mountains", "MountainNumberOption", MountainNumberChoices.Select(c => c.Label), MapCountDefaultIndex);
        _forestNumberOption = AddChoiceRow(vbox, "Forests", "ForestNumberOption", ForestNumberChoices.Select(c => c.Label), MapCountDefaultIndex);
        _bonusNumberOption = AddChoiceRow(vbox, "Bonus resources", "BonusNumberOption", BonusNumberChoices.Select(c => c.Label), MapCountDefaultIndex);

        // Lost-city rumours (FreeCol model.option.rumourNumber; 86d3fq1b8). "Normal" = the classic 35, byte-identical.
        _rumourNumberOption = AddChoiceRow(vbox, "Lost-city rumours", "RumourNumberOption", RumourNumberChoices.Select(c => c.Label), RumourNumberDefaultIndex);

        // National advantages (FreeCol model.option.nationalAdvantages; 86d3fq0za). Selectable (index 0) keeps each
        // nation's advantage — the byte-identical default; None turns every advantage off.
        _advantagesOption = AddChoiceRow(vbox, "National advantages", "AdvantagesOption", AdvantagesChoices.Select(c => c.Label), 0);

        // Game-options section: the base game options FreeCol shows at setup that our engine already honours, grouped as
        // FreeCol's GameOptionsDialog groups them. Each toggle starts at the ruleset's parsed spec default (read from the
        // default-variant ruleset, data-driven), so an untouched Start is byte-identical (REF on / Europeans on / Humans
        // off / fog on / custom-house smuggling on). (The other gameOptions toggles are still NOT surfaced — the engine
        // doesn't read them yet; see the dialog summary.)
        Ruleset defaults = VictoryDefaults();

        // gameOptions.victoryConditions — the three alternative win checks (read by Game.Winner).
        vbox.AddChild(SectionLabel("VictorySectionLabel", "Victory conditions"));
        _victoryRefCheck = OptionCheck("VictoryRefCheck", "Defeat the Royal Expeditionary Force", defaults.VictoryDefeatRef);
        _victoryEuropeansCheck = OptionCheck("VictoryEuropeansCheck", "Be the last European power standing", defaults.VictoryDefeatEuropeans);
        _victoryHumansCheck = OptionCheck("VictoryHumansCheck", "Be the last human power standing", defaults.VictoryDefeatHumans);
        vbox.AddChild(_victoryRefCheck);
        vbox.AddChild(_victoryEuropeansCheck);
        vbox.AddChild(_victoryHumansCheck);

        // gameOptions.map — fog of war (model.option.fogOfWar): ticked = the classic remembered-but-hidden fog; unticked
        // = explored tiles stay permanently visible. Read by Game.CurrentlyVisible/IsVisible. Spec default classic on.
        vbox.AddChild(SectionLabel("MapOptionsSectionLabel", "Map options"));
        _fogOfWarCheck = OptionCheck("FogOfWarCheck", "Fog of war", defaults.GameOptions.FogOfWar);
        vbox.AddChild(_fogOfWarCheck);

        // gameOptions.colony — custom-house boycott smuggling (model.option.customIgnoreBoycott): ticked = a colony's
        // custom house sells (smuggles) goods under boycott; unticked = it skips a boycotted good. Read by the
        // custom-house auto-sell. Spec default classic on.
        vbox.AddChild(SectionLabel("ColonyOptionsSectionLabel", "Colony options"));
        _customIgnoreBoycottCheck = OptionCheck(
            "CustomIgnoreBoycottCheck", "Custom house sells boycotted goods", defaults.GameOptions.CustomIgnoreBoycott);
        vbox.AddChild(_customIgnoreBoycottCheck);

        var start = new Button { Name = "StartButton", Text = "Start" };
        start.Pressed += OnStart;
        vbox.AddChild(start);

        var back = new Button { Name = "BackButton", Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        vbox.AddChild(back);

        UpdateWorldSizeEnabled(); // size/land start enabled (Random is the default map)
        Hide();
    }

    /// <summary>Opens the dialog. <paramref name="onStart"/> receives the chosen size + land amount + difficulty + map source; the dialog then closes.</summary>
    public void Open(Action<WorldSize, LandMass, DifficultyLevel, MapSource> onStart)
    {
        _onStart = onStart;
        Show();
    }

    private void OnStart()
    {
        MapSource source = MapChoices[_mapOption.Selected].Source;
        WorldSize size = WorldSizeOptions.Sizes[_sizeOption.Selected];
        LandMass land = WorldSizeOptions.LandMasses[_landOption.Selected];
        DifficultyLevel difficulty = DifficultyLevels.All[_difficultyOption.Selected];
        // The chosen scenario / variant rides its own static into Game.New (it selects which ruleset the game loads —
        // ADR-018). The default variant (Classic, index 0) → the byte-identical default world.
        GameController.PendingVariant = _variantByIndex[_variantOption.Selected];
        // The chosen nation rides its own static into Game.New (the human's nation is GameLogic state, not a world
        // option — ADR-006). Index 0 ("No nation") maps to null → the classic nation-less human (byte-identical default).
        GameController.PendingNation = _nationByIndex[_nationOption.Selected];
        // The landmass style likewise rides a static into Game.New (it shapes only the random map). Continent (the
        // default index) → the historical byte-identical world.
        GameController.PendingLandStyle = WorldSizeOptions.LandStyles[_landStyleOption.Selected];
        // The chosen victory conditions ride their own static into Game.New, applied to the loaded ruleset (a config
        // override of which win checks fire — ADR-006). The picks left at their spec defaults make this a no-op
        // override (byte-identical); we still forward them so the host always knows the player's explicit choice.
        GameController.PendingVictoryConditions =
            (_victoryRefCheck.ButtonPressed, _victoryEuropeansCheck.ButtonPressed, _victoryHumansCheck.ButtonPressed);
        // The fog-of-war toggle rides its own static into Game.New, applied to the loaded ruleset (a config override of
        // how visibility is derived — ADR-006). Left at the spec default (on) it is a no-op override (byte-identical).
        GameController.PendingFogOfWar = _fogOfWarCheck.ButtonPressed;
        // The custom-house boycott-smuggling toggle rides its own static into Game.New, applied to the loaded ruleset (a
        // config override of whether a custom house smuggles — ADR-006). Left at the spec default (on) it is a no-op.
        GameController.PendingCustomIgnoreBoycott = _customIgnoreBoycottCheck.ButtonPressed;

        // The new setup dials (86d3fq1df / 86d3fq1fd / 86d3fq13u / 86d3fq18b / 86d3fq1b8 / 86d3fq0za) ride their own
        // statics into the new-game host, which threads them into Game.New. Each is the GameLogic value the chosen
        // dropdown index maps to; left at its default index every one is the byte-identical shipped value (ADR-009).
        PendingRivalCount = RivalCountChoices[_rivalCountOption.Selected];
        PendingStartYear = StartYearChoices[_startYearOption.Selected];
        PendingMapOptions = new MapGenerationOptions(
            MountainNumber: MountainNumberChoices[_mountainNumberOption.Selected].Value,
            RiverNumber: RiverNumberChoices[_riverNumberOption.Selected].Value,
            ForestChance: ForestNumberChoices[_forestNumberOption.Selected].Value,
            LandBonusChance: BonusNumberChoices[_bonusNumberOption.Selected].Value,
            TemperatureBias: TemperatureChoices[_temperatureOption.Selected].Bias,
            HumidityBias: HumidityChoices[_humidityOption.Selected].Bias);
        PendingRumourNumber = RumourNumberChoices[_rumourNumberOption.Selected].Value;
        PendingNationalAdvantages = AdvantagesChoices[_advantagesOption.Selected].Mode;

        _onStart?.Invoke(size, land, difficulty, source);
        EmitSignal(SignalName.Closed);
    }

    /// <summary>The dropdown index of the default variant (<see cref="GameVariants.Default"/>) in <see cref="GameVariants.All"/> — pre-selected so an untouched Start loads the byte-identical Classic world.</summary>
    private static int VariantDefaultIndex()
    {
        for (int i = 0; i < GameVariants.All.Count; i++)
        {
            if (GameVariants.All[i].Id == GameVariants.Default.Id)
            {
                return i;
            }
        }
        return 0; // the registry always contains the default; the guard keeps the dropdown valid regardless
    }

    /// <summary>
    /// The ruleset whose parsed <c>gameOptions.victoryConditions</c> defaults seed the victory checkboxes (read from
    /// the default-variant ruleset — data-driven, so a variant's own spec defaults drive the dialog). On any load
    /// failure the classic spec defaults (REF on / Europeans on / Humans off) are used via the embedded fallback, so
    /// the dialog never crashes the main menu.
    /// </summary>
    private static Ruleset VictoryDefaults()
    {
        try
        {
            return GameVariants.Default.LoadRuleset();
        }
        catch (Exception e)
        {
            GD.PushWarning($"NewGameDialog: could not load victory-condition defaults ({e.Message}); using classic spec defaults.");
            return Ruleset.LoadClassic();
        }
    }

    /// <summary>Builds one game-option checkbox, pre-ticked to the ruleset's parsed spec default for that option (so an untouched Start is byte-identical, ADR-009).</summary>
    private static CheckBox OptionCheck(string name, string label, bool defaultOn) =>
        new() { Name = name, Text = label, ButtonPressed = defaultOn };

    /// <summary>Builds one centred section header (the FreeCol option-group label — "Victory conditions" / "Map options" / "Colony options").</summary>
    private static Label SectionLabel(string name, string text) =>
        new() { Name = name, Text = text, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>
    /// The selectable, non-REF European nations the human may choose, in ruleset order, read from the default-variant
    /// ruleset (data-driven — a variant's own powers list drives the menu). On any load failure (a broken/missing
    /// ruleset) the menu degrades to "No nation" only, so the dialog never crashes the main menu.
    /// </summary>
    private static IEnumerable<EuropeanNation> SelectableNations()
    {
        try
        {
            return GameVariants.Default.LoadRuleset().EuropeanNations
                .Where(n => n.Selectable && !n.IsRef)
                .ToList();
        }
        catch (Exception e)
        {
            GD.PushWarning($"NewGameDialog: could not load selectable nations ({e.Message}); offering 'No nation' only.");
            return Array.Empty<EuropeanNation>();
        }
    }

    /// <summary>The dropdown label for a nation — its ruleset display name (e.g. <c>Dutch</c>) and its short advantage tag (e.g. <c>trade</c>) so the player can tell the advantages apart at a glance.</summary>
    private static string NationLabel(EuropeanNation nation) =>
        $"{nation.DisplayName} ({nation.NationType.ShortName})";

    /// <summary>Greys out the world-size/land-mass dropdowns when a fixed map is chosen (its dimensions are set by the loaded grid, so those choices don't apply — see <see cref="GameLogic.GameSession.Game.New"/>).</summary>
    private void UpdateWorldSizeEnabled()
    {
        bool randomMap = MapChoices[_mapOption.Selected].Source == MapSource.Random;
        _sizeOption.Disabled = !randomMap;
        _landOption.Disabled = !randomMap;
        _landStyleOption.Disabled = !randomMap; // a fixed map's land shape is loaded, so the style doesn't apply
    }

    private static HBoxContainer LabeledRow(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(control);
        return row;
    }

    /// <summary>Builds a labelled <see cref="OptionButton"/> row from string item labels, selects <paramref name="defaultIndex"/>, adds the row to <paramref name="parent"/>, and returns the option button (so the caller keeps a field reference for read-back at Start).</summary>
    private static OptionButton AddChoiceRow(BoxContainer parent, string label, string name, IEnumerable<string> items, int defaultIndex)
    {
        var option = new OptionButton { Name = name };
        foreach (string item in items)
        {
            option.AddItem(item);
        }
        option.Selected = defaultIndex;
        parent.AddChild(LabeledRow(label, option));
        return option;
    }
}
