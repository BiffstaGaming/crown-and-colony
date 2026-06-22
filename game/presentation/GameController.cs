using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Audio;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
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

    /// <summary>Directory holding the named save slots (created on first save). The save/load dialog reads it.</summary>
    public const string SavesDir = "user://saves";

    /// <summary>
    /// Set by the main menu's Load Game before it switches to this scene: the save to load on boot instead of starting
    /// a new game. Consumed (and cleared) in <see cref="_Ready"/>. Static because it must survive the scene change.
    /// </summary>
    public static string? PendingLoadPath { get; set; }

    /// <summary>
    /// Set by the main menu's New Game options before it switches to this scene: the world size + land amount for the
    /// new game (null = the shipped default — so existing entry points and tests get the historical 36×24/45% world).
    /// Consumed (and cleared) in <see cref="NewGame"/>. Static because it must survive the scene change, like
    /// <see cref="PendingLoadPath"/>.
    /// </summary>
    public static WorldSize? PendingWorldSize { get; set; }

    /// <summary>Companion to <see cref="PendingWorldSize"/>: the chosen land amount (null = shipped default).</summary>
    public static LandMass? PendingLandMass { get; set; }

    /// <summary>Companion to <see cref="PendingWorldSize"/>: the chosen difficulty level (null = the shipped default, Conquistador/medium). Consumed (and cleared) in <see cref="NewGame"/>. (86d3c9y08)</summary>
    public static DifficultyLevel? PendingDifficulty { get; set; }

    /// <summary>Companion to <see cref="PendingWorldSize"/>: the chosen map source (null = the shipped default, <see cref="MapSource.Random"/> — a procedurally generated New World). <see cref="MapSource.America"/> loads FreeCol's fixed America terrain instead (its dimensions override the world-size choice). Consumed (and cleared) in <see cref="NewGame"/>.</summary>
    public static MapSource? PendingMapSource { get; set; }

    /// <summary>Companion to <see cref="PendingWorldSize"/>: the chosen landmass style (null = the shipped default, <see cref="LandStyle.Continent"/>). <see cref="LandStyle.Archipelago"/>/<see cref="LandStyle.Islands"/> shape the random map into separate masses; ignored on a fixed <see cref="PendingMapSource"/>. Consumed (and cleared) in <see cref="NewGame"/>.</summary>
    public static LandStyleOption? PendingLandStyle { get; set; }

    /// <summary>
    /// Companion to <see cref="PendingWorldSize"/>: the European nation the human chose at New Game (e.g.
    /// <c>model.nation.dutch</c>; null = no pick → the classic nation-less human, byte-identical default). Set by
    /// <see cref="NewGameDialog"/>'s Start and consumed (and cleared) in <see cref="NewGame"/>, where it is forwarded to
    /// <see cref="Game.New"/> as the human's nation so the human plays with that nation's advantage + colony names
    /// (86d3drn5x). Static because it must survive the scene change, like <see cref="PendingWorldSize"/>.
    /// </summary>
    public static string? PendingNation { get; set; }

    /// <summary>
    /// Companion to <see cref="PendingWorldSize"/>: the three alternative <b>victory conditions</b> the human chose at
    /// New Game (defeat-REF / defeat-all-Europeans / defeat-all-humans — FreeCol's <c>gameOptions.victoryConditions</c>
    /// group, surfaced by <see cref="NewGameDialog"/>). <b>Null = no pick → the ruleset's parsed spec defaults</b> (REF
    /// on, Europeans on, Humans off — so a default new game is byte-identical, ADR-009). Set by the dialog's Start and
    /// consumed (and cleared) in <see cref="NewGame"/>, where it is applied to the freshly-loaded ruleset via
    /// <see cref="Ruleset.WithVictoryConditions"/> so <see cref="Game.Winner"/> evaluates exactly the enabled checks.
    /// Static because it must survive the scene change, like <see cref="PendingWorldSize"/>. <b>Session-only</b> — the
    /// override is not written to the save (a reload re-derives the conditions from the variant's spec; persisting them
    /// would bump the save format, 86d3drn64).
    /// </summary>
    public static (bool DefeatRef, bool DefeatEuropeans, bool DefeatHumans)? PendingVictoryConditions { get; set; }

    /// <summary>
    /// Companion to <see cref="PendingWorldSize"/>: the <b>fog-of-war</b> toggle the human chose at New Game (FreeCol's
    /// <c>model.option.fogOfWar</c>, surfaced by <see cref="NewGameDialog"/>). <b>Null = no pick → the ruleset's parsed
    /// spec default</b> (classic <b>on</b> — so a default new game is byte-identical, ADR-009). Set by the dialog's Start
    /// and consumed (and cleared) in <see cref="NewGame"/>, where it is applied to the freshly-loaded ruleset via
    /// <see cref="Ruleset.WithFogOfWar"/> so <see cref="Game.CurrentlyVisible"/> / <see cref="Game.IsVisible"/> derive
    /// the visible set accordingly. Static because it must survive the scene change, like <see cref="PendingWorldSize"/>.
    /// <b>Session-only</b> — the override is not written to the save (a reload re-derives the option from the variant's
    /// spec; persisting it would bump the save format, matching the victory-condition seam, 86d3dzdw3).
    /// </summary>
    public static bool? PendingFogOfWar { get; set; }

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
    private RiverOverlay _riverLayer = null!;
    private ImprovementOverlay _improvementLayer = null!;
    private Node2D _unitLayer = null!;
    private Node2D _colonyLayer = null!;
    private Node2D _nativeLayer = null!;
    private Node2D _rumourLayer = null!;
    private Label _statusLabel = null!;
    private Label _calendarLabel = null!;
    private PanelContainer _selectedUnitPanel = null!;
    private Label _selectedUnitLabel = null!;
    private PanelContainer _tileInfoPanel = null!;
    private Label _tileInfoLabel = null!;
    private Button _fortifyButton = null!;
    private Button _sentryButton = null!;
    private Button _clearOrdersButton = null!;
    private Button _roadButton = null!;
    private Button _plowButton = null!;
    private Button _clearForestButton = null!;
    private Button _sailToEuropeButton = null!;
    private MiniMap _miniMap = null!;
    private PanelContainer _colonyPanel = null!;
    private PanelContainer _europePanel = null!;
    private PanelContainer _nativePanel = null!;
    private PanelContainer _demandPanel = null!;
    private PanelContainer _moundsPanel = null!;
    private MonarchDialog _monarchDialog = null!;
    private EmigrationChoicePanel _emigrationPanel = null!;
    private PreCombatPanel _preCombatPanel = null!;
    private TurnMessagePanel _turnMessagePanel = null!;
    private PanelContainer _tradeRoutePanel = null!;
    private PanelContainer _colonyReportPanel = null!;
    private PanelContainer _findSettlementPanel = null!;
    private PanelContainer _foundingFatherPanel = null!;
    private PanelContainer _colopediaPanel = null!;
    private PanelContainer _victoryPanel = null!;
    private PanelContainer _highScoresPanel = null!;
    private PanelContainer _declarationPanel = null!;
    private PanelContainer _negotiationPanel = null!;
    private Button _independenceButton = null!;
    private Button _endTurnButton = null!;
    private Control _gameOverScreen = null!;
    private Label _gameOverMessage = null!;
    private Unit? _selectedUnit;
    private Position? _inspectedTile;
    private string? _notice;
    private bool _gotoMode;
    private bool _victoryShown;
    private bool _highScoreRecorded;
    private string _gameId = "";
    private GotoMarker _gotoMarker = null!;

    public override void _Ready()
    {
        _mapView = GetNode<MapView>("MapView");
        _riverLayer = GetNode<RiverOverlay>("MapView/RiverLayer");
        _improvementLayer = GetNode<ImprovementOverlay>("MapView/ImprovementLayer");
        _unitLayer = GetNode<Node2D>("MapView/UnitLayer");
        _gotoMarker = GetNode<GotoMarker>("MapView/GotoMarker");
        _colonyLayer = GetNode<Node2D>("MapView/ColonyLayer");
        _nativeLayer = GetNode<Node2D>("MapView/NativeLayer");
        _rumourLayer = GetNode<Node2D>("MapView/RumourLayer");
        _statusLabel = GetNode<Label>("UI/StatusLabel");
        _calendarLabel = GetNode<Label>("UI/CalendarPanel/CalendarLabel");
        _selectedUnitPanel = GetNode<PanelContainer>("UI/SelectedUnitPanel");
        _selectedUnitLabel = GetNode<Label>("UI/SelectedUnitPanel/VBox/Label");
        _tileInfoPanel = GetNode<PanelContainer>("UI/TileInfoPanel");
        _tileInfoLabel = GetNode<Label>("UI/TileInfoPanel/VBox/Label");
        _fortifyButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/FortifyButton");
        _sentryButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/SentryButton");
        _clearOrdersButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/ClearButton");
        _fortifyButton.Pressed += () => ApplyUnitOrder(u => _game.CheckFortify(u).Allowed, _game.Fortify, "Fortifying.");
        _sentryButton.Pressed += () => ApplyUnitOrder(u => _game.CheckSentry(u).Allowed, _game.Sentry, "Sentried.");
        _clearOrdersButton.Pressed += () => ApplyUnitOrder(_ => true, _game.ClearOrders, "Orders cleared.");
        _roadButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/RoadButton");
        _plowButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/PlowButton");
        _clearForestButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/ClearForestButton");
        // Pioneer build orders: each forwards to the BuildImprovement command, gated on its CheckBuildImprovement oracle (ADR-006).
        _roadButton.Pressed += () => ApplyUnitOrder(
            u => _game.CheckBuildImprovement(u, TileImprovementType.RoadId).Allowed,
            u => _game.BuildImprovement(u, TileImprovementType.RoadId), "Building a road.");
        _plowButton.Pressed += () => ApplyUnitOrder(
            u => _game.CheckBuildImprovement(u, TileImprovementType.PlowId).Allowed,
            u => _game.BuildImprovement(u, TileImprovementType.PlowId), "Plowing the field.");
        _clearForestButton.Pressed += () => ApplyUnitOrder(
            u => _game.CheckBuildImprovement(u, TileImprovementType.ClearForestId).Allowed,
            u => _game.BuildImprovement(u, TileImprovementType.ClearForestId), "Clearing the forest.");
        // Ship order: sail to Europe from a high-seas tile (the map edge), gated on CheckSailToEurope (ADR-006).
        _sailToEuropeButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/SailToEuropeButton");
        _sailToEuropeButton.Pressed += () => ApplyUnitOrder(
            u => _game.CheckSailToEurope(u).Allowed, _game.SailToEurope, "Setting sail for Europe.");
        _miniMap = GetNode<MiniMap>("UI/MiniMap");
        _miniMap.TileSelected += CenterCameraOnTile;
        GetNode<Button>("UI/MiniMap/ZoomInButton").Pressed += _miniMap.ZoomIn;
        GetNode<Button>("UI/MiniMap/ZoomOutButton").Pressed += _miniMap.ZoomOut;
        _colonyPanel = GetNode<PanelContainer>("UI/ColonyPanel");
        _europePanel = GetNode<PanelContainer>("UI/EuropePanel");
        _nativePanel = GetNode<PanelContainer>("UI/NativeSettlementPanel");
        _demandPanel = GetNode<PanelContainer>("UI/NativeDemandPanel");
        _moundsPanel = GetNode<PanelContainer>("UI/MoundsDecisionPanel");
        _monarchDialog = GetNode<MonarchDialog>("UI/MonarchDialog");
        _emigrationPanel = GetNode<EmigrationChoicePanel>("UI/EmigrationChoicePanel");
        _preCombatPanel = GetNode<PreCombatPanel>("UI/PreCombatPanel");
        _turnMessagePanel = GetNode<TurnMessagePanel>("UI/TurnMessagePanel");
        _tradeRoutePanel = GetNode<PanelContainer>("UI/TradeRoutePanel");
        _colonyReportPanel = GetNode<PanelContainer>("UI/ColonyReportPanel");
        _findSettlementPanel = GetNode<PanelContainer>("UI/FindSettlementPanel");
        _foundingFatherPanel = GetNode<PanelContainer>("UI/FoundingFatherPanel");
        _colopediaPanel = GetNode<PanelContainer>("UI/ColopediaPanel");
        _victoryPanel = GetNode<PanelContainer>("UI/VictoryPanel");
        _highScoresPanel = GetNode<PanelContainer>("UI/HighScoresPanel");
        _declarationPanel = GetNode<PanelContainer>("UI/DeclarationPanel");
        _negotiationPanel = GetNode<PanelContainer>("UI/NegotiationPanel");
        _independenceButton = GetNode<Button>("UI/IndependenceButton");
        _endTurnButton = GetNode<Button>("UI/EndTurnButton");
        _gameOverScreen = GetNode<Control>("UI/GameOverScreen");
        _gameOverMessage = GetNode<Label>("UI/GameOverScreen/Panel/VBox/Message");
        _endTurnButton.Pressed += OnEndTurnPressed;
        GetNode<Button>("UI/EuropeButton").Pressed += OpenEuropePanel;
        GetNode<Button>("UI/TradeRoutesButton").Pressed += OpenTradeRoutePanel;
        GetNode<Button>("UI/ReportsButton").Pressed += OpenColonyReportPanel;
        GetNode<Button>("UI/ColopediaButton").Pressed += OpenColopediaPanel;
        GetNode<Button>("UI/HighScoresButton").Pressed += OpenHighScoresPanel;
        GetNode<Button>("UI/DiplomacyButton").Pressed += OpenNegotiationPanel;
        _independenceButton.Pressed += OpenDeclarationPanel;
        GetNode<Button>("UI/ColopediaPanel/VBox/CloseButton").Pressed += () => _colopediaPanel.Hide();
        GetNode<Button>("UI/ColonyReportPanel/VBox/CloseButton").Pressed += () => _colonyReportPanel.Hide();
        GetNode<Button>("UI/VictoryPanel/VBox/CloseButton").Pressed += () => _victoryPanel.Hide();
        GetNode<Button>("UI/HighScoresPanel/VBox/CloseButton").Pressed += () => _highScoresPanel.Hide();
        GetNode<Button>("UI/FindSettlementPanel/VBox/CloseButton").Pressed += () => _findSettlementPanel.Hide();
        GetNode<Button>("UI/FoundingFatherPanel/VBox/CloseButton").Pressed += () => _foundingFatherPanel.Hide();
        GetNode<Button>("UI/ColonyPanel/VBox/CloseButton").Pressed += () => _colonyPanel.Hide();
        GetNode<Button>("UI/EuropePanel/VBox/CloseButton").Pressed += () => _europePanel.Hide();
        GetNode<Button>("UI/NegotiationPanel/VBox/CloseButton").Pressed += () => _negotiationPanel.Hide();
        GetNode<Button>("UI/NativeSettlementPanel/VBox/CloseButton").Pressed += () => _nativePanel.Hide();
        GetNode<Button>("UI/TradeRoutePanel/VBox/CloseButton").Pressed += () => _tradeRoutePanel.Hide();
        GetNode<Button>("UI/GameOverScreen/Panel/VBox/NewGameButton").Pressed += NewGame;

        if (PendingLoadPath is { } loadPath)
        {
            PendingLoadPath = null;
            LoadFrom(loadPath); // entered from the main menu's Load Game
        }
        else
        {
            NewGame();
        }
    }

    private void NewGame()
    {
        // The new-game options (if the player chose any) ride a static across the scene change; consume and clear
        // them so a later default new game isn't surprised by a stale choice. Null → the shipped default.
        WorldSize size = PendingWorldSize ?? WorldSizeOptions.DefaultSize;
        LandMass land = PendingLandMass ?? WorldSizeOptions.DefaultLandMass;
        DifficultyLevel difficulty = PendingDifficulty ?? DifficultyLevels.Default;
        MapSource mapSource = PendingMapSource ?? MapSource.Random;
        string? nation = PendingNation; // null = no pick → the classic nation-less human (byte-identical default)
        LandStyle landStyle = (PendingLandStyle ?? WorldSizeOptions.DefaultLandStyle).Style; // null = Continent (default)
        // The chosen victory conditions (null = no pick → the ruleset's parsed spec defaults, byte-identical).
        (bool DefeatRef, bool DefeatEuropeans, bool DefeatHumans)? victory = PendingVictoryConditions;
        // The chosen fog-of-war toggle (null = no pick → the ruleset's parsed spec default, classic on, byte-identical).
        bool? fogOfWar = PendingFogOfWar;
        PendingWorldSize = null;
        PendingLandMass = null;
        PendingDifficulty = null;
        PendingMapSource = null;
        PendingNation = null;
        PendingLandStyle = null;
        PendingVictoryConditions = null;
        PendingFogOfWar = null;

        // Picking the seed may be non-deterministic (player convenience);
        // the game itself is fully determined by the chosen seed.
        StartNewGame(Seed != 0 ? Seed : ((ulong)GD.Randi() << 32) | GD.Randi(), size, land, difficulty, mapSource, nation, landStyle, victory, fogOfWar);
    }

    /// <summary>Starts a new game from an explicit seed at the shipped-default world size / difficulty / map / nation-less human (tests, visual goldens — ADR-009).</summary>
    public void StartNewGame(ulong seed) =>
        StartNewGame(seed, WorldSizeOptions.DefaultSize, WorldSizeOptions.DefaultLandMass, DifficultyLevels.Default, MapSource.Random);

    /// <summary>Starts a new game from an explicit seed, world size / land amount, difficulty level, map source, (optional) human nation, (optional) landmass style, (optional) victory-condition overrides and (optional) fog-of-war override (forwarded from the new-game options). The ruleset is loaded under the chosen level so its balance matches, the level is recorded for the save, a fixed <paramref name="mapSource"/> ignores the size/land/style args (its grid sets the dimensions), <paramref name="humanNationId"/> (null = no pick) seeds the human's national advantage + colony names, <paramref name="landStyle"/> (default <see cref="LandStyle.Continent"/>) shapes the random map's land, <paramref name="victory"/> (null = the ruleset's parsed spec defaults) flips which alternative victory conditions <see cref="Game.Winner"/> evaluates, and <paramref name="fogOfWar"/> (null = the spec default, classic on) flips whether explored-but-unseen tiles are re-hidden — both session-only, not persisted (86d3drn64, 86d3dzdw3).</summary>
    public void StartNewGame(ulong seed, WorldSize size, LandMass landMass, DifficultyLevel difficulty, MapSource mapSource, string? humanNationId = null, LandStyle landStyle = LandStyle.Continent, (bool DefeatRef, bool DefeatEuropeans, bool DefeatHumans)? victory = null, bool? fogOfWar = null)
    {
        _currentSeed = seed;
        // Load the variant's ruleset under the chosen difficulty; if the player picked victory conditions / fog of war,
        // apply them to this freshly-parsed (never-shared) instance before building the game — a configuration override
        // (of which win checks fire / how visibility is derived), not a rules change (ADR-006). Null leaves the spec
        // defaults untouched, so a default new game is byte-identical.
        Ruleset ruleset = _variant.LoadRuleset(difficulty.Id);
        if (victory is { } v)
        {
            ruleset = ruleset.WithVictoryConditions(v.DefeatRef, v.DefeatEuropeans, v.DefeatHumans);
        }
        if (fogOfWar is { } fog)
        {
            ruleset = ruleset.WithFogOfWar(fog);
        }
        StartGame(Game.New(
            ruleset, _currentSeed, size.Width, size.Height,
            landMassFraction: landMass.Fraction, difficultyLevelId: difficulty.Id, mapSource: mapSource,
            humanNationId: humanNationId, landStyle: landStyle));
    }

    private void StartGame(Game game)
    {
        _game = game;
        _selectedUnit = null;
        _inspectedTile = null;
        _notice = null;
        _victoryShown = false; // re-arm the one-shot victory screen for the fresh game (new or loaded)
        _highScoreRecorded = false; // re-arm the one-shot end-of-game high-score record
        _gameId = System.Guid.NewGuid().ToString(); // per-session game id for high-score de-duplication (not persisted in the save)
        _victoryPanel.Hide();
        _highScoresPanel.Hide();
        // Centre on the player's first on-map unit; after founding the only colony the player may have
        // none on the map (and the unit list now also holds native braves), so fall back to a colony,
        // then the map centre.
        Position focus = _game.PlayerUnits.FirstOrDefault(u => u.IsOnMap)?.Position
            ?? _game.Colonies.FirstOrDefault(c => c.OwnerId == _game.HumanPlayer.PlayerId)?.Position
            ?? new Position(_game.Map.Width / 2, _game.Map.Height / 2);
        GetNode<Camera2D>("Camera").Position = MapView.TileCentre(focus);
        // Cue the human player's national anthem once over the running background music (FreeCol plays it at game start).
        PlayAnthem(_game.HumanPlayer.NationId);
        RefreshView();
    }

    /// <summary>Recenters the main camera on a map tile — the minimap's click-to-recenter target (ADR-006).</summary>
    private void CenterCameraOnTile(Position tile) =>
        GetNode<Camera2D>("Camera").Position = MapView.TileCentre(tile);

    /// <summary>Applies a standing order to the selected unit when its <paramref name="allowed"/> check passes, then refreshes (ADR-006).</summary>
    private void ApplyUnitOrder(System.Func<Unit, bool> allowed, System.Action<Unit> apply, string notice)
    {
        if (_selectedUnit is { } u && allowed(u))
        {
            apply(u);
            _notice = notice;
            RefreshView();
        }
    }

    /// <summary>One-line readout for the selected-unit HUD panel (type / moves / role / orders / goto). Reads-only (ADR-006).</summary>
    private string DescribeSelectedUnit(Unit u)
    {
        string role = u.HasDefaultRole ? "" : $"  ·  {u.RoleId[(u.RoleId.LastIndexOf('.') + 1)..]}";
        string orders = u.Orders == UnitOrders.Active ? "" : $"  ·  {u.Orders.ToString().ToLowerInvariant()}";
        // An in-progress tile improvement: show what's being built and the turns of work left.
        string building = u.WorkImprovementId is { } imp
            ? $"  ·  building {imp[(imp.LastIndexOf('.') + 1)..]} ({u.WorkTurnsLeft})"
            : "";
        string goingTo = u.IsGoingTo ? "  ·  going to" : "";
        return $"{u.Type.ShortName}  ·  moves {u.MovementLeft}/{u.Type.Movement}{role}{orders}{building}{goingTo}";
    }

    /// <summary>
    /// One- or two-line readout for the HUD tile-info panel (FreeCol's <c>InfoPanel</c> tile-info): the clicked
    /// tile's terrain, its bonus resource (if any), and what occupies it — the human's own colony, a discovered
    /// native settlement, an on-map unit, or nothing. Fog-gated: an unexplored tile reads only "Unexplored" so the
    /// player learns nothing they haven't seen. Reads-only over <see cref="Game"/> oracles (ADR-006).
    /// </summary>
    private string DescribeTile(Position tile)
    {
        if (!_game.IsExplored(tile))
        {
            return "Unexplored";
        }

        // Terrain + bonus resource. The resource short name is derived from its ruleset id the same way MapView
        // turns the bonus id into an icon name (e.g. model.resource.minerals → minerals); no new oracle needed.
        string terrain = _game.Map.TerrainAt(tile).ShortName;
        string line = terrain;
        if (_game.Map.ResourceAt(tile) is { } resourceId)
        {
            line += $"  ·  {resourceId[(resourceId.LastIndexOf('.') + 1)..]}";
        }

        // Occupant: the human's own colony, a discovered native settlement, then any on-map unit. Foreign colonies
        // are not named (the player learns their name only by visiting); the terrain line alone is shown for them.
        if (_game.ColonyAt(tile) is { } colony && colony.OwnerId == _game.HumanPlayer.PlayerId)
        {
            line += $"\nYour colony: {colony.Name}";
        }
        else if (_game.NativeSettlementAt(tile) is { } settlement)
        {
            line += $"\n{NationLabel(settlement.NationTypeId)} settlement";
        }
        else if (_game.Units.FirstOrDefault(u => u.IsOnMap && u.Position == tile) is { } unit)
        {
            line += $"\n{unit.Type.ShortName}";
        }
        return line;
    }

    private void OnEndTurnPressed()
    {
        _game.EndTurn();
        // Surface what the human suffered or received during the AI phase (no return value to read, unlike a
        // player-initiated attack): raids on units (1c-2/1c-3a′), native pillages of colonies, captures of colonies
        // (1c-3f), then custom-house auto-sales. Notices are in deterministic order; instead of cramming them into
        // the one-line status bar, each is formatted to a player-facing row and shown together in the dismissible
        // TurnMessagePanel (FreeCol's ReportTurnPanel). Formatting (and the rules) stay here / in GameLogic (ADR-006).
        var messages = _game.CombatNotices.Select(FormatCombatNotice)
            .Concat(_game.ColonyRaidNotices.Select(FormatColonyRaidNotice))
            .Concat(_game.ColonyLossNotices.Select(FormatColonyLossNotice))
            .Concat(_game.CustomHouseSaleNotices.Select(FormatCustomHouseSaleNotice))
            .ToList();
        if (_game.IsHumanDefeated)
        {
            // The AI phase took the human's last colony/unit — surface the defeat after the loss notice that caused it.
            messages.Add("💀 You have been defeated — your last colony and units are gone.");
        }
        _turnMessagePanel.Open(messages); // no-op (stays hidden) when there were no events this turn
        RefreshView();

        // A native brave demanded tribute of one of the human's colonies during the AI phase → prompt for a decision
        // (unless the human was just wiped out — no colony to demand of then anyway). Re-opens with the fresh demand
        // each turn, or hides if none; an ignored demand is auto-refused by the next EndTurn (engine-side backstop).
        if (!_game.IsHumanDefeated && _game.PendingDemand is { } demand)
        {
            ((NativeDemandPanel)_demandPanel).Open(_game, demand, outcome =>
            {
                _notice = outcome;
                RefreshView();
            });
        }
        else
        {
            _demandPanel.Hide();
        }
    }

    /// <summary>Turns an AI attack on the human into a status-bar message, from the human defender's point of view.</summary>
    private string FormatCombatNotice(CombatNotice notice)
    {
        string unit = _game.Ruleset.Unit(notice.DefenderUnitTypeId).ShortName;
        bool naval = _game.Ruleset.Unit(notice.DefenderUnitTypeId).IsNaval;
        string at = $"({notice.Position.X},{notice.Position.Y})";
        bool attackerWon = notice.Outcome is CombatResult.GreatWin or CombatResult.Win;

        // A privateer flies no flag — the victim cannot tell whose it was, so name no nation.
        if (notice.AttackerNationId == Game.UnknownEnemyNationId)
        {
            if (notice.Outcome == CombatResult.Evade)
            {
                return $"Your {unit} evaded a privateer at {at}.";
            }
            return attackerWon
                ? $"⚔ A privateer sank your {unit} at {at}!"
                : $"Your {unit} fought off a privateer at {at}.";
        }

        string nation = NationLabel(notice.AttackerNationId);
        if (notice.Outcome == CombatResult.Evade)
        {
            return $"Your {unit} evaded a {nation} attack at {at}.";
        }
        return attackerWon
            ? (naval ? $"⚔ The {nation} sank your {unit} at {at}!" : $"⚔ The {nation} raided your {unit} at {at}!")
            : $"Your {unit} fought off a {nation} {(naval ? "raider" : "raid")} at {at}.";
    }

    /// <summary>Turns an AI capture of a human colony into a status-bar message (the colony-loss sibling of <see cref="FormatCombatNotice"/>).</summary>
    private static string FormatColonyLossNotice(ColonyLossNotice notice) =>
        $"⚑ The {NationLabel(notice.AttackerNationId)} captured your colony {notice.ColonyName} at ({notice.Position.X},{notice.Position.Y})!";

    /// <summary>Turns a native pillage of a human colony into a status-bar message (the goods-raid sibling of <see cref="FormatColonyLossNotice"/>). A null <c>GoodsId</c> means gold was stolen.</summary>
    private string FormatColonyRaidNotice(ColonyRaidNotice notice) =>
        notice.GoodsId is { } goodsId
            ? $"⚔ The {NationLabel(notice.AttackerNationId)} raided {notice.ColonyName} and carried off {notice.Amount} {_game.Ruleset.Goods(goodsId).ShortName}!"
            : $"⚔ The {NationLabel(notice.AttackerNationId)} raided {notice.ColonyName} and made off with {notice.Amount} gold!";

    /// <summary>Turns a custom-house auto-sale into a turn-message row ("💰 Your custom house in X sold N goods for G gold.").</summary>
    private string FormatCustomHouseSaleNotice(CustomHouseSaleNotice notice) =>
        $"💰 Your custom house in {notice.ColonyName} sold {notice.Amount} {_game.Ruleset.Goods(notice.GoodsId).ShortName} for {notice.Gold} gold.";

    /// <summary>The display label for a nation id (e.g. <c>model.nation.dutch</c> → "Dutch").</summary>
    private static string NationLabel(string nationId)
    {
        string shortName = nationId[(nationId.LastIndexOf('.') + 1)..];
        return char.ToUpperInvariant(shortName[0]) + shortName[1..];
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
            case InputEventKey { Keycode: Key.L, Pressed: true, Echo: false }:
                OpenFindSettlementPanel();
                break;
            case InputEventKey { Keycode: Key.F, Pressed: true, Echo: false }:
                OpenFoundingFatherPanel();
                break;
            case InputEventKey { Keycode: Key.G, Pressed: true, Echo: false }:
                EnterGotoMode();
                break;
            case InputEventKey { Keycode: Key.W, Pressed: true, Echo: false }:
                SelectNextUnitToMove();
                break;
            case InputEventKey { Keycode: Key.C, Pressed: true, Echo: false }:
                OpenColopediaPanel();
                break;
        }
    }

    /// <summary>Selects the next of the human's units still needing orders and centres on it (FreeCol's "wait/next unit"
    /// cycle, key W) — reads the shipped <see cref="Game.NextUnitToMove"/> oracle; no-op when none remain (ADR-006).</summary>
    private void SelectNextUnitToMove()
    {
        if (_game.NextUnitToMove(_game.HumanPlayer) is { } next)
        {
            _selectedUnit = next;
            CenterCameraOnTile(next.Position);
            RefreshView();
        }
        else
        {
            _notice = "No units need orders.";
            RefreshView();
        }
    }

    /// <summary>
    /// Sets the selected unit's standing "go to" destination (the goto-mode click target / public test seam).
    /// Validates via <see cref="Game.CheckSetDestination"/>, surfaces the outcome in the status bar, and refreshes
    /// (which draws the destination marker). No-op with no selection. Returns whether a destination was set.
    /// </summary>
    public bool SetSelectedDestination(Position tile)
    {
        if (_selectedUnit is not { } goer)
        {
            return false;
        }
        MoveCheck check = _game.CheckSetDestination(goer, tile);
        if (check.Allowed)
        {
            _game.SetDestination(goer, tile);
            _notice = $"Destination set to ({tile.X},{tile.Y}).";
        }
        else
        {
            _notice = check.Reason;
        }
        RefreshView();
        return check.Allowed;
    }

    /// <summary>Arms goto-target mode: the next map click sets the selected unit's standing destination.</summary>
    private void EnterGotoMode()
    {
        if (_selectedUnit is null)
        {
            _notice = "Select a unit first, then press G to set a destination.";
        }
        else
        {
            _gotoMode = true;
            _notice = "Go to: click a destination tile (Esc-click the unit to cancel).";
        }
        RefreshView();
    }

    private void HandleTileClick(Position tile)
    {
        if (!_game.Map.InBounds(tile))
        {
            return;
        }

        // Remember the clicked tile as the "inspected" tile so the HUD tile-info readout describes it
        // (terrain / resource / occupant). Set on every in-bounds click — selecting, moving or attacking all
        // refresh the readout to the tile the player just acted on (FreeCol's InfoPanel tile-info, ADR-006).
        _inspectedTile = tile;

        // Goto-target mode (armed by the G key): this click sets the selected unit's standing destination
        // instead of moving/attacking. The per-turn ProcessGotos walks it there (ADR-006 — rules in GameLogic).
        if (_gotoMode)
        {
            _gotoMode = false;
            SetSelectedDestination(tile);
            return;
        }

        // Board: a selected land unit clicks an adjacent/same-tile friendly carrier with room → it embarks
        // (ADR-006; the same Board oracle the Europe screen uses). Takes priority over selecting the ship.
        if (_selectedUnit is { Type.IsNaval: false, IsOnMap: true } boarder)
        {
            Unit? ship = _game.PlayerUnits.FirstOrDefault(u =>
                u.Type.IsCarrier && u.IsOnMap && u.Position == tile && _game.CheckBoard(boarder, u).Allowed);
            if (ship is not null)
            {
                _game.Board(boarder, ship);
                _notice = $"{boarder.Type.ShortName} boarded the {ship.Type.ShortName}.";
                _selectedUnit = ship;
                RefreshView();
                return;
            }
        }

        // Disembark: a selected carrier with passengers clicks an adjacent land tile → put a passenger ashore.
        if (_selectedUnit is { IsOnMap: true } carrier && carrier.Type.IsCarrier)
        {
            Unit? passenger = _game.Passengers(carrier).FirstOrDefault(p => _game.CheckDisembark(p, tile).Allowed);
            if (passenger is not null)
            {
                _game.Disembark(passenger, tile);
                _notice = $"{passenger.Type.ShortName} went ashore at ({tile.X},{tile.Y}).";
                _selectedUnit = passenger;
                RefreshView();
                return;
            }
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
        else if (_game.NativeSettlementAt(tile) is { } settlement && _game.IsExplored(tile))
        {
            // A discovered native settlement → open the interaction panel (speak / learn / attack),
            // each gated on the selected unit. Replaces the old click-to-attack so peaceful contact is possible.
            OpenNativeSettlementPanel(settlement, _selectedUnit);
        }
        else if (_selectedUnit is not null)
        {
            // An adjacent enemy on the clicked tile → attack it; otherwise try to move there.
            if (_game.Units.Any(u => u.IsOnMap && u.Position == tile))
            {
                AttackUnitAt(tile); // any on-map unit here is an enemy (the player's own were handled above)
            }
            else if (_game.ColonyAt(tile) is { } rival && rival.OwnerId != _game.HumanPlayer.PlayerId && _game.IsExplored(tile))
            {
                // A scout adjacent to a rival colony opens the scout-mission menu (spy / negotiate, FreeCol
                // moveScoutColony); any other unit falls through to the click-to-capture assault.
                if (!TryOpenScoutColonyMissions(rival, _selectedUnit))
                {
                    AttackColonyAt(tile); // a discovered, ungarrisoned rival colony → assault to capture it
                }
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

    /// <summary>
    /// Opens the pre-combat odds dialog for an attack on the enemy unit at an adjacent tile (`86d3c9xmw`): the
    /// previewed powers/win% come from the side-effect-free <see cref="Game.CombatOddsAgainst"/> oracle, so no
    /// RNG is drawn and no turn spent until the player confirms. The actual roll runs in <see cref="ResolveAttackOn"/>.
    /// </summary>
    private void AttackUnitAt(Position tile)
    {
        MoveCheck check = _game.CheckAttack(_selectedUnit!, tile);
        if (!check.Allowed)
        {
            _notice = check.Reason;
            return;
        }
        Game.CombatOdds? odds = _game.CombatOddsAgainst(_selectedUnit!, tile);
        if (odds is null)
        {
            ResolveAttackOn(tile, _selectedUnit!); // no previewable defender (shouldn't happen post-CheckAttack): resolve directly
            return;
        }
        Unit attacker = _selectedUnit!;
        _preCombatPanel.Open(odds, () => ResolveAttackOn(tile, attacker));
    }

    /// <summary>Rolls the confirmed attack of <paramref name="attacker"/> on <paramref name="tile"/> and reports the outcome.</summary>
    private void ResolveAttackOn(Position tile, Unit attacker)
    {
        string who = attacker.Type.ShortName;
        bool naval = attacker.Type.IsNaval;
        // Snapshot the attacker's tile/sprite before the roll: a loss may demote, move or destroy the unit.
        Position from = attacker.Position;
        string roleShortName = attacker.RoleId[(attacker.RoleId.LastIndexOf('.') + 1)..];
        CombatResult result = _game.Attack(attacker, tile);
        _selectedUnit = null; // the attack ends the unit's turn (and may demote/destroy it)
        _notice = result switch
        {
            CombatResult.GreatWin or CombatResult.Win => $"Your {who} won the battle.",
            CombatResult.Evade => $"The enemy evaded your {who}.", // naval: the defender slipped away
            _ => $"Your {who} was beaten back.",
        };
        // A decisive naval win sinks the enemy ship — play the sinking cue; otherwise the generic combat cue.
        PlaySound(naval && result is CombatResult.GreatWin or CombatResult.Win
            ? SoundEvent.ShipSunk
            : SoundEvent.Combat);
        PlayCombatAnimation(from, tile, result, who, roleShortName);
        RefreshView();
    }

    /// <summary>
    /// Fires the non-blocking procedural attack animation (`86d3drn72`) for a just-resolved combat: a transient sprite
    /// lunges from <paramref name="from"/> toward <paramref name="defenderTile"/> and snaps back, flavoured by the
    /// outcome (<see cref="CombatAnimationMap"/>). Presentation-only over the already-resolved combat (ADR-006); the
    /// turn proceeds immediately. A null unit layer (headless tests that omit it) is a silent no-op.
    /// </summary>
    private void PlayCombatAnimation(Position from, Position defenderTile, CombatResult result,
        string typeShortName, string roleShortName)
    {
        if (_unitLayer is null)
        {
            return;
        }
        CombatAnimationKind kind = CombatAnimationMap.KindFor(result);
        Texture2D? texture = UnitMarker.ResolveTexture(typeShortName, roleShortName);
        CombatAnimation.Play(_unitLayer, from, defenderTile, kind, texture);
    }

    /// <summary>Assaults an ungarrisoned rival colony on an adjacent tile to capture it, reporting the outcome.</summary>
    private void AttackColonyAt(Position tile)
    {
        MoveCheck check = _game.CheckAttackColony(_selectedUnit!, tile);
        if (!check.Allowed)
        {
            _notice = check.Reason;
            return;
        }
        string colonyName = _game.ColonyAt(tile)!.Name;
        // Snapshot the attacker's tile/sprite before the assault (a loss may demote or destroy it).
        Unit assaulter = _selectedUnit!;
        Position from = assaulter.Position;
        string who = assaulter.Type.ShortName;
        string roleShortName = assaulter.RoleId[(assaulter.RoleId.LastIndexOf('.') + 1)..];
        CombatResult result = _game.AttackColony(assaulter, tile);
        _selectedUnit = null; // the assault ends the unit's turn (and may demote/destroy it on a loss)
        _notice = result is CombatResult.GreatWin or CombatResult.Win
            ? $"You captured {colonyName}!"
            : "Your assault on the colony was repelled.";
        PlayCombatAnimation(from, tile, result, who, roleShortName);
    }

    /// <summary>
    /// Plays the SFX cue for <paramref name="evt"/> via the <c>Sound</c> autoload (<c>/root/Sound</c>). Resolved lazily
    /// by node path so this works whether or not the autoload is present (e.g. headless scene tests that don't boot it):
    /// a missing service is silently a no-op rather than a crash.
    /// </summary>
    private void PlaySound(SoundEvent evt) => GetNodeOrNull<SoundService>("/root/Sound")?.Play(evt);

    /// <summary>
    /// Plays the human player's national anthem once via the <c>Music</c> autoload (<c>/root/Music</c>), then the
    /// background playlist resumes. Resolved lazily by node path (no-op if the autoload is absent, e.g. headless scene
    /// tests) and a no-op for a nation FreeCol ships no anthem for. Faithful to FreeCol, which cues the anthem (a
    /// <c>"music"</c>-type resource) at game start.
    /// </summary>
    private void PlayAnthem(string? nationId) =>
        GetNodeOrNull<MusicService>("/root/Music")?.PlayAnthem(nationId);

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
            RefreshView();
            return;
        }

        // Founding on a native-OWNED tile forces a buy-or-steal-or-abandon claim FIRST (86d3e4bj7): raise the choice
        // dialog and resolve through FoundColonyWithClaim. On free land we found immediately (no dialog). The decision
        // itself lives in GameLogic (ADR-006) — this only surfaces the choice and forwards it.
        Game.ForcedLandClaim forced = _game.RequiredLandClaim(_selectedUnit.Position);
        if (forced.Required)
        {
            PromptLandClaim(forced, FoundColonyWithClaim);
            return;
        }

        FoundColonyWithClaim(LandClaimChoice.Buy); // free land → Buy is a zero-cost, peaceful claim (no-op), then found
    }

    /// <summary>Founds a colony, resolving any forced native-land claim with the human's <paramref name="choice"/>
    /// (the thin GameController command for the buy/steal path; <see cref="LandClaimChoice.Abandon"/> cancels).</summary>
    private void FoundColonyWithClaim(LandClaimChoice choice)
    {
        if (_selectedUnit is null || choice == LandClaimChoice.Abandon)
        {
            RefreshView();
            return;
        }
        Colony colony = _game.FoundColony(_selectedUnit, choice);
        _selectedUnit = null;
        _notice = $"{colony.Name} founded!";
        PlaySound(SoundEvent.ColonyFounded);
        RefreshView();
    }

    /// <summary>
    /// Raises the buy-or-steal-or-abandon choice for a forced native-land claim (86d3e4bj7) and invokes
    /// <paramref name="onChosen"/> with the human's <see cref="LandClaimChoice"/>. A code-built modal — no pending-claim
    /// state is stored in <see cref="Game"/> (the founding/working context is held by the caller's closure and the
    /// claim is resolved synchronously on the click). Buy is disabled when the player cannot afford the price.
    /// </summary>
    private void PromptLandClaim(Game.ForcedLandClaim forced, System.Action<LandClaimChoice> onChosen)
    {
        string nation = NationLabel(forced.OwningNation!);
        bool canAfford = _game.HumanPlayer.Gold >= forced.BuyPrice;
        var dialog = new ConfirmationDialog
        {
            Title = "Native land",
            DialogText = $"The {nation} own this land.\nBuy it for {forced.BuyPrice} gold, take it by force (angering them), or abandon the attempt?",
            OkButtonText = canAfford ? $"Buy ({forced.BuyPrice}g)" : $"Buy ({forced.BuyPrice}g — can't afford)",
            CancelButtonText = "Abandon",
        };
        Button stealButton = dialog.AddButton("Steal", right: true, action: "steal");
        if (!canAfford)
        {
            dialog.GetOkButton().Disabled = true;
        }

        void Finish(LandClaimChoice choice)
        {
            dialog.QueueFree();
            onChosen(choice);
        }

        dialog.Confirmed += () => Finish(LandClaimChoice.Buy);
        dialog.Canceled += () => Finish(LandClaimChoice.Abandon);
        stealButton.Pressed += () => Finish(LandClaimChoice.Steal);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Opens the interactive colony screen. Public so scene tests can drive it directly.</summary>
    public void OpenColonyPanel(Colony colony) =>
        ((ColonyPanel)_colonyPanel).Open(_game, colony, RefreshView);

    /// <summary>Opens the Europe screen (dock, recruits, ships in port). Public so scene tests can drive it.</summary>
    public void OpenEuropePanel() =>
        ((EuropePanel)_europePanel).Open(_game, RefreshView);

    /// <summary>Opens the trade-route management screen (list/create/assign/delete routes). Public so scene tests can drive it.</summary>
    public void OpenTradeRoutePanel() =>
        ((TradeRoutePanel)_tradeRoutePanel).Open(_game, RefreshView);

    /// <summary>Opens the empire colony report (per-colony population / production / build requirements). Public so scene tests can drive it.</summary>
    public void OpenColonyReportPanel() =>
        ((ColonyReportPanel)_colonyReportPanel).Open(_game);

    /// <summary>Opens the Find Settlement dialog (pick a colony to recenter the camera on it). Public so scene tests can drive it.</summary>
    public void OpenFindSettlementPanel() =>
        ((FindSettlementPanel)_findSettlementPanel).Open(_game, CenterCameraOnTile);

    /// <summary>Opens the Founding Father choice dialog (pick which offered father to recruit). Public so scene tests can drive it.</summary>
    public void OpenFoundingFatherPanel() =>
        ((FoundingFatherPanel)_foundingFatherPanel).Open(_game, RefreshView);

    /// <summary>
    /// Opens the Declaration-of-Independence signing screen (consequences + confirm, then the signed declaration). The
    /// panel reads the <see cref="Game.CheckDeclareIndependence"/> gate itself — if the human isn't eligible it explains
    /// why — so this can be driven unconditionally; the HUD button that drives it is only shown when eligible. Public so
    /// scene tests can drive it.
    /// </summary>
    public void OpenDeclarationPanel() =>
        ((DeclarationPanel)_declarationPanel).Open(_game, RefreshView);

    /// <summary>Opens the Colopedia reference panel (the Goods category — a read-only ruleset reference). Public so scene tests can drive it.</summary>
    public void OpenColopediaPanel() =>
        ((ColopediaPanel)_colopediaPanel).Open(_game);

    /// <summary>Opens the emigration choice dialog for the pending <c>selectRecruit</c> choice (no-op when none pending). Public so scene tests can drive it.</summary>
    public void OpenEmigrationChoicePanel() =>
        _emigrationPanel.Open(_game, outcome =>
        {
            if (!string.IsNullOrEmpty(outcome))
            {
                _notice = outcome;
            }
            RefreshView();
        });

    /// <summary>
    /// Opens the diplomacy / negotiation dialog (86d3c9xpt): the human answers any queued AI treaty offers
    /// (<see cref="Game.PendingHumanProposals"/>) and may open a fresh negotiation with a contacted rival. The
    /// callback surfaces the outcome and refreshes (a settled treaty may flip a stance). Public so the HUD button
    /// and scene tests can drive it.
    /// </summary>
    public void OpenNegotiationPanel() =>
        ((NegotiationPanel)_negotiationPanel).Open(_game, () =>
        {
            _notice = "Diplomacy updated.";
            RefreshView();
        });

    /// <summary>
    /// Opens the negotiation dialog pinned to the rival colony <paramref name="rivalColony"/> — the scout-at-the-gate
    /// "negotiate" mission (86d3c9ubw, FreeCol <c>SCOUT_COLONY_NEGOTIATE</c>): the proposer chooser is pre-targeted at
    /// the colony's owner. Public so the scout entry point and scene tests can drive it.
    /// </summary>
    public void OpenNegotiationForColony(Colony rivalColony) =>
        ((NegotiationPanel)_negotiationPanel).OpenForColony(_game, rivalColony, () =>
        {
            _notice = "Diplomacy updated.";
            RefreshView();
        });

    /// <summary>
    /// The scout-enters-rival-colony entry point (86d3c9ubw, FreeCol <c>moveScoutColony</c>): a selected scout adjacent
    /// to <paramref name="rivalColony"/> chooses a mission — <b>spy</b> on the interior (<see cref="Game.SpyOnColony(Unit, World.Position)"/>)
    /// or open a <b>negotiation</b> with the owner. Each is gated on its Game oracle; the menu only appears when at least
    /// the spy mission is legal (the scout carries the spyOnColony ability and has movement left). Returns whether the
    /// menu was opened.
    /// </summary>
    private bool TryOpenScoutColonyMissions(Colony rivalColony, Unit scout)
    {
        if (!_game.CheckSpyOnColony(scout, rivalColony.Position).Allowed)
        {
            return false; // not a scout, no moves, or not adjacent — fall through to the normal click handling
        }

        int scoutId = scout.Id;
        ((NegotiationPanel)_negotiationPanel).OpenScoutMissions(
            _game, rivalColony, scoutId,
            onSpy: () =>
            {
                SpyOnRivalColony(scoutId, rivalColony.Position);
            },
            onNegotiate: () =>
            {
                OpenNegotiationForColony(rivalColony);
            });
        return true;
    }

    /// <summary>Runs the scout's spy mission on <paramref name="target"/> and surfaces the glimpse — the interior snapshot is shown in the colony panel as a read-only view; the scout's turn ends. The spy always succeeds (FreeCol-exact).</summary>
    private void SpyOnRivalColony(int scoutId, Position target)
    {
        Unit? scout = _game.Units.FirstOrDefault(u => u.Id == scoutId && u.IsOnMap);
        if (scout is null || !_game.CheckSpyOnColony(scout, target).Allowed)
        {
            return;
        }
        ColonyInteriorSnapshot snapshot = _game.SpyOnColony(scout, target).Snapshot;
        _selectedUnit = null; // the spy ends the scout's turn
        _notice = $"Your scout glimpsed inside {snapshot.Name} (pop {snapshot.Population}, SoL {snapshot.SonsOfLiberty}%).";
        RefreshView();
    }

    /// <summary>Opens the native-settlement interaction panel, acting with <paramref name="actingUnit"/> (may be null — the panel then prompts to select one).</summary>
    public void OpenNativeSettlementPanel(NativeSettlement settlement, Unit? actingUnit)
    {
        int actingId = actingUnit?.Id ?? 0;
        ((NativeSettlementPanel)_nativePanel).Open(_game, settlement, actingId, outcome =>
        {
            // A panel action may have spent / upgraded (Learn keeps the id) / destroyed the acting unit — re-resolve
            // the map selection to the live unit of that id (or clear it if it's gone) so the ring and later clicks
            // stay valid, and surface the action's outcome in the status bar (the panel may have closed on a sack).
            _selectedUnit = actingId == 0 ? null : _game.Units.FirstOrDefault(u => u.Id == actingId && u.IsOnMap);
            if (!string.IsNullOrEmpty(outcome))
            {
                _notice = outcome;
            }
            RefreshView();
        });
    }

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
        // Reload under the save's own variant + difficulty level so its ruleset/balance matches (ADR-018, 86d3c9y08).
        _variant = GameVariants.Resolve(save.Variant);
        StartGame(save.Restore(_variant.LoadRuleset(save.DifficultyLevelOrDefault)));
        _notice = "Game loaded.";
        RefreshView();
    }

    /// <summary>Saves the current game to <paramref name="path"/> (creating the saves directory if needed). Used by the save/load dialog.</summary>
    public void SaveTo(string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(SavesDir);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(SaveGame.From(_game, _variant.Id).ToJson());
    }

    /// <summary>Loads a game from <paramref name="path"/> under the save's own variant ruleset (ADR-018). Used by the save/load dialog and the boot-time pending load.</summary>
    public void LoadFrom(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        SaveGame save = SaveGame.FromJson(file.GetAsText());
        _variant = GameVariants.Resolve(save.Variant);
        StartGame(save.Restore(_variant.LoadRuleset(save.DifficultyLevelOrDefault)));
    }

    private void RefreshView()
    {
        // Drop a stale selection: the selected unit may have just joined a colony (or been removed in combat),
        // so it's no longer in the game — leaving it dangling would mis-route the next map click.
        if (_selectedUnit is not null && !_game.Units.Contains(_selectedUnit))
        {
            _selectedUnit = null;
        }

        // A human explorer that just stepped onto a (non-mounds) Lost City Rumour resolved it inside the move with
        // no return value to read — surface each collected outcome in the status line (the strange-mounds outcome
        // comes via its own panel). Drained here so it catches both an interactive move and a standing-goto move
        // walked during EndTurn. (Empty on a plain selection refresh — harmless.)
        foreach (RumourNotice rumour in _game.TakeRumourNotices())
        {
            _notice = _notice is null ? rumour.Message : $"{_notice}  {rumour.Message}";
        }

        _mapView.ShowState(_game.Map, _game.Explored, _game.CurrentlyVisible);
        _riverLayer.ShowState(_game.Map, _game.Explored, _game.CurrentlyVisible);
        _improvementLayer.ShowState(_game.Map, _game.Explored, _game.CurrentlyVisible);
        _miniMap.ShowState(_game);
        // Outline the selected unit's standing goto destination, if any.
        if (_selectedUnit is { Destination: { } dest })
        {
            _gotoMarker.Position = MapView.TileCentre(dest);
            _gotoMarker.Visible = true;
            _gotoMarker.QueueRedraw();
        }
        else
        {
            _gotoMarker.Visible = false;
        }
        SyncColonyMarkers();
        SyncNativeMarkers();
        SyncRumourMarkers();
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
            $"Turn {_game.Turn} ({_game.CalendarLabel})   |   {subject}   |   seed {_currentSeed}" +
            "   |   B build colony, N new map, F5 save, F9 load";
        if (_notice is not null)
        {
            status += $"   |   ⚠ {_notice}";
            _notice = null;
        }
        _statusLabel.Text = status;

        // The dedicated, classic-style date readout in the HUD (its own panel near the turn controls), distinct
        // from the dev status string above. Presentation-only — reads the Game.CalendarLabel oracle (ADR-006).
        _calendarLabel.Text = _game.CalendarLabel;

        // Selected-unit info readout (type / moves / role / orders / goto) + standing-orders buttons, shown only
        // while a unit is selected; each order button is gated on its Check oracle (ADR-006).
        if (_selectedUnit is { } sel)
        {
            _selectedUnitLabel.Text = DescribeSelectedUnit(sel);
            _fortifyButton.Disabled = !_game.CheckFortify(sel).Allowed;
            _sentryButton.Disabled = !_game.CheckSentry(sel).Allowed;
            _clearOrdersButton.Disabled = sel.Orders == UnitOrders.Active && !sel.IsImproving; // nothing to clear when active and not building
            // Pioneer build orders — each shown only when that improvement can be built here right now (ADR-006 oracle).
            _roadButton.Disabled = !_game.CheckBuildImprovement(sel, TileImprovementType.RoadId).Allowed;
            _plowButton.Disabled = !_game.CheckBuildImprovement(sel, TileImprovementType.PlowId).Allowed;
            _clearForestButton.Disabled = !_game.CheckBuildImprovement(sel, TileImprovementType.ClearForestId).Allowed;
            // Sail to Europe: shown only for ships, enabled once the ship sits on a high-seas tile (the map edge) — the
            // discoverable surface for the existing CheckSailToEurope/SailToEurope command (ADR-006 oracle).
            _sailToEuropeButton.Visible = sel.Type.IsNaval;
            _sailToEuropeButton.Disabled = !_game.CheckSailToEurope(sel).Allowed;
            _selectedUnitPanel.Show();
        }
        else
        {
            _selectedUnitPanel.Hide();
        }

        // Tile-info readout (terrain / resource / occupant), shown only once the player has clicked a tile — empty
        // until then so the camera-centred visual goldens (which never click) are unaffected (ADR-006).
        if (_inspectedTile is { } inspected)
        {
            _tileInfoLabel.Text = DescribeTile(inspected);
            _tileInfoPanel.Show();
        }
        else
        {
            _tileInfoPanel.Hide();
        }

        // The Declare-Independence HUD action appears only once the human may actually declare (FreeCol surfaces the
        // declareIndependence menu item the same way) — gated purely on the CheckDeclareIndependence oracle (ADR-006):
        // a colonial power with ≥ 50% national rebel sentiment, a connected port and still within the colonial era.
        // Hidden the rest of the game (and once the nation has rebelled) so it never clutters the HUD or the goldens.
        _independenceButton.Visible = !_game.IsHumanDefeated
            && _game.CheckDeclareIndependence(_game.HumanPlayer).Allowed;

        UpdateDefeatUi();
        UpdateVictoryUi();
        RecordHighScoreIfGameEnded();

        // A human explorer that stepped onto strange mounds owes an investigate/decline choice — surface the modal
        // (once; resolving it clears the pending state so it won't re-open). Suppressed if the human is defeated.
        if (!_game.IsHumanDefeated && _game.PendingMounds is not null && !_moundsPanel.Visible)
        {
            ((MoundsDecisionPanel)_moundsPanel).Open(_game, outcome =>
            {
                if (!string.IsNullOrEmpty(outcome))
                {
                    _notice = outcome;
                }
                RefreshView();
            });
        }

        // A human with William Brewster (selectRecruit) is due an emigrant — surface the choice dialog (once; the
        // panel re-opens itself for a backlog and clears when the engine clears PendingEmigration). Suppressed on defeat.
        if (!_game.IsHumanDefeated && _game.PendingEmigration is not null && !_emigrationPanel.Visible)
        {
            OpenEmigrationChoicePanel();
        }

        // The home-nation King has made a demand awaiting an answer (a tax rise or a mercenary offer set
        // Game.PendingMonarchDemand during the turn tick) — surface the Monarch dialog (once; resolving it clears the
        // pending demand in GameLogic, so it won't re-open). Suppressed on defeat. The dialog reads the pending demand
        // and forwards the accept/decline to Game.RespondToMonarch (ADR-006 — the monarch rules live in GameLogic).
        if (!_game.IsHumanDefeated && _game.PendingMonarchDemand is not null && !_monarchDialog.Visible)
        {
            _monarchDialog.Open(_game, outcome =>
            {
                if (!string.IsNullOrEmpty(outcome))
                {
                    _notice = outcome;
                }
                RefreshView();
            });
        }

        // A foreign power has proactively offered the human a treaty this turn (alliance / cease-fire, queued in
        // Game.PendingHumanProposals by ProposeProactiveTreaties) — surface the negotiation dialog so the human can
        // accept or decline (once; opening it drains the queue via TakePendingHumanProposals, so it won't re-loop).
        // Suppressed on defeat. PendingHumanProposals is a non-draining peek, so this check is side-effect-free.
        if (!_game.IsHumanDefeated && _game.PendingHumanProposals.Count > 0 && !_negotiationPanel.Visible)
        {
            OpenNegotiationPanel();
        }
    }

    /// <summary>
    /// Reflects human defeat in the HUD: a game-over overlay over the map and a disabled, relabelled End Turn
    /// button. Presentation-only (ADR-006) — defeat is computed by <see cref="Game.IsHumanDefeated"/>; this never
    /// mutates game state and deliberately does <b>not</b> stop the turn loop (a short-circuit in
    /// <see cref="Game.EndTurn"/> would freeze the human's RNG stream 0 and break ADR-009 byte-stability — see the
    /// human-defeat slice). The overlay's full-rect <c>Control</c> swallows map clicks while shown; "New Game"
    /// (and the N hotkey) start a fresh game, which clears the defeat and hides the overlay.
    /// </summary>
    private void UpdateDefeatUi()
    {
        bool defeated = _game.IsHumanDefeated;
        _endTurnButton.Disabled = defeated;
        _endTurnButton.Text = defeated ? "Game Over" : "End Turn";
        if (defeated)
        {
            _gameOverMessage.Text =
                $"You have lost your last colony and all your units on turn {_game.Turn}.\nThe colony is over.";
            // Tidy any panel that happened to be open (e.g. Europe) so it can't be orphaned behind the overlay,
            // which draws on top of and click-blocks the earlier UI siblings.
            _colonyPanel.Hide();
            _europePanel.Hide();
            _nativePanel.Hide();
            _negotiationPanel.Hide();
            _demandPanel.Hide();
            _moundsPanel.Hide();
            _monarchDialog.Hide();
        }
        _gameOverScreen.Visible = defeated;
    }

    /// <summary>
    /// Surfaces the victory / end-of-game statistics screen once the game has a <see cref="Game.Winner"/> (the rebel
    /// broke the REF, or an alternate victory condition fired). Presentation-only (ADR-006): the win is decided in
    /// GameLogic; <see cref="VictoryPanel.Open"/> reads the winner, score breakdown and end-game stats and shows
    /// itself — a no-op while the game is still running. Auto-opened only the first time (the player can Close it and
    /// keep looking around the final board); the panel is not re-forced on subsequent refreshes.
    /// </summary>
    private void UpdateVictoryUi()
    {
        if (_game.Winner is null || _victoryShown)
        {
            return;
        }
        _victoryShown = true;
        ((VictoryPanel)_victoryPanel).Open(_game);
    }

    /// <summary>Opens the victory / end-of-game stats screen directly (the winner's score + final stats). Public so scene tests can drive it.</summary>
    public void OpenVictoryPanel() => ((VictoryPanel)_victoryPanel).Open(_game);

    /// <summary>
    /// Records the human's final score on the persisted leaderboard the first time the game ends — a win
    /// (<see cref="Game.Winner"/> is the human) or the human's defeat (<see cref="Game.IsHumanDefeated"/>). Once only
    /// per game (the <see cref="_highScoreRecorded"/> one-shot), so it cannot double-add as the view refreshes. The
    /// score record + ranking + file all live behind GameLogic / <see cref="HighScoresService"/> (ADR-006): the
    /// controller only decides <i>when</i> and whether it was a win, then hands the entry over. Writes
    /// <c>user://highscores.json</c> — never a game save (the save version is unchanged).
    /// </summary>
    private void RecordHighScoreIfGameEnded()
    {
        if (_highScoreRecorded)
        {
            return;
        }
        bool won = _game.Winner is { } w && w.PlayerId == _game.HumanPlayer.PlayerId;
        bool lost = _game.IsHumanDefeated;
        if (!won && !lost)
        {
            return; // game still running
        }
        _highScoreRecorded = true;
        HighScoresService.Record(_game.RecordHighScore(_game.HumanPlayer, won, _gameId));
    }

    /// <summary>Opens the high-scores leaderboard screen (loads <c>user://highscores.json</c> via <see cref="HighScoresService"/>). Public so the menu and scene tests can drive it.</summary>
    public void OpenHighScoresPanel() => ((HighScoresPanel)_highScoresPanel).Open(HighScoresService.Load());

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

    /// <summary>Draws a marker on each explored Lost City Rumour tile (fog-gated like the settlement markers, ADR-006).</summary>
    private void SyncRumourMarkers()
    {
        foreach (Node child in _rumourLayer.GetChildren())
        {
            child.QueueFree();
        }
        foreach (Position rumour in _game.Map.Rumours)
        {
            if (_game.IsExplored(rumour))
            {
                _rumourLayer.AddChild(new RumourMarker { Position = MapView.TileCentre(rumour) });
            }
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
            // Role short name (e.g. "soldier"/"pioneer", or "default" for unarmed) so the marker can pick the
            // role-specific FreeCol sprite — a colonist-soldier looks different from a plain colonist.
            string roleShortName = unit.RoleId[(unit.RoleId.LastIndexOf('.') + 1)..];
            marker.SetUnit(unit.Type.ShortName, roleShortName);
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
