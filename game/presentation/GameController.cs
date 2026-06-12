using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Root of the main scene: owns the <see cref="Game"/> instance and translates
/// input into game commands. All rules live in GameLogic (ADR-006) — this class
/// only forwards commands and reflects state.
/// </summary>
public partial class GameController : Node2D
{
    private const string QuickSavePath = "user://quicksave.json";

    /// <summary>New-game seed; exported so scenes/tests can pin it (ADR-009).</summary>
    [Export]
    public ulong Seed { get; set; } = 2026;

    private Game _game = null!;
    private MapView _mapView = null!;
    private UnitMarker _unitMarker = null!;
    private Label _statusLabel = null!;
    private Unit? _selectedUnit;

    public override void _Ready()
    {
        _mapView = GetNode<MapView>("MapView");
        _unitMarker = GetNode<UnitMarker>("MapView/UnitMarker");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        GetNode<Button>("UI/EndTurnButton").Pressed += OnEndTurnPressed;

        StartGame(Game.New(Ruleset.LoadClassic(), Seed));
    }

    private void StartGame(Game game)
    {
        _game = game;
        _selectedUnit = null;
        _mapView.ShowMap(_game.Map);
        GetNode<Camera2D>("Camera").Position = MapView.TileCentre(
            new Position(_game.Map.Width / 2, _game.Map.Height / 2));
        RefreshView();
    }

    private void OnEndTurnPressed()
    {
        _game.EndTurn();
        RefreshView();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                HandleTileClick(MapView.TileAt(_mapView.GetLocalMousePosition()));
                break;
            case InputEventKey { Keycode: Key.F5, Pressed: true, Echo: false }:
                QuickSave();
                break;
            case InputEventKey { Keycode: Key.F9, Pressed: true, Echo: false }:
                QuickLoad();
                break;
        }
    }

    private void HandleTileClick(Position tile)
    {
        if (!_game.Map.InBounds(tile))
        {
            return;
        }

        // Click a unit: select it. Click elsewhere with a selection: try to move.
        Unit? unitOnTile = _game.Units.FirstOrDefault(u => u.Position == tile);
        if (unitOnTile is not null)
        {
            _selectedUnit = unitOnTile;
        }
        else if (_selectedUnit is not null)
        {
            MoveCheck check = _game.CheckMove(_selectedUnit, tile);
            if (check.Allowed)
            {
                _game.MoveUnit(_selectedUnit, tile);
            }
            else
            {
                GD.Print($"Move rejected: {check.Reason}");
            }
        }

        RefreshView();
    }

    private void QuickSave()
    {
        using var file = FileAccess.Open(QuickSavePath, FileAccess.ModeFlags.Write);
        file.StoreString(SaveGame.From(_game).ToJson());
        GD.Print("Game saved.");
        RefreshView();
    }

    private void QuickLoad()
    {
        if (!FileAccess.FileExists(QuickSavePath))
        {
            GD.Print("No quicksave found.");
            return;
        }
        using var file = FileAccess.Open(QuickSavePath, FileAccess.ModeFlags.Read);
        StartGame(SaveGame.FromJson(file.GetAsText()).Restore(Ruleset.LoadClassic()));
        GD.Print("Game loaded.");
    }

    private void RefreshView()
    {
        Unit unit = _game.Units[0];
        _unitMarker.Position = MapView.TileCentre(unit.Position);
        _unitMarker.Selected = _selectedUnit == unit;

        string terrain = _game.Map.TerrainAt(unit.Position).ShortName;
        _statusLabel.Text =
            $"Turn {_game.Turn}   |   Unit on {terrain}, movement {unit.MovementLeft}/{Unit.BaseMovementPoints}" +
            "   |   Click unit to select, click tile to move. F5 save, F9 load.";
    }
}
