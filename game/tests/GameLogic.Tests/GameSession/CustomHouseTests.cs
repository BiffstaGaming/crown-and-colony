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
/// Custom house — building/export settings + the auto-export mode (<c>86d3c9ru3</c>): the custom house grants the
/// colony the export ability (<c>model.ability.export</c>), each colony holds per-good export settings (exported +
/// retain level, default 50), and the game carries an <see cref="AutoExportMode"/> (default <c>PerGood</c>).
/// Persisted in save v28. The per-turn auto-sell is the next slice (<c>86d3c9rx2</c>).
/// </summary>
public class CustomHouseTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string CustomHouse = "model.building.customHouse";
    private const string Sugar = "model.goods.sugar";

    private static Colony FoundColony(Game game) =>
        game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));

    // ---- Building parse ----

    [Fact]
    public void CustomHouse_GrantsTheExportAbility_OtherBuildingsDont()
    {
        Assert.True(Classic.Building(CustomHouse).GrantsExport);
        Assert.False(Classic.Building("model.building.townHall").GrantsExport);
        Assert.Equal(0, Classic.Building(CustomHouse).Workplaces); // no work slots
    }

    // ---- Per-colony export settings ----

    [Fact]
    public void Colony_ExportSettings_RoundTrip_AndDefaultIsRemoved()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);

        Assert.Equal(new Colony.ExportSetting(false, 50), colony.ExportOf(Sugar)); // absent → default
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 80);
        Assert.Equal(new Colony.ExportSetting(true, 80), colony.ExportOf(Sugar));
        Assert.Contains(Sugar, colony.Exports.Keys);

        game.SetColonyExport(colony, Sugar, exported: false, exportLevel: 50); // back to default → removed
        Assert.DoesNotContain(Sugar, colony.Exports.Keys);
    }

    [Fact]
    public void SetColonyExport_AllowsFood_ButRejectsNonTradeableGoods()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        game.SetColonyExport(colony, "model.goods.food", exported: true); // FreeCol's custom house CAN export food (opt-in)
        Assert.True(colony.ExportOf("model.goods.food").Exported);
        Assert.Throws<InvalidMoveException>(() => game.SetColonyExport(colony, "model.goods.hammers", true)); // no market → not tradeable
    }

    // ---- Mode ----

    [Fact]
    public void AutoExportMode_DefaultsToPerGood()
    {
        Assert.Equal(AutoExportMode.PerGood, Game.New(Classic, Seed).AutoExportMode);
    }

    // ---- Persistence (v28) ----

    [Fact]
    public void ExportSettingsAndMode_RoundTripThroughSave_V28()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 75);
        game.SetAutoExportMode(AutoExportMode.ExportAllOverLevel);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(53, SaveGame.CurrentVersion);
        Assert.Equal(AutoExportMode.ExportAllOverLevel, restored.AutoExportMode);
        Assert.Equal(new Colony.ExportSetting(true, 75), restored.Colonies.Single(c => c.Id == colony.Id).ExportOf(Sugar));
    }

    [Fact]
    public void ADefaultGame_OmitsTheCustomHouseTokens()
    {
        string json = SaveGame.From(Game.New(Classic, Seed)).ToJson();
        Assert.DoesNotContain("AutoExportMode", json); // PerGood default omitted
        Assert.DoesNotContain("Exports", json);        // no toggled goods
    }

    // ---- Per-turn auto-sell (86d3c9rx2) ----
    //
    // Exercised end-to-end through EndTurn (the human's RunColonyTurn runs the customs sale after the warehouse
    // spill). A non-food exported good is sold down to its retain level, so its post-turn store == the level
    // regardless of how much the colony produced; gold rising proves a sale happened (a warehouse spill credits none).

    private const string Ore = "model.goods.ore";
    private const string Food = "model.goods.food";

    /// <summary>A human colony that has built a custom house (so it has the export ability), plus its game.</summary>
    private static (Game Game, Colony Colony) CustomHouseColony()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        colony.AddBuilding(CustomHouse);
        return (game, colony);
    }

    [Fact]
    public void AutoSell_WithoutACustomHouse_SellsNothing()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);            // no custom house
        game.SetColonyExport(colony, Sugar, exported: true); // flagged, but nothing acts on it
        colony.AddGoods(Sugar, 90);

        game.EndTurn();

        Assert.True(colony.StoreOf(Sugar) >= 90);     // never auto-sold down to the level (50) without the building
    }

    [Fact]
    public void AutoSell_PerGood_SellsAFlaggedGoodDownToItsLevel()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 50);
        colony.AddGoods(Sugar, 90);
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();

        Assert.Equal(50, colony.StoreOf(Sugar));          // surplus over the level is shipped to Europe
        Assert.True(game.HumanPlayer.Gold > goldBefore);  // …for gold (a spill would credit nothing)
    }

    [Fact]
    public void AutoSell_PerGood_LeavesUnflaggedGoodsAlone()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Ore, exported: true, exportLevel: 50); // ore flagged
        colony.AddGoods(Ore, 90);
        colony.AddGoods(Sugar, 90);                                         // sugar NOT flagged

        game.EndTurn();

        Assert.Equal(50, colony.StoreOf(Ore));        // the flagged good sells
        Assert.True(colony.StoreOf(Sugar) >= 90);     // the unflagged good is untouched
    }

    [Fact]
    public void AutoSell_DoesNotSellAtOrBelowTheLevel()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 100);
        colony.AddGoods(Sugar, 30);                    // below the retain level → no surplus
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();

        Assert.True(colony.StoreOf(Sugar) < 100);     // stayed below the level (nothing shipped)
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
    }

    [Fact]
    public void AutoSell_ExportAllOverLevel_SellsEverySurplus_ButProtectsFood()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetAutoExportMode(AutoExportMode.ExportAllOverLevel); // no per-good flag needed
        colony.AddGoods(Sugar, 90);                                // non-food → eligible
        colony.AddGoods(Food, 150 - colony.Food);                 // food → protected, and below the 200 growth bar
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();

        Assert.Equal(50, colony.StoreOf(Sugar));         // sold down to the default retain level
        Assert.True(colony.Food > 100);                  // food is NOT auto-dumped (growth protected)
        Assert.True(game.HumanPlayer.Gold > goldBefore);
    }

    [Fact]
    public void AutoSell_PerGood_CanExportFood_WhenExplicitlyFlagged()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Food, exported: true, exportLevel: 50); // FreeCol-faithful: food is exportable
        colony.AddGoods(Food, 150 - colony.Food);                            // below the 200 growth bar, above the level
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();

        Assert.True(colony.Food < 100);                  // food was shipped down toward the level (then eaten)
        Assert.True(game.HumanPlayer.Gold > goldBefore);
    }

    [Fact]
    public void AutoSell_FoodExport_DoesNotBlockGrowth()
    {
        // The customs sale runs AFTER eat/grow (FreeCol order), so a colony at the growth threshold still births its
        // colonist even with food flagged for export — the sale only ships whatever food is left after the birth.
        // (With the sale before growth, the food would be sold down to 50 first and the colony would never grow.)
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Food, exported: true, exportLevel: 50);
        colony.AddGoods(Food, 205 - colony.Food);        // at/over the 200 growth bar
        int popBefore = colony.Population;

        game.EndTurn();

        Assert.Equal(popBefore + 1, colony.Population);  // grew first; the food sale didn't starve growth
    }

    // ---- Sale notice (86d3c9t7z): transient per-turn feedback for the HUD ----
    //
    // The custom house sells silently to Europe — the player gets no feedback without these notices. Each sale from
    // a human colony records a CustomHouseSaleNotice (colony id/name + goods + amount + after-tax gold); the list is
    // transient (cleared at the start of every EndTurn, never saved).

    [Fact]
    public void AutoSell_RecordsASaleNotice_WithGoodsAndGold()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 50);
        colony.AddGoods(Sugar, 90);                       // 40 surplus over the level
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();

        CustomHouseSaleNotice notice = Assert.Single(game.CustomHouseSaleNotices);
        Assert.Equal(colony.Id, notice.ColonyId);
        Assert.Equal(colony.Name, notice.ColonyName);
        Assert.Equal(Sugar, notice.GoodsId);
        // At least the 40 surplus we added (90 − 50) ships; the colony may also auto-produce sugar this turn
        // (map-dependent on the start tile), which ships on top — so assert the floor, not an exact map-pinned figure.
        Assert.True(notice.Amount >= 40, $"expected the >=40 surplus to ship; was {notice.Amount}");
        Assert.True(notice.Gold > 0);
        Assert.Equal(notice.Gold, game.HumanPlayer.Gold - goldBefore); // matches the gold actually credited
    }

    [Fact]
    public void AutoSell_NoSale_ProducesNoNotice()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 100);
        colony.AddGoods(Sugar, 30);                       // below the retain level → nothing ships

        game.EndTurn();

        Assert.Empty(game.CustomHouseSaleNotices);
    }

    [Fact]
    public void AutoSell_WithoutACustomHouse_ProducesNoNotice()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);               // no custom house
        game.SetColonyExport(colony, Sugar, exported: true);
        colony.AddGoods(Sugar, 90);

        game.EndTurn();

        Assert.Empty(game.CustomHouseSaleNotices);       // no building → no sale → no notice
    }

    [Fact]
    public void AutoSell_SaleNotices_ClearAtTheStartOfEachEndTurn()
    {
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 50);
        colony.AddGoods(Sugar, 90);
        game.EndTurn();
        Assert.NotEmpty(game.CustomHouseSaleNotices);    // sold this turn

        colony.AddGoods(Sugar, -colony.StoreOf(Sugar));  // empty the warehouse so next turn ships nothing
        game.EndTurn();

        Assert.Empty(game.CustomHouseSaleNotices);       // last turn's notices were cleared; nothing new sold
    }

    // ---- Boycott smuggling (86d3e4bac): the custom house always sells boycotted goods in classic ----
    //
    // FreeCol's customIgnoreBoycott (classic default true) makes the custom house a smuggler: a boycotted good still
    // sells, with tax and price movement, no extra penalty. Critically this also fixes a latent EndTurn crash — the
    // auto-sell used to call the throwing manual-sale path on every eligible good, so a boycotted custom-house good
    // would throw InvalidMoveException during EndTurn. The fix routes the boycott decision through the option: on →
    // smuggle (sell); off → skip safely (never enter the sell path, never throw).

    [Fact]
    public void GameOption_CustomIgnoreBoycott_ParsesClassicTrue_AndSeamCanOverride()
    {
        Assert.True(Classic.GameOptions.CustomIgnoreBoycott);                              // classic ships defaultValue="true"
        Assert.True(GameOptions.ClassicDefaults.CustomIgnoreBoycott);                      // fallback matches the spec
        Assert.False(Ruleset.LoadClassic().WithCustomIgnoreBoycott(false).GameOptions.CustomIgnoreBoycott);
        Assert.True(Ruleset.LoadClassic().WithCustomIgnoreBoycott(true).GameOptions.CustomIgnoreBoycott);
    }

    [Fact]
    public void AutoSell_BoycottedGood_Smuggles_WhenCustomIgnoreBoycottOn_AndEndTurnNeverThrows()
    {
        // Classic default (on): a boycotted, exported good still sells through the custom house — and EndTurn
        // completes (the latent crash is gone).
        (Game game, Colony colony) = CustomHouseColony();
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 50);
        colony.AddGoods(Sugar, 90);                                   // 40 surplus over the level
        game.HumanPlayer.Market.SetArrears(Sugar, 5000);             // boycott the good (a tea-party-style arrears)
        Assert.False(game.HumanPlayer.Market.CanTrade(Sugar));       // confirm it is genuinely boycotted
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();                                              // must NOT throw despite the boycott

        Assert.Equal(50, colony.StoreOf(Sugar));                     // smuggled down to the retain level
        Assert.True(game.HumanPlayer.Gold > goldBefore);             // …for gold (tax still applied, price still moved)
        Assert.Equal(5000, game.HumanPlayer.Market.Arrears(Sugar));  // smuggling does not lift the boycott
    }

    [Fact]
    public void AutoSell_BoycottedGood_SkipsSafely_WhenCustomIgnoreBoycottOff_AndEndTurnNeverThrows()
    {
        // Option off: the custom house refuses a boycotted good — it is skipped (never sold), and EndTurn still
        // completes cleanly (no InvalidMoveException leaks out of the auto-sell).
        Game game = Game.New(Ruleset.LoadClassic().WithCustomIgnoreBoycott(false), Seed);
        Colony colony = FoundColony(game);
        colony.AddBuilding(CustomHouse);
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 50);
        colony.AddGoods(Sugar, 90);
        game.HumanPlayer.Market.SetArrears(Sugar, 5000);
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn();                                              // must NOT throw

        Assert.True(colony.StoreOf(Sugar) >= 90);                    // the boycotted good was left alone (not sold)
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);             // no sale → no gold
        Assert.Empty(game.CustomHouseSaleNotices);                   // nothing shipped → no notice
    }

    [Fact]
    public void AutoSell_OptionOff_StillSellsNonBoycottedGoods()
    {
        // Option off must only block the *boycotted* good — other flagged goods still sell normally.
        Game game = Game.New(Ruleset.LoadClassic().WithCustomIgnoreBoycott(false), Seed);
        Colony colony = FoundColony(game);
        colony.AddBuilding(CustomHouse);
        game.SetColonyExport(colony, Sugar, exported: true, exportLevel: 50);  // boycotted
        game.SetColonyExport(colony, Ore, exported: true, exportLevel: 50);    // freely tradeable
        colony.AddGoods(Sugar, 90);
        colony.AddGoods(Ore, 90);
        game.HumanPlayer.Market.SetArrears(Sugar, 5000);

        game.EndTurn();

        Assert.True(colony.StoreOf(Sugar) >= 90);     // boycotted sugar skipped
        Assert.Equal(50, colony.StoreOf(Ore));        // un-boycotted ore still sold down to its level
    }
}
