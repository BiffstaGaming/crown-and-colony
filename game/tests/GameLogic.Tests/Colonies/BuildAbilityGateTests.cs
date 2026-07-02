using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Building <c>required-ability</c> gate (<c>86d3c9p0q</c>, FreeCol <c>Colony.getNoBuildReason</c>
/// MISSING_ABILITY/COASTAL): the factory tier + arsenal need Adam Smith's <c>buildFactory</c>, the custom house
/// needs Stuyvesant's <c>buildCustomHouse</c>, and docks/drydock/shipyard need a coastal colony (<c>hasPort</c>).
/// </summary>
public class BuildAbilityGateTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Docks = "model.building.docks";
    private const string CustomHouse = "model.building.customHouse";
    private const string Stuyvesant = "model.foundingFather.peterStuyvesant";
    private const string BuildFactory = "model.ability.buildFactory";
    private const string HasPort = "model.ability.hasPort";

    // ---- Ruleset parse (data is gated, inherited down the extends chain) ----

    [Theory]
    [InlineData("model.building.ironWorks")]
    [InlineData("model.building.cigarFactory")]
    [InlineData("model.building.textileMill")]
    [InlineData("model.building.rumFactory")]
    [InlineData("model.building.furFactory")]
    [InlineData("model.building.arsenal")]
    public void FactoryTierAndArsenal_RequireBuildFactory(string buildingId)
    {
        Assert.True(Classic.Building(buildingId).RequiredAbilitiesOrEmpty.GetValueOrDefault(BuildFactory));
    }

    [Fact]
    public void CustomHouse_RequiresBuildCustomHouse()
    {
        Assert.True(Classic.Building(CustomHouse).RequiredAbilitiesOrEmpty
            .GetValueOrDefault("model.ability.buildCustomHouse"));
    }

    [Theory]
    [InlineData("model.building.docks")]
    [InlineData("model.building.drydock")]   // extends docks → inherits hasPort
    [InlineData("model.building.shipyard")]  // extends drydock → inherits hasPort
    public void Docks_AndItsUpgrades_RequireHasPort(string buildingId)
    {
        Assert.True(Classic.Building(buildingId).RequiredAbilitiesOrEmpty.GetValueOrDefault(HasPort));
    }

    [Fact]
    public void AnUnconditionalBuilding_HasNoRequiredAbilities()
    {
        Assert.Empty(Classic.Building("model.building.warehouse").RequiredAbilitiesOrEmpty);
        Assert.Empty(Classic.Building("model.building.lumberMill").RequiredAbilitiesOrEmpty);
    }

    // ---- Coastal gate (docks) ----

    [Fact]
    public void Docks_AreRefusedInland_ButAllowedOnTheCoast()
    {
        Game inland = InlandColony(out Colony landlocked);
        MoveCheck refused = inland.CheckSetBuild(landlocked, Docks);
        Assert.False(refused.Allowed);
        Assert.Contains("coastal", refused.Reason);
        Assert.DoesNotContain(inland.Buildables(landlocked), b => b.Id == Docks);

        Game coast = CoastalColony(out Colony port);
        Assert.True(coast.CheckSetBuild(port, Docks).Allowed);
        Assert.Contains(coast.Buildables(port), b => b.Id == Docks);
    }

    [Fact]
    public void EnqueueBuild_AlsoEnforcesTheCoastalGate()
    {
        Game inland = InlandColony(out Colony landlocked);
        Assert.False(inland.CheckEnqueueBuild(landlocked, Docks).Allowed);

        Game coast = CoastalColony(out Colony port);
        Assert.True(coast.CheckEnqueueBuild(port, Docks).Allowed);
    }

    [Fact]
    public void Docks_AreRefusedBesideOnlyAnInlandLake()
    {
        // FreeCol Tile.isCoastland / Settlement.isConnectedPort: a colony beside ONLY an inland lake (enclosed water
        // with no high-seas route) is NOT a port — it cannot build docks. An ocean-adjacent colony still can.
        Game lake = LakeColony(out Colony lakeside);
        MoveCheck refused = lake.CheckSetBuild(lakeside, Docks);
        Assert.False(refused.Allowed);
        Assert.Contains("coastal", refused.Reason);
        Assert.DoesNotContain(lake.Buildables(lakeside), b => b.Id == Docks);
        Assert.False(lake.CheckEnqueueBuild(lakeside, Docks).Allowed);

        Game coast = CoastalColony(out Colony port);
        Assert.True(coast.CheckSetBuild(port, Docks).Allowed);
    }

    // ---- Founding-father gate (custom house) ----

    [Fact]
    public void CustomHouse_IsRefusedWithoutStuyvesant_AndAllowedWithHim()
    {
        Game without = InlandColony(out Colony noFather, congress: null);
        MoveCheck refused = without.CheckSetBuild(noFather, CustomHouse);
        Assert.False(refused.Allowed);
        Assert.Contains("Stuyvesant", refused.Reason);

        Game with = InlandColony(out Colony hasFather, congress: [Stuyvesant]);
        Assert.True(with.CheckSetBuild(hasFather, CustomHouse).Allowed);
    }

    // ---- Coastal gate (custom house under the customsOnCoast option, 86d3fpyx9) ----

    [Fact]
    public void CustomsOnCoast_OptionPlumbsThrough_ClassicDefaultOff()
    {
        // The option defaults off in classic (byte-identity guard) and the seam flips it.
        Assert.False(Ruleset.LoadClassic().GameOptions.CustomsOnCoast);
        Assert.True(Ruleset.LoadClassic().WithCustomsOnCoast(true).GameOptions.CustomsOnCoast);
        Assert.False(Ruleset.LoadClassic().WithCustomsOnCoast(false).GameOptions.CustomsOnCoast);
    }

    [Fact]
    public void CustomHouse_WhenCustomsOnCoastOff_BuildableInland()
    {
        // Default game: an inland colony with Stuyvesant may raise a custom house — no coast needed (classic).
        Game inland = InlandColony(out Colony landlocked, congress: [Stuyvesant]);
        Assert.True(inland.CheckSetBuild(landlocked, CustomHouse).Allowed);
        Assert.Contains(inland.Buildables(landlocked), b => b.Id == CustomHouse);
    }

    [Fact]
    public void CustomHouse_WhenCustomsOnCoastOn_RefusedInland()
    {
        // With the option on, the custom house's coastalOnly ability flips true: an inland colony is refused (COASTAL).
        Ruleset coastal = Ruleset.LoadClassic().WithCustomsOnCoast(true);
        Game inland = InlandColony(out Colony landlocked, congress: [Stuyvesant], ruleset: coastal);
        MoveCheck refused = inland.CheckSetBuild(landlocked, CustomHouse);
        Assert.False(refused.Allowed);
        Assert.Contains("coastal", refused.Reason);
        Assert.DoesNotContain(inland.Buildables(landlocked), b => b.Id == CustomHouse);
    }

    [Fact]
    public void CustomHouse_WhenCustomsOnCoastOn_AllowedOnCoast()
    {
        // With the option on, a sea-connected colony (with Stuyvesant) may still raise a custom house.
        Ruleset coastal = Ruleset.LoadClassic().WithCustomsOnCoast(true);
        Game port = CoastalColony(out Colony seaside, congress: [Stuyvesant], ruleset: coastal);
        Assert.True(port.CheckSetBuild(seaside, CustomHouse).Allowed);
        Assert.Contains(port.Buildables(seaside), b => b.Id == CustomHouse);
    }

    [Fact]
    public void CoastalRefusal_OutranksAMissingAbility_FreeColReasonOrder()
    {
        // FreeCol Colony.getNoBuildReason checks COASTAL BEFORE MISSING_ABILITY (Colony.java:1092-1116, 86d3hjz87):
        // an inland colony withOUT Stuyvesant, with customsOnCoast on, stacks both failures — the reported reason
        // must be the coastal one, not the missing founding father. (The refused OUTCOME is order-independent —
        // every gate must pass either way; only the reported reason follows FreeCol's order.)
        Ruleset coastal = Ruleset.LoadClassic().WithCustomsOnCoast(true);
        Game inland = InlandColony(out Colony landlocked, congress: null, ruleset: coastal);
        MoveCheck refused = inland.CheckSetBuild(landlocked, CustomHouse);
        Assert.False(refused.Allowed);
        Assert.Contains("coastal", refused.Reason);
        Assert.DoesNotContain("Stuyvesant", refused.Reason);

        // On the coast the COASTAL gate passes and the ability gate reports next (the same FreeCol order).
        Game port = CoastalColony(out Colony seaside, congress: null, ruleset: coastal);
        MoveCheck noFather = port.CheckSetBuild(seaside, CustomHouse);
        Assert.False(noFather.Allowed);
        Assert.Contains("Stuyvesant", noFather.Reason);
    }

    // ---- Fixtures ----

    /// <summary>A 1×1 inland plains colony (no water neighbour → no port), with an optional founding-father congress.</summary>
    private static Game InlandColony(out Colony colony, IReadOnlyList<string>? congress = null, Ruleset? ruleset = null)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Inland", 0, 0, 1)],
            Congress = congress,
        }.Restore(ruleset ?? Classic);
        colony = game.Colonies[0];
        return game;
    }

    /// <summary>A plains colony at (0,0) with an ocean tile beside it (a port / coastal colony).</summary>
    private static Game CoastalColony(out Colony colony, IReadOnlyList<string>? congress = null, Ruleset? ruleset = null)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 2,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean"],
            Units = [],
            Explored = [0, 1],
            Colonies = [new SavedColony(1, "Port", 0, 0, 1)],
            Congress = congress,
        }.Restore(ruleset ?? Classic);
        colony = game.Colonies[0];
        return game;
    }

    /// <summary>A plains colony at (0,0) with only an inland-lake tile beside it — water, but no sea route (not a port).</summary>
    private static Game LakeColony(out Colony colony)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 2,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.lake"],
            Units = [],
            Explored = [0, 1],
            Colonies = [new SavedColony(1, "Lakeside", 0, 0, 1)],
        }.Restore(Classic);
        colony = game.Colonies[0];
        return game;
    }
}
