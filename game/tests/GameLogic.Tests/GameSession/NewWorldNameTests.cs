using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The human is prompted to <b>name the New World</b> on first landfall (`86d3fq1fn` — Col1 names the new continent on
/// first landing; FreeCol <c>ServerUnit.csMove</c>'s <c>firstLanding = !owner.isNewLandNamed()</c> land-tile check +
/// <c>Player.newLandName</c>). The first time a human land unit steps ashore <see cref="Game.HasMadeFirstLandfall"/>
/// flips (once, ever), opening a one-shot naming window (<see cref="Game.NewWorldNamePending"/>); answering it via
/// <see cref="Game.NameNewWorld"/> stores the name (a blank falls back to <see cref="Game.DefaultNewWorldName"/>). The
/// name persists at save v66 (omit-when-unset); pre-v66 saves load unnamed. RNG-free / deterministic (ADR-009).
/// </summary>
public class NewWorldNameTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Colonist = "model.unit.freeColonist";

    /// <summary>
    /// A 4×1 row — ocean at (0,0), then plains — with a human free colonist standing on the ocean tile (a fresh
    /// landing party that has not yet stepped ashore) holding a full move allowance and the whole row explored (so the
    /// move is a plain terrain step, not an unexplored-ends-turn step).
    /// </summary>
    private static Game CoastRow() => new SaveGame
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 4, MapHeight = 1,
        Terrain = ["model.tile.ocean", "model.tile.plains", "model.tile.plains", "model.tile.plains"],
        Units = [new SavedUnit(1, Colonist, 0, 0, 12)],
        Explored = [0, 1, 2, 3],
    }.Restore(Classic);

    private static Unit TheUnit(Game game) => game.Units.First(u => u.Id == 1);

    // ── First landfall fires the one-shot prompt ──────────────────────────────────────────────────────────────

    [Fact]
    public void NewGame_HasNotMadeLandfall_AndIsUnnamed()
    {
        var game = Game.New(Classic, seed: 7);
        Assert.False(game.HasMadeFirstLandfall);
        Assert.False(game.NewWorldNamePending);
        Assert.Null(game.NewWorldName);
    }

    [Fact]
    public void SteppingAshore_SetsTheLandfallFlag_AndOpensTheNamePrompt()
    {
        Game game = CoastRow();
        Assert.False(game.HasMadeFirstLandfall); // standing on the ocean tile is not a landfall

        game.MoveUnit(TheUnit(game), new Position(1, 0)); // step onto land

        Assert.True(game.HasMadeFirstLandfall);
        Assert.True(game.NewWorldNamePending); // landed but not yet named → the prompt is owed
        Assert.Null(game.NewWorldName);
    }

    [Fact]
    public void LandfallFiresExactlyOnce_EvenAcrossManyLandMoves()
    {
        Game game = CoastRow();
        Unit unit = TheUnit(game);
        game.MoveUnit(unit, new Position(1, 0)); // first landfall
        game.NameNewWorld("Vinland");            // answer the prompt

        game.MoveUnit(unit, new Position(2, 0)); // a later land move
        game.MoveUnit(unit, new Position(3, 0)); // and another

        Assert.Equal("Vinland", game.NewWorldName); // unchanged — the prompt never re-fires
        Assert.False(game.NewWorldNamePending);
    }

    [Fact]
    public void ShipMovingOnWater_DoesNotCountAsLandfall()
    {
        // A naval unit roaming the ocean never makes "landfall" — only a land unit stepping onto land does.
        var game = new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 3, MapHeight = 1,
            Terrain = ["model.tile.ocean", "model.tile.ocean", "model.tile.plains"],
            Units = [new SavedUnit(1, "model.unit.caravel", 0, 0, 12)],
            Explored = [0, 1, 2],
        }.Restore(Classic);

        game.MoveUnit(game.Units.First(u => u.Id == 1), new Position(1, 0)); // ocean → ocean

        Assert.False(game.HasMadeFirstLandfall);
    }

    // ── Naming ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NameNewWorld_StoresTheTypedName_AndClosesThePrompt()
    {
        Game game = CoastRow();
        game.MoveUnit(TheUnit(game), new Position(1, 0));

        game.NameNewWorld("  Avalon  "); // trims surrounding whitespace

        Assert.Equal("Avalon", game.NewWorldName);
        Assert.False(game.NewWorldNamePending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NameNewWorld_BlankFallsBackToTheDefault(string? blank)
    {
        Game game = CoastRow();
        game.MoveUnit(TheUnit(game), new Position(1, 0));

        game.NameNewWorld(blank);

        Assert.Equal(Game.DefaultNewWorldName, game.NewWorldName);
        Assert.False(game.NewWorldNamePending);
    }

    [Fact]
    public void NameNewWorld_IsOneShot_ASecondCallIsIgnored()
    {
        Game game = CoastRow();
        game.MoveUnit(TheUnit(game), new Position(1, 0));
        game.NameNewWorld("First");

        game.NameNewWorld("Second"); // ignored — the name is one-shot

        Assert.Equal("First", game.NewWorldName);
    }

    // ── Determinism (ADR-009): naming must not perturb the random stream ───────────────────────────────────────

    [Fact]
    public void Landfall_AndNaming_DoNotPerturbTheRandomStream()
    {
        Game named = CoastRow();
        Game bare = CoastRow();
        named.MoveUnit(TheUnit(named), new Position(1, 0));
        bare.MoveUnit(TheUnit(bare), new Position(1, 0));
        named.NameNewWorld("Atlantis"); // only the named game answers the prompt

        Assert.Equal(bare.RandomState, named.RandomState); // naming drew nothing — same future sequence
    }

    // ── Persistence (save v66): the name survives save/load; unset omits; pre-v66 loads null ──────────────────

    [Fact]
    public void NewWorldName_SurvivesSaveLoad()
    {
        Game game = CoastRow();
        game.MoveUnit(TheUnit(game), new Position(1, 0));
        game.NameNewWorld("Hesperia");

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal("Hesperia", loaded.NewWorldName);
        Assert.True(loaded.HasMadeFirstLandfall); // a named world is, by definition, landed-on → never re-prompts
        Assert.False(loaded.NewWorldNamePending);
    }

    [Fact]
    public void UnnamedWorld_IsOmittedFromTheSave_AndRoundTripsByteIdentical()
    {
        // Omit-when-unset: a game that has not named the world writes no NewWorldName token, so the save stays
        // byte-identical to v65 (the new field absent).
        var game = Game.New(Classic, seed: 7);
        Assert.Null(game.NewWorldName);

        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("\"NewWorldName\"", json); // WhenWritingNull → the field is absent

        Game loaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Null(loaded.NewWorldName);
        Assert.False(loaded.HasMadeFirstLandfall);
        // Byte-identical round-trip of an unnamed game.
        Assert.Equal(json, SaveGame.From(loaded).ToJson());
    }

    [Fact]
    public void NamedWorld_RoundTripsByteIdentical()
    {
        Game game = CoastRow();
        game.MoveUnit(TheUnit(game), new Position(1, 0));
        game.NameNewWorld("Cathay");

        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"NewWorldName\"", json); // a named world writes the field
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    [Fact]
    public void PreV66Save_LoadsTheWorldUnnamed()
    {
        // A v65 (pre-v66) save carries no NewWorldName: it loads unnamed, and the prompt fires naturally on the next
        // first landfall exactly as before this feature.
        Game game = CoastRow();
        game.MoveUnit(TheUnit(game), new Position(1, 0));
        game.NameNewWorld("WouldBeLost");

        // Stamp the save back to v65 and drop the field, mimicking a real old-format save.
        SaveGame downVersioned = SaveGame.From(game) with { Version = 65, NewWorldName = null };
        Game loaded = SaveGame.FromJson(downVersioned.ToJson()).Restore(Classic);

        Assert.Null(loaded.NewWorldName);
        Assert.False(loaded.HasMadeFirstLandfall); // re-prompts on the next landfall
    }

    [Fact]
    public void SaveVersion_IsCurrent() => Assert.Equal(70, SaveGame.CurrentVersion);
}
