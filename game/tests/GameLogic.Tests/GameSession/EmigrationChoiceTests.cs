using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The emigration choice (`86d3c9xft`, FreeCol's <c>selectRecruit</c>): a human who has earned William Brewster
/// (<c>model.ability.selectRecruit</c>) is offered the three dock recruits when an emigrant is due, rather than a
/// random one auto-emigrating. <see cref="Game.PendingEmigration"/> exposes the pause; <see cref="Game.ChooseEmigrant"/>
/// resolves it. A player <em>without</em> Brewster keeps the historical random auto-emigrate (no pause).
/// </summary>
public class EmigrationChoiceTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Brewster = "model.foundingFather.williamBrewster";

    /// <summary>A unit-less, colony-less game (just the Europe dock) with the given congress + immigration banked.</summary>
    private static Game DockGame(ulong seed, IReadOnlyList<string>? congress, int immigration)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = seed,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.highSeas"],
            Units = [],
            Explored = [],
            Congress = congress,
            Immigration = immigration,
        };
        return save.Restore(Classic);
    }

    [Fact]
    public void NoChoiceIsPending_OnAFreshGame()
    {
        Assert.Null(Game.New(Classic, seed: 7).PendingEmigration);
    }

    [Fact]
    public void WithBrewster_AnEmigrantPausesForAChoice_InsteadOfAutoEmigrating()
    {
        // Immigration already at the target → an emigrant is due this turn. Brewster makes it a choice.
        Game game = DockGame(seed: 7, congress: [Brewster], immigration: Game.InitialImmigration);
        int unitsBefore = game.UnitsInEurope.Count();

        game.EndTurn();

        Assert.NotNull(game.PendingEmigration);
        Assert.Equal(game.HumanPlayer.PlayerId, game.PendingEmigration!.PlayerId);
        Assert.Equal(Game.RecruitSlots, game.PendingEmigration.RecruitTypeIds.Count);
        Assert.Equal(game.RecruitDock, game.PendingEmigration.RecruitTypeIds); // the live dock is the choice
        Assert.Equal(unitsBefore, game.UnitsInEurope.Count());                 // nobody emigrated yet — it's paused
    }

    [Fact]
    public void ChooseEmigrant_LandsTheChosenRecruit_AndClearsThePause()
    {
        Game game = DockGame(seed: 7, congress: [Brewster], immigration: Game.InitialImmigration);
        game.EndTurn();
        PendingEmigrationChoice pending = game.PendingEmigration!;
        string chosenTypeId = pending.RecruitTypeIds[1];
        int immigrationBefore = game.Immigration;

        Unit? recruit = game.ChooseEmigrant(1);

        Assert.NotNull(recruit);
        Assert.Equal(chosenTypeId, recruit!.Type.Id);                 // the slot the player picked emigrated
        Assert.Equal(UnitLocation.InEurope, recruit.Location);
        Assert.Contains(recruit, game.UnitsInEurope);
        Assert.True(game.Immigration < immigrationBefore);            // immigration was consumed
        Assert.Null(game.PendingEmigration);                         // only one was due → the pause clears
    }

    [Fact]
    public void ChooseEmigrant_OutOfRangeOrWhenNonePending_IsANoOp()
    {
        var fresh = Game.New(Classic, seed: 7);
        Assert.Null(fresh.ChooseEmigrant(0)); // nothing pending

        Game game = DockGame(seed: 7, congress: [Brewster], immigration: Game.InitialImmigration);
        game.EndTurn();
        Assert.NotNull(game.PendingEmigration);
        Assert.Null(game.ChooseEmigrant(99));         // out of range
        Assert.NotNull(game.PendingEmigration);       // still pending — no recruit taken
    }

    [Fact]
    public void WithoutBrewster_AnEmigrantAutoEmigrates_WithNoPause()
    {
        Game game = DockGame(seed: 7, congress: null, immigration: Game.InitialImmigration);
        int unitsBefore = game.UnitsInEurope.Count();

        game.EndTurn();

        Assert.Null(game.PendingEmigration);                          // no selectRecruit → no pause
        Assert.Equal(unitsBefore + 1, game.UnitsInEurope.Count());    // a recruit auto-emigrated as before
    }
}
