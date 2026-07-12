using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Finite bonus-resource depletion (task 86d3fpxam): a working colonist expends a finite resource by the amount it
/// boosted its tile's output that turn; once the deposit's quantity is used up it is removed and the tile falls back
/// to its bare yield. Cross-checked against FreeCol <c>ServerColonyTile.expendResource</c> → <c>Resource.useQuantity</c>
/// (the reduction is the resource's bonus contribution, capped at the remaining quantity; quantity 0 → resource removed).
/// </summary>
public class ResourceDepletionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Ore = "model.goods.ore";
    private const string OreResource = "model.resource.ore"; // hills resource: +2 ore, finite (min 200, max 4000)

    /// <summary>
    /// A 3×3 hills map with a pop-2 colony at the centre and an <paramref name="oreQuantity"/>-deep ore deposit on the
    /// (0,1) ring tile. Hills produce 4 ore attended; the ore resource adds +2 → 6 ore/turn while it lasts.
    /// </summary>
    private static Game HillsColonyWithOreDeposit(int oreQuantity)
    {
        string[] terrain = [.. Enumerable.Repeat("model.tile.hills", 9)];
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
            Colonies = [new SavedColony(1, "Diggertown", 1, 1, 2)],
            Resources = [new SavedResource(1 * 3 + 0, OreResource)],         // deposit on (0,1)
            ResourceQuantities = [new SavedResourceQuantity(1 * 3 + 0, oreQuantity)],
        };
        return save.Restore(Classic);
    }

    private static readonly Position OreTile = new(0, 1);

    [Fact]
    public void WorkingAFiniteResource_DecrementsItByTheBonus()
    {
        // Big deposit: one turn mining ore expends exactly the resource's +2 bonus.
        Game game = HillsColonyWithOreDeposit(oreQuantity: 100);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, OreTile, Ore);

        Assert.Equal(6, game.TileYieldPotential(OreTile, Ore)); // 4 base + 2 resource
        game.EndTurn();

        Assert.Equal(100 - 2, game.Map.ResourceQuantityAt(OreTile)); // depleted by the +2 bonus
        Assert.Equal(OreResource, game.Map.ResourceAt(OreTile));     // still present
    }

    [Fact]
    public void NotWorkingAFiniteResource_LeavesItUntouched()
    {
        // No worker assigned to the deposit tile → its quantity does not move across a turn.
        Game game = HillsColonyWithOreDeposit(oreQuantity: 50);
        game.EndTurn();
        Assert.Equal(50, game.Map.ResourceQuantityAt(OreTile));
    }

    [Fact]
    public void Exhaustion_RemovesTheResource_AndYieldDropsToBase()
    {
        // A deposit with only 2 left: one turn's +2 mining drains it to 0 → the deposit is removed and the tile
        // reverts to its bare 4-ore hills yield.
        Game game = HillsColonyWithOreDeposit(oreQuantity: 2);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, OreTile, Ore);

        game.EndTurn();

        Assert.Null(game.Map.ResourceAt(OreTile));            // deposit gone
        Assert.Null(game.Map.ResourceQuantityAt(OreTile));    // and its quantity cleared
        Assert.Equal(4, game.TileYieldPotential(OreTile, Ore)); // bare hills ore (no +2)
    }

    [Fact]
    public void ResourceDrainsToExhaustionOverSeveralTurns()
    {
        // 5 deep, mined at 2/turn: 5 → 3 → 1 → exhausted on the third turn (1 - 2 ≤ 0).
        Game game = HillsColonyWithOreDeposit(oreQuantity: 5);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, OreTile, Ore);

        game.EndTurn();
        Assert.Equal(3, game.Map.ResourceQuantityAt(OreTile));
        game.EndTurn();
        Assert.Equal(1, game.Map.ResourceQuantityAt(OreTile));
        game.EndTurn();
        Assert.Null(game.Map.ResourceAt(OreTile)); // exhausted and removed
    }

    [Fact]
    public void DepletedResource_RoundTripsThroughSave()
    {
        // After one turn of mining, the reduced quantity persists through a save/load (the existing v46 field).
        Game game = HillsColonyWithOreDeposit(oreQuantity: 100);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, OreTile, Ore);
        game.EndTurn();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(98, loaded.Map.ResourceQuantityAt(OreTile));
    }

    [Fact]
    public void SaveVersion_IsUnchanged() => Assert.Equal(75, SaveGame.CurrentVersion);
}
