using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The settlement-maturity + colony-region activation oracles (Australian Federation, 4a.2): PURE read-only derived
/// computations the later Federation progression loop consumes. These wire NO victory / era logic, add no persisted
/// state, and consume no RNG — they classify a colony from its population and buildings (like <c>Colony.SonsOfLiberty</c>)
/// and test a region's activation from the colonies within it. Criteria per
/// <c>docs/australian_federation_mode_md/06_Colony_Progression_Prerequisites.md</c>.
/// </summary>
public class SettlementMaturityOracleTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0x5A11;

    private const string Docks = "model.building.docks";
    private const string PrintingPress = "model.building.printingPress";
    private const string Newspaper = "model.building.newspaper";
    private const string Schoolhouse = "model.building.schoolhouse";
    private const string Warehouse = "model.building.warehouse";

    private static Colony Found(Game game) =>
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));

    // ===== SettlementMaturityOf =====

    [Fact]
    public void Population1_IsOutpost()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game); // founded at population 1
        Assert.Equal(1, colony.Population);
        Assert.Equal(SettlementMaturity.Outpost, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Population2_IsStillOutpost()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 2;
        Assert.Equal(SettlementMaturity.Outpost, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Population3_IsTownship()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 3;
        Assert.Equal(SettlementMaturity.Township, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Population6_WithPortAndSpecialist_IsColonialTown()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 6;
        colony.AddBuilding(Docks);      // the market/port link
        colony.AddBuilding(Warehouse);  // a built specialist (has a build cost → not a base building)
        Assert.Equal(SettlementMaturity.ColonialTown, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Population6_WithoutAPort_StaysTownship()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 6;
        colony.AddBuilding(Warehouse); // specialist but no port → cannot reach Colonial Town
        Assert.Equal(SettlementMaturity.Township, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Population10_WithCapitalBuildings_IsColonialCapital()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 10;
        colony.AddBuilding(Docks);
        colony.AddBuilding(PrintingPress); // civic press
        colony.AddBuilding(Schoolhouse);   // the school gate
        Assert.Equal(SettlementMaturity.ColonialCapital, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Population10_MissingASchool_IsOnlyColonialTown()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 10;
        colony.AddBuilding(Docks);
        colony.AddBuilding(Newspaper); // civic press present…
        // …but no schoolhouse → not a capital; it is still a Colonial Town (port + a specialist newspaper).
        Assert.Equal(SettlementMaturity.ColonialTown, game.SettlementMaturityOf(colony));
    }

    [Fact]
    public void Oracle_IsPure_DoesNotAdvanceRngOrState()
    {
        // Calling the oracle repeatedly changes nothing: same result, same RNG state, same population (ADR-009).
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 7;
        colony.AddBuilding(Docks);
        colony.AddBuilding(Warehouse);
        var rngBefore = game.RandomState;

        SettlementMaturity first = game.SettlementMaturityOf(colony);
        SettlementMaturity second = game.SettlementMaturityOf(colony);
        Assert.Equal(first, second);
        Assert.Equal(rngBefore, game.RandomState); // no RNG consumed
        Assert.Equal(7, colony.Population);         // no state change
    }

    // ===== IsColonyRegionActive =====

    [Fact]
    public void Region_WithNoColonialTowns_IsInactive()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game); // a lone population-1 Outpost
        int region = game.Map.RegionIdAt(colony.Position);
        Assert.False(game.IsColonyRegionActive(region));
    }

    [Fact]
    public void Region_WithAColonialCapital_IsActive()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 10;
        colony.AddBuilding(Docks);
        colony.AddBuilding(PrintingPress);
        colony.AddBuilding(Schoolhouse);
        Assert.Equal(SettlementMaturity.ColonialCapital, game.SettlementMaturityOf(colony));

        int region = game.Map.RegionIdAt(colony.Position);
        Assert.True(game.IsColonyRegionActive(region)); // a capital alone activates its region
    }

    [Fact]
    public void Region_WithASingleColonialTown_IsStillInactive()
    {
        // One Colonial Town is not enough — the rule needs a capital OR two towns.
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Population = 6;
        colony.AddBuilding(Docks);
        colony.AddBuilding(Warehouse);
        Assert.Equal(SettlementMaturity.ColonialTown, game.SettlementMaturityOf(colony));

        int region = game.Map.RegionIdAt(colony.Position);
        Assert.False(game.IsColonyRegionActive(region));
    }

    [Fact]
    public void EmptyRegionId_IsInactive()
    {
        // A region with no colonies (or the NoRegion sentinel) is never active.
        Game game = Game.New(Classic, Seed);
        Found(game);
        Assert.False(game.IsColonyRegionActive(GameMap.NoRegion));
        Assert.False(game.IsColonyRegionActive(9999)); // a region id no colony sits in
    }
}
