using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
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
    private GameVariant _variant = GameVariants.Default;
    private MapView _mapView = null!;
    private Node2D _unitLayer = null!;
    private Node2D _colonyLayer = null!;
    private Node2D _nativeLayer = null!;
    private Label _statusLabel = null!;
    private PanelContainer _colonyPanel = null!;
    private PanelContainer _europePanel = null!;
    private Unit? _selectedUnit;
    private string? _notice;

    public override void _Ready()
    {
        _mapView = GetNode<MapView>("MapView");
        _unitLayer = GetNode<Node2D>("MapView/UnitLayer");
        _colonyLayer = GetNode<Node2D>("MapView/ColonyLayer");
        _nativeLayer = GetNode<Node2D>("MapView/NativeLayer");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _colonyPanel = GetNode<PanelContainer>("UI/ColonyPanel");
        _europePanel = GetNode<PanelContainer>("UI/EuropePanel");
        GetNode<Button>("UI/EndTurnButton").Pressed += OnEndTurnPressed;
        GetNode<Button>("UI/EuropeButton").Pressed += OpenEuropePanel;
        GetNode<Button>("UI/ColonyPanel/VBox/CloseButton").Pressed += () => _colonyPanel.Hide();
        GetNode<Button>("UI/EuropePanel/VBox/CloseButton").Pressed += () => _europePanel.Hide();

        NewGame();
    }

    private void NewGame()
    {
        // Picking the seed may be non-deterministic (player convenience);
        // the game itself is fully determined by the chosen seed.
        StartNewGame(Seed != 0 ? Seed : ((ulong)GD.Randi() << 32) | GD.Randi());
    }

    /// <summary>Starts a new game from an explicit seed (tests, visual goldens — ADR-009).</summary>
    public void StartNewGame(ulong seed)
    {
        _currentSeed = seed;
        StartGame(Game.New(_variant.LoadRuleset(), _currentSeed));
    }

    private void StartGame(Game game)
    {
        _game = game;
        _selectedUnit = null;
        _notice = null;
        // Centre on the player's first on-map unit; after founding the only colony the player may have
        // none on the map (and the unit list now also holds native braves), so fall back to a colony,
        // then the map centre.
        Position focus = _game.PlayerUnits.FirstOrDefault(u => u.IsOnMap)?.Position
            ?? _game.Colonies.FirstOrDefault(c => c.OwnerId == _game.HumanPlayer.PlayerId)?.Position
            ?? new Position(_game.Map.Width / 2, _game.Map.Height / 2);
        GetNode<Camera2D>("Camera").Position = MapView.TileCentre(focus);
        RefreshView();
    }

    private void OnEndTurnPressed()
    {
        _game.EndTurn();
        if (_game.CombatNotices.Count > 0)
        {
            // Surface raids the human suffered during the AI phase (no return value to read, unlike a
            // player-initiated attack). Notices are in deterministic by-brave order; show them together.
            _notice = string.Join("   ", _game.CombatNotices.Select(FormatCombatNotice));
        }
        RefreshView();
    }

    /// <summary>Turns a native raid on the human into a status-bar message, from the human defender's point of view.</summary>
    private string FormatCombatNotice(CombatNotice notice)
    {
        string id = notice.AttackerNationId;
        string shortName = id[(id.LastIndexOf('.') + 1)..];
        string nation = char.ToUpperInvariant(shortName[0]) + shortName[1..];
        string unit = _game.Ruleset.Unit(notice.DefenderUnitTypeId).ShortName;
        bool nativeWon = notice.Outcome is CombatResult.GreatWin or CombatResult.Win;
        return nativeWon
            ? $"⚔ The {nation} raided your {unit} at ({notice.Position.X},{notice.Position.Y})!"
            : $"Your {unit} fought off a {nation} raid at ({notice.Position.X},{notice.Position.Y}).";
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
            case InputEventKey { Keycode: Key.B, Pressed: true, Echo: false }:
                FoundColony();
                break;
            case InputEventKey { Keycode: Key.E, Pressed: true, Echo: false }:
                OpenEuropePanel();
                break;
        }
    }

    private void HandleTileClick(Position tile)
    {
        if (!_game.Map.InBounds(tile))
        {
            return;
        }

        // Click a unit: select it. Click elsewhere with a selection: try to move. Only the human's own
        // on-map units are clickable (natives and foreign powers are not the player's to command).
        Unit? unitOnTile = _game.PlayerUnits.FirstOrDefault(u => u.IsOnMap && u.Position == tile);
        if (unitOnTile is not null)
        {
            _selectedUnit = unitOnTile;
        }
        else if (_game.ColonyAt(tile) is { } colony && colony.OwnerId == _game.HumanPlayer.PlayerId)
        {
            OpenColonyPanel(colony); // only the human's own colonies are the player's to manage
        }
        else if (_selectedUnit is not null)
        {
            // An adjacent enemy on the clicked tile → attack it; otherwise try to move there.
            if (_game.NativeSettlementAt(tile) is not null)
            {
                AttackSettlementAt(tile);
            }
            else if (_game.Units.Any(u => u.IsOnMap && u.Position == tile))
            {
                AttackUnitAt(tile); // any on-map unit here is an enemy (the player's own were handled above)
            }
            else
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
        }

        RefreshView();
    }

    /// <summary>Attacks the enemy unit on an adjacent tile with the selected unit, reporting the outcome.</summary>
    private void AttackUnitAt(Position tile)
    {
        MoveCheck check = _game.CheckAttack(_selectedUnit!, tile);
        if (!check.Allowed)
        {
            _notice = check.Reason;
            return;
        }
        string who = _selectedUnit!.Type.ShortName;
        CombatResult result = _game.Attack(_selectedUnit, tile);
        _selectedUnit = null; // the attack ends the unit's turn (and may demote/destroy it)
        _notice = result is CombatResult.GreatWin or CombatResult.Win
            ? $"Your {who} won the battle."
            : $"Your {who} was beaten back.";
    }

    /// <summary>Assaults the native settlement on an adjacent tile with the selected unit, reporting the outcome.</summary>
    private void AttackSettlementAt(Position tile)
    {
        MoveCheck check = _game.CheckAttackSettlement(_selectedUnit!, tile);
        if (!check.Allowed)
        {
            _notice = check.Reason;
            return;
        }
        CombatResult result = _game.AttackSettlement(_selectedUnit!, tile);
        _selectedUnit = null;
        _notice = result is CombatResult.GreatWin or CombatResult.Win
            ? "The native settlement was sacked!"
            : "Your assault was repelled.";
    }

    private void FoundColony()
    {
        if (_selectedUnit is null)
        {
            _notice = "Select a unit first (click it), then press B to build.";
            RefreshView();
            return;
        }

        MoveCheck check = _game.CheckFoundColony(_selectedUnit);
        if (!check.Allowed)
        {
            _notice = check.Reason;
        }
        else
        {
            var colony = _game.FoundColony(_selectedUnit);
            _selectedUnit = null;
            _notice = $"{colony.Name} founded!";
        }
        RefreshView();
    }

    /// <summary>Opens the interactive colony screen. Public so scene tests can drive it directly.</summary>
    public void OpenColonyPanel(Colony colony) =>
        ((ColonyPanel)_colonyPanel).Open(_game, colony, RefreshView);

    /// <summary>Opens the Europe screen (dock, recruits, ships in port). Public so scene tests can drive it.</summary>
    public void OpenEuropePanel() =>
        ((EuropePanel)_europePanel).Open(_game, RefreshView);

    private void QuickSave()
    {
        using var file = FileAccess.Open(QuickSavePath, FileAccess.ModeFlags.Write);
        file.StoreString(SaveGame.From(_game, _variant.Id).ToJson());
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
        SaveGame save = SaveGame.FromJson(file.GetAsText());
        // Reload under the save's own variant so its ruleset matches (ADR-018).
        _variant = GameVariants.Resolve(save.Variant);
        StartGame(save.Restore(_variant.LoadRuleset()));
        _notice = "Game loaded.";
        RefreshView();
    }

    private void RefreshView()
    {
        _mapView.ShowState(_game.Map, _game.Explored, _game.CurrentlyVisible);
        SyncColonyMarkers();
        SyncNativeMarkers();
        SyncUnitMarkers();

        Unit? unit = _game.PlayerUnits.FirstOrDefault(u => u.IsOnMap); // the human's first on-map unit, for the status line
        int inEurope = _game.UnitsInEurope.Count();
        string subject = unit is not null
            ? $"{unit.Type.ShortName} on {_game.Map.TerrainAt(unit.Position).ShortName}, " +
              $"movement {unit.MovementLeft}/{unit.Type.Movement}"
            : _game.Colonies.LastOrDefault(c => c.OwnerId == _game.HumanPlayer.PlayerId) is { } ownColony
                ? $"{ownColony.Name} (pop {ownColony.Population})"
                : inEurope > 0
                    ? $"{inEurope} in Europe — press E"
                    : "no units";
        string status =
            $"Turn {_game.Turn}   |   {subject}   |   seed {_currentSeed}" +
            "   |   B build colony, N new map, F5 save, F9 load";
        if (_notice is not null)
        {
            status += $"   |   ⚠ {_notice}";
            _notice = null;
        }
        _statusLabel.Text = status;
    }

    /// <summary>One marker per colony, reconciled each refresh (colony count is tiny).</summary>
    private void SyncColonyMarkers()
    {
        foreach (Node child in _colonyLayer.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var colony in _game.Colonies)
        {
            // Only colonies on explored tiles are shown — a foreign power's colony stays hidden under the
            // human's fog until discovered (the human's own colonies always reveal their own surroundings).
            if (!_game.IsExplored(colony.Position))
            {
                continue;
            }
            var marker = new ColonyMarker
            {
                Position = MapView.TileCentre(colony.Position),
                ColonyName = colony.Name,
            };
            _colonyLayer.AddChild(marker);
        }
    }

    /// <summary>
    /// One marker per discovered native settlement, reconciled each refresh. Only
    /// settlements on explored tiles are shown — undiscovered ones stay hidden under
    /// the fog of war (until the explored-vs-visible upgrade, a settlement once seen
    /// stays drawn).
    /// </summary>
    private void SyncNativeMarkers()
    {
        foreach (Node child in _nativeLayer.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var settlement in _game.NativeSettlements)
        {
            if (!_game.IsExplored(settlement.Position))
            {
                continue;
            }
            string shortName = settlement.NationTypeId[(settlement.NationTypeId.LastIndexOf('.') + 1)..];
            string caption = char.ToUpperInvariant(shortName[0]) + shortName[1..];
            var marker = new NativeSettlementMarker
            {
                Position = MapView.TileCentre(settlement.Position),
                SettlementTypeId = settlement.SettlementTypeId,
                Caption = settlement.IsCapital ? $"{caption} ★" : caption,
            };
            _nativeLayer.AddChild(marker);
        }
    }

    /// <summary>Owner-ring colour for a native brave (earthy red-brown).</summary>
    private static readonly Color NativeUnitColor = new(0.78f, 0.36f, 0.20f);

    /// <summary>Owner-ring colour for a rival whose nation has no colour in the ruleset (a plain rival red).</summary>
    private static readonly Color FallbackRivalColor = new(0.85f, 0.20f, 0.20f);

    /// <summary>
    /// One <see cref="UnitMarker"/> per on-map unit the human can see, reconciled each refresh (unit counts are
    /// tiny — same free-all-then-rebuild pattern as the colony/native layers): the human's own units always;
    /// every non-human unit (a foreign power's or a native brave) only while its tile is in live sight
    /// (<see cref="GameLogic.GameSession.Game.IsVisible"/> — units move, so this uses live visibility, not the
    /// remembered/explored fog). Non-human units get an owner-coloured ring; the human's own render without one.
    /// Presentation-only (ADR-006): reads game state, never mutates it.
    /// </summary>
    private void SyncUnitMarkers()
    {
        foreach (Node child in _unitLayer.GetChildren())
        {
            child.QueueFree();
        }
        foreach (Unit unit in _game.Units)
        {
            if (!unit.IsOnMap)
            {
                continue; // aboard a ship or in Europe — nothing to draw on the map
            }
            bool human = !unit.IsNative && unit.OwnerId == _game.HumanPlayer.PlayerId;
            if (!human && !_game.IsVisible(unit.Position))
            {
                continue; // a rival or brave is drawn only while in the human's line of sight (fog)
            }
            var marker = new UnitMarker
            {
                Position = MapView.TileCentre(unit.Position),
                Selected = _selectedUnit == unit,
                OwnerColor = human ? default : OwnerColorOf(unit),
            };
            marker.SetUnitType(unit.Type.ShortName);
            _unitLayer.AddChild(marker);
        }
    }

    /// <summary>
    /// The owner-ring colour for a non-human unit: a foreign power's nation colour from the ruleset, falling
    /// back to a plain rival red; a native brave uses <see cref="NativeUnitColor"/>. The human's own units pass
    /// <c>default</c> (transparent → no ring) at the call site, so they render exactly as before.
    /// </summary>
    private Color OwnerColorOf(Unit unit)
    {
        if (unit.IsNative)
        {
            return NativeUnitColor;
        }
        string? nationId = _game.Players.FirstOrDefault(p => p.PlayerId == unit.OwnerId)?.NationId;
        EuropeanNation? nation = nationId is null
            ? null
            : _game.Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId);
        return nation?.Color is { } hex
            ? Color.FromString(NormalizeHex(hex), FallbackRivalColor)
            : FallbackRivalColor;
    }

    /// <summary>Normalises a ruleset colour to Godot's canonical <c>#rrggbb</c> form (e.g. <c>0xff9d3c</c> → <c>#ff9d3c</c>); a parse miss still falls back via <see cref="Color.FromString"/>'s default.</summary>
    private static string NormalizeHex(string hex)
    {
        string bare = hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X') ? hex[2..] : hex.TrimStart('#');
        return "#" + bare;
    }
}
