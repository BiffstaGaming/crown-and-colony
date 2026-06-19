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
}
