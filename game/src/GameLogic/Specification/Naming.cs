using System.Collections.Generic;
using System.Linq;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// Turns a ruleset id's camelCase short name into a human-friendly display name for the UI — e.g.
/// <c>freeColonist</c> → <c>Free Colonist</c>, <c>tradeGoods</c> → <c>Trade Goods</c>, <c>food</c> → <c>Food</c>.
/// A later full localisation pass (<c>86d3fq1w6</c>) will replace this with FreeCol's translated name strings; until
/// then this is the single shared humaniser every display site uses, so no raw camelCase id leaks into the UI. Types
/// expose it as <c>DisplayName</c> (over their <c>ShortName</c>); call <see cref="Humanize"/> directly for a bare id.
/// </summary>
public static class Naming
{
    /// <summary>The human-friendly form of a camelCase short name (splits on camelCase boundaries, capitalises each word).</summary>
    public static string Humanize(string shortName)
    {
        if (string.IsNullOrEmpty(shortName))
        {
            return shortName;
        }
        var words = new List<string>();
        int start = 0;
        for (int i = 1; i < shortName.Length; i++)
        {
            if (char.IsUpper(shortName[i]) && !char.IsUpper(shortName[i - 1]))
            {
                words.Add(shortName[start..i]);
                start = i;
            }
        }
        words.Add(shortName[start..]);
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
