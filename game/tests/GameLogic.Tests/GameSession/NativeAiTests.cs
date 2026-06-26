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

    private static int Cheb(Position a, Position b) => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

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
        Unit settler = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Assert.True(game.CheckFoundColony(settler).Allowed);
        game.FoundColony(settler);
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u); // disband the rest of the starting roster so no on-map human unit remains
        }
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

    // ── Displeasure attack weighting (86d3c9vzp): a brave goes for the soft target, not just the nearest ──────────

    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";

    [Fact]
    public void AnAlarmedBrave_PrefersTheSoftTarget_OverAnArmedOneAtTheSameDistance()
    {
        Game game = Game.New(Classic, seed: 7);
        Unit brave = game.NativeUnits.First(b => b.Position.Neighbours().Count(n => Free(game, n)) >= 2);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // its nation is Hateful → it raids
        }

        // Two human targets adjacent to the brave (both Chebyshev distance 1): a defenceless colonist and an armed
        // veteran soldier. The value−distance heuristic should pick the colonist (a far easier kill at equal range).
        var spots = brave.Position.Neighbours().Where(n => Free(game, n)).Take(2).ToList();
        Position softTile = spots[0];
        Position hardTile = spots[1];
        game.SpawnUnit(Classic.Unit(FreeColonist), softTile);
        Unit soldier = game.SpawnUnit(Classic.Unit(VeteranSoldier), hardTile);
        soldier.RoleId = SoldierRole;   // armed → higher defence → lower raid score
        soldier.RoleCount = 1;

        game.EndTurn();

        Assert.Contains(game.CombatNotices, n => n.AttackerNationId == brave.OwnerNationId && n.Position == softTile);
        Assert.DoesNotContain(game.CombatNotices, n => n.Position == hardTile); // the dug-in soldier was passed over
    }

    // ── Equip braves from settlement stock (86d3c9vzp, equip facet — FreeCol NativeAIPlayer.equipBraves) ───────────

    private const string Muskets = "model.goods.muskets";
    private const string Horses = "model.goods.horses";
    private const string DefaultRole = "model.role.default";

    /// <summary>The home settlement (nearest same-nation) a brave equips from — the same rule RunNativeTurn uses.</summary>
    private static NativeSettlement HomeOf(Game game, Unit brave) =>
        game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId)
            .OrderBy(s => System.Math.Max(System.Math.Abs(s.Position.X - brave.Position.X),
                                          System.Math.Abs(s.Position.Y - brave.Position.Y)))
            .ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
            .First();

    /// <summary>
    /// Empties every settlement's generator-seeded military stock (save v54), so a test that needs a <b>controlled</b>
    /// starting stock can deposit exactly the muskets/horses it intends to exercise. (The generator now seeds each camp
    /// with arms — 86d3e49gq — which would otherwise pre-arm these unit tests' braves.)
    /// </summary>
    private static void ClearAllStock(Game game)
    {
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            s.AddStock(Muskets, -s.StockOf(Muskets));
            s.AddStock(Horses, -s.StockOf(Horses));
        }
    }

    [Fact]
    public void EveryGeneratedSettlement_IsSeededWithMilitaryStock_SoThreatenedCampsArmTheirBravesInARealGame()
    {
        // 86d3e49gq: the generator seeds real military stock, so the equip chain fires on live data (not just on stock
        // a test deposits). Every camp holds muskets; capitals / larger settlements also hold horses. With the nation
        // enraged and NO test-deposited stock, a homed brave arms itself purely from the seed during a normal EndTurn.
        Game game = Game.New(Classic, seed: 7);
        Assert.All(game.NativeSettlements, s => Assert.True(s.StockOf(Muskets) >= 25,
            "every generated settlement should be seeded with muskets to arm its brave"));
        Assert.Contains(game.NativeSettlements, s => s.StockOf(Horses) >= 25); // the better/larger camps also stock horses

        Unit brave = game.NativeUnits.First();
        EnrageNationOf(game, brave); // its nation is now Hateful → its camps secure themselves
        game.EndTurn();

        Assert.NotEqual(DefaultRole, brave.RoleId); // armed itself from the seeded stock alone — no test deposit needed
    }

    [Fact]
    public void AThreatenedSettlement_WithMuskets_ArmsItsBrave_FromItsOwnStock()
    {
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // start from a controlled empty stock (the generator now seeds arms — 86d3e49gq)
        Unit brave = game.NativeUnits.First();
        Assert.Equal(DefaultRole, brave.RoleId); // braves start unarmed

        NativeSettlement home = HomeOf(game, brave);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // the nation is Hateful → its camps secure themselves
        }
        home.AddStock(Muskets, 25); // exactly one armed-brave's worth in the camp store

        game.EndTurn();

        Assert.Equal("model.role.armedBrave", brave.RoleId); // the brave armed itself from the stock
        Assert.Equal(0, home.StockOf(Muskets));              // …drawn down from the settlement
    }

    [Fact]
    public void AThreatenedSettlement_WithoutMuskets_LeavesItsBraveUnarmed()
    {
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // the premise is an unstocked camp — empty the generator-seeded arms (86d3e49gq)
        Unit brave = game.NativeUnits.First();
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // alarmed, but the camp holds no muskets/horses
        }

        game.EndTurn();

        Assert.Equal(DefaultRole, brave.RoleId); // no stock → nothing to equip from → still the default role
    }

    [Fact]
    public void ACalmSettlement_DoesNotArmItsBrave_EvenWithMusketsInStock()
    {
        // The trigger is being threatened: a Happy/Content camp keeps its braves unarmed even when it holds muskets.
        Game game = Game.New(Classic, seed: 7);
        Unit brave = game.NativeUnits.First();
        HomeOf(game, brave).AddStock(Muskets, 100); // plenty of muskets, but the nation is at peace (alarm 0)

        game.EndTurn();

        Assert.Equal(DefaultRole, brave.RoleId); // a calm camp does not secure itself → no equip
    }

    [Fact]
    public void EquippingABrave_DrawsOnlyOnTheNativesStream_HumanStream0ByteStable()
    {
        // The decisive ADR-009 guard for the equip path: a game whose natives arm themselves (both muskets AND horses
        // available, so the choice draws from the native stream) keeps the human's stream 0 byte-identical to a game
        // whose natives can't equip — only the natives' own state diverges.
        Game equips = Game.New(Classic, seed: 999);
        Game bare = Game.New(Classic, seed: 999);
        foreach (Game g in new[] { equips, bare })
        {
            ClearAllStock(g); // start both from empty so "bare" genuinely can't equip (the generator seeds arms — 86d3e49gq)
            foreach (NativeSettlement s in g.NativeSettlements)
            {
                g.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // both enraged so the native paths run identically…
            }
        }
        foreach (NativeSettlement s in equips.NativeSettlements)
        {
            s.AddStock(Muskets, 50); // …but only this game's camps can arm (and mount) their braves
            s.AddStock(Horses, 50);
        }

        for (int turn = 0; turn < 10; turn++)
        {
            equips.EndTurn();
            bare.EndTurn();
        }

        // At least one brave actually armed itself (the path ran), proving the test exercises the equip…
        Assert.Contains(equips.NativeUnits, b => b.RoleId != DefaultRole);
        // …yet the equipping nation drew only on its own stream — the human's stream 0 and own state are untouched.
        Assert.Equal(bare.RandomState, equips.RandomState);
        Assert.Equal(bare.HumanPlayer.Gold, equips.HumanPlayer.Gold);
        Assert.Equal(bare.HumanPlayer.Immigration, equips.HumanPlayer.Immigration);
        Assert.Equal(bare.HumanPlayer.RecruitDock, equips.HumanPlayer.RecruitDock);
        // The native streams genuinely diverged (an armed brave fights/moves differently from an unarmed one).
        Assert.NotEqual(SaveGame.From(bare).ToJson(), SaveGame.From(equips).ToJson());
    }

    // ── Native dragoon promotion + strength-ordered securing (86d3c9vzp, FreeCol equipBraves) ──────────────────────

    private const string ArmedBrave = "model.role.armedBrave";
    private const string NativeDragoon = "model.role.nativeDragoon";

    /// <summary>Enrages every settlement of the brave's nation so its camps secure themselves this turn.</summary>
    private static void EnrageNationOf(Game game, Unit brave)
    {
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }
    }

    [Fact]
    public void AThreatenedCamp_WithBothMusketsAndHorses_PromotesItsBraveStraightToNativeDragoon()
    {
        // FreeCol favours the strongest affordable military role: an unarmed brave with both halves in stock becomes a
        // native dragoon outright (not a coin-flip between armed and mounted).
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled stock: deposit exactly both halves (the generator now seeds arms — 86d3e49gq)
        Unit brave = game.NativeUnits.First();
        Assert.Equal(DefaultRole, brave.RoleId);
        NativeSettlement home = HomeOf(game, brave);
        EnrageNationOf(game, brave);
        home.AddStock(Muskets, 25);
        home.AddStock(Horses, 25);

        game.EndTurn();

        Assert.Equal(NativeDragoon, brave.RoleId);   // armed + mounted in one step
        Assert.Equal(0, home.StockOf(Muskets));      // both halves drawn from the camp…
        Assert.Equal(0, home.StockOf(Horses));
    }

    [Fact]
    public void AThreatenedCamp_PromotesAnAlreadyArmedBraveToDragoon_ChargingOnlyTheMissingHorses()
    {
        // The headline new facet: a partially-equipped (armed) brave is promoted to full dragoon by adding only the
        // half it lacks — the camp is charged the horses, NOT a fresh set of muskets (FreeCol getGoodsDifference).
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled stock: only the missing half (the generator now seeds arms — 86d3e49gq)
        Unit brave = game.NativeUnits.First();
        brave.RoleId = ArmedBrave; // already armed (has its muskets)
        brave.RoleCount = 1;
        NativeSettlement home = HomeOf(game, brave);
        EnrageNationOf(game, brave);
        home.AddStock(Horses, 25); // only the missing half is in stock — no muskets at all

        game.EndTurn();

        Assert.Equal(NativeDragoon, brave.RoleId); // promoted using just the horses
        Assert.Equal(0, home.StockOf(Horses));     // the missing half was consumed…
        Assert.Equal(0, home.StockOf(Muskets));    // …and no muskets were charged (it kept the ones it had)
    }

    [Fact]
    public void AThreatenedCamp_WithScarceStock_SecuresTheStrongestBraveFirst()
    {
        // Strength-ordered securing (FreeCol getMilitaryStrengthComparator): with stock for only one upgrade, the
        // strongest-needed brave is equipped first. An already-armed brave (stronger) homed at the camp promotes to
        // dragoon, consuming the lone 25 horses; a weaker unarmed brave at the same camp is then left with nothing it
        // can equip from (no muskets, horses gone) and stays unarmed.
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // scarce-stock premise: deposit just the lone good (the generator now seeds arms — 86d3e49gq)
        NativeSettlement home = game.NativeSettlements.First(s => s.Position.Neighbours().Count(n => Free(game, n)) >= 2);
        string nation = home.NationTypeId;
        var spots = home.Position.Neighbours().Where(n => Free(game, n)).Take(2).ToList();
        Unit strong = game.SpawnUnit(Classic.Unit("model.unit.brave"), spots[0], nation);
        strong.RoleId = ArmedBrave; // already armed → stronger than an unarmed brave
        strong.RoleCount = 1;
        Unit weak = game.SpawnUnit(Classic.Unit("model.unit.brave"), spots[1], nation); // unarmed
        Assert.True(HomeOf(game, strong) == home && HomeOf(game, weak) == home); // both home this camp
        EnrageNationOf(game, strong);
        home.AddStock(Horses, 25); // exactly one upgrade's worth of the scarce good

        game.EndTurn();

        Assert.Equal(NativeDragoon, strong.RoleId); // the strongest brave was secured first…
        Assert.Equal(DefaultRole, weak.RoleId);     // …leaving nothing for the weaker one
        Assert.Equal(0, home.StockOf(Horses));      // the lone stock went to the strong brave
    }

    [Fact]
    public void NativeDragoonPromotion_IsRngFree_HumanStream0ByteStable()
    {
        // ADR-009 for the deepened equip path: a game whose camps promote braves to dragoon keeps the human's stream 0
        // byte-identical to a game whose camps can't equip — the strongest-affordable choice is deterministic (no draw).
        Game equips = Game.New(Classic, seed: 2024);
        Game bare = Game.New(Classic, seed: 2024);
        foreach (Game g in new[] { equips, bare })
        {
            ClearAllStock(g); // start both from empty so "bare" genuinely can't equip (the generator seeds arms — 86d3e49gq)
            foreach (NativeSettlement s in g.NativeSettlements)
            {
                g.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // both enraged so the native paths run identically…
            }
        }
        foreach (NativeSettlement s in equips.NativeSettlements)
        {
            s.AddStock(Muskets, 75); // …but only this game's camps can arm AND mount (→ dragoons)
            s.AddStock(Horses, 75);
        }

        for (int turn = 0; turn < 10; turn++)
        {
            equips.EndTurn();
            bare.EndTurn();
        }

        Assert.Contains(equips.NativeUnits, b => b.RoleId == NativeDragoon); // at least one dragoon was made (path ran)
        Assert.Equal(bare.RandomState, equips.RandomState);                  // …yet stream 0 is untouched (RNG-free equip)
        Assert.Equal(bare.HumanPlayer.Gold, equips.HumanPlayer.Gold);
        Assert.NotEqual(SaveGame.From(bare).ToJson(), SaveGame.From(equips).ToJson()); // the native streams genuinely diverged
    }

    // ── Arms/horse redistribution between camps (86d3c9vzp, FreeCol secureSettlements arms-spreading) ───────────────

    /// <summary>The native player for a nation type id.</summary>
    private static Player NationPlayer(Game game, string nationId) =>
        game.Players.First(p => p.PlayerType == PlayerType.Native && p.NationId == nationId);

    /// <summary>A nation with at least two settlements (so it can redistribute), plus those settlements (id order).</summary>
    private static (string Nation, System.Collections.Generic.List<NativeSettlement> Settlements) NationWithTwoCamps(Game game)
    {
        string nation = game.NativeSettlements.GroupBy(s => s.NationTypeId)
            .First(g => g.Count() >= 2).Key;
        var camps = game.NativeSettlements.Where(s => s.NationTypeId == nation).OrderBy(s => s.Id).ToList();
        return (nation, camps);
    }

    [Fact]
    public void ACalmStockedCamp_ShipsArmsToAThreatenedBareCamp()
    {
        // A calm, well-stocked camp sends one half-unit (25) of its surplus muskets to a threatened same-nation camp
        // that has none — the tribe's arms reach the front line (FreeCol secureSettlements arms-spreading).
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled stock: only the donor is stocked (the generator now seeds arms — 86d3e49gq)
        (string nation, var camps) = NationWithTwoCamps(game);
        NativeSettlement donor = camps[0];
        NativeSettlement recipient = camps[1];
        donor.AddStock(Muskets, 60);                     // surplus (≥ 2 half-units after keeping one for itself)
        game.ChangeNativeAlarm(recipient, NativeSettlement.MaxAlarm); // the recipient is threatened (Hateful)
        Assert.True(donor.AlarmLevel < AlarmLevel.Displeased);        // …the donor stays calm (peaceful = 0)

        game.RedistributeArmsToThreatenedSettlements(NationPlayer(game, nation));

        Assert.Equal(35, donor.StockOf(Muskets));     // shipped one half-unit (25) away
        Assert.Equal(25, recipient.StockOf(Muskets)); // …which the bare front-line camp received
    }

    [Fact]
    public void RedistributedArms_LetTheReceivingCamp_ArmItsBraveTheSameTurn()
    {
        // End-to-end through EndTurn: a bare threatened camp receives muskets from a calm stocked camp and arms one of
        // its own braves the SAME turn (redistribution runs before equipBraves), drawing the delivered stock down to 0.
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled: the recipient must start bare so the delivery is what arms it (generator seeds arms — 86d3e49gq)
        (string nation, var camps) = NationWithTwoCamps(game);
        NativeSettlement recipient = camps[0];                    // its game-spawned brave will arm from the delivery
        NativeSettlement donor = camps.First(s => s.Id != recipient.Id);
        // Every brave homing the recipient starts unarmed (no stock anywhere yet).
        Assert.Contains(game.NativeUnits, b => HomeOf(game, b) == recipient && b.RoleId == DefaultRole);
        donor.AddStock(Muskets, 60);                             // the calm donor's surplus
        game.ChangeNativeAlarm(recipient, NativeSettlement.MaxAlarm); // ONLY the recipient is threatened (donor stays calm)

        game.EndTurn();

        // A brave homing the recipient armed itself this turn from the delivered muskets, which were drawn down.
        Assert.Contains(game.NativeUnits, b => HomeOf(game, b) == recipient && b.RoleId == ArmedBrave);
        Assert.Equal(0, recipient.StockOf(Muskets)); // the delivered half-unit was consumed by the equip
        Assert.Equal(35, donor.StockOf(Muskets));    // the donor shipped exactly one half-unit
    }

    [Fact]
    public void RedistributeArms_DoesNothing_WhenNoCampIsThreatened()
    {
        // No threatened recipient → a calm tribe keeps its weapons in store (no movement).
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled stock counts (the generator now seeds arms — 86d3e49gq)
        (string nation, var camps) = NationWithTwoCamps(game);
        camps[0].AddStock(Muskets, 100);

        game.RedistributeArmsToThreatenedSettlements(NationPlayer(game, nation));

        Assert.Equal(100, camps[0].StockOf(Muskets));        // untouched
        Assert.All(camps.Skip(1), s => Assert.Equal(0, s.StockOf(Muskets)));
    }

    [Fact]
    public void RedistributeArms_DoesNotStripAThreatenedDonor()
    {
        // A donor must be CALM: a threatened, stocked camp keeps its arms for its own braves (it isn't a donor), so a
        // second threatened bare camp gets nothing from it.
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled stock counts (the generator now seeds arms — 86d3e49gq)
        (string nation, var camps) = NationWithTwoCamps(game);
        camps[0].AddStock(Muskets, 100);
        foreach (NativeSettlement s in camps)
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // ALL threatened → no calm donor exists
        }

        game.RedistributeArmsToThreatenedSettlements(NationPlayer(game, nation));

        Assert.Equal(100, camps[0].StockOf(Muskets));        // the threatened stocked camp keeps its own arms
        Assert.All(camps.Skip(1), s => Assert.Equal(0, s.StockOf(Muskets)));
    }

    [Fact]
    public void RedistributeArms_ShipsHorsesToo_AndIsRngFree()
    {
        // Horses spread the same way as muskets, and the whole transfer draws no randomness (ADR-009).
        Game game = Game.New(Classic, seed: 7);
        ClearAllStock(game); // controlled stock counts (the generator now seeds arms — 86d3e49gq)
        (string nation, var camps) = NationWithTwoCamps(game);
        NativeSettlement donor = camps[0];
        NativeSettlement recipient = camps[1];
        donor.AddStock(Horses, 50);
        game.ChangeNativeAlarm(recipient, NativeSettlement.MaxAlarm);
        var before = game.RandomState;

        game.RedistributeArmsToThreatenedSettlements(NationPlayer(game, nation));

        Assert.Equal(25, recipient.StockOf(Horses));   // a half-unit of horses delivered
        Assert.Equal(25, donor.StockOf(Horses));
        Assert.Equal(before, game.RandomState);        // RNG-free → human stream 0 untouched
    }

    [Fact]
    public void RedistributeArms_DrawsOnlyOnNonHumanStreams_HumanStream0ByteStable()
    {
        // The decisive ADR-009 guard: a game whose tribes spread arms (and then arm braves) keeps the human's stream 0
        // byte-identical to a game whose tribes hold no stock — only the native state diverges.
        Game spreads = Game.New(Classic, seed: 4242);
        Game bare = Game.New(Classic, seed: 4242);
        ClearAllStock(spreads); // start both from empty so "bare" genuinely holds no stock (the generator seeds arms — 86d3e49gq)
        ClearAllStock(bare);
        foreach (NativeSettlement s in spreads.NativeSettlements)
        {
            s.AddStock(Muskets, 60); // every spreading camp is stocked
        }
        // Enrage all but the per-nation lowest-id camp in BOTH games (so the native-turn paths run identically — the
        // lowest-id camp stays a calm donor), but only the spreading game actually has stock to move/equip.
        foreach (Game g in new[] { spreads, bare })
        {
            foreach (var grp in g.NativeSettlements.GroupBy(s => s.NationTypeId))
            {
                foreach (NativeSettlement s in grp.OrderBy(s => s.Id).Skip(1))
                {
                    g.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
                }
            }
        }

        for (int turn = 0; turn < 8; turn++)
        {
            spreads.EndTurn();
            bare.EndTurn();
        }

        Assert.Equal(bare.RandomState, spreads.RandomState);   // stream 0 untouched
        Assert.Equal(bare.HumanPlayer.Gold, spreads.HumanPlayer.Gold);
        Assert.NotEqual(SaveGame.From(bare).ToJson(), SaveGame.From(spreads).ToJson()); // native state genuinely diverged
    }

    // ── AI scout-chief + per-player first contact (86d3c9vta slice, save v44) ──────────────────────────────────────

    private static NativeSettlement SettlementWithFreeNeighbour(Game game) =>
        game.NativeSettlements.First(s => s.Position.Neighbours().Any(n => Free(game, n)));

    [Fact]
    public void ChiefVisit_IsPerPlayer_AForeignVisitDoesNotConsumeTheHumansFirstContact()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        NativeSettlement settlement = SettlementWithFreeNeighbour(game);
        Position adj = settlement.Position.Neighbours().First(n => Free(game, n));
        Unit powerColonist = game.SpawnUnit(Classic.Unit(FreeColonist), adj);
        powerColonist.OwnerId = power.PlayerId;

        game.Visit(power, powerColonist, settlement); // the power speaks with the chief (its own stream)

        Assert.True(settlement.HasBeenVisitedBy(power.PlayerId));            // the power has now visited
        Assert.False(settlement.HasBeenVisitedBy(game.HumanPlayer.PlayerId)); // the human's first contact is intact
        Assert.False(settlement.HasBeenVisited);                            // (= the human's flag)
    }

    [Fact]
    public void AForeignColonist_AtColonyCap_SpeaksWithAnAdjacentChief_OnItsTurn()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u); // clear the power's starting units so only what we stage acts
        }
        // Colonies up to MaxAiColonies → the power is at its cap, so its remaining colonist explores/visits not founds.
        var taken = new System.Collections.Generic.List<Position>();
        for (int i = 0; i < Game.MaxAiColonies; i++)
        {
            Position tile = game.Map.AllPositions().First(p =>
                Free(game, p) && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n))
                && taken.All(t => Cheb(t, p) > 3));
            Unit founder = game.SpawnUnit(Classic.Unit(FreeColonist), tile);
            founder.OwnerId = power.PlayerId;
            Colony colony = game.FoundColony(founder);
            colony.OwnerId = power.PlayerId;
            taken.Add(tile);
        }
        // A further colonist beside an unvisited native settlement.
        NativeSettlement settlement = SettlementWithFreeNeighbour(game);
        Position adj = settlement.Position.Neighbours().First(n => Free(game, n));
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), adj);
        colonist.OwnerId = power.PlayerId;
        Assert.False(settlement.HasBeenVisitedBy(power.PlayerId));

        game.EndTurn();

        Assert.True(settlement.HasBeenVisitedBy(power.PlayerId)); // it spoke with the chief on its own turn
    }

    [Fact]
    public void APowersChiefVisit_RoundTripsThroughSave_V44()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        NativeSettlement settlement = SettlementWithFreeNeighbour(game);
        Position adj = settlement.Position.Neighbours().First(n => Free(game, n));
        Unit powerColonist = game.SpawnUnit(Classic.Unit(FreeColonist), adj);
        powerColonist.OwnerId = power.PlayerId;
        game.Visit(power, powerColonist, settlement);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        NativeSettlement r = restored.NativeSettlements.Single(s => s.Id == settlement.Id);

        Assert.Equal(61, SaveGame.CurrentVersion);
        Assert.True(r.HasBeenVisitedBy(power.PlayerId)); // the power's visit survives the round trip
    }

    [Fact]
    public void ANoForeignVisitGame_OmitsTheVisitedByPowersField()
    {
        string json = SaveGame.From(Game.New(Classic, seed: 7)).ToJson();
        Assert.DoesNotContain("VisitedByPowers", json); // additive: omitted → byte-identical to v43 but for the version
    }
}
