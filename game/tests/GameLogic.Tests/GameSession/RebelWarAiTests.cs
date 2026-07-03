using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Offensive rebel-vs-REF war AI for an <b>AI rebel</b> (<c>86d3e4q65</c>, the descoped follow-up of the
/// <c>86d3e49jp</c> AI self-declaration): an AI rebel at war with the Royal Expeditionary Force now prosecutes that
/// war through the same <c>RunForeignPowerTurn</c> offensive the human war uses — <c>RebelRefEnemy</c> makes the REF
/// the war enemy (taking precedence over any human war), so an armed rebel unit recaptures adjacent undefended
/// REF-held colonies, seek-and-destroys the King's landed field units, and marches on the King's colonies. Combat
/// draws only the rebel's own stream (ADR-009), and none of it is the <em>human's</em> business: no
/// <c>CombatNotice</c>/<c>ColonyLossNotice</c> fires for a rebel-vs-REF battle (the <c>RaidForeignUnit</c> rule).
/// The default game has no AI rebel (see <c>ShouldAiDeclareIndependence</c>), so this path never runs there.
/// </summary>
public class RebelWarAiTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";
    private const string KingsRegular = "model.unit.kingsRegular";

    private static Player Ref(Game game) => game.Players.Single(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);

    private static int Cheb(Position a, Position b) => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    private static bool FreeLandTile(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater && g.ColonyAt(p) is null
        && g.NativeSettlementAt(p) is null && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>Founds a colony owned by <paramref name="power"/> on the first free coastal land tile not adjacent to an existing colony.</summary>
    private static Colony FoundColonyFor(Game game, Player power)
    {
        Position spot = game.Map.AllPositions().First(p =>
            FreeLandTile(game, p)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater)
            && !p.Neighbours().Any(n => game.Map.InBounds(n) && game.ColonyAt(n) is not null)); // colony footprints never touch
        Unit colonist = game.SpawnUnit(game.Ruleset.Unit(Game.StartingUnitTypeId), spot);
        colonist.OwnerId = power.PlayerId;
        return game.FoundColony(colonist);
    }

    /// <summary>Clears the REF roster down to nothing (so a test can stage a controlled war).</summary>
    private static void ClearRef(Game game, Player refP)
    {
        foreach (Unit u in game.Units.Where(u => u.OwnerId == refP.PlayerId).ToList())
        {
            game.Disband(u);
        }
    }

    /// <summary>Spawns an armed rebel veteran (soldier role) for <paramref name="rebel"/> at <paramref name="at"/>.</summary>
    private static Unit ArmedRebel(Game game, Player rebel, Position at)
    {
        Unit soldier = game.SpawnUnit(Classic.Unit(VeteranSoldier), at, rebel.PlayerId);
        soldier.RoleId = SoldierRole;
        soldier.RoleCount = 1;
        return soldier;
    }

    /// <summary>
    /// The first AI colonial power with one coastal colony forced to 100% national SoL, its independence declared
    /// (through the same <c>DeclareIndependence</c> the AI trigger calls — it flips to Rebel and spawns the REF at
    /// war with it), and the King's roster cleared for controlled staging. A fresh game has no visited native
    /// settlements, so the declaration's native re-stancing is inert here.
    /// </summary>
    private static (Game Game, Player Rebel, Colony Colony, Player RefP) AiRebelAtWar(ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Colony colony = FoundColonyFor(game, power);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // force this AI's national SoL to 100%
        game.DeclareIndependence(power);
        Player refP = Ref(game);
        ClearRef(game, refP);
        Assert.Equal(PlayerType.Rebel, power.PlayerType);
        Assert.Equal(Stance.War, game.StanceBetween(power.PlayerId, refP.PlayerId));
        return (game, power, colony, refP);
    }

    [Fact]
    public void RebelSoldier_RecapturesAnAdjacentUndefendedRefHeldColony()
    {
        // The decisive-move rung (AdjacentCapturableEnemyColony → CapturePlayerColony): an armed AI-rebel unit beside
        // an undefended colony the King holds takes it back on the rebel's own turn. Pre-86d3e4q65 the offensive was
        // keyed to the human, so a rebel at war with the REF only garrisoned — the colony stayed the King's.
        // Seed note: the assault is a real combat roll on the rebel's own seeded stream; this seed's roll wins (a
        // repelled attacker is a legal outcome — the seek-and-destroy test accepts either — here we pin the recapture).
        (Game game, Player rebel, _, Player refP) = AiRebelAtWar(seed: 42);
        Colony lost = FoundColonyFor(game, rebel); // founded by the rebel…
        lost.OwnerId = refP.PlayerId;              // …and handed to the King, as a REF capture would (ungarrisoned)
        Position beside = lost.Position.Neighbours().First(n => FreeLandTile(game, n));
        ArmedRebel(game, rebel, beside);

        game.EndTurn(); // the rebel's ring slot runs before the (unit-less) REF's — the recapture rung fires

        Assert.Equal(rebel.PlayerId, game.ColonyAt(lost.Position)!.OwnerId); // the colony flies rebel colours again
        // The human is no party to a rebel-vs-REF battle (the RaidForeignUnit rule): no victim-log notices fire.
        Assert.Empty(game.CombatNotices);
        Assert.Empty(game.ColonyLossNotices);
    }

    [Fact]
    public void RebelSoldier_SeeksAndDestroysALandedRefFieldUnit()
    {
        // The scored seek-and-destroy rung (PickAttackTarget(unit, REF) → AttackEnemyUnit): an armed rebel beside a
        // landed king's regular engages it on the rebel's turn. Pre-86d3e4q65 NEITHER side would fight that turn —
        // the rebel only garrisoned, and the colony-only REF doctrine ignores field units until a colony falls — so
        // "both units unchanged" is exactly the old behaviour this pins against.
        (Game game, Player rebel, Colony home, Player refP) = AiRebelAtWar();
        Position fieldTile = game.Map.AllPositions().First(p =>
            FreeLandTile(game, p) && Cheb(p, home.Position) > 1
            && p.Neighbours().Any(n => FreeLandTile(game, n)));
        Unit redcoat = game.SpawnUnit(Classic.Unit(KingsRegular), fieldTile, refP.PlayerId);
        Position beside = fieldTile.Neighbours().First(n => FreeLandTile(game, n));
        Unit soldier = ArmedRebel(game, rebel, beside);
        string roleBefore = soldier.RoleId;

        game.EndTurn(); // the rebel strikes first (its ring slot precedes the REF's, appended at declaration)

        // Combat resolved on the rebel's own stream: the redcoat fell/was captured (a win) or the repelled rebel was
        // demoted/disarmed/captured (a loss) — any outcome proves the offensive engaged.
        Unit? redcoatAfter = game.Units.FirstOrDefault(u => u.Id == redcoat.Id);
        Unit? soldierAfter = game.Units.FirstOrDefault(u => u.Id == soldier.Id);
        bool redcoatFell = redcoatAfter is null || redcoatAfter.OwnerId == rebel.PlayerId;
        bool rebelRepelled = soldierAfter is null || soldierAfter.OwnerId != rebel.PlayerId
            || soldierAfter.RoleId != roleBefore || soldierAfter.Type.Id != VeteranSoldier;
        Assert.True(redcoatFell || rebelRepelled, "the rebel's offensive must engage the adjacent REF field unit");
        Assert.Empty(game.CombatNotices); // no human-facing notice for a rebel-vs-REF fight
    }

    [Fact]
    public void RebelWar_MarchesOnAndRetakesTheKingsColony_WithNoHumanFacingNotices()
    {
        // The colony-seek path over several turns: an armed rebel a few tiles from the King's (ungarrisoned) colony
        // marches on it (scored colony target / besiege fallback) and retakes it on arrival — and across the whole
        // war not one human-facing notice fires (CombatNotices/ColonyLossNotices are the HUMAN's victim log; a
        // rebel-vs-REF war is not the human's business). While the King holds a colony the war cannot resolve
        // (CheckForRefDefeat requires the REF settlement-less), so the offensive keeps running until the recapture.
        (Game game, Player rebel, _, Player refP) = AiRebelAtWar();
        Colony lost = FoundColonyFor(game, rebel);
        lost.OwnerId = refP.PlayerId;
        Position start = game.Map.AllPositions().First(p =>
            FreeLandTile(game, p) && Cheb(p, lost.Position) is >= 3 and <= 6
            && p.Neighbours().Any(n => game.Map.InBounds(n) && FreeLandTile(game, n)
                && Cheb(n, lost.Position) < Cheb(p, lost.Position)));
        ArmedRebel(game, rebel, start);

        for (int turn = 0; turn < 10 && game.ColonyAt(lost.Position)!.OwnerId != rebel.PlayerId; turn++)
        {
            game.EndTurn();
            Assert.Empty(game.CombatNotices);     // rebel-vs-REF combat never reaches the human's victim log
            Assert.Empty(game.ColonyLossNotices); // nor its colony-loss log
        }
        Assert.Equal(rebel.PlayerId, game.ColonyAt(lost.Position)!.OwnerId); // retaken within the window
    }

    [Fact]
    public void Ref_CapturesAnAiRebelsColony_WithNoHumanFacingLossNotice()
    {
        // The REF's OWN offensive against an AI rebel (CaptureRebelColony): a King's regular beside the rebel's
        // undefended colony takes it on the REF's turn. 86d3jgwhj: before the gate the REF-side helpers recorded a
        // ColonyLossNotice unconditionally, so a war between the King and an AI rebel spammed the human with loss
        // notices about a war they are not part of. The notice must now stay empty (the human's victim log), while the
        // capture itself still happens.
        (Game game, Player rebel, Colony colony, Player refP) = AiRebelAtWar();
        Position beside = colony.Position.Neighbours().First(n => FreeLandTile(game, n));
        game.SpawnUnit(Classic.Unit(KingsRegular), beside, refP.PlayerId); // a landed redcoat beside the rebel port

        game.EndTurn(); // the (unit-less) rebel does nothing; the REF regular captures the undefended colony

        Assert.Equal(refP.PlayerId, game.ColonyAt(colony.Position)!.OwnerId); // the King took it
        Assert.Empty(game.ColonyLossNotices); // …but the human — no party to this war — is never told
        Assert.Empty(game.CombatNotices);
    }

    [Fact]
    public void Ref_FightsAnAiRebelsGarrison_WithNoHumanFacingCombatNotice()
    {
        // The REF's OWN offensive against a garrisoned AI-rebel colony (AttackRebelUnit — the "garrisoned → fight the
        // defender" branch): a King's regular beside a defended rebel colony fights its garrison on the REF's turn.
        // 86d3jgwhj: AttackRebelUnit recorded a CombatNotice unconditionally; a REF-vs-AI-rebel battle must record none.
        // Whatever fires this war turn — the REF attacking the garrison, or the rebel sortieing (already gated) — no
        // human-facing notice may appear, since the human is not a party.
        (Game game, Player rebel, Colony colony, Player refP) = AiRebelAtWar();
        Position onColony = colony.Position;
        Unit garrison = ArmedRebel(game, rebel, onColony); // an armed rebel standing in the colony → it is defended
        garrison.Orders = UnitOrders.Fortified;            // dug in → the rebel offensive leaves it; the REF strikes it
        Position beside = onColony.Neighbours().First(n => FreeLandTile(game, n));
        game.SpawnUnit(Classic.Unit(KingsRegular), beside, refP.PlayerId);

        game.EndTurn();

        Assert.Empty(game.CombatNotices);     // REF-vs-AI-rebel combat never reaches the human's victim log
        Assert.Empty(game.ColonyLossNotices);
    }

    [Fact]
    public void Ref_CapturingTheHumanRebelsColony_StillNotifiesTheHuman()
    {
        // The regression pin: the human-victim gate must NOT over-suppress. When the HUMAN is the rebel, the REF taking
        // their colony is very much their business — the ColonyLossNotice must still fire (the human declared, so
        // IsHumanOwned is true even as a Rebel). This is the case AttackRebelUnit/CaptureRebelColony were written for.
        Game game = Game.New(Classic, Seed);
        Player human = game.HumanPlayer;
        Colony colony = FoundColonyFor(game, human);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // 100% national SoL → eligible to declare
        game.DeclareIndependence(human);
        Player refP = Ref(game);
        ClearRef(game, refP);
        Assert.Equal(PlayerType.Rebel, human.PlayerType);
        Position beside = colony.Position.Neighbours().First(n => FreeLandTile(game, n));
        game.SpawnUnit(Classic.Unit(KingsRegular), beside, refP.PlayerId);

        game.EndTurn();

        Assert.Equal(refP.PlayerId, game.ColonyAt(colony.Position)!.OwnerId); // the King took the human's colony…
        Assert.NotEmpty(game.ColonyLossNotices);                             // …and the human is told (their own loss)
    }

    [Fact]
    public void RebelRefWar_IsDeterministic_AcrossTwinGames()
    {
        // The whole offensive draws only the rebel's own seeded stream (target selection is RNG-free; combat is
        // RandomFor(power)) — twin same-seed games play out byte-identically (ADR-009).
        static Game Staged(ulong seed)
        {
            (Game game, Player rebel, Colony home, Player refP) = AiRebelAtWar(seed);
            Position fieldTile = game.Map.AllPositions().First(p =>
                FreeLandTile(game, p) && Cheb(p, home.Position) > 1
                && p.Neighbours().Any(n => FreeLandTile(game, n)));
            game.SpawnUnit(Classic.Unit(KingsRegular), fieldTile, refP.PlayerId);
            ArmedRebel(game, rebel, fieldTile.Neighbours().First(n => FreeLandTile(game, n)));
            return game;
        }

        Game a = Staged(7777);
        Game b = Staged(7777);
        for (int i = 0; i < 5; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        Assert.Equal(a.RandomState, b.RandomState);
    }
}
