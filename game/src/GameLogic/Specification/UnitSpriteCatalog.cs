using System.Collections.Generic;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The engine-free rule for picking a unit's on-map sprite from its ruleset type + role short names. FreeCol resolves
/// a unit's image by <em>type and role</em> — a colonist in the soldier role looks different from a plain one, and a
/// native <c>brave</c> in the <c>mountedBrave</c> role draws mounted — so this returns the candidate
/// <c>res://</c> paths to try <strong>in priority order</strong>. Holding the mapping here (rather than inside the
/// Godot <see cref="!:UnitMarker"/> node) keeps it plain data the L1 suite can assert without booting the engine:
/// the presentation layer just walks the list and loads the first path that exists, falling back to a drawn
/// red disc only when none do.
/// </summary>
/// <remarks>
/// Sprites are FreeCol's own GPL-v2 / CC-BY 4.0 unit art, copied unmodified into <c>game/assets/freecol/units/</c>
/// (see that folder's <c>PROVENANCE.md</c>). Base-type art sits flat at <c>units/&lt;type&gt;.png</c> (e.g.
/// <c>brave</c>, <c>indianConvert</c>, <c>nativeColonist</c>, <c>freeColonist</c>); role-equipped art keeps
/// FreeCol's per-role subfolders <c>units/&lt;role&gt;/&lt;type&gt;.png</c> (e.g. <c>armedBrave</c>,
/// <c>mountedBrave</c>, <c>nativeDragoon</c>, <c>soldier</c>, <c>dragoon</c>). The natives reuse the single
/// <c>brave</c> base type across every native role, so each native role folder holds a <c>brave.png</c>.
/// </remarks>
public static class UnitSpriteCatalog
{
    /// <summary>The directory (Godot <c>res://</c> path) holding every unit sprite.</summary>
    public const string UnitDir = "res://assets/freecol/units";

    /// <summary>The role short name carried by an unarmed/default unit — its art lives at the bare-type path.</summary>
    public const string DefaultRole = "default";

    /// <summary>
    /// The ordered list of candidate sprite paths for a unit of <paramref name="typeShortName"/> in role
    /// <paramref name="roleShortName"/>, most-specific first: role-specific (<c>units/{role}/{type}.png</c>) →
    /// generic-role (<c>units/{role}/{role}.png</c>) → bare-type (<c>units/{type}.png</c>). The presentation layer
    /// loads the first that exists; if none do it draws the red-disc fallback. A null/empty/<c>default</c> role
    /// contributes only the bare-type path (an unarmed brave → <c>units/brave.png</c>).
    /// </summary>
    /// <param name="typeShortName">The unit type's short name (e.g. <c>brave</c>, <c>indianConvert</c>, <c>freeColonist</c>).</param>
    /// <param name="roleShortName">The unit's role short name (e.g. <c>mountedBrave</c>, <c>soldier</c>, or <c>default</c> for unarmed).</param>
    public static IReadOnlyList<string> CandidatePaths(string typeShortName, string? roleShortName)
    {
        var paths = new List<string>(3);
        if (!string.IsNullOrEmpty(roleShortName) && roleShortName != DefaultRole)
        {
            paths.Add($"{UnitDir}/{roleShortName}/{typeShortName}.png"); // role-specific (units/mountedBrave/brave.png)
            paths.Add($"{UnitDir}/{roleShortName}/{roleShortName}.png"); // generic role  (units/soldier/soldier.png)
        }

        paths.Add($"{UnitDir}/{typeShortName}.png");                     // bare type     (units/brave.png)
        return paths;
    }
}
