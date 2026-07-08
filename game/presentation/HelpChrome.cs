using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.Presentation;

/// <summary>
/// The variant-scoped <b>chrome words</b> the instructional help prose (the <see cref="HelpPanel"/> body and the
/// <see cref="ColopediaConcepts"/> topics) weaves into its sentences, so the in-game guidance reads in the active
/// variant's language — classic's "Sons of Liberty / liberty bells / Continental Congress / Indian settlements /
/// royalists", the Australian Federation's "Federationists / Civic Voice / Federation Convention / First Nations
/// communities / Imperial Loyalists". A display-layer relabel only (ADR-018): none of these change a rule; they only
/// change what the reader is told.
/// <para>
/// Every field defaults to its <b>classic</b> string, so a help surface built with the parameterless
/// <see cref="Classic"/> (the pre-game main menu, and any caller that passes nothing) renders <b>byte-identical</b>
/// classic prose — the defaults are the exact original wording at each substitution site. For a <b>classic</b> game
/// <see cref="From(GameVariant)"/> returns <see cref="Classic"/> unchanged, so a classic game's help is byte-identical
/// too; only a renaming variant (Australia) gets substituted words. Threaded the same way
/// <c>DisplayOverrides</c>/<c>CongressName</c> already are: <see cref="GameController"/> passes
/// <see cref="From(GameVariant)"/> into the Colopedia / Help open calls, and the panels build their text from it.
/// </para>
/// <para>
/// The good the town hall produces surfaces in three grammatical forms, each its own field so the classic prose stays
/// exact: the running-prose <b>phrase</b> ("produce liberty bells" → <see cref="BellsPhrase"/>), the sentence-start /
/// link-label <b>good name</b> ("Bells are the engine" → <see cref="BellsGood"/>), and the short <b>title term</b>
/// ("Sons of Liberty &amp; bells" → <see cref="BellsShort"/>). Under a variant that renames the good all three collapse
/// to the single display name (Australia → "Civic Voice"). Likewise the indigenous words keep their <em>classic</em>
/// period literals ("Indian settlements", "tribe") as defaults, mapped to the variant's respectful chrome under Australia.
/// </para>
/// </summary>
/// <param name="RebelSentimentName">The pro-independence sentiment — classic "Sons of Liberty", Australia "Federationists".</param>
/// <param name="RebelCauseWord">The adjective for the pro-independence cause ("backs the rebel cause") — classic "rebel", Australia "Federationist".</param>
/// <param name="LibertyName">The word for the accruing political capital — classic "liberty", Australia "Civic Voice".</param>
/// <param name="BellsPhrase">The running-prose phrase for the town-hall good — classic "liberty bells", Australia "Civic Voice".</param>
/// <param name="BellsGood">The good's display name for sentence-start / link labels — classic "Bells", Australia "Civic Voice".</param>
/// <param name="BellsShort">The short title term for the good — classic "bells", Australia "Civic Voice".</param>
/// <param name="RoyalistsWord">The plural word for the pro-Crown faction in the penalty text — classic "royalists", Australia "Imperial Loyalists".</param>
/// <param name="CongressName">The father-electing body — classic "Continental Congress", Australia "Federation Convention".</param>
/// <param name="NativesCollective">The indigenous nations named collectively in the Help body ("Several native nations live on the map") — classic "native nations", Australia "First Nations".</param>
/// <param name="NativeRelationsTitle">The natives help-topic title — classic "Native relations", Australia "First Nations relations".</param>
/// <param name="NativeSettlementsPhrase">The plural opening phrase of the natives topic — classic "Indian settlements", Australia "First Nations communities".</param>
/// <param name="NativeSettlementSingular">A single indigenous settlement (learning-a-skill sentence) — classic "native settlement", Australia "First Nations community".</param>
/// <param name="NativeGroupWord">The word for one indigenous polity ("each tribe's mood") — classic "tribe", Australia "nation".</param>
/// <param name="ExpeditionaryForceName">The army the Crown sends to crush a bid — classic "Royal Expeditionary Force", Australia "Imperial Pressure".</param>
/// <param name="ExpeditionaryForceAbbrev">Its short form — classic "REF", Australia "Imperial Pressure".</param>
/// <param name="DeclareIndependenceName">The act that begins the independence sequence, phrased as a verb — classic "declare independence", Australia "put the Constitution to referendum".</param>
/// <param name="WarOfIndependenceName">The resulting struggle — classic "War of Independence", Australia "Federation Referendum".</param>
public readonly record struct HelpChrome(
    string RebelSentimentName = "Sons of Liberty",
    string RebelCauseWord = "rebel",
    string LibertyName = "liberty",
    string BellsPhrase = "liberty bells",
    string BellsGood = "Bells",
    string BellsShort = "bells",
    string RoyalistsWord = "royalists",
    string CongressName = "Continental Congress",
    string NativesCollective = "native nations",
    string NativeRelationsTitle = "Native relations",
    string NativeSettlementsPhrase = "Indian settlements",
    string NativeSettlementSingular = "native settlement",
    string NativeGroupWord = "tribe",
    string ExpeditionaryForceName = "Royal Expeditionary Force",
    string ExpeditionaryForceAbbrev = "REF",
    string DeclareIndependenceName = "declare independence",
    string WarOfIndependenceName = "War of Independence")
{
    /// <summary>
    /// The classic (default) chrome — every word its faithful Colonization string, so classic prose is byte-identical.
    /// <para><b>Why the one-argument call, not <c>new()</c>:</b> for a <c>record struct</c> the parameterless
    /// <c>new HelpChrome()</c> (and <c>default</c>) run the struct's zero-init constructor, which sets every string to
    /// <c>null</c> — it does <b>not</b> apply the primary constructor's declared defaults. Passing a single positional
    /// argument forces the <em>primary</em> constructor, which then fills every omitted field with its declared classic
    /// default; the one value we pass (<see cref="RebelSentimentName"/>) is that field's own default, so the whole
    /// struct is the classic wording. This keeps the 16 other defaults as the single source of truth for the classic
    /// strings.</para>
    /// </summary>
    public static readonly HelpChrome Classic = new(RebelSentimentName: "Sons of Liberty");

    /// <summary>
    /// The chrome for a variant: reads its display labels off the <see cref="GameVariant"/> (the same fields the panels
    /// already thread). A <b>classic</b> variant returns <see cref="Classic"/> unchanged, so classic help stays
    /// byte-identical; a renaming variant (Australia) substitutes its words. The town-hall good's three grammatical
    /// forms all collapse to its <see cref="GameVariant.DisplayOverrides"/> name when the variant renames <c>bells</c>
    /// (Australia → "Civic Voice"). The rebel-cause adjective and royalists word come off the variant's colonist /
    /// faction labels (lower-cased when a single word); the indigenous words derive from the variant's
    /// <see cref="GameVariant.NativesName"/> / <see cref="GameVariant.NativeSettlementName"/> respectful chrome.
    /// </summary>
    public static HelpChrome From(GameVariant variant)
    {
        // A classic variant carries the classic chrome defaults; returning Classic keeps its help byte-identical and
        // avoids reproducing the period literals ("Indian settlements", "tribe") from the variant's generic fields.
        if (variant.Id == GameVariants.ClassicAmerica.Id)
        {
            return Classic;
        }

        bool renamesBells = variant.DisplayOverrides.TryGetValue("bells", out string? bellsName);
        string bellsPhrase = renamesBells ? bellsName! : "liberty bells";
        string bellsGood = renamesBells ? bellsName! : "Bells";
        string bellsShort = renamesBells ? bellsName! : "bells";

        return new HelpChrome(
            RebelSentimentName: variant.RebelSentimentName,
            RebelCauseWord: LowerIfSingleWord(variant.RebelColonistName),
            LibertyName: variant.LibertyName,
            BellsPhrase: bellsPhrase,
            BellsGood: bellsGood,
            BellsShort: bellsShort,
            RoyalistsWord: LowerIfSingleWord(variant.LoyalistFactionName),
            CongressName: variant.CongressName,
            NativesCollective: variant.NativesName,
            NativeRelationsTitle: $"{variant.NativesName} relations",
            NativeSettlementsPhrase: Pluralise(variant.NativeSettlementName),
            NativeSettlementSingular: variant.NativeSettlementName,
            NativeGroupWord: SingularGroupWord(variant.NativesName),
            ExpeditionaryForceName: variant.ExpeditionaryForceName,
            ExpeditionaryForceAbbrev: variant.ExpeditionaryForceAbbrev,
            DeclareIndependenceName: variant.DeclareIndependenceName,
            WarOfIndependenceName: variant.WarOfIndependenceName);
    }

    /// <summary>
    /// Lower-cases a <b>single-word</b> label for mid-sentence use (e.g. "Rebel" → "rebel"), but leaves a
    /// <b>multi-word</b> proper label untouched so it reads as a name ("Imperial Loyalists" stays "Imperial Loyalists").
    /// </summary>
    private static string LowerIfSingleWord(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Contains(' '))
        {
            return s;
        }
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    /// <summary>Pluralises a settlement label for the "… start wary" opening ("First Nations community" → "First Nations communities").</summary>
    private static string Pluralise(string s) =>
        s.EndsWith('y') ? s[..^1] + "ies" : s + "s";

    /// <summary>
    /// A word for one indigenous polity ("each ___'s mood") from the collective name — "First Nations" → "nation"
    /// (respectful, doc 19), falling back to the whole label lower-cased for any other variant word.
    /// </summary>
    private static string SingularGroupWord(string collective) =>
        collective.Equals("First Nations", System.StringComparison.Ordinal) ? "nation" : collective;
}
