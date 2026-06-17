using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Native tile ownership (foundation for the LCR burial ground, native land purchase, and settle/work tension):
/// each native settlement claims the land within its <see cref="SettlementType.ClaimableRadius"/>
/// (FreeCol <c>Tile.owner</c>). The claim is a pure, deterministic function of settlement positions + radii, so
/// it is derived at game start and re-derived on load — never saved.
/// </summary>
public class NativeLandClaimTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    private const string Camp = "model.settlement.camp";              // claimable-radius 1
    private const string Apache = "model.nationType.apache";
    private const string Sioux = "model.nationType.sioux";

    private static GameMap Map(int w, int h, params Position[] water)
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        var waterSet = water.ToHashSet();
        var terrain = new System.Collections.Generic.List<TerrainType>(w * h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                terrain.Add(waterSet.Contains(new Position(x, y)) ? ocean : plains);
            }
        }
        return new GameMap(w, h, terrain);
    }

    private static NativeSettlement Camp_(int id, string nation, Position p) =>
        new(id, nation, Camp, isCapital: false, p, size: 1, learnableSkill: null);

    /// <summary>Forces a combat win (NextDouble 0 &lt; any win probability) with no promotion / minimum plunder draw.</summary>
    private sealed class ForceWin : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0.0;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    [Fact]
    public void Claim_OwnsTheClaimableRadiusDisc_OnLand()
    {
        GameMap map = Map(5, 5);
        var owned = NativeLandClaimGenerator.Claim(map, [Camp_(1, Apache, new Position(2, 2))], Classic);

        Assert.Equal(9, owned.Count);                                  // radius 1 → 3×3 disc incl. own tile
        Assert.All(owned.Values, n => Assert.Equal(Apache, n));
        Assert.Contains(new Position(2, 2), owned.Keys);               // its own tile
        Assert.Contains(new Position(1, 1), owned.Keys);               // a corner of the disc
        Assert.DoesNotContain(new Position(0, 2), owned.Keys);         // Chebyshev distance 2 → outside radius 1
    }

    [Fact]
    public void Claim_ExcludesOffMapAndWaterTiles()
    {
        // Camp in the corner (0,0): the (-1,*) / (*,-1) ring is off-map, and (1,0) is ocean.
        GameMap map = Map(3, 3, water: new Position(1, 0));
        var owned = NativeLandClaimGenerator.Claim(map, [Camp_(1, Apache, new Position(0, 0))], Classic);

        Assert.Equal(new[] { new Position(0, 0), new Position(0, 1), new Position(1, 1) }.ToHashSet(),
            owned.Keys.ToHashSet());                                   // off-map + the ocean tile dropped
    }

    [Fact]
    public void Claim_ResolvesOverlaps_NearestThenLowestId()
    {
        // Two camps two tiles apart on a row: their radius-1 claims overlap on column x=2.
        GameMap map = Map(5, 5);
        var owned = NativeLandClaimGenerator.Claim(map,
            [Camp_(2, Sioux, new Position(3, 2)), Camp_(1, Apache, new Position(1, 2))], Classic);

        // The overlap column is equidistant (distance 1) from both → the lower id (1, Apache) wins.
        Assert.Equal(Apache, owned[new Position(2, 2)]);
        Assert.Equal(Apache, owned[new Position(2, 1)]);
        Assert.Equal(Apache, owned[new Position(0, 2)]); // only Apache reaches here
        Assert.Equal(Sioux, owned[new Position(4, 2)]);  // only Sioux reaches here
    }

    [Fact]
    public void Claim_ReleasesTilesWhenItsSettlementIsGone()
    {
        // The pure function underpins re-derivation: drop a settlement and its exclusive tiles are unclaimed.
        GameMap map = Map(5, 5);
        NativeSettlement s1 = Camp_(1, Apache, new Position(1, 2));
        NativeSettlement s2 = Camp_(2, Sioux, new Position(3, 2));

        var both = NativeLandClaimGenerator.Claim(map, [s1, s2], Classic);
        var onlyS1 = NativeLandClaimGenerator.Claim(map, [s1], Classic);

        Assert.True(both.ContainsKey(new Position(4, 2)));        // a Sioux-only tile, claimed while s2 stands
        Assert.False(onlyS1.ContainsKey(new Position(4, 2)));     // released once s2 is gone
        Assert.Equal(Apache, onlyS1[new Position(2, 2)]);         // s1 keeps the former overlap
    }

    [Fact]
    public void Claim_IsDeterministic()
    {
        GameMap map = Map(6, 6);
        NativeSettlement[] s = [Camp_(1, Apache, new Position(1, 1)), Camp_(2, Sioux, new Position(4, 4))];
        Assert.Equal(
            NativeLandClaimGenerator.Claim(map, s, Classic).OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X),
            NativeLandClaimGenerator.Claim(map, s, Classic).OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X));
    }

    // ---- Integration ----

    [Fact]
    public void ANewGame_ClaimsLandAroundEachNativeSettlement()
    {
        Game game = Game.New(Classic, Seed);

        Assert.NotEmpty(game.Map.NativeOwners);
        Assert.All(game.NativeSettlements, s =>
        {
            Assert.True(game.Map.IsNativeOwned(s.Position));            // a settlement owns the tile it sits on
            Assert.Equal(s.NationTypeId, game.Map.NativeOwnerOf(s.Position));
        });
        Assert.All(game.Map.NativeOwners.Keys, p => Assert.False(game.Map.TerrainAt(p).IsWater)); // land only
    }

    [Fact]
    public void DestroyingANativeSettlement_ReleasesItsLandClaim()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement target = game.NativeSettlements.First(s =>
            s.Position.Neighbours().Any(n => FreeLand(game, n)));
        Position adj = target.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit("model.unit.artillery"), adj); // offence 7
        Assert.True(game.Map.IsNativeOwned(target.Position));            // it owns the tile it sits on

        game.AttackSettlement(attacker, target.Position, new ForceWin()); // a win razes the settlement

        Assert.DoesNotContain(target, game.NativeSettlements);            // destroyed
        // The claim was re-derived: the live ownership now matches a fresh claim over the SURVIVING settlements.
        // (Without the re-run the razed settlement's exclusive tiles — or its tile's owning nation — would differ.)
        Assert.Equal(
            NativeLandClaimGenerator.Claim(game.Map, game.NativeSettlements, Classic)
                .OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList(),
            game.Map.NativeOwners.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList());
    }

    [Fact]
    public void NativeOwnership_ReDerivesIdenticallyOnLoad()
    {
        Game game = Game.New(Classic, Seed);
        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Map.NativeOwners.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList(),
            restored.Map.NativeOwners.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList());
    }
}
