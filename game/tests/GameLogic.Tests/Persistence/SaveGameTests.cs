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
        // A v1 save predates natives — only the player's lone starting colonist existed.
        SaveGame v1 = SaveGame.From(game) with
        {
            Version = 1,
            Explored = null,
            Units = game.PlayerUnits.Select(u =>
                new SavedUnit(u.Id, null, u.Position.X, u.Position.Y, u.MovementLeft)).ToList(),
        };

        Game loaded = SaveGame.FromJson(v1.ToJson()).Restore(Classic);

        Assert.Equal(Game.StartingUnitTypeId, loaded.Units[0].Type.Id);
        Assert.True(loaded.IsExplored(loaded.Units[0].Position));
        Assert.InRange(loaded.Explored.Count, 4, 9);
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
        Assert.Equal(game.Map.Regions, loaded.Map.Regions);
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
        // A default-role, human-owned unit serializes with no role/owner tokens (byte-identical to a v17 unit).
        SavedUnit human = save.Units.First(u => u.Id == game.PlayerUnits.First().Id);
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

    private static Position AdjacentLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
}
