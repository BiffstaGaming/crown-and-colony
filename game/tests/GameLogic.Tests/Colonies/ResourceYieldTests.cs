using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Bonus-resource yield modifiers (Phase 4 slice 8): a tile's special deposit boosts
/// what a colonist produces there, and the player's Founding-Father goods modifiers
/// (Henry Hudson +100% furs) stack on top. Values are pinned to the spec's
/// <c>&lt;resource-type&gt;</c> modifiers and tile yields.
/// </summary>
public class ResourceYieldTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Grain = "model.goods.grain";
    private const string Ore = "model.goods.ore";
    private const string Sugar = "model.goods.sugar";
    private const string Furs = "model.goods.furs";
    private const string Hudson = "model.foundingFather.henryHudson";

    private static readonly Position Origin = new(0, 0);

    // ───────────────────────── parsing ─────────────────────────

    [Fact]
    public void Ruleset_ParsesResourceModifiers_IncludingExpertScopes()
    {
        ResourceModifier minerals = Assert.Single(
            Classic.Resource("model.resource.minerals").Modifiers, m => m.GoodsId == Ore);
        Assert.Equal(ModifierType.Additive, minerals.Type);
        Assert.Equal(3, minerals.Value);
        Assert.True(minerals.IsUnscoped);

        Assert.Equal(ModifierType.Multiplicative,
            Assert.Single(Classic.Resource("model.resource.sugar").Modifiers).Type);

        // "game" boosts grain for everyone (+2) and again for an expert farmer (scoped).
        var gameGrain = Classic.Resource("model.resource.game").Modifiers
            .Where(m => m.GoodsId == Grain).ToList();
        Assert.Contains(gameGrain, m => m.IsUnscoped);
        Assert.Contains(gameGrain, m => m.ScopeUnitTypes.Contains("model.unit.expertFarmer"));
    }

    // ───────────────────────── tile yield ─────────────────────────

    [Fact]
    public void Resource_AddsToTheBaseTileYield()
    {
        Assert.Equal(7, Tile("model.tile.mountains", "model.resource.minerals").TileYield(Origin, Ore)); // 4 + 3
        Assert.Equal(6, Tile("model.tile.savannah", "model.resource.sugar").TileYield(Origin, Sugar));    // 3 × 2
    }

    [Fact]
    public void NoResource_LeavesTheBaseYieldUnchanged()
    {
        Assert.Equal(5, Tile("model.tile.plains").TileYield(Origin, Grain));
        Assert.Equal(4, Tile("model.tile.mountains").TileYield(Origin, Ore));
    }

    [Fact]
    public void ExpertScopedBonus_IsNotApplied_WithoutPerColonistExperts()
    {
        // "game" on plains: only the unscoped +2 applies (5 → 7), not the expert +2 (→ 9).
        Assert.Equal(7, Tile("model.tile.plains", "model.resource.game").TileYield(Origin, Grain));
    }

    [Fact]
    public void Resource_DoesNotEnable_AGoodTheTerrainCannotProduce()
    {
        // Mountains make no sugar, so a (mismatched) sugar deposit adds nothing.
        Assert.Equal(0, Tile("model.tile.mountains", "model.resource.sugar").TileYield(Origin, Sugar));
    }

    // ───────────────────────── founding-father goods modifiers ─────────────────────────

    [Fact]
    public void Hudson_Doubles_FurYield()
    {
        Assert.Equal(3, Tile("model.tile.mixedForest").TileYield(Origin, Furs)); // base, no Hudson
        Assert.Equal(6, Tile("model.tile.mixedForest", congress: [Hudson]).TileYield(Origin, Furs));
    }

    [Fact]
    public void ResourceThenFather_Stack_InIndexOrder()
    {
        // furs deposit (+3, index 10) then Hudson (+100%, index 40): (3 + 3) × 2 = 12.
        Game game = Tile("model.tile.mixedForest", "model.resource.furs", congress: [Hudson]);
        Assert.Equal(12, game.TileYield(Origin, Furs));
    }

    // ───────────────────────── production ─────────────────────────

    [Fact]
    public void Production_UsesTheBoostedYield()
    {
        // 3×3 plains, colony at centre, a "game" deposit on the (0,1) farm tile.
        string[] terrain = [.. Enumerable.Repeat("model.tile.plains", 9)];
        Game game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [new SavedColony(1, "Resourceville", 1, 1, 2)],
            Resources = [new SavedResource(1 * 3 + 0, "model.resource.game")], // (0,1)
        }.Restore(Classic);
        Colony colony = game.Colonies[0];

        game.AssignWork(colony, new Position(0, 1), Grain); // boosted farm: 5 + 2 = 7
        game.EndTurn();

        // centre 3 + farm 7 − two colonists eat 4 = 6 (vs 4 with no deposit).
        Assert.Equal(3 + 7 - 4, colony.Food);
    }

    // ───────────────────────── fixtures ─────────────────────────

    private static Game Tile(string terrain, string? resourceId = null, IReadOnlyList<string>? congress = null) =>
        new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = [terrain],
            Units = [],
            Explored = [],
            Resources = resourceId is null ? null : [new SavedResource(0, resourceId)],
            Congress = congress,
        }.Restore(Classic);
}
