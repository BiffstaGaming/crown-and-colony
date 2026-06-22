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
/// AI defence — colonist arming + garrison fortify (<c>86d3e49cm</c>, FreeCol <c>EuropeanAIPlayer.giveNormalMissions</c>
/// arming + <c>buyDragoon</c>'s ARMED+MOUNTED preference + <c>DefendSettlementMission</c> ending in <c>Unit.fortify</c>):
/// a foreign power arms an idle plain colonist standing in an UNDER-DEFENDED own colony to the strongest affordable
/// military role (dragoon over soldier) from that colony's own stock, and an armed land guard on an own colony tile
/// digs in (Fortifying → Fortified). Every choice is by colony stock / ordinal preference — no RNG draw, so the human's
/// stream 0 stays byte-identical (ADR-009; the soak suite proves the byte-stability).
/// </summary>
public class ForeignDefenceArmingTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string FreeColonist = "model.unit.freeColonist";
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string Soldier = "model.role.soldier";
    private const string Dragoon = "model.role.dragoon";
    private const string Muskets = "model.goods.muskets";
    private const string Horses = "model.goods.horses";

    /// <summary>Human (stream 0) + one foreign colonial power (id 1) — the roster the fixtures restore with.</summary>
    private static List<SavedPlayer> HumanPlusForeign() =>
    [
        new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial),
        new SavedPlayer(1, "model.nation.dutch", IsHuman: false, PlayerType: (int)PlayerType.Colonial),
    ];

    /// <summary>
    /// A <paramref name="size"/>×<paramref name="size"/> all-plains map fully explored by the foreign power, with a
    /// foreign colony at the centre and the given foreign-owned units. Returns the game, the foreign colony and the
    /// power. The colony sits at <c>(size/2, size/2)</c> (so (1,1) on the default 3×3).
    /// </summary>
    private static (Game Game, Colony Colony, Player Power) ForeignFixture(
        IReadOnlyList<SavedUnit> units, IReadOnlyDictionary<string, int>? colonyStores = null, int size = 3)
    {
        int c = size / 2;
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = size,
            MapHeight = size,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", size * size)],
            Units = units,
            Explored = [.. Enumerable.Range(0, size * size)], // human-side fog is irrelevant here; the AI uses its own
            Players = HumanPlusForeign(),
            Colonies = [new SavedColony(1, "Nieuw Holland", c, c, 1, Stores: colonyStores)],
        };
        Game game = save.Restore(Classic);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Colony colony = game.Colonies[0];
        colony.OwnerId = power.PlayerId;
        foreach (Position p in game.Map.AllPositions())
        {
            power.ExploredSet.Add(p);
        }
        return (game, colony, power);
    }

    private static SavedUnit ForeignUnit(int id, string typeId, int x, int y, string? role = null, int? roleCount = null) =>
        new(id, typeId, x, y, 3, OwnerId: 1, Role: role, RoleCount: roleCount);

    private static Unit UnitOf(Game game, int id) => game.Units.First(u => u.Id == id);

    [Fact]
    public void ForeignPower_ArmsAnIdleColonistAsASoldier_FromMuskets_WhenUnderDefended()
    {
        // The colony stocks 50 muskets (but no horses) and holds an idle plain colonist with no defender — the power
        // arms it to model.role.soldier, deducting the 50 muskets from THIS colony's store.
        (Game game, Colony colony, _) = ForeignFixture(
            [ForeignUnit(1, FreeColonist, 1, 1)],
            colonyStores: new Dictionary<string, int> { [Muskets] = 50 });

        game.EndTurn();

        Unit colonist = UnitOf(game, 1);
        Assert.Equal(Soldier, colonist.RoleId);                                 // armed as a soldier
        Assert.Equal(0, colony.StoreOf(Classic.StorageIdOf(Muskets)));          // the colony's own muskets were spent
    }

    [Fact]
    public void ForeignPower_PrefersDragoon_WhenTheColonyAlsoStocksHorses()
    {
        // With both 50 muskets AND 50 horses, the strongest affordable role is the ARMED+MOUNTED dragoon (FreeCol
        // buyDragoon's preference) — the colonist is armed to dragoon, not soldier, draining both goods.
        (Game game, Colony colony, _) = ForeignFixture(
            [ForeignUnit(1, FreeColonist, 1, 1)],
            colonyStores: new Dictionary<string, int> { [Muskets] = 50, [Horses] = 50 });

        game.EndTurn();

        Unit colonist = UnitOf(game, 1);
        Assert.Equal(Dragoon, colonist.RoleId);                                 // strongest affordable → dragoon
        Assert.Equal(0, colony.StoreOf(Classic.StorageIdOf(Muskets)));
        Assert.Equal(0, colony.StoreOf(Classic.StorageIdOf(Horses)));
    }

    [Fact]
    public void ForeignPower_FallsBackToSoldier_WhenHorsesAreShort()
    {
        // 50 muskets but only 25 horses — dragoon is unaffordable, so the strongest affordable role is the soldier
        // (muskets only). The 25 horses are not needed for a soldier, so the economy's surplus-sell legitimately cashes
        // them out (they are not reserved) — the meaningful outcome is that the colonist is armed as a soldier, not a
        // dragoon, deducting the muskets from this colony's store.
        (Game game, Colony colony, _) = ForeignFixture(
            [ForeignUnit(1, FreeColonist, 1, 1)],
            colonyStores: new Dictionary<string, int> { [Muskets] = 50, [Horses] = 25 });

        game.EndTurn();

        Assert.Equal(Soldier, UnitOf(game, 1).RoleId);                          // dragoon unaffordable → soldier
        Assert.Equal(0, colony.StoreOf(Classic.StorageIdOf(Muskets)));          // the soldier's 50 muskets were spent
    }

    [Fact]
    public void ForeignPower_DoesNotArm_WhenTheColonyHasNoMuskets()
    {
        // No muskets in store → neither soldier nor dragoon is affordable → the colonist stays a plain worker.
        (Game game, _, _) = ForeignFixture(
            [ForeignUnit(1, FreeColonist, 1, 1)],
            colonyStores: new Dictionary<string, int> { [Horses] = 50 });

        game.EndTurn();

        Assert.True(UnitOf(game, 1).HasDefaultRole);                            // unarmed — nothing to equip with
    }

    [Fact]
    public void ForeignPower_DoesNotArm_WhenTheColonyAlreadyHasAMilitaryDefender()
    {
        // The colony already holds an armed veteran soldier (a military land defender), so the idle colonist is NOT
        // armed — arming is gated on the colony being under-defended, never strips a worker from a defended colony.
        (Game game, Colony colony, _) = ForeignFixture(
            [
                ForeignUnit(1, VeteranSoldier, 1, 1, Soldier, 1), // an existing armed defender on the colony tile
                ForeignUnit(2, FreeColonist, 1, 1),               // an idle colonist in the same colony
            ],
            colonyStores: new Dictionary<string, int> { [Muskets] = 50 });

        game.EndTurn();

        Assert.True(UnitOf(game, 2).HasDefaultRole);                            // stayed a worker — colony is defended
        // The colony already has a defender, so its muskets are NOT reserved for arming — the idle colonist is left a
        // worker (the under-defended gate held). A defended colony's surplus muskets are sold by the economy as usual.
        Assert.Equal(Soldier, UnitOf(game, 1).RoleId);                          // the existing soldier is untouched
    }

    [Fact]
    public void ForeignPower_ArmedGarrison_DigsIn_FortifyingThenFortified()
    {
        // An armed veteran soldier standing on its own undefended colony tile is set Fortifying this turn, then ages to
        // Fortified at the next turn reset — the +50% dig-in the defence context reads (DefenceContext.Fortified).
        (Game game, _, _) = ForeignFixture([ForeignUnit(1, VeteranSoldier, 1, 1, Soldier, 1)]);

        game.EndTurn(); // FP-5 garrison branch fortifies the guard standing in its own colony…
        Assert.True(UnitOf(game, 1).IsFortified);                               // …and the end-of-turn aging completed it
    }

    [Fact]
    public void ForeignPower_ArmsThenFortifies_OverTwoTurns_ForTheCombatBonus()
    {
        // End-to-end: turn 1 arms the idle colonist (under-defended colony, muskets in store); turn 2 the now-armed
        // guard digs in; by the next reset it is Fortified — the AI both arms AND entrenches a defender unaided, the
        // dig-in the combat model applies as +50% defence.
        (Game game, _, _) = ForeignFixture(
            [ForeignUnit(1, FreeColonist, 1, 1)],
            colonyStores: new Dictionary<string, int> { [Muskets] = 50 });

        game.EndTurn();
        Assert.Equal(Soldier, UnitOf(game, 1).RoleId);  // turn 1: armed

        game.EndTurn();
        Assert.True(UnitOf(game, 1).IsFortified);        // turn 2: dug in → Fortified (combat reads +50%)
    }

    [Fact]
    public void ForeignArming_IsDeterministic_AcrossIdenticalRuns()
    {
        // The same fixture played the same way reaches the same role/fortify state — the arm/fortify paths draw no RNG.
        static (string Role, bool Fortified) Play()
        {
            (Game g, _, _) = ForeignFixture(
                [ForeignUnit(1, FreeColonist, 1, 1)],
                colonyStores: new Dictionary<string, int> { [Muskets] = 50, [Horses] = 50 });
            g.EndTurn();
            g.EndTurn();
            Unit u = g.Units.First(x => x.Id == 1);
            return (u.RoleId, u.IsFortified);
        }

        Assert.Equal(Play(), Play());
    }
}
