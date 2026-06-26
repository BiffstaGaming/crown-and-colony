using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// What kind of nation a <see cref="Player"/> is: a European colonial power (the
/// human today; foreign powers later) or a native nation. Drives turn handling and
/// the enemy/fog rules as the multi-player model fills in (ADR-019).
/// </summary>
public enum PlayerType
{
    /// <summary>A European colonial power (the human, and — from FP-3 — the foreign powers).</summary>
    Colonial = 0,

    /// <summary>A native nation.</summary>
    Native = 1,

    /// <summary>A colonial power that has declared independence and is fighting the War of Independence.</summary>
    Rebel = 2,

    /// <summary>A rebel that has won its independence (the REF defeated/expelled).</summary>
    Independent = 3,

    /// <summary>The King's Royal Expeditionary Force — the AI army sent to crush a rebellion.</summary>
    RoyalExpeditionaryForce = 4,

    /// <summary>
    /// A player who has <b>voluntarily retired</b> from the game (FreeCol <c>Player.PlayerType.RETIRED</c>): their
    /// high score is recorded and they withdraw — the game is over for them. Distinct from <see cref="Independent"/>
    /// (a win) and an in-progress defeat. Set by <see cref="Game.Retire"/>; the presentation reads it for the
    /// end-game screen. Persisted as the ordinal in the existing player-type save field.
    /// </summary>
    Retired = 5,
}

/// <summary>
/// One player and all of its <em>player-scoped</em> state (ADR-019): identity, its
/// treasury and tax, its own European <see cref="Trade.Market"/>, its liberty/Congress,
/// its immigration and Europe recruitment dock, and the tiles it has explored.
/// </summary>
/// <remarks>
/// The shared world — the map, units, colonies, native settlements, the turn counter
/// and the main RNG — lives on <see cref="Game"/>; units and colonies are flat global
/// lists referenced by an owner id, not held here (FreeCol treats a player's unit list
/// as derived, not authoritative). For FP-1 there is exactly one player, the human, and
/// it draws from the game's single main RNG stream (stream 0); per-player RNG streams
/// arrive with the foreign powers. State is read publicly and mutated through
/// <c>internal</c> seams used by the rules on <see cref="Game"/> (same assembly).
/// </remarks>
public sealed class Player
{
    private readonly List<string> _congress = [];
    private readonly List<string> _offeredFathers = [];
    private readonly List<string> _recruitDock = [];
    private readonly HashSet<Position> _explored = [];
    private readonly Dictionary<int, Stance> _stance = [];   // this player's directional view of each other player (FP-6a)
    private readonly Dictionary<int, int> _tension = [];     // this player's tension toward each other player (0..MaxTension)
    private readonly Dictionary<int, int> _peaceTurn = [];   // turn this player's peace with each other player was established (FreeCol peaceHolds' peaceTurn; cleared on war)
    private readonly Dictionary<string, int> _unitPrices = []; // this player's escalated Europe purchase prices (artillery); absent = the ruleset base
    private readonly List<TradeRoute> _tradeRoutes = []; // this player's defined trade routes (carriers attach by id)

    /// <summary>Creates a player with its own market; callers seed the remaining state via the internal setters.</summary>
    internal Player(int playerId, string? nationId, bool isHuman, PlayerType playerType, Market market)
    {
        PlayerId = playerId;
        NationId = nationId;
        IsHuman = isHuman;
        PlayerType = playerType;
        Market = market;
    }

    /// <summary>Stable id allocated at creation (the human is 0). Players are found by role/id, never by list index.</summary>
    public int PlayerId { get; }

    /// <summary>
    /// The European/native nation id this player plays. For the human it is the nation chosen at New Game
    /// (<see cref="Game.New"/>'s <c>humanNationId</c>), which grants that nation's advantage + colony names through the
    /// nation-id-driven seams; <b>null</b> for the classic <em>nation-less</em> default human (no advantage), so an
    /// unpicked new game is byte-identical (ADR-009). A foreign power carries its assigned European nation, a native a
    /// native nation-type id. Set once at creation (or restored from the save).
    /// </summary>
    public string? NationId { get; }

    /// <summary>Whether this player is the local human (found via <see cref="Game.HumanPlayer"/>).</summary>
    public bool IsHuman { get; }

    /// <summary>What kind of player this is. Mutates Colonial → Rebel → Independent across the War of Independence (only <see cref="Game"/> changes it).</summary>
    public PlayerType PlayerType { get; internal set; }

    /// <summary>This player's European market (per-player; ADR-019).</summary>
    public Market Market { get; }

    /// <summary>
    /// The deterministic PCG RNG stream id reserved for this player (ADR-009): the human is stream 0
    /// (the game's main stream); other players reserve <c>PlayerId + 1</c> so they avoid both stream 0 and
    /// the native-placement stream (1). The actual stream is created when the AI needs it (FP-4+); reserving
    /// the id now keeps the human's stream 0 — and therefore all existing seeded games/goldens — byte-stable.
    /// </summary>
    public ulong RngStreamId => PlayerId == 0 ? 0UL : (ulong)PlayerId + 1;

    /// <summary>
    /// This non-human player's own PCG stream (ADR-009), used by its AI from FP-4 and saved/restored like
    /// the main stream. Null for the human, which draws from the game's stream 0 (<c>Game._random</c>).
    /// </summary>
    internal Pcg32Random? Rng { get; set; }

    /// <summary>The player's treasury in gold.</summary>
    public int Gold { get; internal set; }

    /// <summary>Sales tax as a percentage (0–100) deducted from this player's European sales.</summary>
    public int TaxRate { get; internal set; }

    /// <summary>
    /// True while this player is bankrupt — i.e. it could not pay its building upkeep on the most recent upkeep turn
    /// (FreeCol <c>Player.getBankrupt</c> / <c>model.disaster.bankruptcy</c>). While bankrupt every colony's building
    /// production is penalised (the bankruptcy disaster's −50% building-production modifier); the flag clears the turn
    /// the player can pay again. <b>Transient</b>: recomputed each turn in <c>PayBuildingUpkeep</c>, never persisted —
    /// so it adds no save field and the save version is unchanged. Only ever set when building upkeep is enabled
    /// (<see cref="Specification.Ruleset.UpkeepEnabled"/>, classic default off), so it is always false in the classic
    /// default game. See [colonies].
    /// </summary>
    public bool Bankrupt { get; internal set; }

    /// <summary>
    /// True once the King is displeased with this player — set when it declines an affordable mercenary offer
    /// (FreeCol <c>Monarch.setDispleasure</c>). While displeased the King offers no more mercenaries or military
    /// support. Persisted with the mercenary slice (omit-when-false). See [monarchy].
    /// </summary>
    public bool MonarchDispleasure { get; internal set; }

    /// <summary>
    /// True once the King has granted this player naval support (FreeCol <c>Monarch.supportSea</c>) — a one-shot,
    /// so SUPPORT_SEA cannot repeat. Persisted with the support slice (omit-when-false). See [monarchy].
    /// </summary>
    public bool SupportSeaGranted { get; internal set; }

    /// <summary>
    /// The turn the King last raised this player's tax (a successful RAISE_TAX demand became a real rate rise), or null
    /// if he never has. Used to enforce the inter-raise tax grace period: the chooser will not offer another tax raise
    /// until the cooldown has elapsed (see <see cref="Game.TaxRaiseGraceTurns"/>). FreeCol has no such cooldown — its
    /// monarch can demand a raise turn after turn, which the FreeCol team itself acknowledges makes its King "more
    /// aggressive with tax increases than Col1 monarchs were" — so this restores the original game's gradual climb.
    /// Persisted v60 (omit-when-null). See [monarchy].
    /// </summary>
    public int? LastTaxRaiseTurn { get; internal set; }

    /// <summary>The turn this player declared independence (FreeCol <c>Player.getIndependenceTurn</c>), or null if it never did. Persisted v41 (omit-when-null). See [independence].</summary>
    public int? DeclaredIndependenceTurn { get; internal set; }

    /// <summary>Bells accrued by a rebel toward the Foreign Intervention Force (FreeCol <c>interventionBells</c>); 0 until rebellion. Persisted v41 (omit-when-0). See [independence].</summary>
    public int InterventionBells { get; internal set; }

    /// <summary>Liberty points banked toward this player's next Founding Father.</summary>
    public int Liberty { get; internal set; }

    /// <summary>The father this player is currently recruiting (null = none chosen).</summary>
    public string? CurrentFather { get; internal set; }

    /// <summary>Immigration points banked toward this player's next emigrant.</summary>
    public int Immigration { get; internal set; }

    /// <summary>Immigration points needed for this player's next emigrant (rises after each emigrant).</summary>
    public int ImmigrationRequired { get; internal set; } = Game.InitialImmigration;

    /// <summary>The escalating base used in the recruit-price formula (FreeCol <c>baseRecruitPrice</c>).</summary>
    public int BaseRecruitPrice { get; internal set; } = Game.InitialRecruitPrice;

    /// <summary>The recruit-price floor (FreeCol <c>recruitLowerCap</c>).</summary>
    public int RecruitLowerCap { get; internal set; } = Game.InitialRecruitLowerCap;

    /// <summary>Founding Fathers elected to this player's Continental Congress, in election order.</summary>
    public IReadOnlyList<string> Congress => _congress;

    /// <summary>The fathers offered to this player this round — one per category with an eligible candidate.</summary>
    public IReadOnlyList<string> OfferedFathers => _offeredFathers;

    /// <summary>The unit types waiting on this player's Europe recruitment dock.</summary>
    public IReadOnlyList<string> RecruitDock => _recruitDock;

    /// <summary>Tiles this player has ever seen (permanent fog of war).</summary>
    public IReadOnlySet<Position> Explored => _explored;

    /// <summary>This player's diplomatic <see cref="Stance"/> toward each other player it has met, by their <see cref="PlayerId"/> (FP-6a; an absent entry = <see cref="Stance.Uncontacted"/>).</summary>
    public IReadOnlyDictionary<int, Stance> Stances => _stance;

    /// <summary>This player's tension toward each other player, by their <see cref="PlayerId"/> (0..<see cref="Game.MaxTension"/>; an absent entry = 0).</summary>
    public IReadOnlyDictionary<int, int> Tensions => _tension;

    /// <summary>
    /// The <see cref="Game.Turn"/> on which this player's <b>peace</b> (or alliance) with each other player was last
    /// established, by their <see cref="PlayerId"/> — FreeCol <c>EuropeanAIPlayer.peaceHolds</c>' <c>peaceTurn</c>. An
    /// absent entry means no peace is currently on record (never met, or the last transition with that player was a
    /// declaration of war), and <see cref="Game"/>'s peace-hold decay treats it as "no recent treaty". Stamped when a
    /// Peace/Alliance stance is set, cleared when War is declared.
    /// </summary>
    public IReadOnlyDictionary<int, int> PeaceTurns => _peaceTurn;

    /// <summary>This player's <b>escalated</b> Europe purchase prices by unit-type id (FreeCol per-player <c>unitPrices</c>): an absent entry means the ruleset base price still applies. Today only artillery escalates (+100 per purchase).</summary>
    public IReadOnlyDictionary<string, int> UnitPriceOverrides => _unitPrices;

    /// <summary>Mutable view of <see cref="UnitPriceOverrides"/> for the Europe-pricing rules on <see cref="Game"/>.</summary>
    internal Dictionary<string, int> UnitPriceMap => _unitPrices;

    /// <summary>This player's defined trade routes (FreeCol per-player <c>tradeRoutes</c>); a carrier attaches to one by <see cref="Units.Unit.TradeRouteId"/>.</summary>
    public IReadOnlyList<TradeRoute> TradeRoutes => _tradeRoutes;

    /// <summary>Mutable view of <see cref="TradeRoutes"/> for the trade-route rules on <see cref="Game"/>.</summary>
    internal List<TradeRoute> TradeRoutesList => _tradeRoutes;

    /// <summary>The next trade-route id to allocate for this player (monotonic; persisted so ids stay stable across save/load).</summary>
    public int NextTradeRouteId { get; internal set; } = 1;

    /// <summary>Mutable view of <see cref="Congress"/> for the rules on <see cref="Game"/>.</summary>
    internal List<string> CongressList => _congress;

    /// <summary>Mutable view of <see cref="OfferedFathers"/> for the rules on <see cref="Game"/>.</summary>
    internal List<string> OfferedFathersList => _offeredFathers;

    /// <summary>Mutable view of <see cref="RecruitDock"/> for the rules on <see cref="Game"/>.</summary>
    internal List<string> RecruitDockList => _recruitDock;

    /// <summary>Mutable view of <see cref="Explored"/> for the rules on <see cref="Game"/>.</summary>
    internal HashSet<Position> ExploredSet => _explored;

    /// <summary>Mutable view of <see cref="Stances"/> for the diplomacy rules on <see cref="Game"/>.</summary>
    internal Dictionary<int, Stance> StanceMap => _stance;

    /// <summary>Mutable view of <see cref="Tensions"/> for the diplomacy rules on <see cref="Game"/>.</summary>
    internal Dictionary<int, int> TensionMap => _tension;

    /// <summary>Mutable view of <see cref="PeaceTurns"/> for the diplomacy rules on <see cref="Game"/>.</summary>
    internal Dictionary<int, int> PeaceTurnMap => _peaceTurn;

    /// <summary>
    /// Current gold price to buy one recruit from the dock (FreeCol
    /// <c>Europe.getCurrentRecruitPrice</c>): <c>max(base·max(required−immigration,0)/required, floor)</c>.
    /// </summary>
    public int RecruitPrice
    {
        get
        {
            int difference = Math.Max(ImmigrationRequired - Immigration, 0);
            return Math.Max(BaseRecruitPrice * difference / ImmigrationRequired, RecruitLowerCap);
        }
    }
}

/// <summary>
/// One player's state handed to <see cref="Game.Restore"/> (a single human element today). The
/// persistence layer builds these from a save's <c>Players[]</c> (v20+) or, for a v19-and-earlier
/// save, from the flat top-level fields folded into one human player. <see cref="Explored"/> null
/// marks a pre-fog (v1) save, whose fog is re-derived by revealing around the player's units.
/// </summary>
public sealed record RestoredPlayer(
    int PlayerId, string? NationId, bool IsHuman, PlayerType PlayerType,
    int Gold, int TaxRate,
    IReadOnlyDictionary<string, int>? MarketDeltas,
    int Liberty, IEnumerable<string>? Congress, string? CurrentFather,
    IEnumerable<string>? OfferedFathers,
    int Immigration, int ImmigrationRequired, int BaseRecruitPrice, int RecruitLowerCap,
    IEnumerable<string>? RecruitDock,
    IEnumerable<Position>? Explored,
    RandomState? Rng = null,
    IReadOnlyDictionary<int, Stance>? Stances = null,
    IReadOnlyDictionary<int, int>? Tensions = null,
    IReadOnlyDictionary<string, int>? UnitPrices = null,
    IReadOnlyDictionary<string, int>? Arrears = null,
    bool MonarchDispleasure = false,
    bool SupportSeaGranted = false,
    int? LastTaxRaiseTurn = null,
    int? DeclaredIndependenceTurn = null,
    int InterventionBells = 0,
    IReadOnlyList<TradeRoute>? TradeRoutes = null,
    int? NextTradeRouteId = null,
    IReadOnlyDictionary<int, int>? PeaceTurns = null,
    IReadOnlyDictionary<string, TradeAccount>? TradeAccounts = null);
