using System;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Tory (loyalist) expulsion on the Declaration of Independence (<c>86d3fq0e0</c>) — a <b>Col1-faithful
/// reconstruction</b> our model adds (FreeCol's <c>csDeclareIndependence</c> omits it). On declaring, each colony
/// loses <c>floor(toryCount · (100 − SoL%) / 100)</c> of its non-rebel colonists (capped to leave ≥ 1): a low-loyalty
/// colony bleeds royalists, a fully-committed 100%-SoL colony loses none. RNG-free → deterministic (ADR-009).
/// </summary>
public class ToryExpulsionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>
    /// A fresh game with the starting roster disbanded and one coastal colony of the requested population and
    /// Sons-of-Liberty percentage (so a declaration's national-SoL gate and per-colony Tory loss are both controllable).
    /// SoL is set via liberty: <c>liberty = 2·SoL·population</c> (since <c>SoL% = liberty·100/(200·population)</c>).
    /// </summary>
    private static (Game Game, Colony Colony) ColonyAt(int population, int sol, ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u); // clear the starting roster so only our colony defines the national SoL
        }
        Colony colony = FoundCoastalColony(game);
        colony.Population = population;
        colony.Liberty = 2 * sol * population; // SoL% = liberty·100 / (200·population) = sol
        Assert.Equal(sol, colony.SonsOfLiberty);
        return (game, colony);
    }

    /// <summary>Founds a colony on the first coastal tile where founding is actually legal (skips water, mountains and
    /// native-owned land), spawning the founding colonist there. Returns the new colony.</summary>
    private static Colony FoundCoastalColony(Game game)
    {
        foreach (Position p in game.Map.AllPositions().Where(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater)
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)))
        {
            Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), p);
            if (game.CheckFoundColony(colonist).Allowed)
            {
                try
                {
                    return game.FoundColony(colonist); // throws LandClaimRequiredException on native-owned land
                }
                catch (LandClaimRequiredException)
                {
                    // native-owned tile — fall through and try the next
                }
            }
            game.Disband(colonist); // unfoundable site (mountain / native land) — try the next
        }
        throw new InvalidOperationException("No foundable coastal tile on this map.");
    }

    [Fact]
    public void LowSoLColony_LosesTories_OnDeclaration()
    {
        // Population 10, SoL 60 → 6 rebels, 4 tories. Loss = floor(4·(100−60)/100) = floor(1.6) = 1.
        (Game game, Colony colony) = ColonyAt(population: 10, sol: 60);
        Assert.Equal(4, colony.ToryCount);

        game.DeclareIndependence(game.HumanPlayer);

        Assert.Equal(9, colony.Population); // one loyalist deserted
        ToryExpulsionNotice notice = Assert.Single(game.ToryExpulsionNotices);
        Assert.Equal(colony.Name, notice.ColonyName);
        Assert.Equal(1, notice.ToriesLost);
        Assert.Equal(9, notice.PopulationAfter);
    }

    [Fact]
    public void VeryLowSoLColony_LosesMoreTories_ThanAModerateOne()
    {
        // Population 10, SoL 50 → 5 rebels, 5 tories. Loss = floor(5·(100−50)/100) = floor(2.5) = 2. (National SoL 50
        // still meets the ≥ 50 declaration gate.) A harsher loss than the SoL-60 colony above (which lost just 1).
        (Game game, Colony colony) = ColonyAt(population: 10, sol: 50);
        Assert.Equal(5, colony.ToryCount);

        game.DeclareIndependence(game.HumanPlayer);

        Assert.Equal(8, colony.Population);
        Assert.Equal(2, Assert.Single(game.ToryExpulsionNotices).ToriesLost);
    }

    [Fact]
    public void FullSoLColony_LosesNoTories_OnDeclaration()
    {
        // Population 10, SoL 100 → 10 rebels, 0 tories. A fully-committed colony loses no-one; no notice is emitted.
        (Game game, Colony colony) = ColonyAt(population: 10, sol: 100);
        Assert.Equal(0, colony.ToryCount);

        game.DeclareIndependence(game.HumanPlayer);

        Assert.Equal(10, colony.Population);
        Assert.Empty(game.ToryExpulsionNotices);
    }

    [Fact]
    public void Expulsion_NeverEmptiesAColony_KeepsAtLeastOneColonist()
    {
        // Population 2, SoL 50 → 1 rebel, 1 tory. Loss = floor(1·50/100) = 0 here, but force the worst case: a 0%-SoL
        // colony of population 3 would lose all 3 tories → capped to 2 so one colonist always remains. We use a second
        // high-SoL colony to keep the NATIONAL SoL ≥ 50 so the declaration is still allowed.
        (Game game, Colony lowColony) = ColonyAt(population: 3, sol: 0);
        // A fervent second colony to satisfy the national-SoL gate (3 rebels vs the low colony's 0 → national 50%).
        Colony fervent = FoundCoastalColony(game);
        fervent.Population = 3;
        fervent.Liberty = 2 * 100 * 3; // SoL 100 → 3 rebels
        Assert.True(game.NationalSonsOfLiberty(game.HumanPlayer) >= 50);

        game.DeclareIndependence(game.HumanPlayer);

        Assert.Equal(1, lowColony.Population); // 3 tories would all flee, but the floor keeps one colonist
        Assert.Equal(2, game.ToryExpulsionNotices.Single(n => n.Position == lowColony.Position).ToriesLost);
    }

    [Fact]
    public void Expulsion_IsDeterministic_UnderTheSameSeed()
    {
        // RNG-free: two games declaring from identical low-SoL rosters lose identical loyalists (ADR-009).
        (Game a, Colony ca) = ColonyAt(population: 10, sol: 50, seed: 0x1234UL);
        (Game b, Colony cb) = ColonyAt(population: 10, sol: 50, seed: 0x1234UL);

        a.DeclareIndependence(a.HumanPlayer);
        b.DeclareIndependence(b.HumanPlayer);

        Assert.Equal(ca.Population, cb.Population);
        Assert.Equal(
            a.ToryExpulsionNotices.Select(n => n.ToriesLost),
            b.ToryExpulsionNotices.Select(n => n.ToriesLost));
    }
}
