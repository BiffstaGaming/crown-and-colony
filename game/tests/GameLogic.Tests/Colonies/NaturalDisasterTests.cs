using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
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

    // ===== Production penalty (86d3fpyu9): a timed −50% on the struck colony's production, 3 turns =====

    private const string Sugar = "model.goods.sugar";
    private const string Rum = "model.goods.rum";
    private const string Free = "model.unit.freeColonist";

    /// <summary>The classic ruleset with the natural disaster forced to fire ONLY its production-penalty effects (tile +
    /// building), every other effect disabled — so a strike deterministically registers the timed production penalty.</summary>
    private static Ruleset LoadClassicWithOnlyProductionPenaltyDisaster()
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        // naturalDisasters on.
        XElement opt = doc.Descendants("percentageOption").Single(o => (string?)o.Attribute("id") == "model.option.naturalDisasters");
        opt.SetAttributeValue("defaultValue", "100");
        opt.SetAttributeValue("value", "100");
        // In the shared "several" effect list, force the two production-penalty effects to 100% and every other to 0%.
        foreach (XElement effect in doc.Descendants("effect"))
        {
            string id = (string?)effect.Attribute("id") ?? "";
            bool isProductionPenalty = id is "model.disaster.effect.lossOfTileProduction" or "model.disaster.effect.lossOfBuildingProduction";
            effect.SetAttributeValue("probability", isProductionPenalty ? "100" : "0");
        }
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private static Position TileProducing(Game game, string goods) =>
        game.Map.AllPositions().First(p => game.TileYieldPotential(p, goods) > 0);

    [Fact]
    public void ProductionPenalty_HalvesTheStruckColonysTileYield_ForThreeTurnsThenRecovers()
    {
        // Register the disaster penalty exactly as ApplyDisaster does: a colony-scoped timed −50% on sugar for 3 turns,
        // at DISASTER_PRODUCTION_INDEX (100). Assert the colony's tile yield drops ~50% while the window is open and
        // returns to full once it expires. Uses the colony-scoped TileYield overload the live colony turn now calls.
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        Position tile = TileProducing(game, Sugar);
        int full = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);
        Assert.True(full > 0, "need a tile that actually produces sugar");

        // FreeCol makeTimedModifier at the strike turn: window [Turn, Turn+2] — active for three colony turns.
        var payload = new FatherModifier(Sugar, ModifierType.Percentage, -50, 100);
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(payload, duration: 3, start: game.Turn, colonyId: colony.Id));

        int penalised = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);
        Assert.Equal(full - full / 2, penalised); // −50%, truncated (matches FreeCol's (int) cast)

        // Advance to the last active turn (start+2): still penalised.
        game.EndTurn(); // → start+1
        game.EndTurn(); // → start+2 (lastTurn) — still in window
        Assert.Equal(full - full / 2, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id));

        game.EndTurn(); // → start+3: the per-turn strip removes it
        Assert.Empty(game.TemporaryModifiers);
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id)); // recovered
    }

    [Fact]
    public void ProductionPenalty_OnlyHitsTheStruckColony_NotAnotherColonyNorNonColonyFolds()
    {
        // The penalty is registered against ONE colony's id. A query for a DIFFERENT colony (or a non-colony fold —
        // null id) must see full production: FreeCol scopes the disaster modifier to the struck colony only.
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        Position tile = TileProducing(game, Sugar);
        int full = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);

        int otherColonyId = colony.Id + 999; // an id no colony has → stands in for a different colony
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(
            new FatherModifier(Sugar, ModifierType.Percentage, -50, 100), duration: 3, start: game.Turn, colonyId: colony.Id));

        Assert.Equal(full - full / 2, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id)); // struck colony: penalised
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar, otherColonyId)); // another colony: untouched
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar)); // non-colony fold (null id): untouched
    }

    [Fact]
    public void ApplyDisaster_WiresTheProductionPenalty_AsAColonyScopedTimedModifier()
    {
        // End-to-end: with a spec whose natural disaster fires ONLY the production-penalty effects, a strike registers
        // colony-scoped timed modifiers (the -50% tile/building goods) and the notice flags the production penalty.
        Ruleset onlyPenalty = LoadClassicWithOnlyProductionPenaltyDisaster();
        Game game = Game.New(onlyPenalty, Seed);
        Colony colony = FoundColony(game);
        game.HumanPlayer.Gold = 1000;

        bool penaltyNoted = false;
        for (int i = 0; i < 10 && !penaltyNoted; i++)
        {
            game.EndTurn();
            penaltyNoted = game.DisasterNotices.Any(n => n.ProductionPenaltyApplied);
        }
        Assert.True(penaltyNoted, "the forced production-penalty disaster should have applied");

        // The struck colony now carries at least one colony-scoped timed penalty modifier (−50% on a good).
        var scoped = game.TemporaryModifiers.Where(m => m.ColonyId == colony.Id).ToList();
        Assert.NotEmpty(scoped);
        Assert.All(scoped, m => Assert.Equal(-50, m.Payload.Value));
        Assert.All(scoped, m => Assert.Equal(ModifierType.Percentage, m.Payload.Type));
    }
}
