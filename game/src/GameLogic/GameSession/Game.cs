using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One running game: the map, the units, exploration state, and the turn counter.
/// All mutations of game state go through methods on this class so rules are
/// enforced in one place.
/// </summary>
public sealed class Game
{
    /// <summary>The starting unit's type for a new game.</summary>
    public const string StartingUnitTypeId = "model.unit.freeColonist";

    /// <summary>Default colony names, used in founding order (nation-specific lists come with nations).</summary>
    private static readonly string[] ColonyNames =
    [
        "Jamestown", "Plymouth", "Boston", "New Amsterdam", "Charlesfort",
        "Salem", "Penobscot", "Roanoke", "New Haven", "Providence",
    ];

    /// <summary>The warehouse goods id for liberty bells.</summary>
    private const string BellsId = "model.goods.bells";

    /// <summary>The terrain a ship sets sail to Europe from (the map's outer edge).</summary>
    private const string HighSeasId = "model.tile.highSeas";

    /// <summary>
    /// Liberty multiplier in the Founding Father cost formula. Classic "other"
    /// difficulty = 24 (spec <c>model.option.foundingFatherFactor</c>);
    /// difficulty-based values arrive with the difficulty system.
    /// </summary>
    public const int FoundingFatherFactor = 24;

    /// <summary>The warehouse goods id for religious crosses (immigration points).</summary>
    private const string CrossesId = "model.goods.crosses";

    /// <summary>Immigration points needed for the first emigrant (spec <c>model.option.initialImmigration</c>, classic 15).</summary>
    public const int InitialImmigration = 15;

    /// <summary>Added to the immigration target after each emigrant (spec <c>crossesIncrement</c>, classic medium 2).</summary>
    public const int CrossesIncrement = 2;

    /// <summary>Immigration lost per person idling in Europe each turn (spec <c>europeanUnitImmigrationPenalty</c>, classic −4).</summary>
    public const int EuropeUnitImmigrationPenalty = -4;

    /// <summary>Flat immigration a colonial player gains each turn (spec <c>playerImmigrationBonus</c>, classic +2).</summary>
    public const int PlayerImmigrationBonus = 2;

    /// <summary>Recruit slots on the Europe dock (FreeCol <c>MigrationType.MIGRANT_COUNT</c>).</summary>
    public const int RecruitSlots = 3;

    /// <summary>Starting base recruit price (FreeCol <c>Europe.RECRUIT_PRICE_INITIAL</c>, classic 200).</summary>
    public const int InitialRecruitPrice = 200;

    /// <summary>Recruit-price floor (FreeCol <c>Europe.LOWER_CAP_INITIAL</c>, classic 80).</summary>
    public const int InitialRecruitLowerCap = 80;

    /// <summary>Base recruit price rise per paid recruit (spec <c>recruitPriceIncrease</c>, classic medium 30).</summary>
    public const int RecruitPriceIncrease = 30;

    /// <summary>Recruit-price-floor rise per paid recruit (spec <c>lowerCapIncrease</c>, classic 0).</summary>
    public const int RecruitLowerCapIncrease = 0;

    private readonly List<Unit> _units = [];
    private readonly List<Colony> _colonies = [];
    private readonly HashSet<Position> _explored = [];
    private readonly List<string> _congress = [];
    private readonly List<string> _offeredFathers = [];
    private readonly List<string> _recruitDock = [];
    private readonly Pcg32Random _random;
    private int _nextUnitId = 1;
    private int _nextColonyId = 1;
    private int _liberty;
    private string? _currentFather;
    private int _immigration;
    private int _immigrationRequired = InitialImmigration;
    private int _baseRecruitPrice = InitialRecruitPrice;
    private int _recruitLowerCap = InitialRecruitLowerCap;

    private Game(Ruleset ruleset, GameMap map, Pcg32Random random, int turn)
    {
        Ruleset = ruleset;
        Map = map;
        _random = random;
        Turn = turn;
        Market = new Market(ruleset);
    }

    /// <summary>The rule data this game plays by.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The game world.</summary>
    public GameMap Map { get; }

    /// <summary>Current turn number, starting at 1.</summary>
    public int Turn { get; private set; }

    /// <summary>The player's treasury in gold.</summary>
    public int Gold { get; private set; }

    /// <summary>Sales tax as a percentage (0–100) deducted from European sales.</summary>
    public int TaxRate { get; private set; }

    /// <summary>The European market (trade prices). (Single shared market until foreign powers arrive.)</summary>
    public Market Market { get; }

    /// <summary>Liberty points banked toward the next Founding Father.</summary>
    public int Liberty => _liberty;

    /// <summary>Founding Fathers elected to the Continental Congress, in election order.</summary>
    public IReadOnlyList<string> Congress => _congress;

    /// <summary>The father the player is currently recruiting (null = none chosen).</summary>
    public string? CurrentFather => _currentFather;

    /// <summary>The fathers offered this round — one per category that has an eligible candidate.</summary>
    public IReadOnlyList<string> OfferedFathers => _offeredFathers;

    /// <summary>Immigration points banked toward the next emigrant (crosses + the Europe contribution).</summary>
    public int Immigration => _immigration;

    /// <summary>Immigration points needed to produce the next emigrant (rises by <see cref="CrossesIncrement"/> each time).</summary>
    public int ImmigrationRequired => _immigrationRequired;

    /// <summary>The unit types waiting on the Europe recruitment dock (one id per <see cref="RecruitSlots"/> slot).</summary>
    public IReadOnlyList<string> RecruitDock => _recruitDock;

    /// <summary>
    /// Current gold price to buy one recruit from the dock (FreeCol
    /// <c>Europe.getCurrentRecruitPrice</c>): <c>max(base·max(required−immigration,0)/required, floor)</c>.
    /// Falls toward the floor as immigration approaches the target, then jumps after each paid recruit.
    /// </summary>
    public int RecruitPrice
    {
        get
        {
            int difference = Math.Max(_immigrationRequired - _immigration, 0);
            return Math.Max(_baseRecruitPrice * difference / _immigrationRequired, _recruitLowerCap);
        }
    }

    /// <summary>The escalating base used in the recruit-price formula (persisted; FreeCol <c>baseRecruitPrice</c>).</summary>
    internal int BaseRecruitPrice => _baseRecruitPrice;

    /// <summary>The recruit-price floor (persisted; FreeCol <c>recruitLowerCap</c>).</summary>
    internal int RecruitLowerCap => _recruitLowerCap;

    /// <summary>
    /// Game age (1–3) used to weight which fathers are offered. Simplified
    /// turn bands until the calendar exists; FreeCol keys age off the year.
    /// </summary>
    public int CurrentAge => Turn < 100 ? 1 : Turn < 200 ? 2 : 3;

    /// <summary>
    /// Liberty needed to elect the next father (FreeCol <c>getTotalFoundingFatherCost</c>):
    /// the first is free-ish at <paramref name="factor"/>, later ones cost
    /// <c>2·(elected+1)·factor + 1</c>.
    /// </summary>
    public static int FoundingFatherCost(int electedCount, int factor) =>
        electedCount == 0 ? factor : 2 * (electedCount + 1) * factor + 1;

    /// <summary>Liberty needed to elect this game's next father.</summary>
    public int TotalFoundingFatherCost() => FoundingFatherCost(_congress.Count, FoundingFatherFactor);

    /// <summary>Chooses which offered father to recruit toward.</summary>
    /// <exception cref="InvalidMoveException">The father is not currently offered.</exception>
    public void ChooseFather(string fatherId)
    {
        if (!_offeredFathers.Contains(fatherId))
        {
            throw new InvalidMoveException($"{fatherId} is not currently offered.");
        }
        _currentFather = fatherId;
    }

    /// <summary>All units in the game.</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>All colonies, in founding order.</summary>
    public IReadOnlyList<Colony> Colonies => _colonies;

    /// <summary>The colony on a tile, or null.</summary>
    public Colony? ColonyAt(Position p) => _colonies.FirstOrDefault(c => c.Position == p);

    /// <summary>Tiles the player has seen (fog of war).</summary>
    public IReadOnlySet<Position> Explored => _explored;

    /// <summary>Whether a tile has been revealed.</summary>
    public bool IsExplored(Position p) => _explored.Contains(p);

    /// <summary>
    /// Starts a new game: generates a map from the seed and places one starting
    /// colonist on the first settleable land tile, revealing its surroundings.
    /// </summary>
    public static Game New(
        Ruleset ruleset, ulong seed, int mapWidth = 36, int mapHeight = 24,
        int startingGold = 0, int startingTax = 0)
    {
        var random = new Pcg32Random(seed);
        GameMap map = MapGenerator.Generate(ruleset, mapWidth, mapHeight, random);

        var game = new Game(ruleset, map, random, turn: 1)
        {
            Gold = startingGold,
            TaxRate = startingTax,
        };

        // Start on settleable land that has somewhere to walk to (not a 1-tile
        // islet), preferring temperate latitudes (nearest the equator row) over
        // a polar landfall.
        bool Settleable(Position p)
        {
            TerrainType t = map.TerrainAt(p);
            return !t.IsWater && t.CanSettle;
        }
        int equator = mapHeight / 2;
        Position start = map.AllPositions()
            .Where(Settleable)
            .OrderBy(p => Math.Abs(p.Y - equator))
            .FirstOrDefault(
                p => p.Neighbours().Any(n => map.InBounds(n) && !map.TerrainAt(n).IsWater),
                map.AllPositions().First(Settleable));
        game.SpawnUnit(ruleset.Unit(StartingUnitTypeId), start);
        game.GenerateOffers(); // Congress choices available from the first turn
        game.InitRecruitDock(); // three recruits waiting on the Europe dock from turn 1

        return game;
    }

    /// <summary>Restores a game from saved state (see <see cref="Persistence.SaveGame"/>).</summary>
    internal static Game Restore(
        Ruleset ruleset, GameMap map, RandomState randomState, int turn,
        IEnumerable<(int id, UnitType type, Position position, int movementLeft,
            UnitLocation location, int sailTurns, IReadOnlyDictionary<string, int>? cargo)> units,
        IEnumerable<Position>? explored,
        IEnumerable<Colony>? colonies = null,
        int gold = 0, int taxRate = 0,
        IReadOnlyDictionary<string, int>? marketDeltas = null,
        int liberty = 0, IEnumerable<string>? congress = null,
        string? currentFather = null, IEnumerable<string>? offeredFathers = null,
        int immigration = 0, int immigrationRequired = InitialImmigration,
        int baseRecruitPrice = InitialRecruitPrice, int recruitLowerCap = InitialRecruitLowerCap,
        IEnumerable<string>? recruitDock = null)
    {
        var game = new Game(ruleset, map, Pcg32Random.FromState(randomState), turn)
        {
            Gold = gold,
            TaxRate = taxRate,
        };
        if (marketDeltas is { Count: > 0 })
        {
            game.Market.LoadDeltas(marketDeltas);
        }
        game._liberty = liberty;
        game._currentFather = currentFather;
        if (congress is not null)
        {
            game._congress.AddRange(congress);
        }
        if (offeredFathers is not null)
        {
            game._offeredFathers.AddRange(offeredFathers);
        }
        game._immigration = immigration;
        game._immigrationRequired = immigrationRequired;
        game._baseRecruitPrice = baseRecruitPrice;
        game._recruitLowerCap = recruitLowerCap;
        if (recruitDock is not null)
        {
            game._recruitDock.AddRange(recruitDock);
        }
        // Top up to a full dock: a no-op when the save held all slots (so the RNG
        // sequence is preserved); draws a fresh dock for pre-v12 saves that had none.
        game.InitRecruitDock();
        foreach ((int id, UnitType type, Position position, int movementLeft,
                  UnitLocation location, int sailTurns, IReadOnlyDictionary<string, int>? cargo) in units)
        {
            var unit = new Unit(id, type, position)
            {
                MovementLeft = movementLeft,
                Location = location,
                SailTurnsRemaining = sailTurns,
            };
            foreach ((string goodsId, int amount) in cargo ?? new Dictionary<string, int>())
            {
                unit.AddCargo(goodsId, amount);
            }
            game._units.Add(unit);
            game._nextUnitId = Math.Max(game._nextUnitId, id + 1);
        }

        foreach (Colony colony in colonies ?? [])
        {
            game._colonies.Add(colony);
            game._nextColonyId = Math.Max(game._nextColonyId, colony.Id + 1);
        }

        if (explored is not null)
        {
            game._explored.UnionWith(explored.Where(map.InBounds));
        }
        else
        {
            // Pre-fog save (format v1): reveal around the units we have.
            foreach (Unit unit in game._units)
            {
                game.Reveal(unit);
            }
        }
        return game;
    }

    /// <summary>The game's RNG state, captured for saving.</summary>
    internal RandomState RandomState => _random.SaveState();

    /// <summary>Creates a new unit at a position and reveals its surroundings.</summary>
    public Unit SpawnUnit(UnitType type, Position position)
    {
        if (!Map.InBounds(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Off the map.");
        }
        if (Map.TerrainAt(position).IsWater != type.IsNaval)
        {
            throw new InvalidMoveException(type.IsNaval
                ? "Naval units must be placed on water."
                : "Land units cannot be placed on water.");
        }

        var unit = new Unit(_nextUnitId++, type, position);
        _units.Add(unit);
        Reveal(unit);
        return unit;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may move to <paramref name="target"/> right now,
    /// and why not if not.
    /// </summary>
    public MoveCheck CheckMove(Unit unit, Position target)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Destination is off the map.");
        }
        if (!unit.Position.IsAdjacentTo(target))
        {
            return MoveCheck.No("Units move one tile at a time.");
        }

        TerrainType terrain = Map.TerrainAt(target);
        if (terrain.IsWater && !unit.Type.IsNaval)
        {
            return MoveCheck.No("Land units cannot enter water.");
        }
        if (!terrain.IsWater && unit.Type.IsNaval)
        {
            return MoveCheck.No("Ships cannot move onto land.");
        }

        int movesLeft = unit.MovementLeft;
        if (movesLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }

        // FreeCol's partial-movement rule (Unit.getMoveCost): when the terrain
        // costs more than the unit has left, the move is still allowed — for the
        // full remainder — only if the unit is near full movement (lost at most
        // 2/3 of a move) or the shortfall is small. (A settlement target also
        // qualifies; none exist yet.) Otherwise the unit must wait.
        int cost = terrain.MoveCost;
        if (cost > movesLeft)
        {
            bool allowed = movesLeft + 2 >= unit.Type.Movement || cost <= movesLeft + 2;
            if (!allowed)
            {
                return MoveCheck.No("Not enough movement left this turn.");
            }
            cost = movesLeft;
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>Moves a unit one tile, spending movement and revealing new ground.</summary>
    /// <exception cref="InvalidMoveException">The move is not allowed; see <see cref="CheckMove"/>.</exception>
    public void MoveUnit(Unit unit, Position target)
    {
        MoveCheck check = CheckMove(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        unit.Position = target;
        unit.MovementLeft -= check.Cost;
        Reveal(unit);
    }

    /// <summary>
    /// Whether <paramref name="unit"/> may found a colony where it stands, and why
    /// not if not.
    /// </summary>
    public MoveCheck CheckFoundColony(Unit unit)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!unit.Type.CanFoundColony)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot found a colony.");
        }
        TerrainType terrain = Map.TerrainAt(unit.Position);
        if (!terrain.CanSettle)
        {
            return MoveCheck.No($"A colony cannot be built on {terrain.ShortName}.");
        }
        if (ColonyAt(unit.Position) is not null)
        {
            return MoveCheck.No("There is already a colony here.");
        }
        // TODO cross-check: FreeCol/original minimum-distance-between-colonies rule.
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Founds a colony where the unit stands. The founding unit settles down and
    /// becomes the colony's first colonist (it leaves the map).
    /// </summary>
    /// <exception cref="InvalidMoveException">Founding is not allowed; see <see cref="CheckFoundColony"/>.</exception>
    public Colony FoundColony(Unit unit)
    {
        MoveCheck check = CheckFoundColony(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        string name = ColonyNames[(_nextColonyId - 1) % ColonyNames.Length];
        var colony = new Colony(_nextColonyId++, name, unit.Position, population: 1);

        // Every colony starts with the free base buildings (no build cost, not
        // an upgrade) — town hall, carpenter's house, the artisan houses, etc.
        foreach (BuildingType building in Ruleset.BuildingTypes
                     .Where(b => b.BuildCost.Count == 0 && b.UpgradesFrom is null))
        {
            colony.AddBuilding(building.Id);
        }

        _colonies.Add(colony);
        _units.Remove(unit);
        AutoAssignIdleToFood(colony);
        return colony;
    }

    /// <summary>
    /// Whether an idle colonist may be put to work in one of the colony's buildings.
    /// </summary>
    public MoveCheck CheckAssignBuildingWork(Colony colony, string buildingId)
    {
        if (!colony.HasBuilding(buildingId))
        {
            return MoveCheck.No("The colony does not have that building.");
        }
        if (colony.IdleColonists <= 0)
        {
            return MoveCheck.No("No idle colonists.");
        }
        BuildingType building = Ruleset.Building(buildingId);
        if (colony.BuildingWorkers.GetValueOrDefault(buildingId) >= building.Workplaces)
        {
            return MoveCheck.No($"The {building.ShortName} is fully staffed.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Puts an idle colonist to work in a building.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignBuildingWork"/>.</exception>
    public void AssignBuildingWork(Colony colony, string buildingId)
    {
        MoveCheck check = CheckAssignBuildingWork(colony, buildingId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.SetBuildingWorkers(buildingId, colony.BuildingWorkers.GetValueOrDefault(buildingId) + 1);
    }

    /// <summary>Returns one of a building's workers to the idle pool.</summary>
    public void UnassignBuildingWork(Colony colony, string buildingId) =>
        colony.SetBuildingWorkers(buildingId, colony.BuildingWorkers.GetValueOrDefault(buildingId) - 1);

    /// <summary>
    /// Sells goods from a colony's warehouse to the European market, crediting the
    /// treasury after tax and moving the market price. (Phase 4 slice 1: an abstract
    /// "shipped to Europe" sale; slice 3 will require an actual ship to carry it.)
    /// </summary>
    /// <returns>The gold credited to the treasury after tax.</returns>
    /// <exception cref="InvalidMoveException">The good is untradeable or the colony lacks the amount.</exception>
    public int SellColonyGoods(Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!Market.IsTradeable(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} cannot be sold in Europe.");
        }
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId} to sell.");
        }

        colony.AddGoods(goodsId, -amount);
        SaleResult sale = Market.Sell(goodsId, amount, TaxRate);
        Gold += sale.GoldAfterTax;
        return sale.GoldAfterTax;
    }

    /// <summary>Turns a naval unit spends crossing the high seas each way (FreeCol TURNS_TO_SAIL).</summary>
    public const int SailTurns = 3;

    /// <summary>Units currently docked in Europe.</summary>
    public IEnumerable<Unit> UnitsInEurope => _units.Where(u => u.Location == UnitLocation.InEurope);

    /// <summary>Whether a naval unit may set sail for Europe from where it is.</summary>
    public MoveCheck CheckSailToEurope(Unit unit)
    {
        if (!unit.Type.IsNaval)
        {
            return MoveCheck.No("Only ships can sail to Europe.");
        }
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The ship is not on the map.");
        }
        if (Map.TerrainAt(unit.Position).Id != HighSeasId)
        {
            return MoveCheck.No("Ships sail to Europe from the high seas (the map edge).");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Sends a ship across the high seas to Europe (arrives in <see cref="SailTurns"/> turns).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSailToEurope"/>.</exception>
    public void SailToEurope(Unit unit)
    {
        MoveCheck check = CheckSailToEurope(unit);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        unit.Location = UnitLocation.SailingToEurope;
        unit.SailTurnsRemaining = SailTurns;
    }

    /// <summary>Sends a docked ship back to the New World (re-enters at its departure high-seas tile).</summary>
    /// <exception cref="InvalidMoveException">The ship is not in Europe.</exception>
    public void SailToNewWorld(Unit unit)
    {
        if (unit.Location != UnitLocation.InEurope)
        {
            throw new InvalidMoveException("Only a ship in Europe can sail to the New World.");
        }
        unit.Location = UnitLocation.SailingToNewWorld;
        unit.SailTurnsRemaining = SailTurns;
    }

    /// <summary>Loads goods from a colony's warehouse into an adjacent ship's hold.</summary>
    /// <exception cref="InvalidMoveException">The ship isn't adjacent on the map, or the colony lacks the goods.</exception>
    public void LoadFromColony(Unit ship, Colony colony, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (!ship.Type.IsNaval || !ship.IsOnMap)
        {
            throw new InvalidMoveException("Only a ship on the map can carry cargo.");
        }
        if (!ship.Position.IsAdjacentTo(colony.Position) && ship.Position != colony.Position)
        {
            throw new InvalidMoveException("The ship must be next to the colony to load cargo.");
        }
        if (colony.StoreOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The colony does not have {amount} {goodsId}.");
        }
        colony.AddGoods(goodsId, -amount);
        ship.AddCargo(goodsId, amount);
    }

    /// <summary>Sells goods from a docked ship's hold to the European market, crediting the treasury after tax.</summary>
    /// <returns>The gold credited after tax.</returns>
    /// <exception cref="InvalidMoveException">The ship isn't in Europe, the good is untradeable, or the hold lacks it.</exception>
    public int SellShipCargo(Unit ship, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (ship.Location != UnitLocation.InEurope)
        {
            throw new InvalidMoveException("Goods are sold once the ship reaches Europe.");
        }
        if (!Market.IsTradeable(goodsId))
        {
            throw new InvalidMoveException($"{goodsId} cannot be sold in Europe.");
        }
        if (ship.CargoOf(goodsId) < amount)
        {
            throw new InvalidMoveException($"The ship is not carrying {amount} {goodsId}.");
        }
        ship.AddCargo(goodsId, -amount);
        SaleResult sale = Market.Sell(goodsId, amount, TaxRate);
        Gold += sale.GoldAfterTax;
        return sale.GoldAfterTax;
    }

    /// <summary>
    /// Whether the player can buy <paramref name="amount"/> of a good in Europe for
    /// the docked <paramref name="ship"/> (no market price rise on buying, per FreeCol).
    /// </summary>
    public MoveCheck CheckBuyEuropeGoods(Unit ship, string goodsId, int amount)
    {
        if (ship.Location != UnitLocation.InEurope)
        {
            return MoveCheck.No("Goods are bought once the ship reaches Europe.");
        }
        if (!Market.IsTradeable(goodsId))
        {
            return MoveCheck.No($"{goodsId} is not sold in Europe.");
        }
        int cost = Market.AskPrice(goodsId) * amount;
        if (Gold < cost)
        {
            return MoveCheck.No($"Not enough gold (need {cost}).");
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>Buys goods in Europe into a docked ship's hold, debiting the treasury at the ask price.</summary>
    /// <returns>The gold spent.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuyEuropeGoods"/>.</exception>
    public int BuyEuropeGoods(Unit ship, string goodsId, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckBuyEuropeGoods(ship, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        Gold -= check.Cost;
        ship.AddCargo(goodsId, amount);
        return check.Cost;
    }

    /// <summary>Advances sailing units; arrivals dock in Europe or re-enter the map.</summary>
    private void AdvanceSailing()
    {
        foreach (Unit unit in _units.Where(u =>
            u.Location is UnitLocation.SailingToEurope or UnitLocation.SailingToNewWorld))
        {
            if (--unit.SailTurnsRemaining > 0)
            {
                continue;
            }
            if (unit.Location == UnitLocation.SailingToEurope)
            {
                unit.Location = UnitLocation.InEurope;
            }
            else
            {
                unit.Location = UnitLocation.OnMap; // re-enters at its departure high-seas tile
                Reveal(unit);
            }
        }
    }

    /// <summary>Whether the colony may start constructing a building type.</summary>
    public MoveCheck CheckSetBuild(Colony colony, string buildingId)
    {
        BuildingType building = Ruleset.Building(buildingId);
        if (colony.HasBuilding(buildingId))
        {
            return MoveCheck.No($"The colony already has a {building.ShortName}.");
        }
        if (building.BuildCost.Count == 0)
        {
            return MoveCheck.No($"The {building.ShortName} cannot be constructed.");
        }
        if (building.UpgradesFrom is not null && !colony.HasBuilding(building.UpgradesFrom))
        {
            return MoveCheck.No($"A {building.ShortName} upgrades an existing building the colony lacks.");
        }
        if (colony.Population < building.RequiredPopulation)
        {
            return MoveCheck.No(
                $"The {building.ShortName} needs a population of {building.RequiredPopulation}.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>Sets what the colony is constructing (null stops construction).</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSetBuild"/>.</exception>
    public void SetBuild(Colony colony, string? buildingId)
    {
        if (buildingId is not null)
        {
            MoveCheck check = CheckSetBuild(colony, buildingId);
            if (!check.Allowed)
            {
                throw new InvalidMoveException(check.Reason!);
            }
        }
        colony.CurrentBuild = buildingId;
    }

    /// <summary>Building types the colony could start constructing right now.</summary>
    public IEnumerable<BuildingType> Buildables(Colony colony) =>
        Ruleset.BuildingTypes.Where(b => CheckSetBuild(colony, b.Id).Allowed);

    /// <summary>
    /// Completes construction when the stores cover the cost: materials are
    /// consumed and the building appears (replacing the one it upgrades).
    /// </summary>
    private void RunConstruction(Colony colony)
    {
        if (colony.CurrentBuild is null)
        {
            return;
        }
        BuildingType building = Ruleset.Building(colony.CurrentBuild);
        if (building.BuildCost.Any(c => colony.StoreOf(Ruleset.StorageIdOf(c.GoodsId)) < c.Amount))
        {
            return; // keep saving materials
        }

        foreach (GoodsOutput cost in building.BuildCost)
        {
            colony.AddGoods(Ruleset.StorageIdOf(cost.GoodsId), -cost.Amount);
        }
        if (building.UpgradesFrom is not null)
        {
            colony.ReplaceBuilding(building.UpgradesFrom, building.Id);
        }
        else
        {
            colony.AddBuilding(building.Id);
        }
        colony.CurrentBuild = null;
    }

    /// <summary>
    /// Ends the current turn: colonies produce, eat, and grow; units regain
    /// movement; the turn counter advances. (Turn-step order matters and grows
    /// with each economy slice — documented in docs/systems/turns.md.)
    /// </summary>
    public void EndTurn()
    {
        foreach (Colony colony in _colonies)
        {
            RunColonyTurn(colony);
        }
        AccumulateLibertyAndElectFathers();
        AccumulateImmigrationAndEmigrate();
        AdvanceSailing();
        foreach (Unit unit in _units)
        {
            unit.ResetMovement();
        }
        Turn++;
    }

    /// <summary>
    /// Converts each colony's freshly-produced bells into player liberty, elects
    /// the chosen father once enough is banked, and refreshes the offered set.
    /// </summary>
    private void AccumulateLibertyAndElectFathers()
    {
        foreach (Colony colony in _colonies)
        {
            int bells = colony.StoreOf(BellsId);
            if (bells > 0)
            {
                colony.AddGoods(BellsId, -bells); // bells become liberty, not tradeable stock
                _liberty += bells;
            }
        }

        if (_currentFather is not null && _liberty >= TotalFoundingFatherCost())
        {
            _liberty -= TotalFoundingFatherCost();
            _congress.Add(_currentFather);
            _currentFather = null;
            _offeredFathers.Clear();
        }

        if (_currentFather is null && _offeredFathers.Count == 0)
        {
            GenerateOffers();
        }
    }

    /// <summary>
    /// Offers one eligible father per category, picked by seeded weight for the
    /// current age (already-elected fathers and zero-weight ones are excluded).
    /// </summary>
    private void GenerateOffers()
    {
        _offeredFathers.Clear();
        int age = CurrentAge;
        var elected = _congress.ToHashSet();

        foreach (FatherType type in Enum.GetValues<FatherType>())
        {
            var candidates = Ruleset.FoundingFathers
                .Where(f => f.Type == type && !elected.Contains(f.Id) && f.WeightForAge(age) > 0)
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }
            int totalWeight = candidates.Sum(f => f.WeightForAge(age));
            int roll = _random.Next(totalWeight);
            foreach (FoundingFather f in candidates)
            {
                roll -= f.WeightForAge(age);
                if (roll < 0)
                {
                    _offeredFathers.Add(f.Id);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Accrues immigration and emigrates while the target is met (FreeCol
    /// <c>Player.getTotalImmigrationProduction</c> + the auto-emigrate loop in
    /// <c>ServerPlayer.csNewTurn</c>): colony crosses drain into the pool (like
    /// bells → liberty), the Europe contribution (−4 per person there, +2 player
    /// bonus, never dropping the turn's total below zero) is added, then each time
    /// the pool meets the target an emigrant arrives in Europe from a random dock slot.
    /// </summary>
    private void AccumulateImmigrationAndEmigrate()
    {
        // Colony crosses become immigration and leave the warehouse (not tradeable stock).
        int crossesThisTurn = 0;
        foreach (Colony colony in _colonies)
        {
            int crosses = colony.StoreOf(CrossesId);
            if (crosses > 0)
            {
                colony.AddGoods(CrossesId, -crosses);
                crossesThisTurn += crosses;
            }
        }

        // Europe contribution: penalty per person docked, plus the flat player bonus,
        // clamped so this turn's immigration production cannot be negative.
        int personsInEurope = _units.Count(u => u.Location == UnitLocation.InEurope && u.Type.IsPerson);
        int europe = (personsInEurope * EuropeUnitImmigrationPenalty) + PlayerImmigrationBonus;
        if (europe + crossesThisTurn < 0)
        {
            europe = -crossesThisTurn;
        }
        _immigration += crossesThisTurn + europe;

        // Auto-emigrate (no William Brewster / select-recruit yet → a random dock slot).
        // Guarded on a stocked dock: test rulesets with no recruitable units have none.
        while (_recruitDock.Count > 0 && _immigration >= _immigrationRequired)
        {
            Emigrate(_random.Next(_recruitDock.Count));
            ReduceImmigration();
            _immigrationRequired += CrossesIncrement;
        }
    }

    /// <summary>
    /// Consumes immigration on emigration (FreeCol <c>Player.reduceImmigration</c> with
    /// classic <c>saveProductionOverflow=true</c>): subtract the target, keeping any surplus.
    /// </summary>
    private void ReduceImmigration() =>
        _immigration = _immigrationRequired > _immigration ? 0 : _immigration - _immigrationRequired;

    /// <summary>
    /// Fills the dock to <see cref="RecruitSlots"/> with fresh weighted draws. A no-op
    /// when the ruleset defines no recruitable units (minimal test rulesets), so those
    /// games simply have no Europe dock.
    /// </summary>
    private void InitRecruitDock()
    {
        if (!Ruleset.UnitTypes.Any(u => u.RecruitProbability > 0))
        {
            return;
        }
        while (_recruitDock.Count < RecruitSlots)
        {
            _recruitDock.Add(DrawRecruitType());
        }
    }

    /// <summary>
    /// A weighted-random recruitable unit type id (FreeCol <c>ServerEurope.generateRecruitablesList</c>):
    /// each type's <see cref="UnitType.RecruitProbability"/> is its weight.
    /// </summary>
    private string DrawRecruitType()
    {
        var pool = Ruleset.UnitTypes.Where(u => u.RecruitProbability > 0).ToList();
        int total = pool.Sum(u => u.RecruitProbability);
        int roll = _random.Next(total);
        foreach (UnitType type in pool)
        {
            roll -= type.RecruitProbability;
            if (roll < 0)
            {
                return type.Id;
            }
        }
        return pool[^1].Id; // unreachable: roll < total guarantees an earlier return
    }

    /// <summary>
    /// Takes the recruit in <paramref name="slot"/> off the dock, lands it in Europe,
    /// and refills the dock with a fresh draw (the new recruit joins at the bottom slot).
    /// </summary>
    private Unit Emigrate(int slot)
    {
        string typeId = _recruitDock[slot];
        _recruitDock.RemoveAt(slot);
        _recruitDock.Add(DrawRecruitType());
        return CreateEuropeRecruit(typeId);
    }

    /// <summary>Creates a recruited unit docked in Europe (it has never been on the map).</summary>
    private Unit CreateEuropeRecruit(string unitTypeId)
    {
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(unitTypeId), new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
        };
        _units.Add(unit);
        return unit;
    }

    /// <summary>Whether the player can buy the recruit in <paramref name="slot"/> right now.</summary>
    public MoveCheck CheckRecruit(int slot)
    {
        if (slot < 0 || slot >= _recruitDock.Count)
        {
            return MoveCheck.No("No recruit in that dock slot.");
        }
        int price = RecruitPrice;
        if (Gold < price)
        {
            return MoveCheck.No($"Not enough gold to recruit (need {price}).");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Buys the recruit in <paramref name="slot"/>: pays <see cref="RecruitPrice"/>, raises the
    /// base price, and — like a free emigrant — consumes immigration and raises the next target
    /// (FreeCol <c>ServerPlayer.csEmigrate</c>, the RECRUIT case falling through to NORMAL).
    /// The recruit lands in Europe and the dock refills.
    /// </summary>
    /// <returns>The recruited unit, docked in Europe.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckRecruit"/>.</exception>
    public Unit Recruit(int slot)
    {
        MoveCheck check = CheckRecruit(slot);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        Gold -= check.Cost;                              // price read before the base rises
        _baseRecruitPrice += RecruitPriceIncrease;       // increaseRecruitmentDifficulty
        _recruitLowerCap += RecruitLowerCapIncrease;
        Unit recruit = Emigrate(slot);                   // extract precedes the immigration cut (as in FreeCol)
        ReduceImmigration();
        _immigrationRequired += CrossesIncrement;
        return recruit;
    }

    /// <summary>
    /// The yield of one goods type when a colonist works a tile: the terrain's
    /// best attended output for that goods (0 when it can't be produced there).
    /// </summary>
    public int TileYield(Position tile, string goodsId) =>
        Map.TerrainAt(tile).Productions
            .Where(p => !p.Unattended)
            .SelectMany(p => p.Outputs)
            .Where(o => o.GoodsId == goodsId)
            .Select(o => o.Amount)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Whether a colonist of <paramref name="colony"/> may be put to work on
    /// <paramref name="tile"/> producing <paramref name="goodsId"/>.
    /// </summary>
    public MoveCheck CheckAssignWork(Colony colony, Position tile, string goodsId)
    {
        if (!Map.InBounds(tile))
        {
            return MoveCheck.No("Tile is off the map.");
        }
        if (!tile.IsAdjacentTo(colony.Position))
        {
            return MoveCheck.No("Colonists work the eight tiles around the colony.");
        }
        if (colony.TileWorkers.ContainsKey(tile))
        {
            return MoveCheck.No("That tile is already worked.");
        }
        if (colony.IdleColonists <= 0)
        {
            return MoveCheck.No("No idle colonists.");
        }
        int yield = TileYield(tile, goodsId);
        if (yield <= 0)
        {
            return MoveCheck.No($"That tile cannot produce {goodsId[(goodsId.LastIndexOf('.') + 1)..]}.");
        }
        return MoveCheck.Yes(yield);
    }

    /// <summary>Puts an idle colonist to work on a tile producing one goods type.</summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckAssignWork"/>.</exception>
    public void AssignWork(Colony colony, Position tile, string goodsId)
    {
        MoveCheck check = CheckAssignWork(colony, tile, goodsId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        colony.SetWorker(tile, goodsId);
    }

    /// <summary>Returns a tile's worker to the idle pool.</summary>
    public void UnassignWork(Colony colony, Position tile) => colony.RemoveWorker(tile);

    /// <summary>
    /// Auto-assigns idle colonists to the best unworked food tiles (highest grain
    /// yield, deterministic tie-break). Runs on founding and growth; also available
    /// to the player ("send idle colonists to the fields").
    /// </summary>
    public void AutoAssignIdleToFood(Colony colony)
    {
        const string grain = "model.goods.grain";
        while (colony.IdleColonists > 0)
        {
            var best = colony.Position.Neighbours()
                .Where(n => Map.InBounds(n) && !colony.TileWorkers.ContainsKey(n))
                .Select(n => (tile: n, yield: TileYield(n, grain)))
                .Where(t => t.yield > 0)
                .OrderByDescending(t => t.yield)
                .ThenBy(t => t.tile.Y)
                .ThenBy(t => t.tile.X)
                .Cast<(Position tile, int yield)?>()
                .FirstOrDefault();
            if (best is null)
            {
                return; // nowhere productive left — colonist stays idle
            }
            colony.SetWorker(best.Value.tile, grain);
        }
    }

    /// <summary>
    /// One building's turn: unattended output plus per-worker conversion of
    /// warehouse inputs to outputs (scaled down when inputs run short).
    /// </summary>
    private void RunBuildingProduction(Colony colony, BuildingType building)
    {
        int workers = colony.BuildingWorkers.GetValueOrDefault(building.Id);
        foreach (ProductionEntry entry in building.Productions)
        {
            int multiplier = entry.Unattended ? 1 : workers;
            if (multiplier == 0)
            {
                continue;
            }

            // Breeding gate (FreeCol autoProduction): goods with a breeding
            // number (horses) only multiply when enough are already stabled.
            bool breedingBlocked = entry.Outputs.Any(o =>
                Ruleset.GoodsTypes.FirstOrDefault(g => g.Id == o.GoodsId)?.BreedingNumber
                    is int needed && colony.StoreOf(Ruleset.StorageIdOf(o.GoodsId)) < needed);
            if (breedingBlocked)
            {
                continue;
            }

            // Scale by the scarcest input (classic conversions are 1:1, but the
            // ratio is honoured generically).
            double fraction = 1.0;
            foreach (GoodsOutput input in entry.Inputs)
            {
                int wanted = input.Amount * multiplier;
                int available = colony.StoreOf(Ruleset.StorageIdOf(input.GoodsId));
                fraction = Math.Min(fraction, wanted == 0 ? 1.0 : Math.Min(1.0, available / (double)wanted));
            }

            foreach (GoodsOutput input in entry.Inputs)
            {
                colony.AddGoods(
                    Ruleset.StorageIdOf(input.GoodsId),
                    -(int)Math.Floor(input.Amount * multiplier * fraction));
            }
            foreach (GoodsOutput output in entry.Outputs)
            {
                colony.AddGoods(
                    Ruleset.StorageIdOf(output.GoodsId),
                    (int)Math.Floor(output.Amount * multiplier * fraction));
            }
        }
    }

    /// <summary>
    /// Drops assignments until they fit the population: building workers are
    /// pulled before field workers (deterministic order — last building, then
    /// last tile in row-major order).
    /// </summary>
    private static void TrimAssignments(Colony colony)
    {
        while (colony.IdleColonists < 0)
        {
            if (colony.BuildingWorkers.Count > 0)
            {
                string building = colony.BuildingWorkers.Keys.Last();
                colony.SetBuildingWorkers(building, colony.BuildingWorkers[building] - 1);
            }
            else if (colony.TileWorkers.Count > 0)
            {
                Position tile = colony.TileWorkers.Keys
                    .OrderBy(p => p.Y).ThenBy(p => p.X)
                    .Last();
                colony.RemoveWorker(tile);
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>One colony's production-eat-grow step.</summary>
    private void RunColonyTurn(Colony colony)
    {
        // 1a. The colony square works itself (unattended yield). Goods enter
        //     the warehouse under their stored-as id: grain/fish → food.
        TerrainType terrain = Map.TerrainAt(colony.Position);
        foreach (ProductionEntry entry in terrain.Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput output in entry.Outputs)
            {
                colony.AddGoods(Ruleset.StorageIdOf(output.GoodsId), output.Amount);
            }
        }

        // 1b. Worked tiles produce their assigned goods.
        foreach ((Position tile, string goodsId) in colony.TileWorkers)
        {
            colony.AddGoods(Ruleset.StorageIdOf(goodsId), TileYield(tile, goodsId));
        }

        // 1c. Buildings produce: unattended entries always run (town hall bell);
        //     worker entries convert inputs to outputs per colonist, limited by
        //     what the warehouse holds.
        foreach (string buildingId in colony.Buildings)
        {
            RunBuildingProduction(colony, Ruleset.Building(buildingId));
        }

        // 1d. Construction completes when materials are saved up.
        RunConstruction(colony);

        // 2. Colonists eat; an unfed colonist starves (population floors at 1 —
        //    colony destruction is a later rule). Assignments shrink to match.
        int shortfall = colony.ConsumeFood(colony.Population * Colony.FoodPerColonist);
        if (shortfall > 0 && colony.Population > 1)
        {
            colony.Population--;
            TrimAssignments(colony);
        }

        // 3. Growth: a food surplus of 200 raises a new colonist, who reports
        //    to the best free food tile.
        if (colony.Food >= Colony.FoodForGrowth)
        {
            colony.ConsumeFood(Colony.FoodForGrowth);
            colony.Population++;
            AutoAssignIdleToFood(colony);
        }
    }

    /// <summary>Reveals all tiles within the unit's line of sight.</summary>
    private void Reveal(Unit unit)
    {
        int r = unit.Type.LineOfSight;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new Position(unit.Position.X + dx, unit.Position.Y + dy);
                if (Map.InBounds(p))
                {
                    _explored.Add(p);
                }
            }
        }
    }
}

/// <summary>Result of a move legality check.</summary>
/// <param name="Allowed">Whether the move may be made.</param>
/// <param name="Cost">Movement points the move would cost (when allowed).</param>
/// <param name="Reason">Why the move is rejected (when not allowed).</param>
public readonly record struct MoveCheck(bool Allowed, int Cost, string? Reason)
{
    /// <summary>An allowed move with the given cost.</summary>
    public static MoveCheck Yes(int cost) => new(true, cost, null);

    /// <summary>A rejected move with the reason shown to the player.</summary>
    public static MoveCheck No(string reason) => new(false, 0, reason);
}

/// <summary>Thrown when an illegal move is attempted directly (UI should use CheckMove first).</summary>
public sealed class InvalidMoveException : Exception
{
    /// <summary>Creates the exception with the player-facing reason.</summary>
    public InvalidMoveException(string message) : base(message) { }
}
