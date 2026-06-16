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

    // ---- Fixtures ----

    /// <summary>A 1×1 inland plains colony (no water neighbour → no port), with an optional founding-father congress.</summary>
    private static Game InlandColony(out Colony colony, IReadOnlyList<string>? congress = null)
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
        }.Restore(Classic);
        colony = game.Colonies[0];
        return game;
    }

    /// <summary>A plains colony at (0,0) with an ocean tile beside it (a port / coastal colony).</summary>
    private static Game CoastalColony(out Colony colony)
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
        }.Restore(Classic);
        colony = game.Colonies[0];
        return game;
    }
}
