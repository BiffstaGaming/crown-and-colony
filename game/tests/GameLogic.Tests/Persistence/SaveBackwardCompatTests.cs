using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Persistence;

/// <summary>
/// Backward-compatibility guard for the save format (the user's "don't lose my save on a code change" guarantee).
/// <para>
/// The promise: every format change since version N is <b>additive + omitted-when-default</b>, and
/// <see cref="SaveGame.Restore"/> never rejects on version — it defaults each absent field. So newer code always
/// loads an older save. (The inverse — older code loading a NEWER save — is NOT supported and cannot be: an old binary
/// has no field to read the newer data into. But the user's case is forward-updating the code, i.e. NEW code reading
/// an OLD save, which is exactly what these tests pin.)
/// </para>
/// <para>
/// Two complementary techniques:
/// <list type="bullet">
/// <item>a hand-authored on-disk <c>save-v40.json</c> fixture — a TRUE old-format file (no field added since v40 is
/// present), so it cannot silently track today's <see cref="SaveGame.From"/> shape; and</item>
/// <item>synthetic down-version saves built by taking a current <see cref="SaveGame.From"/>, stamping an older
/// <see cref="SaveGame.Version"/> and nulling the top-level fields added after that version — proving each historical
/// load path defaults cleanly across a realistic, fully-populated game.</item>
/// </list>
/// </para>
/// (Existing per-version load paths are also covered field-by-field in <see cref="SaveGameTests"/>; this file is the
/// dedicated, broad regression that locks the guarantee as <see cref="SaveGame.CurrentVersion"/> keeps climbing.)
/// </summary>
public class SaveBackwardCompatTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    // ── Hand-authored, true old-format file ───────────────────────────────────────────────────────────────────

    [Fact]
    public void HandWrittenV40Save_LoadsCleanlyOnCurrentVersion_AndIsPlayable()
    {
        // A REAL, hand-authored v40 save FILE (committed under fixtures/, NOT produced by today's From()). v40 predates
        // the rebellion lifecycle (v41), Spanish succession (v42), difficulty/resource-quantities/monarch-demand (v46),
        // river improvements + REF entry tile (v47), pioneer work-state (v48), unit nationality/name (v52), per-player
        // alarm (v55), trade accounting (v56), history/message logs (v58/v59), the tax-raise cooldown (v60), and Players[]
        // (v20). Restore must default every one of those safely. This is a true old-format load: a current From() can
        // never reproduce this shape because it always writes today's version.
        string json = File.ReadAllText(FixturePath("save-v40.json"));
        SaveGame save = SaveGame.FromJson(json);

        Assert.Equal(40, save.Version);
        Assert.True(save.Version < SaveGame.CurrentVersion); // genuinely older than what we run today

        Game loaded = save.Restore(Classic);

        // ── World + turn restored ──
        Assert.Equal(8, loaded.Turn);
        Assert.Equal(6, loaded.Map.Width);
        Assert.Equal(4, loaded.Map.Height);

        // ── Player folded from the flat fields (a v40 save predates Players[]) ──
        Player human = Assert.Single(loaded.Players);
        Assert.True(human.IsHuman);
        Assert.Equal(0, human.PlayerId);
        Assert.Equal(420, human.Gold);
        Assert.Equal(5, human.TaxRate);
        Assert.Equal(15, human.Liberty);

        // ── Units restored; every field added after v40 defaults safely ──
        Assert.Equal(2, loaded.Units.Count);
        Unit colonist = loaded.Units.Single(u => u.Id == 1);
        Assert.Equal("model.unit.freeColonist", colonist.Type.Id);
        Assert.Equal(new Position(2, 1), colonist.Position);
        Assert.Equal(0, colonist.Attrition);              // v50 absent → 0
        Assert.Null(colonist.Name);                       // v52 absent → un-christened
        Assert.Equal(UnitOrders.Active, colonist.Orders); // v23 absent → Active

        // ── Colony restored with its warehouse + buildings ──
        Colony colony = Assert.Single(loaded.Colonies);
        Assert.Equal("Plymouth", colony.Name);
        Assert.Equal(new Position(3, 2), colony.Position);
        Assert.Equal(2, colony.Population);

        // ── Game-wide fields added after v40 default safely ──
        Assert.False(loaded.SpanishSuccessionDone);                          // v42 absent → not done
        Assert.Null(loaded.PendingMonarchDemand);                            // v46 absent → none
        Assert.Equal(DifficultyLevels.DefaultId, loaded.DifficultyLevelId);  // v46 absent → medium
        Assert.NotEmpty(loaded.Map.Regions);                                 // v35 absent → re-derived deterministically
        Assert.All(loaded.Map.Regions, r => Assert.Null(r.DiscoveredBy));    // v51 absent → every region undiscovered
        Assert.False(human.MonarchDispleasure);                              // v38 absent → content
        Assert.Empty(human.PeaceTurns);                                      // v53 absent → no recorded peace

        // ── Playable: an EndTurn runs without throwing and advances the turn ──
        int before = loaded.Turn;
        loaded.EndTurn();
        Assert.Equal(before + 1, loaded.Turn);
    }

    // ── Synthetic down-version loads across a fully-populated game ─────────────────────────────────────────────

    [Theory]
    [InlineData(45)]
    [InlineData(50)]
    [InlineData(55)]
    [InlineData(64)]
    public void DownVersionedSave_LoadsWithoutThrowing_AndKeepsCoreStateIntact(int version)
    {
        // Take a fully-populated current save, stamp an older Version and strip the top-level fields added AFTER that
        // version, then assert Restore tolerates the older shape and the core state (turn / units / colonies / gold)
        // survives. This proves the per-version default path against a realistic game (colonies, natives, market
        // movement, fog) rather than a bare fixture — and it can't drift, since the stripping is keyed off the version.
        var game = Game.New(Classic, seed: 123, startingGold: 333, startingTax: 6);
        var founded = game.FoundColony(game.Units.First(u => !u.Type.IsNaval));
        game.EndTurn();
        game.EndTurn();

        int expectedTurn = game.Turn;
        int expectedUnits = game.Units.Count;
        int expectedColonies = game.Colonies.Count;
        int expectedGold = game.HumanPlayer.Gold;

        SaveGame old = DownVersion(SaveGame.From(game), version);
        Assert.Equal(version, old.Version);

        // Round-trip through JSON so we exercise the real deserialize → Restore path, not an in-memory record.
        Game loaded = SaveGame.FromJson(old.ToJson()).Restore(Classic);

        Assert.Equal(expectedTurn, loaded.Turn);
        Assert.Equal(expectedUnits, loaded.Units.Count);
        Assert.Equal(expectedColonies, loaded.Colonies.Count); // every colony (incl. rivals') survives the down-version
        Assert.Equal(expectedGold, loaded.HumanPlayer.Gold);
        // The human's founded colony is among them, with its name/position intact (a colony with rivals on the map).
        Colony loadedFounded = loaded.Colonies.Single(c => c.Id == founded.Id);
        Assert.Equal(founded.Name, loadedFounded.Name);
        Assert.Equal(founded.Position, loadedFounded.Position);

        // Still playable after the down-version load.
        int before = loaded.Turn;
        loaded.EndTurn();
        Assert.Equal(before + 1, loaded.Turn);
    }

    [Fact]
    public void CurrentSave_RoundTrips_AtL2()
    {
        // Mirror at L2 the round-trip the soak relies on: a current-version save reloads to identical core state. (The
        // soak proves byte-identical RNG continuity over many turns; this is the fast, always-on L2 sentinel.)
        var game = Game.New(Classic, seed: 77, startingGold: 250, startingTax: 9);
        game.FoundColony(game.Units.First(u => !u.Type.IsNaval));
        game.EndTurn();

        SaveGame save = SaveGame.From(game);
        Assert.Equal(SaveGame.CurrentVersion, save.Version); // a fresh From() always writes today's version

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);

        Assert.Equal(game.Turn, loaded.Turn);
        Assert.Equal(game.Units.Count, loaded.Units.Count);
        Assert.Equal(game.Colonies.Count, loaded.Colonies.Count);
        Assert.Equal(game.HumanPlayer.Gold, loaded.HumanPlayer.Gold);
        Assert.Equal(game.RandomState, loaded.RandomState); // RNG continuity (ADR-009)
    }

    /// <summary>
    /// Stamps an older <see cref="SaveGame.Version"/> on a current save and nulls the top-level fields added <b>after</b>
    /// that version, producing a save shaped like real old code would have written. The nested additive fields (per-unit
    /// attrition/name, per-player trade accounts/peace turns, per-settlement alarm channels/general stock, …) are already
    /// omitted-when-default for a fresh game, so a realistic mid-game save's nested data is left as-is and simply ignored
    /// by the older-version assertions — the load path defaults each absent field. Versions ≥20 keep <c>Players[]</c>
    /// (the v20+ load path), so the player state is preserved across the down-version.
    /// </summary>
    private static SaveGame DownVersion(SaveGame s, int version) => version switch
    {
        // After v64: the year-by-year Demographics series (v65, top-level). v64 itself added no field (river magnitude
        // rides the existing Improvements token), so a v64 save is a v65 save minus Demographics.
        64 => s with { Version = 64, Demographics = null },

        // After v55: TradeAccounts (v56), GeneralStock (per-settlement, v57), History (v58), MessageLog (v59),
        // LastTaxRaiseTurn (per-player, v60), then everything through v64. The per-player/per-settlement ones are nested
        // and omitted-when-default; only the top-level History/MessageLog/Demographics need nulling here.
        55 => s with { Version = 55, History = null, MessageLog = null, Demographics = null },

        // After v50: region discovery (v51, nested in Regions), unit nationality/name (v52, nested), PeaceTurns (v53,
        // nested), NextUnitId + MilitaryStock (v54), AlarmChannels (v55, nested), and everything from the v55 case.
        50 => (s with { Version = 50, NextUnitId = null, History = null, MessageLog = null, Demographics = null }),

        // After v45: DifficultyLevel + ResourceQuantities + PendingMonarchDemand (v46), Improvements + RefEntryTile
        // (v47), then all the later top-level fields too.
        45 => (s with
        {
            Version = 45,
            DifficultyLevel = null,
            ResourceQuantities = null,
            PendingMonarchDemand = null,
            Improvements = null,
            RefEntryTile = null,
            NextUnitId = null,
            History = null,
            MessageLog = null,
            Demographics = null,
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unhandled down-version target."),
    };

    /// <summary>Absolute path to a committed save fixture (copied next to the test assembly by the csproj).</summary>
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Persistence", "fixtures", name);
}
