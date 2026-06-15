using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Foreign-power retaliation combat (slice 1c-2): a foreign power at war with the human (war only starts when
/// the human attacks it) sends its armed units after the human's nearest unit, attacking when adjacent. Like
/// the native AI, every combat draw is from the power's OWN RNG stream (ADR-009) — so the human's stream 0
/// stays byte-stable however much the rivals fight. War is the gate; at peace the foreign AI expands as before.
/// </summary>
public class ForeignCombatTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";

    private static bool Free(Game g, Position n) =>
        g.Map.InBounds(n) && !g.Map.TerrainAt(n).IsWater
        && g.NativeSettlementAt(n) is null && g.ColonyAt(n) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == n);

    /// <summary>A foreign power with an armed soldier on a free tile beside the human's first on-map unit; optionally already at war.</summary>
    private static (Game game, Player power, Unit prey) Stage(ulong seed, bool atWar)
    {
        Game game = Game.New(Classic, seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Unit prey = game.PlayerUnits.First(u => u.IsOnMap);
        Position spot = prey.Position.Neighbours().First(n => Free(game, n));

        Unit soldier = game.SpawnUnit(Classic.Unit(VeteranSoldier), spot);
        soldier.OwnerId = power.PlayerId; // reassign from the human (SpawnUnit only owners natives directly)
        soldier.RoleId = SoldierRole;     // offence 0 (type) + 2 (role) > 0 → armed
        soldier.RoleCount = 1;
        if (atWar)
        {
            game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        }
        return (game, power, prey);
    }

    [Fact]
    public void AtWar_RetaliatesAgainstAnAdjacentHumanUnit_AndRecordsANotice()
    {
        (Game game, Player power, Unit prey) = Stage(seed: 7, atWar: true);
        Position preyPos = prey.Position;

        game.EndTurn();

        Assert.Contains(game.CombatNotices, n => n.AttackerNationId == power.NationId && n.Position == preyPos);
    }

    [Fact]
    public void WhenNotAtWar_DoesNotAttackTheHuman()
    {
        // The gate is `StanceBetween(power, human) == War`; the default (un-provoked) stance is Uncontacted, so
        // a foreign soldier sitting right next to the human expands instead of attacking.
        (Game game, Player power, _) = Stage(seed: 7, atWar: false);
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId));

        game.EndTurn();

        Assert.DoesNotContain(game.CombatNotices, n => n.AttackerNationId == power.NationId);
    }

    [Fact]
    public void AtWar_WithNoHumanUnitOnMap_AttacksNothing()
    {
        // The human-only target contract (NearestHumanUnit) is the sole guard keeping a foreign power from
        // attacking natives or other rivals. Empty the map of human units (found a colony with the start unit):
        // an at-war soldier then attacks nobody — no notice, and no native/rival is lost to combat.
        (Game game, _, Unit prey) = Stage(seed: 7, atWar: true);
        Assert.True(game.CheckFoundColony(prey).Allowed);
        game.FoundColony(prey); // consumes the human's only on-map unit
        Assert.DoesNotContain(game.PlayerUnits, u => u.IsOnMap);

        int nativeCountStart = game.NativeUnits.Count();
        game.EndTurn();

        Assert.Empty(game.CombatNotices);                         // no human on the map → the at-war soldier attacks nobody
        Assert.Equal(nativeCountStart, game.NativeUnits.Count()); // and kills no native/rival either
    }

    [Fact]
    public void ForeignRetaliation_IsReplayStable_ForAFixedSeed()
    {
        (Game a, _, _) = Stage(seed: 31337, atWar: true);
        (Game b, _, _) = Stage(seed: 31337, atWar: true);
        for (int turn = 0; turn < 12; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    [Fact]
    public void ForeignRetaliation_DoesNotTouchTheHumansStream0()
    {
        // Same seed + same staged soldier in both; only one is at war. The war game's soldier raids (its own
        // stream); the peace game's expands — but the human's stream 0 and scoped state stay byte-identical.
        (Game peace, _, _) = Stage(seed: 999, atWar: false);
        (Game war, _, _) = Stage(seed: 999, atWar: true);
        for (int turn = 0; turn < 12; turn++)
        {
            peace.EndTurn();
            war.EndTurn();
        }

        Assert.NotEqual(SaveGame.From(peace).ToJson(), SaveGame.From(war).ToJson()); // the war genuinely diverged…
        Assert.Equal(peace.RandomState, war.RandomState);                            // …yet stream 0 is untouched
        Assert.Equal(peace.HumanPlayer.Gold, war.HumanPlayer.Gold);
        Assert.Equal(peace.HumanPlayer.RecruitDock, war.HumanPlayer.RecruitDock);
    }
}
