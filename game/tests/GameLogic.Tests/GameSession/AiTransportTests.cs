using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// AI logistics — transport missions + worker/goods wishes (<c>86d3c9vq9</c>, FreeCol
/// <c>TransportMission</c>/<c>WishRealizationMission</c>/<c>Wish</c>). A foreign power's carrier ferries its Europe
/// colonists to the colony that most wants a worker and lands them into it (the wish realisation), hauls a colony's
/// surplus to Europe to sell, and (the prior slice) banks a treasure train. Every choice draws the power's OWN RNG
/// stream, so the human's stream 0 stays byte-identical (ADR-009; the soak proves the byte-stability at scale).
/// </summary>
public class AiTransportTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Caravel = "model.unit.caravel";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string MasterDistiller = "model.unit.masterDistiller";

    /// <summary>Human (stream 0) + one foreign colonial power (id 1).</summary>
    private static List<SavedPlayer> HumanPlusForeign() =>
    [
        new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial),
        new SavedPlayer(1, "model.nation.dutch", IsHuman: false, PlayerType: (int)PlayerType.Colonial),
    ];

    /// <summary>
    /// A 3×1 coastal strip — plains colony at (0,0), ocean at (1,0), high seas at (2,0) — with a foreign colony owned
    /// by the power (id 1) and the given foreign units. The whole map is revealed to the power so its carrier can
    /// path to the high seas. Returns the game, the foreign colony and the power.
    /// </summary>
    private static (Game Game, Colony Colony, Player Power) CoastalFixture(
        IReadOnlyList<SavedUnit> units, int colonyPopulation = 3,
        IReadOnlyDictionary<string, int>? colonyStores = null)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
            Units = units,
            Explored = [0, 1, 2],
            Players = HumanPlusForeign(),
            Colonies = [new SavedColony(1, "Nieuw Holland", 0, 0, colonyPopulation, Stores: colonyStores, OwnerId: 1)],
        };
        Game game = save.Restore(Classic);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Colony colony = game.Colonies[0];
        foreach (Position p in game.Map.AllPositions())
        {
            power.ExploredSet.Add(p);
        }
        return (game, colony, power);
    }

    private static SavedUnit ForeignUnit(int id, string typeId, int x, int y, int location = (int)UnitLocation.OnMap) =>
        new(id, typeId, x, y, 12, location, OwnerId: 1);

    private static Unit UnitOf(Game game, int id) => game.Units.First(u => u.Id == id);

    // ───────────────────────── transport from Europe ─────────────────────────

    [Fact]
    public void ForeignCarrierInEurope_BoardsADockColonist_AndSailsForTheNewWorld()
    {
        // A foreign caravel and a free colonist both wait in the power's Europe; the power owns a colony to deliver to.
        (Game game, _, _) = CoastalFixture(
        [
            ForeignUnit(1, Caravel, 0, 0, (int)UnitLocation.InEurope),
            ForeignUnit(2, FreeColonist, 0, 0, (int)UnitLocation.InEurope),
        ]);

        game.EndTurn();

        Unit ship = UnitOf(game, 1);
        Unit colonist = UnitOf(game, 2);
        Assert.Equal(UnitLocation.SailingToNewWorld, ship.Location); // it set sail
        Assert.Equal(ship.Id, colonist.CarrierId);                   // with the colonist aboard
    }

    [Fact]
    public void ForeignCarrierInEurope_BoardsTheExpertFirst_TheStrongerWorkerWish()
    {
        // A caravel holds two; with three colonists and an expert waiting, the EXPERT boards first (it satisfies a
        // stronger worker wish, FreeCol WorkerWish prefers the wanted expert), and at least one free colonist is left.
        (Game game, _, _) = CoastalFixture(
        [
            ForeignUnit(1, Caravel, 0, 0, (int)UnitLocation.InEurope),
            ForeignUnit(2, FreeColonist, 0, 0, (int)UnitLocation.InEurope),
            ForeignUnit(3, FreeColonist, 0, 0, (int)UnitLocation.InEurope),
            ForeignUnit(4, MasterDistiller, 0, 0, (int)UnitLocation.InEurope),
        ]);

        game.EndTurn();

        Unit ship = UnitOf(game, 1);
        // The caravel holds two; the expert (id 4) is one of the two it boarded — experts are preferred over the
        // free colonists (ids 2,3), at least one of which is left on the dock.
        Assert.Equal(ship.Id, UnitOf(game, 4).CarrierId);
        Assert.Contains(game.Units, u => u.Id is 2 or 3 && u.Location == UnitLocation.InEurope && !u.IsAboard);
    }

    // ───────────────────────── wish realisation (deliver to colony) ─────────────────────────

    [Fact]
    public void ForeignCarrierBesideItsColony_LandsThePassengerIntoTheColony()
    {
        // A loaded caravel sits on the ocean tile (1,0) next to its colony at (0,0); the colonist aboard joins it. A
        // pop-1 colony (its plains centre feeds one with surplus, so the turn brings no starvation, and one turn's
        // surplus is nowhere near the 200-food growth threshold) — so the +1 here is purely the delivered colonist.
        (Game game, Colony colony, _) = CoastalFixture(
        [
            ForeignUnit(1, Caravel, 1, 0),
            ForeignUnit(2, FreeColonist, 1, 0),
        ], colonyPopulation: 1);
        // Put the colonist aboard the ship by hand (simulating a finished crossing).
        game.Board(UnitOf(game, 2), UnitOf(game, 1));
        int populationBefore = colony.Population;

        game.EndTurn();

        Assert.Equal(populationBefore + 1, colony.Population);       // the wish was realised — population grew
        Assert.DoesNotContain(game.Units, u => u.Id == 2);          // the colonist left the map into the colony
    }

    // ───────────────────────── goods haul + empty-carrier collection ─────────────────────────

    [Fact]
    public void EmptyForeignCarrier_WithColonistsWaitingInEurope_MakesForTheHighSeas()
    {
        // An empty caravel on the colony tile's ocean, with a colonist waiting in the power's Europe → it heads for
        // the high seas to cross and fetch them (it reaches (2,0) and sails, or steps toward it).
        (Game game, _, _) = CoastalFixture(
        [
            ForeignUnit(1, Caravel, 1, 0),
            ForeignUnit(2, FreeColonist, 0, 0, (int)UnitLocation.InEurope),
        ]);

        game.EndTurn();

        Unit ship = UnitOf(game, 1);
        // Either it sailed (reached the high seas this turn) or it is now standing on / closer to the high seas tile.
        Assert.True(
            ship.Location == UnitLocation.SailingToEurope || ship.Position.X > 1,
            $"the empty carrier did not make for the high seas (loc={ship.Location}, x={ship.Position.X})");
    }

    // ───────────────────────── byte-stability (ADR-009) ─────────────────────────

    [Fact]
    public void TransportRuns_LeaveTheHumansStream0ByteIdentical()
    {
        // Two same-seed full games (human + foreign economies + transport) end byte-identical: every transport choice
        // draws the foreign power's own stream and the board/sail/join seams are RNG-free, so the human's stream 0 is
        // never touched. A heavily-funded perturbed run makes the rivals ship far more — yet stream 0 is unmoved.
        Game baseline = Game.New(Classic, seed: 13579);
        Game perturbed = Game.New(Classic, seed: 13579);

        for (int turn = 0; turn < 25; turn++)
        {
            foreach (Player rival in perturbed.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial))
            {
                rival.Gold += 1000; // fund the rivals so they recruit + ship far more than the baseline
            }
            baseline.EndTurn();
            perturbed.EndTurn();
        }

        // The perturbation genuinely diverged the rivals…
        Assert.NotEqual(SaveGame.From(baseline).ToJson(), SaveGame.From(perturbed).ToJson());
        // …yet the human's RNG stream 0 and its own scoped state are byte-identical — no transport path touched them.
        Assert.Equal(baseline.RandomState, perturbed.RandomState);
        Assert.Equal(baseline.HumanPlayer.Gold, perturbed.HumanPlayer.Gold);
        Assert.Equal(baseline.HumanPlayer.Immigration, perturbed.HumanPlayer.Immigration);
    }
}
