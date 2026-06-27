using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Forced buy-or-steal-or-abandon land-claim trigger (<c>86d3e4bj7</c>, FreeCol <c>ServerPlayer.csClaimLand</c> invoked
/// from <c>InGameController.claimLand</c> before <c>BuildColonyMission</c> builds): founding a colony on, or working,
/// a native-OWNED tile is no longer free — it forces a claim first. The human must surface a
/// <see cref="LandClaimChoice"/> (else <see cref="LandClaimRequiredException"/>); the AI resolves it deterministically
/// (pay if affordable, else steal — RNG-free). Stealing raises the acting player's <b>own</b> per-player alarm
/// (C1 multi-channel), reusing the voluntary <c>ClaimLandByPaying</c>/<c>ClaimLandByStealing</c> paths. Resolved
/// synchronously — no pending-claim state, so no save bump.
/// </summary>
public class NativeForcedLandClaimTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const int TakenAlarm = 200; // FreeCol TENSION_ADD_LAND_TAKEN

    /// <summary>True when a tile is empty land a colony could stand on (no unit, no colony, no settlement, settle-able).</summary>
    private static bool SettleableAndEmpty(Game game, Position p) =>
        game.Map.InBounds(p) && game.Map.TerrainAt(p).CanSettle
        && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>
    /// Stages a founder owned by <paramref name="ownerId"/> standing on a native-OWNED, non-settlement tile whose ring is
    /// clear of colonies (so founding is legal). Picks a settle-able empty tile far from every colony, makes ONLY the
    /// centre tile native (clearing any native ownership from its eight neighbours), and assigns it to a native nation
    /// that still has settlements (so the land-taken alarm is observable). Isolating the centre claim keeps the
    /// founding-cost assertions exact (the auto food-assign that runs on founding never has a native ring tile to also
    /// claim). Returns the founder, its tile, and the owning nation.
    /// </summary>
    private static (Unit founder, Position tile, string nation) StageFounderOnNativeLand(Game game, int ownerId = 0)
    {
        // A nation that owns sellable (non-settlement) land AND keeps at least one settlement, so its alarm is visible.
        string nation = game.NativeSettlements.Select(s => s.NationTypeId).First();

        Position tile = game.Map.AllPositions().First(p =>
            SettleableAndEmpty(game, p)
            && p.Neighbours().All(n => game.Map.InBounds(n) && game.ColonyAt(n) is null)
            && !game.Map.IsNativeOwned(p));
        foreach (Position n in tile.Neighbours().Where(game.Map.InBounds))
        {
            game.Map.ClearNativeOwner(n); // only the centre forces a claim — keeps the founding-cost assertions exact
        }
        game.Map.SetNativeOwner(tile, nation);

        Unit founder = game.SpawnUnit(Classic.Unit(FreeColonist), tile);
        founder.OwnerId = ownerId;
        return (founder, tile, nation);
    }

    private static Dictionary<int, int> NationAlarm(Game game, string nation, int channel) =>
        game.NativeSettlements.Where(s => s.NationTypeId == nation).ToDictionary(s => s.Id, s => s.AlarmFor(channel));

    // ---- The trigger: founding on native land forces the choice ----

    [Fact]
    public void CheckFoundColony_OnNativeLand_IsStillAllowed_TheClaimIsAFollowOnGate()
    {
        Game game = Game.New(Classic, seed: 7);
        (Unit founder, _, _) = StageFounderOnNativeLand(game);

        // Founding remains LEGAL on native land (CheckFoundColony is a pure legality gate); the claim is enforced by
        // FoundColony itself, surfaced via RequiredLandClaim.
        Assert.True(game.CheckFoundColony(founder).Allowed);
        Assert.True(game.RequiredLandClaim(founder.Position).Required);
    }

    [Fact]
    public void FoundColony_OnNativeLand_WithoutAChoice_ForcesTheHumanToDecide()
    {
        Game game = Game.New(Classic, seed: 7);
        (Unit founder, Position tile, string nation) = StageFounderOnNativeLand(game);

        // The no-choice overload throws for the human — the UI must surface buy/steal/abandon (no colony is founded).
        LandClaimRequiredException ex = Assert.Throws<LandClaimRequiredException>(() => game.FoundColony(founder));
        Assert.Equal(game.LandPrice(tile), ex.BuyPrice);
        Assert.Equal(nation, ex.OwningNation);
        Assert.Empty(game.Colonies);                 // nothing founded
        Assert.True(game.Map.IsNativeOwned(tile));   // still the natives'
    }

    [Fact]
    public void FoundColony_OnFreeLand_NeedsNoChoice_AndIsUnchanged()
    {
        // A non-native tile forces nothing — the parameterless overload founds exactly as before (byte-stable path).
        Game game = Game.New(Classic, seed: 7);
        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Assert.False(game.RequiredLandClaim(founder.Position).Required);

        Colony colony = game.FoundColony(founder);
        Assert.Single(game.Colonies);
        Assert.Equal(founder.Position, colony.Position);
    }

    [Fact]
    public void FoundColony_ByBuying_PaysThePrice_FoundsTheColony_AndRaisesNoAlarm()
    {
        Game game = Game.New(Classic, seed: 7);
        (Unit founder, Position tile, string nation) = StageFounderOnNativeLand(game);
        game.HumanPlayer.Gold = 100_000;
        int price = game.LandPrice(tile);
        var alarmBefore = NationAlarm(game, nation, channel: 0);

        Colony colony = game.FoundColony(founder, LandClaimChoice.Buy);

        Assert.Equal(100_000 - price, game.HumanPlayer.Gold); // paid exactly the price
        Assert.Equal(tile, colony.Position);
        Assert.False(game.Map.IsNativeOwned(tile));           // bought → no longer native
        Assert.True(game.Map.IsClaimedFromNatives(tile));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(alarmBefore[s.Id], s.AlarmFor(0))); // a peaceful purchase angers no one
    }

    [Fact]
    public void FoundColony_ByStealing_TakesItFree_FoundsTheColony_AndRaisesTheHumansAlarm()
    {
        Game game = Game.New(Classic, seed: 7);
        (Unit founder, Position tile, string nation) = StageFounderOnNativeLand(game);
        game.HumanPlayer.Gold = 1000;
        var alarmBefore = NationAlarm(game, nation, channel: 0);

        Colony colony = game.FoundColony(founder, LandClaimChoice.Steal);

        Assert.Equal(1000, game.HumanPlayer.Gold);  // no gold changes hands
        Assert.Equal(tile, colony.Position);
        Assert.False(game.Map.IsNativeOwned(tile));
        // every settlement of the robbed nation gains +200 on the HUMAN's channel (clamped at MaxAlarm)
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(System.Math.Min(NativeSettlement.MaxAlarm, alarmBefore[s.Id] + TakenAlarm), s.AlarmFor(0)));
    }

    [Fact]
    public void FoundColony_WithAbandonChoice_FoundsNothing()
    {
        Game game = Game.New(Classic, seed: 7);
        (Unit founder, Position tile, _) = StageFounderOnNativeLand(game);

        Assert.Throws<InvalidMoveException>(() => game.FoundColony(founder, LandClaimChoice.Abandon));
        Assert.Empty(game.Colonies);
        Assert.True(game.Map.IsNativeOwned(tile)); // untouched
    }

    // ---- Working a native-owned ring tile forces the same choice ----

    [Fact]
    public void AssignWork_OnANativeOwnedRingTile_ForcesTheHumanToDecide()
    {
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        // Make one of the colony's worked-able ring tiles native land, with an idle colonist to seat.
        Position ring = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
                && game.NativeSettlementAt(n) is null && !colony.TileWorkers.ContainsKey(n)
                && game.TileWorkOptions(n).Count > 0);
        string nation = game.NativeSettlements.Select(s => s.NationTypeId).First();
        game.Map.SetNativeOwner(ring, nation);
        colony.Population++; // one idle colonist to seat on the ring tile
        string good = game.TileWorkOptions(ring)[0].GoodsId;

        Assert.Throws<LandClaimRequiredException>(() => game.AssignWork(colony, ring, good));
        Assert.False(colony.TileWorkers.ContainsKey(ring)); // no one seated
        Assert.True(game.Map.IsNativeOwned(ring));          // still the natives'
    }

    [Fact]
    public void AssignWork_ByStealing_SeatsTheWorker_AndRaisesTheHumansAlarm()
    {
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        Position ring = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
                && game.NativeSettlementAt(n) is null && !colony.TileWorkers.ContainsKey(n)
                && game.TileWorkOptions(n).Count > 0);
        string nation = game.NativeSettlements.Select(s => s.NationTypeId).First();
        game.Map.SetNativeOwner(ring, nation);
        colony.Population++; // one idle colonist to seat on the ring tile
        string good = game.TileWorkOptions(ring)[0].GoodsId;
        var alarmBefore = NationAlarm(game, nation, channel: 0);

        game.AssignWork(colony, ring, good, LandClaimChoice.Steal);

        Assert.True(colony.TileWorkers.ContainsKey(ring));  // colonist is now working the tile
        Assert.False(game.Map.IsNativeOwned(ring));         // taken
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(System.Math.Min(NativeSettlement.MaxAlarm, alarmBefore[s.Id] + TakenAlarm), s.AlarmFor(0)));
    }

    // ---- The AI resolves deterministically (RNG-free), on its OWN alarm channel ----

    [Fact]
    public void AiResolveLandClaim_BuysWhenAffordable_StealsWhenNot()
    {
        Game game = Game.New(Classic, seed: 7);
        (_, Position tile, _) = StageFounderOnNativeLand(game);
        int price = game.LandPrice(tile);

        game.HumanPlayer.Gold = price;     // exactly enough → buy
        Assert.Equal(LandClaimChoice.Buy, game.AiResolveLandClaim(game.HumanPlayer, tile));

        game.HumanPlayer.Gold = price - 1; // one short → steal
        Assert.Equal(LandClaimChoice.Steal, game.AiResolveLandClaim(game.HumanPlayer, tile));
    }

    [Fact]
    public void FoundColony_ByAnAiFounder_AutoResolves_OnItsOwnChannel_LeavingTheHumanUntouched()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        (Unit founder, Position tile, string nation) = StageFounderOnNativeLand(game, ownerId: power.PlayerId);
        power.Gold = 0; // broke → the AI steals (FreeCol BuildColonyMission: checkGold false → STEAL_LAND)
        var humanBefore = NationAlarm(game, nation, channel: 0);
        var powerBefore = NationAlarm(game, nation, power.PlayerId);

        Colony colony = game.FoundColony(founder); // no explicit choice → AI auto-resolves (deterministic, RNG-free)

        Assert.Equal(power.PlayerId, colony.OwnerId);
        Assert.Equal(0, power.Gold);                // stole — paid nothing
        Assert.False(game.Map.IsNativeOwned(tile));
        // the steal landed on the POWER's channel; the human's channel 0 is untouched (ADR-009 per-player isolation)
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation), s =>
        {
            Assert.Equal(System.Math.Min(NativeSettlement.MaxAlarm, powerBefore[s.Id] + TakenAlarm), s.AlarmFor(power.PlayerId));
            Assert.Equal(humanBefore[s.Id], s.AlarmFor(0));
        });
    }

    [Fact]
    public void FoundColony_ByAWealthyAiFounder_BuysAndRaisesNoAlarm()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        (Unit founder, Position tile, string nation) = StageFounderOnNativeLand(game, ownerId: power.PlayerId);
        power.Gold = 100_000; // flush → the AI buys
        int price = game.LandPrice(tile);
        var powerBefore = NationAlarm(game, nation, power.PlayerId);

        game.FoundColony(founder); // AI auto-resolves → Buy

        Assert.Equal(100_000 - price, power.Gold);  // paid the price
        Assert.False(game.Map.IsNativeOwned(tile));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(powerBefore[s.Id], s.AlarmFor(power.PlayerId))); // a buy angers no one
    }

    // ---- L2 scenario: a voluntary buy vs a forced steal differ in tension ----

    [Fact]
    [Trait("Category", "Scenario")]
    public void FoundingByBuying_vs_ByStealing_DifferInTension()
    {
        // Two identical games (same seed, same staged native founding tile): one founds by BUYING the land, the other
        // by STEALING it. The buy leaves the tribe peaceful; the steal raises the human's alarm by exactly the
        // land-taken delta on every settlement of the robbed nation — so the two end states differ precisely in tension.
        Game buyGame = Game.New(Classic, seed: 7);
        Game stealGame = Game.New(Classic, seed: 7);
        (Unit buyFounder, _, string nation) = StageFounderOnNativeLand(buyGame);
        (Unit stealFounder, _, _) = StageFounderOnNativeLand(stealGame);
        buyGame.HumanPlayer.Gold = 100_000;
        stealGame.HumanPlayer.Gold = 100_000;

        // Same starting tension on the robbed nation in both games (the staging is identical at the same seed).
        var baseline = NationAlarm(buyGame, nation, channel: 0);
        Assert.Equal(baseline, NationAlarm(stealGame, nation, channel: 0));

        buyGame.FoundColony(buyFounder, LandClaimChoice.Buy);
        stealGame.FoundColony(stealFounder, LandClaimChoice.Steal);

        // Both founded a colony; the buyer spent gold, the thief did not.
        Assert.Single(buyGame.Colonies);
        Assert.Single(stealGame.Colonies);
        Assert.True(buyGame.HumanPlayer.Gold < 100_000);          // bought → paid
        Assert.Equal(100_000, stealGame.HumanPlayer.Gold);        // stole → free

        // The tension diverges: the buyer angered no one, the thief raised +200 on every settlement of the nation.
        Assert.All(buyGame.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(baseline[s.Id], s.AlarmFor(0)));
        Assert.All(stealGame.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(System.Math.Min(NativeSettlement.MaxAlarm, baseline[s.Id] + TakenAlarm), s.AlarmFor(0)));
        // And concretely: the thief's most-provoked settlement is strictly angrier than the buyer's same settlement.
        int buyMax = buyGame.NativeSettlements.Where(s => s.NationTypeId == nation).Max(s => s.AlarmFor(0));
        int stealMax = stealGame.NativeSettlements.Where(s => s.NationTypeId == nation).Max(s => s.AlarmFor(0));
        Assert.True(stealMax > buyMax, $"steal tension ({stealMax}) should exceed buy tension ({buyMax})");
    }

    // ---- No save change: a forced claim flows through the existing override (no new state) ----

    [Fact]
    public void AResolvedForcedClaim_AddsNoNewSaveState_AndRoundTripsByteIdentically()
    {
        Game game = Game.New(Classic, seed: 7);
        (Unit founder, _, _) = StageFounderOnNativeLand(game);
        game.HumanPlayer.Gold = 100_000;
        game.FoundColony(founder, LandClaimChoice.Buy);

        string json = SaveGame.From(game).ToJson();
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(65, SaveGame.CurrentVersion);                                 // forced land-claim adds no save field of its own (no pending-claim state); 56 comes from other slices
        Assert.Equal(json, SaveGame.From(restored).ToJson());                      // byte-identical round-trip
        Assert.Equal(game.Colonies.Count, restored.Colonies.Count);
    }
}
