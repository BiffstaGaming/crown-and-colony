using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Audio;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Audio;

/// <summary>
/// Verifies the engine-free sound-event → clip mapping (<see cref="SoundEventCatalog"/>). Audio <em>playback</em> can't
/// be exercised headlessly, but the mapping is plain data: every event must resolve to a well-formed, unique
/// <c>res://</c> path under the SFX directory. This is the unit-level guard that catches a missing or malformed cue.
/// </summary>
public class SoundEventCatalogTests
{
    [Fact]
    public void EverySoundEvent_HasAClip()
    {
        foreach (SoundEvent evt in Enum.GetValues<SoundEvent>())
        {
            Assert.True(SoundEventCatalog.TryGetClipPath(evt, out string path),
                $"No clip mapped for {evt}.");
            Assert.False(string.IsNullOrWhiteSpace(path), $"Clip path for {evt} is empty.");
        }
    }

    [Fact]
    public void AllClipPaths_AreOggFilesUnderTheSoundDirectory()
    {
        foreach (KeyValuePair<SoundEvent, string> entry in SoundEventCatalog.All)
        {
            Assert.StartsWith(SoundEventCatalog.SoundDir, entry.Value);
            Assert.EndsWith(".ogg", entry.Value);
        }
    }

    [Fact]
    public void SoundDir_IsAGodotResourcePath()
    {
        Assert.StartsWith("res://", SoundEventCatalog.SoundDir);
        Assert.EndsWith("/", SoundEventCatalog.SoundDir);
    }

    [Fact]
    public void Catalog_CoversEveryEnumValue_NoExtras()
    {
        var mapped = SoundEventCatalog.All.Keys.ToHashSet();
        var declared = Enum.GetValues<SoundEvent>().ToHashSet();
        Assert.Equal(declared, mapped);
    }

    [Theory]
    [InlineData(SoundEvent.ColonyFounded, "colony.ogg")]
    [InlineData(SoundEvent.BuildingComplete, "building.ogg")]
    [InlineData(SoundEvent.Combat, "attack.ogg")]
    [InlineData(SoundEvent.ShipSunk, "sunk.ogg")]
    [InlineData(SoundEvent.IllegalMove, "illegal.ogg")]
    public void KeyEvents_MapToTheExpectedClip(SoundEvent evt, string fileName)
    {
        Assert.Equal(SoundEventCatalog.SoundDir + fileName, SoundEventCatalog.ClipPath(evt));
    }

    [Fact]
    public void ClipPath_ThrowsForAnUnmappedValue()
    {
        // An out-of-range cast simulates a future enum member someone forgot to add to the catalog.
        var bogus = (SoundEvent)999;
        Assert.False(SoundEventCatalog.TryGetClipPath(bogus, out _));
        Assert.Throws<KeyNotFoundException>(() => SoundEventCatalog.ClipPath(bogus));
    }
}
