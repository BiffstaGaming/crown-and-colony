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
/// Foreign-AI colony capture (slice 1c-3f): colony capture is now two-directional. A foreign power at war with
/// the human, whose armed <b>land</b> unit stands beside an <b>undefended</b> human colony, captures it during
/// its AI turn (reusing the shared <see cref="Game.AttackColony(Unit, Position, Randomness.IGameRandom)"/> path
/// on the power's OWN RNG stream) and the human is told via a <see cref="Combat.ColonyLossNotice"/>. A garrisoned
/// colony is fought through the unit-hunt first. War is the gate; at peace the foreign AI expands as before. As
/// with all AI combat, every draw is from the power's own stream (ADR-009) — the human's stream 0 stays stable.
/// </summary>
public class ForeignColonyCaptureTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Artillery = "model.unit.artillery"; // offence 7 — overwhelms a colony's lone colonist (defence ~1)

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>The human founds an (undefended) colony; a foreign power's armed artillery stands beside it; optionally already at war.</summary>
    private static (Game game, Colony colony, Player power, int humanId) Stage(ulong seed, bool atWar)
    {
        Game game = Game.New(Classic, seed);
        int humanId = game.HumanPlayer.PlayerId;
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder); // human-owned; undefended (the founder became its population)

        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj);
        attacker.OwnerId = power.PlayerId; // reassign from the human to the foreign power (SpawnUnit owners players via OwnerId)
        // Capture odds are deliberately overwhelming (artillery offence 7 ×1.5 attack bonus vs the colony's lone
        // colonist defence 1 → win prob ≈ 0.91), so the at-war capture lands turn 1 for seed 7 — the capture
        // assertions below hinge on that single roll. If an unrelated change shifts this power's stream-1
        // consumption the roll can re-seed; re-pick the seed (the L3 scene sibling hardens with three attackers).
        if (atWar)
        {
            game.SetStance(power.PlayerId, humanId, Stance.War);
        }
        return (game, colony, power, humanId);
    }

    [Fact]
    public void AtWar_CapturesAnAdjacentUndefendedHumanColony_AndRecordsALossNotice()
    {
        (Game game, Colony colony, Player power, _) = Stage(seed: 7, atWar: true);
        Position pos = colony.Position;
        string name = colony.Name;

        game.EndTurn();

        Assert.Equal(power.PlayerId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // it is the rival's now
        Assert.Contains(game.ColonyLossNotices,
            n => n.AttackerNationId == power.NationId && n.Position == pos && n.ColonyName == name);
    }

    [Fact]
    public void WhenNotAtWar_DoesNotCaptureTheHumanColony()
    {
        // The gate is War; the default (un-provoked) stance is Uncontacted, so a foreign soldier sitting right
        // beside an undefended human colony expands/idles instead of seizing it.
        (Game game, Colony colony, _, int humanId) = Stage(seed: 7, atWar: false);
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(humanId, game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId));

        game.EndTurn();

        Assert.Equal(humanId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // still ours
        Assert.Empty(game.ColonyLossNotices);
    }

    [Fact]
    public void GarrisonedHumanColony_IsNotCaptured_TheGarrisonIsFoughtFirst()
    {
        // A unit standing on the colony tile means CheckAttackColony refuses the assault, so the foreign unit
        // hunts the garrison instead. The colony cannot change hands while defended — whatever the fight's outcome.
        (Game game, Colony colony, Player power, int humanId) = Stage(seed: 7, atWar: true);
        Unit garrison = game.SpawnUnit(Classic.Unit(Artillery), colony.Position); // a human defender on the tile
        Assert.Equal(humanId, garrison.OwnerId);

        game.EndTurn();

        Assert.Equal(humanId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // not captured while defended
        Assert.Empty(game.ColonyLossNotices);
    }

    [Fact]
    public void Capture_IsReplayStable_ForAFixedSeed()
    {
        (Game a, _, _, _) = Stage(seed: 7, atWar: true);
        (Game b, _, _, _) = Stage(seed: 7, atWar: true);
        for (int turn = 0; turn < 6; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    private static int Cheb(Position a, Position b) => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    [Fact]
    public void AtWar_WithNoFieldPrey_MarchesOnTheNearestHumanColony()
    {
        // The besiege fallback (86d3bx03d): a war-power land unit with no human field unit to chase steps toward
        // the nearest human colony instead of wandering off to explore. Empty the map of human units (found the
        // colony with the one on-map unit), then place a foreign artillery two tiles from the colony with a clear
        // closer step, at war — it should close the distance.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        int humanId = game.HumanPlayer.PlayerId;
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        Assert.DoesNotContain(game.PlayerUnits, u => u.IsOnMap); // no field prey for a foreign land unit

        // A free land tile at distance 2 from the colony that has a free-land neighbour at distance 1 (a legal closer step).
        Position spot = game.Map.AllPositions().First(p =>
            Cheb(p, colony.Position) == 2 && FreeLand(game, p)
            && p.Neighbours().Any(n => Cheb(n, colony.Position) == 1 && FreeLand(game, n)));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), spot);
        attacker.OwnerId = power.PlayerId;
        game.SetStance(power.PlayerId, humanId, Stance.War);

        game.EndTurn();

        Assert.True(Cheb(attacker.Position, colony.Position) < 2); // it marched on the colony (closed from distance 2)
    }

    [Fact]
    public void Capture_DoesNotTouchTheHumansStream0()
    {
        // Same seed + same staged colony/attacker in both; only one is at war. In one EndTurn the war game's
        // power captures the colony (its own stream) while the peace game's artillery idles. The capture happens
        // in the foreign phase, AFTER the human's identical stream-0 turn — so the save genuinely diverges yet
        // stream 0 is byte-identical. (One turn only: from the next turn the war game's human produces on one
        // fewer colony, which is a legitimate downstream difference, not a stream-0 violation.)
        (Game peace, Colony peaceColony, _, _) = Stage(seed: 7, atWar: false);
        (Game war, Colony warColony, Player power, _) = Stage(seed: 7, atWar: true);

        peace.EndTurn();
        war.EndTurn();

        Assert.Equal(power.PlayerId, war.Colonies.First(c => c.Id == warColony.Id).OwnerId);   // the war game really captured…
        Assert.NotEqual(SaveGame.From(peace).ToJson(), SaveGame.From(war).ToJson());           // …so the games diverged…
        Assert.Equal(peace.RandomState, war.RandomState);                                      // …yet stream 0 is untouched
        // The human starts with 0 gold, so the capture's plunder is a no-op here (a broke victim yields nothing);
        // the gold-bearing plunder case (and its stream-0 safety) is covered by ColonyPlunderTests.
        Assert.Equal(peace.HumanPlayer.Gold, war.HumanPlayer.Gold);
    }
}
