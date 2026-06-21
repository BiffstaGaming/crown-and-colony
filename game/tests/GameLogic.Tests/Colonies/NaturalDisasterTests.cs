using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Natural disasters (<c>86d3c9uu8</c>, FreeCol <c>Disaster.java</c> + <c>ServerPlayer.csNaturalDisasters</c>): the
/// data model parsed from the spec's <c>&lt;disasters&gt;</c> block, plus the per-turn per-player roll. The roll
/// fires only when the ruleset's <c>model.option.naturalDisasters</c> percentage is above 0; the classic ruleset
/// ships it <b>0</b>, so the default classic game rolls no disasters (its economy is unchanged and its stream 0 is
/// never advanced). These tests assert the parse, the default-off path, and the on path (a spec with the option
/// forced to 100%).
/// </summary>
public class NaturalDisasterTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 7UL;

    /// <summary>The classic ruleset with <c>model.option.naturalDisasters</c> forced to 100% (everything else identical).</summary>
    private static readonly Ruleset DisastersAlways = LoadClassicWithDisasterChance(100);

    private static Ruleset LoadClassicWithDisasterChance(int percent)
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement option = doc.Descendants("percentageOption")
            .Single(o => (string?)o.Attribute("id") == "model.option.naturalDisasters");
        option.SetAttributeValue("defaultValue", percent.ToString());
        option.SetAttributeValue("value", percent.ToString());
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private static Colony FoundColony(Game game)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.AddGoods("model.goods.food", 100);
        return colony;
    }

    // ===== Data model =====

    [Fact]
    public void Classic_ParsesTheDisasters_IncludingBankruptcy_ResolvingTheExtendsChain()
    {
        // The bankruptcy disaster is special (not natural) and carries a building-production-penalty effect.
        Disaster? bankruptcy = Classic.FindDisaster(Disaster.BankruptcyId);
        Assert.NotNull(bankruptcy);
        Assert.False(bankruptcy!.Natural);

        // Flood extends the abstract model.disaster.common, so it inherits its effect list (the common effect set).
        Disaster? flood = Classic.FindDisaster("model.disaster.flood");
        Assert.NotNull(flood);
        Assert.True(flood!.Natural);
        Assert.NotEmpty(flood.Effects); // inherited from the common parent via extends

        // The abstract parent is never instantiated.
        Assert.Null(Classic.FindDisaster("model.disaster.common"));
    }

    [Fact]
    public void Classic_NaturalDisasters_AreTheNaturalOnes_AndExcludeBankruptcy()
    {
        Assert.NotEmpty(Classic.NaturalDisasters);
        Assert.All(Classic.NaturalDisasters, d => Assert.True(d.Natural));
        Assert.DoesNotContain(Classic.NaturalDisasters, d => d.Id == Disaster.BankruptcyId);
    }

    [Fact]
    public void Classic_NaturalDisasterPercentage_IsZeroByDefault()
    {
        Assert.Equal(0, Classic.NaturalDisasterPercentage);     // classic ships defaultValue="0"
        Assert.Equal(100, DisastersAlways.NaturalDisasterPercentage); // the forced spec
    }

    // ===== The roll =====

    [Fact]
    public void ClassicDefault_RollsNoDisasters()
    {
        Game game = Game.New(Classic, Seed);
        FoundColony(game);
        game.HumanPlayer.Gold = 1000;
        for (int i = 0; i < 10; i++)
        {
            game.EndTurn();
        }
        Assert.Empty(game.DisasterNotices); // option 0 → no roll ever fires
    }

    [Fact]
    public void DisasterChance100_StrikesAColony()
    {
        Game game = Game.New(DisastersAlways, Seed);
        Colony colony = FoundColony(game);
        // Stock gold + a goods stack so a loss-of-money / loss-of-goods effect has something to take.
        game.HumanPlayer.Gold = 1000;
        colony.AddGoods("model.goods.tobacco", 100);

        // At 100% a disaster is rolled every turn; over a few turns at least one effect must land on the colony.
        bool struck = false;
        for (int i = 0; i < 10 && !struck; i++)
        {
            game.EndTurn();
            struck = game.DisasterNotices.Count > 0;
        }
        Assert.True(struck);
        DisasterNotice notice = game.DisasterNotices[0];
        Assert.Equal(colony.Name, notice.ColonyName);
        // Some effect fired: gold lost, goods lost, or a production-penalty note.
        Assert.True(notice.GoldLost > 0 || notice.GoodsLost > 0 || notice.ProductionPenaltyApplied);
    }
}
