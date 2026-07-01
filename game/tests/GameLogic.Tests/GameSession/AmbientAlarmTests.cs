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
/// Ambient native alarm (FreeCol <c>ServerPlayer.csNewTurn</c>): each turn a settlement resents the human's
/// nearby footprint — every human colony within <c>radius + 2</c> tiles adds <c>2 + population</c> and every human
/// offensive land unit adds its type offence; the gain is damped by Pocahontas's −50% <c>nativeAlarmModifier</c>
/// (its faithful home), then the usual decay runs. RNG-free. With one artillery (offence 7) beside a calm
/// settlement: ambient +7, decay −4 → 3; under Pocahontas the gain halves to 3, decay −4 → 0.
/// </summary>
public class AmbientAlarmTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Artillery = "model.unit.artillery"; // offence 7
    private const string Pocahontas = "model.foundingFather.pocahontas";

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    private static Game GameWithPocahontas()
    {
        Game game = Game.New(Classic, Seed);
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { Congress = new[] { Pocahontas } } : p).ToList(),
        }).Restore(Classic);
    }

    /// <summary>A calm settlement (alarm zeroed) with a free adjacent land tile.</summary>
    private static NativeSettlement CalmSettlement(Game game)
    {
        NativeSettlement s = game.NativeSettlements.First(x => x.Position.Neighbours().Any(n => FreeLand(game, n)));
        game.ChangeNativeAlarm(s, -s.Alarm); // start at 0 (Happy)
        return s;
    }

    [Fact]
    public void AmbientAlarm_RisesWhenAHumanWarMachineIsNearby()
    {
        // Baseline: no human in range → the calm settlement stays calm through the turn.
        Game baseline = Game.New(Classic, Seed);
        NativeSettlement calm = CalmSettlement(baseline);
        int calmId = calm.Id;
        baseline.EndTurn();
        Assert.Equal(0, baseline.NativeSettlements.First(s => s.Id == calmId).Alarm);

        // Same settlement, now with an artillery beside it: its alarm climbs (ambient +7, decay −4 → 3).
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = CalmSettlement(game);
        Position adj = s.Position.Neighbours().First(n => FreeLand(game, n));
        game.SpawnUnit(Classic.Unit(Artillery), adj);
        int sid = s.Id;
        game.EndTurn();
        Assert.Equal(3, game.NativeSettlements.First(x => x.Id == sid).Alarm);
    }

    [Fact]
    public void Pocahontas_HalvesTheAmbientAlarmGain()
    {
        // Same artillery-beside-a-settlement setup; Pocahontas damps the +7 gain to +3, which the −4 decay erases.
        Game game = GameWithPocahontas();
        NativeSettlement s = CalmSettlement(game);
        Position adj = s.Position.Neighbours().First(n => FreeLand(game, n));
        game.SpawnUnit(Classic.Unit(Artillery), adj);
        int sid = s.Id;

        game.EndTurn();

        Assert.Equal(0, game.NativeSettlements.First(x => x.Id == sid).Alarm); // halved gain (3) < decay (4) → 0
    }

    [Fact]
    public void FrenchNationType_HalvesTheAmbientAlarmGain_LikePocahontas()
    {
        // The French (model.nationType.cooperation) carry the same nativeAlarmModifier −50% as Pocahontas, as a
        // national trait rather than a father: a French human's +7 ambient gain halves to +3, which the −4 decay erases.
        string french = Classic.EuropeanNations.First(n =>
            n.NationType.Modifiers.Any(m => m.TargetId == "model.modifier.nativeAlarmModifier")).Id;
        Game game = GameWithHumanNation(french);
        NativeSettlement s = CalmSettlement(game);
        Position adj = s.Position.Neighbours().First(n => FreeLand(game, n));
        game.SpawnUnit(Classic.Unit(Artillery), adj);
        int sid = s.Id;

        game.EndTurn();

        Assert.Equal(0, game.NativeSettlements.First(x => x.Id == sid).Alarm); // halved gain (3) < decay (4) → 0
    }

    /// <summary>A fresh game with the human assigned <paramref name="nationId"/>, via the save/restore path — so its nation-type advantages apply.</summary>
    private static Game GameWithHumanNation(string nationId)
    {
        SaveGame save = SaveGame.From(Game.New(Classic, Seed));
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { NationId = nationId } : p).ToList(),
        }).Restore(Classic);
    }

    [Fact]
    public void AmbientAlarm_IsReplayStable()
    {
        // The ambient pass is RNG-free, so two identical setups stay byte-identical across several turns.
        static Game Staged()
        {
            Game g = Game.New(Classic, Seed);
            NativeSettlement s = CalmSettlement(g);
            g.SpawnUnit(Classic.Unit(Artillery), s.Position.Neighbours().First(n => FreeLand(g, n)));
            return g;
        }
        Game a = Staged();
        Game b = Staged();
        for (int turn = 0; turn < 5; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    // ---- Per-turn missionary calming (FreeCol ServerPlayer.java:1896-1906; MISSION_INFLUENCE −10, ×2 for an expert) ----

    /// <summary>The human's alarm at a chosen settlement after one EndTurn, given it starts at <paramref name="startAlarm"/> and optionally holds a mission.</summary>
    private static int AlarmAfterTurn(int startAlarm, bool mission, bool expertMission)
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement s = game.NativeSettlements.First();
        int sid = s.Id;
        game.ChangeNativeAlarm(s, startAlarm - s.Alarm); // pin to startAlarm
        if (mission)
        {
            s.MissionOwnerId = game.HumanPlayer.PlayerId; // the human's resident mission
            s.MissionIsExpert = expertMission;
        }
        game.EndTurn();
        return game.NativeSettlements.First(x => x.Id == sid).Alarm;
    }

    [Fact]
    public void MissionedSettlement_AlarmFallsByTheRelief_EachTurn()
    {
        // The only difference between the two runs is the resident human mission, so the gap is exactly the −10 relief.
        // Start at 450 so the per-turn decay's proportional term (value/100 = 4) is identical for both the missioned
        // (440) and un-missioned (450) state — isolating the missionary calming from the decay's own −value/100−4.
        const int start = 450;
        int withMission = AlarmAfterTurn(start, mission: true, expertMission: false);
        int withoutMission = AlarmAfterTurn(start, mission: false, expertMission: false);
        Assert.Equal(10, withoutMission - withMission); // MISSION_INFLUENCE = −10
    }

    [Fact]
    public void AnExpertMission_DoublesTheRelief()
    {
        // 450 ordinary→440, 450 expert→430 — both share the decay's value/100 = 4, so the gap is the expert's extra −10.
        const int start = 450;
        int expert = AlarmAfterTurn(start, mission: true, expertMission: true);
        int ordinary = AlarmAfterTurn(start, mission: true, expertMission: false);
        Assert.Equal(10, ordinary - expert); // the expert's extra −10 over the ordinary mission's −10 (−20 vs −10)
    }

    [Fact]
    public void MissionaryRelief_ClampsAtZero()
    {
        // A nearly-calm missioned settlement can't go negative — ChangeNativeAlarm clamps at 0 (and the decay floors there too).
        int alarm = AlarmAfterTurn(startAlarm: 5, mission: true, expertMission: true);
        Assert.Equal(0, alarm);
    }

    [Fact]
    public void MissionRelief_IsHalvedUnderPocahontas()
    {
        // FreeCol folds the mission relief into the SAME per-settlement accumulator as the ambient pressure, then scales
        // the NET by NATIVE_ALARM_MODIFIER once — so Pocahontas's −50% damps the relief too, not just gains. With no
        // ambient pressure (net = relief only) an ordinary mission's −10 halves to −5 and an expert's −20 halves to −10.
        // We measure the relief as (no-mission alarm) − (mission alarm) after one turn: the shared start and decay cancel,
        // leaving exactly the scaled relief. Pinned high (450) so the decay's value/100 term is identical across runs.
        const int start = 450;
        int ordinary = ReliefUnderPocahontas(start, expertMission: false);
        int expert = ReliefUnderPocahontas(start, expertMission: true);
        Assert.Equal(5, ordinary);  // −10 relief damped −50% → −5
        Assert.Equal(10, expert);   // −20 expert relief damped −50% → −10
    }

    /// <summary>
    /// The per-turn relief (as a positive magnitude) a resident human mission grants under Pocahontas, isolated by
    /// differencing a missioned run against a bare run at the same start. Every human on-map unit is disbanded first so
    /// the ambient proximity pressure is <b>0</b> at every settlement — the net that folds/scales is then the relief
    /// alone, so the gap is exactly the Pocahontas-scaled relief (−5 ordinary / −10 expert).
    /// </summary>
    private static int ReliefUnderPocahontas(int startAlarm, bool expertMission)
    {
        static int Run(int startAlarm, bool mission, bool expertMission)
        {
            Game game = GameWithPocahontas();
            // Zero the human's footprint so no settlement gains ambient proximity pressure — isolating the mission relief.
            foreach (Unit u in game.Units.Where(u => u.IsOnMap && u.OwnerId == game.HumanPlayer.PlayerId).ToList())
            {
                game.Disband(u);
            }
            NativeSettlement s = game.NativeSettlements.First();
            int sid = s.Id;
            game.ChangeNativeAlarm(s, startAlarm - s.Alarm);
            if (mission)
            {
                s.MissionOwnerId = game.HumanPlayer.PlayerId;
                s.MissionIsExpert = expertMission;
            }
            game.EndTurn();
            return game.NativeSettlements.First(x => x.Id == sid).Alarm;
        }
        int withMission = Run(startAlarm, mission: true, expertMission);
        int withoutMission = Run(startAlarm, mission: false, expertMission: false);
        return withoutMission - withMission; // shared start + decay cancel; the gap is the scaled relief
    }

    [Fact]
    public void MissionaryRelief_IsReplayStable()
    {
        // RNG-free: two identical missioned setups stay byte-identical across several turns.
        static Game Staged()
        {
            Game g = Game.New(Classic, Seed);
            NativeSettlement s = g.NativeSettlements.First();
            g.ChangeNativeAlarm(s, 400 - s.Alarm);
            s.MissionOwnerId = g.HumanPlayer.PlayerId;
            s.MissionIsExpert = true;
            return g;
        }
        Game a = Staged();
        Game b = Staged();
        for (int turn = 0; turn < 5; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }
}
