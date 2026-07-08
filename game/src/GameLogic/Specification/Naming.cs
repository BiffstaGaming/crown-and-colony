using System.Collections.Generic;
using System.Linq;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// Turns a ruleset id's camelCase short name into a human-friendly display name for the UI — e.g.
/// <c>freeColonist</c> → <c>Free Colonist</c>, <c>tradeGoods</c> → <c>Trade Goods</c>, <c>food</c> → <c>Food</c>.
/// A later full localisation pass (<c>86d3fq1w6</c>) will replace this with FreeCol's translated name strings; until
/// then this is the single shared humaniser every display site uses (the panels' former private copies all forward
/// here), so no raw camelCase id leaks into the UI and display overrides apply uniformly.
/// </summary>
public static class Naming
{
    /// <summary>
    /// Display-name overrides for the few short names whose literal camelCase split is meaningless to a player.
    /// FreeCol's <c>model.building.country</c> is the base horse pasture every colony starts with — "Country" tells
    /// the player nothing, so it displays as <b>Pasture</b> (Chris 2026-07-08). The ruleset <em>id</em> is untouched
    /// (data fidelity — saves, specs and the FreeCol XML all still say <c>country</c>); only the display changes.
    /// Applies uniformly at every display site (colony screen, Colopedia, reports) since all go through here.
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new()
    {
        ["country"] = "Pasture",
        // The Australian Pioneer whose Scots patronymic the camelCase split would mangle ("Mc Douall").
        ["johnMcDouallStuart"] = "John McDouall Stuart",
    };

    /// <summary>The human-friendly form of a camelCase short name (splits on camelCase boundaries, capitalises each word), with the odd <see cref="Overrides"/> entry for names whose literal split would mislead.</summary>
    public static string Humanize(string shortName) => Humanize(shortName, null);

    /// <summary>
    /// The human-friendly form of a camelCase short name, honouring a <b>variant-scoped</b> display override first
    /// (e.g. the Australian Federation shows <c>model.goods.bells</c> as "Civic Voice" and <c>cotton</c> as "Wool"
    /// while the id — the transposability anchor the engine keys on — is untouched, ADR-018). The lookup order is
    /// <paramref name="variantOverrides"/> → the global <see cref="Overrides"/> → the camelCase split, so a variant
    /// relabels only what it names and everything else humanises as before. Classic passes no overrides, so its text
    /// is byte-identical. This is the display-layer seam the wider localisation pass (<c>86d3fq1w6</c>) will build on.
    /// </summary>
    /// <param name="shortName">The ruleset short name / id suffix to display (e.g. <c>cotton</c>, <c>expertFarmer</c>).</param>
    /// <param name="variantOverrides">The active variant's display overrides keyed by short name, or <c>null</c>/empty for none (classic).</param>
    public static string Humanize(string shortName, IReadOnlyDictionary<string, string>? variantOverrides)
    {
        if (string.IsNullOrEmpty(shortName))
        {
            return shortName;
        }
        if (variantOverrides is not null && variantOverrides.TryGetValue(shortName, out string? variantLabel))
        {
            return variantLabel;
        }
        if (Overrides.TryGetValue(shortName, out string? overridden))
        {
            return overridden;
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
