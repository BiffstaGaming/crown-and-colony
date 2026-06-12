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

    /// <summary>
    /// New-game seed. 0 (default) = pick a random seed per game; set non-zero to
    /// pin the world (tests, bug reproduction — ADR-009).
    /// </summary>
    [Export]
    public ulong Seed { get; set; }

    private Game _game = null!;
    private ulong _currentSeed;
    private MapView _mapView = null!;
    private UnitMarker _unitMarker = null!;
    private Label _statusLabel = null!;
    private Unit? _selectedUnit;
    private string? _notice;

    public override void _Ready()
    {
        _mapView = GetNode<MapView>("MapView");
        _unitMarker = GetNode<UnitMarker>("MapView/UnitMarker");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        GetNode<Button>("UI/EndTurnButton").Pressed += OnEndTurnPressed;

        NewGame();
    }

    private void NewGame()
    {
        // Picking the seed may be non-deterministic (player convenience);
        // the game itself is fully determined by the chosen seed.
        _currentSeed = Seed != 0 ? Seed : ((ulong)GD.Randi() << 32) | GD.Randi();
        StartGame(Game.New(Ruleset.LoadClassic(), _currentSeed));
    }

    private void StartGame(Game game)
    {
        _game = game;
        _selectedUnit = null;
        _notice = null;
        GetNode<Camera2D>("Camera").Position = MapView.TileCentre(_game.Units[0].Position);
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
            case InputEventKey { Keycode: Key.N, Pressed: true, Echo: false }:
                NewGame();
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
                _notice = check.Reason;
            }
        }

        RefreshView();
    }

    private void QuickSave()
    {
        using var file = FileAccess.Open(QuickSavePath, FileAccess.ModeFlags.Write);
        file.StoreString(SaveGame.From(_game).ToJson());
        _notice = "Game saved.";
        RefreshView();
    }

    private void QuickLoad()
    {
        if (!FileAccess.FileExists(QuickSavePath))
        {
            _notice = "No quicksave found.";
            RefreshView();
            return;
        }
        using var file = FileAccess.Open(QuickSavePath, FileAccess.ModeFlags.Read);
        StartGame(SaveGame.FromJson(file.GetAsText()).Restore(Ruleset.LoadClassic()));
        _notice = "Game loaded.";
        RefreshView();
    }

    private void RefreshView()
    {
        _mapView.ShowState(_game.Map, _game.Explored);

        Unit unit = _game.Units[0];
        _unitMarker.Position = MapView.TileCentre(unit.Position);
        _unitMarker.Selected = _selectedUnit == unit;

        string terrain = _game.Map.TerrainAt(unit.Position).ShortName;
        string status =
            $"Turn {_game.Turn}   |   {unit.Type.ShortName} on {terrain}, " +
            $"movement {unit.MovementLeft}/{unit.Type.Movement}   |   seed {_currentSeed}" +
            "   |   N new map, F5 save, F9 load";
        if (_notice is not null)
        {
            status += $"   |   ⚠ {_notice}";
            _notice = null;
        }
        _statusLabel.Text = status;
    }
}
