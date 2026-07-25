using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// WS5.3 — the First Nations relationship model (design docs
/// <c>15_First_Nations_Design_Principles.md</c> / <c>18_Diplomacy_Tension_Respect_Mechanics.md</c>;
/// <c>docs/systems/first-nations-relations.md</c>). Verifies:
/// <list type="bullet">
///   <item>classic is completely untouched — no axes, no state, no save token, and the inherited alarm model is unchanged (ADR-009);</item>
///   <item>Respect seeds at the contact baseline, moves on conduct (paying for land earns, seizing destroys) and clamps 0–100;</item>
///   <item>seizing land costs more Respect than paying earns — the asymmetry the design calls for;</item>
///   <item>Country Pressure is derived from the live footprint and <em>falls</em> when the colonist withdraws;</item>
///   <item>the seven relationship states resolve per doc 18, with Tension outranking Respect;</item>
///   <item>Country Pressure and low Respect keep pressing Tension per turn — the point of the model;</item>
///   <item>Respect round-trips a v76 save, and a classic save omits the token entirely.</item>
/// </list>
/// </summary>
public class FirstNationsRelationsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private const ulong Seed = 0xFED0A05UL;

    // ─────────────────────────────── classic is untouched (ADR-009) ───────────────────────────────

    [Fact]
    public void Classic_HasNoRelationshipModel_AndOmitsTheSaveToken()
    {
        Game game = Game.New(Classic, Seed);
        string nation = AnyNativeNation(game);

        game.ChangeFirstNationsRespect(nation, 40); // must be inert in classic

        Assert.Equal(0, game.FirstNationsRespectFor(nation));
        Assert.Equal(0, game.CountryPressureFor(nation));
        Assert.Equal(0, game.FirstNationsTensionFor(nation));
        Assert.False(game.HasContactedFirstNations(nation));
        Assert.Equal(FirstNationsRelationship.Unknown, game.RelationshipWithFirstNations(nation));
        Assert.Empty(game.FirstNationsSummary());
        Assert.Null(SaveGame.From(game).FirstNationsRespect); // omitted-when-empty → byte-identical to v75
    }

    [Fact]
    public void Classic_LandSeizure_MovesAlarmButBanksNoRespect()
    {
        // The inherited single-axis model must behave exactly as before in classic — the new axis simply does not exist.
        Game game = Game.New(Classic, Seed);
        (string nation, Position tile) = AnyOwnedTile(game);
        int alarmBefore = game.TribeTensionFor(nation, HumanId(game));

        game.ClaimLandByStealing(tile);

        Assert.True(game.TribeTensionFor(nation, HumanId(game)) > alarmBefore, "classic land-taken alarm must still fire");
        Assert.Empty(game.FirstNationsRespect);
    }

    // ─────────────────────────────── Respect ───────────────────────────────

    [Fact]
    public void Respect_SeedsAtTheContactBaseline_OnFirstMovement()
    {
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);

        game.ChangeFirstNationsRespect(nation, 5);

        // Seeded at the baseline first, then moved — not started from zero.
        Assert.Equal(Game.FirstNationsRespectBaseline + 5, game.FirstNationsRespectFor(nation));
    }

    [Fact]
    public void Respect_ClampsToZeroAndOneHundred()
    {
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);

        game.ChangeFirstNationsRespect(nation, 500);
        Assert.Equal(Game.FirstNationsAxisMax, game.FirstNationsRespectFor(nation));

        game.ChangeFirstNationsRespect(nation, -500);
        Assert.Equal(0, game.FirstNationsRespectFor(nation));
    }

    [Fact]
    public void SeizingLand_DestroysMoreRespectThanPayingEarns()
    {
        // Doc 18 lists "Seizure of land" among the sharpest Respect losses; paying is a modest gain. The asymmetry is
        // the design intent — trust is easier to destroy than to build.
        Assert.True(Game.RespectLandSeized < 0, "seizure must cost Respect");
        Assert.True(Game.RespectLandPaid > 0, "paying must earn Respect");
        Assert.True(System.Math.Abs(Game.RespectLandSeized) > Game.RespectLandPaid,
            "seizing must cost more than paying earns");
    }

    [Fact]
    public void SeizingLand_DestroysRespect_AndPayingEarnsIt()
    {
        Game seizing = NewAustralia();
        (string seizedNation, Position seizedTile) = AnyOwnedTile(seizing);
        seizing.RecordFirstNationsContact(seizedNation);
        seizing.ClaimLandByStealing(seizedTile);
        Assert.Equal(Game.FirstNationsRespectBaseline + Game.RespectLandSeized, seizing.FirstNationsRespectFor(seizedNation));

        Game paying = NewAustralia();
        (string paidNation, Position paidTile) = AnyOwnedTile(paying);
        paying.RecordFirstNationsContact(paidNation);
        paying.Players.First(p => p.IsHuman).Gold = 10_000; // afford the land price
        paying.ClaimLandByPaying(paidTile);
        Assert.Equal(Game.FirstNationsRespectBaseline + Game.RespectLandPaid, paying.FirstNationsRespectFor(paidNation));
    }

    [Fact]
    public void RecordingContact_IsIdempotent_AndNeverLaundersDestroyedTrust()
    {
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);
        game.RecordFirstNationsContact(nation);
        game.ChangeFirstNationsRespect(nation, -30);
        int damaged = game.FirstNationsRespectFor(nation);

        game.RecordFirstNationsContact(nation); // re-contact must NOT reset the record

        Assert.Equal(damaged, game.FirstNationsRespectFor(nation));
    }

    // ─────────────────────────────── Country Pressure (derived) ───────────────────────────────

    [Fact]
    public void CountryPressure_RisesWithTheColonialFootprint_AndFallsWhenItWithdraws()
    {
        Game game = NewAustralia();
        (string nation, Position tile) = AnyOwnedTile(game);
        NativeSettlement settlement = game.NativeSettlements.First(s => s.NationTypeId == nation);

        Assert.Equal(0, game.CountryPressureFor(nation)); // nothing built yet

        Colony colony = FoundNear(game, settlement.Position);
        colony.Population = 30;
        int pressed = game.CountryPressureFor(nation);
        Assert.True(pressed > 0, "a grown colony on their Country must register as pressure");

        colony.Population = 1; // the footprint shrinks
        Assert.True(game.CountryPressureFor(nation) < pressed,
            "Country Pressure is derived, so withdrawing must relieve it");
    }

    // ─────────────────────────────── relationship states (doc 18 table) ───────────────────────────────

    [Fact]
    public void UncontactedPeople_AreUnknown()
    {
        Game game = NewAustralia();
        string nation = UncontactedNation(game);

        Assert.Equal(FirstNationsRelationship.Unknown, game.RelationshipWithFirstNations(nation));
    }

    [Theory]
    [InlineData(35, FirstNationsRelationship.CautiousContact)]   // contacted, below the trade bar
    [InlineData(45, FirstNationsRelationship.TradeRelationship)]
    [InlineData(60, FirstNationsRelationship.AgreementRelationship)]
    [InlineData(80, FirstNationsRelationship.TrustedRelationship)]
    public void RespectBands_ResolveToTheDesignedStates_WhenTensionIsLow(int respect, FirstNationsRelationship expected)
    {
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);
        game.RecordFirstNationsContact(nation);
        SetRespect(game, nation, respect);

        Assert.Equal(expected, game.RelationshipWithFirstNations(nation));
    }

    [Fact]
    public void Tension_OutranksRespect_StrainedThenHostile()
    {
        // The whole point of two axes: a people pushed far enough is Strained/Hostile however much trust was banked.
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);
        game.RecordFirstNationsContact(nation);
        SetRespect(game, nation, 100); // maximum trust…

        SetTensionPercent(game, nation, 65);
        Assert.Equal(FirstNationsRelationship.Strained, game.RelationshipWithFirstNations(nation));

        SetTensionPercent(game, nation, 85);
        Assert.Equal(FirstNationsRelationship.Hostile, game.RelationshipWithFirstNations(nation));
    }

    // ─────────────────────────────── the per-turn pressure coupling ───────────────────────────────

    [Fact]
    public void CountryPressure_KeepsRaisingTension_EachTurn()
    {
        // Under the inherited model a colonist could sit on Country indefinitely with no adjacent unit and alarm would
        // not climb. Now the footprint itself keeps pressing.
        Game game = NewAustralia();
        (string nation, _) = AnyOwnedTile(game);
        NativeSettlement settlement = game.NativeSettlements.First(s => s.NationTypeId == nation);
        game.RecordFirstNationsContact(nation);
        Colony colony = FoundNear(game, settlement.Position);
        colony.Population = 60;

        int before = game.FirstNationsTensionFor(nation);
        game.ApplyFirstNationsPressureTension();
        int after = game.FirstNationsTensionFor(nation);

        Assert.True(game.CountryPressureFor(nation) > 0, "the test needs a real footprint to be meaningful");
        Assert.True(after > before, $"Country Pressure must feed Tension ({after} vs {before})");
    }

    [Fact]
    public void UnpressedPeople_GainNoTension()
    {
        // The pass must never invent free-floating tension: a people with no colonial footprint on their Country is
        // left exactly where they were, however many turns pass.
        // (Note: founding a colony beside a community also EXPLORES it, which is itself contact — so an "uncontacted
        // but heavily pressed" board is not actually reachable. Zero-footprint is the honest invariant to guard.)
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);
        game.RecordFirstNationsContact(nation);

        Assert.Equal(0, game.CountryPressureFor(nation));
        int before = game.FirstNationsTensionFor(nation);
        game.ApplyFirstNationsPressureTension();

        Assert.Equal(before, game.FirstNationsTensionFor(nation));
    }

    // ─────────────────────────────── persistence (v76) ───────────────────────────────

    [Fact]
    public void Respect_RoundTripsThroughASave()
    {
        Game game = NewAustralia();
        string nation = AnyNativeNation(game);
        game.RecordFirstNationsContact(nation);
        game.ChangeFirstNationsRespect(nation, 22);
        int expected = game.FirstNationsRespectFor(nation);

        SaveGame save = SaveGame.From(game);
        Assert.NotNull(save.FirstNationsRespect);
        Assert.Equal(76, save.Version);

        Game restored = save.Restore(Australia);
        Assert.Equal(expected, restored.FirstNationsRespectFor(nation));
    }

    [Fact]
    public void AustraliaGameWithNoContact_OmitsTheToken()
    {
        // Omitted-when-empty: an Australia game that has met nobody must not grow a token either (ADR-009 discipline).
        Game game = NewAustralia();

        Assert.Null(SaveGame.From(game).FirstNationsRespect);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static Game NewAustralia() => Game.New(Australia, Seed, mapSource: MapSource.Australia);

    private static int HumanId(Game game) => game.Players.First(p => p.IsHuman).PlayerId;

    private static string AnyNativeNation(Game game) => game.NativeSettlements.First().NationTypeId;

    /// <summary>A nation whose settlements the human has not explored, so it reads as uncontacted.</summary>
    private static string UncontactedNation(Game game) =>
        game.NativeSettlements
            .Select(s => s.NationTypeId)
            .Distinct()
            .First(id => !game.HasContactedFirstNations(id));

    /// <summary>A native-owned tile plus the nation that owns it.</summary>
    private static (string Nation, Position Tile) AnyOwnedTile(Game game)
    {
        foreach (NativeSettlement settlement in game.NativeSettlements)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    var tile = new Position(settlement.Position.X + dx, settlement.Position.Y + dy);
                    if (game.Map.InBounds(tile) && game.Map.NativeOwnerOf(tile) is { } owner && game.NativeSettlementAt(tile) is null)
                    {
                        return (owner, tile);
                    }
                }
            }
        }
        throw new System.InvalidOperationException("no native-owned tile found on this board");
    }

    /// <summary>Founds a colony on a land tile near <paramref name="origin"/> (stealing the land if it is owned, so the helper never throws).</summary>
    private static Colony FoundNear(Game game, Position origin)
    {
        for (int radius = 1; radius <= 4; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var tile = new Position(origin.X + dx, origin.Y + dy);
                    if (!game.Map.InBounds(tile) || game.Map.TerrainAt(tile).IsWater || game.NativeSettlementAt(tile) is not null)
                    {
                        continue;
                    }
                    Units.Unit colonist = game.SpawnUnit(Australia.Unit(Colony.FreeColonistTypeId), tile);
                    try
                    {
                        return game.FoundColony(colonist, LandClaimChoice.Steal);
                    }
                    catch (System.Exception)
                    {
                        // tile unusable (too close to another colony, etc.) — try the next one
                    }
                }
            }
        }
        throw new System.InvalidOperationException("no foundable tile found near the settlement");
    }

    private static void SetRespect(Game game, string nation, int respect)
    {
        game.ChangeFirstNationsRespect(nation, -Game.FirstNationsAxisMax); // floor it
        game.ChangeFirstNationsRespect(nation, respect);
    }

    /// <summary>
    /// Drives a nation's tension toward the human so <see cref="Game.FirstNationsTensionFor"/> reads about
    /// <paramref name="percent"/>. Moves the <b>nation-level</b> channel (which is what FirstNationsTensionFor reads),
    /// not the per-settlement alarm accumulator.
    /// </summary>
    private static void SetTensionPercent(Game game, string nation, int percent)
    {
        int target = percent * 1100 / 100; // MaxTension is 1100
        game.RaiseTribeTension(nation, HumanId(game), target - game.TribeTensionFor(nation, HumanId(game)));
    }
}
