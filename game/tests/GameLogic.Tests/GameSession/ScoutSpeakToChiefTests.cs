using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Scout speaks with the chief (<c>86d3c9tf0</c>, FreeCol <c>InGameController.scoutSpeakToChief</c>): a scout-role
/// unit's chief audience rolls die-at-hateful / nothing-if-scouted / a seasoned-scout training chance / "tales" (a
/// map reveal) / "beads" (gold from the settlement type's <c>&lt;gifts&gt;</c> range, +10% for an expert scout).
/// Since native first contact (<c>86d3kgbnq</c>), the FIRST audience with a nation is the accepted peace offer — the
/// scout outcome runs on <c>ResolvePendingFirstContact(accept)</c> (which takes a scripted-RNG overload); a later
/// settlement of an already-met tribe runs the audience inline. A non-scout colonist gets the basic flat gift.
/// </summary>
public class ScoutSpeakToChiefTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string ScoutRole = "model.role.scout";
    private const string SeasonedScout = "model.unit.seasonedScout";
    private const string FreeColonist = "model.unit.freeColonist";

    private sealed class ScriptedRandom(params int[] values) : IGameRandom
    {
        private readonly Queue<int> _values = new(values);
        public int Next(int maxExclusive) => _values.Dequeue();
        public int Next(int minInclusive, int maxExclusive) => _values.Dequeue();
        public double NextDouble() => 0;
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>A fresh game with a scout-role unit standing next to a settlement matching <paramref name="pick"/>.</summary>
    private static (Game Game, NativeSettlement Settlement, Unit Scout) ScoutAtSettlement(
        Func<NativeSettlement, bool>? pick = null, string unitType = FreeColonist)
    {
        Game game = Game.New(Classic, Seed);
        bool HasLandNeighbour(NativeSettlement s) =>
            s.Position.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        NativeSettlement settlement = game.NativeSettlements.First(s => (pick?.Invoke(s) ?? true) && HasLandNeighbour(s));
        Position adjacent = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        Unit scout = game.SpawnUnit(Classic.Unit(unitType), adjacent);
        scout.RoleId = ScoutRole;
        return (game, settlement, scout);
    }

    private static bool NotHatefulNorScoutTeacher(NativeSettlement s) =>
        s.AlarmLevel != AlarmLevel.Hateful && s.LearnableSkill != SeasonedScout;

    // ---- Ruleset parse ----

    [Fact]
    public void SettlementTypes_CarryAGiftsRange()
    {
        Game game = Game.New(Classic, Seed);
        Assert.All(game.NativeSettlements, s =>
        {
            SettlementGifts? gifts = Classic.Settlement(s.SettlementTypeId).Gifts;
            Assert.NotNull(gifts);
            Assert.Equal(100, gifts!.Probability);            // classic gifts always pay
            Assert.True(gifts.Factor > 0 && gifts.Maximum >= gifts.Minimum && gifts.Minimum > 0);
        });
    }

    // ---- Outcomes (the first audience with a nation is the accepted peace offer, 86d3kgbnq) ----

    [Fact]
    public void Scout_GetsBeads_FromTheSettlementTypeGiftsRange()
    {
        (Game game, NativeSettlement settlement, Unit scout) = ScoutAtSettlement(NotHatefulNorScoutTeacher);
        SettlementGifts gifts = Classic.Settlement(settlement.SettlementTypeId).Gifts!;
        int goldBefore = game.HumanPlayer.Gold;

        game.Visit(game.HumanPlayer, scout, settlement); // first contact → the offer is pending
        // Accept the peace; the scout audience then rolls. rnd 4 → not expert (≠0), not tales (>3). FreeCol's CONTINUOUS
        // gifts roll: gold = roll + Min×Factor (a non-multiple roll of 50 distinguishes it from the discrete plunder formula).
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom(4, 50));

        int expected = 50 + (gifts.Minimum * gifts.Factor);
        Assert.Equal(goldBefore + expected, game.HumanPlayer.Gold);
        Assert.True(settlement.HasBeenVisited);
    }

    [Fact]
    public void Scout_GetsTales_OnALowRoll_AndNoGold()
    {
        (Game game, NativeSettlement settlement, Unit scout) = ScoutAtSettlement(NotHatefulNorScoutTeacher);
        int goldBefore = game.HumanPlayer.Gold;
        int exploredBefore = game.HumanPlayer.Explored.Count;

        game.Visit(game.HumanPlayer, scout, settlement);
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom(1, 0)); // rnd 1 ≤ 3 → tales

        Assert.Equal(goldBefore, game.HumanPlayer.Gold);                       // no beads
        Assert.True(game.HumanPlayer.Explored.Count > exploredBefore, "tales reveal nearby lands");
    }

    [Fact]
    public void Scout_OnAZeroRoll_IsTrainedIntoASeasonedScout()
    {
        (Game game, NativeSettlement settlement, Unit scout) = ScoutAtSettlement(NotHatefulNorScoutTeacher);
        int id = scout.Id;

        game.Visit(game.HumanPlayer, scout, settlement);
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom(0)); // rnd 0 → expert training (no gift draw)

        Assert.Equal(SeasonedScout, game.Units.Single(u => u.Id == id).Type.Id);
    }

    [Fact]
    public void Scout_AtAHatefulSettlement_IsSlain()
    {
        (Game game, NativeSettlement settlement, Unit scout) = ScoutAtSettlement();
        game.ChangeNativeAlarm(settlement, NativeSettlement.MaxAlarm); // → Hateful
        int id = scout.Id;

        game.Visit(game.HumanPlayer, scout, settlement);
        // Accepting the (contrived) offer at a hateful settlement still runs the audience, which slays the scout —
        // no RNG drawn before the death (an empty ScriptedRandom would throw on a draw).
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom());

        Assert.DoesNotContain(game.Units, u => u.Id == id);
    }

    [Fact]
    public void AnExpertScout_HagglesTenPercentMoreBeads()
    {
        (Game game, NativeSettlement settlement, Unit scout) = ScoutAtSettlement(NotHatefulNorScoutTeacher, SeasonedScout);
        SettlementGifts gifts = Classic.Settlement(settlement.SettlementTypeId).Gifts!;
        int goldBefore = game.HumanPlayer.Gold;

        game.Visit(game.HumanPlayer, scout, settlement);
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom(4, 50)); // beads (continuous roll 50)

        Assert.Equal(goldBefore + ((50 + (gifts.Minimum * gifts.Factor)) * 11 / 10), game.HumanPlayer.Gold); // +10% for an expert scout
    }

    [Fact]
    public void ASecondScoutVisit_ToTheSameChief_GetsNothing_AndConsumesExactlyOneRnd()
    {
        // FreeCol InGameController:3518-3521 — on a revisit the scouting `rnd` is drawn FIRST, then hasAnyScouted()
        // short-circuits to "nothing": no gold, no skill, no extra reveal, the turn just ends.
        (Game game, NativeSettlement settlement, Unit scout) = ScoutAtSettlement(NotHatefulNorScoutTeacher);

        // First audience = the accepted first contact: a normal beads outcome (rnd 4, gift roll 50) marks the chief scouted.
        game.Visit(game.HumanPlayer, scout, settlement);
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom(4, 50));
        Assert.True(settlement.HasBeenVisitedBy(game.HumanPlayer.PlayerId));

        // The scout is allowed back (a colonist would still be refused — covered by NativeInteractionTests).
        scout.MovementLeft = 1; // give it a move again so CheckVisit passes
        Assert.True(game.CheckVisit(scout, settlement).Allowed);

        int goldBefore = game.HumanPlayer.Gold;
        int exploredBefore = game.HumanPlayer.Explored.Count;
        int id = scout.Id;

        // Revisit: the nation is already met, so it runs the audience INLINE (no offer). A SINGLE rnd is consumed
        // (an empty remainder would throw — proving exactly one draw), then "nothing".
        int gained = game.Visit(game.HumanPlayer, scout, settlement, new ScriptedRandom(7));

        Assert.Null(game.PendingFirstContact);                            // no fresh offer — nation already met
        Assert.Equal(0, gained);                                          // no gold
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
        Assert.Equal(exploredBefore, game.HumanPlayer.Explored.Count);    // no new reveal
        Assert.Equal(ScoutRole, game.Units.Single(u => u.Id == id).RoleId);
        Assert.NotEqual(SeasonedScout, game.Units.Single(u => u.Id == id).Type.Id); // not upgraded
        Assert.Equal(0, game.Units.Single(u => u.Id == id).MovementLeft);  // the audience still ends the turn
    }

    [Fact]
    public void ANonScoutColonist_StillGetsTheBasicFlatGift_NotBeads()
    {
        // A default-role colonist takes the simplified visit path (flat 10–80 gift), never the scout beads/tales/die.
        Game game = Game.New(Classic, Seed);
        bool HasLandNeighbour(NativeSettlement s) =>
            s.Position.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        NativeSettlement settlement = game.NativeSettlements.First(s => s.AlarmLevel != AlarmLevel.Hateful && HasLandNeighbour(s));
        Position adjacent = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), adjacent); // default role, NOT a scout
        int goldBefore = game.HumanPlayer.Gold;

        game.Visit(game.HumanPlayer, colonist, settlement);
        game.ResolvePendingFirstContact(accept: true, new ScriptedRandom(35)); // flat gift = the Next(10,81) roll

        Assert.Equal(goldBefore + 35, game.HumanPlayer.Gold); // the basic-visit path takes the scripted Next(GiftMin, GiftMax+1) value directly
    }
}
