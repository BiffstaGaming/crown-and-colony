using System.Linq;
using CrownAndColony.GameLogic.Audio;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Audio;

/// <summary>
/// The pure game-state → <see cref="MusicContext"/> rule (<see cref="MusicContextSelector"/>, 86d3fq1wy): no game →
/// <see cref="MusicContext.Menu"/>; war with any <b>non-native</b> power (a colonial rival, or the REF between the
/// declaration and independence) → <see cref="MusicContext.InGameWar"/>; everything else (fresh game, cease-fire,
/// peace, alliance, native-only war) → <see cref="MusicContext.InGamePeace"/>. RNG-free and read-only (ADR-009), so
/// these tests never advance a turn.
/// </summary>
public class MusicContextSelectorTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>The first non-human colonial rival (a classic game always spawns three).</summary>
    private static Player Rival(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    [Fact]
    public void NoGame_IsTheMenuContext()
    {
        Assert.Equal(MusicContext.Menu, MusicContextSelector.For(null));
    }

    [Fact]
    public void FreshGame_IsInGamePeace()
    {
        // A new classic game: rivals are Uncontacted (absent from the stance map) — not war.
        Game game = Game.New(Classic, Seed);
        Assert.Equal(MusicContext.InGamePeace, MusicContextSelector.For(game));
    }

    [Fact]
    public void WarWithAColonialRival_IsInGameWar()
    {
        Game game = Game.New(Classic, Seed);
        game.SetStance(game.HumanPlayer.PlayerId, Rival(game).PlayerId, Stance.War);
        Assert.Equal(MusicContext.InGameWar, MusicContextSelector.For(game));
    }

    [Theory]
    [InlineData(Stance.Uncontacted)]
    [InlineData(Stance.Peace)]
    [InlineData(Stance.CeaseFire)]
    [InlineData(Stance.Alliance)]
    public void EveryNonWarStance_IsInGamePeace(Stance stance)
    {
        // Only War flips the bed — a cooling war (CeaseFire) already sounds like peace, matching the report wording.
        Game game = Game.New(Classic, Seed);
        game.HumanPlayer.StanceMap[Rival(game).PlayerId] = stance; // direct write: pins the raw stance→context mapping
        Assert.Equal(MusicContext.InGamePeace, MusicContextSelector.For(game));
    }

    [Fact]
    public void WarWithANativeNationOnly_StaysInGamePeace()
    {
        // Deliberate exclusion (86d3fq1wy design): native wars are tracked in the transient native-stance channels
        // (Game.NativeStanceToward, from tribe alarm — 8e5e646), never in Player.Stances; and even a native-keyed War
        // entry written into the human's stance map (as here) must not cue the European-war bed. The PlayerType.Native
        // filter is the load-bearing guard this test pins down.
        Game game = Game.New(Classic, Seed);
        Player native = game.Players.First(p => p.PlayerType == PlayerType.Native);
        game.HumanPlayer.StanceMap[native.PlayerId] = Stance.War; // direct: SetStance is colonial-pair-only
        Assert.Equal(MusicContext.InGamePeace, MusicContextSelector.For(game));
    }

    [Fact]
    public void WarStanceTowardAnUnknownPlayerId_IsIgnored()
    {
        // A stance entry whose player no longer resolves (defensive: e.g. a hand-edited save) cannot flip the bed.
        Game game = Game.New(Classic, Seed);
        game.HumanPlayer.StanceMap[9999] = Stance.War;
        Assert.Equal(MusicContext.InGamePeace, MusicContextSelector.For(game));
    }

    [Fact]
    public void RefWar_IsInGameWar_AndIndependenceRestoresPeace_WhileTheRefPlayerRemains()
    {
        // The real REF lifecycle (Game.Independence): declaring writes mutual War with the REF → war music; winning
        // independence writes mutual Peace back → peace music. The REF player OBJECT outlives the war — which is why
        // the selector's predicate is the stance, never "a REF player exists".
        Game game = RebellionReady();
        game.DeclareIndependence(game.HumanPlayer);
        Player refPlayer = game.Players.First(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        Assert.Equal(Stance.War, game.HumanPlayer.Stances[refPlayer.PlayerId]); // the declaration set the war
        Assert.Equal(MusicContext.InGameWar, MusicContextSelector.For(game));

        game.GiveIndependence(refPlayer, game.HumanPlayer);
        Assert.Contains(game.Players, p => p.PlayerType == PlayerType.RoyalExpeditionaryForce); // REF outlives the war
        Assert.Equal(MusicContext.InGamePeace, MusicContextSelector.For(game));
    }

    [Fact]
    public void PeaceTreatyWithTheOnlyWarringRival_RestoresInGamePeace()
    {
        Game game = Game.New(Classic, Seed);
        Player rival = Rival(game);
        game.SetStance(game.HumanPlayer.PlayerId, rival.PlayerId, Stance.War);
        Assert.Equal(MusicContext.InGameWar, MusicContextSelector.For(game));

        game.SetStance(game.HumanPlayer.PlayerId, rival.PlayerId, Stance.Peace);
        Assert.Equal(MusicContext.InGamePeace, MusicContextSelector.For(game));
    }

    /// <summary>A game with one coastal colony at 100% Sons of Liberty — ready to declare (IndependenceTests pattern).</summary>
    private static Game RebellionReady()
    {
        Game game = Game.New(Classic, Seed);
        Position coastal = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), coastal);
        Colony colony = game.FoundColony(colonist);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // force national SoL to 100%
        return game;
    }
}
