using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The War of Independence (P6 arc item 7, <c>86d3c9v28</c>): the rebel-sentiment gate, declaring independence
/// (continental muster + losing Europe), and the Royal Expeditionary Force taking the field at war with the rebel.
/// </summary>
public class IndependenceTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>A game with one coastal colony at full Sons-of-Liberty, ready to declare independence.</summary>
    private static (Game Game, Colony Colony) RebellionReady(ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        Position coastal = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), coastal);
        Colony colony = game.FoundColony(colonist);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // force national SoL to 100%
        return (game, colony);
    }

    /// <summary>The first <paramref name="count"/> empty, land tiles (for spawning test units).</summary>
    private static List<Position> EmptyLand(Game game, int count) =>
        game.Map.AllPositions()
            .Where(p => !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null
                && game.NativeSettlementAt(p) is null && !game.Units.Any(u => u.IsOnMap && u.Position == p))
            .Take(count).ToList();

    [Fact]
    public void NationalSonsOfLiberty_IsRebelsOverPopulation()
    {
        (Game game, Colony colony) = RebellionReady();
        Assert.Equal(100, game.NationalSonsOfLiberty(game.HumanPlayer));
        colony.Liberty = 0;
        Assert.Equal(0, game.NationalSonsOfLiberty(game.HumanPlayer));
    }

    [Fact]
    public void CheckDeclareIndependence_GatesOnSonsOfLiberty()
    {
        (Game game, Colony colony) = RebellionReady();
        Assert.True(game.CheckDeclareIndependence(game.HumanPlayer).Allowed); // 100% SoL, coastal

        colony.Liberty = 0; // SoL → 0
        Assert.False(game.CheckDeclareIndependence(game.HumanPlayer).Allowed);
    }

    [Fact]
    public void DeclareIndependence_TurnsRebel_LosesEuropeUnits_AndStartsTheWar()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        Unit inEurope = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), EmptyLand(game, 1)[0]);
        inEurope.Location = UnitLocation.InEurope; // a colonist waiting in Europe — forfeit on declaration
        int inEuropeId = inEurope.Id;

        game.DeclareIndependence(rebel);

        Assert.Equal(PlayerType.Rebel, rebel.PlayerType);
        Assert.Equal(game.Turn, rebel.DeclaredIndependenceTurn);
        Assert.DoesNotContain(game.Units, u => u.Id == inEuropeId); // the Europe unit is gone

        Player refPlayer = game.Players.Single(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        Assert.Equal(Stance.War, game.StanceBetween(refPlayer.PlayerId, rebel.PlayerId));
        Assert.True(game.Units.Count(u => u.OwnerId == refPlayer.PlayerId) >= 60); // the REF realised into units
    }

    [Fact]
    public void DeclareIndependence_MustersVeteransIntoColonialRegulars()
    {
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        // Three veteran soldiers — the muster cap with one SoL-100 colony is
        // (unitCount + 2) * (100 - 50) / 100 = (3 + 2) * 50 / 100 = 2 upgrades, leaving 1.
        foreach (Position p in EmptyLand(game, 3))
        {
            game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), p);
        }

        game.DeclareIndependence(rebel);

        int colonialRegulars = game.Units.Count(u => u.Type.Id == "model.unit.colonialRegular");
        int remainingVeterans = game.Units.Count(u => u.Type.Id == "model.unit.veteranSoldier" && u.OwnerId == rebel.PlayerId);
        Assert.Equal(3, colonialRegulars + remainingVeterans); // conservation: every spawned veteran is one or the other
        Assert.True(colonialRegulars >= 1, "the muster should have upgraded at least one veteran to a colonial regular");
    }

    [Fact]
    public void EndTurn_RunsCleanlyAfterDeclaration()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.EndTurn(); // REF turn is a no-op stub for now; the rebel runs the colonial path
        Assert.True(game.Turn >= 2);
    }

    [Fact]
    public void Rebellion_PersistsAcrossSaveLoad()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        int refUnits = game.Units.Count(u => game.Players.Single(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce).PlayerId == u.OwnerId);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(PlayerType.Rebel, loaded.HumanPlayer.PlayerType);
        Assert.Equal(game.HumanPlayer.DeclaredIndependenceTurn, loaded.HumanPlayer.DeclaredIndependenceTurn);
        Player loadedRef = loaded.Players.Single(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        Assert.Equal(Stance.War, loaded.StanceBetween(loadedRef.PlayerId, loaded.HumanPlayer.PlayerId));
        Assert.Equal(refUnits, loaded.Units.Count(u => u.OwnerId == loadedRef.PlayerId));
    }

    [Fact]
    public void PreIndependenceGame_OmitsRebellionTokens()
    {
        SaveGame save = SaveGame.From(Game.New(Classic, Seed));
        string json = save.ToJson();
        Assert.DoesNotContain("\"DeclaredIndependenceTurn\"", json);
        Assert.DoesNotContain("\"InterventionBells\"", json);
    }
}
