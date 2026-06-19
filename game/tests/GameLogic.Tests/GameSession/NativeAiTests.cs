using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The native AI (slice 1b): braves wander when calm and raid the human when their home settlement is alarmed
/// enough (Displeased+). The decisive invariant is byte-stability (ADR-009): every native choice — wander,
/// pathing, combat — draws only from the nation's own RNG stream, never the human's stream 0, so the human's
/// seeded game is unaffected by however much the natives do. Combat uses the native stream via the internal
/// <c>Attack</c> overload; outcome odds themselves are covered in <see cref="CombatTests"/>.
/// </summary>
public class NativeAiTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";

    private static bool Free(Game g, Position n) =>
        g.Map.InBounds(n) && !g.Map.TerrainAt(n).IsWater
        && g.NativeSettlementAt(n) is null && g.ColonyAt(n) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == n);

    /// <summary>Cranks every native settlement to Hateful so its braves go raiding this turn (alarm decays slowly, so one crank lasts the test).</summary>
    private static void EnrageAllNatives(Game game)
    {
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // clamped to Hateful (1000)
        }
    }

    [Fact]
    public void AFriendlyTribe_BringsAGiftToAnAdjacentHumanColony()
    {
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.Type.CanFoundColony));
        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => Free(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), adj, nation);

        bool gifted = false;
        for (int turn = 0; turn < 60 && !gifted; turn++)
        {
            foreach (NativeSettlement s in game.NativeSettlements) // keep the tribe Happy (cancel any ambient alarm)
            {
                game.ChangeNativeAlarm(s, -s.Alarm);
            }
            brave.Position = adj; // park it beside the colony (it would otherwise wander off between gift rolls)
            game.EndTurn();
            gifted = game.ColonyGiftNotices.Any(g => g.ColonyName == colony.Name);
        }

        Assert.True(gifted, "a Happy tribe's brave beside a human colony should eventually bring a gift");
        ColonyGiftNotice gift = game.ColonyGiftNotices.First(g => g.ColonyName == colony.Name);
        Assert.Equal(nation, gift.GiverNationId);
        Assert.Equal("model.goods.tobacco", gift.GoodsId);
        Assert.Equal(25, gift.Amount);
        Assert.True(colony.StoreOf("model.goods.tobacco") >= 25); // the parcel reached the warehouse
    }

    [Fact]
    public void SameSeed_WithProvokedNatives_PlayOutByteIdentically()
    {
        // Determinism: two same-seed games, both with their natives enraged into raiding, must be byte-identical
        // after many turns — every native draw is from a deterministic per-nation stream.
        Game a = Game.New(Classic, seed: 12345);
        Game b = Game.New(Classic, seed: 12345);
        EnrageAllNatives(a);
        EnrageAllNatives(b);

        for (int turn = 0; turn < 15; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }

        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    [Fact]
    public void NativeRaids_DoNotTouchTheHumansStream0()
    {
        // The decisive guard (the native counterpart of HumanStream0_IsUnaffectedByHowMuchTheRivalsDo): however
        // much the natives roam and raid, the human's RNG stream 0 and its own scoped state stay byte-identical.
        Game calm = Game.New(Classic, seed: 999);
        Game raiding = Game.New(Classic, seed: 999);
        EnrageAllNatives(raiding); // only this game's natives hunt; the other's merely wander

        for (int turn = 0; turn < 15; turn++)
        {
            calm.EndTurn();
            raiding.EndTurn();
        }

        // The natives genuinely diverged (seek-and-destroy pathing vs. idle wandering moves braves differently)…
        Assert.NotEqual(SaveGame.From(calm).ToJson(), SaveGame.From(raiding).ToJson());
        // …yet the human's stream 0 and its own player state are untouched — no native path drew from stream 0.
        Assert.Equal(calm.RandomState, raiding.RandomState);
        Assert.Equal(calm.HumanPlayer.Gold, raiding.HumanPlayer.Gold);
        Assert.Equal(calm.HumanPlayer.Immigration, raiding.HumanPlayer.Immigration);
        Assert.Equal(calm.HumanPlayer.RecruitDock, raiding.HumanPlayer.RecruitDock);
    }

    [Fact]
    public void AnAlarmedBrave_RaidsAnAdjacentHumanUnit_AndRecordsANotice()
    {
        Game game = Game.New(Classic, seed: 7);
        Unit brave = game.NativeUnits.First(b => b.Position.Neighbours().Any(n => Free(game, n)));
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // its nation is now Hateful → it raids
        }
        Position spot = brave.Position.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(Classic.Unit(FreeColonist), spot); // a human colonist (OwnerId 0) right beside the brave

        game.EndTurn();

        // The brave raided the human: a notice was recorded, attributed to its nation, at the prey's tile.
        Assert.Contains(game.CombatNotices, n => n.AttackerNationId == brave.OwnerNationId && n.Position == spot);
    }

    [Fact]
    public void CalmNatives_RecordNoRaidNotices()
    {
        // Happy/Content natives only wander — they never attack, so no raid notice is produced.
        Game game = Game.New(Classic, seed: 7);
        for (int turn = 0; turn < 10; turn++)
        {
            game.EndTurn();
            Assert.Empty(game.CombatNotices);
        }
    }

    [Fact]
    public void NativeAi_KeepsBravesOnLegalLand_NeverStacksOnTheHuman_AndNeverMultiplies()
    {
        Game game = Game.New(Classic, seed: 4242);
        int braveCountStart = game.NativeUnits.Count();
        EnrageAllNatives(game); // exercise both the seek and (when blocked) the path/wander branches hard

        for (int turn = 0; turn < 30; turn++)
        {
            game.EndTurn();
            foreach (Unit brave in game.NativeUnits)
            {
                Assert.True(game.Map.InBounds(brave.Position));
                Assert.False(game.Map.TerrainAt(brave.Position).IsWater); // braves never wander into the sea
                Assert.DoesNotContain(game.PlayerUnits,
                    h => h.IsOnMap && h.Position == brave.Position);       // never stacked onto a human unit
            }
            Assert.True(game.NativeUnits.Count() <= braveCountStart);      // no runaway: braves only die, never multiply
        }
    }

    [Fact]
    public void EnragedBraves_WithNoHumanUnitToHunt_AttackNothing()
    {
        // Braves attack the HUMAN only (NearestHumanUnit is the sole target contract — the engine's AreEnemies
        // would also admit foreign powers and rival tribes). Remove the human from the map (found a colony with
        // its only unit) so no on-map human unit exists: enraged braves then attack nothing at all — no raid
        // notice ever, and no brave is ever lost in combat (they never fight foreign powers or each other).
        Game game = Game.New(Classic, seed: 31);
        Unit settler = game.PlayerUnits.First(u => u.IsOnMap);
        Assert.True(game.CheckFoundColony(settler).Allowed);
        game.FoundColony(settler);
        Assert.DoesNotContain(game.PlayerUnits, u => u.IsOnMap); // no on-map human unit for braves to hunt

        int nativeCountStart = game.NativeUnits.Count();
        for (int turn = 0; turn < 20; turn++)
        {
            EnrageAllNatives(game); // keep every tribe at maximum alarm the whole run
            game.EndTurn();
            Assert.Empty(game.CombatNotices);                            // no human on the map → never a raid
            Assert.Equal(nativeCountStart, game.NativeUnits.Count());    // braves attack nobody → none die in combat
        }
    }

    [Fact]
    public void NativeActivity_RoundTripsThroughSave_AndContinuesDeterministically()
    {
        // Natives that have moved/raided save and reload byte-identically (their RNG streams persist), and a
        // reloaded game continues to play out identically to one that was never saved.
        Game live = Game.New(Classic, seed: 555);
        EnrageAllNatives(live);
        for (int turn = 0; turn < 8; turn++)
        {
            live.EndTurn();
        }

        Game reloaded = SaveGame.FromJson(SaveGame.From(live).ToJson()).Restore(Classic);
        Assert.Equal(SaveGame.From(live).ToJson(), SaveGame.From(reloaded).ToJson());

        for (int turn = 0; turn < 5; turn++)
        {
            live.EndTurn();
            reloaded.EndTurn();
        }
        Assert.Equal(SaveGame.From(live).ToJson(), SaveGame.From(reloaded).ToJson());
    }
}
