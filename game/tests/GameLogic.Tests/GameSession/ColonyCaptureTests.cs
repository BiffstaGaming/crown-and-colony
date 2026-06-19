using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Colonial-colony assault + capture (slice 1c-3e): a colonial land unit assaults an ungarrisoned rival colony;
/// its last-resort defender is an unarmed colonist; a win hands the colony (people/buildings/stores) to the
/// attacker's owner, a loss disarms/demotes the attacker. Assaulting is an act of war. Plunder, the colony
/// defence bonus, and foreign-AI-initiated capture are later sub-slices. Outcomes forced via a fixed RNG.
/// </summary>
public class ColonyCaptureTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Artillery = "model.unit.artillery"; // offence 7

    /// <summary>A fixed RNG (same NextDouble each call) — forces a chosen combat band.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>The human founds a colony, it is handed to a rival, and a human artillery stands beside it ungarrisoned.</summary>
    private static (Game game, Colony colony, Unit attacker, int foreignId, int humanId) StageRivalColony()
    {
        Game game = Game.New(Classic, Seed);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder);
        colony.OwnerId = foreignId; // now a rival colony (the founding colonist became its population)

        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj); // human-owned (OwnerId 0), offence 7
        return (game, colony, attacker, foreignId, humanId);
    }

    [Fact]
    public void Assault_CapturesAnUngarrisonedRivalColony()
    {
        (Game game, Colony colony, Unit attacker, _, int humanId) = StageRivalColony();

        CombatResult result = game.AttackColony(attacker, colony.Position, new FixedRandom(0.0)); // forced win

        Assert.True(result is CombatResult.GreatWin or CombatResult.Win);
        Assert.Equal(humanId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // it is ours now
    }

    [Fact]
    public void Capture_DeclaresWarOnTheFormerOwner()
    {
        (Game game, Colony colony, Unit attacker, int foreignId, int humanId) = StageRivalColony();

        game.AttackColony(attacker, colony.Position, new FixedRandom(0.0));

        Assert.Equal(Stance.War, game.StanceBetween(humanId, foreignId));
    }

    [Fact]
    public void RepelledAssault_DoesNotCapture()
    {
        (Game game, Colony colony, Unit attacker, int foreignId, _) = StageRivalColony();

        CombatResult result = game.AttackColony(attacker, colony.Position, new FixedRandom(0.99)); // forced loss

        Assert.False(result is CombatResult.GreatWin or CombatResult.Win);
        Assert.Equal(foreignId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // still the rival's
    }

    [Fact]
    public void CheckAttackColony_RefusesAGarrisonedColony()
    {
        (Game game, Colony colony, Unit attacker, int foreignId, _) = StageRivalColony();
        Unit garrison = game.SpawnUnit(Classic.Unit(Artillery), colony.Position); // a defender stands on the tile
        garrison.OwnerId = foreignId;

        Assert.False(game.CheckAttackColony(attacker, colony.Position).Allowed); // must defeat the defender first
    }

    [Fact]
    public void CheckAttackColony_RefusesAColonyTheAttackerOwns()
    {
        (Game game, Colony colony, Unit attacker, _, int humanId) = StageRivalColony();

        // Hand the colony back to the human: you cannot assault your own colony.
        game.Colonies.First(c => c.Id == colony.Id).OwnerId = humanId;
        Assert.False(game.CheckAttackColony(attacker, colony.Position).Allowed);
    }

    [Fact]
    public void CapturedColony_SurvivesSaveRoundTrip()
    {
        (Game game, Colony colony, Unit attacker, _, int humanId) = StageRivalColony();
        game.AttackColony(attacker, colony.Position, new FixedRandom(0.0));

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(humanId, restored.Colonies.First(c => c.Id == colony.Id).OwnerId);
    }

    // ===== Ships caught in a falling colony (86d3c9twd) =====================================================

    /// <summary>
    /// Stages a coastal rival colony: a fresh free colonist founds a colony on a coastal land tile (so the colony has
    /// a water "port" neighbour), the colony is handed to a rival, and a human artillery stands beside it ungarrisoned.
    /// Returns a free port-water tile a ship can be moored on.
    /// </summary>
    private static (Game game, Colony colony, Unit attacker, int foreignId, Position port) StageCoastalRivalColony()
    {
        Game game = Game.New(Classic, Seed);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

        // A free land tile with both a water neighbour (a port) and a free-land neighbour (for the attacker).
        Position site = game.Map.AllPositions().First(p =>
            FreeLand(game, p)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater)
            && p.Neighbours().Any(n => FreeLand(game, n)));

        Unit founder = game.SpawnUnit(Classic.Unit(Colony.FreeColonistTypeId), site);
        Colony colony = game.FoundColony(founder);
        colony.OwnerId = foreignId;

        Position attackTile = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), attackTile);
        Position port = colony.Position.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        return (game, colony, attacker, foreignId, port);
    }

    private static Unit MoorRivalShip(Game game, Position water, int foreignId)
    {
        Unit ship = game.SpawnUnit(Classic.UnitTypes.First(u => u.IsNaval), water);
        ship.OwnerId = foreignId;
        return ship;
    }

    [Fact]
    public void CaughtShip_IsDamagedAndLimpsToRepair_WhenItsColonyIsCaptured()
    {
        (Game game, Colony colony, Unit attacker, int foreignId, Position port) = StageCoastalRivalColony();
        Unit ship = MoorRivalShip(game, port, foreignId);

        game.AttackColony(attacker, colony.Position, new FixedRandom(0.0)); // forced capture

        Unit survivor = game.Units.Single(u => u.Id == ship.Id);   // not sunk — it limps off
        Assert.True(survivor.RepairTurnsRemaining > 0);            // under forced repair
        Assert.Equal(UnitLocation.InEurope, survivor.Location);    // the rival has no drydock → repairs in Europe
        Assert.Equal(foreignId, survivor.OwnerId);                 // still the rival's ship, not the captor's
    }

    [Fact]
    public void CaughtShips_AreAllResolved_WhenTheColonyFalls()
    {
        (Game game, Colony colony, Unit attacker, int foreignId, Position port) = StageCoastalRivalColony();
        Unit a = MoorRivalShip(game, port, foreignId);
        Unit b = MoorRivalShip(game, port, foreignId);

        game.AttackColony(attacker, colony.Position, new FixedRandom(0.0));

        Assert.All(new[] { a.Id, b.Id }, id =>
            Assert.Equal(UnitLocation.InEurope, game.Units.Single(u => u.Id == id).Location));
        Assert.DoesNotContain(game.Units, u => u.IsOnMap && u.Position == port && u.Type.IsNaval);
    }

    [Fact]
    public void ShipAtSea_IsNotCaught_WhenAColonyFalls()
    {
        (Game game, Colony colony, Unit attacker, int foreignId, _) = StageCoastalRivalColony();
        Position farWater = game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).IsWater && !p.IsAdjacentTo(colony.Position));
        Unit offshore = MoorRivalShip(game, farWater, foreignId); // a rival ship elsewhere, not in this colony's port

        game.AttackColony(attacker, colony.Position, new FixedRandom(0.0));

        Unit same = game.Units.Single(u => u.Id == offshore.Id);
        Assert.True(same.IsOnMap && same.Position == farWater); // untouched
        Assert.Equal(0, same.RepairTurnsRemaining);
    }
}
