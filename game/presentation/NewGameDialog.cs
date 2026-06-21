using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// New-game world-options overlay (<c>86d3c9w9c</c> + <c>86d3c9y08</c> + the America scenario map + <c>86d3drn5x</c>): the
/// player picks the <b>map</b> (a procedurally generated random New World, or FreeCol's fixed America), the world
/// <b>size</b>, how much of the map is <b>land</b> (FreeCol's <c>model.option.mapWidth</c>/<c>mapHeight</c> +
/// <c>model.option.landMass</c>), the <b>difficulty</b> level (FreeCol's five classic levels) and the <b>nation</b> the
/// human plays (the ruleset's selectable European powers — Dutch/French/English/Spanish — or "No nation" for the classic
/// nation-less start) before starting. The size/land choices only apply to the random map (a fixed map sets its own
/// dimensions), so they are disabled while America is selected. It collects the choices and hands the world options back
/// to the host via the <c>onStart</c> callback; the chosen nation is forwarded separately through
/// <see cref="GameController.PendingNation"/> (the human's nation is GameLogic state, not a presentation world-option —
/// ADR-006). The map generation, difficulty balance and national advantage all live in GameLogic
/// (<see cref="MapGenerator"/> / <see cref="GameLogic.GameSession.Game.New"/>, forwarded by <see cref="GameController"/>).
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

    private OptionButton _mapOption = null!;
    private OptionButton _sizeOption = null!;
    private OptionButton _landOption = null!;
    private OptionButton _landStyleOption = null!;
    private OptionButton _difficultyOption = null!;
    private OptionButton _nationOption = null!;
    private Action<WorldSize, LandMass, DifficultyLevel, MapSource>? _onStart;

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

        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        vbox.AddChild(new Label
        {
            Name = "Title",
            Text = "New Game",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

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
        // The chosen nation rides its own static into Game.New (the human's nation is GameLogic state, not a world
        // option — ADR-006). Index 0 ("No nation") maps to null → the classic nation-less human (byte-identical default).
        GameController.PendingNation = _nationByIndex[_nationOption.Selected];
        // The landmass style likewise rides a static into Game.New (it shapes only the random map). Continent (the
        // default index) → the historical byte-identical world.
        GameController.PendingLandStyle = WorldSizeOptions.LandStyles[_landStyleOption.Selected];
        _onStart?.Invoke(size, land, difficulty, source);
        EmitSignal(SignalName.Closed);
    }

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
}
