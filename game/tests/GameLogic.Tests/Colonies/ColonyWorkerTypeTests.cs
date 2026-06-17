using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Per-colonist worker-type overlay (<c>86d3b6nrz</c> slice 2): each colony worker carries a unit-type identity (a
/// sparse overlay over the count model — absent ⇒ free colonist), persisted in save v30 (omitted when all free).
/// Production is still type-blind in this slice; the overlay just <em>tracks</em> who's a specialist and round-trips.
/// </summary>
public class ColonyWorkerTypeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string ExpertFarmer = "model.unit.expertFarmer";
    private const string ExpertOreMiner = "model.unit.expertOreMiner";
    private const string MasterCarpenter = "model.unit.masterCarpenter";
    private const string TownHall = "model.building.townHall";

    private static Colony FoundFreeColony(Game game) =>
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));

    [Fact]
    public void ANonFreeColonistsType_IsTrackedSomewhereInTheOverlay()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreeColony(game);
        Position adj = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.ColonyAt(n) is null
                        && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit expert = game.SpawnUnit(Classic.Unit(ExpertOreMiner), adj);

        game.JoinColony(expert, colony); // joins → idle → maybe auto-assigned

        bool tracked = colony.TileWorkerTypes.Values.Contains(ExpertOreMiner)
            || colony.IdleWorkerTypes.Contains(ExpertOreMiner)
            || colony.BuildingWorkerTypes.Values.Any(l => l.Contains(ExpertOreMiner));
        Assert.True(tracked, "the joined expert's type should be recorded in the worker overlay");
    }

    [Fact]
    public void MixedWorkerTypes_RoundTripThroughSave_V30()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = FoundFreeColony(game);
        Position tile = colony.TileWorkers.Keys.First(); // the founding colonist's auto-assigned food tile

        // Build a known mix directly: an expert on a tile, a master carpenter in a building, an idle expert.
        colony.Population = 3;
        colony.SetWorker(tile, colony.TileWorkers[tile], ExpertFarmer);
        colony.AssignBuildingWorker(TownHall, MasterCarpenter);
        colony.AddIdleColonist(ExpertOreMiner);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Colony r = restored.Colonies.Single(c => c.Id == colony.Id);

        Assert.Equal(30, SaveGame.CurrentVersion);
        Assert.Equal(ExpertFarmer, r.WorkerTypeAt(tile));
        Assert.Contains(MasterCarpenter, r.BuildingWorkerTypes[TownHall]);
        Assert.Contains(ExpertOreMiner, r.IdleWorkerTypes);
    }

    [Fact]
    public void AnAllFreeColony_OmitsTheWorkerTypeTokens_AndRoundTripsAsAllFree()
    {
        Game game = Game.New(Classic, Seed);
        FoundFreeColony(game); // a free colonist founds + auto-assigns
        string json = SaveGame.From(game).ToJson();

        // Byte-identity gate: a free-colonist-only game emits none of the v30 worker-type tokens (so it stays
        // byte-identical to v29 but for the version field). This also stands in for "a pre-v30 save loads all-free".
        Assert.DoesNotContain("UnitTypeId", json);
        Assert.DoesNotContain("BuildingWorkerTypes", json);
        Assert.DoesNotContain("IdleWorkerTypes", json);

        Colony r = SaveGame.FromJson(json).Restore(Classic).Colonies.First();
        Assert.Empty(r.TileWorkerTypes);
        Assert.Empty(r.BuildingWorkerTypes);
        Assert.Empty(r.IdleWorkerTypes);
    }
}
