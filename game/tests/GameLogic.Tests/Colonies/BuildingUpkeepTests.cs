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
        Game game = Game.New(UpkeepOn, Seed);
        FoundColonyWith(game, LumberMill); // 10/turn upkeep, more than the treasury below
        game.HumanPlayer.Gold = 3;
        game.EndTurn();
        Assert.Equal(0, game.HumanPlayer.Gold); // never goes negative
    }

    // ===== Bankruptcy (86d3c9ux4): a player who cannot pay building upkeep goes bankrupt — a transient flag (never
    // persisted) that halves every colony's building production until they can pay again (FreeCol
    // model.disaster.bankruptcy −50% lossOfBuildingProduction). Off in classic (upkeep disabled → never bankrupt).

    [Fact]
    public void ClassicDefault_PlayerIsNeverBankrupt()
    {
        Game game = Game.New(Classic, Seed);
        FoundColonyWith(game, LumberMill);
        game.HumanPlayer.Gold = 0; // even with an empty purse, classic charges no upkeep
        game.EndTurn();
        Assert.False(game.HumanPlayer.Bankrupt); // upkeep off → bankruptcy can never strike
    }

    [Fact]
    public void UpkeepEnabled_GoesBankrupt_WhenUpkeepUnpayable_AndRecovers_WhenPayable()
    {
        Game game = Game.New(UpkeepOn, Seed);
        FoundColonyWith(game, LumberMill); // 10/turn upkeep
        game.HumanPlayer.Gold = 3;         // can't cover the 10 bill
        game.EndTurn();
        Assert.True(game.HumanPlayer.Bankrupt); // unpayable → bankrupt
        Assert.Equal(0, game.HumanPlayer.Gold);

        game.HumanPlayer.Gold = 1000; // now solvent
        game.EndTurn();
        Assert.False(game.HumanPlayer.Bankrupt); // payable → bankruptcy lifted
    }

    [Fact]
    public void Bankruptcy_HalvesBuildingProduction()
    {
        const string Hammers = "model.goods.hammers";
        const string Lumber = "model.goods.lumber";

        // Solvent baseline: a free colonist in the carpenter's house with plenty of lumber, one penalty-free turn.
        Game solvent = Game.New(UpkeepOn, Seed);
        Colony okColony = StaffedCarpenter(solvent, gold: 1000, lumber: 100);
        solvent.EndTurn();
        int solventHammers = okColony.StoreOf(Hammers);
        Assert.True(solventHammers > 0); // the carpenter made hammers

        // Bankrupt run: identical colony, but the player can't pay upkeep, so it is bankrupt by the time buildings
        // produce next turn. Turn 1 declares bankruptcy (after production); turn 2 produces under the penalty.
        Game broke = Game.New(UpkeepOn, Seed);
        Colony brokeColony = StaffedCarpenter(broke, gold: 0, lumber: 100);
        broke.EndTurn(); // bankruptcy declared this turn (production already ran at full rate)
        Assert.True(broke.HumanPlayer.Bankrupt);
        brokeColony.AddGoods(Lumber, 100); // top the carpenter up for the penalised turn
        int before = brokeColony.StoreOf(Hammers);
        broke.EndTurn(); // now produces under the bankruptcy penalty
        int penalised = brokeColony.StoreOf(Hammers) - before;

        Assert.True(penalised > 0);
        Assert.Equal(solventHammers / 2, penalised); // −50% building production while bankrupt
    }

    /// <summary>
    /// Founds a colony, pulls the founder off the fields into the carpenter's house (so only its hammer production
    /// runs), adds a blacksmith shop purely to charge a non-zero upkeep (5/turn — the carpenter's house itself is
    /// upkeep 0, so without this an empty purse would not trigger bankruptcy), and stocks lumber + food.
    /// </summary>
    private static Colony StaffedCarpenter(Game game, int gold, int lumber)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile); // free the founder from the fields so only building work runs
        }
        if (!colony.HasBuilding(BlacksmithShop))
        {
            colony.AddBuilding(BlacksmithShop); // upkeep 5 — gives bankruptcy something to be unable to pay
        }
        colony.Population = 1;
        colony.AssignBuildingWorker("model.building.carpenterHouse", "model.unit.freeColonist");
        colony.AddGoods("model.goods.food", 100); // no starvation perturbing the worker count
        colony.AddGoods("model.goods.lumber", lumber);
        game.HumanPlayer.Gold = gold;
        return colony;
    }

    [Fact]
    public void Upkeep_ParsedFromTheSpec_AndResolvedUpTheExtendsChain()
    {
        Assert.Equal(10, Classic.Building(LumberMill).Upkeep);
        Assert.Equal(5, Classic.Building(BlacksmithShop).Upkeep);
        Assert.Equal(0, Classic.Building("model.building.carpenterHouse").Upkeep); // base house: no upkeep attribute → 0
    }
}
