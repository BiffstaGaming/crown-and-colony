using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native-facing Founding-Father effects. Pocahontas (FreeCol <c>model.foundingFather.pocahontas</c>): on
/// election her <c>resetNativeAlarm</c> event zeroes all native alarm toward the human (the Happy band), and her
/// <c>nativeAlarmModifier</c> (−50%) permanently damps future combat alarm gains (the −50% itself is pinned in
/// <see cref="CombatTests"/>). Both ride on the persisted Congress — no RNG, no save-format change. Franklin is
/// deferred: he is a European-diplomacy father (ignore-European-wars / always-offered-peace / peace-treaty), with
/// no native effect, and faithful delivery needs the monarch/REF-war + inter-European-stance + AI-trade systems.
/// </summary>
public class NativeFatherEffectsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Pocahontas = "model.foundingFather.pocahontas";
    private const string Jefferson = "model.foundingFather.thomasJefferson";

    /// <summary>A copy of the game with the human poised to elect <paramref name="father"/> next turn (chosen + ample liberty).</summary>
    private static Game ElectNextTurn(Game game, string father)
    {
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!
                .Select(p => p.IsHuman ? p with { CurrentFather = father, Liberty = 100_000 } : p)
                .ToList(),
        }).Restore(Classic);
    }

    [Fact]
    public void Pocahontas_OnElection_ResetsAllNativeSettlementAlarmToHappy()
    {
        Game game = ElectNextTurn(Game.New(Classic, Seed), Pocahontas);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, 900); // Hateful
        }
        Assert.Contains(game.NativeSettlements, s => s.AlarmLevel != AlarmLevel.Happy);

        game.EndTurn(); // the human elects Pocahontas this turn → resetNativeAlarm fires

        Assert.Contains(Pocahontas, game.Congress);
        Assert.All(game.NativeSettlements, s =>
        {
            Assert.Equal(0, s.Alarm);
            Assert.Equal(AlarmLevel.Happy, s.AlarmLevel);
        });
    }

    [Fact]
    public void ElectingAnotherFather_DoesNotResetNativeAlarm()
    {
        // Control: only Pocahontas's election resets alarm (the fixture isn't the cause).
        Game game = ElectNextTurn(Game.New(Classic, Seed), Jefferson);
        NativeSettlement target = game.NativeSettlements.First();
        game.ChangeNativeAlarm(target, 900);

        game.EndTurn(); // elects Jefferson, not Pocahontas

        Assert.Contains(Jefferson, game.Congress);
        Assert.True(target.Alarm > 0, "a non-Pocahontas election must not reset native alarm"); // only per-turn decay
    }

    [Fact]
    public void Pocahontas_AlarmReset_SurvivesASaveRoundTrip()
    {
        Game game = ElectNextTurn(Game.New(Classic, Seed), Pocahontas);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, 900);
        }
        game.EndTurn(); // reset fires

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.All(loaded.NativeSettlements, s => Assert.Equal(0, s.Alarm)); // the zeroed state persists (no replay/undo)
    }

    [Fact]
    public void ForeignPowerElectingPocahontas_DoesNotResetNativeAlarm()
    {
        // The reset is the HUMAN's (native alarm is toward the human) — a foreign power electing Pocahontas is a
        // no-op (the reset is gated on player.IsHuman). Inject her as a foreign power's chosen father + liberty.
        SaveGame save = SaveGame.From(Game.New(Classic, Seed));
        int foreignId = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial).PlayerId;
        Game game = (save with
        {
            Players = save.Players!
                .Select(p => p.PlayerId == foreignId ? p with { CurrentFather = Pocahontas, Liberty = 100_000 } : p)
                .ToList(),
        }).Restore(Classic);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, 900);
        }

        game.EndTurn(); // the foreign power elects Pocahontas — but IsHuman is false, so the reset must NOT fire

        Assert.Contains(Pocahontas, game.Players.First(p => p.PlayerId == foreignId).Congress);
        Assert.All(game.NativeSettlements, s => Assert.True(s.Alarm > 0, "a foreign power's Pocahontas must not reset native alarm"));
    }

    [Fact]
    public void Pocahontas_Election_IsReplayStable_AndAlwaysResets()
    {
        // Two same-seed games electing Pocahontas stay byte-identical (the reset is a pure RNG-free loop), and
        // both actually reset — so this would fail if the reset were dropped (not just same-seed determinism).
        Game a = ElectNextTurn(Game.New(Classic, Seed), Pocahontas);
        Game b = ElectNextTurn(Game.New(Classic, Seed), Pocahontas);
        foreach (NativeSettlement s in a.NativeSettlements)
        {
            a.ChangeNativeAlarm(s, 900);
        }
        foreach (NativeSettlement s in b.NativeSettlements)
        {
            b.ChangeNativeAlarm(s, 900);
        }
        a.EndTurn();
        b.EndTurn();

        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        Assert.All(a.NativeSettlements, s => Assert.Equal(0, s.Alarm)); // the reset fired (not a vacuous same-seed check)
    }
}
