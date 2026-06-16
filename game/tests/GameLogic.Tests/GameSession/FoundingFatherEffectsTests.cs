using System.Collections.Generic;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Founding-Father <em>effects</em> (Phase 4 slice 7): the modifier + ability system,
/// applied to the production it can reach today — bells → liberty (Jefferson, Paine),
/// crosses → immigration (Penn) — and William Brewster's recruit-pool ban. Every value
/// is pinned to the spec's father modifiers (Jefferson +50% bells, Penn +50% crosses,
/// Paine + the tax rate, Brewster denies servants/criminals).
/// </summary>
public class FoundingFatherEffectsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Jefferson = "model.foundingFather.thomasJefferson";
    private const string Penn = "model.foundingFather.williamPenn";
    private const string Paine = "model.foundingFather.thomasPaine";
    private const string Brewster = "model.foundingFather.williamBrewster";
    private const string Servant = "model.unit.indenturedServant";
    private const string Criminal = "model.unit.pettyCriminal";
    private const string TownHall = "model.building.townHall";
    private const string Church = "model.building.church";

    // ───────────────────────── parsing ─────────────────────────

    [Fact]
    public void Ruleset_ParsesFatherModifiers_AndAbilities()
    {
        FoundingFather jefferson = Classic.Father(Jefferson);
        FatherModifier bells = Assert.Single(jefferson.Modifiers);
        Assert.Equal("model.goods.bells", bells.TargetId);
        Assert.Equal(ModifierType.Percentage, bells.Type);
        Assert.Equal(50, bells.Value);

        FoundingFather brewster = Classic.Father(Brewster);
        Assert.Contains(brewster.Abilities, a => a.Id == "model.ability.selectRecruit" && a.Value);
        FatherAbility ban = Assert.Single(
            brewster.Abilities, a => a.Id == "model.ability.canRecruitUnit" && !a.Value);
        Assert.Contains(Servant, ban.ScopeTypes);
        Assert.Contains(Criminal, ban.ScopeTypes);
    }

    // ───────────────────────── production modifiers ─────────────────────────

    [Fact]
    public void Jefferson_Boosts_BellsToLiberty_By50Percent()
    {
        // A pop-1 colony with the colonist in the town hall makes 4 bells/turn (1 + 3).
        Game baseline = BellColony(congress: null);
        baseline.EndTurn();
        Assert.Equal(4, baseline.Liberty); // 1 unattended + 3 from the statesman

        Game jefferson = BellColony(congress: [Jefferson]);
        jefferson.EndTurn();
        Assert.Equal(6, jefferson.Liberty); // 4 + 50%
    }

    [Fact]
    public void Penn_Boosts_CrossesToImmigration_By50Percent()
    {
        // A colony with a church makes 2 crosses/turn (unattended); +2 player bonus.
        Game baseline = ChurchColony(congress: null);
        baseline.EndTurn();
        Assert.Equal(4, baseline.Immigration); // 2 crosses + 2 bonus

        Game penn = ChurchColony(congress: [Penn]);
        penn.EndTurn();
        Assert.Equal(5, penn.Immigration); // (2 + 50% = 3) + 2 bonus
    }

    [Fact]
    public void Paine_Adds_TheTaxRate_ToBellProduction()
    {
        Game baseline = BellColony(congress: null, tax: 50);
        baseline.EndTurn();
        Assert.Equal(4, baseline.Liberty); // tax alone does nothing to bells

        Game paine = BellColony(congress: [Paine], tax: 50);
        Assert.True(paine.HasAbility("model.ability.addTaxToBells"));
        paine.EndTurn();
        Assert.Equal(6, paine.Liberty); // 4 + 50% (the tax rate)
    }

    [Fact]
    public void HasAbility_IsTrue_OnlyForElectedFathers()
    {
        Assert.False(BellColony(congress: null).HasAbility("model.ability.selectRecruit"));
        Assert.True(BrewsterGame(1).HasAbility("model.ability.selectRecruit"));
    }

    // ───────────────────────── Brewster's recruit ban ─────────────────────────

    [Fact]
    public void Brewster_KeepsServantsAndCriminals_OffTheDock()
    {
        // Across many seeds the dock never offers scum once Brewster is elected…
        for (ulong seed = 1; seed <= 50; seed++)
        {
            Game game = BrewsterGame(seed);
            Assert.DoesNotContain(game.RecruitDock, id => id == Servant || id == Criminal);
        }

        // …but the unfiltered pool clearly can (proving the ban actually removes them).
        bool scumWithoutBrewster = false;
        for (ulong seed = 1; seed <= 50 && !scumWithoutBrewster; seed++)
        {
            foreach (string id in PlainGame(seed).RecruitDock)
            {
                if (id == Servant || id == Criminal)
                {
                    scumWithoutBrewster = true;
                }
            }
        }
        Assert.True(scumWithoutBrewster, "the default recruit pool should include servants/criminals");
    }

    [Fact]
    public void ElectingBrewster_RefreshesTheDock()
    {
        // Brewster chosen, one liberty short of the first father's cost; a town-hall
        // colony tips it over the threshold this turn.
        Game game = BellColony(congress: null, tax: 0, currentFather: Brewster, liberty: 23);
        game.EndTurn();

        Assert.Contains(Brewster, game.Congress);
        Assert.DoesNotContain(game.RecruitDock, id => id == Servant || id == Criminal);
    }

    // ───────────────────────── Simón Bolívar's +20 Sons of Liberty ─────────────────────────

    private const string Bolivar = "model.foundingFather.simonBolivar";

    [Fact]
    public void Ruleset_Bolivar_CarriesTheSoLModifier()
    {
        FoundingFather bolivar = Classic.Father(Bolivar);
        Assert.Contains(bolivar.Modifiers, m => m.TargetId == "model.modifier.SoL" && (int)m.Value == 20);
    }

    [Fact]
    public void Bolivar_AddsTwentyPoints_ToEveryColonysSoL()
    {
        // pop 5, liberty 250 → base SoL 25; with Bolívar in Congress it reads 45.
        Assert.Equal(25, SoLColony(congress: null).Colonies[0].SonsOfLiberty);
        Assert.Equal(45, SoLColony(congress: [Bolivar]).Colonies[0].SonsOfLiberty);
    }

    [Fact]
    public void Bolivar_IsAStandingModifier_NotABake_SurvivesPopulationGrowth()
    {
        // The faithful test: Bolívar's +20 is applied to the percentage every read, so when the colony grows the
        // SoL recomputes from liberty at the new population (a one-time liberty bake would drift here).
        Game game = SoLColony(congress: [Bolivar], food: 210); // seeded to grow this turn
        Colony colony = game.Colonies[0];
        Assert.Equal(45, colony.SonsOfLiberty); // pop 5

        game.EndTurn();

        Assert.Equal(6, colony.Population); // grew
        int expected = System.Math.Clamp(colony.Liberty * 100 / (200 * 6) + 20, 0, 100);
        Assert.Equal(expected, colony.SonsOfLiberty); // recomputed at pop 6 (≈40), not a stale baked value
        Assert.True(colony.SonsOfLiberty < 45, "SoL drops as population grows — the modifier recomputes");
    }

    [Fact]
    public void Bolivar_ClampsSoLAtOneHundred()
    {
        // pop 5, liberty 900 → base SoL 90; +20 would be 110 but clamps to 100.
        Assert.Equal(100, SoLColony(congress: [Bolivar], liberty: 900).Colonies[0].SonsOfLiberty);
    }

    [Fact]
    public void ElectingBolivar_BumpsExistingColoniesImmediately()
    {
        // Player liberty already over the first father's cost, so Bolívar is elected this turn regardless of the
        // (unstaffed) colony's bell upkeep.
        Game game = SoLColony(congress: null, currentFather: Bolivar, playerLiberty: 100);
        Assert.Equal(25, game.Colonies[0].SonsOfLiberty); // before election
        game.EndTurn();
        Assert.Contains(Bolivar, game.Congress);
        Assert.True(game.Colonies[0].SonsOfLiberty >= 40, "the elected Bolívar grants the standing +20 at once");
    }

    /// <summary>A pop-5 colony seeded with <paramref name="liberty"/> colony liberty (default 250 → base SoL 25), optionally food-stocked to grow.</summary>
    private static Game SoLColony(IReadOnlyList<string>? congress, int liberty = 250, int food = 0, string? currentFather = null, int playerLiberty = 0)
    {
        var stores = food > 0 ? new Dictionary<string, int> { ["model.goods.food"] = food } : null;
        return Plains3x3(
            new SavedColony(1, "Liberty", 1, 1, 5, Stores: stores, Liberty: liberty),
            congress, tax: 0, currentFather: currentFather, liberty: playerLiberty);
    }

    // ───────────────────────── fixtures ─────────────────────────

    /// <summary>A pop-1 colony with the colonist in the town hall (4 bells/turn) on a 3×3 plains map.</summary>
    private static Game BellColony(
        IReadOnlyList<string>? congress, int tax = 0, string? currentFather = null, int liberty = 0) =>
        Plains3x3(
            new SavedColony(1, "Bell", 1, 1, 1,
                BuildingWorkers: new Dictionary<string, int> { [TownHall] = 1 }),
            congress, tax, currentFather, liberty);

    /// <summary>A pop-3 colony with a church (2 crosses/turn), food stocked so it doesn't starve.</summary>
    private static Game ChurchColony(IReadOnlyList<string>? congress) =>
        Plains3x3(
            new SavedColony(1, "Cross", 1, 1, 3,
                Stores: new Dictionary<string, int> { ["model.goods.food"] = 50 },
                Buildings: [TownHall, Church]),
            congress, tax: 0, currentFather: null, liberty: 0);

    private static Game Plains3x3(
        SavedColony colony, IReadOnlyList<string>? congress, int tax, string? currentFather, int liberty)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = [.. System.Linq.Enumerable.Repeat("model.tile.plains", 9)],
            Units = [],
            Explored = [],
            Colonies = [colony],
            Tax = tax,
            Liberty = liberty,
            Congress = congress,
            CurrentFather = currentFather,
        };
        return save.Restore(Classic);
    }

    /// <summary>A unit-less, colony-less game with the given congress (just the Europe dock).</summary>
    private static Game CongressGame(ulong seed, IReadOnlyList<string>? congress)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = seed,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.highSeas"],
            Units = [],
            Explored = [],
            Congress = congress,
        };
        return save.Restore(Classic);
    }

    private static Game BrewsterGame(ulong seed) => CongressGame(seed, [Brewster]);

    private static Game PlainGame(ulong seed) => CongressGame(seed, congress: null);
}
