using CrownAndColony.GameLogic.Units;

namespace CrownAndColony.GameLogic.GameSession;

public sealed partial class Game
{
    /// <summary>
    /// The fixed default name offered for the New World on first landing (Sid Meier's Colonization / FreeCol's
    /// <c>newLandName</c> default). A constant string — <b>RNG-free</b> by design: the prompt's suggested name must not
    /// draw the seeded stream mid-turn, or it would shift the human's economy sequence and break the byte-stability
    /// invariant (ADR-009). The player may type any name over it.
    /// </summary>
    public const string DefaultNewWorldName = "The New World";

    // The human's chosen name for the new world (FreeCol Player.newLandName). Null until the human's first landfall is
    // answered. Persisted from save v66 (SaveGame.NewWorldName, omit-when-null), so a named world survives reload; a
    // game that never named it (or a pre-v66 save) loads null. RNG-free.
    private string? _newWorldName;

    // Whether the human's first landfall has happened yet (so the name prompt fires exactly once, ever). FreeCol gates
    // on Player.isNewLandNamed(); we keep an explicit flag because our default name is only applied when the player
    // confirms (we surface the prompt rather than auto-naming), so "named" and "has landed" are distinct until answered.
    // Set true the instant a human land unit first steps ashore; never reset. NOT separately persisted — it is implied by
    // the presence of NewWorldName once answered, and a save taken in the one-turn gap before the human names it simply
    // re-prompts on load (harmless: the prompt is idempotent and one-shot thereafter). See HasMadeFirstLandfall.
    private bool _hasMadeFirstLandfall;

    /// <summary>
    /// The human's chosen name for the New World, or <c>null</c> if it has not yet been named (FreeCol
    /// <c>Player.getNewLandName</c>). <b>Persisted</b> from save v66 (omit-when-null), so a named world survives a
    /// save/load round-trip; a game that never named it loads null. Read-only for presentation (ADR-006); set via
    /// <see cref="NameNewWorld"/>.
    /// </summary>
    public string? NewWorldName => _newWorldName;

    /// <summary>
    /// Whether the human has stepped ashore on the New World for the first time yet — the one-shot gate for the
    /// "name the new world" prompt (FreeCol <c>Player.isNewLandNamed</c>'s landfall trigger). Flips <c>true</c> the
    /// instant a human-owned land unit first occupies a land tile and never resets, so the prompt fires exactly once
    /// per game. Read by presentation after a move to decide whether to open the naming dialog; once it is true and
    /// <see cref="NewWorldName"/> is set, the prompt is done for good.
    /// </summary>
    public bool HasMadeFirstLandfall => _hasMadeFirstLandfall;

    /// <summary>
    /// Whether the human still owes a "name the new world" prompt — it has made first landfall but not yet named the
    /// world (FreeCol surfaces the naming dialog while <c>newLandName == null</c> after the first landing). True for the
    /// single window between the first landfall and the human answering the prompt; the presentation polls this after a
    /// move to know when to open the naming dialog. RNG-free.
    /// </summary>
    public bool NewWorldNamePending => _hasMadeFirstLandfall && _newWorldName is null;

    /// <summary>
    /// Records that a human-owned land unit has stepped ashore — the one-time first-landfall event that triggers the
    /// "name the new world" prompt (FreeCol <c>ServerUnit.csMove</c>'s <c>firstLanding = !owner.isNewLandNamed()</c>
    /// land-tile check). Idempotent: it sets <see cref="HasMadeFirstLandfall"/> once and is a no-op forever after, so it
    /// can be called from every land move cheaply. Called by <see cref="MoveUnit"/> when a human, non-naval unit enters a
    /// land tile. <b>RNG-free</b> — flipping a flag draws no randomness (ADR-009).
    /// </summary>
    /// <param name="unit">The unit that just moved; the landfall registers only for a human-owned land unit on a land tile.</param>
    private void NoteFirstLandfall(Unit unit)
    {
        if (_hasMadeFirstLandfall || unit.Type.IsNaval || !IsHumanOwned(unit) || !unit.IsOnMap)
        {
            return; // already landed, a ship (sea exploration is not a landfall), not the human's, or not on the map
        }
        if (Map.TerrainAt(unit.Position).IsWater)
        {
            return; // still on a water tile (a coastal high-seas re-entry) — landfall is stepping onto LAND
        }
        _hasMadeFirstLandfall = true;
    }

    /// <summary>
    /// Sets the human's chosen name for the New World — the answer to the first-landfall naming prompt (FreeCol
    /// <c>setNewLandName</c>). One-shot: it only takes once (the first non-empty name sticks); a blank/whitespace name
    /// falls back to <see cref="DefaultNewWorldName"/>, so the world is always named after the prompt is answered and the
    /// prompt never re-fires. Idempotent if called again (a later call is ignored once a name is set), keeping the name
    /// stable across an accidental re-prompt after a save taken mid-prompt. <b>Persisted</b> at save v66. RNG-free.
    /// </summary>
    /// <param name="name">The player's typed name; empty/whitespace ⇒ the default name.</param>
    public void NameNewWorld(string? name)
    {
        if (_newWorldName is not null)
        {
            return; // already named — the name is one-shot (a re-prompt after a mid-prompt save load is a no-op)
        }
        _hasMadeFirstLandfall = true; // naming implies the landfall happened (covers a debug/forced name with no move)
        _newWorldName = string.IsNullOrWhiteSpace(name) ? DefaultNewWorldName : name.Trim();
    }

    /// <summary>
    /// Re-applies the persisted New-World name to a freshly <see cref="Restore">restored</see> game (save v66+). Called
    /// once by <see cref="Persistence.SaveGame.Restore"/>; a pre-v66 / never-named save passes <c>null</c>, leaving the
    /// world unnamed and the landfall flag false exactly as before persistence (it re-prompts naturally on the next
    /// landfall — harmless and one-shot). A non-null name marks the world named <b>and</b> landed, so a reloaded named
    /// game never re-prompts.
    /// </summary>
    /// <param name="name">The saved New-World name, or null if the save never named it.</param>
    internal void RestoreNewWorldName(string? name)
    {
        if (name is null)
        {
            return; // pre-v66 / unnamed → leave unset; the prompt fires on the next first landfall
        }
        _newWorldName = name;
        _hasMadeFirstLandfall = true; // a named world has, by definition, been landed on — never re-prompt
    }
}
