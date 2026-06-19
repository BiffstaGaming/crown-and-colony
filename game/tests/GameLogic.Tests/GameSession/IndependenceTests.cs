using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
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
        // Disband the leftover starting colonist so the rebel's unit count is exactly the 3 veterans below; with one
        // SoL-100 colony the cap is then (3 + 2) * (100 - 50) / 100 = 2 upgrades, leaving 1 (pins the cap + the
        // "some veterans un-mustered" branch).
        game.Disband(game.Units.First(u => u.OwnerId == rebel.PlayerId && u.IsOnMap && u.Type.Id == "model.unit.freeColonist"));
        foreach (Position p in EmptyLand(game, 3))
        {
            game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), p);
        }

        game.DeclareIndependence(rebel);

        Assert.Equal(2, game.Units.Count(u => u.Type.Id == "model.unit.colonialRegular"));
        Assert.Equal(1, game.Units.Count(u => u.Type.Id == "model.unit.veteranSoldier" && u.OwnerId == rebel.PlayerId));
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

    // ── Item 8: REF arrival + War-of-Independence combat ─────────────────────────────────────────────────

    private static Player Ref(Game game) => game.Players.Single(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);

    [Fact]
    public void Ref_SailsIn_AndLandsNearTheRebel()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        Player refP = Ref(game);
        Assert.All(game.Units.Where(u => u.OwnerId == refP.PlayerId), u => Assert.Equal(UnitLocation.InEurope, u.Location));

        game.EndTurn(); // RunRefTurn lands the invasion

        Assert.Contains(game.Units, u => u.OwnerId == refP.PlayerId && u.IsOnMap);
    }

    [Fact]
    public void RefRebelWar_DoesNotCoolOverTime()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        Player refP = Ref(game);

        for (int i = 0; i < 3; i++)
        {
            game.EndTurn(); // the colonial-diplomacy decay must not reach the REF/rebel pair
        }

        Assert.Equal(Stance.War, game.StanceBetween(refP.PlayerId, game.HumanPlayer.PlayerId));
    }

    [Fact]
    public void Rebellion_IsDeterministic_AcrossTwinGames()
    {
        // The whole war (REF landing + combat) draws only from the REF's own stream and the human's stream 0,
        // both seeded — twin games stay byte-identical (ADR-009; the REF never perturbs an unrelated stream).
        (Game a, _) = RebellionReady(7777);
        (Game b, _) = RebellionReady(7777);
        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);
        for (int i = 0; i < 5; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(a.RandomState, b.RandomState);
    }

    [Fact]
    public void InFlightWar_RoundTripsSaveLoad()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.EndTurn(); // REF on the map, war under way
        Player refP = Ref(game);
        int refOnMap = game.Units.Count(u => u.OwnerId == refP.PlayerId && u.IsOnMap);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Player loadedRef = Ref(loaded);

        Assert.Equal(refOnMap, loaded.Units.Count(u => u.OwnerId == loadedRef.PlayerId && u.IsOnMap));
        Assert.Equal(Stance.War, loaded.StanceBetween(loadedRef.PlayerId, loaded.HumanPlayer.PlayerId));
    }

    // ── Item 9: Win — defeat the REF + Spanish Succession ────────────────────────────────────────────────

    [Fact]
    public void CheckForRefDefeat_IsFalseWhileTheRefIsIntact()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Assert.False(game.CheckForRefDefeat(Ref(game), rebel)); // a full 60-land/8-naval REF is far from broken
    }

    [Fact]
    public void GiveIndependence_WinsTheWar_SurrendersTheRedcoats()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        game.EndTurn(); // some REF regulars land
        int refLandOnMap = game.Units.Count(u => u.OwnerId == refP.PlayerId && u.IsOnMap && !u.Type.IsNaval);
        int rebelUnitsBefore = game.Units.Count(u => u.OwnerId == rebel.PlayerId);

        game.GiveIndependence(refP, rebel);

        Assert.Equal(PlayerType.Independent, rebel.PlayerType);
        Assert.Equal(0, rebel.TaxRate);
        Assert.Equal(Stance.Peace, game.StanceBetween(refP.PlayerId, rebel.PlayerId));
        Assert.Equal(0, game.Units.Count(u => u.OwnerId == refP.PlayerId)); // the REF is gone (land surrendered, navy withdrawn)
        Assert.Equal(rebelUnitsBefore + refLandOnMap, game.Units.Count(u => u.OwnerId == rebel.PlayerId)); // the on-map redcoats surrendered
        Assert.Equal(rebel, game.Winner);
    }

    /// <summary>Disbands the REF down to the given on-roster land/naval unit counts (to drive the defeat thresholds).</summary>
    private static void ReduceRefTo(Game game, Player refP, int keepLand, int keepNaval)
    {
        foreach (Unit u in game.Units.Where(u => u.OwnerId == refP.PlayerId && !u.Type.IsNaval).OrderBy(u => u.Id).Skip(keepLand).ToList())
        {
            game.Disband(u);
        }
        foreach (Unit u in game.Units.Where(u => u.OwnerId == refP.PlayerId && u.Type.IsNaval).OrderBy(u => u.Id).Skip(keepNaval).ToList())
        {
            game.Disband(u);
        }
    }

    [Fact]
    public void RebelWinsTheWar_WhenTheRefIsBrokenAndTheTurnResolves()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        ReduceRefTo(game, refP, keepLand: 0, keepNaval: 1); // broken: 0 land, 1 naval, no colonies held
        Assert.True(game.CheckForRefDefeat(refP, rebel));

        game.EndTurn(); // ResolveWarOfIndependence (in the world-advance band) must fire GiveIndependence

        Assert.Equal(PlayerType.Independent, rebel.PlayerType);
        Assert.Equal(rebel, game.Winner);
    }

    [Fact]
    public void CheckForRefDefeat_AtTheSevenLandTwoNavalBoundary_IsNotDefeated()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        ReduceRefTo(game, refP, keepLand: 7, keepNaval: 2); // exactly at the threshold — still a credible force
        Assert.False(game.CheckForRefDefeat(refP, rebel)); // pins the >= comparison (7 land AND 2 naval = not broken)
    }

    [Fact]
    public void Ref_DrawsNothingFromStreamZero_DuringTheWar()
    {
        // The REF fights on its OWN stream — with the human idle, stream 0 must not move (ADR-009 isolation, not just determinism).
        (Game game, _) = RebellionReady(7777);
        game.DeclareIndependence(game.HumanPlayer);
        game.EndTurn(); // let the REF land/settle
        RandomState frozen = game.RandomState;
        for (int i = 0; i < 4; i++)
        {
            game.EndTurn(); // the REF wages war on its own stream
        }
        Assert.Equal(frozen, game.RandomState);
    }

    [Fact]
    public void Victory_RoundTripsSaveLoad()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.GiveIndependence(Ref(game), game.HumanPlayer);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(PlayerType.Independent, loaded.HumanPlayer.PlayerType);
        Assert.Equal(loaded.HumanPlayer, loaded.Winner);
    }

    [Fact]
    public void SpanishSuccession_DoesNotFireBefore1600_AndItsFlagPersists()
    {
        Game game = Game.New(Classic, Seed);
        game.EndTurn();
        Assert.False(game.SpanishSuccessionDone); // it is well before 1600

        game.SetSpanishSuccessionDone(true);
        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.True(loaded.SpanishSuccessionDone);
    }

    [Fact]
    public void PreVictoryGame_OmitsTheSuccessionToken()
    {
        Assert.DoesNotContain("\"SpanishSuccession\"", SaveGame.From(Game.New(Classic, Seed)).ToJson());
    }

    // ── Item 10: Lose — the rebel loses its last connected port ──────────────────────────────────────────

    [Fact]
    public void Rebel_LosingItsLastPort_IsDefeated()
    {
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Assert.Equal(1, game.GetNumberOfPorts(rebel));
        Assert.False(game.IsRebelDefeated(rebel)); // still holds its port

        colony.OwnerId = Ref(game).PlayerId; // the REF captures the last port

        Assert.Equal(0, game.GetNumberOfPorts(rebel));
        Assert.True(game.IsRebelDefeated(rebel));
    }

    [Fact]
    public void ColonialPlayer_WithNoPort_IsNotRebelDefeated()
    {
        // The lose condition only applies after declaring — a plain colony with no port is just a colony.
        Game game = Game.New(Classic, Seed);
        Assert.False(game.IsRebelDefeated(game.HumanPlayer));
    }

    [Fact]
    public void EndTurn_StaysByteStable_AfterTheRebelLosesItsLastPort()
    {
        // Defeat is a presentation flag — EndTurn must NOT short-circuit (ADR-009 byte-stability). Twins with a
        // defeated rebel still advance stream 0 identically.
        (Game a, Colony ca) = RebellionReady(7777);
        (Game b, Colony cb) = RebellionReady(7777);
        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);
        ca.OwnerId = Ref(a).PlayerId;
        cb.OwnerId = Ref(b).PlayerId;
        for (int i = 0; i < 3; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.True(a.IsRebelDefeated(a.HumanPlayer));
        Assert.Equal(a.RandomState, b.RandomState);
    }
}
