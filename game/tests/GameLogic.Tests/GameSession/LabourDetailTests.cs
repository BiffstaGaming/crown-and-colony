using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The labour-detail drill-down oracle (`86d3fq0g6` — FreeCol's <see cref="Game.LabourDetail"/> /
/// <c>ReportLabourDetailPanel</c>): for one unit type, <b>where the human's colonists of that type are</b> — a
/// per-location head-count over the same colonists the flat Labour report tallies (colony residents from the worker
/// overlays + on-map person units, under <see cref="Game.LabourFieldLocation"/>). Read-only (ADR-006), RNG-free.
/// </summary>
public class LabourDetailTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const string ExpertFarmer = "model.unit.expertFarmer";

    /// <summary>A 3×3 game with a pop-3 colony (2 idle expert farmers + 1 idle free colonist) and two on-map expert farmers.</summary>
    private static Game Setup() => new SaveGame
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 3, MapHeight = 3,
        Terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.mountains",
        ],
        // Two on-map expert farmers (in the field) + one free colonist on the map.
        Units =
        [
            new SavedUnit(1, ExpertFarmer, 2, 0, 3),
            new SavedUnit(2, ExpertFarmer, 2, 1, 3),
            new SavedUnit(3, FreeColonist, 0, 1, 3),
        ],
        // A colony at (1,1) holding three idle residents: two expert farmers + one free colonist (the free remainder).
        Colonies =
        [
            new SavedColony(10, "Capital", 1, 1, 3, IdleWorkerTypes: [ExpertFarmer, ExpertFarmer]),
        ],
        Explored = [],
    }.Restore(Classic);

    [Fact]
    public void LabourDetail_ForAType_BucketsTheColonistsByLocation()
    {
        Game game = Setup();

        var detail = game.LabourDetail(ExpertFarmer);

        // Two expert farmers idle in the Capital, two more on the map (the field).
        Game.LabourLocation colony = detail.Single(l => l.Location == "Capital");
        Assert.Equal(2, colony.Count);
        Game.LabourLocation field = detail.Single(l => l.Location == Game.LabourFieldLocation);
        Assert.Equal(2, field.Count);
        Assert.Equal(2, detail.Count); // only those two locations have expert farmers
    }

    [Fact]
    public void LabourDetail_CountsTheFreeColonistRemainder_InTheColony()
    {
        Game game = Setup();

        var detail = game.LabourDetail(FreeColonist);

        // The colony's third idle resident is a free colonist (pop 3 − the two named expert farmers); one free colonist
        // also stands on the map.
        Assert.Equal(1, detail.Single(l => l.Location == "Capital").Count);
        Assert.Equal(1, detail.Single(l => l.Location == Game.LabourFieldLocation).Count);
    }

    [Fact]
    public void LabourDetail_ForATypeTheHumanHasNoneOf_IsEmpty()
    {
        Game game = Setup();
        Assert.Empty(game.LabourDetail("model.unit.veteranSoldier"));
    }

    [Fact]
    public void LabourDetail_IsReadOnly_LeavesTheGameByteIdentical()
    {
        Game game = Setup();
        string before = SaveGame.From(game).ToJson();

        _ = game.LabourDetail(ExpertFarmer);
        _ = game.LabourDetail(FreeColonist);

        Assert.Equal(before, SaveGame.From(game).ToJson()); // a report read mutates nothing (ADR-006/009)
    }
}
