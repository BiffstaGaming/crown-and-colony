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
    public void NationalSonsOfLiberty_IsThePerColonyUnweightedAverage()
    {
        // FreeCol Player.getSoL (Player.java:1409-1413): the UNWEIGHTED average of each colony's own SoL% with Java
        // integer (floor) division — NOT rebels-over-population. A pop-10 colony at 100% + a pop-1 colony at 0%
        // average to (100 + 0) / 2 = 50; the old population-weighted formula gave 10·100/11 = 90 (86d3hzz4w).
        (Game game, Colony fervent) = RebellionReady();
        fervent.Population = 10;
        fervent.Liberty = Colony.LibertyPerRebel * fervent.Population; // SoL 100
        Position unclaimed = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && !game.Map.IsNativeOwned(p)
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), unclaimed);
        Colony apathetic = game.FoundColony(colonist); // population 1, Liberty 0 → SoL 0
        apathetic.Liberty = 0;
        Assert.Equal(100, fervent.SonsOfLiberty);
        Assert.Equal(0, apathetic.SonsOfLiberty);

        Assert.Equal(50, game.NationalSonsOfLiberty(game.HumanPlayer)); // (100 + 0) / 2, not 90

        fervent.Liberty = 0; // both colonies at 0 → average 0
        Assert.Equal(0, game.NationalSonsOfLiberty(game.HumanPlayer));
    }

    [Fact]
    public void NationalSonsOfLiberty_IsZeroWithNoColonies()
    {
        // FreeCol Player.getSoL returns 0 for a player with no colonies (colonies.isEmpty() → 0).
        Game game = Game.New(Classic, Seed);
        Assert.DoesNotContain(game.Colonies, c => c.OwnerId == game.HumanPlayer.PlayerId);
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

    // ── Item 8b: specialised REF combat doctrine (86d3drn5a, FreeCol REFAIPlayer) ────────────────────────

    /// <summary>Clears the REF roster down to nothing (so a test can place a single controlled REF unit).</summary>
    private static void ClearRef(Game game, Player refP)
    {
        foreach (Unit u in game.Units.Where(u => u.OwnerId == refP.PlayerId).ToList())
        {
            game.Disband(u);
        }
    }

    [Fact]
    public void Ref_DoesNotChaseRebelFieldUnits_UntilAColonyIsCaptured()
    {
        // FreeCol REFAIPlayer.adjustMission: "Do not chase units until at least one colony is captured." A REF land
        // unit standing next to a lone rebel soldier — with no REF-held colony yet — must NOT attack it; it heads for
        // the rebel's colony instead. We pin the rule by asserting the adjacent rebel unit is left untouched.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        ClearRef(game, refP);

        // A lone rebel soldier in the open field, away from the colony, with a REF king's regular right beside it.
        Position fieldTile = EmptyLand(game, 1).First(p => !p.IsAdjacentTo(colony.Position) && p != colony.Position);
        Unit rebelSoldier = game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), fieldTile);
        Position refTile = fieldTile.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.ColonyAt(n) is null
            && game.NativeSettlementAt(n) is null && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        game.SpawnUnit(Classic.Unit("model.unit.kingsRegular"), refTile, refP.PlayerId);
        Assert.DoesNotContain(game.Colonies, c => c.OwnerId == refP.PlayerId); // the REF holds no colony

        int noticesBefore = game.CombatNotices.Count;
        game.EndTurn(); // RunRefTurn — colony-only doctrine, the field soldier is ignored

        // The rebel soldier is unharmed and on the map (it was never attacked); no new combat notice fired against it.
        Unit survivor = game.Units.Single(u => u.Id == rebelSoldier.Id);
        Assert.Equal("model.unit.veteranSoldier", survivor.Type.Id);
        Assert.True(survivor.IsOnMap);
        Assert.Equal(noticesBefore, game.CombatNotices.Count);
    }

    [Fact]
    public void Ref_NavyHuntsTheRebelsShips()
    {
        // FreeCol REFNavyGoalDecider + the navy block in REFAIPlayer.initialize: a man-o-war seek-and-destroys rebel
        // warships. A REF man-o-war beside a rebel ship engages it — we pin the doctrine by asserting an attack fires
        // (a combat notice is recorded), regardless of the RNG outcome.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        ClearRef(game, refP);

        // A rebel ship on the water by the colony, with a REF man-o-war on an adjacent water tile.
        Position rebelWater = colony.Position.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        game.SpawnUnit(Classic.Unit("model.unit.caravel"), rebelWater);
        Position refWater = rebelWater.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater
            && n != colony.Position && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        game.SpawnUnit(Classic.Unit("model.unit.manOWar"), refWater, refP.PlayerId);

        int noticesBefore = game.CombatNotices.Count;
        game.EndTurn(); // RunRefTurn — the man-o-war hunts and engages the rebel ship

        Assert.True(game.CombatNotices.Count > noticesBefore); // the REF navy attacked the rebel ship
    }

    [Fact]
    public void Ref_MarchesOnTheRebelColony_WhenNoFieldUnitIsInRange()
    {
        // FreeCol REFAIPlayer.findColonyTargets: with no rebel field unit near, a REF land unit closes on the rebel's
        // (connected-port) colony — it steps toward it rather than idling. Place a king's regular a few tiles off the
        // colony (within the seek ladder, but not adjacent so it marches rather than assaults) with a known closer land
        // neighbour, and assert it ends the turn nearer the colony.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        ClearRef(game, refP);
        // Disband the rebel's units so nothing distracts the REF (and the colony is the only target).
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }

        // A land staging tile 2-6 tiles from the colony (not adjacent → it marches, not assaults) that has at least one
        // legal land neighbour strictly closer to the colony — so a single step must reduce the Chebyshev distance.
        int Dist(Position p) => Math.Max(Math.Abs(p.X - colony.Position.X), Math.Abs(p.Y - colony.Position.Y));
        Position start = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)
            && Dist(p) is >= 2 and <= 6
            && p.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
                && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
                && !game.Units.Any(u => u.IsOnMap && u.Position == n) && Dist(n) < Dist(p)));
        Unit regular = game.SpawnUnit(Classic.Unit("model.unit.kingsRegular"), start, refP.PlayerId);
        int distanceBefore = Dist(start);

        game.EndTurn(); // RunRefTurn — the regular marches on the colony

        Unit moved = game.Units.Single(u => u.Id == regular.Id);
        Assert.True(Dist(moved.Position) < distanceBefore,
            $"REF regular should close on the rebel colony ({distanceBefore} → {Dist(moved.Position)})");
    }

    // ── REF/rebellion combat modifiers (86d3e4bkk): bombardBonus + popularSupport + ambushPenalty ────────

    /// <summary>
    /// Spawns a REF king's regular on a land tile adjacent to <paramref name="colony"/>, ready to assault it. Returns
    /// the regular. The REF's roster is cleared first so it is the only REF unit in play.
    /// </summary>
    private static Unit RefRegularBeside(Game game, Player refP, Colony colony)
    {
        ClearRef(game, refP);
        Position refTile = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.ColonyAt(n) is null
            && game.NativeSettlementAt(n) is null && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        return game.SpawnUnit(Classic.Unit("model.unit.kingsRegular"), refTile, refP.PlayerId);
    }

    [Fact]
    public void RefSiege_BombardBonusAndPopularSupport_ShiftTheOdds_VersusAnOrdinaryCapture()
    {
        // L2 REF-siege scenario. A REF king's regular assaults a rebel colony defended by a lone veteran soldier
        // garrison. The corrected odds fold in (a) the REF's +50% bombard bonus on offence and (b) the colony's
        // popular-support defence (100−SoL% for a REF attacker). We pin the corrected attacker win probability against
        // a hand-computed FreeCol figure, then show it differs from the old (pre-fix) odds with neither modifier.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);

        // Garrison the colony with a veteran soldier (the defender DefenderAt will pick); SoL 40% (a contested town).
        game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population * 40 / 100; // ~40% Sons-of-Liberty
        Assert.Equal(40, colony.SonsOfLiberty);

        Game.CombatOdds odds = game.CombatOddsAgainst(RefRegularBeside(game, refP, colony), colony.Position)!;

        // Attacker: king's regular base offence (4) × 1.5 attack bonus × 1.5 bombard bonus.
        double baseOff = Classic.Unit("model.unit.kingsRegular").Offence;
        Assert.Equal(baseOff * 1.5 * 1.5, odds.AttackPower, 4);
        // Defender: veteran-soldier (unarmed) defence × popular support (REF attacker → 100−40 = +60% defence).
        double baseDef = Classic.Unit("model.unit.veteranSoldier").Defence;
        Assert.Equal(baseDef * 1.60, odds.DefencePower, 4);
        Assert.Equal(odds.AttackPower / (odds.AttackPower + odds.DefencePower), odds.WinProbability, 6);

        // Contrast: the SAME assault with NEITHER modifier (the old, wrong odds) — offence loses the bombard ×1.5 and
        // defence loses the +60% popular support. The corrected odds must DIFFER (the bug was that they were equal to
        // this un-modified figure); here the two roughly cancel so the corrected odds land a touch below — the point is
        // that the resolution is no longer the bare attack-bonus-vs-bare-defence number it used to be.
        double oldAttack = baseOff * 1.5;        // attack bonus only, no bombard
        double oldDefence = baseDef;             // no popular support
        double oldWin = oldAttack / (oldAttack + oldDefence);
        Assert.NotEqual(oldWin, odds.WinProbability, 6);
    }

    [Fact]
    public void RefSiege_PopularSupport_FavoursTheDefenderMoreInAHighLoyaltyRebelTown()
    {
        // The popular-support flip (100−SoL% for a REF attacker) means the REF has an EASIER time against a high-SoL
        // (deeply rebel) town than a low-SoL one: high SoL → small loyalist defence bonus; low SoL → large one.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position);

        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // 100% SoL → REF faces 0% popular support
        double winHighSoL = game.CombatOddsAgainst(RefRegularBeside(game, refP, colony), colony.Position)!.WinProbability;

        colony.Liberty = Colony.LibertyPerRebel * colony.Population * 20 / 100; // 20% SoL → REF faces +80% popular support
        double winLowSoL = game.CombatOddsAgainst(RefRegularBeside(game, refP, colony), colony.Position)!.WinProbability;

        Assert.True(winHighSoL > winLowSoL,
            $"REF should win more easily against a 100%-SoL town ({winHighSoL:F3}) than a 20% one ({winLowSoL:F3})");
    }

    [Fact]
    public void NativeAttacker_OnARebelColony_GetsNoBombardOrPopularSupport()
    {
        // ADR-009 byte-identity guard: a NATIVE attacker (a brave) assaulting the same garrisoned rebel colony gets
        // neither the bombard/colony-assault bonus (natives never get it — it is a European powers' regulars bonus,
        // 86d3kgbp3) nor popular support (it is not a War-of-Independence battle) — its odds are the plain
        // attack-bonus vs role-defence figure, exactly as before this feature.
        (Game game, Colony colony) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer); // the colony is rebel-held
        colony.Liberty = Colony.LibertyPerRebel * colony.Population * 40 / 100; // 40% SoL — would matter only in a WoI battle
        game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position); // garrison

        // A native brave on an adjacent NON-ambush (open) tile so the ambush bonus does not muddy the offence figure.
        // Reuse a live brave's nation-type id so the owner is a real native nation.
        string nationTypeId = game.NativeUnits.First().OwnerNationId!;
        Position tile = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && !game.Map.TerrainAt(n).AmbushTerrain
            && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), tile, nationTypeId);

        Game.CombatOdds odds = game.CombatOddsAgainst(brave, colony.Position)!;

        // Offence: brave base offence × 1.5 attack bonus only (no bombard — natives never get it).
        Assert.Equal(Classic.Unit("model.unit.brave").Offence * 1.5, odds.AttackPower, 4);
        // Defence: bare (unarmed) veteran-soldier defence, no popular support despite the 40% SoL (not a WoI battle).
        Assert.Equal(Classic.Unit("model.unit.veteranSoldier").Defence, odds.DefencePower, 4);
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

        // Measure one quiet turn's baseline bell accrual (the rebel's surviving colony produces a little liberty each
        // turn — with the REF now landing in staggered waves, the colony is not overrun turn-one and keeps producing).
        game.EndTurn();
        int baselineGain = rebel.InterventionBells - bellsAfterSeed;

        int before = rebel.InterventionBells;
        rebel.Liberty += 250; // simulate a turn's extra liberty production on top of the colony's baseline
        game.EndTurn();

        Assert.Equal(before + baselineGain + 250, rebel.InterventionBells); // the injected gain banked atop the baseline
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
        // The friendly ally's landfall must not perturb the human's stream 0 (ADR-009 isolation): it draws only on the
        // dedicated InterventionStreamId. We resolve the war directly (not a full EndTurn) so only the intervention
        // landing runs — a full turn would also run the rebel's own colony economy, which legitimately draws stream 0.
        (Game game, _) = RebellionReady(4242);
        game.DeclareIndependence(game.HumanPlayer);
        game.ResolveWarOfIndependence(); // seed the liberty snapshot (gain 0 on first sight)
        RandomState frozen = game.RandomState;

        game.HumanPlayer.InterventionBells = Classic.InterventionBells;
        game.ResolveWarOfIndependence(); // the intervention force lands — only the ally's dedicated stream is drawn

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

    // ── Item 1: Native re-stancing on the Declaration of Independence (86d3drn4p → full mechanism 86d3fq0b9,
    //    FreeCol InGameController.java:1482-1548) ────────────────────────────────────────────────────────────

    /// <summary>Marks every settlement of <paramref name="nation"/> chief-visited by <paramref name="playerId"/> — the contact proxy for FreeCol <c>hasContacted</c>.</summary>
    private static void VisitNation(Game game, string nation, int playerId)
    {
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
        {
            s.MarkVisitedBy(playerId);
        }
    }

    /// <summary>Pins the transient tribe-tension channel to an exact value (raise/lower relative to the current level).</summary>
    private static void SetTribeTension(Game game, string nation, int playerId, int target) =>
        game.RaiseTribeTension(nation, playerId, target - game.TribeTensionFor(nation, playerId));

    /// <summary>The native player owning <paramref name="nationId"/> (drives <c>DetermineNativeStances</c> in tests).</summary>
    private static Player NationPlayer(Game game, string nationId) =>
        game.Players.First(p => p.PlayerType == PlayerType.Native && p.NationId == nationId);

    /// <summary>The first <paramref name="count"/> distinct native nations (stable id order), asserted to exist.</summary>
    private static List<string> DistinctNations(Game game, int count)
    {
        List<string> nations = game.NativeSettlements.Select(s => s.NationTypeId).Distinct()
            .OrderBy(n => n, System.StringComparer.Ordinal).Take(count).ToList();
        Assert.Equal(count, nations.Count); // the classic map seeds several native nations
        return nations;
    }

    [Fact]
    public void DeclareIndependence_LeastHostileContactedNation_BecomesTheAlly_HatefulTowardTheRef()
    {
        // FreeCol InGameController.java:1482-1548: the LEAST-hostile contacted nation is the ALLY (`good = first` of
        // the tension-ascending sort) — at peace its tension toward the rebel is untouched (delta 0) and it turns
        // hateful (1000, HATEFUL.limit) toward the REF; the MOST-hostile contacted nation not already at war is the
        // ENEMY — set hateful (1000) toward the rebel with an immediate WAR stance, its REF tension zeroed.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        List<string> nations = DistinctNations(game, 2);
        (string ally, string enemy) = (nations[0], nations[1]);
        VisitNation(game, ally, rebel.PlayerId);
        VisitNation(game, enemy, rebel.PlayerId);
        SetTribeTension(game, ally, rebel.PlayerId, 50);   // least hostile
        SetTribeTension(game, enemy, rebel.PlayerId, 900); // most hostile, stance still Peace (never derived)
        int allyTensionBefore = game.TribeTensionFor(ally, rebel.PlayerId);
        Dictionary<int, int> enemyAlarmsBefore = game.NativeSettlements.Where(s => s.NationTypeId == enemy)
            .ToDictionary(s => s.Id, s => s.AlarmFor(rebel.PlayerId));

        game.DeclareIndependence(rebel);
        int refId = game.Players.First(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce).PlayerId;

        // The ally: at peace → tension toward the rebel unchanged (FreeCol's PEACE branch, delta 0)…
        Assert.Equal(allyTensionBefore, game.TribeTensionFor(ally, rebel.PlayerId));
        Assert.Equal(Stance.Peace, game.NativeStanceToward(ally, rebel.PlayerId));
        // …and hateful toward the King's force — on the nation channel AND its settlements (the persistence path).
        Assert.Equal(NativeSettlement.MaxAlarm, game.TribeTensionFor(ally, refId));
        Assert.Equal(AlarmLevel.Hateful, game.TribeAlarmLevelFor(ally, refId));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == ally),
            s => Assert.Equal(NativeSettlement.MaxAlarm, s.AlarmFor(refId)));

        // The enemy: pinned to HATEFUL (1000) toward the rebel with an immediate transient WAR stance; each of its
        // camps gained the same delta FreeCol's csModifyTension propagates (1000 − 900 = +100); REF channel zero.
        Assert.Equal(NativeSettlement.MaxAlarm, game.TribeTensionFor(enemy, rebel.PlayerId));
        Assert.Equal(Stance.War, game.NativeStanceToward(enemy, rebel.PlayerId));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == enemy),
            s => Assert.Equal(enemyAlarmsBefore[s.Id] + 100, s.AlarmFor(rebel.PlayerId)));
        Assert.Equal(0, game.TribeTensionFor(enemy, refId));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == enemy),
            s => Assert.Equal(0, s.AlarmFor(refId)));
    }

    [Fact]
    public void DeclareIndependence_AllyAtWar_IsCalmedToContent()
    {
        // FreeCol's WAR branch: a formally-warring ally is calmed TO CONTENT.limit (600) — the delta lands on the
        // nation channel and (csModifyTension) on every one of its camps alike.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        string nation = DistinctNations(game, 1).Single();
        VisitNation(game, nation, rebel.PlayerId);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
        {
            game.ChangeNativeAlarm(s, rebel.PlayerId, NativeSettlement.MaxAlarm); // camps at Hateful (1000)
        }
        SetTribeTension(game, nation, rebel.PlayerId, 1100); // past the >1010 war threshold
        game.DetermineNativeStances(NationPlayer(game, nation));
        Assert.Equal(Stance.War, game.NativeStanceToward(nation, rebel.PlayerId)); // formally at war before declaring

        game.DeclareIndependence(rebel);

        // Calmed to exactly CONTENT (600): tension 1100 → 600, so every camp drops by the same −500 (1000 → 500).
        Assert.Equal(NativeSettlement.AlarmContentMax, game.TribeTensionFor(nation, rebel.PlayerId));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(NativeSettlement.MaxAlarm - 500, s.AlarmFor(rebel.PlayerId)));
        // No direct stance write for the ALLY: at 600 (> the ≤590 cease-fire limit) the war cools only through the
        // normal DetermineNativeStances hysteresis on later turns — faithful de-escalation, not an instant flip.
        Assert.Equal(Stance.War, game.NativeStanceToward(nation, rebel.PlayerId));
    }

    [Fact]
    public void DeclareIndependence_SingleContactedNation_HasNoEnemyHalf()
    {
        // FreeCol's reverse scan breaks immediately at `good` → bad == null: a single contacted nation is the ally
        // only — nobody is set hateful toward the rebel and no WAR stance appears.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        string nation = DistinctNations(game, 1).Single();
        VisitNation(game, nation, rebel.PlayerId);
        SetTribeTension(game, nation, rebel.PlayerId, 400); // Peace stance → the ally's tension stays untouched
        int before = game.TribeTensionFor(nation, rebel.PlayerId);

        game.DeclareIndependence(rebel);
        int refId = game.Players.First(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce).PlayerId;

        Assert.Equal(before, game.TribeTensionFor(nation, rebel.PlayerId));            // ally half: Peace → delta 0
        Assert.Equal(Stance.Peace, game.NativeStanceToward(nation, rebel.PlayerId));   // no enemy half → no WAR write
        Assert.Equal(NativeSettlement.MaxAlarm, game.TribeTensionFor(nation, refId));  // ally half still runs vs the REF
    }

    [Fact]
    public void DeclareIndependence_ThirdNation_IsUntouched()
    {
        // Three contacted nations: the least-hostile allies, the most-hostile turns enemy — the middle one keeps its
        // tension, stance and settlement alarm (FreeCol touches exactly `good` and `bad`, nobody else).
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        List<string> nations = DistinctNations(game, 3);
        (string ally, string middle, string enemy) = (nations[0], nations[1], nations[2]);
        foreach (string nation in nations)
        {
            VisitNation(game, nation, rebel.PlayerId);
        }
        SetTribeTension(game, ally, rebel.PlayerId, 50);
        SetTribeTension(game, middle, rebel.PlayerId, 500);
        SetTribeTension(game, enemy, rebel.PlayerId, 900);
        Dictionary<int, int> middleAlarmsBefore = game.NativeSettlements.Where(s => s.NationTypeId == middle)
            .ToDictionary(s => s.Id, s => s.AlarmFor(rebel.PlayerId));

        game.DeclareIndependence(rebel);
        int refId = game.Players.First(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce).PlayerId;

        // The middle nation is wholly untouched — tension, stance, REF channel, and every camp's alarm.
        Assert.Equal(500, game.TribeTensionFor(middle, rebel.PlayerId));
        Assert.Equal(Stance.Peace, game.NativeStanceToward(middle, rebel.PlayerId));
        Assert.Equal(0, game.TribeTensionFor(middle, refId));
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == middle),
            s => Assert.Equal(middleAlarmsBefore[s.Id], s.AlarmFor(rebel.PlayerId)));
        // Sanity: the halves landed on the right nations.
        Assert.Equal(NativeSettlement.MaxAlarm, game.TribeTensionFor(ally, refId));
        Assert.Equal(Stance.War, game.NativeStanceToward(enemy, rebel.PlayerId));
    }

    [Fact]
    public void DeclareIndependence_LeavesUncontactedNativesUntouched()
    {
        // Only a nation the rebel has CONTACTED can realign (FreeCol filters on hasContacted). A hostile but
        // never-met nation keeps its alarm.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        NativeSettlement uncontacted = game.NativeSettlements.First();
        game.ChangeNativeAlarm(uncontacted, NativeSettlement.MaxAlarm); // hateful, but never visited
        int before = uncontacted.Alarm;

        game.DeclareIndependence(rebel);

        Assert.Equal(before, game.NativeSettlements.First(s => s.Id == uncontacted.Id).Alarm); // untouched (never met)
    }

    [Fact]
    public void DeclareIndependence_NativeRealignment_RoundTripsSaveLoad()
    {
        // The realignment persists WITHOUT a save field: the ally's REF hate and the enemy's rebel hate ride the v55
        // per-settlement alarm channels, and the transient tribe channels re-derive from them on load.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        List<string> nations = DistinctNations(game, 2);
        (string ally, string enemy) = (nations[0], nations[1]);
        VisitNation(game, ally, rebel.PlayerId);
        VisitNation(game, enemy, rebel.PlayerId);
        SetTribeTension(game, ally, rebel.PlayerId, 50);
        SetTribeTension(game, enemy, rebel.PlayerId, 900);
        game.DeclareIndependence(rebel);
        int refId = game.Players.First(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce).PlayerId;
        Dictionary<int, int> enemyAlarms = game.NativeSettlements.Where(s => s.NationTypeId == enemy)
            .ToDictionary(s => s.Id, s => s.AlarmFor(rebel.PlayerId));

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.All(loaded.NativeSettlements.Where(s => s.NationTypeId == ally),
            s => Assert.Equal(NativeSettlement.MaxAlarm, s.AlarmFor(refId)));           // the ally's REF hate persisted
        Assert.All(loaded.NativeSettlements.Where(s => s.NationTypeId == enemy),
            s => Assert.Equal(enemyAlarms[s.Id], s.AlarmFor(rebel.PlayerId)));          // the enemy's rebel hate persisted
        // The transient tribe channels re-derive from the angriest persisted camp (the documented lazy seed): the
        // ally's REF channel comes back at the full 1000; the enemy's rebel channel at its angriest camp's alarm.
        Assert.Equal(NativeSettlement.MaxAlarm, loaded.TribeTensionFor(ally, refId));
        Assert.Equal(enemyAlarms.Values.Max(), loaded.TribeTensionFor(enemy, rebel.PlayerId));
    }

    [Fact]
    public void DeclareIndependence_NativeReStancing_IsByteStableOnStreamZero()
    {
        // The re-stancing is RNG-free — twins (same seed) advance stream 0 identically through the declaration, with
        // BOTH halves (ally + enemy) running, and serialize byte-identically afterwards.
        (Game a, _) = RebellionReady(7777);
        (Game b, _) = RebellionReady(7777);
        foreach (Game g in new[] { a, b })
        {
            List<string> nations = DistinctNations(g, 2);
            VisitNation(g, nations[0], g.HumanPlayer.PlayerId);
            VisitNation(g, nations[1], g.HumanPlayer.PlayerId);
            SetTribeTension(g, nations[0], g.HumanPlayer.PlayerId, 50);
            SetTribeTension(g, nations[1], g.HumanPlayer.PlayerId, 900);
        }
        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);
        Assert.Equal(a.RandomState, b.RandomState);
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    // ── Item 2: War-time mercenary (Hessian) offer on declaration (86d3c9vdb) ─────────────────────────────

    [Fact]
    public void DeclareIndependence_OffersWarMercenaries_WhenAffordable()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 100000; // plenty to fund the whole fixed force untrimmed

        game.DeclareIndependence(rebel);

        // A pending Hessian offer is surfaced (the same seam as the in-game mercenary offers), not auto-applied.
        Assert.NotNull(game.PendingMonarchDemand);
        Assert.Equal(MonarchAction.HessianMercenaries, game.PendingMonarchDemand!.Action);
        Assert.NotNull(game.PendingMonarchDemand.Offer);

        // 86d3fq0eg/86d3fpztm: the declaration force is the FIXED model.option.mercenaryForce, not the land-only
        // periodic generator — so a well-funded rebel is offered the whole classic roster: 3 armed + 3 mounted veterans,
        // 3 artillery, and — the parity fix — 2 men-o-war (11 units), priced by hire price (3·2000 + 3·2000 + 3·500 +
        // 2·10000 = 33500). Previously the old LoadMercenaries generator offered only 2-3 groups of armed veterans.
        var offer = game.PendingMonarchDemand.Offer!;
        Assert.Equal(11, offer.Sum(e => e.Count));
        Assert.Equal(2, offer.Single(e => e.UnitTypeId == "model.unit.manOWar").Count); // the parity gap: a rebel navy
        Assert.Equal(3, offer.Single(e => e.UnitTypeId == "model.unit.veteranSoldier" && e.RoleId == "model.role.soldier").Count);
        Assert.Equal(3, offer.Single(e => e.UnitTypeId == "model.unit.veteranSoldier" && e.RoleId == "model.role.dragoon").Count);
        Assert.Equal(3, offer.Single(e => e.UnitTypeId == "model.unit.artillery").Count);
        Assert.Equal(33500, game.PendingMonarchDemand.Price);
    }

    [Fact]
    public void DeclareIndependence_AcceptingMercenaries_SpawnsTheMenOWar_OnTheRebelsEuropeDock()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 100000; // fund the whole fixed force so both men-o-war are offered

        game.DeclareIndependence(rebel);
        Assert.Equal(2, game.PendingMonarchDemand!.Offer!.Single(e => e.UnitTypeId == "model.unit.manOWar").Count);

        game.RespondToMonarch(accept: true);

        // The two hired men-o-war arrive on the rebel's Europe dock (SpawnInEurope seats naval units at the Europe
        // entry tile, InEurope) — the rebel now has a navy to sail out and face the REF at sea.
        Assert.Equal(2, game.Units.Count(u => u.OwnerId == rebel.PlayerId
            && u.Location == UnitLocation.InEurope && u.Type.Id == "model.unit.manOWar"));
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

    [Fact]
    public void DeclareIndependence_MercenaryOffer_IsTrimmedToTheTreasury_WhenTheWholeForceIsUnaffordable()
    {
        // A rebel who cannot afford the whole 33500 roster is offered an affordability-trimmed subset (FreeCol
        // loadMercenaryForce drops random units until the running total is payable). With 5000 gold the offer never
        // exceeds the treasury, and — as at least one land unit (cheapest 500) is affordable — an offer is still made.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.Gold = 5000;

        game.DeclareIndependence(rebel);

        Assert.NotNull(game.PendingMonarchDemand);
        Assert.True(game.PendingMonarchDemand!.Offer!.Sum(e => e.Count) > 0);
        Assert.True(game.PendingMonarchDemand.Price <= 5000); // trimmed to what the treasury can pay
        Assert.True(game.PendingMonarchDemand.Price > 0);
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

    [Fact]
    public void WithVictoryConditions_OverridesTheThreeFlags_AndIsHonouredByWinner()
    {
        // The New-Game game-options section (86d3drn64) threads the player's victory picks via
        // Ruleset.WithVictoryConditions — a configuration override of which already-implemented win checks fire.
        // 1) The override flips exactly the three flags the dialog exposes.
        Ruleset overridden = Ruleset.LoadClassic().WithVictoryConditions(defeatRef: false, defeatEuropeans: false, defeatHumans: true);
        Assert.False(overridden.VictoryDefeatRef);
        Assert.False(overridden.VictoryDefeatEuropeans);
        Assert.True(overridden.VictoryDefeatHumans);

        // 2) A fresh load is unaffected — the spec defaults still parse (REF on / Europeans on / Humans off): the
        //    override mutates only the instance it is called on (each load is a fresh, unshared parse).
        Ruleset fresh = Ruleset.LoadClassic();
        Assert.True(fresh.VictoryDefeatRef);
        Assert.True(fresh.VictoryDefeatEuropeans);
        Assert.False(fresh.VictoryDefeatHumans);

        // 3) End-to-end: the engine honours the override. defeatHumans is OFF by spec default, so a default game has no
        //    winner at turn 1; the same game built from the WithVictoryConditions-enabled ruleset awards the lone human
        //    the win — proving the dialog's toggle actually changes the game outcome, not just a flag.
        Assert.Null(Game.New(Ruleset.LoadClassic(), Seed).Winner);
        Game enabled = Game.New(
            Ruleset.LoadClassic().WithVictoryConditions(defeatRef: false, defeatEuropeans: false, defeatHumans: true), Seed);
        Assert.Equal(enabled.HumanPlayer, enabled.Winner);
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
        // Robust to whatever colonies the OTHER AI powers founded over the 108-turn advance: make EVERY AI colony
        // dominant (SoL 100), then drop the fading power's colonies alone to 0. So `weak` is the unique fading power
        // and `strong` (the lowest-id AI power, hence the lowest-id SoL-100 power) the unique dominant — regardless of
        // how the AI evolved. (RunSpanishSuccession ranks across all AI colonial powers with colonies, not just two.)
        foreach (Colony col in AiColonies(game)) col.Liberty = Colony.LibertyPerRebel * col.Population; // SoL 100
        foreach (Colony col in game.Colonies.Where(c => c.OwnerId == weak.PlayerId)) col.Liberty = 0;   // weak fades
        Unit weakUnit = SpawnLandUnitFor(game, weak);
        int weakColonyId = weakColony.Id;
        int weakUnitId = weakUnit.Id;

        Assert.False(game.SpanishSuccessionDone);
        game.EndTurn(); // RunSpanishSuccession fires (year ≥ 1600, clear strong/weak pair)

        Assert.True(game.SpanishSuccessionDone);
        Assert.Equal(strong.PlayerId, game.Colonies.Single(c => c.Id == weakColonyId).OwnerId); // colony ceded
        Assert.Equal(strong.PlayerId, game.Units.Single(u => u.Id == weakUnitId).OwnerId);      // unit ceded
        Assert.DoesNotContain(game.Colonies, c => c.OwnerId == weak.PlayerId);                  // the fading power is emptied
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
        // Robust: make EVERY AI colony dominant (SoL 100) — including any the other AI powers founded during the
        // advance — so no fading (SoL < 50) power exists anywhere and the succession cannot consolidate.
        foreach (Colony col in AiColonies(game)) col.Liberty = Colony.LibertyPerRebel * col.Population;

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

    /// <summary>Every colony owned by a non-human colonial (European) AI power — the players the Spanish Succession ranks.</summary>
    private static IEnumerable<Colony> AiColonies(Game game) =>
        game.Colonies.Where(c => game.Players.Any(p =>
            p.PlayerId == c.OwnerId && !p.IsHuman && p.PlayerType == PlayerType.Colonial));

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

    // ── A4 (86d3e49jp): a rival AI can declare independence ───────────────────────────────────────────────
    // FreeCol's AI does NOT self-declare (getRebelStrengthRatio is debug-logging only) — this is a faithful-SPIRIT
    // addition: a dominant AI colonial power that out-strengthens the amassed REF (1.5×, the CheckForRefDefeat
    // yardstick) declares through the same DeclareIndependence the human UI calls. The gate is RNG-free (ADR-009).

    private const string KingsRegular = "model.unit.kingsRegular";
    private const string InfantryRole = "model.role.infantry";

    /// <summary>The first non-human colonial power.</summary>
    private static Player AiPower(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    /// <summary>
    /// Arms <paramref name="power"/> with king's-regular infantry standing in <paramref name="colony"/> until its land
    /// strength clears the AI-declaration gate (<see cref="Game.RefLandStrength"/> × 1.5 + a margin). Returns the count
    /// spawned. The veterans garrison the colony tile (so the muster can draw them too).
    /// </summary>
    private static int ArmPastTheRefRatio(Game game, Player power, Colony colony)
    {
        int spawned = 0;
        double target = 1.5 * game.RefLandStrength() + 1; // strictly clear 1.5×
        while (game.LandPowerOf(power) < target)
        {
            Unit regular = game.SpawnUnit(game.Ruleset.Unit(KingsRegular), colony.Position, power.PlayerId);
            regular.RoleId = InfantryRole; // an armed king's regular — high land offence
            spawned++;
        }
        return spawned;
    }

    /// <summary>An AI colonial power with one coastal colony forced to 100% national SoL (the SoL/port/year half of the gate met).</summary>
    private static (Game Game, Player Power, Colony Colony) AiRebellionReady(ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        Player power = AiPower(game);
        Colony colony = FoundColonyFor(game, power);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // force this AI's national SoL to 100%
        return (game, power, colony);
    }

    [Fact]
    public void RefLandStrength_EqualsTheLandPowerOfTheSpawnedRefAfterDeclaration()
    {
        // The un-spawned Force computation must match the live LandPowerOf the gate is measured against, so the gate
        // and the win (CheckForRefDefeat) speak the same units — read it before, declare, sum it after.
        (Game game, _) = RebellionReady();
        double before = game.RefLandStrength();
        game.DeclareIndependence(game.HumanPlayer);
        Assert.True(before > 0);
        Assert.Equal(before, game.LandPowerOf(Ref(game)), precision: 6);
    }

    [Fact]
    public void ExpeditionaryForceStrength_MatchesTheBaseRefCounts_WithoutMaterialisingIt()
    {
        // The read-only Military-report oracle reports the King's growing army's land/naval counts and must NOT store
        // a lazily-built base force (RefForceOrNull stays the saved truth — no spurious save state, ADR-009).
        (Game game, _) = RebellionReady();
        (int land, int naval) = game.ExpeditionaryForceStrength();
        Assert.Equal(31 + 15 + 14, land); // medium base: infantry + cavalry + artillery
        Assert.Equal(8, naval);            // 8 men-o-war
        Assert.Null(game.RefForceOrNull);  // the read did not materialise/store the base force
    }

    [Fact]
    public void ExpeditionaryForceComposition_BreaksTheRefDownByTypeAndRole_WithoutMaterialisingIt()
    {
        // The REF intelligence-report oracle (86d3fq0d8) projects the King's force to per-block counts, land first then
        // naval, and — like ExpeditionaryForceStrength — must not store a lazily-built base force (ADR-009).
        (Game game, _) = RebellionReady();
        IReadOnlyList<Game.RefForceBlock> blocks = game.ExpeditionaryForceComposition();

        // The medium base REF: King's Regular (infantry) + King's Regular (cavalry) + Artillery on land, Man-o-War naval.
        Assert.Equal(31, blocks.Single(b => !b.IsNaval && b.UnitTypeId == KingsRegular && b.RoleId == InfantryRole).Count);
        Assert.Equal(15, blocks.Single(b => !b.IsNaval && b.UnitTypeId == KingsRegular && b.RoleId == "model.role.cavalry").Count);
        Assert.Equal(14, blocks.Single(b => !b.IsNaval && b.UnitTypeId == "model.unit.artillery").Count);
        Assert.Equal(8, blocks.Single(b => b.IsNaval && b.UnitTypeId == "model.unit.manOWar").Count);
        // Land blocks come before naval blocks (force order), and the totals match the count oracle.
        Assert.Equal(31 + 15 + 14, blocks.Where(b => !b.IsNaval).Sum(b => b.Count));
        Assert.Equal(8, blocks.Where(b => b.IsNaval).Sum(b => b.Count));
        Assert.Null(game.RefForceOrNull); // the read did not materialise/store the base force
    }

    [Fact]
    public void NationRanking_RanksColonialPowersByScoreDescending()
    {
        // The score / nation report oracle (86d3fq0fb): every colonial-or-independent power, ordered by final score
        // (PlayerScore) descending. Gifting the human gold lifts its score (⌊0.001·gold⌋) so it sorts to the top.
        (Game game, _, _) = AiRebellionReady();
        game.HumanPlayer.Gold += 1_000_000; // a colossal treasury → the human's score dominates

        IReadOnlyList<Game.NationStanding> ranking = game.NationRanking();
        Assert.NotEmpty(ranking);
        // Sorted by score descending (stable id tiebreak).
        for (int i = 1; i < ranking.Count; i++)
        {
            Assert.True(ranking[i - 1].Score >= ranking[i].Score);
        }
        // The human, now the richest, ranks first, and each standing's score matches the score oracle.
        Assert.True(ranking[0].Player.IsHuman);
        foreach (Game.NationStanding s in ranking)
        {
            Assert.Equal(game.PlayerScore(s.Player), s.Score);
        }
    }

    [Fact]
    public void TensionLevelBetween_BandsTheRawTensionThroughFreeColsLevels()
    {
        // The Foreign-Affairs attitude oracle (86d3fq0e6): the raw TensionBetween scalar banded by FreeCol's
        // Tension.Level thresholds (Happy ≤ 100, Content ≤ 600, Displeased ≤ 700, Angry ≤ 800, else Hateful).
        (Game game, Player power, _) = AiRebellionReady();
        int human = game.HumanPlayer.PlayerId;

        // A fresh pair starts at zero tension → Happy.
        Assert.Equal(AlarmLevel.Happy, game.TensionLevelBetween(human, power.PlayerId));

        // Raising tension across the thresholds bumps the band; the oracle agrees with the raw scalar's band.
        game.ChangeTension(human, power.PlayerId, 650);  // into the Displeased band (601–700)
        Assert.Equal(AlarmLevel.Displeased, game.TensionLevelBetween(human, power.PlayerId));
        game.ChangeTension(human, power.PlayerId, 400);  // 1050 → past Hateful (801+)
        Assert.Equal(AlarmLevel.Hateful, game.TensionLevelBetween(human, power.PlayerId));
    }

    [Fact]
    public void HumanMilitaryStrength_CountsTheHumansLandUnits_AndReportsTheGateYardstickPower()
    {
        (Game game, Colony colony) = RebellionReady();
        Player human = game.HumanPlayer;
        int landBefore = game.HumanMilitaryStrength().LandUnits;

        Unit soldier = game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), colony.Position, human.PlayerId);
        soldier.RoleId = InfantryRole; // arm it so it carries land offence

        (double landPower, int land, _) = game.HumanMilitaryStrength();
        Assert.Equal(landBefore + 1, land);                            // one more land unit counted
        Assert.Equal(game.LandPowerOf(human), landPower, precision: 6); // power matches the independence-gate yardstick
        Assert.True(landPower > 0);
    }

    [Fact]
    public void ColonialStrength_TotalsAPlayersLandAndNavalAttackPower()
    {
        // The Foreign-Affairs oracle (86d3f0wcg) reports a rival colonial power's combined land + naval attack power
        // (FreeCol NationSummary.getMilitaryStrength/getNavalStrength). It must agree with the land/naval power
        // yardsticks and grow as the power is armed.
        (Game game, Player power, Colony colony) = AiRebellionReady();
        (double landBefore, double navalBefore) = game.ColonialStrength(power);

        // Arm a land unit (an armed king's regular carries land offence) and float a man-o-war on the coastal water.
        Unit regular = game.SpawnUnit(game.Ruleset.Unit(KingsRegular), colony.Position, power.PlayerId);
        regular.RoleId = InfantryRole;
        Position water = colony.Position.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        game.SpawnUnit(game.Ruleset.Unit("model.unit.manOWar"), water, power.PlayerId);

        (double land, double naval) = game.ColonialStrength(power);
        Assert.Equal(game.LandPowerOf(power), land, precision: 6);
        Assert.Equal(game.NavalPowerOf(power), naval, precision: 6);
        Assert.True(land > landBefore);   // the armed regular lifted land strength
        Assert.True(naval > navalBefore); // the man-o-war lifted naval strength
    }

    [Fact]
    public void ShouldAiDeclareIndependence_IsFalse_UntilTheAiOutStrengthsTheRef()
    {
        (Game game, Player power, Colony colony) = AiRebellionReady();
        // SoL + port + year are met, but the AI has no army → the REF-strength half of the gate fails.
        Assert.True(game.CheckDeclareIndependence(power).Allowed);
        Assert.False(game.ShouldAiDeclareIndependence(power));

        // Arm it past 1.5× the amassed REF — now every limb of the gate is satisfied.
        ArmPastTheRefRatio(game, power, colony);
        Assert.True(game.ShouldAiDeclareIndependence(power));
    }

    [Fact]
    public void ShouldAiDeclareIndependence_IsRngFree()
    {
        // ADR-009: the gate reads SoL/ports/year/strength only — it must draw NO randomness (least of all stream 0),
        // whichever side of the threshold the power sits on.
        (Game game, Player power, Colony colony) = AiRebellionReady();
        RandomState before = game.RandomState;
        Assert.False(game.ShouldAiDeclareIndependence(power)); // under-strength branch
        ArmPastTheRefRatio(game, power, colony);
        Assert.True(game.ShouldAiDeclareIndependence(power));   // over-strength branch
        Assert.Equal(before, game.RandomState);                 // neither branch advanced stream 0
    }

    [Fact]
    public void ShouldAiDeclareIndependence_NeverFiresForTheHuman()
    {
        // The human pulls its own trigger from the UI; the AI auto-gate must ignore it even at 100% SoL + a huge army.
        (Game game, Colony colony) = RebellionReady();
        Player human = game.HumanPlayer;
        for (int i = 0; i < 200; i++)
        {
            Unit r = game.SpawnUnit(game.Ruleset.Unit(KingsRegular), colony.Position, human.PlayerId);
            r.RoleId = InfantryRole;
        }
        Assert.True(game.LandPowerOf(human) >= 1.5 * game.RefLandStrength());
        Assert.False(game.ShouldAiDeclareIndependence(human));
    }

    [Fact]
    public void ShouldAiDeclareIndependence_IsFalse_WhenLandlockedOrPastTheLastColonialYear()
    {
        // The strength half alone is not enough — the CheckDeclareIndependence limbs (a port, the year) still bind.
        // Landlocked: a colony with no coastal neighbour fails the connected-port limb.
        Game game = Game.New(Classic, Seed);
        Player power = AiPower(game);
        Position inland = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater)
            && !p.Neighbours().Any(n => game.Map.InBounds(n) && game.ColonyAt(n) is not null));
        Unit colonist = game.SpawnUnit(game.Ruleset.Unit(Game.StartingUnitTypeId), inland, power.PlayerId);
        Colony landlocked = game.FoundColony(colonist);
        landlocked.Liberty = Colony.LibertyPerRebel * landlocked.Population;
        ArmPastTheRefRatio(game, power, landlocked);
        Assert.False(game.ShouldAiDeclareIndependence(power)); // armed + 100% SoL, but no port
    }

    [Fact]
    public void AiPower_DeclaresIndependence_OnItsTurn_WhenItOutStrengthsTheRef()
    {
        // The headline acceptance criterion: a dominant AI power reaches the gate → it calls DeclareIndependence on its
        // own turn (run by EndTurn) → flips to Rebel, forfeits its Europe units, musters regulars, the REF spawns at war.
        (Game game, Player power, Colony colony) = AiRebellionReady();
        ArmPastTheRefRatio(game, power, colony);
        // Park a unit of this power in Europe — the declaration forfeits it (FreeCol: in/bound-for-Europe units lost).
        Unit inEurope = game.SpawnUnit(game.Ruleset.Unit(Game.StartingUnitTypeId), colony.Position, power.PlayerId);
        inEurope.Location = UnitLocation.InEurope;
        int europeUnitId = inEurope.Id;
        Assert.Equal(PlayerType.Colonial, power.PlayerType);

        game.EndTurn(); // the AI power takes its turn → MaybeDeclareIndependence fires

        Assert.Equal(PlayerType.Rebel, power.PlayerType);                              // it rebelled
        Assert.DoesNotContain(game.Units, u => u.Id == europeUnitId);                   // the Europe unit is forfeit
        Player refP = Ref(game);                                                        // a REF now exists…
        Assert.Equal(Stance.War, game.StanceBetween(refP.PlayerId, power.PlayerId));    // …at war with the new rebel
        Assert.Contains(game.Units, u => u.OwnerId == refP.PlayerId);                   // its force is mustered
    }

    [Fact]
    public void AfterAiDeclares_TheRefLands_AndTheAiRebelSurvivesAndDefends()
    {
        // Acceptance: ResolveWarOfIndependence + RunRefTurn run against an AI rebel unchanged (the REF lands & assaults),
        // and the AI rebel at minimum defends (it runs the colonial path: garrison/arming per A1, and stays alive).
        (Game game, Player power, Colony colony) = AiRebellionReady();
        ArmPastTheRefRatio(game, power, colony);
        game.EndTurn();                       // turn 1: the AI declares; the REF is still mustering in Europe
        Assert.Equal(PlayerType.Rebel, power.PlayerType);
        Player refP = Ref(game);

        for (int i = 0; i < 4; i++)
        {
            game.EndTurn();                   // the REF sails in and lands (RunRefTurn), the rebel runs its colonial path
        }

        Assert.Contains(game.Units, u => u.OwnerId == refP.PlayerId && u.IsOnMap);      // the King's army made landfall
        Assert.True(power.PlayerType is PlayerType.Rebel or PlayerType.Independent);    // the rebel still stands (defended)
    }

    [Fact]
    public void AiDeclaration_DrawsNothingFromStream0()
    {
        // ADR-009: an AI declaring (and the war that follows) draws ONLY the per-player/REF streams — never the human's
        // stream 0. Two games share a seed and an idle human; in one the AI is armed past the gate (so it declares and
        // the REF war runs), in the other it is left under-strength (so it never declares). The human's stream-0 state
        // (Game.RandomState) must be byte-identical between them: nothing on the AI-declaration / REF-war path touched it.
        (Game declares, Player dPower, Colony dColony) = AiRebellionReady(4242);
        (Game quiet, Player qPower, _) = AiRebellionReady(4242);
        ArmPastTheRefRatio(declares, dPower, dColony); // only this game's AI reaches the gate

        for (int i = 0; i < 6; i++)
        {
            declares.EndTurn();
            quiet.EndTurn();
        }

        Assert.Equal(PlayerType.Rebel, dPower.PlayerType);        // the armed AI rebelled…
        Assert.Equal(PlayerType.Colonial, qPower.PlayerType);     // …the under-strength one never did
        Assert.Equal(quiet.RandomState, declares.RandomState);    // yet the human's stream 0 is identical — untouched (ADR-009)
    }

    [Fact]
    public void AiDeclaration_IsTwinDeterministic()
    {
        // Twin games (same seed, same provocation) stay byte-identical across the whole declare-and-war arc.
        (Game a, Player pa, Colony ca) = AiRebellionReady(909090);
        (Game b, Player pb, Colony cb) = AiRebellionReady(909090);
        ArmPastTheRefRatio(a, pa, ca);
        ArmPastTheRefRatio(b, pb, cb);
        for (int i = 0; i < 5; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(PlayerType.Rebel, pa.PlayerType);                      // it really did declare in the driven game
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson()); // byte-identical whole-state
    }

    // ── Intervention force grows every interventionTurns (86d3e4bm9, FreeCol Monarch.updateInterventionForce) ──

    /// <summary>The land-unit count of a grown intervention force (the part that scales with the war's length).</summary>
    private static int LandCount(InterventionForceComposition force) =>
        force.Units.Where(u => Classic.Unit(u.UnitTypeId).IsNaval is false).Sum(u => u.Count);

    [Fact]
    public void GrownInterventionForce_GrowsLandBlocksByOnePerInterval()
    {
        // FreeCol updateInterventionForce: updates = turn / interventionTurns extra of each land block. Classic medium
        // is 2+2+2 land (6) + 2 men-o-war, interventionTurns = 52. The growth is deterministic on the turn alone.
        (Game game, _) = RebellionReady();
        int turns = Classic.InterventionTurns; // 52

        // Before the first interval (turns 1..51): the base force, unchanged — no growth yet.
        Assert.Equal(Classic.InterventionForce.TotalCount, game.GrownInterventionForce(1).TotalCount);
        Assert.Equal(6, LandCount(game.GrownInterventionForce(turns - 1)));

        // One interval (turn 52): each of the 3 land blocks gains 1 → 3+3+3 = 9 land units.
        InterventionForceComposition oneInterval = game.GrownInterventionForce(turns);
        Assert.Equal(9, LandCount(oneInterval));
        Assert.All(oneInterval.Units.Where(u => Classic.Unit(u.UnitTypeId).IsNaval is false),
            u => Assert.Equal(3, u.Count));

        // Two intervals (turn 104): 4+4+4 = 12 land. Three intervals (turn 156): 5+5+5 = 15 land — monotone growth.
        Assert.Equal(12, LandCount(game.GrownInterventionForce(turns * 2)));
        Assert.Equal(15, LandCount(game.GrownInterventionForce(turns * 3)));
    }

    [Fact]
    public void GrownInterventionForce_AddsTransportShips_SoTheEnlargedLandForceStillFitsTheFleet()
    {
        // FreeCol Force.prepareToBoard: once the grown land force needs more hold slots than the 2 base men-o-war
        // provide (2 × space 6 = 12 slots), extra men-o-war are added to carry it. At 3 intervals the land force is
        // 15 units (15 slots > 12) so the fleet must grow beyond the base 2 men-o-war.
        (Game game, _) = RebellionReady();
        InterventionForceComposition force = game.GrownInterventionForce(Classic.InterventionTurns * 3);

        int navalCount = force.Units.Where(u => Classic.Unit(u.UnitTypeId).IsNaval).Sum(u => u.Count);
        int capacity = force.Units.Where(u => Classic.Unit(u.UnitTypeId).IsNaval)
            .Sum(u => Classic.Unit(u.UnitTypeId).Space * u.Count);
        int required = force.Units.Where(u => Classic.Unit(u.UnitTypeId).IsNaval is false)
            .Sum(u => Classic.Unit(u.UnitTypeId).CarrySlots * u.Count);

        Assert.True(navalCount > 2, "the fleet grew past the 2 base men-o-war to carry the enlarged land force");
        Assert.True(capacity >= required, "the grown fleet can berth the whole enlarged land force (no troops stranded)");
    }

    /// <summary>Lands the intervention force at a chosen turn by round-tripping the in-flight war through a save with
    /// that turn, then resolving — a faithful long-rebellion snapshot without driving dozens of real turns.</summary>
    private static int LandInterventionAtTurn(Game declared, int turn)
    {
        SaveGame snapshot = SaveGame.From(declared) with { Turn = turn };
        Game atTurn = snapshot.Restore(Classic);
        Player rebel = atTurn.HumanPlayer;
        rebel.InterventionBells = Classic.InterventionBells; // at the threshold — the next resolution lands the ally
        int before = atTurn.Units.Count(u => u.OwnerId == rebel.PlayerId && (u.IsOnMap || u.IsAboard));
        atTurn.ResolveWarOfIndependence(); // the ally lands (the REF is still mustering, so the fleet arrives unmolested)
        return atTurn.Units.Count(u => u.OwnerId == rebel.PlayerId && (u.IsOnMap || u.IsAboard)) - before;
    }

    [Fact]
    public void LongRebellion_BringsProgressivelyLargerAllyLandings()
    {
        // L2 scenario: the same rebellion, held out to ever-later turns, draws ever-bigger ally landings (FreeCol
        // updateInterventionForce). Base force = 8 (6 land + 2 naval). Each interval adds 3 land units (+ transport).
        (Game game, _) = RebellionReady(4242);
        game.DeclareIndependence(game.HumanPlayer);

        int early = LandInterventionAtTurn(game, 1);                                    // before the first interval: the base 8
        int oneInterval = LandInterventionAtTurn(game, Classic.InterventionTurns);       // +3 land = 11 (+ any transport)
        int twoIntervals = LandInterventionAtTurn(game, Classic.InterventionTurns * 2);  // +6 land
        int threeIntervals = LandInterventionAtTurn(game, Classic.InterventionTurns * 3); // +9 land (+ extra men-o-war)

        Assert.Equal(Classic.InterventionForce.TotalCount, early); // turn < interventionTurns → the unchanged base force
        Assert.True(oneInterval > early, "one interval of war brings a larger landing than the first");
        Assert.True(twoIntervals > oneInterval, "two intervals brings a larger landing than one");
        Assert.True(threeIntervals > twoIntervals, "the landings keep growing the longer the war drags on");
    }

    // ── Voluntary retirement (86d3fq125, FreeCol InGameController.retire) ──────────────────────────────────

    [Fact]
    public void Retire_RecordsTheScore_AndEndsTheGameForThePlayer()
    {
        (Game game, _) = RebellionReady();
        Player human = game.HumanPlayer;
        Assert.True(game.CheckRetire(human).Allowed); // an active colonial power may retire
        int expectedScore = game.PlayerScore(human);

        HighScore score = game.Retire(human, gameId: "g1");

        Assert.Equal(expectedScore, score.Score);          // the leaderboard entry carries the final score
        Assert.Equal(game.Turn, score.RetirementTurn);     // …stamped with the retirement turn (FreeCol retirementTurn)
        Assert.Equal("g1", score.GameId);                  // …and the per-game id for de-dup
        Assert.Equal(PlayerType.Retired, human.PlayerType); // the player has withdrawn — the game is over for them
        Assert.True(game.IsHumanRetired);
    }

    [Fact]
    public void CheckRetire_RejectsAnAlreadyEndedPlayer()
    {
        (Game game, _) = RebellionReady();
        game.Retire(game.HumanPlayer);
        Assert.False(game.CheckRetire(game.HumanPlayer).Allowed); // a retired player can't retire again
        Assert.Throws<InvalidMoveException>(() => game.Retire(game.HumanPlayer));
    }

    [Fact]
    public void Retire_OnAnIndependentWinner_RecordsAVictory()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.GiveIndependence(Ref(game), game.HumanPlayer); // the human won its independence
        HighScore score = game.Retire(game.HumanPlayer);
        Assert.True(score.Won); // retiring as an independent nation records a win, not a defeat
    }

    [Fact]
    public void Retire_DrawsNothingFromStreamZero()
    {
        // Retiring is a pure read + a type flip — it must leave the human's stream 0 byte-identical (ADR-009).
        (Game game, _) = RebellionReady();
        RandomState frozen = game.RandomState;
        game.Retire(game.HumanPlayer);
        Assert.Equal(frozen, game.RandomState);
    }

    [Fact]
    public void RetiredPlayerType_RoundTripsSaveLoad()
    {
        (Game game, _) = RebellionReady();
        game.Retire(game.HumanPlayer);
        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(PlayerType.Retired, loaded.HumanPlayer.PlayerType);
        Assert.True(loaded.IsHumanRetired);
    }

    // ── Continue playing after winning (86d3fq161, FreeCol InGameController.continuePlaying) ───────────────

    [Fact]
    public void ContinuePlaying_ClearsTheWinAndLetsTheGameProceed()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.GiveIndependence(Ref(game), game.HumanPlayer);
        Assert.Equal(game.HumanPlayer, game.Winner); // the human has won
        Assert.True(game.CanContinuePlaying);

        game.ContinuePlaying();

        Assert.Null(game.Winner);                 // the victory conditions are disabled — no winner now
        Assert.True(game.VictoryConditionsDisabled);
        Assert.False(game.CanContinuePlaying);    // already continuing — the option is spent
        game.EndTurn();                           // the game proceeds without re-firing the win
        Assert.Null(game.Winner);
    }

    [Fact]
    public void CanContinuePlaying_IsFalse_WhileTheGameIsStillRunning()
    {
        (Game game, _) = RebellionReady();
        Assert.Null(game.Winner);
        Assert.False(game.CanContinuePlaying); // no winner yet — nothing to continue past
        game.ContinuePlaying();                // a no-op
        Assert.False(game.VictoryConditionsDisabled);
    }

    [Fact]
    public void ContinuePlaying_DrawsNothingFromStreamZero()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.GiveIndependence(Ref(game), game.HumanPlayer);
        RandomState frozen = game.RandomState;
        game.ContinuePlaying();
        Assert.Equal(frozen, game.RandomState); // flipping config booleans draws no stream (ADR-009)
    }

    [Fact]
    public void ContinuePlaying_RoundTripsSaveLoad_WinStaysDisabled()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.GiveIndependence(Ref(game), game.HumanPlayer);
        game.ContinuePlaying();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.True(loaded.VictoryConditionsDisabled);
        Assert.Null(loaded.Winner); // the disabled win persisted — the reload does not re-fire victory
    }

    // ── REF staggered reinforcement waves + the King's morale (86d3fq0ak) ──────────────────────────────────

    [Fact]
    public void Ref_LandsInSuccessiveWaves_NotAllAtOnce()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        Player refP = Ref(game);
        int totalLand = game.Units.Count(u => u.OwnerId == refP.PlayerId && !u.Type.IsNaval);
        Assert.True(totalLand >= 60); // the whole army mustered in Europe at the declaration

        // The REF's first turn brings only the first echelon ashore — NOT the whole army at once.
        game.EndTurn();
        int ashoreFirstWave = game.Units.Count(u => u.OwnerId == refP.PlayerId && u.IsOnMap && !u.Type.IsNaval);
        int stillInEurope = game.Units.Count(u => u.OwnerId == refP.PlayerId && u.Location == UnitLocation.InEurope && !u.Type.IsNaval);
        Assert.True(ashoreFirstWave > 0, "the first wave landed");
        Assert.True(ashoreFirstWave < totalLand, "but not the whole army at once");
        Assert.True(stillInEurope > 0, "later waves wait in Europe");

        // The next echelon is held for the wave interval, then a second wave comes ashore — strictly more troops on the
        // field than after the first wave (proving the staggered reinforcement). RNG-free: a fixed cadence.
        for (int i = 0; i < RefWaveIntervalConst + 1; i++)
        {
            game.EndTurn();
        }
        int ashoreSecondWave = game.Units.Count(u => u.OwnerId == refP.PlayerId && u.IsOnMap && !u.Type.IsNaval);
        Assert.True(ashoreSecondWave > ashoreFirstWave, "a later wave brought fresh redcoats ashore");
    }

    [Fact]
    public void RefLanding_FiresExactlyOneWarning_OnTheFirstLanding_NotOnLaterWaves()
    {
        // After the human declares and the King's army first comes ashore, exactly ONE RefLandingNotice is produced —
        // the player-facing "the REF has landed" warning (FreeCol's REF-arrival message). The later staggered
        // reinforcement waves do NOT re-warn (gated on no REF unit having been ashore before the turn). RNG-free.
        (Game game, Colony colony) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        Player refP = Ref(game);
        // Before the first landing the whole REF is still in Europe → no warning yet.
        Assert.Empty(game.RefLandingNotices);
        Assert.DoesNotContain(game.Units, u => u.OwnerId == refP.PlayerId && u.IsOnMap);

        // First REF turn: the first echelon lands → exactly one warning, naming the threatened rebel colony.
        game.EndTurn();
        Assert.Contains(game.Units, u => u.OwnerId == refP.PlayerId && u.IsOnMap);
        Assert.Single(game.RefLandingNotices);
        Assert.Equal(colony.Name, game.RefLandingNotices[0].NearestColonyName);

        // Every subsequent turn (including the next reinforcement wave) produces NO further landing warning — the
        // per-turn notice reset clears it and the gate (REF already ashore) keeps it from re-firing.
        for (int i = 0; i < RefWaveIntervalConst * 2 + 2; i++)
        {
            game.EndTurn();
            Assert.Empty(game.RefLandingNotices);
        }
    }

    [Fact]
    public void RefDefeat_CountsTheUnLandedWaves_SoTheWinCannotFireBeforeReinforcementsAreSpent()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        game.EndTurn(); // only the first wave is ashore; the rest waits in Europe

        // Disband everything ASHORE — but the un-landed waves still in Europe keep the King in the fight.
        foreach (Unit u in game.Units.Where(u => u.OwnerId == refP.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        Assert.Contains(game.Units, u => u.OwnerId == refP.PlayerId && u.Location == UnitLocation.InEurope);
        Assert.False(game.CheckForRefDefeat(refP, rebel)); // reinforcements survive in Europe → not yet broken
    }

    [Fact]
    public void RefMorale_Breaks_WhenMostOfTheCommittedArmyIsDestroyed_AndTheKingWithdraws()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        Player refP = Ref(game);
        Assert.False(game.RefMoraleBroken); // a full army — resolve intact
        int peak = game.RefMoralePeak;
        Assert.True(peak > 0);

        // Grind the King's land army down below a quarter of its peak (and below the 7-land hang-on floor), keeping a
        // couple of ships. The rebel here is weak (no army to speak of), so the 1.5×-power path does NOT fire — only the
        // King's broken morale (three-quarters of the army he committed is gone) ends the war. This is the distinct
        // value of the morale trigger: a thoroughly-beaten King gives up even when neither side clearly out-musters.
        ReduceRefTo(game, refP, keepLand: 3, keepNaval: 2); // 3 < peak/4 and < 7 → morale broken, below the floor
        Assert.True(game.RefMoraleBroken);
        Assert.False(game.LandPowerOf(rebel) >= 1.5 * game.LandPowerOf(refP)); // the rebel is NOT 1.5× stronger
        Assert.True(game.CheckForRefDefeat(refP, rebel)); // …yet the morale break alone satisfies the defeat test

        game.EndTurn(); // ResolveWarOfIndependence withdraws the King → independence

        Assert.Equal(PlayerType.Independent, rebel.PlayerType);
        Assert.Equal(rebel, game.Winner);
    }

    [Fact]
    public void Ref_WavesAndMorale_AreDeterministic_AcrossTwinGames()
    {
        // The whole staggered-reinforcement + morale machinery is RNG-free, so the same seed produces byte-identical
        // wave timing, morale, and REF unit positions across twin games (ADR-009 determinism).
        (Game a, _) = RebellionReady(4242);
        (Game b, _) = RebellionReady(4242);
        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);
        for (int i = 0; i < RefWaveIntervalConst * 2 + 2; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(a.RefWaveCountdown, b.RefWaveCountdown);
        Assert.Equal(a.RefMorale, b.RefMorale);
        Assert.Equal(a.RefMoralePeak, b.RefMoralePeak);
        var aRef = a.Units.Where(u => u.OwnerId == Ref(a).PlayerId).Select(u => (u.Id, u.Position, u.Location)).OrderBy(x => x.Id).ToList();
        var bRef = b.Units.Where(u => u.OwnerId == Ref(b).PlayerId).Select(u => (u.Id, u.Position, u.Location)).OrderBy(x => x.Id).ToList();
        Assert.Equal(aRef, bRef);
    }

    [Fact]
    public void RefReinforcementState_RoundTripsSaveLoad()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        game.EndTurn(); // the wave timer + morale are now non-default (a wave landed, more owed)
        int countdown = game.RefWaveCountdown;
        int morale = game.RefMorale;
        int peak = game.RefMoralePeak;
        Assert.True(peak > 0);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(countdown, loaded.RefWaveCountdown);
        Assert.Equal(morale, loaded.RefMorale);
        Assert.Equal(peak, loaded.RefMoralePeak);
    }

    [Fact]
    public void PreV62Save_LoadsCleanly_WithNoKeptPlayingOverride_AndNoPendingRefWaves()
    {
        // A v61 save (this slice's predecessor) carries none of the v62 fields. It must load with the victory still
        // enabled and the REF wave/morale state defaulted (countdown 0, morale 0) — byte-compatible down-version load.
        Game game = Game.New(Classic, Seed);
        SaveGame v62 = SaveGame.From(game);
        Assert.Equal(69, v62.Version); // current version (the v62 down-version logic below still holds — a fresh game omits the later fields too)

        // Simulate an older save: stamp the version back and drop the v62 fields (they were all omitted/default anyway).
        SaveGame asV61 = v62 with { Version = 61, VictoryConditionsDisabled = null, RefWaveCountdown = null, RefMoralePeak = null };
        Game loaded = SaveGame.FromJson(asV61.ToJson()).Restore(Classic);

        Assert.False(loaded.VictoryConditionsDisabled); // no kept-playing override
        Assert.Equal(0, loaded.RefWaveCountdown);        // no pending wave
        Assert.Equal(0, loaded.RefMorale);               // morale derives to 0 (no REF afield)
        Assert.Equal(0, loaded.RefMoralePeak);
    }

    private const int RefWaveIntervalConst = 4; // mirrors Game.RefWaveInterval (private)

    // ── No NEW founding fathers after the Declaration of Independence (classic) (86d3fq0fj) ───────────────
    //
    // FreeCol ServerPlayer.canRecruitFoundingFather: a COLONIAL player recruits freely; a REBEL/INDEPENDENT player only
    // under model.option.continueFoundingFatherRecruitment (classic default false). The bells→Liberty bake stays ungated
    // (Sons of Liberty must keep updating) and already-elected fathers keep aiding the war — only NEW recruitment stops.

    private const string Washington = "model.foundingFather.georgeWashington";

    [Fact]
    public void GameOption_ContinueFoundingFatherRecruitment_ParsesClassicFalse_AndSeamCanOverride()
    {
        Assert.False(Classic.GameOptions.ContinueFoundingFatherRecruitment);                 // classic ships defaultValue="false"
        Assert.False(GameOptions.ClassicDefaults.ContinueFoundingFatherRecruitment);          // fallback matches the spec
        Assert.True(Ruleset.LoadClassic().WithContinueFoundingFatherRecruitment(true).GameOptions.ContinueFoundingFatherRecruitment);
        Assert.False(Ruleset.LoadClassic().WithContinueFoundingFatherRecruitment(false).GameOptions.ContinueFoundingFatherRecruitment);
    }

    [Fact]
    public void Rebel_WithBankedLiberty_AndAChosenFather_ElectsNoNewFather_ClassicDefault()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        string target = game.OfferedFathers[0];
        rebel.CurrentFather = target;
        rebel.Liberty = game.TotalFoundingFatherCost() + 1000; // far more than the cost — a colonial would elect at once

        game.DeclareIndependence(rebel);                       // → Rebel; the Congress closes (classic)
        Assert.Equal(PlayerType.Rebel, rebel.PlayerType);

        game.EndTurn();                                        // the rebel runs the colonial bell/liberty path

        Assert.DoesNotContain(target, game.Congress);          // NO new father elected post-declaration
        Assert.Equal(target, game.CurrentFather);              // the choice is simply stalled, not consumed
    }

    [Fact]
    public void Rebel_BellsStillBakeIntoSonsOfLiberty_EvenThoughRecruitmentStops()
    {
        // The bake above the election gate is ungated: a rebel colony's bells still feed its colony Sons-of-Liberty
        // liberty each turn, so the SoL machinery keeps working after the Declaration (only NEW fathers are blocked).
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        game.DeclareIndependence(rebel);
        colony.Liberty = 0;                                    // zero the colony's accrued liberty

        game.EndTurn();                                        // the town hall makes bells → liberty (ungated bake)

        Assert.True(colony.Liberty > 0, "a rebel colony's bells must still bake into its Sons-of-Liberty liberty");
    }

    [Fact]
    public void ElectedWashington_StillAutoPromotes_ARebelsWinningUnit()
    {
        // The DONE half (already-elected fathers keep aiding the war): a rebel with Washington in Congress still gets the
        // automatic promotion on an ordinary win — the effect derives from player.Congress, not the player type.
        (Game game, Colony colony) = RebellionReady();
        Player rebel = game.HumanPlayer;
        rebel.CongressList.Add(Washington);
        game.DeclareIndependence(rebel);
        Assert.Equal(PlayerType.Rebel, rebel.PlayerType);
        Assert.Contains(Washington, game.Congress);            // Washington survives the Declaration

        // A rebel soldier standing beside a native brave, on free adjacent land.
        bool Free(Position n) =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n);
        Unit brave = game.NativeUnits.First(b => b.Position.Neighbours().Any(Free));
        Position spot = brave.Position.Neighbours().First(Free);
        Unit soldier = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), spot);
        soldier.OwnerId = rebel.PlayerId;
        soldier.RoleId = "model.role.soldier";
        soldier.RoleCount = 1;
        int id = soldier.Id;

        game.Attack(soldier, brave.Position, new FixedWin()); // an ordinary (non-great) win

        Assert.Equal("model.unit.veteranSoldier", game.Units.First(u => u.Id == id).Type.Id); // Washington promoted it
    }

    [Fact]
    public void Rebel_WithTheOptionOn_StillElectsNewFathers_PostDeclaration()
    {
        (Game game, _) = RebellionReady(Ruleset.LoadClassic().WithContinueFoundingFatherRecruitment(true));
        Player rebel = game.HumanPlayer;
        string target = game.OfferedFathers[0];
        rebel.CurrentFather = target;
        rebel.Liberty = game.TotalFoundingFatherCost() + 1000;

        game.DeclareIndependence(rebel);
        game.EndTurn();

        Assert.Contains(target, game.Congress);                // the option restores post-declaration recruitment
        Assert.Null(game.CurrentFather);                       // the choice was consumed by the election
    }

    // ── Name the new independent nation on declaring (86d3fq0a2, FreeCol declareIndependence(nationName, …)) ──
    //
    // The free nation takes the name the player chose at the Declaration (Player.IndependentNationName), which becomes
    // its display label once Rebel/Independent (getNationLabel → independentNationName). A blank name falls back to the
    // colonial nation's normal label. The name persists additively (v68, omit-when-null): a game that never declares
    // serialises byte-identically to v67, and a pre-v68 save loads every player with the name unset.

    [Fact]
    public void DeclareIndependence_WithAName_SetsIt_AndRelabelsTheNation()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;

        game.DeclareIndependence(rebel, "United States");

        Assert.Equal(PlayerType.Rebel, rebel.PlayerType);
        Assert.Equal("United States", rebel.IndependentNationName);
        Assert.Equal("United States", game.NationLabelOf(rebel)); // the label switches to the chosen name (getNationLabel)
    }

    [Fact]
    public void DeclareIndependence_TrimsTheChosenName()
    {
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;

        game.DeclareIndependence(rebel, "  Gran Colombia  ");

        Assert.Equal("Gran Colombia", rebel.IndependentNationName); // surrounding whitespace is trimmed
        Assert.Equal("Gran Colombia", game.NationLabelOf(rebel));
    }

    [Fact]
    public void DeclareIndependence_BlankName_FallsBackToTheColonialNationLabel()
    {
        // A blank/whitespace answer leaves IndependentNationName unset, so the label falls back to the nation's normal
        // display name. A nation-less default game labels "Anonymous" (FreeCol's anonymous fallback), unchanged by rebel.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        string colonialLabel = game.NationLabelOf(rebel); // captured while still Colonial

        game.DeclareIndependence(rebel, "   "); // whitespace ⇒ unset

        Assert.Null(rebel.IndependentNationName);
        Assert.Equal(colonialLabel, game.NationLabelOf(rebel)); // the label is unchanged — the default fallback applies
    }

    [Fact]
    public void DeclareIndependence_NameLessOverload_LeavesTheNameUnset()
    {
        // The kept name-less DeclareIndependence(player) overload routes through the naming one with a null name.
        (Game game, _) = RebellionReady();
        Player rebel = game.HumanPlayer;
        string colonialLabel = game.NationLabelOf(rebel);

        game.DeclareIndependence(rebel); // no name

        Assert.Null(rebel.IndependentNationName);
        Assert.Equal(colonialLabel, game.NationLabelOf(rebel));
    }

    [Fact]
    public void IndependentNationName_RoundTripsSaveLoad()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer, "United States");

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal("United States", loaded.HumanPlayer.IndependentNationName);
        Assert.Equal("United States", loaded.NationLabelOf(loaded.HumanPlayer)); // the label survives the reload
    }

    [Fact]
    public void IndependentNationName_IsOmittedWhenUnset_ByteIdenticalToPriorVersion()
    {
        // A game that never declared writes no IndependentNationName token, and the whole save is byte-identical to the
        // one produced with the field explicitly nulled everywhere — the omit-when-null proof the soak's byte-identity
        // check relies on. A fresh, never-declared game is the default case.
        Game game = Game.New(Classic, Seed);
        string json = SaveGame.From(game).ToJson();

        Assert.DoesNotContain("\"IndependentNationName\"", json); // WhenWritingNull → the field is absent

        // A rebel that took the default (blank) name also omits the field.
        (Game rebelGame, _) = RebellionReady();
        rebelGame.DeclareIndependence(rebelGame.HumanPlayer); // no name → unset
        string rebelJson = SaveGame.From(rebelGame).ToJson();
        Assert.DoesNotContain("\"IndependentNationName\"", rebelJson);
    }

    [Fact]
    public void NamedNation_WritesTheField()
    {
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer, "United States");
        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"IndependentNationName\"", json); // a named nation writes the field
        Assert.Contains("United States", json);
    }

    [Fact]
    public void PreV68Save_LoadsWithTheNameUnset()
    {
        // A v67 (pre-v68) save carries no per-player IndependentNationName. Simulate one by declaring, nulling the field
        // on every saved player (exactly what pre-v68 code would have written), and stamping the version back: a restored
        // rebel loads with the name unset and keeps labelling itself by its colonial nation name (the pre-this-feature
        // behaviour). The field rides the per-player Players[] collection and is omitted-when-null.
        (Game game, _) = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer, "United States");

        SaveGame current = SaveGame.From(game);
        SaveGame preV68Save = current with
        {
            Version = 67,
            Players = current.Players!.Select(p => p with { IndependentNationName = null }).ToList(),
        };
        Game preV68 = SaveGame.FromJson(preV68Save.ToJson()).Restore(Classic);

        Assert.Null(preV68.HumanPlayer.IndependentNationName); // no name restored from a pre-v68 save
        Assert.Equal(PlayerType.Rebel, preV68.HumanPlayer.PlayerType); // still a rebel — just labelled by the colonial name
    }

    /// <summary>A fixed RNG forcing an ordinary (non-great) combat win — NextDouble 0.5 lands in the win band.</summary>
    private sealed class FixedWin : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0.5;
        public RandomState SaveState() => new(0, 0);
    }
}
