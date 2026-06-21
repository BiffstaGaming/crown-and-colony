using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Per-turn building gold upkeep (<c>86d3drmzk</c>, FreeCol <c>Colony.getUpkeep</c> → <c>ServerPlayer.csPayUpkeep</c>):
/// each colony's Σ <see cref="BuildingType.Upkeep"/> is deducted from its owner's gold each turn — but ONLY when the
/// ruleset's <c>model.option.enableUpkeep</c> game option is on. The classic ruleset ships it <b>off</b>, so the
/// default classic game charges no upkeep (its economy is unchanged); these tests assert both the off (classic) path
/// and the on path, the latter via a classic spec with the option flipped to <c>true</c>.
/// </summary>
public class BuildingUpkeepTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 3UL;
    private const string LumberMill = "model.building.lumberMill"; // upkeep 10
    private const string BlacksmithShop = "model.building.blacksmithShop"; // upkeep 5

    /// <summary>The classic ruleset with the <c>enableUpkeep</c> game option flipped on (everything else identical).</summary>
    private static readonly Ruleset UpkeepOn = LoadClassicWithUpkeepEnabled();

    /// <summary>
    /// Loads the embedded classic spec, flips <c>model.option.enableUpkeep</c> from its <c>defaultValue="false"</c> to
    /// <c>true</c> (via the parsed XML, so whitespace is irrelevant), and re-parses it — a ruleset identical to classic
    /// except that upkeep is charged.
    /// </summary>
    private static Ruleset LoadClassicWithUpkeepEnabled()
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement option = doc.Descendants("booleanOption")
            .Single(o => (string?)o.Attribute("id") == "model.option.enableUpkeep");
        option.SetAttributeValue("defaultValue", "true");
        option.SetAttributeValue("value", "true");
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    /// <summary>Founds a fresh colony for the human player, with the given extra buildings added (and ample food).</summary>
    private static Colony FoundColonyWith(Game game, params string[] extraBuildings)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (string buildingId in extraBuildings)
        {
            if (!colony.HasBuilding(buildingId))
            {
                colony.AddBuilding(buildingId);
            }
        }
        colony.AddGoods("model.goods.food", 100); // no starvation perturbing the turn
        return colony;
    }

    [Fact]
    public void EnableUpkeepOff_IsTheClassicDefault()
    {
        Assert.False(Classic.UpkeepEnabled); // classic ships model.option.enableUpkeep defaultValue="false"
        Assert.True(UpkeepOn.UpkeepEnabled); // the flipped spec turns it on
    }

    [Fact]
    public void ClassicDefault_ChargesNoUpkeep_GoldUnchanged()
    {
        Game game = Game.New(Classic, Seed);
        FoundColonyWith(game, LumberMill); // a building that WOULD cost 10/turn if upkeep were on
        game.HumanPlayer.Gold = 500;
        game.EndTurn();
        Assert.Equal(500, game.HumanPlayer.Gold); // upkeep off in classic → no deduction
    }

    [Fact]
    public void UpkeepEnabled_DeductsTheColonysTotalBuildingUpkeepEachTurn()
    {
        Game game = Game.New(UpkeepOn, Seed);
        Colony colony = FoundColonyWith(game, LumberMill, BlacksmithShop);
        int expectedUpkeep = colony.Buildings.Sum(b => UpkeepOn.Building(b).Upkeep);
        Assert.Equal(15, expectedUpkeep); // lumber mill 10 + blacksmith shop 5 (the free base buildings are upkeep 0)

        game.HumanPlayer.Gold = 500;
        game.EndTurn();
        Assert.Equal(500 - expectedUpkeep, game.HumanPlayer.Gold);
    }

    [Fact]
    public void UpkeepEnabled_FloorsGoldAtZero_WhenThePlayerCannotPay()
    {
        // TODO(86d3c9ux4): FreeCol applies a bankruptcy production penalty here; we only floor gold at 0 for now.
        Game game = Game.New(UpkeepOn, Seed);
        FoundColonyWith(game, LumberMill); // 10/turn upkeep, more than the treasury below
        game.HumanPlayer.Gold = 3;
        game.EndTurn();
        Assert.Equal(0, game.HumanPlayer.Gold); // never goes negative
    }

    [Fact]
    public void Upkeep_ParsedFromTheSpec_AndResolvedUpTheExtendsChain()
    {
        Assert.Equal(10, Classic.Building(LumberMill).Upkeep);
        Assert.Equal(5, Classic.Building(BlacksmithShop).Upkeep);
        Assert.Equal(0, Classic.Building("model.building.carpenterHouse").Upkeep); // base house: no upkeep attribute → 0
    }
}
