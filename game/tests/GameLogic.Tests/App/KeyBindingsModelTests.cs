using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.App;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.App;

/// <summary>
/// L1 coverage for the engine-free <see cref="KeyBindingsModel"/>: the action list, the default chords, the
/// override semantics (Set/Reset, default-equals-clears) and the persistence round-trip (overrides only).
/// </summary>
public class KeyBindingsModelTests
{
    [Fact]
    public void Defaults_NoOverrides_ChordIsTheShippedDefault()
    {
        var m = new KeyBindingsModel();

        Assert.Empty(m.OverriddenActions);
        // A sample of the canonical defaults (mirrors project.godot [input]).
        Assert.Equal(new KeyBindingsModel.KeyChord(4194309, false), m.ChordFor("end_turn"));   // Enter
        Assert.Equal(new KeyBindingsModel.KeyChord(32, false), m.ChordFor("skip_unit"));        // Space
        Assert.Equal(new KeyBindingsModel.KeyChord(67, true), m.ChordFor("center_unit"));       // Ctrl+C
        Assert.False(m.HasOverride("end_turn"));
    }

    [Fact]
    public void EveryActionId_IsUnique_AndHasADefault()
    {
        var ids = KeyBindingsModel.Actions.Select(a => a.ActionId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count()); // no duplicate ids
        foreach (KeyBindingsModel.ActionDef def in KeyBindingsModel.Actions)
        {
            Assert.Equal(def.DefaultChord, KeyBindingsModel.DefaultFor(def.ActionId));
        }
    }

    [Fact]
    public void Set_ToANewChord_RecordsAnOverride()
    {
        var m = new KeyBindingsModel();
        var chord = new KeyBindingsModel.KeyChord(84, false); // T

        m.Set("end_turn", chord);

        Assert.True(m.HasOverride("end_turn"));
        Assert.Equal(chord, m.ChordFor("end_turn"));
        Assert.Contains("end_turn", m.OverriddenActions);
    }

    [Fact]
    public void Set_BackToTheDefault_ClearsTheOverride()
    {
        var m = new KeyBindingsModel();
        m.Set("skip_unit", new KeyBindingsModel.KeyChord(84, false)); // override to T
        Assert.True(m.HasOverride("skip_unit"));

        m.Set("skip_unit", KeyBindingsModel.DefaultFor("skip_unit")); // back to Space

        Assert.False(m.HasOverride("skip_unit"));
        Assert.Empty(m.OverriddenActions);
    }

    [Fact]
    public void Reset_AndResetAll_ClearOverrides()
    {
        var m = new KeyBindingsModel();
        m.Set("end_turn", new KeyBindingsModel.KeyChord(84, false));
        m.Set("skip_unit", new KeyBindingsModel.KeyChord(85, false));

        m.Reset("end_turn");
        Assert.False(m.HasOverride("end_turn"));
        Assert.True(m.HasOverride("skip_unit"));

        m.ResetAll();
        Assert.Empty(m.OverriddenActions);
    }

    [Fact]
    public void Set_UnknownAction_IsIgnored()
    {
        var m = new KeyBindingsModel();
        m.Set("not_an_action", new KeyBindingsModel.KeyChord(84, false));
        Assert.Empty(m.OverriddenActions);
    }

    [Fact]
    public void ToDictionary_SerializesOverridesOnly()
    {
        var m = new KeyBindingsModel();
        Assert.Empty(m.ToDictionary()); // no overrides → nothing persisted

        m.Set("save_game", new KeyBindingsModel.KeyChord(75, true)); // Ctrl+K
        IReadOnlyDictionary<string, string> d = m.ToDictionary();

        Assert.Single(d);
        Assert.Equal("75,ctrl", d["save_game"]);
    }

    [Fact]
    public void RoundTrip_ThroughDictionary_PreservesOverrides()
    {
        var m = new KeyBindingsModel();
        var endTurn = new KeyBindingsModel.KeyChord(84, false);  // T, no ctrl
        var center = new KeyBindingsModel.KeyChord(90, true);    // Ctrl+Z
        m.Set("end_turn", endTurn);
        m.Set("center_unit", center);

        KeyBindingsModel restored = KeyBindingsModel.FromDictionary(m.ToDictionary());

        Assert.Equal(endTurn, restored.ChordFor("end_turn"));
        Assert.Equal(center, restored.ChordFor("center_unit"));
        Assert.Equal(2, restored.OverriddenActions.Count);
    }

    [Fact]
    public void FromDictionary_DropsUnknownAndGarbageEntries()
    {
        var data = new Dictionary<string, string>
        {
            ["end_turn"] = "84",            // valid override → kept
            ["not_an_action"] = "84",       // unknown id → dropped
            ["skip_unit"] = "not-a-number", // garbage value → dropped
        };

        KeyBindingsModel m = KeyBindingsModel.FromDictionary(data);

        Assert.Equal(new KeyBindingsModel.KeyChord(84, false), m.ChordFor("end_turn"));
        Assert.True(m.HasOverride("end_turn"));
        Assert.False(m.HasOverride("skip_unit")); // garbage ignored → still the default
        Assert.Single(m.OverriddenActions);
    }

    [Fact]
    public void FromDictionary_OverrideEqualToDefault_StaysADefault()
    {
        // Persisting a value that happens to equal the default should not count as an override after load.
        var data = new Dictionary<string, string> { ["skip_unit"] = "32" }; // Space = the default
        KeyBindingsModel m = KeyBindingsModel.FromDictionary(data);
        Assert.False(m.HasOverride("skip_unit"));
        Assert.Empty(m.OverriddenActions);
    }
}
