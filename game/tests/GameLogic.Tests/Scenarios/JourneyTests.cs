using System.Collections.Generic;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Scenarios;

/// <summary>
/// End-to-end journeys (docs/TEST-PLAN.md): each test drives one complete player
/// journey through the real Game API and asserts at every milestone that the step's
/// output feeds the next — the gap that isolated slice tests and broad-invariant
/// soak runs leave open. Seed-pinned and deterministic; map-dependent steps are
/// guarded, not assumed.
/// </summary>
[Trait("Category", "E2E")]
public class JourneyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Food = "model.goods.food";
    private const string Grain = "model.goods.grain";
    private const string Cotton = "model.goods.cotton";
    private const string Lumber = "model.goods.lumber";
    private const string Hammers = "model.goods.hammers";
    private const string Bells = "model.goods.bells";
    private const string Sugar = "model.goods.sugar";
    private const ulong FoundSeed = 424242; // proven to start on settleable land

    // ───────────────────────── Journey 1: Explore & found ─────────────────────────

    [Fact]
    public void Journey1_ExploreAndFoundAColony()
    {
        // M1 — new game.
        var game = Game.New(Classic, seed: FoundSeed);
        Assert.Equal(1, game.Turn);
        Unit unit = Assert.Single(game.PlayerUnits); // native braves also exist
        Assert.True(game.IsExplored(unit.Position), "start tile must be explored");
        Assert.False(game.Map.TerrainAt(unit.Position).IsWater);
        Assert.True(game.Map.TerrainAt(unit.Position).CanSettle);
        Assert.True(game.Explored.Count >= 4);
        Assert.All(game.Explored, p => Assert.True(game.Map.InBounds(p)));

        // M2 — explore (guarded: a start tile can legally be boxed in).
        int exploredBefore = game.Explored.Count;
        Position startPos = unit.Position;
        Position? step = unit.Position.Neighbours()
            .Where(n => game.CheckMove(unit, n).Allowed)
            .Cast<Position?>()
            .FirstOrDefault();
        if (step is not null)
        {
            game.MoveUnit(unit, step.Value);
            Assert.Equal(step.Value, unit.Position);
            Assert.True(game.Explored.Count > exploredBefore, "moving must reveal new tiles");
            Assert.True(game.IsExplored(startPos) && game.IsExplored(unit.Position),
                "no fog-of-war violation — every occupied tile stays revealed");
        }

        // M3 — found is legal.
        Assert.True(game.CheckFoundColony(unit).Allowed);
        Position foundedAt = unit.Position;

        // M4 — found consumes the founder and creates the colony.
        Colony colony = game.FoundColony(unit);
        Assert.Empty(game.PlayerUnits); // braves remain on the map
        Colony onlyColony = Assert.Single(game.Colonies);
        Assert.Same(colony, onlyColony);
        Assert.Equal(foundedAt, colony.Position);
        Assert.Equal(1, colony.Population);
        Assert.False(string.IsNullOrEmpty(colony.Name));
        Assert.Null(colony.CurrentBuild);

        // M5 — free base buildings present (derived from the ruleset, not hard-coded).
        var freeBase = Classic.BuildingTypes
            .Where(b => b.BuildCost.Count == 0 && b.UpgradesFrom is null)
            .Select(b => b.Id)
            .ToList();
        Assert.NotEmpty(freeBase);
        Assert.All(freeBase, id =>
            Assert.True(colony.HasBuilding(id), $"new colony must have free base building {id}"));

        // M6 — auto-assignment (branched: a pop-1 colony may have no grain neighbour).
        Assert.True(colony.TileWorkers.Count <= 1);
        if (colony.TileWorkers.Count == 1)
        {
            (Position tile, string goods) = colony.TileWorkers.First();
            Assert.Equal(Grain, goods);
            Assert.True(tile.IsAdjacentTo(colony.Position));
            Assert.True(game.TileYield(tile, Grain) > 0);
            Assert.Equal(0, colony.IdleColonists);
        }

        // M7 — economy tick converts bells to liberty.
        game.EndTurn();
        Assert.Equal(2, game.Turn);
        Assert.All(colony.Stores.Values, v => Assert.True(v >= 0, "no negative stores after the first tick"));
        Assert.Equal(1, game.Liberty);              // town hall rang one bell → one liberty
        Assert.Equal(0, colony.StoreOf(Bells));     // bells left the warehouse

        // M8 — save/load acid test.
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(json, SaveGame.From(reloaded).ToJson());
        Colony r = Assert.Single(reloaded.Colonies, c => c.OwnerId == 0); // the human's colony (foreign powers found their own)
        Assert.Equal(colony.Name, r.Name);
        Assert.Equal(colony.Position, r.Position);
        Assert.Equal(colony.Population, r.Population);
        Assert.Equal(game.Liberty, reloaded.Liberty);
    }

    [Fact]
    public void Journey1_IsDeterministic()
    {
        static string PlayAndSave()
        {
            var game = Game.New(Classic, seed: FoundSeed);
            game.FoundColony(game.Units[0]);
            game.EndTurn();
            return SaveGame.From(game).ToJson();
        }
        Assert.Equal(PlayAndSave(), PlayAndSave());
    }

    // ───────────────────────── Journey 2: Colony economy ─────────────────────────

    [Fact]
    public void Journey2_RawToRefineToConstructToGrow()
    {
        // Deterministic fixture: a 3×3 all-plains map, pop-2 colony at the centre,
        // free base buildings re-derived, no auto-assigned workers.
        Game game = AllPlainsColony(population: 2);
        Colony colony = game.Colonies[0];
        Assert.True(colony.HasBuilding("model.building.townHall"));
        Assert.True(colony.HasBuilding("model.building.carpenterHouse"));

        // M1 — assign a farmer + a carpenter; raw tile yield AND building refinement
        //      both reach the warehouse in one connected tick.
        game.AssignWork(colony, new Position(0, 1), Grain);          // plains farm, yield 5
        game.AssignBuildingWork(colony, "model.building.carpenterHouse");
        colony.AddGoods(Lumber, 10);

        game.EndTurn();
        Assert.Equal(3 + 5 - 4, colony.Food);       // centre 3 + farm 5 − two colonists eat 4
        Assert.Equal(3, colony.StoreOf(Hammers));   // carpenter: 3 lumber → 3 hammers
        Assert.Equal(7, colony.StoreOf(Lumber));    // 10 − 3 consumed
        Assert.Equal(2, colony.StoreOf(Cotton));    // centre square's cotton, untouched

        // M2 — queue a buildable, fund it, and watch construction complete.
        game.UnassignBuildingWork(colony, "model.building.carpenterHouse"); // quiet the hammer noise
        BuildingType target = Assert.Single(game.Buildables(colony).Take(1));
        game.SetBuild(colony, target.Id);
        foreach (GoodsOutput cost in target.BuildCost)
        {
            colony.AddGoods(cost.GoodsId, cost.Amount);
        }
        Assert.False(colony.HasBuilding(target.Id));
        game.EndTurn();
        Assert.True(colony.HasBuilding(target.Id), $"{target.ShortName} should be built");
        Assert.Null(colony.CurrentBuild);

        // M3 — a food surplus raises a new colonist.
        int popBefore = colony.Population;
        colony.AddGoods(Food, 200);
        game.EndTurn();
        Assert.Equal(popBefore + 1, colony.Population);

        // M4 — round-trip.
        string json = SaveGame.From(game).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    // ───────────────────────── Journey 3: Trade to treasury ─────────────────────────

    [Fact]
    public void Journey3_ProduceSellPriceTaxTreasury()
    {
        // Sugar seeded into the store stands in for production (Journey 2 proves that).
        var game = Game.New(Classic, seed: 7, startingGold: 0, startingTax: 0);
        Colony colony = game.FoundColony(game.Units[0]);
        colony.AddGoods(Sugar, 50);

        int initialAmount = game.Market.AmountInMarket(Sugar);
        int initialBid = game.Market.BidPrice(Sugar);

        // M1 — sell: the store empties and the treasury gains exactly the credit.
        int credited = game.SellColonyGoods(colony, Sugar, 50);
        Assert.Equal(0, colony.StoreOf(Sugar));
        Assert.Equal(credited, game.Gold);
        Assert.Equal(50 * initialBid, credited); // 50 < one chunk, no tax → full revenue

        // M2 — the price moved: more sugar in Europe, bid no higher than before.
        Assert.True(game.Market.AmountInMarket(Sugar) > initialAmount);
        Assert.True(game.Market.BidPrice(Sugar) <= initialBid);

        // M3 — tax is applied (a fresh game with 50% tax).
        var taxed = Game.New(Classic, seed: 7, startingGold: 0, startingTax: 50);
        Colony taxedColony = taxed.FoundColony(taxed.Units[0]);
        taxedColony.AddGoods(Sugar, 50);
        int taxedCredit = taxed.SellColonyGoods(taxedColony, Sugar, 50);
        Assert.Equal(50 * initialBid / 2, taxedCredit); // (100−50)% truncated
        Assert.Equal(taxedCredit, taxed.Gold);

        // M4 — round-trip preserves treasury + moved market.
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(game.Gold, reloaded.Gold);
        Assert.Equal(game.Market.AmountInMarket(Sugar), reloaded.Market.AmountInMarket(Sugar));
    }

    // ───────────────── Journey 3b: Full trade voyage (sailing leg) ─────────────────

    [Fact]
    public void Journey3b_LoadSailSellReturn()
    {
        // Coast → ocean → high-seas strip with a port colony holding sugar.
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.ocean", "model.tile.ocean", "model.tile.highSeas"],
            Units = [new SavedUnit(1, "model.unit.caravel", 1, 0, 12)],
            Explored = [],
            Colonies = [new SavedColony(1, "Port", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 })],
        };
        Game game = save.Restore(Classic);
        Unit ship = game.Units[0];
        Colony colony = game.Colonies[0];

        // M1 — load cargo at the colony.
        game.LoadFromColony(ship, colony, Sugar, 100);
        Assert.Equal(100, ship.CargoOf(Sugar));
        Assert.Equal(0, colony.StoreOf(Sugar));

        // M2 — sail to the edge and set out for Europe.
        game.MoveUnit(ship, new Position(2, 0));
        game.MoveUnit(ship, new Position(3, 0));
        game.MoveUnit(ship, new Position(4, 0));
        game.SailToEurope(ship);
        Assert.Equal(UnitLocation.SailingToEurope, ship.Location);

        // M3 — a mid-voyage save survives byte-identical.
        string midJson = SaveGame.From(game).ToJson();
        Assert.Equal(midJson, SaveGame.From(SaveGame.FromJson(midJson).Restore(Classic)).ToJson());

        // M4 — arrive in Europe after exactly the sail time.
        for (int i = 0; i < Game.SailTurns; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(UnitLocation.InEurope, ship.Location);

        // M5 — sell the cargo; treasury gains, hold empties.
        int credited = game.SellShipCargo(ship, Sugar, 100);
        Assert.Equal(200, credited);            // 100 × bid 2, no tax
        Assert.Equal(200, game.Gold);
        Assert.Equal(0, ship.CargoOf(Sugar));

        // M6 — sail home and re-enter at the departure tile.
        game.SailToNewWorld(ship);
        for (int i = 0; i < Game.SailTurns; i++)
        {
            game.EndTurn();
        }
        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(4, 0), ship.Position);
    }

    // ───────────────────────── Journey 4: Liberty & elections ─────────────────────────

    [Fact]
    public void Journey4_TwoSequentialFatherElectionsWithCostEscalation()
    {
        var game = Game.New(Classic, seed: 42);
        Colony colony = game.FoundColony(game.Units[0]);

        // A lone food-farming colony grows and stalls liberty (bell upkeep outpaces its single unattended bell);
        // staff the town hall instead — the colony square feeds the one colonist, and statesmen make the bells that
        // elect fathers (faithful: a colony must produce liberty to keep accruing it).
        foreach (Position field in new List<Position>(colony.TileWorkers.Keys))
        {
            game.UnassignWork(colony, field);
        }
        game.AssignBuildingWork(colony, "model.building.townHall");

        // M1 — first election: choose a father, accrue bells from the staffed town hall, elect at cost 40.
        string father1 = game.OfferedFathers[0];
        game.ChooseFather(father1);
        for (int t = 0; t < 60 && game.Congress.Count == 0; t++)
        {
            game.EndTurn();
        }
        Assert.Equal(new[] { father1 }, game.Congress.ToArray());
        Assert.True(game.Liberty < 40, "liberty resets after election");
        Assert.DoesNotContain(father1, game.OfferedFathers);

        // M2 — cost escalation asserted directly (count 1 → 2·2·40+1 = 161).
        Assert.Equal(161, game.TotalFoundingFatherCost());

        // M3 — second election: the next father costs more and both are retained.
        string father2 = game.OfferedFathers[0];
        game.ChooseFather(father2);
        for (int t = 0; t < 200 && game.Congress.Count == 1; t++)
        {
            game.EndTurn();
        }
        Assert.Equal(2, game.Congress.Count);
        Assert.Contains(father1, game.Congress);
        Assert.Contains(father2, game.Congress);
        Assert.DoesNotContain(father2, game.OfferedFathers);

        // M4 — round-trip AFTER an election (no other test does this).
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(json, SaveGame.From(reloaded).ToJson());
        Assert.Equal(game.Congress, reloaded.Congress);
    }

    // ──────────────────── Journey 6: Immigration & recruitment ────────────────────

    [Fact]
    public void Journey6_AccrueImmigrationEmigrateThenRecruit()
    {
        // M1 — new game with a treasury; found a colony. The dock is stocked and the
        //      immigration clock starts at the classic target with the full base price.
        var game = Game.New(Classic, seed: 42, startingGold: 1000);
        game.FoundColony(game.Units[0]);
        Assert.Equal(Game.RecruitSlots, game.RecruitDock.Count);
        Assert.All(game.RecruitDock, id => Assert.True(Classic.Unit(id).RecruitProbability > 0));
        Assert.Equal(0, game.Immigration);
        Assert.Equal(15, game.ImmigrationRequired);
        Assert.Equal(200, game.RecruitPrice);

        // M2 — accrual feeds Europe: 3 immigration/turn (1 chapel cross + 2 player bonus);
        //      no emigrant before the target, exactly one the turn it is met.
        for (int t = 0; t < 4; t++)
        {
            game.EndTurn();
        }
        Assert.Empty(game.UnitsInEurope);           // 12 < 15: nobody has emigrated yet
        Assert.Equal(12, game.Immigration);

        game.EndTurn();                             // 15 ≥ 15 → emigrate
        Unit emigrant = Assert.Single(game.UnitsInEurope);
        Assert.True(emigrant.Type.IsPerson);
        Assert.Equal(UnitLocation.InEurope, emigrant.Location);
        Assert.Equal(17, game.ImmigrationRequired); // the target escalated
        Assert.Equal(0, game.Immigration);

        // M3 — the emigrant idling in Europe suppresses further immigration (−4 clamp):
        //      a single chapel's cross cannot push the clock forward.
        game.EndTurn();
        Assert.Equal(0, game.Immigration);
        Assert.Single(game.UnitsInEurope);          // still just the one

        // M4 — paid recruit: gold buys the chosen slot's unit into Europe, the dock
        //      refills, and the base price escalates for next time.
        int goldBefore = game.Gold;
        int price = game.RecruitPrice;              // 200 (immigration 0, required 17)
        string slotType = game.RecruitDock[1];
        Unit recruit = game.Recruit(1);
        Assert.Equal(slotType, recruit.Type.Id);
        Assert.Equal(goldBefore - price, game.Gold);
        Assert.Equal(2, game.UnitsInEurope.Count()); // emigrant + recruit
        Assert.Equal(Game.RecruitSlots, game.RecruitDock.Count);
        Assert.Equal(price + game.Ruleset.Difficulty.RecruitPriceIncrease, game.RecruitPrice); // 200 → 230 (medium)

        // M5 — acid test: the whole immigration + dock + Europe state round-trips identically.
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(json, SaveGame.From(reloaded).ToJson());
        Assert.Equal(game.Immigration, reloaded.Immigration);
        Assert.Equal(game.ImmigrationRequired, reloaded.ImmigrationRequired);
        Assert.Equal(game.RecruitDock, reloaded.RecruitDock);
        Assert.Equal(game.RecruitPrice, reloaded.RecruitPrice);
        Assert.Equal(game.UnitsInEurope.Count(), reloaded.UnitsInEurope.Count());
    }

    // ───────────────── Journey 7: A recruit reaches the New World ─────────────────

    [Fact]
    public void Journey7_BoardSailDisembarkAndFound()
    {
        // A caravel and a recruit, both in Europe; a coast to come home to.
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, "model.unit.caravel", 1, 0, 12, (int)UnitLocation.InEurope),
                new SavedUnit(2, "model.unit.freeColonist", 0, 0, 3, (int)UnitLocation.InEurope),
            ],
            Explored = [],
        };
        Game game = save.Restore(Classic);
        Unit ship = game.Units.First(u => u.Id == 1);
        Unit recruit = game.Units.First(u => u.Id == 2);

        // M1 — the recruit boards the ship on the Europe dock.
        game.Board(recruit, ship);
        Assert.Equal(ship.Id, recruit.CarrierId);
        Assert.Equal(1, game.CargoSlotsUsed(ship));

        // M2 — sail home: after the crossing the ship is on the map with the recruit still aboard.
        game.SailToNewWorld(ship);
        for (int i = 0; i < Game.SailTurns; i++)
        {
            game.EndTurn();
        }
        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(1, 0), ship.Position);
        Assert.True(recruit.IsAboard);
        Assert.Equal(ship.Position, recruit.Position); // the passenger tracked the carrier

        // M3 — acid test mid-state: a passenger-aboard game round-trips byte-identical.
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(json, SaveGame.From(reloaded).ToJson());
        Assert.Equal(ship.Id, reloaded.Units.First(u => u.Id == 2).CarrierId);

        // M4 — disembark onto the adjacent coast; the recruit is a free unit on the map.
        game.Disembark(recruit, new Position(0, 0));
        Assert.True(recruit.IsOnMap);
        Assert.Equal(new Position(0, 0), recruit.Position);
        Assert.Empty(game.Passengers(ship));

        // M5 — the colonist founds a colony: the immigration → New World loop is closed.
        Colony colony = game.FoundColony(recruit);
        Assert.Equal(new Position(0, 0), colony.Position);
        Assert.Single(game.Colonies);
        Assert.DoesNotContain(recruit, game.Units); // the founder settled down
    }

    // ─────────────── Journey 8: An elected father grants its bonus ───────────────

    [Fact]
    public void Journey8_ElectJefferson_ThenBellsBecomeMoreLiberty()
    {
        // A pop-1 colony with the colonist in the town hall makes 4 bells/turn.
        // Jefferson is chosen and the treasury of liberty is one short of his cost.
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 9)],
            Units = [],
            Explored = [],
            Colonies =
            [
                new SavedColony(1, "Capital", 1, 1, 1,
                    BuildingWorkers: new Dictionary<string, int> { ["model.building.townHall"] = 1 }),
            ],
            Liberty = 39,
            CurrentFather = "model.foundingFather.thomasJefferson",
        };
        Game game = save.Restore(Classic);
        Assert.Empty(game.Congress);

        // M1 — the election turn: 4 bells (unmodified, he's not in yet) tips liberty over
        //      the 40 cost and Jefferson is elected; liberty resets with the surplus.
        game.EndTurn();
        Assert.Contains("model.foundingFather.thomasJefferson", game.Congress);
        Assert.Equal(3, game.Liberty); // 39 + 4 − 40

        // M2 — now elected, the same 4 bells become 6 liberty (+50%): the bonus took effect.
        game.EndTurn();
        Assert.Equal(9, game.Liberty); // 3 + 6

        // M3 — the effect persists across save/load (it rides on the elected congress).
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(json, SaveGame.From(reloaded).ToJson());
        reloaded.EndTurn();
        Assert.Equal(15, reloaded.Liberty); // 9 + 6, still boosted after reload
    }

    // ──────────── Journey 9: Immigration grows an existing colony ────────────

    [Fact]
    public void Journey9_ShipARecruitHome_ThenJoinTheColony()
    {
        // A coastal colony at (0,0); a caravel in Europe already carrying a recruit.
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, "model.unit.caravel", 1, 0, 12, (int)UnitLocation.InEurope),
                new SavedUnit(2, "model.unit.freeColonist", 1, 0, 3, (int)UnitLocation.InEurope,
                    CarrierId: 1),
            ],
            Explored = [],
            Colonies = [new SavedColony(1, "Port", 0, 0, 1)],
        };
        Game game = save.Restore(Classic);
        Unit ship = game.Units.First(u => u.Id == 1);
        Unit recruit = game.Units.First(u => u.Id == 2);
        Colony colony = game.Colonies[0];
        Assert.Equal(1, colony.Population);
        Assert.Equal(recruit.Id, Assert.Single(game.Passengers(ship)).Id);

        // M1 — sail home; the recruit arrives aboard at the ship's coastal tile.
        game.SailToNewWorld(ship);
        for (int i = 0; i < Game.SailTurns; i++)
        {
            game.EndTurn();
        }
        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(1, 0), ship.Position);

        // M2 — disembark onto the colony's tile.
        game.Disembark(recruit, colony.Position);
        Assert.True(recruit.IsOnMap);
        Assert.Equal(colony.Position, recruit.Position);

        // M3 — join the colony: population grows, the unit is consumed and put to work.
        game.JoinColony(recruit, colony);
        Assert.Equal(2, colony.Population);
        Assert.DoesNotContain(recruit, game.Units);

        // M4 — acid round-trip: the grown colony + empty docks survive identically.
        string json = SaveGame.From(game).ToJson();
        Game reloaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Equal(json, SaveGame.From(reloaded).ToJson());
        Assert.Equal(2, reloaded.Colonies[0].Population);
    }

    // ───────────────────────── fixtures ─────────────────────────

    /// <summary>A pop-N colony at the centre of a 3×3 all-plains map (free base buildings re-derived).</summary>
    private static Game AllPlainsColony(int population)
    {
        string[] terrain = Enumerable.Repeat("model.tile.plains", 9).ToArray();
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [new SavedColony(1, "Econtown", 1, 1, population)],
        };
        return save.Restore(Classic);
    }
}
