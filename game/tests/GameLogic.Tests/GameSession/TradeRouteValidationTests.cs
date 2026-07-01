using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Trade-route validation warnings (86d3drn0j): a pure read mirroring FreeCol's <c>TradeRoute.verify()</c>. The game
/// <b>warns but never blocks</b> — a route with warnings still runs. Faithful checks: fewer than two stops; a stop
/// that is not the owner's colony; no stop loads any goods; a good loaded at every stop (never delivered anywhere),
/// relaxed when ENHANCED_TRADE_ROUTES is on (off by default in classic, so always on-check today).
/// </summary>
public class TradeRouteValidationTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Sugar = "model.goods.sugar";
    private const string Cotton = "model.goods.cotton";

    /// <summary>A 3-tile plains strip: human colonies Alpha (0,0, 100 sugar) and Beta (2,0), a human wagon train on Alpha.</summary>
    private static Game TwoColonyStrip(out Colony alpha, out Colony beta, Ruleset? ruleset = null)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.plains", "model.tile.plains"],
            Units = [new SavedUnit(1, "model.unit.wagonTrain", 0, 0, 12)],
            Explored = [0, 1, 2],
            Colonies =
            [
                new SavedColony(1, "Alpha", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 }),
                new SavedColony(2, "Beta", 2, 0, 1),
            ],
        };
        Game game = save.Restore(ruleset ?? Classic);
        alpha = game.Colonies[0];
        beta = game.Colonies[1];
        return game;
    }

    [Fact]
    public void AValidRoute_HasNoWarnings()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Sugar run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]); // pick up at Alpha, drop at Beta

        Assert.Empty(game.ValidateTradeRoute(route));
    }

    [Fact]
    public void FewerThanTwoStops_WarnsNotEnoughStops()
    {
        Game game = TwoColonyStrip(out Colony alpha, out _);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "One stop", [new TradeRouteStop(alpha.Id, [Sugar])]);

        TradeRouteWarning warning = Assert.Single(game.ValidateTradeRoute(route));
        Assert.Equal(TradeRouteWarningKind.NotEnoughStops, warning.Kind);
        Assert.Equal(route.Id, warning.RouteId);
    }

    [Fact]
    public void AStopThatIsNotTheOwnersColony_WarnsInvalidStop_WithTheStopIndex()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        // Build a route whose 2nd stop's colony is then demolished, leaving a stop that points at nothing.
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);
        game.AbandonColony(beta); // Beta no longer exists → stop index 1 is now invalid

        TradeRouteWarning warning = Assert.Single(game.ValidateTradeRoute(route),
            w => w.Kind == TradeRouteWarningKind.InvalidStop);
        Assert.Equal(1, warning.StopIndex); // the second stop
    }

    [Fact]
    public void NoStopLoadsAnything_WarnsAllEmpty()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Empty",
            [new TradeRouteStop(alpha.Id, []), new TradeRouteStop(beta.Id, [])]); // nothing loaded anywhere

        Assert.Contains(game.ValidateTradeRoute(route), w => w.Kind == TradeRouteWarningKind.AllEmpty);
    }

    [Fact]
    public void AGoodLoadedAtEveryStop_WarnsAlwaysPresent_NamingTheGood()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        // Sugar is loaded at BOTH stops → it is never unloaded anywhere → can never be delivered.
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Stuck",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [Sugar])]);

        TradeRouteWarning warning = Assert.Single(game.ValidateTradeRoute(route),
            w => w.Kind == TradeRouteWarningKind.GoodsAlwaysPresent);
        Assert.Equal(Sugar, warning.GoodsId);
        Assert.Contains("sugar", warning.Message);
    }

    [Fact]
    public void EnhancedTradeRoutes_OptionPlumbsThrough_ClassicDefaultOff()
    {
        // The option defaults off in classic (byte-identity guard) and the seam flips it.
        Assert.False(Ruleset.LoadClassic().GameOptions.EnhancedTradeRoutes);
        Assert.True(Ruleset.LoadClassic().WithEnhancedTradeRoutes(true).GameOptions.EnhancedTradeRoutes);
        Assert.False(Ruleset.LoadClassic().WithEnhancedTradeRoutes(false).GameOptions.EnhancedTradeRoutes);
    }

    [Fact]
    public void AGoodLoadedAtEveryStop_WhenEnhancedTradeRoutesOn_NoAlwaysPresentWarning()
    {
        // Same setup as AGoodLoadedAtEveryStop_WarnsAlwaysPresent, but with model.option.enhancedTradeRoutes ON:
        // FreeCol TradeRoute.verify() relaxes the always-present check (cargoes are re-sorted for max transfer), so
        // the GoodsAlwaysPresent advisory is suppressed and this otherwise-flagged route validates clean.
        Ruleset enhanced = Ruleset.LoadClassic().WithEnhancedTradeRoutes(true);
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta, enhanced);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Stuck",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [Sugar])]);

        IReadOnlyList<TradeRouteWarning> warnings = game.ValidateTradeRoute(route);
        Assert.DoesNotContain(warnings, w => w.Kind == TradeRouteWarningKind.GoodsAlwaysPresent);
    }

    [Fact]
    public void AGoodDeliveredAtSomeStop_DoesNotWarnAlwaysPresent()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        // Sugar at Alpha only, cotton at Beta only — each good is unloaded at the OTHER stop, so neither is "always present".
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Swap",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [Cotton])]);

        Assert.Empty(game.ValidateTradeRoute(route));
    }

    [Fact]
    public void MultipleAlwaysPresentGoods_AreEachReported()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Both stuck",
            [new TradeRouteStop(alpha.Id, [Sugar, Cotton]), new TradeRouteStop(beta.Id, [Cotton, Sugar])]);

        IReadOnlyList<TradeRouteWarning> warnings = game.ValidateTradeRoute(route);
        Assert.Equal(2, warnings.Count(w => w.Kind == TradeRouteWarningKind.GoodsAlwaysPresent));
        Assert.Contains(warnings, w => w.GoodsId == Sugar);
        Assert.Contains(warnings, w => w.GoodsId == Cotton);
    }

    [Fact]
    public void ValidateTradeRoutesOf_AggregatesAcrossAllThePlayersRoutes()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        game.CreateTradeRoute(game.HumanPlayer, "Good",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]); // valid → 0 warnings
        game.CreateTradeRoute(game.HumanPlayer, "Bad", [new TradeRouteStop(alpha.Id, [Sugar])]); // 1 stop → NotEnoughStops

        IReadOnlyList<TradeRouteWarning> warnings = game.ValidateTradeRoutesOf(game.HumanPlayer);
        TradeRouteWarning warning = Assert.Single(warnings);
        Assert.Equal(TradeRouteWarningKind.NotEnoughStops, warning.Kind);
    }

    [Fact]
    public void ValidateTradeRoutesOf_IsEmptyWhenThePlayerHasNoRoutes()
    {
        Game game = TwoColonyStrip(out _, out _);
        Assert.Empty(game.ValidateTradeRoutesOf(game.HumanPlayer));
    }

    [Fact]
    public void Validation_DoesNotMutateTheRoute_OrRunTheCarrier()
    {
        Game game = TwoColonyStrip(out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Stuck",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [Sugar])]);

        game.ValidateTradeRoute(route); // pure read
        game.ValidateTradeRoute(route);

        Assert.Equal(100, alpha.StoreOf(Sugar)); // nothing hauled
        Assert.Equal(2, game.HumanPlayer.TradeRoutes.Single().Stops.Count); // route unchanged
    }
}
