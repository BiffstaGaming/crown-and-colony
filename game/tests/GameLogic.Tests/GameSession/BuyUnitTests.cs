using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Buying units in Europe (Phase 4 slice 11): pay the spec's <c>price</c>, the unit
/// docks in Europe. Ships enter at the high-seas tile so they can sail home; land
/// units wait on the dock to board one. Prices are pinned to the classic spec.
/// </summary>
public class BuyUnitTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Caravel = "model.unit.caravel";
    private const string Artillery = "model.unit.artillery";
    private const string ManOWar = "model.unit.manOWar";
    private const string FreeColonist = "model.unit.freeColonist";

    private static Game Europe(int gold, params string[] terrain) => new SaveGame
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = terrain.Length, MapHeight = 1, Terrain = terrain,
        Units = [], Explored = [], Gold = gold,
    }.Restore(Classic);

    [Fact]
    public void Ruleset_ParsesUnitPrices_AndPurchasability()
    {
        Assert.Equal(1000, Classic.Unit(Caravel).Price);
        Assert.True(Classic.Unit(Caravel).IsPurchasable);
        Assert.Equal(5000, Classic.Unit("model.unit.frigate").Price);
        Assert.Equal(500, Classic.Unit(Artillery).Price);

        // Man-o-war is mercenary-only (mercenary-price, not price); a free colonist is recruited.
        Assert.Equal(0, Classic.Unit(ManOWar).Price);
        Assert.False(Classic.Unit(ManOWar).IsPurchasable);
        Assert.Equal(0, Classic.Unit(FreeColonist).Price);
        Assert.False(Classic.Unit(FreeColonist).IsPurchasable);
    }

    [Fact]
    public void BuyUnit_DebitsGold_AndDocksInEurope()
    {
        Game game = Europe(2000, "model.tile.ocean", "model.tile.highSeas");

        Unit ship = game.BuyUnit(Caravel);

        Assert.Equal(1000, game.Gold);
        Assert.Equal(Caravel, ship.Type.Id);
        Assert.Equal(UnitLocation.InEurope, ship.Location);
        Assert.Contains(ship, game.UnitsInEurope);
    }

    [Fact]
    public void BoughtShip_EntersAtTheHighSeas_AndCanSailHome()
    {
        Game game = Europe(2000, "model.tile.ocean", "model.tile.highSeas");

        Unit ship = game.BuyUnit(Caravel);
        Assert.Equal(new Position(1, 0), ship.Position); // the map's high-seas entry tile

        game.SailToNewWorld(ship);
        for (int i = 0; i < Game.SailTurns; i++)
        {
            game.EndTurn();
        }
        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(1, 0), ship.Position);
    }

    [Fact]
    public void BoughtLandUnit_WaitsOnTheDock()
    {
        Game game = Europe(2000, "model.tile.highSeas");

        Unit artillery = game.BuyUnit(Artillery);

        Assert.Equal(UnitLocation.InEurope, artillery.Location);
        Assert.False(artillery.Type.IsNaval);
        Assert.False(game.CheckSailToEurope(artillery).Allowed); // can't sail on its own
    }

    [Fact]
    public void BuyUnit_Rejected_ForNonPurchasable_OrTooLittleGold()
    {
        Game poor = Europe(100, "model.tile.highSeas");
        Assert.False(poor.CheckBuyUnit(Caravel).Allowed);          // needs 1000
        Assert.Throws<InvalidMoveException>(() => poor.BuyUnit(Caravel));

        Game rich = Europe(20000, "model.tile.highSeas");
        Assert.False(rich.CheckBuyUnit(ManOWar).Allowed);          // mercenary-only
        Assert.False(rich.CheckBuyUnit(FreeColonist).Allowed);     // recruited, not bought
    }

    [Fact]
    public void BoughtShip_RoundTrips()
    {
        Game game = Europe(2000, "model.tile.ocean", "model.tile.highSeas");
        game.BuyUnit(Caravel);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Single(loaded.UnitsInEurope);
        Assert.Equal(game.Gold, loaded.Gold);
    }
}
