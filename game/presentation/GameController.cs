using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.App;
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
    /// The autosave file (under <see cref="SavesDir"/>). Written automatically at the end of a player turn when the
    /// <see cref="SettingsModel.AutosavePeriod"/> option is non-zero and the turn is a multiple of it. A <b>distinct</b>
    /// slot the manual save dialog never overwrites, and which the load dialog lists as its own entry. (86d3f0vb8)
    /// </summary>
    public static string AutosavePath => $"{SavesDir}/autosave.json";

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
    /// Companion to <see cref="PendingWorldSize"/>: the <b>custom-house boycott-smuggling</b> game option the human
    /// chose at New Game (FreeCol's <c>model.option.customIgnoreBoycott</c>, the <c>gameOptions.colony</c> group,
    /// surfaced by <see cref="NewGameDialog"/>). <b>Null = no pick → the ruleset's parsed spec default</b> (classic
    /// <b>on</b> — so a default new game is byte-identical, ADR-009). Set by the dialog's Start and consumed (and
    /// cleared) in <see cref="NewGame"/>, where it is applied to the freshly-loaded ruleset via
    /// <see cref="Ruleset.WithCustomIgnoreBoycott"/> so a colony's custom house auto-sell smuggles a boycotted good
    /// (on) or skips it (off). Static because it must survive the scene change, like <see cref="PendingWorldSize"/>.
    /// <b>Session-only</b> — the override is not written to the save (a reload re-derives the option from the variant's
    /// spec; persisting it would bump the save format, matching the victory-condition / fog-of-war seams, 86d3e4bu0).
    /// </summary>
    public static bool? PendingCustomIgnoreBoycott { get; set; }

    /// <summary>
    /// Companion to <see cref="PendingWorldSize"/>: the <b>ruleset / variant</b> the human chose at New Game (the
    /// scenario selector surfaced by <see cref="NewGameDialog"/> — FreeCol's "rules" dropdown on its New-game panel).
    /// <b>Null = no pick → <see cref="GameVariants.Default"/></b> (Colonial America / Classic — so a default new game is
    /// byte-identical, ADR-009). Set by the dialog's Start and consumed (and cleared) in <see cref="NewGame"/>, where it
    /// becomes the controller's active <c>_variant</c> so the new game loads that variant's ruleset (its nations,
    /// Founding Fathers, units, terrain — ADR-018). This is the seam a future variant (e.g. Australia) plugs into: it
    /// becomes a dropdown entry in <see cref="GameVariants.All"/>, not a code change. Static because it must survive the
    /// scene change, like <see cref="PendingWorldSize"/>. The chosen variant <b>is</b> persisted (the save already
    /// records its variant id so it reloads under the right ruleset — no new save field).
    /// </summary>
    public static GameVariant? PendingVariant { get; set; }

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
    private Button _cashInTreasureButton = null!;
    private Button _skipButton = null!;
    private Button _disbandButton = null!;
    private MiniMap _miniMap = null!;
    private MapControlsOverlay _mapControls = null!;
    private PanelContainer _colonyPanel = null!;
    private PanelContainer _europePanel = null!;
    private PanelContainer _nativePanel = null!;
    private PanelContainer _demandPanel = null!;
    private PanelContainer _moundsPanel = null!;
    private MonarchDialog _monarchDialog = null!;
    private EmigrationChoicePanel _emigrationPanel = null!;
    private PreCombatPanel _preCombatPanel = null!;
    private TurnMessagePanel _turnMessagePanel = null!;
    private MessageLogPanel _messageLogPanel = null!;
    private readonly List<MessageLogPanel.Entry> _messageLog = []; // history of per-turn notices; persisted across save/load from save v59 (round-tripped through SaveGame.MessageLog)
    private PanelContainer _tradeRoutePanel = null!;
    private PanelContainer _colonyReportPanel = null!;
    private PanelContainer _findSettlementPanel = null!;
    private PanelContainer _foundingFatherPanel = null!;
    private PanelContainer _colopediaPanel = null!;
    private PanelContainer _victoryPanel = null!;
    private PanelContainer _highScoresPanel = null!;
    private PanelContainer _declarationPanel = null!;
    private PanelContainer _negotiationPanel = null!;
    private AdvisorPanel _advisorPanel = null!;
    private Button _independenceButton = null!;
    private Button _endTurnButton = null!;
    /// <summary>The always-on bottom-right HUD button column (Europe/TradeRoutes/Reports/MessageLog/Colopedia/HighScores/Diplomacy/EndTurn) — hidden while a full-screen panel is open so it never floats over or eats clicks meant for the panel (86d3fr6bc). The IndependenceButton is handled separately (it carries its own game-state gate). Collected in <see cref="_Ready"/>.</summary>
    private Button[] _cornerHudButtons = null!;
    private Control _gameOverScreen = null!;
    private Label _gameOverMessage = null!;
    private Unit? _selectedUnit;
    private Position? _inspectedTile;

    /// <summary>The tile the cursor is hovering (86d3fq1nk). While set, the HUD tile-info panel shows that tile's terrain + a colonist-yield preview; null falls back to the clicked <see cref="_inspectedTile"/>.</summary>
    private Position? _hoveredTile;
    private string? _notice;
    private bool _gotoMode;

    /// <summary>
    /// Whether the game has <b>unsaved changes</b> since the last save / load / new game (86d3fq1v8). Set by
    /// <see cref="MarkDirty"/> on any state-mutating command and on End Turn; cleared by <see cref="StartGame"/> (a fresh
    /// or loaded game starts clean) and after a successful save (<see cref="MarkClean"/>). The pause menu reads
    /// <see cref="HasUnsavedChanges"/> so quitting a dirty game prompts "save before quitting?" instead of a bare confirm.
    /// A presentation-only convenience (ADR-006) — not game state, never persisted; it tracks the UI's save bookkeeping,
    /// not the rules.
    /// </summary>
    private bool _dirty;

    /// <summary>
    /// Whether the "name the new world" dialog is currently on screen (86d3fq1fn) — a re-entrancy guard so a second
    /// move/refresh while the modal prompt is open does not stack a second copy of it. Cleared when the dialog closes.
    /// </summary>
    private bool _newWorldNameDialogOpen;

    /// <summary>
    /// Whether the <b>rename-unit</b> dialog is currently on screen (86d3drmzu) — a re-entrancy guard so a second
    /// right-click / refresh while the modal prompt is open does not stack a second copy of it. Cleared when the
    /// dialog closes (confirm or cancel/Escape).
    /// </summary>
    private bool _renameUnitDialogOpen;

    /// <summary>
    /// Whether the <b>sail-to-Europe</b> auto-prompt is currently on screen (86d3fpzqp) — a re-entrancy guard so a
    /// second move/refresh while the modal prompt is open does not stack a second copy of it. Cleared when the dialog
    /// closes (confirm or cancel/Escape).
    /// </summary>
    private bool _sailPromptDialogOpen;

    /// <summary>
    /// A ship that has just <b>crossed onto a high-seas tile</b> this map-click and so is owed a "sail to Europe?"
    /// prompt (86d3fpzqp, FreeCol <c>moveHighSeas</c>). Set by the move handler only on a fresh crossing (the ship was
    /// not on the high seas before the move and carries no standing Europe goto) and consumed once by
    /// <see cref="MaybePromptSailToEurope"/> after the view refresh — so a plain re-selection of a ship already sitting
    /// on the high seas never re-nags. Null the rest of the time.
    /// </summary>
    private Unit? _pendingSailPromptShip;

    /// <summary>
    /// Unit ids the player has <b>skipped</b> for this turn (Space — 86d3f0vuy). A skipped unit is passed over by the
    /// W-cycle (<see cref="SelectNextUnitToMove"/>) until the set clears at turn rollover (in <see cref="OnEndTurnPressed"/>).
    /// Session-only / controller-side and <b>never persisted</b> (ADR-009) — it's a transient input convenience, not game
    /// state, so the W-cycle oracle <see cref="Game.NextUnitToMove"/> stays pure and the save format is unchanged.
    /// </summary>
    private readonly HashSet<int> _skippedThisTurn = [];

    /// <summary>The toggleable F1 keys legend overlay (built from the authoritative <see cref="KeyBindings"/> table).</summary>
    private Label? _keysLegend;
    private bool _victoryShown;
    private bool _highScoreRecorded;
    private string _gameId = "";
    private GotoMarker _gotoMarker = null!;

    public override void _Ready()
    {
        _mapView = GetNode<MapView>("MapView");
        _mapView.HoveredTileChanged += OnHoveredTileChanged; // tile-yield-on-hover preview (86d3fq1nk)
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
        // Treasure-train order: cash in the carried gold (at an owned colony — King's cut — or aboard a galleon
        // docked in Europe — fee-free), gated on CheckCashInTreasureTrain (ADR-006); shows the value then confirms.
        _cashInTreasureButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/CashInTreasureButton");
        _cashInTreasureButton.Pressed += CashInSelectedTreasureTrain;
        // Skip (Space) + Disband (D) order buttons — Skip flags the unit skipped-this-turn and cycles on; Disband
        // prompts for confirmation then removes the unit. Both share the keyboard paths (SkipSelectedUnit / DisbandSelectedUnit).
        _skipButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/SkipButton");
        _skipButton.Pressed += SkipSelectedUnit;
        _disbandButton = GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/DisbandButton");
        _disbandButton.Pressed += DisbandSelectedUnit;
        _miniMap = GetNode<MiniMap>("UI/MiniMap");
        _miniMap.TileSelected += CenterCameraOnTile;
        GetNode<Button>("UI/MiniMap/ZoomInButton").Pressed += _miniMap.ZoomIn;
        GetNode<Button>("UI/MiniMap/ZoomOutButton").Pressed += _miniMap.ZoomOut;
        // On-screen map-controls cluster (zoom +/− + recentre) — built in code, added to the HUD CanvasLayer (86d3fq0ch).
        // The zoom buttons drive the camera's discrete zoom; recentre snaps the camera back to the player's current focus.
        _mapControls = new MapControlsOverlay { Name = "MapControls" };
        GetNode<CanvasLayer>("UI").AddChild(_mapControls);
        _mapControls.Build(GetNode<CameraController>("Camera"));
        _mapControls.RecentreRequested += RecentreCamera;
        _advisorPanel = new AdvisorPanel();
        GetNode<CanvasLayer>("UI").AddChild(_advisorPanel);
        _colonyPanel = GetNode<PanelContainer>("UI/ColonyPanel");
        _europePanel = GetNode<PanelContainer>("UI/EuropePanel");
        _nativePanel = GetNode<PanelContainer>("UI/NativeSettlementPanel");
        _demandPanel = GetNode<PanelContainer>("UI/NativeDemandPanel");
        _moundsPanel = GetNode<PanelContainer>("UI/MoundsDecisionPanel");
        _monarchDialog = GetNode<MonarchDialog>("UI/MonarchDialog");
        _emigrationPanel = GetNode<EmigrationChoicePanel>("UI/EmigrationChoicePanel");
        _preCombatPanel = GetNode<PreCombatPanel>("UI/PreCombatPanel");
        _turnMessagePanel = GetNode<TurnMessagePanel>("UI/TurnMessagePanel");
        _messageLogPanel = GetNode<MessageLogPanel>("UI/MessageLogPanel");
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
        GetNode<Button>("UI/MessageLogButton").Pressed += OpenMessageLogPanel;
        GetNode<Button>("UI/ColopediaButton").Pressed += OpenColopediaPanel;
        GetNode<Button>("UI/HighScoresButton").Pressed += OpenHighScoresPanel;
        GetNode<Button>("UI/DiplomacyButton").Pressed += OpenNegotiationPanel;
        _independenceButton.Pressed += OpenDeclarationPanel;
        // The bottom-right HUD button column is declared after the full-screen panels in the scene, so as a later sibling
        // it draws on top and receives input first — eating clicks over an open ColonyPanel/EuropePanel's bottom-right
        // footprint (86d3fr6bc). Collect the always-on buttons and hide the whole column whenever a full-screen panel is
        // open (wired to each panel's VisibilityChanged + RefreshView), so it neither floats over the panel nor steals
        // its clicks; it returns when the panel closes.
        _cornerHudButtons = new[]
        {
            GetNode<Button>("UI/EuropeButton"), GetNode<Button>("UI/TradeRoutesButton"),
            GetNode<Button>("UI/ReportsButton"), GetNode<Button>("UI/MessageLogButton"),
            GetNode<Button>("UI/ColopediaButton"), GetNode<Button>("UI/HighScoresButton"),
            GetNode<Button>("UI/DiplomacyButton"), _endTurnButton,
        };
        _colonyPanel.VisibilityChanged += RefreshHudButtonVisibility;
        _europePanel.VisibilityChanged += RefreshHudButtonVisibility;
        GetNode<Button>("UI/ColopediaPanel/VBox/CloseButton").Pressed += () => _colopediaPanel.Hide();
        GetNode<Button>("UI/ColonyReportPanel/VBox/CloseButton").Pressed += () => _colonyReportPanel.Hide();
        GetNode<Button>("UI/VictoryPanel/VBox/CloseButton").Pressed += () => _victoryPanel.Hide();
        ((VictoryPanel)_victoryPanel).OnContinuePlaying = OnContinuePlaying; // keep playing past victory (FreeCol continuePlaying)
        ((VictoryPanel)_victoryPanel).OnRetire = OnRetire;                   // record the score + end the game (FreeCol retire)
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
        // The chosen custom-house boycott-smuggling toggle (null = no pick → the spec default, classic on, byte-identical).
        bool? customIgnoreBoycott = PendingCustomIgnoreBoycott;
        // The setup dials (86d3fq1df/86d3fq1fd/86d3fq13u/86d3fq18b/86d3fq1b8/86d3fq0za) ride NewGameDialog statics; null →
        // the classic default for each, so an unset dial is byte-identical (ADR-009).
        int? rivalCount = NewGameDialog.PendingRivalCount;
        int? startYear = NewGameDialog.PendingStartYear;
        MapGenerationOptions? mapOptions = NewGameDialog.PendingMapOptions;
        int? rumourNumber = NewGameDialog.PendingRumourNumber;
        NationalAdvantages? nationalAdvantages = NewGameDialog.PendingNationalAdvantages;
        // The chosen variant becomes the active ruleset source (null = no pick → the default Classic variant). Set BEFORE
        // StartNewGame so the new game loads the picked variant's ruleset (ADR-018); the save then records its id.
        _variant = PendingVariant ?? GameVariants.Default;
        PendingWorldSize = null;
        PendingLandMass = null;
        PendingDifficulty = null;
        PendingMapSource = null;
        PendingNation = null;
        PendingLandStyle = null;
        PendingVictoryConditions = null;
        PendingFogOfWar = null;
        PendingCustomIgnoreBoycott = null;
        PendingVariant = null;
        NewGameDialog.PendingRivalCount = null;
        NewGameDialog.PendingStartYear = null;
        NewGameDialog.PendingMapOptions = null;
        NewGameDialog.PendingRumourNumber = null;
        NewGameDialog.PendingNationalAdvantages = null;

        // Picking the seed may be non-deterministic (player convenience);
        // the game itself is fully determined by the chosen seed.
        StartNewGame(Seed != 0 ? Seed : ((ulong)GD.Randi() << 32) | GD.Randi(), size, land, difficulty, mapSource, nation, landStyle, victory, fogOfWar, customIgnoreBoycott, rivalCount, startYear, mapOptions, rumourNumber, nationalAdvantages);
    }

    /// <summary>Starts a new game from an explicit seed at the shipped-default world size / difficulty / map / nation-less human (tests, visual goldens — ADR-009).</summary>
    public void StartNewGame(ulong seed) =>
        StartNewGame(seed, WorldSizeOptions.DefaultSize, WorldSizeOptions.DefaultLandMass, DifficultyLevels.Default, MapSource.Random);

    /// <summary>Starts a new game from an explicit seed, world size / land amount, difficulty level, map source, (optional) human nation, (optional) landmass style, (optional) victory-condition overrides, (optional) fog-of-war override and (optional) custom-house boycott-smuggling override (forwarded from the new-game options). The active <c>_variant</c>'s ruleset is loaded under the chosen level so its balance matches, the level is recorded for the save, a fixed <paramref name="mapSource"/> ignores the size/land/style args (its grid sets the dimensions), <paramref name="humanNationId"/> (null = no pick) seeds the human's national advantage + colony names, <paramref name="landStyle"/> (default <see cref="LandStyle.Continent"/>) shapes the random map's land, <paramref name="victory"/> (null = the ruleset's parsed spec defaults) flips which alternative victory conditions <see cref="Game.Winner"/> evaluates, <paramref name="fogOfWar"/> (null = the spec default, classic on) flips whether explored-but-unseen tiles are re-hidden, and <paramref name="customIgnoreBoycott"/> (null = the spec default, classic on) flips whether a custom house smuggles boycotted goods — all three session-only, not persisted (86d3drn64, 86d3dzdw3, 86d3e4bu0). The chosen <em>variant</em> is set by the caller (<see cref="NewGame"/>) before this runs and is recorded in the save.</summary>
    public void StartNewGame(ulong seed, WorldSize size, LandMass landMass, DifficultyLevel difficulty, MapSource mapSource, string? humanNationId = null, LandStyle landStyle = LandStyle.Continent, (bool DefeatRef, bool DefeatEuropeans, bool DefeatHumans)? victory = null, bool? fogOfWar = null, bool? customIgnoreBoycott = null, int? rivalCount = null, int? startYear = null, MapGenerationOptions? mapOptions = null, int? rumourNumber = null, NationalAdvantages? nationalAdvantages = null)
    {
        _currentSeed = seed;
        // Load the active variant's ruleset under the chosen difficulty; if the player picked victory conditions / fog
        // of war / custom-house smuggling, apply them to this freshly-parsed (never-shared) instance before building the
        // game — a configuration override (of which win checks fire / how visibility is derived / whether a custom house
        // smuggles), not a rules change (ADR-006). Null leaves each spec default untouched, so a default new game is
        // byte-identical.
        Ruleset ruleset = _variant.LoadRuleset(difficulty.Id);
        if (victory is { } v)
        {
            ruleset = ruleset.WithVictoryConditions(v.DefeatRef, v.DefeatEuropeans, v.DefeatHumans);
        }
        if (fogOfWar is { } fog)
        {
            ruleset = ruleset.WithFogOfWar(fog);
        }
        if (customIgnoreBoycott is { } smuggle)
        {
            ruleset = ruleset.WithCustomIgnoreBoycott(smuggle);
        }
        if (startYear is { } sy)
        {
            ruleset = ruleset.WithStartingYear(sy); // the start-year dial (86d3fq1fd); session-only override, no save bump
        }
        StartGame(Game.New(
            ruleset, _currentSeed, size.Width, size.Height,
            landMassFraction: landMass.Fraction, difficultyLevelId: difficulty.Id, mapSource: mapSource,
            humanNationId: humanNationId, landStyle: landStyle,
            foreignPowerCount: rivalCount, mapOptions: mapOptions,
            rumourNumber: rumourNumber ?? LostCityRumourGenerator.DefaultRumourNumber,
            nationalAdvantages: nationalAdvantages ?? NationalAdvantages.Selectable));
    }

    private void StartGame(Game game)
    {
        _game = game;
        _selectedUnit = null;
        _inspectedTile = null;
        _hoveredTile = null; // a fresh/loaded game starts with no hovered tile (cleared so the tile-info panel is empty)
        _notice = null;
        _messageLog.Clear(); // a fresh/loaded game starts with no logged notices; a load re-fills it after this (RestoreMessageLog)
        UnitMarker.ResetMoveMemory(); // drop per-unit last-tile cache on a game swap so a reused tile/id can't spuriously slide (86d3fq26m)
        MarkClean(); // a fresh or just-loaded game matches what's on disk — no unsaved changes yet (86d3fq1v8)
        _skippedThisTurn.Clear(); // a fresh/loaded game starts with no skipped units (session-only set; 86d3f0vuy)
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
        // Hand the camera the map's pixel extent (the diamond centres of the four corner tiles) so pan/edge-scroll/zoom
        // clamp the camera centre inside the map (86d3fq22p) — a view constraint, not a rule (ADR-006). The isometric
        // projection's min/max screen X/Y fall on the corner tiles, so SetMapBounds derives the rectangle from them.
        var camera = GetNode<CameraController>("Camera");
        Vector2 c0 = MapView.TileCentre(new Position(0, 0));
        Vector2 c1 = MapView.TileCentre(new Position(_game.Map.Width - 1, 0));
        Vector2 c2 = MapView.TileCentre(new Position(0, _game.Map.Height - 1));
        Vector2 c3 = MapView.TileCentre(new Position(_game.Map.Width - 1, _game.Map.Height - 1));
        camera.SetMapBounds(
            new Vector2(Mathf.Min(Mathf.Min(c0.X, c1.X), Mathf.Min(c2.X, c3.X)), Mathf.Min(Mathf.Min(c0.Y, c1.Y), Mathf.Min(c2.Y, c3.Y))),
            new Vector2(Mathf.Max(Mathf.Max(c0.X, c1.X), Mathf.Max(c2.X, c3.X)), Mathf.Max(Mathf.Max(c0.Y, c1.Y), Mathf.Max(c2.Y, c3.Y))));
        camera.Position = MapView.TileCentre(focus);
        // Switch the background music to the in-game context as the game starts. Both the menu and gameplay currently
        // share FreeCol's single Background playlist, so this keeps the music seamless (SetContext only restarts the
        // playlist when the mood actually changes) — the seam is here for a future war/tension context (86d3fq1wy).
        SetMusicContext(MusicContext.Background);
        // Cue the human player's national anthem once over the running background music (FreeCol plays it at game start).
        PlayAnthem(_game.HumanPlayer.NationId);
        RefreshView();
    }

    /// <summary>Recenters the main camera on a map tile — the minimap's click-to-recenter target (ADR-006). Clamped to the map bounds.</summary>
    private void CenterCameraOnTile(Position tile) =>
        GetNode<CameraController>("Camera").CenterOn(MapView.TileCentre(tile));

    /// <summary>
    /// Snaps the camera back to the player's current focus — the selected unit, else the next unit needing orders, else
    /// the first own colony, else the map centre. The on-screen map-controls "recentre" affordance (86d3fq0ch); a view
    /// action only (ADR-006).
    /// </summary>
    private void RecentreCamera()
    {
        Position focus = _selectedUnit is { IsOnMap: true } sel ? sel.Position
            : _game.NextUnitToMove(_game.HumanPlayer)?.Position
            ?? _game.PlayerUnits.FirstOrDefault(u => u.IsOnMap)?.Position
            ?? _game.Colonies.FirstOrDefault(c => c.OwnerId == _game.HumanPlayer.PlayerId)?.Position
            ?? new Position(_game.Map.Width / 2, _game.Map.Height / 2);
        CenterCameraOnTile(focus);
    }

    /// <summary>Applies a standing order to the selected unit when its <paramref name="allowed"/> check passes, then refreshes (ADR-006).</summary>
    private void ApplyUnitOrder(System.Func<Unit, bool> allowed, System.Action<Unit> apply, string notice)
    {
        if (_selectedUnit is { } u && allowed(u))
        {
            apply(u);
            MarkDirty(); // a unit order changed game state → unsaved changes (86d3fq1v8)
            _notice = notice;
            RefreshView();
        }
    }

    /// <summary>One-line readout for the selected-unit HUD panel (custom name / type / moves / role / orders / goto). Reads-only (ADR-006).</summary>
    private string DescribeSelectedUnit(Unit u)
    {
        string role = u.HasDefaultRole ? "" : $"  ·  {u.RoleId[(u.RoleId.LastIndexOf('.') + 1)..]}";
        string orders = u.Orders == UnitOrders.Active ? "" : $"  ·  {u.Orders.ToString().ToLowerInvariant()}";
        // An in-progress tile improvement: show what's being built and the turns of work left.
        string building = u.WorkImprovementId is { } imp
            ? $"  ·  building {imp[(imp.LastIndexOf('.') + 1)..]} ({u.WorkTurnsLeft})"
            : "";
        string goingTo = u.IsGoingTo ? "  ·  going to" : "";
        // A christened unit leads with its custom name (86d3drmzu), the generic type name following as its class.
        string named = u.Name is { Length: > 0 } name ? $"\"{name}\"  ·  " : "";
        return $"{named}{u.Type.ShortName}  ·  moves {u.MovementLeft}/{u.Type.Movement}{role}{orders}{building}{goingTo}";
    }

    /// <summary>
    /// The selected unit's display label for menus/prompts (86d3drmzu): its custom <see cref="Unit.Name"/> when
    /// christened, else its generic type short name. Presentation-only (ADR-006) — the rename rule itself lives in
    /// <see cref="Game.NameUnit"/>.
    /// </summary>
    private static string UnitDisplayName(Unit u) => u.Name is { Length: > 0 } name ? name : u.Type.ShortName;

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

    /// <summary>
    /// Records the tile the cursor is hovering and refreshes the HUD so the tile-info panel shows that tile's yield
    /// preview (86d3fq1nk). Off-map → clears the hover (the panel falls back to the clicked tile). A view-only callback
    /// from <see cref="MapView.HoveredTileChanged"/>; the yields come from the public oracle (ADR-006).
    /// </summary>
    private void OnHoveredTileChanged(Position? tile)
    {
        if (_game is null)
        {
            return; // hover events can arrive before the first game is built; ignore until there is one
        }
        _hoveredTile = tile;
        // Only the tile-info readout depends on the hover, so refresh just that — a full RefreshView each mouse-tile
        // crossing would be wasteful. RefreshTileInfo is also called by the main RefreshView.
        RefreshTileInfo();
    }

    /// <summary>
    /// The HUD readout for a hovered tile (86d3fq1nk): the standard terrain/resource/occupant lines (<see cref="DescribeTile"/>)
    /// plus a colonist-yield <b>preview</b> line — the goods a colonist could produce there with each yield
    /// ("Yield: grain 5, cotton 3"), read from the public <see cref="Game.TileWorkOptions"/> oracle. Fog-gated via
    /// <see cref="DescribeTile"/> (unexplored → "Unexplored", no yields); a water/barren tile shows no yield line. Reads only (ADR-006).
    /// </summary>
    private string DescribeTileYield(Position tile)
    {
        string line = DescribeTile(tile);
        if (!_game.IsExplored(tile))
        {
            return line; // "Unexplored" — reveal no production for a tile the player hasn't seen
        }
        var options = _game.TileWorkOptions(tile);
        if (options.Count > 0)
        {
            line += "\nYield: " + string.Join(", ", options.Select(o => $"{o.GoodsId[(o.GoodsId.LastIndexOf('.') + 1)..]} {o.Yield}"));
        }
        return line;
    }

    /// <summary>
    /// Updates the HUD tile-info readout: the hovered tile's yield preview when the cursor is over the map (86d3fq1nk),
    /// else the last clicked ("inspected") tile's terrain/occupant readout, else hidden. Shared by the hover callback and
    /// the main <see cref="RefreshView"/> so both keep the panel consistent.
    /// </summary>
    private void RefreshTileInfo()
    {
        if (_hoveredTile is { } hovered)
        {
            _tileInfoLabel.Text = DescribeTileYield(hovered);
            _tileInfoPanel.Show();
        }
        else if (_inspectedTile is { } inspected)
        {
            _tileInfoLabel.Text = DescribeTile(inspected);
            _tileInfoPanel.Show();
        }
        else
        {
            _tileInfoPanel.Hide();
        }
    }

    private void OnEndTurnPressed()
    {
        // Snapshot the human's total buildings before the turn resolves so we can detect a just-completed construction
        // (the engine surfaces no build-complete notice — we observe the state change, presentation-only, ADR-006).
        int buildingsBefore = HumanBuildingCount();
        MarkDirty(); // a turn resolved → unsaved changes (86d3fq1v8); cleared by the autosave below or a manual save
        _game.EndTurn();
        // A colony finished a building this turn → play the build-complete cue (FreeCol's buildingComplete event).
        if (HumanBuildingCount() > buildingsBefore)
        {
            PlaySound(SoundEvent.BuildingComplete);
        }
        // A fresh turn: every unit may need orders again, so the session-only skip set (Space) clears at rollover (86d3f0vuy).
        _skippedThisTurn.Clear();
        // Surface what the human suffered or received during the AI phase (no return value to read, unlike a
        // player-initiated attack): raids on units (1c-2/1c-3a′), native pillages of colonies, captures of colonies
        // (1c-3f), then custom-house auto-sales. Notices are in deterministic order; instead of cramming them into
        // the one-line status bar, each is formatted to a player-facing row and shown together in the dismissible
        // TurnMessagePanel (FreeCol's ReportTurnPanel). Formatting (and the rules) stay here / in GameLogic (ADR-006).
        // Each notice is tagged with a MessageCategory so the message log can group/filter by kind (combat / natives /
        // economy / monarch / colony). Combat raids and AI captures of a colony are combat; native pillages are a
        // native event; warehouse overflow and custom-house sales are economic; famine/starvation are colony events;
        // the King's decrees are monarch events. (FreeCol's per-type ModelMessage "show this kind" toggles.)
        var entries = _game.CombatNotices.Select(n => Logged(MessageCategory.Combat, FormatCombatNotice(n)))
            .Concat(_game.ColonyRaidNotices.Select(n => Logged(MessageCategory.Natives, FormatColonyRaidNotice(n))))
            .Concat(_game.ColonyLossNotices.Select(n => Logged(MessageCategory.Combat, FormatColonyLossNotice(n))))
            .Concat(_game.WarehouseOverflowNotices.Select(n => Logged(MessageCategory.Economy, FormatWarehouseOverflowNotice(n)))) // warehouse spilling over capacity
            .Concat(_game.ColonyFamineNotices.Select(n => Logged(MessageCategory.Colony, FormatColonyFamineNotice(n))))           // a colony lost a colonist to famine
            .Concat(_game.ColonyStarvedNotices.Select(n => Logged(MessageCategory.Colony, FormatColonyStarvedNotice(n))))         // a colony starved out of existence
            .Concat(_game.MonarchDecreeNotices.Select(n => Logged(MessageCategory.Monarch, FormatMonarchDecreeNotice(n))))         // immediate King's decrees (war/peace/tax/support/REF)
            .Concat(_game.FirstContactNotices.Select(n => Logged(MessageCategory.Diplomacy, FormatFirstContactNotice(n))))         // the human met a new colonial power (FP-6a)
            .Concat(_game.StanceChangeNotices.Select(n => Logged(MessageCategory.Diplomacy, FormatStanceChangeNotice(n))))         // a turn-driven stance shift with a rival (FP-6b)
            .Concat(_game.CustomHouseSaleNotices.Select(n => Logged(MessageCategory.Economy, FormatCustomHouseSaleNotice(n))))
            .Concat(_game.PriceChangeNotices.Select(n => Logged(MessageCategory.Economy, FormatPriceChangeNotice(n))))             // a Europe-market good's price rose or fell (86d3fpz0p)
            .ToList();
        if (_game.IsHumanDefeated)
        {
            // The AI phase took the human's last colony/unit — surface the defeat after the loss notice that caused it.
            entries.Add(Logged(MessageCategory.Combat, "💀 You have been defeated — your last colony and units are gone."));
        }
        // Keep a record so the player can re-open the log later. Persisted across save/load from save v59 (the controller
        // round-trips _messageLog through SaveGame.MessageLog — see SaveTo / LoadFrom). The FULL set is logged regardless
        // of the per-category popup preference — silencing only suppresses the popup, never the log.
        if (entries.Count > 0)
        {
            _messageLog.Add(new MessageLogPanel.Entry(_game.Turn, entries));
        }
        // The dismissible turn-message panel shows what just happened this turn — but only the categories the player has
        // NOT silenced (the popup-vs-log-silently preference, 86d3fq1tc): a silenced category still lands in the log
        // above, it just doesn't pop the panel. So filter the popup rows by the live SilencedMessageCategories client
        // option (every category pops up by default). The attention cue and the panel both key off the popup rows, so a
        // turn whose every event is silenced neither chimes nor pops (the build-complete cue above is more specific, so
        // don't double up when that was the only thing this turn).
        var silenced = GetNodeOrNull<SettingsService>("/root/Settings")?.Settings.SilencedMessageCategories;
        List<string> popupRows = entries
            .Where(e => silenced is null || !silenced.Contains(e.Category))
            .Select(e => e.Text)
            .ToList();
        if (popupRows.Count > 0)
        {
            PlaySound(SoundEvent.Alert);
        }
        _turnMessagePanel.Open(popupRows); // no-op (stays hidden) when no un-silenced events this turn
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

        MaybePromptNewWorldName(); // a goto/auto-move that landed a unit ashore this turn may owe the name-the-new-world prompt (86d3fq1fn)
        MaybeAutosave();
    }

    /// <summary>
    /// Writes the autosave at the end of a player turn when the <see cref="SettingsModel.AutosavePeriod"/> option is on
    /// (non-zero) and the just-finished turn is a multiple of it — e.g. period 1 saves every turn, period 5 saves at
    /// turns 5, 10, 15… It reuses the existing <see cref="SaveTo"/> path (no new save format) but targets the dedicated
    /// <see cref="AutosavePath"/>, which the manual save slots never touch. Resolves the live setting from the
    /// <c>/root/Settings</c> autoload; if it is absent (e.g. a bare test scene), autosaving is simply skipped.
    /// FreeCol's <c>ClientOptions.AUTOSAVE_PERIOD</c>. (86d3f0vb8)
    /// </summary>
    private void MaybeAutosave()
    {
        int period = GetNodeOrNull<SettingsService>("/root/Settings")?.Settings.AutosavePeriod ?? 0;
        if (period > 0 && _game.Turn % period == 0)
        {
            SaveTo(AutosavePath);
        }
    }

    /// <summary>Pairs a formatted notice string with its <see cref="MessageCategory"/> for the filterable message log.</summary>
    private static MessageLogPanel.LogMessage Logged(MessageCategory category, string text) => new(category, text);

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

    /// <summary>
    /// Turns a Europe-market price-change notice into a turn-message row (FreeCol's <c>model.market.priceIncrease</c>/
    /// <c>priceDecrease</c> "told about price" message). Phrases it as the good rising or falling, and surfaces the new
    /// buy/sell prices so the player can react without opening the Trade report. The notice carries the buy (ask) price
    /// it keys off; the sell (bid) rides alongside.
    /// </summary>
    private string FormatPriceChangeNotice(PriceChangeNotice notice)
    {
        string goods = _game.Ruleset.Goods(notice.GoodsId).ShortName;
        string direction = notice.NewPrice > notice.OldPrice ? "rose" : "fell";
        return $"⚖ The price of {goods} {direction} to {notice.SellPrice}/{notice.NewPrice} (sell/buy).";
    }

    /// <summary>Turns a warehouse-overflow notice into a turn-message row (goods wasted over the warehouse cap).</summary>
    private string FormatWarehouseOverflowNotice(WarehouseOverflowNotice notice) =>
        $"📦 {notice.ColonyName}'s warehouse is full — {notice.Wasted} {_game.Ruleset.Goods(notice.GoodsId).ShortName} spoiled this turn.";

    /// <summary>Turns a survivable-famine notice into a turn-message row (a colony lost a colonist but lives on).</summary>
    private static string FormatColonyFamineNotice(ColonyFamineNotice notice) =>
        $"🍞 {notice.ColonyName} could not feed everyone — a colonist starved (population now {notice.PopulationAfter}).";

    /// <summary>Turns a colony-destroyed-by-starvation notice into a turn-message row.</summary>
    private static string FormatColonyStarvedNotice(ColonyStarvedNotice notice) =>
        $"☠ {notice.ColonyName} starved and was lost at ({notice.Position.X},{notice.Position.Y}).";

    /// <summary>
    /// Turns an immediate King's-decree notice into a turn-message row (the no-choice monarch actions — lower/waive
    /// tax, declare war/peace on the player's behalf, free support, REF growth). The tax-rise and mercenary
    /// <em>demands</em> never reach here; they surface through <see cref="MonarchDialog"/> instead.
    /// </summary>
    private string FormatMonarchDecreeNotice(MonarchDecreeNotice notice)
    {
        switch (notice.Action)
        {
            case MonarchAction.LowerTaxWar:
            case MonarchAction.LowerTaxOther:
                return $"👑 The Crown lowered your tax rate to {notice.TaxRate}%.";
            case MonarchAction.WaiveTax:
                return "👑 The Crown graciously waived a tax increase this year.";
            case MonarchAction.DeclareWar:
                string atWar = notice.RivalNationId is { } w ? NationLabel(w) : "a rival power";
                string support = notice.UnitCount > 0
                    ? $" He sends {notice.UnitCount} units" + (notice.Gold > 0 ? $" and {notice.Gold} gold to aid the fight." : " to aid the fight.")
                    : "";
                return $"👑 The Crown declared war on the {atWar} on your behalf.{support}";
            case MonarchAction.DeclarePeace:
                string atPeace = notice.RivalNationId is { } p ? NationLabel(p) : "a rival power";
                return $"👑 The Crown made peace with the {atPeace} on your behalf.";
            case MonarchAction.SupportSea:
                return $"👑 The Crown sent {notice.UnitCount} warship(s) to your defence.";
            case MonarchAction.SupportLand:
                return $"👑 The Crown sent {notice.UnitCount} soldier(s) to your defence.";
            case MonarchAction.AddToRef:
                return $"👑 The Crown reinforced the Royal Expeditionary Force with {notice.UnitCount} unit(s).";
            default:
                return "👑 The Crown issued a decree.";
        }
    }

    /// <summary>Turns a first-contact notice into a turn-message row — the human's explored fog just met a new colonial power (FP-6a; FreeCol's first-contact message).</summary>
    private static string FormatFirstContactNotice(FirstContactNotice notice) =>
        $"🤝 You have made contact with the {NationLabel(notice.RivalNationId)}. You are at peace.";

    /// <summary>
    /// Turns a turn-driven stance shift into a turn-message row (FP-6b) — a war cooling to a cease-fire or peace, or a
    /// rival breaking the peace. The phrasing keys off the <em>new</em> stance (and, for a de-escalation, the old one):
    /// reaching <see cref="Stance.War"/> from peace is the rival breaking it; otherwise it is the relationship thawing.
    /// </summary>
    private static string FormatStanceChangeNotice(StanceChangeNotice notice)
    {
        string nation = NationLabel(notice.RivalNationId);
        return notice.Current switch
        {
            Stance.War => $"⚔ The {nation} have broken the peace — you are now at war!",
            Stance.CeaseFire => $"🕊 Your war with the {nation} has cooled to a cease-fire.",
            Stance.Peace => $"🕊 You are now at peace with the {nation}.",
            Stance.Alliance => $"🤝 You are now allied with the {nation}.",
            _ => $"Your relations with the {nation} have changed.",
        };
    }

    /// <summary>The display label for a nation id (e.g. <c>model.nation.dutch</c> → "Dutch").</summary>
    private static string NationLabel(string nationId)
    {
        string shortName = nationId[(nationId.LastIndexOf('.') + 1)..];
        return char.ToUpperInvariant(shortName[0]) + shortName[1..];
    }

    /// <summary>
    /// The single authoritative table of in-game keyboard shortcuts (`86d3f0vjg`; named-action migration `86d3f0wjj`).
    /// Both the <see cref="_UnhandledInput"/> dispatch <b>and</b> the F1 keys legend are generated from this one list,
    /// so a key and its on-screen description can never drift apart. Each row pairs a named <c>InputMap</c> action id
    /// (defined in <c>project.godot</c> <c>[input]</c>, defaults matching the historical keys; rebindable via the
    /// settings key-bindings screen) with the action it fires and a short human label. Presentation-only (ADR-006):
    /// every action forwards to a command/oracle method. Mirrors FreeCol's accelerator-driven menu actions (e.g.
    /// DisbandUnitAction=D, EndTurn=ENTER, Save=Ctrl+S, Open=Ctrl+O, SkipUnitAction=SPACE, CenterAction=Ctrl+C).
    /// </summary>
    private IReadOnlyList<KeyBinding> KeyBindings => _keyBindings ??= BuildKeyBindings();

    private IReadOnlyList<KeyBinding>? _keyBindings;

    /// <summary>One row of the authoritative key table: the named <c>InputMap</c> action id that triggers it, the method it fires, and a legend label.</summary>
    private sealed record KeyBinding(string ActionId, System.Action Action, string Label);

    /// <summary>Builds the authoritative key table (single source for dispatch + legend). Action ids match the <c>project.godot</c> <c>[input]</c> actions; lambdas/method groups capture <c>this</c>.</summary>
    private KeyBinding[] BuildKeyBindings() =>
    [
        new("end_turn", OnEndTurnPressed, "End turn"),
        new("skip_unit", SkipSelectedUnit, "Skip unit (this turn)"),
        new("next_unit", SelectNextUnitToMove, "Next unit needing orders"),
        new("goto_mode", EnterGotoMode, "Go to (set destination)"),
        new("build_colony", FoundColony, "Build colony"),
        new("disband_unit", DisbandSelectedUnit, "Disband unit"),
        new("open_europe", OpenEuropePanel, "Europe"),
        new("find_settlement", OpenFindSettlementPanel, "Find settlement"),
        new("founding_fathers", OpenFoundingFatherPanel, "Founding fathers"),
        new("colopedia", OpenColopediaPanel, "Colopedia"),
        new("center_unit", CenterOnSelectedUnit, "Centre on unit"),
        new("new_map", NewGame, "New map"),
        new("save_game", OpenSaveDialog, "Save game"),
        new("load_game", OpenLoadDialog, "Load game"),
        new("quick_save", QuickSave, "Quick save"),
        new("quick_load", QuickLoad, "Quick load"),
        new("toggle_legend", ToggleKeysLegend, "Toggle this legend"),
    ];

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } leftClick:
                // Pick from the event's own position (zoom/pan-correct via the MapView canvas transform), not the live
                // cursor — the buffered cursor can drift between press and dispatch, landing the click on the wrong tile.
                HandleTileClick(_mapView.TileAtScreen(leftClick.Position));
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } rightClick:
                // Right-button RELEASE without a drag: open the tile context menu. The press/drag is the camera pan
                // (CameraController), which consumes the release when it actually dragged — so a real pan never reaches
                // here and panning is never broken; only a drag-free right-click falls through to open the menu (86d3f0vrz).
                HandleRightClick(rightClick.Position);
                break;
            case InputEventKey { Pressed: true, Echo: false } key when !IsTextInputFocused():
                if (IsDuplicateKeyDown(key))
                {
                    break; // a single press already dispatched this frame — never fire its action twice (see IsDuplicateKeyDown)
                }
                // Dispatch through the authoritative key table by named InputMap action (rebindable; defined in
                // project.godot [input]). A key fires its bound action unless a modal/text field owns focus (so typing
                // into a save-slot/search field never triggers a hotkey). 86d3f0vjg / 86d3f0wjj.
                foreach (KeyBinding binding in KeyBindings)
                {
                    // exactMatch:true so a plain-key action does not fire while Ctrl is held (and vice-versa) —
                    // preserves the old KeyChord.Matches modifier discipline (e.g. C vs Ctrl+C).
                    if (@event.IsActionPressed(binding.ActionId, exactMatch: true))
                    {
                        binding.Action();
                        GetViewport().SetInputAsHandled();
                        break;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Whether a text-entry control currently owns keyboard focus (a <see cref="LineEdit"/>/<see cref="TextEdit"/>),
    /// so the hotkey dispatch can stand down while the player is typing into a field (e.g. a search box). Guards every
    /// new key against firing mid-type, as the brief requires.
    /// </summary>
    private bool IsTextInputFocused() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;

    /// <summary>The (keycode + Ctrl) of the last non-echo key-down dispatched, and the process-frame it fired on — for same-frame de-duplication.</summary>
    private (Key Code, bool Ctrl) _lastKeyDown;
    private bool _hasLastKeyDown;
    private ulong _lastKeyDownFrame = ulong.MaxValue;

    /// <summary>
    /// Whether <paramref name="key"/> is the same key-down we already dispatched on this process frame, so its bound
    /// action must not fire a second time. A single physical press produces exactly one <c>_UnhandledInput</c> call in a
    /// live game, but the L3 GdUnit <c>SceneRunner</c> delivers each simulated press <em>twice</em> in the same frame —
    /// once by pumping the global <see cref="Input"/> pipeline and again by calling <c>_unhandled_input</c> directly — so
    /// a turn-advancing key (Enter) would advance twice and a toggle (F1) would flip back to its start. Collapsing an
    /// identical same-frame key-down to one keeps real single-press behaviour intact (genuine key repeats arrive as
    /// <c>Echo</c> events, already filtered, and a deliberate second press lands on a later frame). The frame stamp uses
    /// <see cref="Engine.GetProcessFrames"/>, which the runner advances between simulated presses. Keyed on the raw
    /// keycode + Ctrl (not the rebindable action id) so the guard is independent of any remap.
    /// </summary>
    private bool IsDuplicateKeyDown(InputEventKey key)
    {
        var chord = (key.Keycode, key.CtrlPressed);
        ulong frame = Engine.GetProcessFrames();
        bool duplicate = _hasLastKeyDown && _lastKeyDownFrame == frame && _lastKeyDown == chord;
        _lastKeyDown = chord;
        _hasLastKeyDown = true;
        _lastKeyDownFrame = frame;
        return duplicate;
    }

    /// <summary>Selects the next of the human's units still needing orders and centres on it (FreeCol's "wait/next unit"
    /// cycle, key W) — reads the shipped <see cref="Game.NextUnitToMove"/> oracle, passing the session-only skip set so a
    /// unit skipped this turn (Space) is not re-offered until next turn (86d3f0vuy); no-op when none remain (ADR-006).</summary>
    private void SelectNextUnitToMove()
    {
        if (_game.NextUnitToMove(_game.HumanPlayer, _skippedThisTurn) is { } next)
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
    /// Skips the selected unit for the rest of this turn (Space, FreeCol's <c>SkipUnitAction</c> — 86d3f0vuy): flags its
    /// id in the session-only <see cref="_skippedThisTurn"/> set (never persisted, ADR-009) so the W-cycle passes it over,
    /// then advances to the next unit needing orders. No-op with no selection. Presentation-only (ADR-006): it touches no
    /// game rules — the unit keeps its movement and orders, it's merely passed over by the cycle until turn rollover.
    /// </summary>
    private void SkipSelectedUnit()
    {
        if (_selectedUnit is { } unit)
        {
            _skippedThisTurn.Add(unit.Id);
        }
        SelectNextUnitToMove();
    }

    /// <summary>
    /// Disbands the selected unit after a confirmation prompt (D / the Orders "Disband" button — 86d3f0vgd, FreeCol's
    /// <c>DisbandUnitAction</c>): gated on the <see cref="Game.CheckDisband"/> oracle, then forwards to
    /// <see cref="Game.Disband"/> on confirm, clears the selection and refreshes (ADR-006). No-op with no selection or
    /// when the oracle forbids it (e.g. a carrier still holding passengers); the reason is surfaced in the status bar.
    /// </summary>
    private void DisbandSelectedUnit()
    {
        if (_selectedUnit is not { } unit)
        {
            return;
        }
        MoveCheck check = _game.CheckDisband(unit);
        if (!check.Allowed)
        {
            NoticeBlocked(check.Reason); // can't disband → status + deny buzz
            RefreshView();
            return;
        }
        var dialog = new ConfirmationDialog
        {
            Title = "Disband unit",
            DialogText = $"Disband the {unit.Type.ShortName}? It is removed from the game for good.",
            OkButtonText = "Disband",
            CancelButtonText = "Keep",
        };
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            // Re-check on confirm: the world can't change behind a modal here, but the oracle is the single gate (ADR-006).
            if (_game.CheckDisband(unit).Allowed)
            {
                _game.Disband(unit);
                MarkDirty(); // 86d3fq1v8
                _selectedUnit = null;
                _notice = "Unit disbanded.";
            }
            RefreshView();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Cashes in the selected treasure train (86d3f62q1): the discoverable UI surface for the existing
    /// <see cref="Game.CashInTreasureTrain"/> command. Gated on <see cref="Game.CheckCashInTreasureTrain"/> (ADR-006);
    /// a guard failure (not at an owned colony / not aboard a galleon docked in Europe) is shown in the status bar,
    /// never thrown. On success it raises a confirmation that surfaces the net <see cref="Game.CashInValue(Unit)"/>: at a
    /// colony it is framed as the King's OFFER to ship the treasure home for a cut equal to the tax rate
    /// (<see cref="Game.TreasureKingsCut(Unit)"/>), reminding the player that carrying it home themselves keeps it all;
    /// in Europe (carried home yourself) it confirms the full fee-free amount. It then banks the gold and consumes the
    /// train (which clears the selection, since the train leaves the game).
    /// </summary>
    private void CashInSelectedTreasureTrain()
    {
        if (_selectedUnit is not { } train)
        {
            return;
        }
        MoveCheck check = _game.CheckCashInTreasureTrain(train);
        if (!check.Allowed)
        {
            NoticeBlocked(check.Reason); // e.g. "Bring the treasure train to one of your colonies …" — status + deny buzz
            RefreshView();
            return;
        }

        // The net the player would bank (CheckCashInTreasureTrain.Cost == CashInValue), framed by where it is cashed in.
        // In Europe you carried it home yourself → keep the full amount, no fee, no tax. At a colony the King OFFERS to
        // ship it across for a cut equal to your current tax rate — surface that offer (rate, gold cut, net, and the
        // full amount you'd keep by sailing it home) so the player weighs it before committing.
        int amount = train.TreasureAmount;
        int value = _game.CashInValue(train);
        bool feeFree = _game.TreasureCashInIsFeeFree(train);
        string prompt = feeFree
            ? $"Cash in the {train.Type.ShortName} carrying {amount} gold?\n"
              + $"Carried home yourself — the King takes no cut and no tax. You keep all {value} gold."
            : $"The King will carry your {train.Type.ShortName}'s {amount} gold to Europe and take "
              + $"{_game.TaxRate}% ({_game.TreasureKingsCut(train)}g); you keep {value}g.\n"
              + $"(Carry it home on a galleon yourself to keep all {amount}g.)";
        var dialog = new ConfirmationDialog
        {
            Title = "Cash in treasure",
            DialogText = prompt,
            OkButtonText = $"Cash in ({value}g)",
            CancelButtonText = "Keep",
        };
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            // Re-check on confirm: the world can't change behind a modal here, but the oracle is the single gate (ADR-006).
            if (_game.CheckCashInTreasureTrain(train).Allowed)
            {
                int banked = _game.CashInValue(train);
                _game.CashInTreasureTrain(train);
                _selectedUnit = null; // the train left the game — clear the (now dangling) selection
                _notice = $"Treasure cashed in: {banked} gold banked.";
                PlaySound(SoundEvent.CargoSold); // treasure banked → the cash/sell cue
            }
            else
            {
                PlaySound(SoundEvent.IllegalMove); // the gate closed behind the modal → deny buzz
            }
            RefreshView();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Centres the camera on the selected unit (Ctrl+C, FreeCol's <c>CenterAction</c> — 86d3f0vqf); no-op with no selection.</summary>
    private void CenterOnSelectedUnit()
    {
        if (_selectedUnit is { } unit && unit.IsOnMap)
        {
            CenterCameraOnTile(unit.Position);
        }
    }

    /// <summary>Opens the save/load dialog in save mode via the pause-menu path (Ctrl+S, FreeCol's Save — 86d3f0vjg).</summary>
    private void OpenSaveDialog() => GetNode<PauseMenu>("UI/PauseMenu").OpenSave();

    /// <summary>Opens the save/load dialog in load mode via the pause-menu path (Ctrl+O, FreeCol's Open — 86d3f0vjg).</summary>
    private void OpenLoadDialog() => GetNode<PauseMenu>("UI/PauseMenu").OpenLoad();

    /// <summary>
    /// Toggles the F1 keys legend overlay — the on-screen list generated from the authoritative key table (86d3f0vjg).
    /// The legend text is rebuilt each time it is shown so it reflects the <b>current</b> (possibly rebound) keys; it can
    /// never drift from what the dispatch actually fires because both read the same <c>InputMap</c> actions (86d3f0wjj).
    /// </summary>
    private void ToggleKeysLegend()
    {
        _keysLegend ??= CreateKeysLegendLabel();
        if (!_keysLegend.Visible)
        {
            _keysLegend.Text = BuildKeysLegendText(); // refresh from the live InputMap so a rebind shows immediately
        }
        _keysLegend.Visible = !_keysLegend.Visible;
    }

    /// <summary>Creates (once) the F1 keys-legend label node, anchored on the right of the screen; the text is filled by <see cref="BuildKeysLegendText"/>.</summary>
    private Label CreateKeysLegendLabel()
    {
        var label = new Label { Name = "KeysLegend", Visible = false, Text = BuildKeysLegendText() };
        label.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        label.OffsetLeft = -280f;
        label.OffsetTop = 60f;
        GetNode<CanvasLayer>("UI").AddChild(label);
        return label;
    }

    /// <summary>
    /// Builds the F1 keys-legend text from the single authoritative <see cref="KeyBindings"/> table, reading each
    /// action's <b>current</b> key combination(s) live from the global <c>InputMap</c> (via <see cref="KeyChordsFor"/>)
    /// — so the legend always shows the keys the dispatch will actually honour, rebinds included.
    /// </summary>
    private string BuildKeysLegendText() =>
        "Keys\n" + string.Join("\n", KeyBindings.Select(b =>
            $"  {string.Join(" / ", KeyChordsFor(b.ActionId).Select(KeyBindingsService.Describe))}  —  {b.Label}"));

    /// <summary>
    /// The current key combination(s) bound to an <c>InputMap</c> action, as engine-free <see cref="KeyBindingsModel.KeyChord"/>s
    /// (only <see cref="InputEventKey"/> events; joypad/mouse events are ignored). Reads the live map so it reflects any rebind.
    /// </summary>
    private static IEnumerable<KeyBindingsModel.KeyChord> KeyChordsFor(string actionId)
    {
        if (!InputMap.HasAction(actionId))
        {
            yield break;
        }
        foreach (InputEvent e in InputMap.ActionGetEvents(actionId))
        {
            if (e is InputEventKey k)
            {
                yield return new KeyBindingsModel.KeyChord((long)k.Keycode, k.CtrlPressed);
            }
        }
    }

    /// <summary>
    /// Opens the right-click tile context menu (86d3f0vrz): a small <see cref="PopupMenu"/> at the mouse offering one
    /// "Activate" entry per own unit standing on the tile (fixing stack-select, which otherwise picks only the first),
    /// "Centre here", and "Go to here" (arms <see cref="SetSelectedDestination"/>). Fired on right-button release without
    /// a drag, so the right-drag camera pan is unaffected. Presentation-only (ADR-006).
    /// </summary>
    /// <param name="viewportPosition">
    /// The triggering event's position in viewport (screen) space — picked via the MapView canvas transform so the menu
    /// targets the tile the click landed on, not where the cursor has since drifted. When <c>null</c> (only the L3 test
    /// drives it directly) the current cursor position is used instead.
    /// </param>
    private void HandleRightClick(Vector2? viewportPosition = null)
    {
        // From the dispatch we have the event's own position (accurate); when driven directly with no position (the L3
        // stack test) fall back to the live cursor via the node helper — the same path the left-click used before.
        Position tile = viewportPosition is { } pos
            ? _mapView.TileAtScreen(pos)
            : MapView.TileAt(_mapView.GetLocalMousePosition());
        if (!_game.Map.InBounds(tile))
        {
            return;
        }
        _inspectedTile = tile;

        var menu = new PopupMenu();
        // One "Activate" entry per own on-map unit on the tile — selecting any of a stack (HandleTileClick picks the first).
        var unitsHere = _game.PlayerUnits.Where(u => u.IsOnMap && u.Position == tile).OrderBy(u => u.Id).ToList();
        foreach (Unit u in unitsHere)
        {
            menu.AddItem($"Activate {u.Type.ShortName} (#{u.Id})", u.Id);
        }
        if (unitsHere.Count > 0)
        {
            menu.AddSeparator();
        }
        const int CentreId = -1;
        const int GotoId = -2;
        const int RenameId = -3;
        menu.AddItem("Centre here", CentreId);
        menu.AddItem("Go to here", GotoId);
        // "Rename unit…" — the discoverable surface for the existing Game.NameUnit command (86d3drmzu). Offered when the
        // selected unit is one of the human's own units standing on this tile (the natural per-unit christening target,
        // FreeCol Nameable); opens a code-built rename dialog. Absent when nothing fitting is selected here.
        if (_selectedUnit is { IsOnMap: true } chosen && unitsHere.Any(u => u.Id == chosen.Id))
        {
            menu.AddSeparator();
            menu.AddItem($"Rename {UnitDisplayName(chosen)}…", RenameId);
        }

        menu.IdPressed += id =>
        {
            switch ((int)id)
            {
                case CentreId:
                    CenterCameraOnTile(tile);
                    break;
                case GotoId:
                    SetSelectedDestination(tile); // arms/sets the selected unit's standing destination (no-op with no selection)
                    break;
                case RenameId:
                    RenameSelectedUnit(); // opens the code-built rename dialog for the selected unit (no-op with no selection)
                    break;
                default:
                    _selectedUnit = _game.PlayerUnits.FirstOrDefault(u => u.Id == (int)id && u.IsOnMap);
                    RefreshView();
                    break;
            }
        };
        menu.PopupHide += menu.QueueFree;
        GetNode<CanvasLayer>("UI").AddChild(menu);
        menu.Position = (Vector2I)GetViewport().GetMousePosition();
        menu.Popup();
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
            MarkDirty(); // 86d3fq1v8
            _notice = $"Destination set to ({tile.X},{tile.Y}).";
        }
        else
        {
            NoticeBlocked(check.Reason); // can't set that destination → status + deny buzz
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

    /// <summary>
    /// Opens the one-shot <b>name-the-new-world</b> dialog when the human has just made first landfall but not yet named
    /// the continent (FreeCol <c>newLandNameHandler</c> — Col1 prompts on the first landing; `86d3fq1fn`). Gated on the
    /// engine's <see cref="Game.NewWorldNamePending"/> oracle (the rule lives in GameLogic, ADR-006), so it fires exactly
    /// once per game and never for a goto/auto-move that has already been answered. A code-built modal (no scene edit): a
    /// text field pre-filled with the engine's default name plus an OK button; confirming (or pressing Enter) forwards the
    /// typed name to <see cref="Game.NameNewWorld"/> (a blank falls back to the default, decided in GameLogic). The
    /// re-entrancy guard (<see cref="_newWorldNameDialogOpen"/>) keeps a second move/refresh from stacking the prompt.
    /// </summary>
    private void MaybePromptNewWorldName()
    {
        if (_newWorldNameDialogOpen || !_game.NewWorldNamePending)
        {
            return; // already prompting, or the world is already named / no landfall yet
        }
        _newWorldNameDialogOpen = true;

        var dialog = new AcceptDialog
        {
            Title = "The New World",
            OkButtonText = "Name it",
            Exclusive = true,
        };
        var nameField = new LineEdit
        {
            Name = "NewWorldNameField",
            Text = Game.DefaultNewWorldName, // the engine's fixed default (RNG-free); the player may type over it
            CustomMinimumSize = new Vector2(220, 0),
        };
        nameField.SelectAll();
        // A short prompt label above the field, in a column so the dialog lays them out vertically.
        var column = new VBoxContainer();
        column.AddChild(new Label { Text = "Your explorers have reached a new land. What shall it be called?" });
        column.AddChild(nameField);
        dialog.AddChild(column);

        void Confirm()
        {
            _game.NameNewWorld(nameField.Text); // GameLogic decides the blank→default fallback (ADR-006)
            MarkDirty();                         // the named world is unsaved state (save v66)
            _notice = $"You have named the New World \"{_game.NewWorldName}\".";
            _newWorldNameDialogOpen = false;
            dialog.QueueFree();
            RefreshView();
        }
        dialog.Confirmed += Confirm;
        nameField.TextSubmitted += _ => { dialog.Hide(); Confirm(); }; // Enter in the field confirms too
        // Dismissing the modal (Escape / window-close button — both route through AcceptDialog's `canceled`) accepts the
        // engine default name: faithful to FreeCol's auto-named-then-renamable land, it clears NewWorldNamePending so the
        // prompt never re-nags, resets the re-entrancy guard, and frees the node. Without this, an Escape/X left the guard
        // stuck open (the world stayed unnamed all game) and leaked the dialog as an orphan (Wave 8 review). NameNewWorld
        // is idempotent, so this never double-names even if it raced a confirm.
        dialog.Canceled += () =>
        {
            _game.NameNewWorld(null); // null ⇒ DefaultNewWorldName (the blank→default rule lives in GameLogic, ADR-006)
            MarkDirty();
            _notice = $"The New World was named \"{_game.NewWorldName}\".";
            _newWorldNameDialogOpen = false;
            dialog.QueueFree();
            RefreshView();
        };
        AddChild(dialog);
        dialog.PopupCentered();
        nameField.GrabFocus();
    }

    /// <summary>
    /// Offers to sail a ship to Europe when it has just <b>crossed onto a high-seas tile</b> (86d3fpzqp, FreeCol
    /// <c>InGameController.moveHighSeas</c>): a code-built <see cref="ConfirmationDialog"/> ("Sail to Europe?") whose OK
    /// forwards to the existing <see cref="Game.SailToEurope"/> command and whose Cancel leaves the ship on the high
    /// seas. The just-crossed ship is handed in via <see cref="_pendingSailPromptShip"/> (set by the move handler only
    /// on a genuine crossing — see there), and consumed here exactly once, so a plain re-selection of a ship already
    /// on the high seas never re-nags. Reads-only over <see cref="Game.CheckSailToEurope"/> (ADR-006); the re-entrancy
    /// guard (<see cref="_sailPromptDialogOpen"/>) plus the modal's <c>Exclusive</c> flag stop a second prompt stacking.
    /// <para>
    /// Cancel path (Wave 8/9 dialog discipline): the <c>Canceled</c> signal (Cancel button / Escape / window-close)
    /// resets the guard and frees the node, leaving the ship where it is — without it the guard would stick and the
    /// node would leak.
    /// </para>
    /// </summary>
    private void MaybePromptSailToEurope()
    {
        if (_pendingSailPromptShip is not { } ship)
        {
            return; // no fresh crossing this click
        }
        _pendingSailPromptShip = null; // one-shot: consume the pending crossing
        if (_sailPromptDialogOpen || !_game.CheckSailToEurope(ship).Allowed)
        {
            return; // already prompting, or the ship is no longer on the high seas (defensive re-check)
        }
        _sailPromptDialogOpen = true;

        var dialog = new ConfirmationDialog
        {
            Title = "Sail to Europe",
            DialogText = $"{UnitDisplayName(ship)} has reached the high seas. Sail to Europe?\n"
                + $"The crossing takes {Game.SailTurns} turns.",
            OkButtonText = "Set sail",
            CancelButtonText = "Stay",
            Exclusive = true,
        };
        dialog.Confirmed += () =>
        {
            // Re-check on confirm: the oracle is the single gate (ADR-006). The world can't change behind the modal.
            if (_game.CheckSailToEurope(ship).Allowed)
            {
                _game.SailToEurope(ship);
                MarkDirty(); // 86d3fq1v8
                _notice = $"{UnitDisplayName(ship)} sets sail for Europe.";
            }
            _sailPromptDialogOpen = false;
            dialog.QueueFree();
            RefreshView();
        };
        // Cancel / Escape / window-close: leave the ship on the high seas, reset the guard, free the node (no orphan).
        dialog.Canceled += () =>
        {
            _sailPromptDialogOpen = false;
            dialog.QueueFree();
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Opens the <b>rename-unit</b> dialog for the currently selected unit (86d3drmzu): the discoverable presentation
    /// surface for the existing <see cref="Game.NameUnit"/> command (FreeCol christens an individual unit via
    /// <c>Nameable</c>). A code-built modal (no scene edit) — a text field pre-filled with the unit's current custom
    /// name (blank when un-christened) plus an OK button; confirming (or pressing Enter) forwards the typed text to
    /// <see cref="Game.NameUnit"/>, which trims it or clears the name back to the generic type name on a blank
    /// (the blank→clear rule lives in GameLogic, ADR-006). No-op with no selection. The re-entrancy guard
    /// (<see cref="_renameUnitDialogOpen"/>) keeps a second right-click / refresh from stacking the prompt.
    /// <para>
    /// Cancel path (Wave 8 review regression guard): a code-built <see cref="AcceptDialog"/> raises <c>Canceled</c>
    /// (not <c>Confirmed</c>) on Escape / the window-close button. The <c>Canceled</c> handler frees the node and
    /// resets the guard, leaving the unit's name <b>unchanged</b> — without it, an Escape left the guard stuck open
    /// (the rename never re-fired) and leaked the dialog as an orphan.
    /// </para>
    /// </summary>
    private void RenameSelectedUnit()
    {
        if (_renameUnitDialogOpen || _selectedUnit is not { } unit)
        {
            return; // already prompting, or nothing selected to rename
        }
        _renameUnitDialogOpen = true;

        var dialog = new AcceptDialog
        {
            Title = "Rename unit",
            OkButtonText = "Rename",
            Exclusive = true,
        };
        var nameField = new LineEdit
        {
            Name = "RenameUnitField",
            Text = unit.Name ?? "", // the current custom name, or blank when the unit still shows its generic type name
            PlaceholderText = unit.Type.ShortName,
            CustomMinimumSize = new Vector2(220, 0),
        };
        nameField.SelectAll();
        var column = new VBoxContainer();
        column.AddChild(new Label { Text = $"Name this {unit.Type.ShortName} (clear it to use the default name):" });
        column.AddChild(nameField);
        dialog.AddChild(column);

        void Confirm()
        {
            _game.NameUnit(unit, nameField.Text); // GameLogic trims, or clears on a blank (ADR-006)
            MarkDirty();                           // the custom name is unsaved state (save v52)
            _notice = string.IsNullOrWhiteSpace(unit.Name)
                ? $"The {unit.Type.ShortName} reverted to its default name."
                : $"Unit renamed to \"{unit.Name}\".";
            _renameUnitDialogOpen = false;
            dialog.QueueFree();
            RefreshView();
        }
        dialog.Confirmed += Confirm;
        nameField.TextSubmitted += _ => { dialog.Hide(); Confirm(); }; // Enter in the field confirms too
        // Escape / titlebar X both raise Canceled: leave the name unchanged, reset the guard, free the node (no orphan).
        dialog.Canceled += () =>
        {
            _renameUnitDialogOpen = false;
            dialog.QueueFree();
        };
        AddChild(dialog);
        dialog.PopupCentered();
        nameField.GrabFocus();
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
                MarkDirty(); // 86d3fq1v8
                _notice = $"{boarder.Type.ShortName} boarded the {ship.Type.ShortName}.";
                _selectedUnit = ship;
                RefreshView();
                return;
            }
        }

        // Amphibious assault: a selected carrier with an armed passenger clicks an adjacent enemy-held land tile →
        // the passenger assaults straight off the ship (the −75% amphibious penalty applies). Takes priority over
        // disembark below, which only puts a unit onto an EMPTY tile (an enemy holds this one). Gated on the
        // amphibiousMoves option + a non-REF passenger via CheckAttackAmphibious.
        if (_selectedUnit is { IsOnMap: true } assaultShip && assaultShip.Type.IsCarrier)
        {
            Unit? marine = _game.Passengers(assaultShip).FirstOrDefault(p => _game.CheckAttackAmphibious(p, tile).Allowed);
            if (marine is not null)
            {
                AmphibiousAssaultFrom(marine, tile);
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
                MarkDirty(); // 86d3fq1v8
                _notice = $"{passenger.Type.ShortName} went ashore at ({tile.X},{tile.Y}).";
                _selectedUnit = passenger;
                RefreshView();
                MaybePromptNewWorldName(); // the classic first landing: a colonist stepping ashore may owe the name-the-new-world prompt (86d3fq1fn)
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
            // The player's own colony tile. If a selected unit can legally move onto it (adjacent/reachable),
            // clicking the colony MOVES the unit there — so you can send a unit (e.g. a treasure train) onto the
            // colony by clicking it, instead of being forced to use go-to (Chris playtest 86d3fy…). The unit then
            // STANDS on the colony tile (MoveUnit does not auto-join the work force; a colonist Joins only via the
            // explicit Join button, and a treasure train can be cashed in from there). With no movable selection
            // (nothing selected, or the unit can't reach here — not adjacent, or it is already on this tile), the
            // click opens the colony panel as before; click again once the unit has arrived to manage it.
            if (_selectedUnit is { } mover && _game.CheckMove(mover, tile).Allowed)
            {
                _game.MoveUnit(mover, tile);
                MarkDirty(); // 86d3fq1v8
            }
            else
            {
                OpenColonyPanel(colony); // only the human's own colonies are the player's to manage
            }
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
                    Unit moved = _selectedUnit;
                    // FreeCol moveHighSeas: offer to sail to Europe only when a ship CROSSES onto the high seas this
                    // move — it was not already there (couldSailBefore) and carries no standing Europe goto (which sails
                    // silently on arrival). Captured before the move; the prompt fires after RefreshView.
                    bool couldSailBefore = moved.Type.IsNaval && _game.CheckSailToEurope(moved).Allowed;
                    _game.MoveUnit(moved, tile);
                    MarkDirty(); // 86d3fq1v8
                    if (moved.Type.IsNaval && moved.Destination is null && !couldSailBefore
                        && _game.CheckSailToEurope(moved).Allowed)
                    {
                        _pendingSailPromptShip = moved; // a fresh high-seas crossing → offer Europe (86d3fpzqp)
                    }
                }
                else
                {
                    NoticeBlocked(check.Reason); // blocked move → status + deny buzz
                }
            }
        }

        RefreshView();
        MaybePromptNewWorldName(); // a land unit stepping ashore for the first time may owe the name-the-new-world prompt (86d3fq1fn)
        MaybePromptSailToEurope(); // a ship that just crossed onto the high seas may offer to sail to Europe (86d3fpzqp)
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
            NoticeBlocked(check.Reason); // blocked attack → status + deny buzz
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
        MarkDirty(); // 86d3fq1v8
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

    /// <summary>
    /// Resolves an amphibious assault: <paramref name="marine"/> attacks the enemy on the adjacent land tile straight
    /// off its ship (the −75% amphibious penalty applies; a beaten capturable defender is slain, not captured), reports
    /// the outcome, and plays the combat cue/animation. The marine stays aboard the ship throughout; the assault ends
    /// its turn. Selection is cleared (the marine spent its move and may have been demoted/destroyed on a loss).
    /// </summary>
    private void AmphibiousAssaultFrom(Unit marine, Position tile)
    {
        string who = marine.Type.ShortName;
        string roleShortName = marine.RoleId[(marine.RoleId.LastIndexOf('.') + 1)..];
        Position from = marine.Position; // the ship's water tile — the lunge animates from there
        CombatResult result = _game.AttackAmphibious(marine, tile);
        MarkDirty(); // 86d3fq1v8
        _selectedUnit = null; // the assault ends the marine's turn (and may demote/destroy it on a loss)
        _notice = result is CombatResult.GreatWin or CombatResult.Win
            ? $"Your {who} stormed ashore and won the battle."
            : $"Your {who}'s landing was beaten back.";
        PlaySound(SoundEvent.Combat);
        PlayCombatAnimation(from, tile, result, who, roleShortName);
    }

    /// <summary>Assaults an ungarrisoned rival colony on an adjacent tile to capture it, reporting the outcome.</summary>
    private void AttackColonyAt(Position tile)
    {
        MoveCheck check = _game.CheckAttackColony(_selectedUnit!, tile);
        if (!check.Allowed)
        {
            NoticeBlocked(check.Reason); // blocked colony assault → status + deny buzz
            return;
        }
        string colonyName = _game.ColonyAt(tile)!.Name;
        // Snapshot the attacker's tile/sprite before the assault (a loss may demote or destroy it).
        Unit assaulter = _selectedUnit!;
        Position from = assaulter.Position;
        string who = assaulter.Type.ShortName;
        string roleShortName = assaulter.RoleId[(assaulter.RoleId.LastIndexOf('.') + 1)..];
        CombatResult result = _game.AttackColony(assaulter, tile);
        MarkDirty(); // 86d3fq1v8
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

    // Surfaces a blocked-action reason in the status bar AND plays the illegal-move buzz — the single helper used at the
    // player-initiated "this is not allowed" paths (a failed move/attack/found/disband/goto check), so the deny cue and
    // the message stay in lock-step. Presentation-only over the engine's MoveCheck gate (ADR-006).
    private void NoticeBlocked(string? reason)
    {
        _notice = reason;
        PlaySound(SoundEvent.IllegalMove);
    }

    // The total number of buildings across all the human player's colonies — snapshotted around EndTurn to detect a
    // just-finished construction (one build completes per colony per turn; a net increase means at least one finished).
    // Reads-only over already-resolved state (ADR-006); the build-complete cue is purely cosmetic.
    private int HumanBuildingCount()
    {
        int total = 0;
        foreach (Colony colony in _game.Colonies)
        {
            if (colony.OwnerId == _game.HumanPlayer.PlayerId)
            {
                total += colony.Buildings.Count;
            }
        }
        return total;
    }

    /// <summary>
    /// Plays the human player's national anthem once via the <c>Music</c> autoload (<c>/root/Music</c>), then the
    /// background playlist resumes. Resolved lazily by node path (no-op if the autoload is absent, e.g. headless scene
    /// tests) and a no-op for a nation FreeCol ships no anthem for. Faithful to FreeCol, which cues the anthem (a
    /// <c>"music"</c>-type resource) at game start.
    /// </summary>
    private void PlayAnthem(string? nationId) =>
        GetNodeOrNull<MusicService>("/root/Music")?.PlayAnthem(nationId);

    /// <summary>
    /// Switches the background music to match <paramref name="context"/> (menu vs in-game vs a future war/tension cue)
    /// via the <c>Music</c> autoload. Resolved lazily by node path (no-op if the autoload is absent, e.g. headless scene
    /// tests). The service only restarts the playlist when the mood actually changes, so a same-context call is harmless.
    /// </summary>
    private void SetMusicContext(MusicContext context) =>
        GetNodeOrNull<MusicService>("/root/Music")?.SetContext(context);

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
            NoticeBlocked(check.Reason); // can't found here → status + deny buzz
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
        MarkDirty(); // 86d3fq1v8
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
        ((ColonyPanel)_colonyPanel).Open(_game, colony, RefreshView, LoadColonyCargo, UnloadColonyCargo, SetColonyExport, RenameColony, AbandonColony, PayBoycott, DumpColonyGoods);

    /// <summary>
    /// Thin colony-screen command (86d3fq0aw/86d3fpy6k): renames <paramref name="colony"/> via the
    /// <see cref="Game.RenameColony"/> oracle, then refreshes. The engine forbids a blank name and throws
    /// <see cref="System.ArgumentException"/>; the reason is surfaced in the status bar instead of bubbling to the UI
    /// (ADR-006 — the rule lives in <see cref="Game"/>; this only forwards the command and reflects the outcome).
    /// </summary>
    public void RenameColony(Colony colony, string name)
    {
        try
        {
            _game.RenameColony(colony, name);
            MarkDirty(); // 86d3fq1v8
            _notice = $"Colony renamed to {colony.Name}.";
        }
        catch (System.ArgumentException ex)
        {
            _notice = ex.Message; // e.g. "A colony name cannot be blank." — show, don't throw to the UI
        }
        RefreshView();
    }

    /// <summary>
    /// Thin colony-screen command (86d3fq0bg/86d3fpy8f): abandons <paramref name="colony"/> via the
    /// <see cref="Game.AbandonColony"/> oracle (gated by <see cref="Game.CheckAbandonColony"/>), then <b>closes the
    /// colony panel</b> (the colony no longer exists) and refreshes. A disallowed attempt (population &gt; 1, or a
    /// fortified colony) throws <see cref="InvalidMoveException"/>; the reason is surfaced in the status bar instead of
    /// bubbling to the UI (ADR-006 — the rules live in <see cref="Game"/>; this only forwards the command and reflects
    /// the outcome). The departed colonist walks out onto the colony's old tile as its real type.
    /// </summary>
    public void AbandonColony(Colony colony)
    {
        try
        {
            _game.AbandonColony(colony);
            MarkDirty(); // 86d3fq1v8
            _colonyPanel.Hide(); // the colony is gone — there is nothing left to manage
            _notice = "Colony abandoned.";
        }
        catch (InvalidMoveException ex)
        {
            _notice = ex.Message; // e.g. "Send the other colonists out before abandoning the colony." — show, don't throw
        }
        RefreshView();
    }

    /// <summary>
    /// Thin colony-screen command (86d3fpyu0): pays off the boycott back-tax on <paramref name="goodsId"/> via the
    /// <see cref="Game.PayArrears"/> oracle (gated by <see cref="Game.CheckPayArrears"/>), lifting the boycott, then
    /// refreshes. A disallowed attempt (the good is not boycotted, or the player cannot afford the arrears) throws
    /// <see cref="InvalidMoveException"/>; the reason is surfaced in the status bar instead of bubbling to the UI
    /// (ADR-006 — the rules live in <see cref="Game"/>; this only forwards the command and reflects the outcome).
    /// </summary>
    public void PayBoycott(string goodsId)
    {
        try
        {
            _game.PayArrears(goodsId);
            MarkDirty(); // 86d3fq1v8
            _notice = $"Boycott lifted on {goodsId[(goodsId.LastIndexOf('.') + 1)..]}.";
        }
        catch (InvalidMoveException ex)
        {
            _notice = ex.Message; // e.g. "Lifting the boycott costs N gold." — show, don't throw to the UI
        }
        RefreshView();
    }

    /// <summary>
    /// Thin colony-screen command (86d3f5y8r): loads <paramref name="amount"/> of <paramref name="goodsId"/> from
    /// <paramref name="colony"/>'s warehouse into <paramref name="carrier"/>'s holds via the <see cref="Game.LoadFromColony"/>
    /// oracle, then refreshes. The engine guards the move (not a carrier / not adjacent / colony lacks the goods / hold full)
    /// and throws <see cref="InvalidMoveException"/>; the reason is surfaced in the status bar instead of bubbling to the UI
    /// (ADR-006 — the rules live in <see cref="Game"/>; this only forwards the command and reflects the outcome).
    /// </summary>
    public void LoadColonyCargo(Unit carrier, Colony colony, string goodsId, int amount)
    {
        try
        {
            _game.LoadFromColony(carrier, colony, goodsId, amount);
            MarkDirty(); // 86d3fq1v8
            _notice = $"Loaded {amount} {goodsId[(goodsId.LastIndexOf('.') + 1)..]} onto the {carrier.Type.ShortName}.";
            PlaySound(SoundEvent.CargoMoved); // cargo loaded into a hold
        }
        catch (InvalidMoveException ex)
        {
            _notice = ex.Message; // e.g. "The carrier has no room for that cargo." — show, don't throw to the UI
            PlaySound(SoundEvent.IllegalMove); // blocked load → the deny buzz
        }
        RefreshView();
    }

    /// <summary>
    /// Thin colony-screen command (86d3f5y8r): unloads <paramref name="amount"/> of <paramref name="goodsId"/> from
    /// <paramref name="carrier"/>'s holds back into <paramref name="colony"/>'s warehouse via the
    /// <see cref="Game.UnloadToColony"/> oracle, then refreshes. The engine guards the move (not a carrier / not adjacent /
    /// not carrying the goods) and throws <see cref="InvalidMoveException"/>; the reason is surfaced in the status bar
    /// instead of bubbling to the UI (ADR-006 — the unload half of <see cref="LoadColonyCargo"/>).
    /// </summary>
    public void UnloadColonyCargo(Unit carrier, Colony colony, string goodsId, int amount)
    {
        try
        {
            _game.UnloadToColony(carrier, colony, goodsId, amount);
            MarkDirty(); // 86d3fq1v8
            _notice = $"Unloaded {amount} {goodsId[(goodsId.LastIndexOf('.') + 1)..]} into {colony.Name}.";
            PlaySound(SoundEvent.CargoMoved); // cargo unloaded from a hold (shares FreeCol's load/unload clip)
        }
        catch (InvalidMoveException ex)
        {
            _notice = ex.Message;
            PlaySound(SoundEvent.IllegalMove); // blocked unload → the deny buzz
        }
        RefreshView();
    }

    /// <summary>
    /// Thin colony-screen command (86d3f62q8): sets a colony's custom-house per-good export setting (whether the good's
    /// surplus auto-sells each turn, and the warehouse amount to retain) via the <see cref="Game.SetColonyExport"/> oracle,
    /// then refreshes. The engine validates the good is storable + tradeable and throws <see cref="InvalidMoveException"/>
    /// otherwise; the reason is surfaced in the status bar instead of bubbling to the UI (ADR-006 — the rules live in
    /// <see cref="Game"/>; this only forwards the command and reflects the outcome). The custom house then auto-sells the
    /// exported goods over their retain level each colony turn (no caller action beyond End Turn).
    /// </summary>
    public void SetColonyExport(Colony colony, string goodsId, bool exported, int retainLevel)
    {
        try
        {
            _game.SetColonyExport(colony, goodsId, exported, retainLevel);
            MarkDirty(); // 86d3fq1v8
            string good = goodsId[(goodsId.LastIndexOf('.') + 1)..];
            _notice = exported
                ? $"{colony.Name}'s custom house will export {good} above {retainLevel}."
                : $"{colony.Name}'s custom house will keep all its {good}.";
        }
        catch (InvalidMoveException ex)
        {
            _notice = ex.Message; // e.g. "… cannot be exported through the custom house." — show, don't throw to the UI
        }
        RefreshView();
    }

    /// <summary>
    /// Thin colony-screen command (86d3fq0bq): throws away <paramref name="amount"/> of <paramref name="goodsId"/> from
    /// <paramref name="colony"/>'s warehouse via the <see cref="Game.DumpColonyGoods"/> oracle — the FreeCol warehouse
    /// discard, freeing space for a good that cannot be sold (boycotted) or is overflowing and wasting production. The
    /// engine guards the amount (positive, ≤ the stored stock) and throws <see cref="InvalidMoveException"/>; the reason
    /// is surfaced in the status bar instead of bubbling to the UI (ADR-006 — the rule lives in <see cref="Game"/>; this
    /// only forwards the command and reflects the outcome). No gold and no market move (unlike a sale).
    /// </summary>
    public void DumpColonyGoods(Colony colony, string goodsId, int amount)
    {
        try
        {
            _game.DumpColonyGoods(colony, goodsId, amount);
            MarkDirty(); // 86d3fq1v8 — the warehouse changed
            _notice = $"Threw away {amount} {goodsId[(goodsId.LastIndexOf('.') + 1)..]} from {colony.Name}.";
            PlaySound(SoundEvent.CargoMoved); // goods leaving the warehouse (shares the load/unload clip)
        }
        catch (InvalidMoveException ex)
        {
            _notice = ex.Message;
            PlaySound(SoundEvent.IllegalMove); // refused dump → the deny buzz
        }
        RefreshView();
    }

    /// <summary>Opens the Europe screen (dock, recruits, ships in port). Public so scene tests can drive it.</summary>
    public void OpenEuropePanel() =>
        ((EuropePanel)_europePanel).Open(_game, RefreshView);

    /// <summary>Opens the trade-route management screen (list/create/assign/delete routes). Public so scene tests can drive it.</summary>
    public void OpenTradeRoutePanel() =>
        ((TradeRoutePanel)_tradeRoutePanel).Open(_game, RefreshView);

    /// <summary>Opens the empire colony report (per-colony population / production / build requirements). Public so scene tests can drive it.</summary>
    public void OpenColonyReportPanel() =>
        ((ColonyReportPanel)_colonyReportPanel).Open(_game);

    /// <summary>
    /// Opens the message log — the accumulated per-turn notices that the dismissible <see cref="TurnMessagePanel"/>
    /// showed and then forgot. The log is persisted across save/load (save v59, round-tripped through
    /// <see cref="SaveGame.MessageLog"/>), so a reloaded game keeps its history. The panel groups by turn and filters by
    /// <see cref="MessageCategory"/>; the hidden-category set is the live <see cref="SettingsModel.HiddenMessageCategories"/>
    /// client option, and toggling a category box persists the choice to <c>settings.cfg</c>. Public so scene tests can
    /// drive it directly.
    /// </summary>
    public void OpenMessageLogPanel()
    {
        SettingsService? settings = GetNodeOrNull<SettingsService>("/root/Settings");
        // No Settings autoload (a bare test scene) → an ephemeral hide-set with a no-op persist; the filter still works
        // within the open panel, it just is not written anywhere.
        ISet<MessageCategory> hidden = settings?.Settings.HiddenMessageCategories ?? new HashSet<MessageCategory>();
        _messageLogPanel.Open(_messageLog, hidden, (category, nowHidden) =>
        {
            // The hidden set was already mutated in-place by the panel; mirror that into the live settings model (it is
            // the same instance when Settings exists) and persist. The category/nowHidden args make the intent explicit.
            settings?.UpdateAndApply(s =>
            {
                if (nowHidden)
                {
                    s.HiddenMessageCategories.Add(category);
                }
                else
                {
                    s.HiddenMessageCategories.Remove(category);
                }
            });
            settings?.Save();
        });
    }

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

    /// <summary>
    /// Thin native-mission command (`86d3f62qr`): the human's missionary <paramref name="unit"/> establishes a mission at
    /// <paramref name="settlement"/> via the <see cref="Game.EstablishMission(Unit, NativeSettlement)"/> oracle, returning a
    /// one-line outcome the <see cref="NativeSettlementPanel"/> surfaces (and forwards to the status bar). The engine routes
    /// install-vs-denounce, the −100 goodwill, the line-of-sight reveal and the missionary's consumption; if the tribe is
    /// Angry/Hateful it kills the missionary instead (the oracle returns <c>false</c>) — both are faithful, neither throws to
    /// the UI. A disallowed attempt (no movement, not adjacent, not a missionary) throws
    /// <see cref="InvalidMoveException"/>, caught here and shown as a notice (ADR-006 — the rules live in <see cref="Game"/>;
    /// this only forwards the command and reports the result). RNG (only the rival-denounce branch draws) is the engine's
    /// injected stream — never <c>new Random()</c> (ADR-009).
    /// </summary>
    public string EstablishMission(Unit unit, NativeSettlement settlement)
    {
        try
        {
            bool installed = _game.EstablishMission(unit, settlement);
            return installed
                ? "Your missionary established a mission. The tribe softens toward you."
                : "The tribe killed your missionary before he could preach.";
        }
        catch (InvalidMoveException ex)
        {
            return ex.Message; // e.g. "Move next to the settlement to establish a mission." — show, don't throw to the UI
        }
    }

    /// <summary>
    /// Thin native-mission command (`86d3f62qr`): the human's missionary <paramref name="unit"/> denounces the <b>rival</b>
    /// mission standing at <paramref name="settlement"/> via the <see cref="Game.DenounceMission(Unit, NativeSettlement)"/>
    /// oracle (an immigration-weighted roll on the human's own RNG stream — ADR-009, the engine's injected stream, never
    /// <c>new Random()</c>), returning a one-line outcome. On a winning roll the rival is expelled and the human's mission
    /// installed (unless an Angry/Hateful tribe kills the challenger first); a losing roll consumes the missionary for
    /// nothing. The oracle never throws on those outcomes; a disallowed attempt (no rival mission, own mission, not a
    /// denouncer) throws <see cref="InvalidMoveException"/>, caught here and shown as a notice (ADR-006).
    /// </summary>
    public string DenounceMission(Unit unit, NativeSettlement settlement)
    {
        try
        {
            bool installed = _game.DenounceMission(unit, settlement);
            return installed
                ? "You denounced the rival mission and took it over."
                : "Your denunciation failed; your missionary was cast out.";
        }
        catch (InvalidMoveException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Opens the native-settlement interaction panel, acting with <paramref name="actingUnit"/> (may be null — the panel then prompts to select one).</summary>
    public void OpenNativeSettlementPanel(NativeSettlement settlement, Unit? actingUnit)
    {
        int actingId = actingUnit?.Id ?? 0;
        ((NativeSettlementPanel)_nativePanel).Open(_game, settlement, actingId, EstablishMission, DenounceMission, outcome =>
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
        file.StoreString(BuildSave().ToJson());
        MarkClean(); // quicksave also clears unsaved changes (86d3fq1v8)
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
        RestoreMessageLog(save);
        _notice = "Game loaded.";
        RefreshView();
    }

    /// <summary>Saves the current game to <paramref name="path"/> (creating the saves directory if needed). Used by the save/load dialog.</summary>
    public void SaveTo(string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(SavesDir);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(BuildSave().ToJson());
        MarkClean(); // the game now matches disk — no unsaved changes (86d3fq1v8); a manual save or autosave both clear it
    }

    /// <summary>Loads a game from <paramref name="path"/> under the save's own variant ruleset (ADR-018). Used by the save/load dialog and the boot-time pending load.</summary>
    public void LoadFrom(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        SaveGame save = SaveGame.FromJson(file.GetAsText());
        _variant = GameVariants.Resolve(save.Variant);
        StartGame(save.Restore(_variant.LoadRuleset(save.DifficultyLevelOrDefault)));
        RestoreMessageLog(save);
    }

    /// <summary>
    /// Builds the <see cref="SaveGame"/> snapshot for the current game, attaching the presentation-owned in-session
    /// <see cref="_messageLog"/> as <see cref="SaveGame.MessageLog"/> (save v59). The strings are formatted here
    /// (ADR-006), so the controller — not <see cref="SaveGame.From"/> — supplies them, via a <c>with</c>; an empty log
    /// leaves the field null so a no-notice game serialises byte-identically to v58.
    /// </summary>
    private SaveGame BuildSave() => SaveGame.From(_game, _variant.Id) with
    {
        MessageLog = _messageLog.Count > 0
            ? _messageLog
                .SelectMany(entry => entry.Messages.Select(m => new SavedLogMessage(entry.Turn, (int)m.Category, m.Text)))
                .ToList()
            : null,
    };

    /// <summary>
    /// Re-fills the in-session <see cref="_messageLog"/> from a just-loaded <paramref name="save"/> (save v59),
    /// re-grouping the flat per-message rows back into one <see cref="MessageLogPanel.Entry"/> per turn (preserving
    /// turn order and within-turn order). A pre-v59 / omitted log leaves the log empty (already cleared by
    /// <see cref="StartGame"/>) — exactly the prior behaviour, where the log was never persisted.
    /// </summary>
    private void RestoreMessageLog(SaveGame save)
    {
        if (save.MessageLog is not { } rows || rows.Count == 0)
        {
            return; // pre-v59 / empty → the log stays cleared
        }
        // Group consecutive rows by turn, preserving order — the save writes them turn-grouped and in-order, so a simple
        // run-length grouping reproduces the original per-turn entries exactly.
        var current = new List<MessageLogPanel.LogMessage>();
        int currentTurn = rows[0].Turn;
        foreach (SavedLogMessage row in rows)
        {
            if (row.Turn != currentTurn && current.Count > 0)
            {
                _messageLog.Add(new MessageLogPanel.Entry(currentTurn, current));
                current = new List<MessageLogPanel.LogMessage>();
            }
            currentTurn = row.Turn;
            current.Add(new MessageLogPanel.LogMessage((MessageCategory)row.Category, row.Text));
        }
        if (current.Count > 0)
        {
            _messageLog.Add(new MessageLogPanel.Entry(currentTurn, current));
        }
    }

    /// <summary>
    /// Shows the bottom-right HUD button column only while no full-screen panel (<see cref="_colonyPanel"/>/
    /// <see cref="_europePanel"/>, both <c>anchors_preset=15</c>) is open (86d3fr6bc). The column is declared after those
    /// panels in <c>main.tscn</c>, so as a later sibling it draws on top and receives input first over the panel's
    /// bottom-right footprint; hiding it while a panel is open stops it both floating over the panel and consuming
    /// clicks meant for it. Wired to each panel's <c>VisibilityChanged</c> and called from <see cref="RefreshView"/>.
    /// The <see cref="_independenceButton"/> keeps its game-state gate (FreeCol's <c>declareIndependence</c> menu item —
    /// shown only when <see cref="Game.CheckDeclareIndependence"/> allows and the human is not defeated), now also
    /// suppressed while a panel is open. A no-op before a game starts (no <c>_game</c>), so the column keeps its scene default.
    /// </summary>
    private void RefreshHudButtonVisibility()
    {
        if (_game is null)
        {
            return; // not in a game yet (the menu) — leave the column at its scene-default visibility
        }
        bool fullScreenPanelOpen = _colonyPanel.Visible || _europePanel.Visible;
        foreach (Button button in _cornerHudButtons)
        {
            button.Visible = !fullScreenPanelOpen;
        }
        _independenceButton.Visible = !fullScreenPanelOpen
            && !_game.IsHumanDefeated && _game.CheckDeclareIndependence(_game.HumanPlayer).Allowed;
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
        // Outline the selected unit's standing goto destination, if any, and preview the projected route (86d3fq1pe).
        if (_selectedUnit is { Destination: { } dest })
        {
            _gotoMarker.Position = MapView.TileCentre(dest);
            _gotoMarker.Visible = true;
            _gotoMarker.QueueRedraw();
            _mapView.ShowRoutePreview(_game.PreviewRoute(_selectedUnit, dest), _selectedUnit.Position);
        }
        else
        {
            _gotoMarker.Visible = false;
            _mapView.ClearRoutePreview();
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
            "   |   Enter end turn, Space skip, F1 keys";
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
            // Cash-in treasure: shown only for treasure-carrying units (the discoverable surface for the existing
            // CheckCashInTreasureTrain/CashInTreasureTrain command), enabled when the train can be cashed in where it
            // stands — at an owned colony or aboard a galleon docked in Europe (ADR-006 oracle).
            _cashInTreasureButton.Visible = sel.Type.CarryTreasure;
            _cashInTreasureButton.Disabled = !_game.CheckCashInTreasureTrain(sel).Allowed;
            // Skip is always available for a selected unit (a session convenience); Disband gates on its oracle (ADR-006).
            _skipButton.Disabled = false;
            _disbandButton.Disabled = !_game.CheckDisband(sel).Allowed;
            _selectedUnitPanel.Show();
            _advisorPanel.Show(_game.AdviseUnit(sel));
        }
        else
        {
            _selectedUnitPanel.Hide();
            _advisorPanel.Hide();
        }

        // Tile-info readout: the hovered tile's yield preview while the cursor is over the map (86d3fq1nk), else the
        // last-clicked tile's terrain/occupant readout, else hidden — empty until the player hovers/clicks a tile so the
        // camera-centred visual goldens (which never move the mouse) are unaffected (ADR-006).
        RefreshTileInfo();

        // The bottom-right HUD column (incl. the game-state-gated Declare-Independence action) — shown only when no
        // full-screen panel is open, so it never overlaps an open ColonyPanel/EuropePanel (86d3fr6bc).
        RefreshHudButtonVisibility();

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
    /// Reflects the end of the human's game in the HUD — defeat (<see cref="Game.IsHumanDefeated"/>) <b>or</b> a
    /// voluntary <see cref="Game.IsHumanRetired">retirement</see> (86d3fq125): a game-over overlay over the map and a
    /// disabled, relabelled End Turn button. Presentation-only (ADR-006) — both end-states are computed in GameLogic;
    /// this never mutates game state and deliberately does <b>not</b> stop the turn loop (a short-circuit in
    /// <see cref="Game.EndTurn"/> would freeze the human's RNG stream 0 and break ADR-009 byte-stability — see the
    /// human-defeat slice). The overlay's full-rect <c>Control</c> swallows map clicks while shown; "New Game"
    /// (and the N hotkey) start a fresh game, which clears the end-state and hides the overlay.
    /// </summary>
    private void UpdateDefeatUi()
    {
        bool defeated = _game.IsHumanDefeated;
        bool retired = _game.IsHumanRetired;
        bool over = defeated || retired;
        _endTurnButton.Disabled = over;
        _endTurnButton.Text = over ? "Game Over" : "End Turn";
        if (over)
        {
            _gameOverMessage.Text = retired
                ? $"You retired from the colony on turn {_game.Turn}.\nYour deeds are recorded in the high scores."
                : $"You have lost your last colony and all your units on turn {_game.Turn}.\nThe colony is over.";
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
        _gameOverScreen.Visible = over;
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

    /// <summary>
    /// Handles the victory screen's <b>Keep Playing</b> choice (86d3fq161): forwards to <see cref="Game.ContinuePlaying"/>
    /// — which disables the victory conditions so the single-player winner keeps playing — then refreshes the HUD so the
    /// victory state clears and play resumes on the final board. Presentation-only (ADR-006): the rule lives in GameLogic.
    /// </summary>
    private void OnContinuePlaying()
    {
        _game.ContinuePlaying();
        _victoryShown = false; // the win is disabled now; if a later (alternate) victory fires it can show again
        RefreshView();
    }

    /// <summary>
    /// Handles the victory screen's <b>Retire</b> choice (86d3fq125): forwards to <see cref="Game.Retire"/> — which
    /// records the (winning) high score and withdraws the player — then persists the returned score to the leaderboard
    /// (marking the end-of-game one-shot so <see cref="RecordHighScoreIfGameEnded"/> won't double-add) and refreshes so
    /// the end-game state shows. Presentation-only (ADR-006): scoring + withdrawal are decided in GameLogic.
    /// </summary>
    private void OnRetire()
    {
        if (!_game.CheckRetire(_game.HumanPlayer).Allowed)
        {
            return; // can't retire now (already ended) — guard against a stale button press
        }
        HighScore score = _game.Retire(_game.HumanPlayer, _gameId);
        if (!_highScoreRecorded)
        {
            _highScoreRecorded = true; // the retire records the leaderboard entry; block the auto end-of-game record
            HighScoresService.Record(score);
        }
        _victoryPanel.Hide();
        RefreshView(); // the human has withdrawn — the end-game (game-over) UI takes over
    }

    /// <summary>Whether the human may retire right now (FreeCol gates the Retire menu action on a live, in-play player). The pause menu reads this to enable/disable its Retire button. Pure read (ADR-006).</summary>
    public bool CanRetire => _game.CheckRetire(_game.HumanPlayer).Allowed;

    /// <summary>
    /// Whether the game has <b>unsaved changes</b> since the last save / load / new game (86d3fq1v8). The pause menu reads
    /// this to choose between a plain "quit without saving?" confirm (clean) and the unsaved-aware "save before quitting?"
    /// prompt (dirty). Presentation-only bookkeeping (ADR-006); not game state.
    /// </summary>
    public bool HasUnsavedChanges => _dirty;

    /// <summary>
    /// Flags the game as having unsaved changes (86d3fq1v8). Called by the state-mutating command handlers and by End
    /// Turn, so a quit afterwards prompts to save. Idempotent; cheap.
    /// </summary>
    public void MarkDirty() => _dirty = true;

    // Clears the unsaved-changes flag — the game now matches what's on disk. Called after a successful save and from
    // StartGame (a fresh or just-loaded game starts clean).
    private void MarkClean() => _dirty = false;

    /// <summary>
    /// The <b>mid-game Retire</b> entry point (the pause menu's Retire item, FreeCol's <c>RetireAction</c>): records the
    /// human's high score and ends the game for them, then refreshes so the end-game UI takes over. Same flow as the
    /// victory screen's Retire (<see cref="OnRetire"/>); public so the pause menu (and scene tests) can drive it.
    /// </summary>
    public void RequestRetire() => OnRetire();

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
            marker.SetUnit(unit.Type.ShortName, roleShortName, unit.Id); // pass identity so the marker can detect a move and slide (86d3fq26m)
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
            return AccessibilityPalette.Native(NativeUnitColor);
        }
        string? nationId = _game.Players.FirstOrDefault(p => p.PlayerId == unit.OwnerId)?.NationId;
        EuropeanNation? nation = nationId is null
            ? null
            : _game.Ruleset.EuropeanNations.FirstOrDefault(n => n.Id == nationId);
        return nation?.Color is { } hex
            ? AccessibilityPalette.RivalNation(nation.ShortName, Color.FromString(NormalizeHex(hex), FallbackRivalColor))
            : AccessibilityPalette.RivalFallback(FallbackRivalColor);
    }

    /// <summary>Normalises a ruleset colour to Godot's canonical <c>#rrggbb</c> form (e.g. <c>0xff9d3c</c> → <c>#ff9d3c</c>); a parse miss still falls back via <see cref="Color.FromString"/>'s default.</summary>
    private static string NormalizeHex(string hex)
    {
        string bare = hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X') ? hex[2..] : hex.TrimStart('#');
        return "#" + bare;
    }
}
