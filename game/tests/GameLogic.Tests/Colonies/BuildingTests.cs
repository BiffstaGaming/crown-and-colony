using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

public class BuildingTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string TownHall = "model.building.townHall";
    private const string Carpenter = "model.building.carpenterHouse";
    private const string Bells = "model.goods.bells";
    private const string Lumber = "model.goods.lumber";
    private const string Hammers = "model.goods.hammers";

    private static Game FoundedColony()
    {
        var game = Game.New(Classic, seed: 424242);
        game.FoundColony(game.Units[0]);
        return game;
    }

    [Fact]
    public void NewColony_StartsWithFreeBaseBuildings()
    {
        Game game = FoundedColony();
        Colony colony = game.Colonies[0];

        Assert.True(colony.HasBuilding(TownHall));
        Assert.True(colony.HasBuilding(Carpenter));
        // No upgrades or costed buildings at founding.
        Assert.DoesNotContain("model.building.lumberMill", colony.Buildings);
        Assert.True(colony.Buildings.Count >= 8, $"only {colony.Buildings.Count} starting buildings");
    }

    [Fact]
    public void TownHall_RingsOneBell_EveryTurn_Unattended()
    {
        Game game = FoundedColony();
        Colony colony = game.Colonies[0];

        game.EndTurn();
        game.EndTurn();

        Assert.Equal(2, colony.StoreOf(Bells));
    }

    [Fact]
    public void CarpenterWorker_ConvertsLumberToHammers_LimitedByStore()
    {
        Game game = FoundedColony();
        Colony colony = game.Colonies[0];
        // Free the founder from the fields and send them to the workshop.
        foreach (var tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile);
        }
        game.AssignBuildingWork(colony, Carpenter);
        colony.AddGoods(Lumber, 5);

        game.EndTurn(); // wants 3 lumber → full conversion

        Assert.Equal(3, colony.StoreOf(Hammers));
        Assert.Equal(2, colony.StoreOf(Lumber));

        game.EndTurn(); // only 2 lumber left → partial conversion

        Assert.Equal(5, colony.StoreOf(Hammers));
        Assert.Equal(0, colony.StoreOf(Lumber));

        game.EndTurn(); // nothing to convert
        Assert.Equal(5, colony.StoreOf(Hammers));
    }

    [Fact]
    public void BuildingAssignment_Validation_AndIdleAccounting()
    {
        Game game = FoundedColony();
        Colony colony = game.Colonies[0];
        foreach (var tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile);
        }
        Assert.Equal(1, colony.IdleColonists);

        Assert.False(game.CheckAssignBuildingWork(colony, "model.building.lumberMill").Allowed); // not built
        game.AssignBuildingWork(colony, Carpenter);
        Assert.Equal(0, colony.IdleColonists);
        Assert.False(game.CheckAssignBuildingWork(colony, TownHall).Allowed); // no idle colonist
        Assert.Throws<InvalidMoveException>(() => game.AssignBuildingWork(colony, TownHall));

        game.UnassignBuildingWork(colony, Carpenter);
        Assert.Equal(1, colony.IdleColonists);
        Assert.True(game.CheckAssignBuildingWork(colony, TownHall).Allowed);
    }

    [Fact]
    public void Pasture_BreedsHorses_OnlyWhenTwoArePresent()
    {
        // The country building auto-converts food→horses, gated by the
        // breeding number: no horses appear from an empty stable.
        Game game = FoundedColony();
        Colony colony = game.Colonies[0];
        colony.AddGoods("model.goods.food", 50);

        game.EndTurn();
        Assert.Equal(0, colony.StoreOf("model.goods.horses"));

        colony.AddGoods("model.goods.horses", 2);
        int foodBefore = colony.Food;
        game.EndTurn();
        Assert.Equal(3, colony.StoreOf("model.goods.horses")); // bred one
        Assert.True(colony.Food < foodBefore + 10, "breeding must consume food");
    }

    [Fact]
    public void SaveRoundTrip_PreservesBuildingsAndStaffing_PreV6Rederives()
    {
        Game game = FoundedColony();
        Colony colony = game.Colonies[0];
        foreach (var tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile);
        }
        game.AssignBuildingWork(colony, Carpenter);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(colony.Buildings, loaded.Colonies[0].Buildings);
        Assert.Equal(1, loaded.Colonies[0].BuildingWorkers[Carpenter]);

        // Pre-v6 save: buildings null → free base set re-derived.
        SaveGame v5 = SaveGame.From(game) with
        {
            Version = 5,
            Colonies = [new SavedColony(1, "Old", colony.Position.X, colony.Position.Y, 1)],
        };
        Game old = SaveGame.FromJson(v5.ToJson()).Restore(Classic);
        Assert.True(old.Colonies[0].HasBuilding(TownHall));
    }
}
