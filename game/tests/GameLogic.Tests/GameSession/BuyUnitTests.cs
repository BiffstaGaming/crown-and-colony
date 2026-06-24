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

    /// <summary>
    /// Regression (86d3f6…, "Ships arriving from Europe spawn far from colonies"): a ship bought in Europe must be
    /// given the high-seas entry tile nearest the player's colony, not the map's top-left default, so that when it
    /// sails to the New World it arrives <em>beside the colony</em> rather than at the far corner of the map.
    /// </summary>
    [Fact]
    public void BoughtShip_EntersBesideTheColony_NotAtTheTopLeftDefault()
    {
        // A 4×2 map. The default Europe entry tile (first high-seas in row-major order) is (0,0), far from a
        // colony placed at (2,1); the high-seas tile nearest that colony is (3,1).
        //   y=0:  highSeas(0,0)  ocean(1,0)  ocean(2,0)  ocean(3,0)
        //   y=1:  ocean(0,1)     ocean(1,1)  plains(2,1) highSeas(3,1)
        var save = new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 4, MapHeight = 2,
            Terrain =
            [
                "model.tile.highSeas", "model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
                "model.tile.ocean", "model.tile.ocean", "model.tile.plains", "model.tile.highSeas",
            ],
            Units = [], Explored = [], Gold = 2000,
            Colonies = [new SavedColony(1, "Port", 2, 1, 1)],
        }.Restore(Classic);

        Unit ship = save.BuyUnit(Caravel);

        // The bought ship is parked at the high-seas tile beside the colony, NOT the top-left default (0,0).
        Assert.Equal(new Position(3, 1), ship.Position);
        Assert.NotEqual(new Position(0, 0), ship.Position);

        save.SailToNewWorld(ship);
        for (int i = 0; i < Game.SailTurns; i++)
        {
            save.EndTurn();
        }

        // It re-enters at that same nearby tile — adjacent to the colony, not at the far corner.
        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(3, 1), ship.Position);
        Assert.NotEqual(new Position(0, 0), ship.Position);
        Assert.True(ship.Position.IsAdjacentTo(new Position(2, 1)), "the arriving ship should be beside its colony");
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
