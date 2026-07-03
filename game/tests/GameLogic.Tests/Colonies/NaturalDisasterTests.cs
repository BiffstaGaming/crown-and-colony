using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Natural disasters (<c>86d3c9uu8</c>, FreeCol <c>Disaster.java</c> + <c>ServerPlayer.csNaturalDisasters</c>): the
/// data model parsed from the spec's <c>&lt;disasters&gt;</c> block, plus the per-turn per-player roll. The roll
/// fires only when the ruleset's <c>model.option.naturalDisasters</c> percentage is above 0; the classic ruleset
/// ships it <b>0</b>, so the default classic game rolls no disasters (its economy is unchanged and its stream 0 is
/// never advanced). These tests assert the parse, the default-off path, and the on path (a spec with the option
/// forced to 100%).
/// </summary>
public class NaturalDisasterTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 7UL;

    /// <summary>The classic ruleset with <c>model.option.naturalDisasters</c> forced to 100% (everything else identical).</summary>
    private static readonly Ruleset DisastersAlways = LoadClassicWithDisasterChance(100);

    private static Ruleset LoadClassicWithDisasterChance(int percent)
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement option = doc.Descendants("percentageOption")
            .Single(o => (string?)o.Attribute("id") == "model.option.naturalDisasters");
        option.SetAttributeValue("defaultValue", percent.ToString());
        option.SetAttributeValue("value", percent.ToString());
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private static Colony FoundColony(Game game)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.AddGoods("model.goods.food", 100);
        // The disaster pool is now the colony's centre-tile terrain's disaster list (86d3fq0ye). Pin the centre tile to
        // plains (spec: tornado @ 100) so a strike-dependent test deterministically has a disaster to draw, regardless of
        // the seed's starting terrain (which may list none, e.g. grassland). Deterministic + RNG-free; a test that needs a
        // different terrain (or none) overrides this with its own SetTerrain after founding.
        game.Map.SetTerrain(colony.Position, game.Ruleset.Terrain("model.tile.plains"));
        return colony;
    }

    // ===== Data model =====

    [Fact]
    public void Classic_ParsesTheDisasters_IncludingBankruptcy_ResolvingTheExtendsChain()
    {
        // The bankruptcy disaster is special (not natural) and carries a building-production-penalty effect.
        Disaster? bankruptcy = Classic.FindDisaster(Disaster.BankruptcyId);
        Assert.NotNull(bankruptcy);
        Assert.False(bankruptcy!.Natural);

        // Flood extends the abstract model.disaster.common, so it inherits its effect list (the common effect set).
        Disaster? flood = Classic.FindDisaster("model.disaster.flood");
        Assert.NotNull(flood);
        Assert.True(flood!.Natural);
        Assert.NotEmpty(flood.Effects); // inherited from the common parent via extends

        // The abstract parent is never instantiated.
        Assert.Null(Classic.FindDisaster("model.disaster.common"));
    }

    [Fact]
    public void Classic_NaturalDisasters_AreTheNaturalOnes_AndExcludeBankruptcy()
    {
        Assert.NotEmpty(Classic.NaturalDisasters);
        Assert.All(Classic.NaturalDisasters, d => Assert.True(d.Natural));
        Assert.DoesNotContain(Classic.NaturalDisasters, d => d.Id == Disaster.BankruptcyId);
    }

    [Fact]
    public void Classic_NaturalDisasterPercentage_IsZeroByDefault()
    {
        Assert.Equal(0, Classic.NaturalDisasterPercentage);     // classic ships defaultValue="0"
        Assert.Equal(100, DisastersAlways.NaturalDisasterPercentage); // the forced spec
    }

    // ===== The roll =====

    [Fact]
    public void ClassicDefault_RollsNoDisasters()
    {
        Game game = Game.New(Classic, Seed);
        FoundColony(game);
        game.HumanPlayer.Gold = 1000;
        for (int i = 0; i < 10; i++)
        {
            game.EndTurn();
        }
        Assert.Empty(game.DisasterNotices); // option 0 → no roll ever fires
    }

    [Fact]
    public void DisasterChance100_StrikesAColony()
    {
        Game game = Game.New(DisastersAlways, Seed);
        Colony colony = FoundColony(game);
        // Stock gold + a goods stack so a loss-of-money / loss-of-goods effect has something to take.
        game.HumanPlayer.Gold = 1000;
        colony.AddGoods("model.goods.tobacco", 100);

        // At 100% a disaster is rolled every turn; over a few turns at least one effect must land on the colony.
        bool struck = false;
        for (int i = 0; i < 10 && !struck; i++)
        {
            game.EndTurn();
            struck = game.DisasterNotices.Count > 0;
        }
        Assert.True(struck);
        DisasterNotice notice = game.DisasterNotices[0];
        Assert.Equal(colony.Name, notice.ColonyName);
        // Some effect fired: gold lost, goods lost, or a production-penalty note.
        Assert.True(notice.GoldLost > 0 || notice.GoodsLost > 0 || notice.ProductionPenaltyApplied);
    }

    // ===== Production penalty (86d3fpyu9): a timed −50% on the struck colony's production, 3 turns =====

    private const string Sugar = "model.goods.sugar";
    private const string Rum = "model.goods.rum";
    private const string Free = "model.unit.freeColonist";

    /// <summary>The classic ruleset with the natural disaster forced to fire ONLY its production-penalty effects (tile +
    /// building), every other effect disabled — so a strike deterministically registers the timed production penalty.</summary>
    private static Ruleset LoadClassicWithOnlyProductionPenaltyDisaster()
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        // naturalDisasters on.
        XElement opt = doc.Descendants("percentageOption").Single(o => (string?)o.Attribute("id") == "model.option.naturalDisasters");
        opt.SetAttributeValue("defaultValue", "100");
        opt.SetAttributeValue("value", "100");
        // In the shared "several" effect list, force the two production-penalty effects to 100% and every other to 0%.
        foreach (XElement effect in doc.Descendants("effect"))
        {
            string id = (string?)effect.Attribute("id") ?? "";
            bool isProductionPenalty = id is "model.disaster.effect.lossOfTileProduction" or "model.disaster.effect.lossOfBuildingProduction";
            effect.SetAttributeValue("probability", isProductionPenalty ? "100" : "0");
        }
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private static Position TileProducing(Game game, string goods) =>
        game.Map.AllPositions().First(p => game.TileYieldPotential(p, goods) > 0);

    [Fact]
    public void ProductionPenalty_HalvesTheStruckColonysTileYield_ForThreeTurnsThenRecovers()
    {
        // Register the disaster penalty exactly as ApplyDisaster does: a colony-scoped timed −50% on sugar for 3 turns,
        // at DISASTER_PRODUCTION_INDEX (100). Assert the colony's tile yield drops ~50% while the window is open and
        // returns to full once it expires. Uses the colony-scoped TileYield overload the live colony turn now calls.
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        Position tile = TileProducing(game, Sugar);
        int full = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);
        Assert.True(full > 0, "need a tile that actually produces sugar");

        // Direct MakeTimed contract: MakeTimed(duration:3) is active [Turn, Turn+2] (3 inclusive turns). (ApplyDisaster
        // itself passes the spec duration + 1 so a spec-duration-3 disaster matches FreeCol's [Turn, Turn+3] — asserted
        // end-to-end in ApplyDisaster_WiresTheProductionPenalty below; here we exercise the raw fold/strip mechanic.)
        var payload = new FatherModifier(Sugar, ModifierType.Percentage, -50, 100);
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(payload, duration: 3, start: game.Turn, colonyId: colony.Id));

        int penalised = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);
        Assert.Equal(full - full / 2, penalised); // −50%, truncated (matches FreeCol's (int) cast)

        // Advance to the last active turn (start+2): still penalised.
        game.EndTurn(); // → start+1
        game.EndTurn(); // → start+2 (lastTurn) — still in window
        Assert.Equal(full - full / 2, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id));

        game.EndTurn(); // → start+3: the per-turn strip removes it
        Assert.Empty(game.TemporaryModifiers);
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id)); // recovered
    }

    [Fact]
    public void ProductionPenalty_OnlyHitsTheStruckColony_NotAnotherColonyNorNonColonyFolds()
    {
        // The penalty is registered against ONE colony's id. A query for a DIFFERENT colony (or a non-colony fold —
        // null id) must see full production: FreeCol scopes the disaster modifier to the struck colony only.
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        Position tile = TileProducing(game, Sugar);
        int full = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);

        int otherColonyId = colony.Id + 999; // an id no colony has → stands in for a different colony
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(
            new FatherModifier(Sugar, ModifierType.Percentage, -50, 100), duration: 3, start: game.Turn, colonyId: colony.Id));

        Assert.Equal(full - full / 2, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id)); // struck colony: penalised
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar, otherColonyId)); // another colony: untouched
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar)); // non-colony fold (null id): untouched
    }

    [Fact]
    public void ApplyDisaster_WiresTheProductionPenalty_AsAColonyScopedTimedModifier()
    {
        // End-to-end: with a spec whose natural disaster fires ONLY the production-penalty effects, a strike registers
        // colony-scoped timed modifiers (the -50% tile/building goods) and the notice flags the production penalty.
        Ruleset onlyPenalty = LoadClassicWithOnlyProductionPenaltyDisaster();
        Game game = Game.New(onlyPenalty, Seed);
        Colony colony = FoundColony(game);
        game.HumanPlayer.Gold = 1000;

        bool penaltyNoted = false;
        for (int i = 0; i < 10 && !penaltyNoted; i++)
        {
            game.EndTurn();
            penaltyNoted = game.DisasterNotices.Any(n => n.ProductionPenaltyApplied);
        }
        Assert.True(penaltyNoted, "the forced production-penalty disaster should have applied");

        // The struck colony now carries at least one colony-scoped timed penalty modifier (−50% on a good).
        var scoped = game.TemporaryModifiers.Where(m => m.ColonyId == colony.Id).ToList();
        Assert.NotEmpty(scoped);
        Assert.All(scoped, m => Assert.Equal(-50, m.Payload.Value));
        Assert.All(scoped, m => Assert.Equal(ModifierType.Percentage, m.Payload.Type));
        // FreeCol-faithful window: makeTimedModifier lastTurn = start + duration (the classic disaster spec duration is 3),
        // so the inclusive window spans FirstTurn..FirstTurn+3 (ApplyDisaster passes mod.Duration + 1).
        Assert.All(scoped, m => Assert.Equal(3, m.LastTurn - m.FirstTurn));
    }

    // ===== Persistence (86d3hz9ga, save v69 — FreeCol persists a timed Modifier's firstTurn/lastTurn via
    // Feature.writeAttributes, common/model/Feature.java L297-321): an active penalty survives save/reload
    // mid-window and still expires on the same turn; a default game omits the token entirely. =====

    [Fact]
    public void ProductionPenalty_SurvivesSaveLoad_MidWindow_AndExpiresOnTheSameTurn()
    {
        // Register the penalty exactly as ApplyDisaster does, advance one turn INTO the window, save, reload,
        // and assert: (a) the reloaded colony's tile yield is still the penalised pre-save value, and (b) the
        // reloaded penalty expires on the same turn as the uninterrupted twin's (no reload extension/reset).
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundColony(game);
        Position tile = TileProducing(game, Sugar);
        int full = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);
        Assert.True(full > 0, "need a tile that actually produces sugar");

        var payload = new FatherModifier(Sugar, ModifierType.Percentage, -50, 100);
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(payload, duration: 3, start: game.Turn, colonyId: colony.Id));
        game.EndTurn(); // mid-window: one active turn consumed, two remain (lastTurn = start + 2)
        int penalisedBeforeSave = game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id);
        Assert.Equal(full - full / 2, penalisedBeforeSave);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Colony loadedColony = loaded.Colonies.Single(c => c.Id == colony.Id);

        // (a) The reloaded game still folds the penalty: same penalised yield as before the save.
        Assert.Equal(penalisedBeforeSave, loaded.TileYield(loaded.HumanPlayer, Free, tile, Sugar, loadedColony.Id));
        // The reloaded modifier is field-identical to the live one (payload + window + colony scope, record equality).
        Assert.Equal(game.TemporaryModifiers, loaded.TemporaryModifiers);

        // (b) Expiry on the same turn: both the uninterrupted game and the reloaded one stay penalised through
        // the last active turn, then recover together on the next.
        game.EndTurn();   // → last active turn (start + 2)
        loaded.EndTurn();
        Assert.Equal(full - full / 2, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id));
        Assert.Equal(full - full / 2, loaded.TileYield(loaded.HumanPlayer, Free, tile, Sugar, loadedColony.Id));
        game.EndTurn();   // → past the window: the per-turn strip removes it in both
        loaded.EndTurn();
        Assert.Empty(game.TemporaryModifiers);
        Assert.Empty(loaded.TemporaryModifiers);
        Assert.Equal(full, game.TileYield(game.HumanPlayer, Free, tile, Sugar, colony.Id));
        Assert.Equal(full, loaded.TileYield(loaded.HumanPlayer, Free, tile, Sugar, loadedColony.Id));
    }

    [Fact]
    public void ApplyDisaster_PenaltyRoundTripsThroughSave_EndToEnd()
    {
        // End-to-end via the real ApplyDisaster path (the forced production-penalty spec): the registered
        // colony-scoped timed modifiers round-trip through the save byte-for-byte (record equality).
        Ruleset onlyPenalty = LoadClassicWithOnlyProductionPenaltyDisaster();
        Game game = Game.New(onlyPenalty, Seed);
        Colony colony = FoundColony(game);
        game.HumanPlayer.Gold = 1000;
        for (int i = 0; i < 10 && !game.TemporaryModifiers.Any(); i++)
        {
            game.EndTurn();
        }
        Assert.NotEmpty(game.TemporaryModifiers); // a strike registered at least one penalty

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(onlyPenalty);

        Assert.Equal(game.TemporaryModifiers, loaded.TemporaryModifiers);
        Assert.All(loaded.TemporaryModifiers, m => Assert.Equal(colony.Id, m.ColonyId)); // the colony scope survived
    }

    [Fact]
    public void ADefaultGame_OmitsTheTemporaryModifiersToken_AndOldSavesLoadWithNone()
    {
        // The classic default game (disaster chance 0) registers no timed modifier, so a default save writes no
        // TemporaryModifiers token (byte-identical to v68 content); a v68-style save (no token) loads with none.
        Game fresh = Game.New(Classic, Seed);
        string json = SaveGame.From(fresh).ToJson();
        Assert.DoesNotContain("TemporaryModifiers", json);

        SaveGame asV68 = SaveGame.FromJson(json) with { Version = 68, TemporaryModifiers = null };
        Game loaded = SaveGame.FromJson(asV68.ToJson()).Restore(Classic);
        Assert.Empty(loaded.TemporaryModifiers);
    }

    // ===== Effect parity (86d3fq0ye): lossOfBuilding / lossOfUnit / damagedUnit + terrain-mapped pool =====

    /// <summary>
    /// The classic ruleset with <c>naturalDisasters</c> forced to 100% and the shared "several" effect list forced to
    /// fire ONLY the named effect id (that effect 100%, every other 0%) — so a strike deterministically applies just
    /// that one effect. Optionally forces every tile-type's disaster list to a single disaster id so the terrain-mapped
    /// pool is known.
    /// </summary>
    private static Ruleset LoadClassicForcingEffect(string effectId, string? soleTerrainDisasterId = null)
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement opt = doc.Descendants("percentageOption").Single(o => (string?)o.Attribute("id") == "model.option.naturalDisasters");
        opt.SetAttributeValue("defaultValue", "100");
        opt.SetAttributeValue("value", "100");
        foreach (XElement effect in doc.Descendants("effect"))
        {
            bool wanted = (string?)effect.Attribute("id") == effectId;
            effect.SetAttributeValue("probability", wanted ? "100" : "0");
        }
        if (soleTerrainDisasterId is not null)
        {
            // Point every tile-type at a single disaster so any colony (whatever its terrain) draws that one.
            foreach (XElement tile in doc.Descendants("tile-type"))
            {
                tile.Elements("disaster").Remove();
                tile.Add(new XElement("disaster", new XAttribute("id", soleTerrainDisasterId), new XAttribute("probability", "100")));
            }
        }
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private const string Docks = "model.building.docks";
    private const string Warehouse = "model.building.warehouse";
    private const string Depot = "model.building.depot";

    [Fact]
    public void LossOfBuilding_RazesOrDowngradesABurnableBuilding()
    {
        // A spec whose disaster fires ONLY lossOfBuilding, terrain forced to always pick model.disaster.flood.
        Ruleset ruleset = LoadClassicForcingEffect("model.disaster.effect.lossOfBuilding", "model.disaster.flood");
        Game game = Game.New(ruleset, Seed);
        Colony colony = FoundColony(game);
        // Give the colony one burnable building that DOWNGRADES (warehouse → depot), the sole burnable target.
        colony.ReplaceBuilding(Depot, Warehouse);
        Assert.Contains(Warehouse, colony.Buildings);

        bool struck = false;
        for (int i = 0; i < 20 && !struck; i++)
        {
            game.EndTurn();
            struck = game.DisasterNotices.Any(n => n.BuildingLostId is not null);
        }
        Assert.True(struck, "the forced lossOfBuilding disaster should have razed/downgraded a building");
        Assert.DoesNotContain(Warehouse, colony.Buildings); // burned…
        Assert.Contains(Depot, colony.Buildings);           // …downgraded back to the depot
        Assert.Equal(Warehouse, game.DisasterNotices.First(n => n.BuildingLostId is not null).BuildingLostId);
    }

    [Fact]
    public void LossOfUnit_DropsColonyPopulationByOne_WhenAboveOne()
    {
        Ruleset ruleset = LoadClassicForcingEffect("model.disaster.effect.lossOfUnit", "model.disaster.flood");
        Game game = Game.New(ruleset, Seed);
        Colony colony = FoundColony(game);
        colony.Population = 3;

        bool struck = false;
        for (int i = 0; i < 20 && !struck; i++)
        {
            game.EndTurn();
            struck = game.DisasterNotices.Any(n => n.ColonistLost);
        }
        Assert.True(struck, "the forced lossOfUnit disaster should have removed a colonist");
        Assert.True(colony.Population < 3, "population must have dropped");
        Assert.Contains(game.Colonies, c => c.Id == colony.Id); // colony survives (it had > 1 colonist)
    }

    [Fact]
    public void LossOfUnit_DestroysASizeOneColony()
    {
        Ruleset ruleset = LoadClassicForcingEffect("model.disaster.effect.lossOfUnit", "model.disaster.flood");
        Game game = Game.New(ruleset, Seed);
        Colony colony = FoundColony(game);
        colony.Population = 1; // its last colonist
        int id = colony.Id;

        bool destroyed = false;
        for (int i = 0; i < 20 && !destroyed; i++)
        {
            game.EndTurn();
            destroyed = game.Colonies.All(c => c.Id != id);
        }
        Assert.True(destroyed, "losing the last colonist must destroy the colony");
        Assert.Contains(game.DisasterNotices, n => n.ColonistLost); // the loss is noted for the human
        Assert.Contains(game.ColonyStarvedNotices, n => n.ColonyName == colony.Name); // …and surfaced as a colony-lost row
    }

    [Fact]
    public void DamagedUnit_DamagesOrSinksADockedShip()
    {
        Ruleset ruleset = LoadClassicForcingEffect("model.disaster.effect.damagedUnit", "model.disaster.flood");
        Game game = Game.New(ruleset, Seed);
        Colony colony = FoundColony(game);
        // Park a human ship on a water tile adjacent to the colony (its port).
        Position water = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        Unit ship = game.SpawnUnit(ruleset.Unit("model.unit.caravel"), water);
        int shipId = ship.Id;

        bool struck = false;
        for (int i = 0; i < 20 && !struck; i++)
        {
            game.EndTurn();
            struck = game.DisasterNotices.Any(n => n.ShipDamaged);
        }
        Assert.True(struck, "the forced damagedUnit disaster should have damaged the docked ship");
        Unit after = game.Units.First(u => u.Id == shipId);
        Assert.True(after.IsUnderRepair); // a colonial owner has Europe → the ship is damaged and limps off to repair
    }

    [Fact]
    public void TerrainMappedPool_OnlyDrawsDisastersListedForTheColonysTerrain()
    {
        // Force the colony onto tundra (spec: blizzard @ 100 — its ONLY disaster) and force the disaster to fire ONLY
        // lossOfMoney (100%) so a strike always registers a notice. Over many strikes the notice's disaster id must
        // ALWAYS be the blizzard: the pool is the centre-tile terrain's disaster list, never uniform.
        Ruleset ruleset = LoadClassicForcingEffect("model.disaster.effect.lossOfMoney");
        Game game = Game.New(ruleset, Seed);
        Colony colony = FoundColony(game);
        game.Map.SetTerrain(colony.Position, ruleset.Terrain("model.tile.tundra"));

        for (int i = 0; i < 25; i++)
        {
            game.HumanPlayer.Gold = 1000;  // keep a purse so the forced lossOfMoney always has something to take
            colony.AddGoods("model.goods.food", 20); // keep the tundra colony fed so it isn't starved out
            game.EndTurn();
        }
        Assert.NotEmpty(game.DisasterNotices); // tundra lists a disaster, so strikes land
        Assert.All(game.DisasterNotices, n => Assert.Equal("model.disaster.blizzard", n.DisasterId));
    }

    [Fact]
    public void TerrainWithNoDisasters_TakesNoDisaster()
    {
        // Grassland lists NO <disaster> child in the classic spec. A colony whose only terrain lists none is never struck
        // (the colony contributes nothing to the walk), even at 100% disaster chance.
        Ruleset ruleset = LoadClassicForcingEffect("model.disaster.effect.lossOfMoney");
        Game game = Game.New(ruleset, Seed);
        Colony colony = FoundColony(game);
        game.Map.SetTerrain(colony.Position, ruleset.Terrain("model.tile.grassland")); // no disasters

        for (int i = 0; i < 25; i++)
        {
            game.HumanPlayer.Gold = 1000;
            colony.AddGoods("model.goods.food", 20);
            game.EndTurn();
        }
        Assert.Empty(game.DisasterNotices); // grassland lists no disaster → this colony is never struck
    }

    [Fact]
    public void ByteStabilityTwin_NaturalDisastersOff_TwoSameSeedGamesStayIdentical()
    {
        // The classic default (naturalDisasters = 0) draws no RNG in the disaster path: two same-seed games — one an
        // untouched twin — stay byte-identical over a long horizon, and neither ever records a disaster (ADR-009).
        Game a = Game.New(Classic, Seed);
        Game b = Game.New(Classic, Seed);
        FoundColony(a);
        FoundColony(b);
        for (int turn = 0; turn < 40; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson()); // byte-identical
        Assert.Equal(a.RandomState, b.RandomState);                        // stream 0 in lockstep
        Assert.Empty(a.DisasterNotices);
        Assert.Empty(b.DisasterNotices);
    }
}
