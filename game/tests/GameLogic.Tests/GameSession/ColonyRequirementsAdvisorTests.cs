using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// L1 unit tests for the colony requirements-advisor oracles (FreeCol <c>ReportRequirementsPanel.checkColony</c>
/// extras, surfaced via <see cref="Game.ColonyProductionWarnings"/> / <see cref="Game.ColonyTileImprovementSuggestions"/>):
/// the production/missing-goods warning (a manned building starved of a net-negative input good) and the
/// tile-improvement suggestions (worked tiles that would gain from a plow/road, or unexplored worked tiles). Read-only
/// oracles (ADR-006) reusing the tested production-summary + improvement-yield rules — RNG-free.
/// </summary>
public class ColonyRequirementsAdvisorTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    // ── A. Production shortage: a manned building whose input good is net-negative ───────────────────────────

    [Fact]
    public void ColonyProductionWarnings_FlagsAStarvedBuilding_ByItsNetNegativeInput()
    {
        // A weaver's house staffed and fed cotton from the store, with no tile producing cotton: the colony consumes
        // cotton (net-negative) so the weaver runs below its potential — the production shortage warning.
        var game = Game.New(Classic, seed: 424242);
        game.Units[0].RoleId = RoleType.DefaultRoleId; // clean colony — no banked pioneer tools skew the baseline
        game.Units[0].RoleCount = 0;
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];
        foreach (Position tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile); // no tile makes cotton now
        }
        game.AssignBuildingWork(colony, "model.building.weaverHouse");
        colony.AddGoods("model.goods.cotton", 30); // the weaver has cotton to burn → it is consumed, none produced

        var warnings = game.ColonyProductionWarnings(colony);

        Assert.Contains(warnings, w =>
            w.OutputGoodsId == "model.goods.cloth" && w.InputGoodsId == "model.goods.cotton");
    }

    [Fact]
    public void ColonyProductionWarnings_AreEmpty_WhenNoBuildingIsStarved()
    {
        // A freshly founded colony with its default tile workers and no manned artisan house has no input shortage.
        var game = Game.New(Classic, seed: 424242);
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];

        Assert.Empty(game.ColonyProductionWarnings(colony));
    }

    // ── B. Tile-improvement suggestions ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ColonyTileImprovementSuggestions_SuggestsAnImprovement_ForAWorkedTileThatWouldGainYield()
    {
        // A colony working a farmable tile: a plow would raise the farmed good's yield, so it is suggested.
        var game = Game.New(Classic, seed: 424242);
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];

        var suggestions = game.ColonyTileImprovementSuggestions(colony);

        // At least one worked tile should carry a concrete (plow/road) improvement suggestion that raises its yield.
        Assert.Contains(suggestions, s => s.ImprovementId is not null);
        // Every concrete suggestion names a plow or a road (a natural river is never suggested), on a worked tile.
        Assert.All(suggestions.Where(s => s.ImprovementId is not null), s =>
        {
            Assert.True(colony.TileWorkers.ContainsKey(s.Tile));
            Assert.NotEqual(TileImprovementType.RiverId, s.ImprovementId);
        });
    }

    [Fact]
    public void ColonyTileImprovementSuggestions_DoNotRepeatAnImprovementAlreadyPresent()
    {
        // Once a tile already carries the plow, that tile no longer draws a plow suggestion.
        var game = Game.New(Classic, seed: 424242);
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];

        var before = game.ColonyTileImprovementSuggestions(colony);
        var plowRow = before.FirstOrDefault(s => s.ImprovementId == TileImprovementType.PlowId);
        if (plowRow.ImprovementId is null)
        {
            return; // no plowable worked tile in this seed — nothing to assert (the suggestion path is exercised above)
        }

        game.Map.AddImprovement(plowRow.Tile, Classic.Improvement(TileImprovementType.PlowId));
        var after = game.ColonyTileImprovementSuggestions(colony);
        Assert.DoesNotContain(after, s => s.Tile == plowRow.Tile && s.ImprovementId == TileImprovementType.PlowId);
    }
}
