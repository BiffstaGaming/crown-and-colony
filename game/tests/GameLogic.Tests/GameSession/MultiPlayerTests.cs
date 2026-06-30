using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Multi-player scaffolding (FP-3b, ADR-019): the human plus the foreign colonial powers and the native
/// nations as players, the ring-buffer turn, and the multi-element save. Every non-human player draws only
/// from its own RNG stream (foreign powers run an economy + AI; natives raid/wander from 1b), so the human's
/// stream 0 stays byte-stable. Native-AI specifics live in <see cref="NativeAiTests"/>.
/// </summary>
public class MultiPlayerTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void NewGame_HasHuman_ForeignPowers_AndNativePlayers()
    {
        var game = Game.New(Classic, seed: 7);

        Player human = Assert.Single(game.Players, p => p.IsHuman);
        Assert.Equal(0, human.PlayerId);
        Assert.Equal(PlayerType.Colonial, human.PlayerType);
        Assert.Null(human.NationId);
        Assert.Same(human, game.HumanPlayer);
        Assert.Same(human, game.CurrentPlayer); // it is the human's turn

        // Three inert foreign colonial powers, each a real European nation.
        var foreignPowers = game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList();
        Assert.Equal(3, foreignPowers.Count);
        Assert.All(foreignPowers, p => Assert.Contains(Classic.EuropeanNations, n => n.Id == p.NationId));

        // Every distinct native nation present is a Native player.
        var nativeNations = game.NativeSettlements.Select(s => s.NationTypeId).Distinct().ToList();
        var nativePlayers = game.Players.Where(p => p.PlayerType == PlayerType.Native).ToList();
        Assert.Equal(nativeNations.Count, nativePlayers.Count);
        Assert.All(nativePlayers, p => Assert.Contains(p.NationId, nativeNations));

        // Player ids are dense and unique (0..N-1).
        Assert.Equal(Enumerable.Range(0, game.Players.Count), game.Players.Select(p => p.PlayerId).OrderBy(i => i));
    }

    [Fact]
    public void ForeignPowers_StartOnTheMap_HiddenFromTheHuman()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

        var theirUnits = game.Units.Where(u => u.OwnerNationId is null && u.OwnerId == power.PlayerId).ToList();
        Assert.NotEmpty(theirUnits);
        Assert.Contains(theirUnits, u => u.IsOnMap);                   // landed on the map (FP-4), not docked in Europe
        Assert.DoesNotContain(theirUnits, u => game.PlayerUnits.Contains(u)); // never the human's
        // A foreign power lands far from the human, so its units do not lift the human's fog.
        Assert.All(theirUnits.Where(u => u.IsOnMap), u => Assert.False(game.IsVisible(u.Position)));
        Assert.Empty(game.UnitsInEurope);                              // the human has none of its own in Europe
    }

    [Fact]
    public void EndTurn_RunsForeignPowerAi_AndItsEconomy()
    {
        var game = Game.New(Classic, seed: 7);
        int turnBefore = game.Turn;

        game.EndTurn();

        Assert.Equal(turnBefore + 1, game.Turn);            // the world advanced once
        Assert.Same(game.HumanPlayer, game.CurrentPlayer);  // control returned to the human
        // A foreign power acted — it founded a colony on the land it settled (FP-4).
        Assert.Contains(game.Colonies, c => game.Players.Any(p =>
            p.PlayerId == c.OwnerId && !p.IsHuman && p.PlayerType == PlayerType.Colonial));
        // …and it now runs an economy (FP-5): it accrued immigration on its own (the flat +2 player bonus,
        // independent of terrain) and it has its own Europe recruit dock.
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Assert.True(power.Immigration > 0, "the foreign power accrued no immigration");
        Assert.NotEmpty(power.RecruitDock);
    }

    [Fact]
    public void ForeignPowerAi_IsReplayStable_ForAFixedSeed()
    {
        var a = Game.New(Classic, seed: 31337);
        var b = Game.New(Classic, seed: 31337);
        for (int turn = 0; turn < 20; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }

        // Same seed → byte-identical games after 20 turns of foreign-power AI: every AI choice draws from
        // that player's own deterministic stream (ADR-009), and the human's stream 0 is never touched.
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        Assert.Contains(a.Colonies, c => c.OwnerId != 0); // a foreign power was active — it founded a colony
    }

    [Fact]
    public void MultiPlayerSave_RoundTripsAllPlayers_AndForeignUnits()
    {
        var game = Game.New(Classic, seed: 7);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Players.Select(p => (p.PlayerId, p.NationId, p.IsHuman, p.PlayerType)).OrderBy(t => t.PlayerId),
            loaded.Players.Select(p => (p.PlayerId, p.NationId, p.IsHuman, p.PlayerType)).OrderBy(t => t.PlayerId));

        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Assert.Equal(
            game.Units.Count(u => u.OwnerId == power.PlayerId && u.OwnerNationId is null),
            loaded.Units.Count(u => u.OwnerId == power.PlayerId && u.OwnerNationId is null));
    }

    // The Chebyshev distance between two tiles (the same metric the placement code spaces powers by).
    private static int Cheb(CrownAndColony.GameLogic.World.Position a, CrownAndColony.GameLogic.World.Position b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    [Theory]
    [InlineData(7)]
    [InlineData(31337)]
    [InlineData(99)]
    public void ForeignPowers_SpreadAlongTheCoast_NotClusteredAtOneCorner(ulong seed)
    {
        // 86d3c9w5n: each landed foreign power's anchor should sit a sensible distance from every other power's, so
        // rivals don't all pile into one corner. We take each power's first on-map unit as its anchor proxy.
        var game = Game.New(Classic, seed: seed);
        var anchors = game.Players
            .Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial)
            .Select(p => game.Units.Where(u => u.OwnerId == p.PlayerId && u.IsOnMap)
                .OrderBy(u => u.Id).Select(u => (CrownAndColony.GameLogic.World.Position?)u.Position).FirstOrDefault())
            .Where(pos => pos is not null)
            .Select(pos => pos!.Value)
            .ToList();

        // At least two powers landed (the default map has room), and every distinct pair is more than 1 tile apart
        // (no two powers share a corner/adjacent tile) — the spacing pass spreads them.
        Assert.True(anchors.Count >= 2, "expected at least two foreign powers to land on the map");
        for (int i = 0; i < anchors.Count; i++)
        {
            for (int j = i + 1; j < anchors.Count; j++)
            {
                Assert.True(Cheb(anchors[i], anchors[j]) > 1,
                    $"powers landed too close: {anchors[i]} vs {anchors[j]} (seed {seed})");
            }
        }
    }

    [Fact]
    public void Spec_ParsesStartsOnEastCoast_DefaultTrue_RussiaFalse()
    {
        // FreeCol <nation starts-on-east-coast="…">: absent ⇒ true (the Atlantic seaboard); only Russia sets false.
        Assert.True(Classic.EuropeanNation("model.nation.dutch").StartsOnEastCoast);
        Assert.True(Classic.EuropeanNation("model.nation.french").StartsOnEastCoast);
        Assert.True(Classic.EuropeanNation("model.nation.spanish").StartsOnEastCoast);
        Assert.False(Classic.EuropeanNation("model.nation.russian").StartsOnEastCoast);
    }

    /// <summary>The landing X of the foreign power playing <paramref name="nationId"/> in <paramref name="game"/>, or null if it never landed on the map.</summary>
    private static int? RivalLandingX(Game game, string nationId)
    {
        CrownAndColony.GameLogic.World.Position? anchor = game.Players
            .Where(p => p.NationId == nationId && !p.IsHuman)
            .SelectMany(p => game.Units.Where(u => u.OwnerId == p.PlayerId && u.IsOnMap).OrderBy(u => u.Id))
            .Select(u => (CrownAndColony.GameLogic.World.Position?)u.Position)
            .FirstOrDefault();
        return anchor?.X;
    }

    [Fact]
    public void ForeignPowers_AreBiasedOntoTheirNationsPreferredCoast_AcrossManySeeds()
    {
        // 86d3fq1eb (FreeCol CLASSIC startsOnEastCoast): the coast preference is the LEAD candidate-sort key, so rivals
        // land overwhelmingly on their nation's home coast. On a crowded eastern coast the spacing pass may relax one
        // rival off-coast (documented in LandForeignPower), so this asserts the AGGREGATE bias across many seeds — robust
        // against that rare relaxation — rather than strict per-rival equality (which flakes ~3% of seeds). The default
        // classic rivals are all east-coast, so they should land on the eastern (high-X, Atlantic) half.
        int onPreferred = 0, total = 0;
        for (ulong seed = 0; seed < 40; seed++)
        {
            Game game = Game.New(Classic, seed: seed);
            int mid = game.Map.Width / 2;
            foreach (Player p in game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial && p.NationId is not null))
            {
                if (RivalLandingX(game, p.NationId!) is not { } x)
                {
                    continue;
                }
                total++;
                if ((x >= mid) == Classic.EuropeanNation(p.NationId!).StartsOnEastCoast)
                {
                    onPreferred++;
                }
            }
        }
        Assert.True(total > 0, "no foreign power landed across the sampled seeds");
        // In practice ~99% of placements honour the preferred coast (the spacing relaxation is <1%); assert a wide margin.
        Assert.True(onPreferred >= total * 0.9,
            $"only {onPreferred}/{total} rivals landed on their preferred coast (expected ≥90% — the coast bias should dominate)");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(31337)]
    [InlineData(12)]   // a seed the old strict 3-rival check relaxed one rival off-coast on; a lone rival never relaxes
    public void ALoneRival_LandsStrictlyOnItsNationsPreferredCoast(ulong seed)
    {
        // With a single rival there is no inter-power spacing to force a relaxation, so the lone (east-coast) rival lands
        // strictly on its preferred coast — a clean per-rival proof that the StartsOnEastCoast flag drives placement.
        Game game = Game.New(Classic, seed: seed, foreignPowerCount: 1);
        Player rival = game.Players.Single(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial && p.NationId is not null);
        int? x = RivalLandingX(game, rival.NationId!);
        Assert.NotNull(x);
        Assert.Equal(Classic.EuropeanNation(rival.NationId!).StartsOnEastCoast, x!.Value >= game.Map.Width / 2);
    }

    [Fact]
    public void Russia_TheOnlyWestCoastPower_LandsOnTheWesternHalf()
    {
        // The StartsOnEastCoast=false branch end-to-end (the default roster's first powers are all east-coast, so only a
        // full 8-power roster includes Russia). The seven east-coast nations fill the eastern coast and Russia takes the
        // western half. Aggregated over seeds so an occasional crowded relaxation can't flake it.
        int west = 0, seen = 0;
        for (ulong seed = 0; seed < 30; seed++)
        {
            Game game = Game.New(Classic, seed: seed, foreignPowerCount: 8);
            if (RivalLandingX(game, "model.nation.russian") is not { } x)
            {
                continue; // Russia docked in Europe on a crowded map this seed — not counted
            }
            seen++;
            if (x < game.Map.Width / 2)
            {
                west++;
            }
        }
        Assert.True(seen > 0, "Russia never landed on the map with the full 8-power roster");
        Assert.True(west >= seen * 0.8, $"Russia landed on the western half only {west}/{seen} times (its west-coast bias should dominate)");
    }

    [Fact]
    public void RefEntryTile_IsSet_NearTheHumanStart_OnWater()
    {
        var game = Game.New(Classic, seed: 7);
        Assert.NotNull(game.RefEntryTile);
        CrownAndColony.GameLogic.World.Position entry = game.RefEntryTile!.Value;
        Assert.True(game.Map.TerrainAt(entry).IsWater, "the REF entry tile must be a water tile");

        // It is the nearest water to the human's start — no land-or-water tile is closer to the start that is also water.
        CrownAndColony.GameLogic.World.Position humanStart = game.Units.First(u => u.OwnerId == 0 && u.IsOnMap).Position;
        int entryDist = Cheb(entry, humanStart);
        Assert.All(game.Map.AllPositions().Where(p => game.Map.TerrainAt(p).IsWater),
            p => Assert.True(Cheb(p, humanStart) >= entryDist));
    }

    [Fact]
    public void RefEntryTile_RoundTripsThroughSave()
    {
        var game = Game.New(Classic, seed: 7);
        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(game.RefEntryTile, loaded.RefEntryTile);
    }

    [Fact]
    public void RefEntryTile_OmittedWhenUnset_AndPreV47LoadsWithNone()
    {
        var game = Game.New(Classic, seed: 7);
        // Simulate an old save that predates the REF entry tile: drop it + back-date the version.
        SaveGame old = SaveGame.From(game) with { Version = 46, RefEntryTile = null };
        Assert.DoesNotContain("\"RefEntryTile\"", old.ToJson());
        Game loaded = SaveGame.FromJson(old.ToJson()).Restore(Classic);
        Assert.Null(loaded.RefEntryTile);
    }
}
