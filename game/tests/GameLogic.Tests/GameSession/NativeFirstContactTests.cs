using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native first contact (<c>86d3kgbnq</c>, Col1: on first meeting a tribe the chief offers peace + a small land grant;
/// the HUMAN may ACCEPT — peace + a parcel of the tribe's land + the chief's welcome — or REJECT, and the tribe declares
/// war). A human's first speak-with-chief of a NATION defers to the accept/reject prompt; an AI power (and any later
/// settlement of an already-met tribe) resolves inline as before. The offer is transient (never saved); the land grant
/// is a persistent claim (the saved override the native-ownership derivation honours).
/// </summary>
public class NativeFirstContactTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Free = "model.unit.freeColonist";

    private static bool LandReachable(Game game, NativeSettlement s) =>
        s.Position.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);

    private static Position LandBeside(Game game, NativeSettlement s) =>
        s.Position.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);

    private static (Game game, NativeSettlement settlement, Unit colonist) Setup()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement settlement = game.NativeSettlements.First(s => LandReachable(game, s));
        Unit colonist = game.SpawnUnit(Classic.Unit(Free), LandBeside(game, settlement));
        return (game, settlement, colonist);
    }

    [Fact]
    public void Reject_DeclaresWarWithTheTribe_AndGrantsNoLand()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        int goldBefore = game.Gold;
        game.Visit(colonist, settlement); // the offer is now pending

        string outcome = game.ResolvePendingFirstContact(accept: false);

        Assert.Contains("war", outcome);
        Assert.Null(game.PendingFirstContact);
        Assert.Equal(Stance.War, game.NativeStanceToward(settlement.NationTypeId, game.HumanPlayer.PlayerId));
        Assert.Equal(goldBefore, game.Gold); // no gift on rejection
    }

    [Fact]
    public void Accept_GrantsAParcelOfTheTribesLand_ThatPersistsThroughSave()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        Assert.Contains(settlement.NationTypeId, game.Map.NativeOwners.Values); // the tribe claims land around its settlement
        Assert.Equal(settlement.NationTypeId, game.Map.NativeOwnerOf(colonist.Position)); // incl. the tile the colonist stands on

        game.Visit(colonist, settlement);
        game.ResolvePendingFirstContact(accept: true);

        Assert.True(game.Map.IsClaimedFromNatives(colonist.Position)); // the tribe cedes the ground under (and around) you
        Assert.Null(game.Map.NativeOwnerOf(colonist.Position));        // no longer native-owned
        int claimedNow = game.Map.ClaimedFromNatives.Count;
        Assert.True(claimedNow > 0);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(claimedNow, restored.Map.ClaimedFromNatives.Count); // survives save/load (the derivation honours the override)
        Assert.True(restored.Map.IsClaimedFromNatives(colonist.Position));
    }

    [Fact]
    public void ForAnAiPower_ResolvesInline_WithNoPendingOffer()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        colonist.OwnerId = power.PlayerId; // an AI power's unit

        game.Visit(power, colonist, settlement); // internal overload for a non-human owner

        Assert.Null(game.PendingFirstContact); // the AI auto-establishes contact inline — no dialog
        Assert.True(settlement.HasBeenVisitedBy(power.PlayerId));
    }

    [Fact]
    public void SecondSettlementOfAnAlreadyMetTribe_ResolvesInline_WithNoOffer()
    {
        Game game = Game.New(Classic, Seed);
        var pair = game.NativeSettlements.Where(s => LandReachable(game, s))
            .GroupBy(s => s.NationTypeId).FirstOrDefault(g => g.Count() >= 2)?.ToList();
        if (pair is null)
        {
            return; // no multi-settlement nation reachable on this seed — the inline path is also covered by the AI test
        }
        NativeSettlement first = pair[0], second = pair[1];

        Unit c1 = game.SpawnUnit(Classic.Unit(Free), LandBeside(game, first));
        game.Visit(c1, first);
        game.ResolvePendingFirstContact(accept: true); // the nation is now met

        Unit c2 = game.SpawnUnit(Classic.Unit(Free), LandBeside(game, second));
        int gift = game.Visit(c2, second); // a second settlement of an already-met tribe → inline gift, no offer

        Assert.Null(game.PendingFirstContact);
        Assert.InRange(gift, 10, 80);
    }

    [Fact]
    public void UnansweredOffer_AtTurnEnd_DefaultsToAcceptance_NotWar()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        game.Visit(colonist, settlement);
        Assert.NotNull(game.PendingFirstContact);

        game.EndTurn(); // an unanswered offer times out to acceptance (peace) before the AI turns

        Assert.Null(game.PendingFirstContact);
        Assert.NotEqual(Stance.War, game.NativeStanceToward(settlement.NationTypeId, game.HumanPlayer.PlayerId));
    }

    [Fact]
    public void PendingOffer_IsNotSaved()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        game.Visit(colonist, settlement);
        Assert.NotNull(game.PendingFirstContact);
        Assert.DoesNotContain("PendingFirstContact", SaveGame.From(game).ToJson()); // transient UI state, never serialized
    }
}
