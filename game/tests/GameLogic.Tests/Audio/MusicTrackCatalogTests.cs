using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Audio;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Audio;

/// <summary>
/// Verifies the engine-free music-context → track mapping (<see cref="MusicTrackCatalog"/>). Audio <em>playback</em>
/// can't be exercised headlessly, but the mapping is plain data: every context resolves to a non-empty list of
/// well-formed, unique <c>res://</c> <c>.ogg</c> paths under the music directory, and every shipped European nation
/// resolves to an anthem under the anthem directory. This is the unit-level guard that catches a missing or malformed
/// music cue.
/// </summary>
public class MusicTrackCatalogTests
{
    [Fact]
    public void EveryContext_HasANonEmptyPlaylist()
    {
        foreach (MusicContext ctx in Enum.GetValues<MusicContext>())
        {
            Assert.True(MusicTrackCatalog.TryGetPlaylist(ctx, out IReadOnlyList<string> playlist),
                $"No playlist mapped for {ctx}.");
            Assert.NotEmpty(playlist);
            Assert.All(playlist, p => Assert.False(string.IsNullOrWhiteSpace(p), $"Empty track path in {ctx}."));
        }
    }

    [Fact]
    public void Catalog_CoversEveryContext_NoExtras()
    {
        var mapped = MusicTrackCatalog.All.Keys.ToHashSet();
        var declared = Enum.GetValues<MusicContext>().ToHashSet();
        Assert.Equal(declared, mapped);
    }

    [Fact]
    public void EveryContextsTracks_AreOggFilesUnderTheMusicDirectory_AndUnique()
    {
        foreach (MusicContext ctx in Enum.GetValues<MusicContext>())
        {
            IReadOnlyList<string> playlist = MusicTrackCatalog.Playlist(ctx);
            foreach (string path in playlist)
            {
                Assert.StartsWith(MusicTrackCatalog.MusicDir, path);
                Assert.EndsWith(".ogg", path);
            }
            Assert.Equal(playlist.Count, playlist.Distinct().Count()); // no duplicate tracks in a playlist
        }
    }

    [Fact]
    public void MenuAndInGamePeace_ShareTheIdenticalPlaylist()
    {
        // The seamless menu→game guarantee (86d3fq1wy): MusicService relabels instead of restarting only because the
        // two contexts resolve to the SAME track sequence (FreeCol's single shared background bed).
        Assert.Equal(
            MusicTrackCatalog.Playlist(MusicContext.Menu),
            MusicTrackCatalog.Playlist(MusicContext.InGamePeace));
    }

    [Fact]
    public void WarPlaylist_DiffersFromPeace_AndReusesOnlyExistingTracks()
    {
        // The audible-switch guard: war must SOUND different (a different sequence than peace), and — no new audio
        // assets this wave — every war track must already ship in the shared background playlist (interim subset;
        // the dedicated war track is a recorded asset gap).
        IReadOnlyList<string> war = MusicTrackCatalog.Playlist(MusicContext.InGameWar);
        IReadOnlyList<string> peace = MusicTrackCatalog.Playlist(MusicContext.InGamePeace);
        Assert.NotEmpty(war);
        Assert.False(war.SequenceEqual(peace), "War must not resolve to the same playlist as peace (no audible switch).");
        Assert.All(war, track => Assert.Contains(track, peace)); // interim: only existing, already-licensed tracks
    }

    [Fact]
    public void MusicAndAnthemDirs_AreGodotResourcePaths()
    {
        Assert.StartsWith("res://", MusicTrackCatalog.MusicDir);
        Assert.EndsWith("/", MusicTrackCatalog.MusicDir);
        Assert.StartsWith(MusicTrackCatalog.MusicDir, MusicTrackCatalog.AnthemDir);
        Assert.EndsWith("/", MusicTrackCatalog.AnthemDir);
    }

    [Theory]
    [InlineData("model.nation.dutch", "dutch.ogg")]
    [InlineData("model.nation.english", "english.ogg")]
    [InlineData("model.nation.french", "french.ogg")]
    [InlineData("model.nation.spanish", "spanish.ogg")]
    public void EuropeanNations_ResolveToTheExpectedAnthem(string nationId, string fileName)
    {
        Assert.True(MusicTrackCatalog.TryGetAnthem(nationId, out string path), $"No anthem for {nationId}.");
        Assert.Equal(MusicTrackCatalog.AnthemDir + fileName, path);
    }

    [Fact]
    public void EveryAnthem_IsAnOggUnderTheAnthemDirectory()
    {
        foreach (KeyValuePair<string, string> entry in MusicTrackCatalog.AnthemsByNation)
        {
            Assert.StartsWith("model.nation.", entry.Key);
            Assert.StartsWith(MusicTrackCatalog.AnthemDir, entry.Value);
            Assert.EndsWith(".ogg", entry.Value);
        }
    }

    [Fact]
    public void ShippedAnthemNations_AreTheEightEuropeanPowers()
    {
        // The 8 European powers FreeCol ships an anthem for (sound.anthem.model.nation.<x>).
        var expected = new HashSet<string>
        {
            "model.nation.dutch", "model.nation.english", "model.nation.french", "model.nation.spanish",
            "model.nation.danish", "model.nation.portuguese", "model.nation.russian", "model.nation.swedish",
        };
        Assert.Equal(expected, MusicTrackCatalog.AnthemsByNation.Keys.ToHashSet());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("model.nation.dutchREF")]   // the REF has no anthem
    [InlineData("model.nationType.apache")] // natives have no anthem
    [InlineData("model.nation.unknownEnemy")]
    public void NationsWithoutAnAnthem_ResolveToFalse(string? nationId)
    {
        Assert.False(MusicTrackCatalog.TryGetAnthem(nationId, out string path));
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void TryGetPlaylist_ReturnsFalseForAnUnmappedContext()
    {
        // An out-of-range cast simulates a future context member someone forgot to add to the catalog.
        var bogus = (MusicContext)999;
        Assert.False(MusicTrackCatalog.TryGetPlaylist(bogus, out _));
        Assert.Throws<KeyNotFoundException>(() => MusicTrackCatalog.Playlist(bogus));
    }
}
