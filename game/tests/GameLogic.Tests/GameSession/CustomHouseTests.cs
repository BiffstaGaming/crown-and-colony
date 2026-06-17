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

        Assert.Equal(28, SaveGame.CurrentVersion);
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
}
