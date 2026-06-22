using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Per-player native tension (86d3e4bfb, save v55): a settlement tracks alarm toward each colonial power independently
/// (FreeCol <c>IndianSettlement.getAlarm(Player)</c> / its <c>Map&lt;Player, Tension&gt;</c>) and derives a
/// <see cref="NativeSettlement.MostHated"/> (FreeCol <c>getMostHated</c>). L1 unit coverage of the new API + the
/// save round-trip/migration; L2 of two powers provoking the same settlement independently.
/// </summary>
public class NativeAlarmChannelsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    private static int HumanId(Game g) => g.HumanPlayer.PlayerId;

    private static Player ForeignPower(Game g) =>
        g.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    // ── L1: per-player independence ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AlarmFor_DefaultsToZero_AndIsIndependentPerPlayer()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];
        Player power = ForeignPower(game);

        // Provoke the foreign power's channel only.
        game.ChangeNativeAlarm(s, power.PlayerId, 650);

        Assert.Equal(650, s.AlarmFor(power.PlayerId)); // the power's channel rose
        Assert.Equal(0, s.AlarmFor(HumanId(game)));     // the human's channel is untouched (ADR-009)
        Assert.Equal(0, s.Alarm);                       // .Alarm == human channel (legacy accessor)
        Assert.Equal(AlarmLevel.Displeased, s.AlarmLevelFor(power.PlayerId, NativeTensionOptions.ClassicMedium));
        Assert.Equal(AlarmLevel.Happy, s.AlarmLevelFor(HumanId(game), NativeTensionOptions.ClassicMedium));
    }

    [Fact]
    public void ChangeNativeAlarm_ClampsEachChannelIndependently()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];
        Player power = ForeignPower(game);

        game.ChangeNativeAlarm(s, HumanId(game), 5000);          // over the ceiling → clamps
        game.ChangeNativeAlarm(s, power.PlayerId, -50);          // below 0 → clamps to 0 (drops the channel)

        Assert.Equal(NativeTensionOptions.ClassicMedium.MaxAlarm, s.Alarm);
        Assert.Equal(0, s.AlarmFor(power.PlayerId));
    }

    // ── L1: most-hated (FreeCol getMostHated) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MostHated_IsNull_WhenEveryChannelIsHappy()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];

        // A Happy-band channel is never "hated" (FreeCol skips HAPPY).
        game.ChangeNativeAlarm(s, HumanId(game), 50); // still Happy (≤100)
        Assert.Null(s.MostHated);
    }

    [Fact]
    public void MostHated_PicksTheAngriestNonHappyChannel()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];
        Player power = ForeignPower(game);

        game.ChangeNativeAlarm(s, HumanId(game), 300);     // Content (hated)
        game.ChangeNativeAlarm(s, power.PlayerId, 650);    // Displeased (angrier)
        Assert.Equal(power.PlayerId, s.MostHated);

        // Flip it: make the human angrier than the power.
        game.ChangeNativeAlarm(s, HumanId(game), 400);     // now 700 > 650
        Assert.Equal(HumanId(game), s.MostHated);
    }

    [Fact]
    public void MostHated_DropsAChannelThatDecaysBackToHappy()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];
        Player power = ForeignPower(game);

        game.ChangeNativeAlarm(s, power.PlayerId, 150); // Content (hated)
        Assert.Equal(power.PlayerId, s.MostHated);

        game.ChangeNativeAlarm(s, power.PlayerId, -150); // back to 0 → Happy → no longer hated
        Assert.Null(s.MostHated);
    }

    // ── L1: a one-power (human-only) game matches the old single-scalar behaviour exactly ─────────────────────────

    [Fact]
    public void OnePowerGame_BehavesExactlyLikeTheOldSingleScalar()
    {
        // With only channel 0 (the human) ever touched, the legacy .Alarm / .AlarmLevel accessors and the per-turn
        // decay behave identically to the pre-v55 single scalar: the human-only game is byte-identical.
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];

        game.ChangeNativeAlarm(s, 250); // the legacy 2-arg overload → human channel
        Assert.Equal(250, s.Alarm);
        Assert.Equal(250, s.AlarmFor(HumanId(game)));
        Assert.Equal(AlarmLevel.Content, s.AlarmLevel);

        // Only channel 0 exists — the per-player map has a single entry.
        Assert.Equal(new[] { System.Collections.Generic.KeyValuePair.Create(HumanId(game), 250) },
            s.AlarmChannels.ToArray());

        // A whole human-only game with native alarm set still serialises with only channel-0 data and round-trips.
        string json = SaveGame.From(game).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    // ── L1: persistence — v55 round-trip, omit-when-default, v54→v55 migration ───────────────────────────────────

    [Fact]
    public void PerPlayerAlarm_SurvivesASaveRoundTrip_V55()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements[0];
        Player power = ForeignPower(game);
        game.ChangeNativeAlarm(s, HumanId(game), 320);
        game.ChangeNativeAlarm(s, power.PlayerId, 640);

        string json = SaveGame.From(game).ToJson();
        Game restored = SaveGame.FromJson(json).Restore(Classic);
        NativeSettlement after = restored.NativeSettlements.Single(x => x.Id == s.Id);

        Assert.Equal(320, after.AlarmFor(HumanId(game)));
        Assert.Equal(640, after.AlarmFor(power.PlayerId));
        Assert.Equal(json, SaveGame.From(restored).ToJson()); // byte-identical
    }

    [Fact]
    public void AnAllPeaceSettlement_OmitsTheAlarmChannelsField_ByteIdenticalToV54()
    {
        // The additive map is omit-when-default (ADR-009): a settlement at peace with everyone emits NO AlarmChannels
        // field, so a fully-calm save carries nothing for it. A fresh game's settlements are all at peace.
        Game game = Game.New(Classic, Seed);
        Assert.All(game.NativeSettlements, s => Assert.Empty(s.AlarmChannels));

        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("AlarmChannels", json);
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    [Fact]
    public void PreV55Save_MigratesTheSingleScalarIntoTheHumanChannel()
    {
        // A pre-v55 save carried a single Alarm scalar (effectively the human's). Rebuild a v54-shaped save with that
        // legacy field set and no AlarmChannels, then assert it loads into the human's channel (player id 0).
        Game game = Game.New(Classic, Seed);
        SaveGame current = SaveGame.From(game);
        int targetId = game.NativeSettlements[0].Id;

        var legacy = current with
        {
            Version = 54,
            NativeSettlements = current.NativeSettlements!
                .Select(ns => ns.Id == targetId
                    ? ns with { Alarm = 480, AlarmChannels = null } // the old single scalar; no per-player map yet
                    : ns)
                .ToList(),
        };

        Game loaded = SaveGame.FromJson(legacy.ToJson()).Restore(Classic);
        NativeSettlement s = loaded.NativeSettlements.Single(x => x.Id == targetId);

        Assert.Equal(480, s.AlarmFor(HumanId(game))); // migrated into channel 0
        Assert.Equal(480, s.Alarm);                    // == the human channel
        Assert.Equal(AlarmLevel.Content, s.AlarmLevel);
    }

    // ── L2: two powers provoke the same settlement independently ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Scenario")]
    public void TwoPowers_ProvokeOneSettlement_Independently()
    {
        Game game = Game.New(Classic, Seed);
        Player power = ForeignPower(game);
        NativeSettlement s = game.NativeSettlements[0];

        // The human angers the tribe into the Displeased band; the foreign power stays Happy.
        game.ChangeNativeAlarm(s, HumanId(game), 650);  // Displeased toward the human (601–700)
        game.ChangeNativeAlarm(s, power.PlayerId, 80);  // still Happy toward the power (≤100)

        // The tribe is hostile to the human but happy with the power — and most-hates the human.
        Assert.Equal(AlarmLevel.Displeased, s.AlarmLevelFor(HumanId(game), NativeTensionOptions.ClassicMedium));
        Assert.Equal(AlarmLevel.Happy, s.AlarmLevelFor(power.PlayerId, NativeTensionOptions.ClassicMedium));
        Assert.Equal(HumanId(game), s.MostHated);

        // Now the power outrages the tribe past the human; the human's standing is unchanged by the power's act.
        int humanBefore = s.AlarmFor(HumanId(game));
        game.ChangeNativeAlarm(s, power.PlayerId, 900); // → far angrier than the human (clamps under the 1000 ceiling)
        Assert.Equal(humanBefore, s.AlarmFor(HumanId(game)));       // the human's channel never moved (ADR-009)
        Assert.Equal(power.PlayerId, s.MostHated);                  // most-hated swung to the power

        // A whole-game round-trip preserves both channels exactly.
        string json = SaveGame.From(game).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    [Fact]
    [Trait("Category", "Scenario")]
    public void PerTurnDecay_CoolsEveryChannelIndependently()
    {
        // FreeCol's tension decay runs per player. Two channels at different alarms each cool by −(alarm/100 + 4) per
        // turn, independently, and a channel that reaches 0 is dropped (so most-hated updates).
        Game game = Game.New(Classic, Seed);
        Player power = ForeignPower(game);
        NativeSettlement s = game.NativeSettlements[0];

        // Clear every colonial on-map unit so no ambient proximity pressure perturbs a channel before the decay — the
        // tick is then PURE decay, and the exact-equality assertions below are stable.
        foreach (Unit u in game.Units.Where(u => u.IsOnMap && !u.IsNative).ToList())
        {
            game.Disband(u);
        }
        game.ChangeNativeAlarm(s, HumanId(game), 500);
        game.ChangeNativeAlarm(s, power.PlayerId, 200);

        game.EndTurn(); // one decay tick (classic: −value/100 − 4)

        NativeSettlement after = game.NativeSettlements.Single(x => x.Id == s.Id);
        // Each channel cooled by its own amount; the human (higher) loses more than the power.
        Assert.Equal(500 - (500 / 100 + 4), after.AlarmFor(HumanId(game)));   // 500 → 491
        Assert.Equal(200 - (200 / 100 + 4), after.AlarmFor(power.PlayerId));  // 200 → 194
    }
}
