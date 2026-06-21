using System;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// New-game world-options overlay (<c>86d3c9w9c</c> + <c>86d3c9y08</c> + the America scenario map): the player picks
/// the <b>map</b> (a procedurally generated random New World, or FreeCol's fixed America), the world <b>size</b>, how
/// much of the map is <b>land</b> (FreeCol's <c>model.option.mapWidth</c>/<c>mapHeight</c> + <c>model.option.landMass</c>)
/// and the <b>difficulty</b> level (FreeCol's five classic levels) before starting. The size/land choices only apply to
/// the random map (a fixed map sets its own dimensions), so they are disabled while America is selected. It only
/// collects the choice and hands it back to the host via the <c>onStart</c> callback — the map generation and
/// difficulty balance live in GameLogic (<see cref="MapGenerator"/> / <see cref="GameLogic.GameSession.Game.New"/>,
/// forwarded by <see cref="GameController"/>). Presentation-only (ADR-006). Built programmatically (no scene file) and
/// added as a child of the main menu like the other overlays; shares the parchment/wood look via <see cref="ColonyTheme"/>.
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
    private OptionButton _difficultyOption = null!;
    private Action<WorldSize, LandMass, DifficultyLevel, MapSource>? _onStart;

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

        _difficultyOption = new OptionButton { Name = "DifficultyOption" };
        foreach (DifficultyLevel d in DifficultyLevels.All)
        {
            _difficultyOption.AddItem(d.Name);
        }
        _difficultyOption.Selected = DifficultyLevels.DefaultIndex;
        vbox.AddChild(LabeledRow("Difficulty", _difficultyOption));

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
        _onStart?.Invoke(size, land, difficulty, source);
        EmitSignal(SignalName.Closed);
    }

    /// <summary>Greys out the world-size/land-mass dropdowns when a fixed map is chosen (its dimensions are set by the loaded grid, so those choices don't apply — see <see cref="GameLogic.GameSession.Game.New"/>).</summary>
    private void UpdateWorldSizeEnabled()
    {
        bool randomMap = MapChoices[_mapOption.Selected].Source == MapSource.Random;
        _sizeOption.Disabled = !randomMap;
        _landOption.Disabled = !randomMap;
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
