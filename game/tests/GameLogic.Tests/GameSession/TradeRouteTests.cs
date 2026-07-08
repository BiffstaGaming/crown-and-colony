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
/// Trade routes (86d3c9rq1): a player defines a named ring of colony stops (each listing goods to load), assigns a
/// carrier to it, and the carrier auto-hauls each turn — delivering what a stop doesn't want, loading what it does,
/// then moving to the next stop. Reuses the carrier-haulage seam (Load/UnloadToColony). Save v43, omit-when-default.
/// </summary>
public class TradeRouteTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Sugar = "model.goods.sugar";

    /// <summary>A 3-tile plains strip: colony Alpha (0,0) holding 100 sugar, colony Beta (2,0), a human wagon train on Alpha.</summary>
    private static Game TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta)
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
        Game game = save.Restore(Classic);
        wagon = game.Units[0];
        alpha = game.Colonies[0];
        beta = game.Colonies[1];
        return game;
    }

    [Fact]
    public void CreateTradeRoute_RejectsAStopThatIsNotThePlayersColony()
    {
        Game game = TwoColonyStrip(out _, out Colony alpha, out _);
        Assert.Throws<InvalidMoveException>(() => game.CreateTradeRoute(game.HumanPlayer, "Bogus",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(999, [])])); // 999 is no colony
    }

    [Fact]
    public void AssignTradeRoute_IsCarrierOnly()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);

        Unit colonist = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), new Position(1, 0)); // space 0 → not a carrier
        Assert.False(game.CheckAssignTradeRoute(colonist, route.Id).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.AssignTradeRoute(colonist, route.Id));

        game.AssignTradeRoute(wagon, route.Id); // a wagon train is a carrier
        Assert.Equal(route.Id, wagon.TradeRouteId);
        game.ClearTradeRoute(wagon);
        Assert.Null(wagon.TradeRouteId);
    }

    [Fact]
    public void AnAssignedCarrier_HaulsGoodsAlongItsRoute()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Sugar run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]); // load at Alpha, deliver at Beta
        game.AssignTradeRoute(wagon, route.Id);

        for (int turn = 0; turn < 5; turn++)
        {
            game.EndTurn();
        }

        Assert.True(beta.StoreOf(Sugar) > 0, "the carrier should have hauled sugar from Alpha to Beta");
        Assert.Equal(0, alpha.StoreOf(Sugar)); // picked up at the source
    }

    // ───────────────────────── Per-good import level cap (86d3fpz0t) ─────────────────────────

    [Fact]
    public void ImportLevel_CapsAutomaticDelivery_LeavingTheSurplusAboard()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        beta.AddGoods(Sugar, 20);                  // Beta already holds 20 sugar
        game.SetColonyImport(beta, Sugar, 30);     // …and won't auto-import sugar past 30

        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Capped run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]); // pick up at Alpha, drop at Beta
        game.AssignTradeRoute(wagon, route.Id);

        for (int turn = 0; turn < 6; turn++)
        {
            game.EndTurn();
        }

        // FreeCol getImportAmount: deliver only up to (import level − present) = 30 − 20 = 10; Beta tops out at 30.
        Assert.Equal(30, beta.StoreOf(Sugar));
        // The carrier keeps shuttling but can never push Beta past 30, so the excess it loaded stays aboard / at Alpha.
        Assert.True(wagon.CargoOf(Sugar) + alpha.StoreOf(Sugar) > 0, "the surplus the cap refused is left aboard / unmoved");
    }

    [Fact]
    public void ImportLevel_AtWarehouseCapacityByDefault_DeliversTheWholeLoad()
    {
        // No import level set on Beta → the effective cap is its warehouse capacity, so delivery is bounded only by the
        // warehouse exactly as before this feature. (Proves an untouched good auto-imports unchanged.)
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        Assert.Equal(game.ColonyWarehouseCapacity(beta), game.EffectiveImportLevel(beta, Sugar));

        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Open run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);
        game.AssignTradeRoute(wagon, route.Id);

        for (int turn = 0; turn < 6; turn++)
        {
            game.EndTurn();
        }

        Assert.Equal(100, beta.StoreOf(Sugar)); // all 100 hauled across (well under the depot's 100+ capacity)
        Assert.Equal(0, alpha.StoreOf(Sugar));
    }

    [Fact]
    public void ImportLevel_SurvivesSaveRoundTrip_AtV67()
    {
        Game game = TwoColonyStrip(out _, out _, out Colony beta);
        game.SetColonyImport(beta, Sugar, 40);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(71, SaveGame.CurrentVersion);
        Assert.Equal(40, loaded.Colonies.Single(c => c.Id == beta.Id).ExportOf(Sugar).ImportLevel);
    }

    // ───────────────────────── Per-type compact-cargo load cap (86d3fpz2g) ─────────────────────────

    /// <summary>
    /// A 3-tile coastal strip — plains port Alpha at (0,0) with a <b>warehouse expansion</b> (capacity 300) pre-stocked
    /// with 250 sugar, ocean at (1,0), high seas at (2,0) — and a human <b>galleon</b> (6-slot hold) on the ocean beside
    /// Alpha. The big depot lets the 250 survive end-of-turn warehouse spillage, and the galleon's large hold means the
    /// per-type load cap, not the hold, is what binds.
    /// </summary>
    private static Game BigDepotWithGalleon(out Unit galleon, out Colony alpha)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
            Units = [new SavedUnit(1, Galleon, 1, 0, 18)], // galleon on the ocean tile, adjacent to Alpha
            Explored = [0, 1, 2],
            Colonies =
            [
                new SavedColony(1, "Alpha", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 250 },
                    Buildings: ["model.building.warehouseExpansion"]),
            ],
        };
        Game game = save.Restore(Classic);
        galleon = game.Units[0];
        alpha = game.Colonies[0];
        return game;
    }

    [Fact]
    public void AutoLoad_CapsEachGoodAtOneCargoSlotPerListing_NotTheWholeHold()
    {
        // FreeCol getCompactCargo: the auto-load target for a good is CargoSlotSize(100) × (times it is listed at the
        // stop). Alpha holds 250 sugar and the galleon has a 6-slot hold (600) — before this fix it grabbed the whole
        // hold (all 250); now, sugar listed ONCE tops the galleon up to exactly 100. One EndTurn loads-and-advances at
        // Alpha but doesn't reach Beta, so we read the load straight off the carrier before any delivery.
        Game game = BigDepotWithGalleon(out Unit galleon, out Colony alpha);

        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "One listing",
            [new TradeRouteStop(alpha.Id, [Sugar]), TradeRouteStop.Europe([])]); // sugar listed once, deliver in Europe
        game.AssignTradeRoute(galleon, route.Id);

        game.EndTurn();

        Assert.Equal(100, galleon.CargoOf(Sugar)); // exactly one slot, not the whole 600-unit hold
        Assert.Equal(150, alpha.StoreOf(Sugar));   // 250 − 100 left behind
    }

    [Fact]
    public void AutoLoad_ListingAGoodTwice_RaisesItsCapToTwoCargoSlots()
    {
        // Listing sugar TWICE at the stop doubles its compact-cargo target to 200 (CargoSlotSize × 2). The galleon's
        // hold is far bigger, so the cap — not the hold — binds at 200. Proves the cap scales with the listing count.
        Game game = BigDepotWithGalleon(out Unit galleon, out Colony alpha);

        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Two listings",
            [new TradeRouteStop(alpha.Id, [Sugar, Sugar]), TradeRouteStop.Europe([])]); // sugar listed twice
        game.AssignTradeRoute(galleon, route.Id);

        game.EndTurn();

        Assert.Equal(200, galleon.CargoOf(Sugar)); // two slots' worth
        Assert.Equal(50, alpha.StoreOf(Sugar));    // 250 − 200
    }

    // ───────────────────────── Europe stops (86d3e4bcp, GAP B) ─────────────────────────

    private const string Galleon = "model.unit.galleon";

    /// <summary>
    /// A 3×1 coastal strip — plains port colony Alpha at (0,0) holding 200 sugar, ocean at (1,0), high seas at (2,0) —
    /// with a human galleon on the ocean beside the colony. Returns the game, the galleon and Alpha.
    /// </summary>
    private static Game CoastalPort(out Unit galleon, out Colony alpha)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
            Units = [new SavedUnit(1, Galleon, 1, 0, 18)],
            Explored = [0, 1, 2],
            Colonies = [new SavedColony(1, "Alpha", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 200 })],
        };
        Game game = save.Restore(Classic);
        galleon = game.Units[0];
        alpha = game.Colonies[0];
        return game;
    }

    [Fact]
    public void CreateTradeRoute_AcceptsAEuropeStop()
    {
        Game game = CoastalPort(out _, out Colony alpha);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Export run",
            [new TradeRouteStop(alpha.Id, [Sugar]), TradeRouteStop.Europe([])]); // Alpha → Europe
        Assert.Equal(2, route.Stops.Count);
        Assert.True(route.Stops[1].IsEurope);
        Assert.Empty(game.ValidateTradeRoute(route)); // a Europe stop is a valid location → no warnings
    }

    [Fact]
    public void AEuropeStopRoute_HaulsColonyGoodsToEurope_AndSellsThem()
    {
        Game game = CoastalPort(out Unit galleon, out Colony alpha);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Sugar export",
            [new TradeRouteStop(alpha.Id, [Sugar]), TradeRouteStop.Europe([])]); // load sugar at Alpha, sell everything in Europe
        game.AssignTradeRoute(galleon, route.Id);
        int goldBefore = game.HumanPlayer.Gold;
        int sugarBefore = alpha.StoreOf(Sugar);

        for (int turn = 0; turn < 14; turn++)
        {
            game.EndTurn(); // pick up at Alpha → sail to Europe → sell → sail home → repeat
        }

        Assert.True(alpha.StoreOf(Sugar) < sugarBefore, "the carrier should have shipped sugar out of Alpha");
        Assert.True(game.HumanPlayer.Gold > goldBefore, "selling the sugar in Europe should have credited the treasury");
    }

    [Fact]
    public void AEuropeStop_SurvivesSaveRoundTrip_WithoutAVersionBump()
    {
        Game game = CoastalPort(out _, out Colony alpha);
        game.CreateTradeRoute(game.HumanPlayer, "Export",
            [new TradeRouteStop(alpha.Id, [Sugar]), TradeRouteStop.Europe([Sugar])]); // Europe stop loads sugar (buy)

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        TradeRoute restored = Assert.Single(loaded.HumanPlayer.TradeRoutes);
        Assert.Equal(2, restored.Stops.Count);
        Assert.False(restored.Stops[0].IsEurope);          // the colony stop
        Assert.True(restored.Stops[1].IsEurope);           // the Europe stop round-trips via the sentinel ColonyId (0)
        Assert.Equal([Sugar], restored.Stops[1].LoadGoodsIds);
        Assert.Equal(71, SaveGame.CurrentVersion);         // Europe stop adds no save field of its own (existing TradeRouteStop shape); 58 comes from other slices
    }

    [Fact]
    public void AWagonAssignedAEuropeStop_SkipsIt_RatherThanStallingForever()
    {
        Game game = CoastalPort(out _, out Colony alpha);
        Unit wagon = game.SpawnUnit(Classic.Unit("model.unit.wagonTrain"), alpha.Position); // a land carrier, cannot sail
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Mixed",
            [new TradeRouteStop(alpha.Id, [Sugar]), TradeRouteStop.Europe([])]);
        game.AssignTradeRoute(wagon, route.Id);
        wagon.TradeRouteStopIndex = 1; // park it on the Europe stop

        game.EndTurn();

        Assert.Equal(0, wagon.TradeRouteStopIndex); // the un-sailable Europe stop was skipped (wrapped back to stop 0), not stuck
        Assert.True(wagon.IsOnMap);                 // and the wagon never left the map
    }

    [Fact]
    public void ARouteLessGame_OmitsTheV43Tokens_AndStaysCurrent()
    {
        string json = SaveGame.From(Game.New(Classic, seed: 7)).ToJson();
        Assert.DoesNotContain("\"TradeRoutes\"", json);
        Assert.DoesNotContain("\"TradeRouteId\"", json);
        Assert.DoesNotContain("\"TradeRouteStop\"", json);
        Assert.DoesNotContain("NextTradeRouteId", json); // omit-when-default (counter still 1) → byte-identical to v44
        Assert.Equal(71, SaveGame.CurrentVersion);
    }

    [Fact]
    public void Routes_AndAssignment_SurviveSaveRoundTrip()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Sugar run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);
        game.AssignTradeRoute(wagon, route.Id);
        game.EndTurn(); // serves Alpha + advances the stop index to 1

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        TradeRoute restored = Assert.Single(loaded.HumanPlayer.TradeRoutes);
        Assert.Equal("Sugar run", restored.Name);
        Assert.Equal(2, restored.Stops.Count);
        Assert.Equal([Sugar], restored.Stops[0].LoadGoodsIds);
        Unit loadedWagon = loaded.Units.Single(u => u.Id == wagon.Id);
        Assert.Equal(route.Id, loadedWagon.TradeRouteId);
        Assert.Equal(wagon.TradeRouteStopIndex, loadedWagon.TradeRouteStopIndex); // mid-route position preserved
    }

    [Fact]
    public void RemoveTradeRoute_DeletesTheRoute_AndUnassignsItsCarrier()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);
        game.AssignTradeRoute(wagon, route.Id);

        game.RemoveTradeRoute(game.HumanPlayer, route.Id);

        Assert.Empty(game.HumanPlayer.TradeRoutes); // the route is gone
        Assert.Null(wagon.TradeRouteId);            // its carrier was un-assigned
        game.RemoveTradeRoute(game.HumanPlayer, 999); // unknown route → no-op, no throw
    }

    [Fact]
    public void NextTradeRouteId_SurvivesSaveLoad_SoIdsAreNotReusedAfterDeleting()
    {
        Game game = TwoColonyStrip(out _, out Colony alpha, out Colony beta);
        List<TradeRouteStop> Stops() => [new(alpha.Id, [Sugar]), new(beta.Id, [])];
        game.CreateTradeRoute(game.HumanPlayer, "A", Stops()); // id 1
        game.CreateTradeRoute(game.HumanPlayer, "B", Stops()); // id 2
        TradeRoute c = game.CreateTradeRoute(game.HumanPlayer, "C", Stops()); // id 3 → counter now 4
        game.RemoveTradeRoute(game.HumanPlayer, c.Id);         // delete the highest id

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        // Pre-fix the counter re-derived to max(1,2)+1 = 3 and the next route REUSED id 3; now it's preserved at 4.
        TradeRoute next = loaded.CreateTradeRoute(loaded.HumanPlayer, "D", Stops());
        Assert.Equal(4, next.Id);
        Assert.Equal([1, 2, 4], loaded.HumanPlayer.TradeRoutes.Select(r => r.Id)); // A, B, D — all distinct, no reuse
    }

    [Fact]
    public void APreV45Save_WithoutTheCounter_FallsBackToMaxIdPlusOne()
    {
        Game game = TwoColonyStrip(out _, out Colony alpha, out Colony beta);
        List<TradeRouteStop> Stops() => [new(alpha.Id, [Sugar]), new(beta.Id, [])];
        game.CreateTradeRoute(game.HumanPlayer, "A", Stops()); // id 1
        game.CreateTradeRoute(game.HumanPlayer, "B", Stops()); // id 2

        // Simulate a pre-v45 save: the NextTradeRouteId field is absent (null) on every player.
        SaveGame save = SaveGame.From(game);
        save = save with { Players = save.Players!.Select(p => p with { NextTradeRouteId = null }).ToList() };
        Game loaded = save.Restore(Classic);

        // No persisted counter → fall back to max(1,2)+1 = 3 (the legacy behaviour, preserved for old saves).
        Assert.Equal(3, loaded.CreateTradeRoute(loaded.HumanPlayer, "C", Stops()).Id);
    }
}
