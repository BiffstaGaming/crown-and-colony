using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The Australian Federation variant's <b>novel content</b> (Phase-4d.2–4d.6): the new economy/civic buildings and
/// pioneer tile-improvements authored in <c>australia/specification.xml</c>, plus the two bespoke on-election
/// handlers whose designed mechanic is genuinely new — Edward Hargraves' "Payable Gold" gold rush (a Gold-deposit
/// reveal + immigration surge) and Arthur Phillip's "Survival Rations" (emergency Food + Tools to the first
/// settlement). Every id here is Australia-only: the classic ruleset declares none of it and replays byte-identically
/// (the soak gate proves that separately). A clean load of the Australia ruleset proves the new XML parses — a
/// malformed building/improvement id fails the ruleset load, so the parse assertions below double as validity checks.
/// Sources: docs/australian_federation_mode_md/17 (goods/buildings/improvements) and 08/10 (the Pioneers).
/// </summary>
public class AustralianContentTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Hargraves = "model.foundingFather.edwardHargraves";
    private const string Phillip = "model.foundingFather.arthurPhillip";
    private const string GoldResource = "model.resource.silver"; // silver = the reskin's Gold stand-in
    private const string FoodId = "model.goods.food";
    private const string ToolsId = "model.goods.tools";

    // ───────────────────────── new buildings (4d.2 / 4d.3) parse + are available ─────────────────────────

    [Theory]
    // Economy (4d.2)
    [InlineData("model.building.goldfieldsOffice")]
    [InlineData("model.building.freezingWorks")]
    [InlineData("model.building.railDepot")]
    [InlineData("model.building.telegraphOffice")]
    // Civic (4d.3)
    [InlineData("model.building.federationLeagueHall")]
    [InlineData("model.building.conventionHall")]
    [InlineData("model.building.harbourBattery")]
    public void NewAustralianBuildings_ParseIntoTheAustraliaRuleset_AndAreAbsentFromClassic(string buildingId)
    {
        // Parses + is available in the Australia ruleset…
        BuildingType building = Australia.Building(buildingId);
        Assert.Equal(buildingId, building.Id);

        // …and is Australia-only — classic knows nothing of it (byte-identical guard).
        Assert.DoesNotContain(Classic.BuildingTypes, b => b.Id == buildingId);
    }

    [Fact]
    public void GoldfieldsOffice_IsGatedOnHargraves_GoldRushUnlockAbility()
    {
        // The office can only be built once Hargraves' election grants model.ability.buildGoldfieldsOffice.
        BuildingType office = Australia.Building("model.building.goldfieldsOffice");
        Assert.True(office.RequiredAbilitiesOrEmpty.GetValueOrDefault("model.ability.buildGoldfieldsOffice"));
        Assert.Contains(Australia.Father(Hargraves).Abilities,
            a => a.Id == "model.ability.buildGoldfieldsOffice" && a.Value);
    }

    [Fact]
    public void HarbourBattery_FortifiesTheColony_AndBombardsShips()
    {
        BuildingType battery = Australia.Building("model.building.harbourBattery");
        Assert.Equal(75, battery.DefenceBonus);                       // +75% colony defence
        Assert.True(battery.BombardsShips);                            // coastal guns fire on passing ships
        Assert.True(battery.RequiredAbilitiesOrEmpty.GetValueOrDefault("model.ability.hasPort")); // coastal only
    }

    [Fact]
    public void CivicBuildings_BoostCivicVoice_TelegraphAndLeagueHallOnTheBellsSeam()
    {
        // Telegraph Office +50% Civic Voice (bells); Federation League Hall +25% — the printing-press seam.
        Assert.Equal(50, Australia.Building("model.building.telegraphOffice").BellBonus);
        Assert.Equal(25, Australia.Building("model.building.federationLeagueHall").BellBonus);
        // Convention Hall is a workplace building that produces bells directly (the drafting chamber).
        Assert.Contains(Australia.Building("model.building.conventionHall").Productions,
            p => p.Outputs.Any(o => o.GoodsId == "model.goods.bells"));
    }

    // ───────────────────────── new tile improvements (4d.4) parse + are available ─────────────────────────

    [Theory]
    [InlineData("model.improvement.goldfield", "model.goods.silver")]  // +Gold
    [InlineData("model.improvement.stockRoute", "model.goods.cotton")] // +Wool (cotton stand-in)
    [InlineData("model.improvement.telegraphLine", "model.goods.bells")] // +Civic Voice
    [InlineData("model.improvement.rail", "model.goods.ore")]          // +Ore
    public void NewAustralianImprovements_ParseIntoTheAustraliaRuleset_WithTheirYieldModifier(
        string improvementId, string yieldGoodsId)
    {
        var improvement = Australia.Improvement(improvementId);
        Assert.Equal(improvementId, improvement.Id);
        Assert.Equal("model.role.pioneer", improvement.RequiredRoleId); // pioneer-built, expends a tool load
        Assert.Contains(improvement.Modifiers, m => m.GoodsId == yieldGoodsId);

        // Australia-only — classic has no such improvement.
        Assert.DoesNotContain(Classic.ImprovementTypes, i => i.Id == improvementId);
    }

    [Fact]
    public void AgreementGatedImprovements_AreDeferred_NotYetPresent()
    {
        // Waterhole Camp / Eel Trap depend on the First Nations 4b agreement system (not built) — deliberately absent.
        Assert.DoesNotContain(Australia.ImprovementTypes, i => i.Id == "model.improvement.waterholeCamp");
        Assert.DoesNotContain(Australia.ImprovementTypes, i => i.Id == "model.improvement.eelTrap");
    }

    // ───────────────────────── Hargraves' "Payable Gold" (4d.5) ─────────────────────────

    [Fact]
    public void Hargraves_CarriesTheGoldRushMarker_AndTheStandingGoldBoost()
    {
        FoundingFather hargraves = Australia.Father(Hargraves);
        Assert.Contains(hargraves.Abilities, a => a.Id == "model.ability.goldRush" && a.Value);
        // The standing +100% Gold (silver stand-in) production modifier rides on unchanged.
        Assert.Contains(hargraves.Modifiers, m => m.TargetId == "model.goods.silver" && m.Value == 100);
    }

    [Fact]
    public void ElectingHargraves_RevealsGoldDeposits_OnExploredElevationTilesNearColonies()
    {
        Game game = GoldRushGame(currentFather: Hargraves, liberty: 45); // ≥ first father cost (40)
        int goldBefore = CountGold(game);
        Assert.Equal(0, goldBefore); // no gold placed on the seed map

        game.EndTurn(); // liberty 45 ≥ 40 → Hargraves elected → the gold rush fires

        Assert.Contains(Hargraves, game.Congress);
        int goldAfter = CountGold(game);
        Assert.InRange(goldAfter, 2, 4); // doc 08: 2–4 Gold deposits revealed

        // Every deposit landed on an explored, dry ELEVATION tile the player owns fog over (hills/mountains).
        foreach (Position p in game.Map.Resources.Where(kv => kv.Value == GoldResource).Select(kv => kv.Key))
        {
            Assert.True(game.Map.TerrainAt(p).IsElevation, $"gold at {p} is not on elevation terrain");
        }
    }

    [Fact]
    public void ElectingHargraves_TriggersAnImmigrationSurge()
    {
        // A surge banks a full recruit's worth of immigration; on a fresh game with a stocked dock that tips at least
        // one emigrant across (immigration bar filled), which the election-turn loop then converts.
        Game game = GoldRushGame(currentFather: Hargraves, liberty: 45);
        int required = game.ImmigrationRequired;
        Assert.True(required > 0);

        game.EndTurn();
        Assert.Contains(Hargraves, game.Congress);
        // The surge (one full bar) guarantees measurable immigration progress this turn — either the bar was consumed
        // by an emigrant (immigration reset toward 0) or it is banked; in both cases the surge moved the needle.
        // Assert an emigrant was produced: the required threshold rose by the per-emigrant increment.
        Assert.True(game.ImmigrationRequired > required,
            "the gold-rush surge should have produced at least one emigrant, raising the next threshold");
    }

    [Fact]
    public void GoldRush_IsDeterministic_SameSeedSameDeposits()
    {
        var a = GoldRushGame(currentFather: Hargraves, liberty: 45);
        var b = GoldRushGame(currentFather: Hargraves, liberty: 45);
        a.EndTurn();
        b.EndTurn();
        Assert.Equal(
            a.Map.Resources.Where(kv => kv.Value == GoldResource).Select(kv => kv.Key).OrderBy(p => p.Y).ThenBy(p => p.X),
            b.Map.Resources.Where(kv => kv.Value == GoldResource).Select(kv => kv.Key).OrderBy(p => p.Y).ThenBy(p => p.X));
    }

    // ───────────────────────── Arthur Phillip's "Survival Rations" (4d.6) ─────────────────────────

    [Fact]
    public void Phillip_CarriesTheSurvivalRationsMarker_AlongsideHisAutoEquipPayload()
    {
        FoundingFather phillip = Australia.Father(Phillip);
        Assert.Contains(phillip.Abilities, a => a.Id == "model.ability.survivalRations" && a.Value);
        Assert.Contains(phillip.Abilities, a => a.Id == "model.ability.automaticEquipment" && a.Value);
    }

    [Fact]
    public void ElectingPhillip_SuppliesTheFirstSettlement_WithEmergencyFoodAndTools()
    {
        Game game = RationsGame(currentFather: Phillip, liberty: 45);
        int foodBefore = game.Colonies[0].StoreOf(FoodId);
        int toolsBefore = game.Colonies[0].StoreOf(ToolsId);

        game.EndTurn(); // Phillip elected → the first settlement is supplied

        Assert.Contains(Phillip, game.Congress);
        Assert.True(game.Colonies[0].StoreOf(FoodId) > foodBefore, "emergency food should have been delivered");
        Assert.True(game.Colonies[0].StoreOf(ToolsId) >= toolsBefore + 20, "emergency tools should have been delivered");
    }

    // ───────────────────────── fixtures ─────────────────────────

    private static int CountGold(Game game) => game.Map.Resources.Count(kv => kv.Value == GoldResource);

    /// <summary>
    /// A 5×5 Australia-ruleset game: a pop-3 colony at the centre on plains, ringed by explored <b>hills</b> tiles
    /// (the gold-rush candidates) plus the colony tile, with the chosen current father staged one liberty short so
    /// he is elected on the next EndTurn. Enough food is stocked so the colony doesn't starve.
    /// </summary>
    private static Game GoldRushGame(string currentFather, int liberty)
    {
        const int w = 5, h = 5;
        // All hills except the colony centre (plains, settleable) — so every non-centre tile is a gold candidate.
        var terrain = Enumerable.Repeat("model.tile.hills", w * h).ToList();
        int centre = 2 * w + 2; // (2,2)
        terrain[centre] = "model.tile.plains";

        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 42,
            RandomIncrement = 1,
            MapWidth = w,
            MapHeight = h,
            Terrain = terrain,
            Units = [],
            Explored = Enumerable.Range(0, w * h).ToList(), // whole map explored (fog lifted)
            Colonies = [new SavedColony(1, "Ballarat", 2, 2, 3,
                Stores: new Dictionary<string, int> { [FoodId] = 100 })],
            Congress = null,
            CurrentFather = currentFather,
            Liberty = liberty,
        };
        return save.Restore(Australia);
    }

    /// <summary>A 3×3 Australia-ruleset game with a single pop-3 colony, the given father staged for election.</summary>
    private static Game RationsGame(string currentFather, int liberty)
    {
        var terrain = Enumerable.Repeat("model.tile.plains", 9).ToList();
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 7,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [],
            Explored = Enumerable.Range(0, 9).ToList(),
            Colonies = [new SavedColony(1, "Sydney Cove", 1, 1, 3,
                Stores: new Dictionary<string, int> { [FoodId] = 50 })],
            Congress = null,
            CurrentFather = currentFather,
            Liberty = liberty,
        };
        return save.Restore(Australia);
    }
}
