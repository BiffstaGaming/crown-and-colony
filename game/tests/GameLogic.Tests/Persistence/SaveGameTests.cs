using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Persistence;

public class SaveGameTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void JsonRoundTrip_PreservesEverything()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = game.Units[0];
        game.MoveUnit(unit, AdjacentLand(game, unit.Position));
        game.EndTurn();

        string json = SaveGame.From(game).ToJson();
        Game loaded = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(game.Turn, loaded.Turn);
        Assert.Equal(game.Map.Width, loaded.Map.Width);
        Assert.Equal(game.Map.Height, loaded.Map.Height);
        Assert.Equal(
            game.Map.AllPositions().Select(p => game.Map.TerrainAt(p).Id),
            loaded.Map.AllPositions().Select(p => loaded.Map.TerrainAt(p).Id));
        Assert.Equal(game.Units.Count, loaded.Units.Count);
        Assert.Equal(game.Units[0].Id, loaded.Units[0].Id);
        Assert.Equal(game.Units[0].Type.Id, loaded.Units[0].Type.Id);
        Assert.Equal(game.Units[0].Position, loaded.Units[0].Position);
        Assert.Equal(game.Units[0].MovementLeft, loaded.Units[0].MovementLeft);
    }

    [Fact]
    public void NextUnitId_IsOmitted_ForAFreshGame_AndReDerivedOnLoad()
    {
        // The common case: the counter equals max(unit id)+1, so the field is omitted (byte-identical to pre-v54) and
        // re-derived from the units on load — exactly the legacy behaviour.
        var game = Game.New(Classic, seed: 99);
        SaveGame save = SaveGame.From(game);

        Assert.Null(save.NextUnitId);                            // omitted in the common case
        Assert.DoesNotContain("NextUnitId", save.ToJson());      // …so no token in the JSON

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        Assert.Equal(game.NextUnitId, loaded.NextUnitId);        // re-derived to the same value
    }

    [Fact]
    public void NextUnitId_SurvivesRoundTrip_WhenAheadOfMaxExistingId()
    {
        // The bug this fixes: the id counter is monotonic and not rewound when a unit is destroyed, so it can run AHEAD
        // of max(existing id)+1. Persisting it (v54) keeps a save/load round-trip byte-identical instead of resetting
        // the counter to max+1 and risking a divergent (lower) id for the next unit created.
        var game = Game.New(Classic, seed: 99);
        int ahead = game.Units.Max(u => u.Id) + 50; // simulate having created + destroyed many higher-id units
        SaveGame save = SaveGame.From(game) with { NextUnitId = ahead };

        Assert.Contains("\"NextUnitId\": " + ahead, save.ToJson()); // written when ahead of max+1

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        Assert.Equal(ahead, loaded.NextUnitId);                     // counter preserved, not reset to max+1
    }

    [Fact]
    public void NextUnitId_NeverGoesBelowASurvivingUnit_OnLoad()
    {
        // A defensive clamp: a (corrupt or hand-edited) saved counter below the highest surviving unit id must not
        // pull the allocator down into collision range — RestoreNextUnitId takes the max with the re-derived value.
        var game = Game.New(Classic, seed: 99);
        int maxId = game.Units.Max(u => u.Id);
        SaveGame save = SaveGame.From(game) with { NextUnitId = 1 }; // absurdly low

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        Assert.True(loaded.NextUnitId > maxId, "the counter was pulled below a surviving unit id");
    }

    [Fact]
    public void PreV54Save_LoadsWithoutTheCounter_ReDerivingItFromUnits()
    {
        // Back-compat: a save with no NextUnitId field (pre-v54, or any omitted-default save) loads and re-derives the
        // counter from the surviving units exactly as before — no exception, counter ≥ max+1.
        var game = Game.New(Classic, seed: 99);
        SaveGame v53 = SaveGame.From(game) with { Version = 53, NextUnitId = null };

        Game loaded = SaveGame.FromJson(v53.ToJson()).Restore(Classic);
        Assert.Equal(game.Units.Max(u => u.Id) + 1, loaded.NextUnitId);
    }

    [Fact]
    public void RoundTrip_PreservesExploredTilesExactly()
    {
        var game = Game.New(Classic, seed: 99);
        game.MoveUnit(game.Units[0], AdjacentLand(game, game.Units[0].Position));

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Explored.OrderBy(p => (p.Y, p.X)),
            loaded.Explored.OrderBy(p => (p.Y, p.X)));
    }

    [Fact]
    public void LoadedGame_ContinuesIdenticalRandomSequence()
    {
        var original = Game.New(Classic, seed: 7);
        string json = SaveGame.From(original).ToJson();
        Game loaded = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(original.RandomState, loaded.RandomState);
    }

    [Fact]
    public void V1Save_WithoutFogOrUnitTypes_LoadsWithDefaults()
    {
        // Format v1 (Phase 1) had no Explored list and no unit TypeId. Loading
        // must still work: units default to free colonists, fog reveals around them.
        var game = Game.New(Classic, seed: 5);
        // A v1 save predates natives, roles and the multi-unit roster — only the player's lone starting colonist
        // existed; model that (one typeless land unit) so the v1 default-load path is exercised faithfully.
        Unit colonist = game.PlayerUnits.First(u => !u.Type.IsNaval);
        SaveGame v1 = SaveGame.From(game) with
        {
            Version = 1,
            Explored = null,
            Units = [new SavedUnit(colonist.Id, null, colonist.Position.X, colonist.Position.Y, colonist.MovementLeft)],
        };

        Game loaded = SaveGame.FromJson(v1.ToJson()).Restore(Classic);

        Assert.Equal(Game.StartingUnitTypeId, loaded.Units[0].Type.Id);
        Assert.True(loaded.IsExplored(loaded.Units[0].Position));
        Assert.InRange(loaded.Explored.Count, 4, 9); // one colonist, sight 1 → a 3×3 block
    }

    [Fact]
    public void RoundTrip_PreservesEveryTilesRegionAndScore()
    {
        // Map regions (86d3c9w12) slice 5: a v35 save restores the region of every tile and the region table.
        var game = Game.New(Classic, seed: 99);
        game.EndTurn();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Map.AllPositions().Select(game.Map.RegionIdAt),
            loaded.Map.AllPositions().Select(loaded.Map.RegionIdAt));
        Assert.Equal(game.Map.Regions, loaded.Map.Regions); // table (incl. score/key/parent) restored verbatim
        // Concrete spot-check that a polar rule survives the reload (full per-tile equality above already
        // proves preservation; the generator produces antarctic — not arctic — land, so we check that band).
        var antarcticLand = loaded.Map.AllPositions()
            .Where(p => !loaded.Map.TerrainAt(p).IsWater && p.Y >= loaded.Map.Height - 3).ToList();
        Assert.NotEmpty(antarcticLand);
        Assert.All(antarcticLand, p => Assert.Equal("model.region.antarctic", loaded.Map.RegionOf(p)!.Key));
    }

    [Fact]
    public void GeneratedGame_PersistsRegions_InTheSave()
    {
        // A real generated map always carries a region layer, so the save emits it (omit-when-default only
        // suppresses regionless fixtures).
        SaveGame save = SaveGame.From(Game.New(Classic, seed: 42));

        Assert.NotNull(save.RegionIds);
        Assert.NotNull(save.Regions);
        Assert.Equal(save.MapWidth * save.MapHeight, save.RegionIds!.Count);
        Assert.Contains(save.Regions!, r => r.Key == "model.region.pacific" && r.ScoreValue == 100);
    }

    [Fact]
    public void PreV35Save_WithoutRegions_ReDerivesThemDeterministically()
    {
        // A pre-v35 save has no region layer; loading must re-derive exactly the layer a fresh generation
        // produces for the same terrain (mirrors the native-land claim re-derivation).
        var game = Game.New(Classic, seed: 7);
        SaveGame v34 = SaveGame.From(game) with { Version = 34, RegionIds = null, Regions = null };

        Game loaded = SaveGame.FromJson(v34.ToJson()).Restore(Classic);

        Assert.NotEmpty(loaded.Map.Regions);
        Assert.Equal(
            game.Map.AllPositions().Select(game.Map.RegionIdAt),  // the layer the original game generated
            loaded.Map.AllPositions().Select(loaded.Map.RegionIdAt)); // == re-derived on load
        // Compare the geographic region data only: a pre-v35 save carries no discovery state, so the re-derived layer
        // is pristine (undiscovered), whereas the original game already discovered regions on its starting fog reveal
        // (P6). Strip the discovery fields before comparing the geography (ids/type/score/key/parent re-derive exactly).
        static Region Geography(Region r) => r with { DiscoveredBy = null, Name = null, DiscoveredInTurn = null };
        Assert.Equal(game.Map.Regions.Select(Geography), loaded.Map.Regions);
    }

    [Fact]
    public void RegionFields_WhenNull_AreOmittedFromTheJson()
    {
        // Omit-when-default mechanism (DefaultIgnoreCondition.WhenWritingNull): when a save has no region layer
        // the JSON carries no region tokens. A real generated game ALWAYS has a region layer (see
        // GeneratedGame_PersistsRegions), so this omit path only ever guards a (hypothetical) regionless map.
        string bareJson = (SaveGame.From(Game.New(Classic, seed: 5)) with { RegionIds = null, Regions = null }).ToJson();

        Assert.DoesNotContain("\"RegionIds\"", bareJson);
        Assert.DoesNotContain("\"Regions\"", bareJson);
    }

    [Fact]
    public void FreshGame_HumanDefaultUnits_OmitRoleAndOwnerTokens()
    {
        var game = Game.New(Classic, seed: 5);
        SaveGame save = SaveGame.From(game);
        // A default-role, human-owned unit serializes with no role/owner tokens (byte-identical to a v17 unit). The
        // starting caravel carries the unarmed default role (the pioneer/soldier are role-equipped — they correctly
        // DO emit role tokens), so pick a default-role human unit.
        Unit defaultRoleUnit = game.PlayerUnits.First(u => u.HasDefaultRole);
        SavedUnit human = save.Units.First(u => u.Id == defaultRoleUnit.Id);
        Assert.Null(human.Role);
        Assert.Null(human.RoleCount);
        Assert.Null(human.OwnerId); // the human (id 0) is omitted
        Assert.Null(human.Owner);   // no native nation
        // Native braves still carry their owning nation (the genuinely new v18 unit field).
        Assert.Contains(save.Units, u => u.Owner is not null);
    }

    [Fact]
    public void PreV18Save_LoadsUnitsAsPlayerOwnedDefaultRole()
    {
        // v17 and earlier predate unit owner + role; loading must default every unit to
        // player-owned and the unarmed default role (and such a save carries no braves).
        var game = Game.New(Classic, seed: 5);
        SaveGame v17 = SaveGame.From(game) with
        {
            Version = 17,
            Units = game.PlayerUnits
                .Select(u => new SavedUnit(u.Id, u.Type.Id, u.Position.X, u.Position.Y, u.MovementLeft))
                .ToList(),
        };

        Game loaded = SaveGame.FromJson(v17.ToJson()).Restore(Classic);

        Assert.All(loaded.Units, u =>
        {
            Assert.Null(u.OwnerNationId);
            Assert.Equal(RoleType.DefaultRoleId, u.RoleId);
            Assert.Equal(0, u.RoleCount);
        });
    }

    [Fact]
    public void RoundTrip_PreservesColonies()
    {
        var game = Game.New(Classic, seed: 11);
        var founded = game.FoundColony(game.Units[0]);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        var colony = Assert.Single(loaded.Colonies);
        Assert.Equal(founded.Id, colony.Id);
        Assert.Equal(founded.Name, colony.Name);
        Assert.Equal(founded.Position, colony.Position);
        Assert.Equal(founded.Population, colony.Population);
    }

    [Fact]
    public void PreV3Save_WithoutColonies_LoadsEmpty()
    {
        var game = Game.New(Classic, seed: 11);
        SaveGame v2 = SaveGame.From(game) with { Version = 2, Colonies = null };

        Game loaded = SaveGame.FromJson(v2.ToJson()).Restore(Classic);

        Assert.Empty(loaded.Colonies);
    }

    [Fact]
    public void RoundTrip_PreservesBonusResources()
    {
        var game = Game.New(Classic, seed: 11);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Map.Resources.OrderBy(r => (r.Key.Y, r.Key.X)),
            loaded.Map.Resources.OrderBy(r => (r.Key.Y, r.Key.X)));
    }

    [Fact]
    public void Load_WithUnknownTerrainId_Throws()
    {
        var game = Game.New(Classic, seed: 1);
        SaveGame save = SaveGame.From(game);
        SaveGame corrupted = save with
        {
            Terrain = save.Terrain.Select((id, i) => i == 0 ? "model.tile.atlantis" : id).ToList(),
        };

        Assert.Throws<KeyNotFoundException>(() => corrupted.Restore(Classic));
    }

    [Fact]
    public void V19Save_LoadsAsSingleHumanPlayer()
    {
        // A v19-and-earlier save has no Players[]; its flat top-level player fields must fold
        // into exactly one human player on load (ADR-019, save v20).
        var game = Game.New(Classic, seed: 42, startingGold: 500, startingTax: 10);
        game.EndTurn(); // accrue a little immigration so the folded value is non-trivial

        // A v19 save predates Players[]; its flat top-level fields carry the human's state. From() no longer
        // writes those fields (dropped at FP-7), so this fixture *constructs* the v19 shape explicitly to keep
        // exercising the fold path. Players = null forces the fold even though the source game has one.
        SaveGame v19 = SaveGame.From(game) with
        {
            Version = 19,
            Players = null,
            Gold = 500,
            Tax = 10,
            Liberty = game.Liberty,
            Congress = game.Congress.Count > 0 ? game.Congress.ToList() : null,
            CurrentFather = game.CurrentFather,
            OfferedFathers = game.OfferedFathers.Count > 0 ? game.OfferedFathers.ToList() : null,
            Immigration = game.Immigration,
            ImmigrationRequired = game.ImmigrationRequired,
            BaseRecruitPrice = game.BaseRecruitPrice,
            RecruitLowerCap = game.RecruitLowerCap,
            RecruitDock = game.RecruitDock.Count > 0 ? game.RecruitDock.ToList() : null,
            MarketState = new Dictionary<string, int> { ["model.goods.sugar"] = 99 },
            Explored = game.Explored.Select(p => p.Y * game.Map.Width + p.X).OrderBy(i => i).ToList(),
        };

        Game loaded = SaveGame.FromJson(v19.ToJson()).Restore(Classic);

        Player human = Assert.Single(loaded.Players);
        Assert.Same(human, loaded.HumanPlayer);
        Assert.True(human.IsHuman);
        Assert.Equal(0, human.PlayerId);
        Assert.Equal(PlayerType.Colonial, human.PlayerType);
        Assert.Equal(500, human.Gold);
        Assert.Equal(10, human.TaxRate);
        Assert.Equal(game.Liberty, human.Liberty);
        Assert.Equal(game.Congress, human.Congress);
        Assert.Equal(game.CurrentFather, human.CurrentFather);
        Assert.Equal(game.OfferedFathers, human.OfferedFathers);
        Assert.Equal(game.Immigration, human.Immigration);
        Assert.Equal(game.ImmigrationRequired, human.ImmigrationRequired);
        Assert.Equal(game.BaseRecruitPrice, human.BaseRecruitPrice);
        Assert.Equal(game.RecruitLowerCap, human.RecruitLowerCap);
        Assert.Equal(game.RecruitDock, human.RecruitDock);
        Assert.Equal(99, human.Market.AmountInMarket("model.goods.sugar"));
        Assert.Equal(
            game.Explored.OrderBy(p => (p.Y, p.X)),
            loaded.Explored.OrderBy(p => (p.Y, p.X)));
    }

    [Fact]
    public void NewSave_OmitsLegacyFlatPlayerFields()
    {
        // FP-7: From() no longer populates the legacy flat top-level player fields — player state lives only
        // in Players[]. (The flat names are shared with SavedPlayer, so we prove omission via the record/reload,
        // not a raw-JSON substring search.)
        var save = SaveGame.From(Game.New(Classic, seed: 5, startingGold: 500, startingTax: 10));

        Assert.Null(save.Gold);
        Assert.Null(save.Tax);
        Assert.Null(save.Liberty);
        Assert.Null(save.Immigration);
        Assert.Null(save.MarketState);
        Assert.Null(save.Congress);
        Assert.Null(save.CurrentFather);
        Assert.Null(save.OfferedFathers);
        Assert.Null(save.ImmigrationRequired);
        Assert.Null(save.BaseRecruitPrice);
        Assert.Null(save.RecruitLowerCap);
        Assert.Null(save.RecruitDock);
        Assert.Null(save.Explored);

        // They round-trip as absent (a reloaded record still reads them null at the top level)…
        SaveGame reloaded = SaveGame.FromJson(save.ToJson());
        Assert.Null(reloaded.Gold);
        Assert.Null(reloaded.Explored);
        Assert.Null(reloaded.RecruitDock);

        // …while the human's state is carried under Players[].
        SavedPlayer human = save.Players!.First(p => p.IsHuman);
        Assert.Equal(500, human.Gold);
        Assert.Equal(10, human.Tax);
    }

    [Fact]
    public void LegacyV20Save_WithFlatFieldsAndPlayers_LoadsFromPlayersIgnoringFlatFields()
    {
        // A pre-FP-7 v20 save carries BOTH Players[] and the flat fields. The v20 load path reads Players[]
        // and ignores the flat fields — proven by feeding deliberately-wrong flat values.
        var game = Game.New(Classic, seed: 42, startingGold: 250, startingTax: 7);
        SaveGame withStaleFlats = SaveGame.From(game) with
        {
            Gold = 999999,
            Tax = 99,
            Liberty = 12345,
            Immigration = 4242,
            Explored = new List<int>(), // bogus empty fog
        };

        Game loaded = SaveGame.FromJson(withStaleFlats.ToJson()).Restore(Classic);
        Player human = loaded.HumanPlayer;

        Assert.Equal(250, human.Gold);    // from Players[], not the bogus 999999
        Assert.Equal(7, human.TaxRate);
        Assert.Equal(game.Liberty, human.Liberty);
        Assert.Equal(game.Immigration, human.Immigration);
        Assert.NotEmpty(loaded.Explored); // real fog from Players[0], not the bogus empty list
    }

    [Theory]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(19)]
    public void OldSaveVersion_WithFlatFields_FoldsToOneHuman(int version)
    {
        // Every pre-Players[] version still folds its flat fields into a single human (Players = null forces
        // the fold even though the source game has rivals).
        var game = Game.New(Classic, seed: 7, startingGold: 400, startingTax: 8);
        SaveGame old = SaveGame.From(game) with
        {
            Version = version,
            Players = null,
            Gold = 400,
            Tax = 8,
            ImmigrationRequired = game.ImmigrationRequired,
            BaseRecruitPrice = game.BaseRecruitPrice,
            RecruitLowerCap = game.RecruitLowerCap,
            RecruitDock = game.RecruitDock.ToList(),
            Explored = game.Explored.Select(p => p.Y * game.Map.Width + p.X).ToList(),
        };

        Game loaded = SaveGame.FromJson(old.ToJson()).Restore(Classic);

        Player human = Assert.Single(loaded.Players);
        Assert.True(human.IsHuman);
        Assert.Equal(400, human.Gold);
        Assert.Equal(8, human.TaxRate);
    }

    [Fact]
    public void PreV9Save_WithNoTreasuryTokens_FoldsToZeroDefaults()
    {
        // A genuinely old (pre-v9) save carries no Gold/Tax/Liberty/Immigration tokens at all; the fold must
        // default them to 0 (the former default(int)), proving the now-nullable fields' `?? 0` coalesce.
        var game = Game.New(Classic, seed: 7);
        SaveGame preV9 = SaveGame.From(game) with
        {
            Version = 8,
            Players = null,
            // Gold/Tax/Liberty/Immigration deliberately left null (token absent) — must fold to 0.
        };

        Game loaded = SaveGame.FromJson(preV9.ToJson()).Restore(Classic);
        Player human = Assert.Single(loaded.Players);

        Assert.Equal(0, human.Gold);
        Assert.Equal(0, human.TaxRate);
        Assert.Equal(0, human.Liberty);
        Assert.Equal(0, human.Immigration);
    }

    [Fact]
    public void HumanState_RoundTripsThroughPlayersOnly()
    {
        var game = Game.New(Classic, seed: 99, startingGold: 300, startingTax: 5);
        game.EndTurn(); // accrue some immigration; advance state

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Gold, loaded.Gold);
        Assert.Equal(game.TaxRate, loaded.TaxRate);
        Assert.Equal(game.Liberty, loaded.Liberty);
        Assert.Equal(game.Immigration, loaded.Immigration);
        Assert.Equal(game.RecruitDock, loaded.RecruitDock);
        Assert.Equal(
            game.Explored.OrderBy(p => (p.Y, p.X)),
            loaded.Explored.OrderBy(p => (p.Y, p.X)));
        Assert.Null(SaveGame.From(game).Gold); // carried under Players[], not the legacy flat field
    }

    [Fact]
    public void RoundTrip_PreservesPeaceTurnStamps()
    {
        // v53: the per-pair peace-turn (FreeCol peaceHolds' peaceTurn) survives a save/load round-trip on both sides.
        var game = Game.New(Classic, seed: 99);
        Player a = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        int b = game.HumanPlayer.PlayerId;
        game.SetStance(a.PlayerId, b, Stance.Peace); // stamps the peace turn (turn 1) both ways

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Player loadedA = loaded.Players.First(p => p.PlayerId == a.PlayerId);
        Assert.Equal(a.PeaceTurns[b], loadedA.PeaceTurns[b]);
        Assert.Equal(
            game.HumanPlayer.PeaceTurns[a.PlayerId],
            loaded.HumanPlayer.PeaceTurns[a.PlayerId]);
    }

    [Fact]
    public void PeaceTurns_WhenNoneRecorded_AreOmittedFromTheSave()
    {
        // Omit-when-empty: a fresh game with no established peace writes no PeaceTurns map on any player, so it stays
        // byte-identical to a v52 save (the gate is inert without a recorded peace anyway).
        var game = Game.New(Classic, seed: 99);
        SaveGame save = SaveGame.From(game);
        Assert.All(save.Players!, p => Assert.Null(p.PeaceTurns));
    }

    [Fact]
    public void StoredV41Save_LoadsCleanlyOnCurrentVersion_AndIsPlayable()
    {
        // Save-compat regression (86d3dze09): a REAL, hand-authored v41 save FILE — committed as a test
        // resource, NOT produced by today's From() — must still load on the current format (v53+). Every field
        // added since v41 (region discovery, REF force/entry, Spanish succession, difficulty, monarch demand,
        // pioneer work-state, attrition, unit nationality/ethnicity/name, peace turns, …) is absent from the file;
        // Restore must default each one safely (the ??/default fallbacks). This guards the additive-migration
        // promise as CurrentVersion keeps climbing — a round-trip of a current game can't, since it writes today's
        // shape. The fixture pre-dates Players[] (v20), so it also exercises the legacy flat-field fold to one human.
        string json = File.ReadAllText(FixturePath("save-v41.json"));
        SaveGame save = SaveGame.FromJson(json);
        Assert.Equal(41, save.Version);                 // the file really is an old-version save
        Assert.True(save.Version < SaveGame.CurrentVersion); // …older than what we run today

        Game loaded = save.Restore(Classic);

        // ── The world restored ──────────────────────────────────────────────────────────────────────
        Assert.Equal(12, loaded.Turn);
        Assert.Equal(6, loaded.Map.Width);
        Assert.Equal(4, loaded.Map.Height);

        // ── The player folded from the flat fields (no Players[] in a v41 save) ─────────────────────
        Player human = Assert.Single(loaded.Players);
        Assert.True(human.IsHuman);
        Assert.Equal(350, human.Gold);
        Assert.Equal(8, human.TaxRate);
        Assert.Equal(24, human.Liberty);
        Assert.Equal(
            new[] { "model.unit.freeColonist", "model.unit.expertFisherman", "model.unit.veteranSoldier" },
            human.RecruitDock);
        Assert.Equal(4, human.Market.AmountInMarket("model.goods.sugar")); // moved market entry survives

        // ── Units restored, with post-v41 fields defaulted ─────────────────────────────────────────
        Assert.Equal(2, loaded.Units.Count);
        Unit colonist = loaded.Units.Single(u => u.Id == 1);
        Assert.Equal("model.unit.freeColonist", colonist.Type.Id);
        Assert.Equal(new Position(3, 1), colonist.Position);
        Assert.Equal(0, colonist.Attrition);                       // v50: absent → 0
        Assert.Null(colonist.Name);                                // v52: un-christened
        Assert.Equal(UnitOrders.Active, colonist.Orders);          // v23: absent → Active
        Assert.Null(colonist.Destination);                         // v36: no goto

        // ── Colony restored, with its buildings + build queue ──────────────────────────────────────
        Colony colony = Assert.Single(loaded.Colonies);
        Assert.Equal("Jamestown", colony.Name);
        Assert.Equal(new Position(2, 1), colony.Position);
        Assert.Equal(3, colony.Population);
        Assert.Equal("model.building.docks", colony.CurrentBuild); // queue front restored

        // ── Post-v41 game-wide fields default safely ────────────────────────────────────────────────
        Assert.False(loaded.SpanishSuccessionDone);                 // v42: absent → not done
        Assert.Null(loaded.PendingMonarchDemand);                   // v46: absent → none
        Assert.Equal(DifficultyLevels.DefaultId, loaded.DifficultyLevelId); // v46: absent → medium
        Assert.NotEmpty(loaded.Map.Regions);                        // v35: absent → re-derived deterministically
        Assert.All(loaded.Map.Regions, r => Assert.Null(r.DiscoveredBy)); // v51: absent → every region undiscovered
        Assert.False(human.MonarchDispleasure);                     // v38: absent → content
        Assert.False(human.SupportSeaGranted);                      // v39: absent → not granted
        Assert.Null(human.DeclaredIndependenceTurn);                // v41: absent → never declared
        Assert.Empty(human.PeaceTurns);                             // v53: absent → no recorded peace

        // ── Playable: an EndTurn runs without throwing and advances the turn ────────────────────────
        int before = loaded.Turn;
        loaded.EndTurn();
        Assert.Equal(before + 1, loaded.Turn);
    }

    // ── Message log (save v59): the in-session notice log round-trips ─────────────────────────────────────

    [Fact]
    public void MessageLog_IsOmittedFromAFromGame_SoAFreshGameStaysByteIdenticalToV58()
    {
        // The message log is presentation-owned (ADR-006): SaveGame.From never writes it (it has no formatted strings),
        // so a From() snapshot leaves MessageLog null and emits no token — a fresh game stays byte-identical to v58.
        var game = Game.New(Classic, seed: 99);
        SaveGame save = SaveGame.From(game);

        Assert.Null(save.MessageLog);
        Assert.DoesNotContain("\"MessageLog\"", save.ToJson());
    }

    [Fact]
    public void MessageLog_RoundTripsThroughJson_PreservingTurnCategoryAndText()
    {
        // The controller attaches a MessageLog via `with` before serialising; that field must survive a JSON round-trip
        // with each row's turn, category ordinal and text intact (the controller re-groups them back into per-turn
        // entries on load).
        var game = Game.New(Classic, seed: 99);
        SaveGame save = SaveGame.From(game) with
        {
            MessageLog = new List<SavedLogMessage>
            {
                new(3, 0, "A privateer sank your Caravel at (4,5)!"), // category 0 = Combat
                new(7, 4, "The Crown lowered your tax rate to 18%."), // category 4 = Monarch
            },
        };

        Assert.Contains("\"MessageLog\"", save.ToJson()); // present once the controller supplied it

        SaveGame round = SaveGame.FromJson(save.ToJson());
        Assert.Equal(2, round.MessageLog!.Count);
        Assert.Equal(new SavedLogMessage(3, 0, "A privateer sank your Caravel at (4,5)!"), round.MessageLog[0]);
        Assert.Equal(new SavedLogMessage(7, 4, "The Crown lowered your tax rate to 18%."), round.MessageLog[1]);
    }

    /// <summary>Absolute path to a committed save fixture (copied next to the test assembly by the csproj).</summary>
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Persistence", "fixtures", name);

    private static Position AdjacentLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
}
