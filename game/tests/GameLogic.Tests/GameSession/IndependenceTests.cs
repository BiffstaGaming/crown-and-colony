using System.Text;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
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
    private static (Game Game, Colony Colony) RebellionReady(ulong seed = Seed) => RebellionReady(Classic, seed);

    /// <summary>As <see cref="RebellionReady(ulong)"/>, but on a supplied ruleset (e.g. a custom last-colonial-year).</summary>
    private static (Game Game, Colony Colony) RebellionReady(Ruleset ruleset, ulong seed = Seed)
    {
        Game game = Game.New(ruleset, seed);
        Position coastal = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        Unit colonist = game.SpawnUnit(ruleset.Unit(Game.StartingUnitTypeId), coastal);
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

    // ── model.option.lastColonialYear is a ruleset value, not a magic number (86d3drn4t) ──────────────────

    [Fact]
    public void ClassicRuleset_ParsesLastColonialYearAs1800()
    {
        // ADR-009 byte-identity: the default game's gate threshold is the same 1800 the constant used to hardcode.
        Assert.Equal(1800, Classic.LastColonialYear);
    }

    [Fact]
    public void ParseIntOption_ReadsNonDefaultSpecValue_AndFallsBack()
    {
        // Prove the value comes from the parsed game option, not a hardcoded 1800.
        XElement withOption = XElement.Parse(
            "<freecol-specification><options><optionGroup id='gameOptions.years'>" +
            "  <integerOption id='model.option.lastColonialYear' value='1650' />" +
            "</optionGroup></options></freecol-specification>");
        Assert.Equal(1650, Ruleset.ParseIntOption(withOption, "model.option.lastColonialYear", fallback: 1800));

        // Absent option → the supplied fallback (a spec without it stays at the classic 1800).
        XElement empty = XElement.Parse("<freecol-specification />");
        Assert.Equal(1800, Ruleset.ParseIntOption(empty, "model.option.lastColonialYear", fallback: 1800));
    }

    [Fact]
    public void CheckDeclareIndependence_ReadsTheRulesetYear_NotAHardcoded1800()
    {
        // A ruleset whose last colonial year is 1493 blocks the declaration in 1494 — a hardcoded 1800 would
        // wrongly still allow it. Proves the gate reads Ruleset.LastColonialYear.
        Ruleset earlyCutoff = ClassicWithLastColonialYear(1493);
        (Game game, _) = RebellionReady(earlyCutoff);
        Assert.Equal(1492, game.CurrentYear);
        Assert.True(game.CheckDeclareIndependence(game.HumanPlayer).Allowed); // 1492 ≤ 1493

        game.EndTurn(); // → 1493 (still ≤ cutoff)
        Assert.True(game.CheckDeclareIndependence(game.HumanPlayer).Allowed);

        game.EndTurn(); // → 1494, now past the ruleset's 1493 cutoff
        Assert.Equal(1494, game.CurrentYear);
        MoveCheck check = game.CheckDeclareIndependence(game.HumanPlayer);
        Assert.False(check.Allowed);
        Assert.Equal("It is too late in history to declare independence.", check.Reason);
    }

    [Fact]
    public void CheckDeclareIndependence_AllowsOnTheCutoffYear_BlocksTheYearAfter()
    {
        // Pin the boundary (year ≤ lastColonialYear, FreeCol's "le" limit): 1494 cutoff allows in 1494, blocks in 1495.
        Ruleset cutoff1494 = ClassicWithLastColonialYear(1494);
        (Game game, _) = RebellionReady(cutoff1494);
        game.EndTurn();
        game.EndTurn(); // → 1494, exactly the cutoff
        Assert.Equal(1494, game.CurrentYear);
        Assert.True(game.CheckDeclareIndependence(game.HumanPlayer).Allowed);

        game.EndTurn(); // → 1495, one past
        Assert.False(game.CheckDeclareIndependence(game.HumanPlayer).Allowed);
    }

    /// <summary>The classic ruleset reloaded with <c>model.option.lastColonialYear</c> overridden to <paramref name="year"/>.</summary>
    private static Ruleset ClassicWithLastColonialYear(int year)
    {
        var assembly = typeof(Ruleset).Assembly;
        using Stream raw = assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(raw);
        XElement option = doc.Descendants("integerOption")
            .Single(o => (string?)o.Attribute("id") == "model.option.lastColonialYear");
        option.SetAttributeValue("value", year);
        using var patched = new MemoryStream(Encoding.UTF8.GetBytes(doc.ToString()));
        return Ruleset.Load(patched);
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
        // Disband the leftover starting roster, then garrison 4 veterans ON the colony's tile (the per-colony muster
        // draws from each colony's OWN tile garrison, FreeCol getAllUnitsList). The colony has population 1, so its
        // unit count is 1 worker + 4 garrison = 5, and at SoL 100 the cap is (5 + 2) * (100 - 50) / 100 = 3 upgrades,
        // leaving 1 veteran un-mustered (pins the per-colony cap + the "some veterans stay veterans" branch).
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        for (int i = 0; i < 4; i++)
        {
            game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position);
        }

        game.DeclareIndependence(rebel);

        Assert.Equal(3, game.Units.Count(u => u.Type.Id == "model.unit.colonialRegular"));
        Assert.Equal(1, game.Units.Count(u => u.Type.Id == "model.unit.veteranSoldier" && u.OwnerId == rebel.PlayerId));
    }

    [Fact]
    public void DeclareIndependence_MusterDrawsFromEachColonysOwnGarrison_NotTheWholeMap()
    {
        // Per-colony fidelity (FreeCol csDeclareIndependence): a veteran standing AWAY from any SoL>50 colony is not
        // mustered — only each colony's own tile garrison upgrades. Here the colony's tile holds one veteran (mustered),
        // while a second veteran sits on open land elsewhere (left a veteran).
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position); // garrisoning the colony — eligible
        Position away = EmptyLand(game, 1)[0];
        Unit straggler = game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), away); // in the field — not at any colony

        game.DeclareIndependence(rebel);

        // The garrison veteran rose to a colonial regular; the field veteran stayed a veteran (it belongs to no colony).
        Assert.Equal(1, game.Units.Count(u => u.Type.Id == "model.unit.colonialRegular"));
        Assert.Equal("model.unit.veteranSoldier", game.Units.Single(u => u.Id == straggler.Id).Type.Id);
    }

    [Fact]
    public void DeclareIndependence_MusterCapCountsTheColonysOwnUnitsOnly()
    {
        // The cap term is (unitCount + 2) * (SoL - 50) / 100 over THIS colony's own units — population (workers) plus
        // its tile garrison (FreeCol allUnits.size()). With population 1 and a single garrisoned veteran the colony's
        // unit count is 2, so the cap is (2 + 2) * (100 - 50) / 100 = 2 — comfortably covering that one veteran. A
        // nationwide count would have summed in unrelated units; this proves the cap is per colony.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position);

        game.DeclareIndependence(rebel);

        Assert.Equal(1, game.Units.Count(u => u.Type.Id == "model.unit.colonialRegular"));
        Assert.Equal(0, game.Units.Count(u => u.Type.Id == "model.unit.veteranSoldier" && u.OwnerId == rebel.PlayerId));
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

    // ── Foreign Intervention Force (86d3c9vap) ───────────────────────────────────────────────────────────

    /// <summary>The units the rebel owns that aren't its starting roster — i.e. the freshly-landed intervention force.</summary>
    private static int RebelOwnedUnits(Game game, Player rebel) =>
        game.Units.Count(u => u.OwnerId == rebel.PlayerId && u.IsOnMap);

    [Fact]
    public void ClassicRuleset_ParsesTheInterventionOptions()
    {
        // ADR-009: the default game's intervention threshold/force come from the medium difficulty data, not magic numbers.
        Assert.Equal(5000, Classic.InterventionBells);
        Assert.Equal(52, Classic.InterventionTurns);

        // Classic medium force: 2 colonial-regular soldiers + 2 dragoons + 2 artillery + 2 men-o-war = 8 units, 6 land.
        Assert.Equal(8, Classic.InterventionForce.TotalCount);
        Assert.Equal(2, Classic.InterventionForce.Units
            .Single(u => u.UnitTypeId == "model.unit.manOWar").Count);
        Assert.Equal(2, Classic.InterventionForce.Units
            .Single(u => u.UnitTypeId == "model.unit.colonialRegular" && u.RoleId == "model.role.dragoon").Count);
    }

    [Fact]
    public void ParseIntervention_ReadsTheChosenLevel_AndFallsBack()
    {
        // A spec with a single difficulty level overriding the bells; proves the value is parsed, not hardcoded.
        XElement withOption = XElement.Parse(
            "<freecol-specification><options>" +
            "  <optionGroup id='model.difficulty.medium'>" +
            "    <integerOption id='model.option.interventionBells' value='1234' />" +
            "    <integerOption id='model.option.interventionTurns' value='9' />" +
            "    <unitListOption id='model.option.interventionForce'>" +
            "      <unitOption id='x'><unitType value='model.unit.manOWar' /><role value='model.role.default' /><number value='3' /></unitOption>" +
            "    </unitListOption>" +
            "  </optionGroup>" +
            "</options></freecol-specification>");
        (int bells, int turns, InterventionForceComposition force) = Ruleset.ParseIntervention(withOption);
        Assert.Equal(1234, bells);
        Assert.Equal(9, turns);
        Assert.Equal(3, force.TotalCount);

        // Absent options → the classic-medium fallback (5000 / 52 / the 8-unit force).
        (int fbBells, int fbTurns, InterventionForceComposition fbForce) =
            Ruleset.ParseIntervention(XElement.Parse("<freecol-specification />"));
        Assert.Equal(5000, fbBells);
        Assert.Equal(52, fbTurns);
        Assert.Equal(8, fbForce.TotalCount);
    }

    /// <summary>Units the rebel owns aboard a ship (the ally's troops travel in as cargo on the men-o-war).</summary>
    private static int RebelAboardUnits(Game game, Player rebel) =>
        game.Units.Count(u => u.OwnerId == rebel.PlayerId && u.IsAboard);

    [Fact]
    public void RebelReachingTheThreshold_GetsAnInterventionForce_AndResetsTheCounter()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        int beforeOnMap = RebelOwnedUnits(game, rebel);
        int beforeAboard = RebelAboardUnits(game, rebel);

        rebel.InterventionBells = Classic.InterventionBells; // at the threshold — the next resolution lands the ally
        // Resolve the war directly (not a full EndTurn): the REF's units are still mustering in Europe, so the ally
        // fleet makes landfall at the rebel's port unmolested — the same spawn EndTurn runs, isolated from the REF's
        // own turn (which would otherwise land 60+ redcoats and sink the newly-arrived fleet on the very same turn).
        game.ResolveWarOfIndependence(); // accrues (gain 0 on first sight) then fires the force

        Assert.Equal(0, rebel.InterventionBells); // the counter reset
        // The fleet arrives off the port with the troops aboard (FreeCol loadShips): the 2 men-o-war appear on the
        // water, carrying the 6 land units as passengers — so the rebel gains 2 on-map ships + 6 aboard = the full 8.
        // (The rebel began with a caravel, not a man-o-war, so the two men-o-war are unmistakably the ally's.)
        Assert.Equal(2, game.Units.Count(u => u.OwnerId == rebel.PlayerId && u.IsOnMap && u.Type.Id == "model.unit.manOWar"));
        Assert.Equal(beforeOnMap + 2, RebelOwnedUnits(game, rebel));
        Assert.Equal(beforeAboard + 6, RebelAboardUnits(game, rebel));
        // The whole classic-medium force is accounted for (on the map + aboard).
        Assert.Equal(beforeOnMap + beforeAboard + Classic.InterventionForce.TotalCount,
            game.Units.Count(u => u.OwnerId == rebel.PlayerId && (u.IsOnMap || u.IsAboard)));
    }

    [Fact]
    public void RebelBelowTheThreshold_GetsNoInterventionForce()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        int beforeOnMap = RebelOwnedUnits(game, rebel);
        int beforeAboard = RebelAboardUnits(game, rebel);

        rebel.InterventionBells = Classic.InterventionBells - 1; // one short
        game.ResolveWarOfIndependence();

        Assert.Equal(beforeOnMap, RebelOwnedUnits(game, rebel)); // nothing landed
        Assert.Equal(beforeAboard, RebelAboardUnits(game, rebel));
        Assert.Equal(Classic.InterventionBells - 1, rebel.InterventionBells); // still accruing (no liberty gain this turn)
    }

    [Fact]
    public void InterventionBells_AccrueTheRebelsLibertyEachTurn()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        game.EndTurn(); // first resolution seeds the liberty snapshot (no accrual yet)
        int bellsAfterSeed = rebel.InterventionBells;

        rebel.Liberty += 250; // simulate a turn's liberty production
        game.EndTurn();

        Assert.Equal(bellsAfterSeed + 250, rebel.InterventionBells); // the gain banked toward the threshold
    }

    [Fact]
    public void InterventionForce_IsDeterministic_AcrossTwinGames()
    {
        // The ally lands on its own dedicated stream (InterventionStreamId), never stream 0 — twins stay byte-identical.
        (Game a, _) = RebellionReady(4242);
        (Game b, _) = RebellionReady(4242);
        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);
        a.HumanPlayer.InterventionBells = Classic.InterventionBells;
        b.HumanPlayer.InterventionBells = Classic.InterventionBells;
        a.EndTurn();
        b.EndTurn();

        // Same seed → the same port, the same landing tiles, the same unit ids.
        var aSpots = a.Units.Where(u => u.OwnerId == a.HumanPlayer.PlayerId && u.IsOnMap).Select(u => (u.Id, u.Position)).OrderBy(x => x.Id).ToList();
        var bSpots = b.Units.Where(u => u.OwnerId == b.HumanPlayer.PlayerId && u.IsOnMap).Select(u => (u.Id, u.Position)).OrderBy(x => x.Id).ToList();
        Assert.Equal(aSpots, bSpots);
    }

    [Fact]
    public void InterventionForce_LandingDrawsNothingFromStreamZero()
    {
        // The friendly ally's landfall must not perturb the human's stream 0 (ADR-009 isolation): it draws only on
        // the dedicated InterventionStreamId. With the human idle, stream 0 stays frozen across the landing.
        (Game game, _) = RebellionReady(4242);
        game.DeclareIndependence(game.HumanPlayer);
        game.EndTurn(); // settle the REF/rebel turn, seed snapshots
        RandomState frozen = game.RandomState;

        game.HumanPlayer.InterventionBells = Classic.InterventionBells;
        game.EndTurn(); // the intervention force lands

        Assert.Equal(frozen, game.RandomState);
    }

    [Fact]
    public void InterventionForce_RoundTripsSaveLoad()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        rebel.InterventionBells = Classic.InterventionBells;
        game.EndTurn(); // the ally lands; counter resets to 0
        int rebelOnMap = RebelOwnedUnits(game, rebel);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(rebelOnMap, RebelOwnedUnits(loaded, loaded.HumanPlayer)); // the landed force persisted
        Assert.Equal(0, loaded.HumanPlayer.InterventionBells); // the reset counter persisted (omitted at 0)
    }

    // ── Item 1: Native re-stancing on the Declaration of Independence (86d3drn4p) ─────────────────────────

    [Fact]
    public void DeclareIndependence_CalmsTheMostHostileContactedNativeNation()
    {
        // FreeCol csDeclareIndependence: the most-hostile contacted native nation throws in with the rebel and is
        // calmed toward it (to the CONTENT band). We model the faithful, representable half: the angriest contacted
        // nation's settlements drop to at most CONTENT (600). Set one contacted nation hateful, then declare.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        NativeSettlement settlement = game.NativeSettlements.First();
        string nation = settlement.NationTypeId;
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
        {
            s.MarkVisitedBy(rebel.PlayerId);          // the rebel has met this nation (FreeCol hasContacted)
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // → hateful (1000)
        }

        game.DeclareIndependence(rebel);

        // Every settlement of the angriest contacted nation is calmed down to the CONTENT limit (600), not beyond.
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(NativeSettlement.AlarmContentMax, s.Alarm));
    }

    [Fact]
    public void DeclareIndependence_LeavesUncontactedNativesUntouched()
    {
        // Only a nation the rebel has CONTACTED can swing behind it (FreeCol filters on hasContacted). A hostile but
        // never-met nation keeps its alarm — and a nation already calmer than CONTENT is not stirred up.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        NativeSettlement uncontacted = game.NativeSettlements.First();
        game.ChangeNativeAlarm(uncontacted, NativeSettlement.MaxAlarm); // hateful, but never visited
        int before = uncontacted.Alarm;

        game.DeclareIndependence(rebel);

        Assert.Equal(before, game.NativeSettlements.First(s => s.Id == uncontacted.Id).Alarm); // untouched (never met)
    }

    [Fact]
    public void DeclareIndependence_NativeReStancing_IsByteStableOnStreamZero()
    {
        // The re-stancing is RNG-free — twins (same seed) advance stream 0 identically through the declaration.
        (Game a, _) = RebellionReady(7777);
        (Game b, _) = RebellionReady(7777);
        foreach (Game g in new[] { a, b })
        {
            NativeSettlement s = g.NativeSettlements.First();
            s.MarkVisitedBy(g.HumanPlayer.PlayerId);
            g.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }
        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);
        Assert.Equal(a.RandomState, b.RandomState);
    }

    // ── Item 2: War-time mercenary (Hessian) offer on declaration (86d3c9vdb) ─────────────────────────────

    [Fact]
    public void DeclareIndependence_OffersWarMercenaries_WhenAffordable()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 100000; // plenty to fund a Hessian force

        game.DeclareIndependence(rebel);

        // A pending Hessian offer is surfaced (the same seam as the in-game mercenary offers), not auto-applied.
        Assert.NotNull(game.PendingMonarchDemand);
        Assert.Equal(MonarchAction.HessianMercenaries, game.PendingMonarchDemand!.Action);
        Assert.NotNull(game.PendingMonarchDemand.Offer);
        Assert.True(game.PendingMonarchDemand.Offer!.Sum(e => e.Count) > 0);
        Assert.True(game.PendingMonarchDemand.Price > 0);
    }

    [Fact]
    public void DeclareIndependence_MercenaryOffer_IsAppliedOnlyOnAccept()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 100000;
        game.DeclareIndependence(rebel);
        PendingMonarchDemand offer = game.PendingMonarchDemand!;
        int offered = offer.Offer!.Sum(e => e.Count);
        int goldBefore = rebel.Gold;
        // No units were spawned by the mere offer — the rebel keeps its declaration roster.
        int europeBefore = game.Units.Count(u => u.OwnerId == rebel.PlayerId && u.Location == UnitLocation.InEurope);

        game.RespondToMonarch(accept: true);

        Assert.Null(game.PendingMonarchDemand); // the offer is consumed
        Assert.Equal(goldBefore - offer.Price, rebel.Gold); // paid for
        // The hired force arrives on the rebel's Europe dock (FreeCol csAddMercenaries spawns them in Europe).
        Assert.Equal(europeBefore + offered,
            game.Units.Count(u => u.OwnerId == rebel.PlayerId && u.Location == UnitLocation.InEurope));
    }

    [Fact]
    public void DeclareIndependence_MercenaryOffer_DeclineSpawnsNothing()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 100000;
        game.DeclareIndependence(rebel);
        int goldBefore = rebel.Gold;
        int unitsBefore = game.Units.Count(u => u.OwnerId == rebel.PlayerId);

        game.RespondToMonarch(accept: false);

        Assert.Null(game.PendingMonarchDemand);
        Assert.Equal(goldBefore, rebel.Gold); // not charged
        Assert.Equal(unitsBefore, game.Units.Count(u => u.OwnerId == rebel.PlayerId)); // no force hired
    }

    [Fact]
    public void DeclareIndependence_NoMercenaryOffer_WhenBroke()
    {
        // With an empty treasury the trimmed offer is null — no Hessian offer is surfaced (FreeCol's "not affordable").
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 0;

        game.DeclareIndependence(rebel);

        Assert.Null(game.PendingMonarchDemand);
    }

    // ── Item 3: Alternative victory conditions (86d3drn5n / victoryDefeat* options) ───────────────────────

    [Fact]
    public void ClassicRuleset_ParsesTheVictoryOptions()
    {
        // ADR-009: the default game's victory toggles come from the classic spec (REF on, Europeans on, Humans off).
        Assert.True(Classic.VictoryDefeatRef);
        Assert.True(Classic.VictoryDefeatEuropeans);
        Assert.False(Classic.VictoryDefeatHumans);
    }

    [Fact]
    public void ParseBooleanOption_ReadsVictoryDefaults_AndFallsBack()
    {
        // The values come from the parsed booleanOption defaultValue, not a hardcoded constant.
        XElement withOption = XElement.Parse(
            "<freecol-specification><options><optionGroup id='gameOptions.victoryConditions'>" +
            "  <booleanOption id='model.option.victoryDefeatHumans' defaultValue='true' />" +
            "</optionGroup></options></freecol-specification>");
        Assert.True(Ruleset.ParseBooleanOption(withOption, "model.option.victoryDefeatHumans", fallback: false));

        XElement empty = XElement.Parse("<freecol-specification />");
        Assert.False(Ruleset.ParseBooleanOption(empty, "model.option.victoryDefeatHumans", fallback: false));
    }

    [Fact]
    public void DefeatAllEuropeans_WinsWhenOneEuropeanPowerRemains()
    {
        // VICTORY_DEFEAT_EUROPEANS (classic on): when only one non-REF European power is still alive, it wins. Wipe
        // out the foreign powers (no colonies, no units) so only the human remains.
        Game game = Game.New(Classic, Seed);
        Player human = game.HumanPlayer;
        Assert.Null(game.Winner); // the human + 3 foreign powers are all alive → no winner

        foreach (Player power in game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList())
        {
            foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId).ToList())
            {
                game.Disband(u); // the foreign power loses its last unit (it never founded a colony) → wiped out
            }
        }

        Assert.Equal(human, game.Winner); // the last European standing wins
    }

    [Fact]
    public void DefeatAllEuropeans_NoWinnerWhileTwoEuropeanPowersSurvive()
    {
        // With two live European powers no Europeans-victory fires (and the human hasn't gone independent).
        Game game = Game.New(Classic, Seed);
        // Leave the human + at least one foreign power alive: wipe out only the foreign powers beyond the first.
        List<Player> foreign = game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList();
        foreach (Player power in foreign.Skip(1))
        {
            foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId).ToList())
            {
                game.Disband(u);
            }
        }
        Assert.True(game.Players.Count(p => p.PlayerType == PlayerType.Colonial
            && (game.Colonies.Any(c => c.OwnerId == p.PlayerId) || game.Units.Any(u => u.OwnerId == p.PlayerId))) >= 2);
        Assert.Null(game.Winner); // two live European powers → no Europeans victory
    }

    [Fact]
    public void DefeatAllEuropeans_Disabled_NoWinnerEvenWithOneSurvivor()
    {
        // With victoryDefeatEuropeans off (and REF off, since no rebellion), one surviving European power is NOT a win.
        Ruleset noEuropeanVictory = ClassicWithVictoryOptions(ref_: false, europeans: false, humans: false);
        Game game = Game.New(noEuropeanVictory, Seed);
        foreach (Player power in game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList())
        {
            foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId).ToList())
            {
                game.Disband(u);
            }
        }
        Assert.Null(game.Winner); // the option is off → no victory despite being the last European standing
    }

    [Fact]
    public void DefeatAllHumans_WinsWhenEnabledAndOneHumanRemains()
    {
        // VICTORY_DEFEAT_HUMANS: with the single human alive among the Europeans and the option on, the human wins
        // immediately (there is only one human player in our game). Off by default, so this needs the option enabled.
        Ruleset humanVictory = ClassicWithVictoryOptions(ref_: false, europeans: false, humans: true);
        Game game = Game.New(humanVictory, Seed);
        Assert.Equal(game.HumanPlayer, game.Winner); // only one non-AI European alive → human victory
    }

    /// <summary>The classic ruleset reloaded with the three victory booleans overridden.</summary>
    private static Ruleset ClassicWithVictoryOptions(bool ref_, bool europeans, bool humans)
    {
        var assembly = typeof(Ruleset).Assembly;
        using Stream raw = assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(raw);
        void Set(string id, bool value) => doc.Descendants("booleanOption")
            .Single(o => (string?)o.Attribute("id") == id)
            .SetAttributeValue("defaultValue", value ? "true" : "false");
        Set("model.option.victoryDefeatREF", ref_);
        Set("model.option.victoryDefeatEuropeans", europeans);
        Set("model.option.victoryDefeatHumans", humans);
        using var patched = new MemoryStream(Encoding.UTF8.GetBytes(doc.ToString()));
        return Ruleset.Load(patched);
    }

    // ── Item 4: Spanish Succession end-to-end consolidation (86d3dbugp) ───────────────────────────────────

    [Fact]
    public void SpanishSuccession_AbsorbsTheFadingAiIntoTheDominantOne_OnTheTurnAt1600()
    {
        // A scripted year-1600 scenario (FreeCol ServerGame.csSpanishSuccession): two AI European powers, one fervent
        // (SoL > 50) and one fading (SoL < 50), each with a colony + a unit. On the turn at/after 1600 the fading
        // power is absorbed by the dominant one — its colony and unit change hands, and the flag is set (once).
        Game game = Game.New(Classic, Seed);
        AdvanceTo1600(game);

        List<Player> powers = game.Players
            .Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial)
            .OrderBy(p => p.PlayerId).Take(2).ToList();
        Player strong = powers[0];
        Player weak = powers[1];

        Colony strongColony = FoundColonyFor(game, strong);
        Colony weakColony = FoundColonyFor(game, weak);
        strongColony.Liberty = Colony.LibertyPerRebel * strongColony.Population;     // SoL 100 → dominant
        weakColony.Liberty = 0;                                                       // SoL 0 → fading
        Unit weakUnit = SpawnLandUnitFor(game, weak);
        int weakColonyId = weakColony.Id;
        int weakUnitId = weakUnit.Id;

        Assert.False(game.SpanishSuccessionDone);
        game.EndTurn(); // RunSpanishSuccession fires (year ≥ 1600, clear strong/weak pair)

        Assert.True(game.SpanishSuccessionDone);
        Assert.Equal(strong.PlayerId, game.Colonies.Single(c => c.Id == weakColonyId).OwnerId); // colony ceded
        Assert.Equal(strong.PlayerId, game.Units.Single(u => u.Id == weakUnitId).OwnerId);      // unit ceded
        Assert.Empty(game.Colonies.Where(c => c.OwnerId == weak.PlayerId));                     // the fading power is emptied
    }

    [Fact]
    public void SpanishSuccession_DoesNotFire_WithoutAClearStrongWeakPair()
    {
        // Two AI powers both fervent (SoL > 50): no fading power, so the succession does not consolidate them.
        Game game = Game.New(Classic, Seed);
        AdvanceTo1600(game);
        List<Player> powers = game.Players
            .Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial)
            .OrderBy(p => p.PlayerId).Take(2).ToList();
        Colony c0 = FoundColonyFor(game, powers[0]);
        Colony c1 = FoundColonyFor(game, powers[1]);
        c0.Liberty = Colony.LibertyPerRebel * c0.Population; // both at SoL 100 → no fading power
        c1.Liberty = Colony.LibertyPerRebel * c1.Population;

        game.EndTurn();

        Assert.False(game.SpanishSuccessionDone); // no clear weak (SoL < 50) power → no absorption
        Assert.Equal(powers[1].PlayerId, game.Colonies.Single(c => c.Id == c1.Id).OwnerId); // still its owner
    }

    /// <summary>Runs end-turns until the calendar reaches (or passes) the Spanish-Succession trigger year 1600.</summary>
    private static void AdvanceTo1600(Game game)
    {
        while (game.CurrentYear < 1600)
        {
            game.EndTurn();
        }
    }

    /// <summary>Founds a colony owned by <paramref name="power"/> on the first free coastal land tile not adjacent to an existing colony (reusing a spawned colonist).</summary>
    private static Colony FoundColonyFor(Game game, Player power)
    {
        Position spot = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater)
            && !p.Neighbours().Any(n => game.Map.InBounds(n) && game.ColonyAt(n) is not null)); // colony footprints never touch
        Unit colonist = game.SpawnUnit(game.Ruleset.Unit(Game.StartingUnitTypeId), spot);
        colonist.OwnerId = power.PlayerId;
        return game.FoundColony(colonist);
    }

    /// <summary>Spawns a free colonist on open land owned by <paramref name="power"/> (a unit to be ceded in the succession).</summary>
    private static Unit SpawnLandUnitFor(Game game, Player power)
    {
        Position spot = EmptyLand(game, 1)[0];
        Unit unit = game.SpawnUnit(game.Ruleset.Unit(Game.StartingUnitTypeId), spot);
        unit.OwnerId = power.PlayerId;
        return unit;
    }
}
