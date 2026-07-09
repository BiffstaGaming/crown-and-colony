using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A selectable game variant — a self-contained historical setting (Colonial
/// America is the only one today; Australia and others come later). A variant is
/// identified by a stable <see cref="Id"/> and knows how to load <em>its</em>
/// ruleset, which defines that world's nations, Founding Fathers (and their
/// perks), units, goods and terrain.
///
/// <para><b>The transposability contract (ADR-018):</b> selecting a variant is the
/// <em>only</em> thing that changes which data the engine plays by. The rules code
/// (`Game`, the turn loop, combat, …) is variant-agnostic — it reads whatever the
/// loaded <see cref="Ruleset"/> contains. Adding a new variant is therefore a data
/// task (author a spec, register a variant here), not a code change.</para>
/// </summary>
public sealed class GameVariant
{
    private readonly string _specResource;
    private static readonly IReadOnlyDictionary<string, string> NoOverrides =
        new Dictionary<string, string>();

    internal GameVariant(string id, string displayName, string description, string specResource,
        string congressName = "Continental Congress",
        IReadOnlyDictionary<string, string>? displayOverrides = null,
        string rebelSentimentName = "Sons of Liberty",
        string rebelColonistName = "Rebel",
        string loyalistColonistName = "Tory",
        string rebelFactionName = "Rebels",
        string loyalistFactionName = "Royalists",
        string expeditionaryForceName = "Royal Expeditionary Force",
        string expeditionaryForceAbbrev = "REF",
        string declareIndependenceName = "Declare Independence",
        string warOfIndependenceName = "War of Independence",
        string nativesName = "Native nations",
        string nativeSettlementName = "Native settlement",
        string libertyName = "liberty",
        IReadOnlyList<string>? openingBeats = null,
        MapSource? forcedMapSource = null,
        int? defaultForeignPowerCount = null,
        bool referendumVictoryOnly = false,
        string? commonwealthVictoryTitle = null,
        string? commonwealthProclamation = null,
        string? commonwealthAddendum = null)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        CongressName = congressName;
        DisplayOverrides = displayOverrides ?? NoOverrides;
        OpeningBeats = openingBeats ?? GameVariants.ClassicOpeningBeats;
        RebelSentimentName = rebelSentimentName;
        RebelColonistName = rebelColonistName;
        LoyalistColonistName = loyalistColonistName;
        RebelFactionName = rebelFactionName;
        LoyalistFactionName = loyalistFactionName;
        ExpeditionaryForceName = expeditionaryForceName;
        ExpeditionaryForceAbbrev = expeditionaryForceAbbrev;
        DeclareIndependenceName = declareIndependenceName;
        WarOfIndependenceName = warOfIndependenceName;
        NativesName = nativesName;
        NativeSettlementName = nativeSettlementName;
        LibertyName = libertyName;
        ForcedMapSource = forcedMapSource;
        DefaultForeignPowerCount = defaultForeignPowerCount;
        ReferendumVictoryOnly = referendumVictoryOnly;
        CommonwealthVictoryTitle = commonwealthVictoryTitle;
        CommonwealthProclamation = commonwealthProclamation;
        CommonwealthAddendum = commonwealthAddendum;
        _specResource = specResource;
    }

    /// <summary>Stable id, persisted in saves so a game reloads under the right ruleset (e.g. <c>classic</c>).</summary>
    public string Id { get; }

    /// <summary>Player-facing name (e.g. "Colonial America (Classic)").</summary>
    public string DisplayName { get; }

    /// <summary>One-line description for the variant-select screen.</summary>
    public string Description { get; }

    /// <summary>
    /// The player-facing name of this variant's father-electing body — classic's <b>Continental Congress</b>, the
    /// Australian Federation's <b>Federation Convention</b> (task 86d3kwtjb). A display label only: the election
    /// machinery is identical for every variant (ADR-018); the presentation titles its Congress panel/report with this.
    /// </summary>
    public string CongressName { get; }

    /// <summary>
    /// The ordered narrative <b>beats</b> of this variant's opening cinematic — the short story panels shown on the
    /// interactive New-Game path before the board appears. Classic's default is the <b>1492</b> expedition scene
    /// (charter → crossing → landfall → send-off); the Australian Federation supplies its own <b>1788</b> First-Fleet →
    /// Federation arc (doc 03 story arc; doc 19 tone rules — sober, not triumphalist). A display-only field (ADR-018):
    /// the cinematic machinery is identical for every variant; only the text differs. The presentation threads this into
    /// <c>OpeningCinematic</c> before <c>Play()</c>. A fixed ordered list — no RNG (ADR-009). Defaults to
    /// <see cref="GameVariants.ClassicOpeningBeats"/> so unspecified variants stay byte-identical to classic.
    /// </summary>
    public IReadOnlyList<string> OpeningBeats { get; }

    /// <summary>
    /// Variant-scoped <b>display-name overrides</b>, keyed by ruleset short name (e.g. <c>bells</c> → "Civic Voice",
    /// <c>cotton</c> → "Wool", <c>silver</c> → "Gold"). A display-layer rename only: the underlying ids are the
    /// transposability anchors the engine keys on (ADR-018) and are never changed — the reskin renames what the
    /// player reads, not what the engine plays by. Threaded to the presentation layer, which passes it to
    /// <see cref="Naming.Humanize(string, IReadOnlyDictionary{string, string})"/>. Empty for classic (byte-identical text).
    /// </summary>
    public IReadOnlyDictionary<string, string> DisplayOverrides { get; }

    /// <summary>
    /// The player-facing name of the pro-independence sentiment / movement — classic's <b>Sons of Liberty</b>, the
    /// Australian Federation's <b>Federationists</b> (doc 19 UI-text table). A display label only (ADR-018): the
    /// rebel-factor maths is identical for every variant; the presentation labels the sentiment with this.
    /// </summary>
    public string RebelSentimentName { get; }

    /// <summary>The word for a colonist who supports independence — classic <b>Rebel</b>, Australia <b>Federationist</b>. Display label only.</summary>
    public string RebelColonistName { get; }

    /// <summary>The word for a colonist loyal to the Crown — classic <b>Tory</b>, Australia <b>Imperial Loyalist</b>. Display label only.</summary>
    public string LoyalistColonistName { get; }

    /// <summary>The colony screen's plural label for the pro-independence faction/count — classic <b>Rebels</b>, Australia <b>Federationists</b> (distinct from the classic "Sons of Liberty" sentiment name). Display label only (ADR-018).</summary>
    public string RebelFactionName { get; }

    /// <summary>The colony screen's plural label for the pro-Crown faction/count — classic <b>Royalists</b>, Australia <b>Imperial Loyalists</b>. Display label only (ADR-018).</summary>
    public string LoyalistFactionName { get; }

    /// <summary>
    /// The name of the army the Crown sends to crush an independence bid — classic's <b>Royal Expeditionary Force</b>,
    /// the Australian Federation's <b>Imperial Pressure</b> (doc 19). Display label only (ADR-018): the REF mechanic is
    /// identical for every variant; the reports/military panels title it with this.
    /// </summary>
    public string ExpeditionaryForceName { get; }

    /// <summary>The short form of <see cref="ExpeditionaryForceName"/> — classic <b>REF</b>, Australia <b>Imperial Pressure</b> (used on the compact report tab).</summary>
    public string ExpeditionaryForceAbbrev { get; }

    /// <summary>
    /// The name of the act that begins the independence sequence — classic's <b>Declare Independence</b>, the Australian
    /// Federation's <b>Put Constitution to Referendum</b> (doc 19). Display label only (ADR-018).
    /// </summary>
    public string DeclareIndependenceName { get; }

    /// <summary>
    /// The name of the resulting struggle — classic's <b>War of Independence</b>, the Australian Federation's
    /// <b>Federation Referendum</b> (doc 19). Display label only (ADR-018).
    /// </summary>
    public string WarOfIndependenceName { get; }

    /// <summary>
    /// The chrome word for the indigenous nations collectively — classic's <b>Native nations</b>, the Australian
    /// Federation's <b>First Nations</b> (doc 19; a respectful, sombre relabel — no "savage"/"primitive"). Display label
    /// only (ADR-018): this is the chrome WORD in panel titles/labels, <em>not</em> the specific nation display names
    /// (Eora/Kulin/… — those are spec id-renames handled by the nations stream).
    /// </summary>
    public string NativesName { get; }

    /// <summary>The chrome word for a single indigenous settlement — classic <b>Native settlement</b>, Australia <b>First Nations community</b> (doc 19: "First Nations Communities", not "Indian villages"). Display label only.</summary>
    public string NativeSettlementName { get; }

    /// <summary>
    /// The chrome word for the accruing political capital that recruits pioneers / declares independence — classic
    /// <b>liberty</b> (from liberty bells), the Australian Federation's <b>Civic Voice</b> (doc 19; the <c>bells</c> good
    /// itself is renamed via <see cref="DisplayOverrides"/>). Display label only (ADR-018).
    /// </summary>
    public string LibertyName { get; }

    /// <summary>
    /// The map this variant is <b>fixed to</b>, if any — e.g. the Australian Federation always plays on the authored
    /// Australia continent (<see cref="MapSource.Australia"/>), so the New-Game map picker is set to it and locked. A
    /// setting-only convenience (ADR-006): the engine still takes whatever map it is handed; this just drives the UI
    /// default + lock so a scenario isn't played on the wrong world. <c>null</c> for classic (the player chooses).
    /// </summary>
    public MapSource? ForcedMapSource { get; }

    /// <summary>
    /// The number of rival European colonial powers this variant seeds by default, if it fixes one — e.g. the Australian
    /// Federation is **0** (historically the continent was colonised by Britain alone; the Dutch and French explored and
    /// charted its coasts but founded no colonies here, so there is no multi-power colonial contest to model). <c>null</c>
    /// for classic → the engine's default roster (three rivals). A New-Game default + lock; the First Nations communities
    /// are the native nations and are unaffected by this.
    /// </summary>
    public int? DefaultForeignPowerCount { get; }

    /// <summary>
    /// Whether this variant is won <b>only by the Federation Referendum</b> (the Commonwealth proclamation) — <c>true</c>
    /// for the Australian Federation, whose referendum replaces the classic War-of-Independence / last-power-standing wins
    /// entirely (Chris 2026-07-09). The engine already makes the Federation victory exclusive whenever it is enabled
    /// (<see cref="GameSession.Game.Winner"/>); the New-Game dialog reads this to switch off + lock the three classic
    /// victory-condition checkboxes so the UI matches. <c>false</c> for classic (the player picks the classic conditions).
    /// </summary>
    public bool ReferendumVictoryOnly { get; }

    /// <summary>The victory-screen title when this variant is won by the Federation path (e.g. Australia's "The Commonwealth
    /// of Australia is proclaimed"), or <c>null</c> for a variant with no Federation victory (classic → the panel keeps its
    /// "{winner} is victorious!" title). Display text only (ADR-018).</summary>
    public string? CommonwealthVictoryTitle { get; }

    /// <summary>The Federation-victory proclamation shown on the victory screen (doc 19 "Victory text"), or <c>null</c> for none.</summary>
    public string? CommonwealthProclamation { get; }

    /// <summary>The historically-honest addendum shown beneath the proclamation (doc 19) — the exclusion of Aboriginal and
    /// Torres Strait Islander peoples and other communities from the 1901 settlement — or <c>null</c> for none. Binding for Australia.</summary>
    public string? CommonwealthAddendum { get; }

    /// <summary>Loads this variant's ruleset by parsing its embedded specification, applying a difficulty level.</summary>
    /// <param name="difficultyLevelId">The difficulty level to apply (default <c>model.difficulty.medium</c> → the historical balance).</param>
    public Ruleset LoadRuleset(string difficultyLevelId = DifficultyLevels.DefaultId) =>
        Ruleset.LoadEmbedded(_specResource, difficultyLevelId);
}

/// <summary>
/// The registry of game variants the build ships with. New variants (the Australia
/// scenario, etc.) are registered here and become selectable everywhere — no other
/// code changes (ADR-018).
/// </summary>
public static class GameVariants
{
    /// <summary>Resource name of the embedded classic FreeCol specification.</summary>
    internal const string ClassicSpecResource =
        "CrownAndColony.GameLogic.Specification.classic.specification.xml";

    /// <summary>Resource name of the embedded Australian Federation specification (P8).</summary>
    internal const string AustraliaSpecResource =
        "CrownAndColony.GameLogic.Specification.australia.specification.xml";

    /// <summary>
    /// The Australian Federation's display-name overrides, keyed by ruleset short name (the id after the last dot).
    /// A display-layer reskin only (ADR-018) — every underlying id is unchanged. Grouped by the P8 task that owns it:
    /// <list type="bullet">
    ///   <item><b>Goods</b> (86d3kwty1): <c>cotton</c>→Wool, <c>silver</c>→Gold, <c>bells</c>→Civic Voice; plus the
    ///     playtest-gap closers (86d3mm2q4) — the sealing/skins economy that replaced the fur trade
    ///     (<c>furs</c>→Skins, <c>coats</c>→Leather).</item>
    ///   <item><b>Units</b> (86d3kwtvc / 86d3mm2nv): <c>pettyCriminal</c>→Convict Labourer, <c>indenturedServant</c>→
    ///     Emancipist, <c>freeColonist</c>→Free Settler, plus the two pioneer stand-ins the perks already reuse —
    ///     the gold specialist <c>expertSilverMiner</c>→Digger and the wool specialist <c>masterCottonPlanter</c>→Shepherd.</item>
    ///   <item><b>Buildings</b> (86d3mm25r): <c>depot</c>→Government Stores; the wool-processing chain
    ///     <c>weaverHouse</c>→Wool Shed and <c>weaverShop</c>→Shearing Shed (doc 17). The playtest gap closers
    ///     (86d3mm2q4) reskin the remaining American-flavoured processing chains so a building agrees with the good it
    ///     works: the rum chain (early NSW's "Rum Corps") <c>distillerHouse</c>→Rum Still; the sealskins chain
    ///     <c>furTraderHouse</c>→Skinner's Hut / <c>furTradingPost</c>→Tannery / <c>furFactory</c>→Leather Works; and the
    ///     northern tobacco chain <c>tobacconistHouse</c>→Tobacco Shed / <c>tobacconistShop</c>→Tobacco Works.</item>
    /// </list>
    /// The raw goods a chain works on (<c>sugar</c>→rum, <c>tobacco</c>→cigars) keep their classic display — sugar cane
    /// (Queensland) and northern tobacco are historically Australian already, so no forced rename; likewise
    /// <c>rum</c>/<c>cigars</c>/<c>rumDistillery</c>/<c>rumFactory</c>/<c>cigarFactory</c> whose camelCase split already
    /// reads Australian. Declared <em>before</em> the <see cref="Australia"/> variant so it is initialised first (static
    /// field initialisers run in textual order — a later-declared field would still be <c>null</c> at construction).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AustraliaDisplayOverrides =
        new Dictionary<string, string>
        {
            // ── Goods (86d3kwty1) ──
            ["cotton"] = "Wool",
            ["silver"] = "Gold",
            ["bells"] = "Civic Voice",
            // ── Goods: sealing/skins economy replacing the fur trade (playtest gap, 86d3mm2q4) ──
            ["furs"] = "Skins",                      // raw hunted product (sealing/skins, not beaver furs)
            ["coats"] = "Leather",                   // the processed skins product (classic "coats")
            // ── Units (86d3kwtvc + retarget-pioneers 86d3mm2nv) ──
            ["pettyCriminal"] = "Convict Labourer",
            ["indenturedServant"] = "Emancipist",
            ["freeColonist"] = "Free Settler",
            ["expertSilverMiner"] = "Digger",        // the gold specialist (silver = Gold stand-in)
            ["masterCottonPlanter"] = "Shepherd",    // the wool specialist (cotton = Wool stand-in)
            // ── Buildings (86d3mm25r) ──
            ["depot"] = "Government Stores",
            ["weaverHouse"] = "Wool Shed",           // cotton→cloth processing = wool processing
            ["weaverShop"] = "Shearing Shed",        // the advanced wool-processing upgrade
            // ── Buildings: reskin the remaining American-flavoured processing chains (playtest gap, 86d3mm2q4) ──
            ["distillerHouse"] = "Rum Still",        // rum chain (early NSW "Rum Corps"); rumDistillery/rumFactory read fine already
            ["furTraderHouse"] = "Skinner's Hut",    // sealskins chain — the raw-skins processing house
            ["furTradingPost"] = "Tannery",          // the mid tier of the skins chain
            ["furFactory"] = "Leather Works",        // the advanced skins/leather chain
            ["tobacconistHouse"] = "Tobacco Shed",   // northern tobacco chain (grown in the north); cigarFactory reads fine
            ["tobacconistShop"] = "Tobacco Works",   // the advanced tobacco-processing upgrade
        };

    /// <summary>
    /// The <b>classic</b> opening-cinematic beats — the faithful 1492 expedition scene (charter → crossing → landfall →
    /// send-off), in order. This is the shared default: both the classic variant and (via <see cref="GameVariant"/>'s
    /// ctor) any variant that supplies no beats of its own point at this identical list, so classic intro text stays
    /// byte-identical. Original period-flavour wording (GPL-clean); a fixed ordered list — no RNG (ADR-009).
    /// </summary>
    internal static readonly IReadOnlyList<string> ClassicOpeningBeats = new[]
    {
        "In the year of our Lord 1492, the Crown grants you a charter: sail west, claim new lands, and return their " +
        "riches to the throne.",

        "Your caravels slip their moorings and stand out to sea. For weeks there is only the grey Atlantic, the wind, " +
        "and the faith of your colonists.",

        "At last a cry from the masthead — land! A green and unknown shore rises from the ocean haze.",

        "History waits ashore. Found your colonies, win your people's hearts, and one day raise a new nation of your " +
        "own. The New World is yours to build.",
    };

    /// <summary>
    /// The <b>Australian Federation</b> opening-cinematic beats — the 1788 First Fleet → 1901 Federation arc (doc 03
    /// story arc), told in the sober, historically-honest register doc 19 requires: the arrival came to a continent
    /// already home to many peoples, and the narrative acknowledges that rather than glorifying it. Four beats:
    /// the First Fleet's voyage &amp; landing at Sydney Cove; the hard early penal years; growth into six colonies;
    /// the road to Federation of the Commonwealth in 1901. Original wording (GPL-clean); a fixed ordered list (ADR-009).
    /// </summary>
    internal static readonly IReadOnlyList<string> AustraliaOpeningBeats = new[]
    {
        "In 1788 the First Fleet drops anchor at Sydney Cove — convicts, marines and settlers landing on the shore of " +
        "a continent already home to many peoples, whose Country stretches beyond every horizon.",

        "The early years are lean and uncertain. Rations run short, the soil resists the plough, and the fledgling " +
        "penal colony survives on discipline, on the sea, and on an uneasy proximity to those whose land it is.",

        "Decade by decade the settlements spread, until six separate British colonies stretch across the continent — " +
        "their farms, wool, ports and goldfields growing on Country that was never ceded.",

        "By 1901 the colonies debate a shared future. Guide them toward Federation and the Commonwealth of Australia — " +
        "a new nation whose founding leaves many questions of land, belonging and justice still unanswered.",
    };

    /// <summary>The faithful 1492 New World: the four European powers, the historic Founding Fathers, and the indigenous nations.</summary>
    public static readonly GameVariant ClassicAmerica = new(
        id: "classic",
        displayName: "Colonial America (Classic)",
        description: "The 1492 New World — English, French, Spanish and Dutch colonists, the historic "
            + "Founding Fathers, and the indigenous nations. The faithful Colonization ruleset.",
        specResource: ClassicSpecResource);

    /// <summary>
    /// The Australian Federation setting (P8) — British Australia on the real continent, 1788–1901. The perk-granting
    /// figures are the <b>Australian Pioneers</b> (this variant's Founding-Father equivalent). <b>Skeleton:</b> the spec
    /// starts as a copy of classic and is reskinned to Australian content over the P8 slices; the map is the authored
    /// Australia continent (<see cref="World.MapSource.Australia"/>).
    /// </summary>
    public static readonly GameVariant Australia = new(
        id: "australia",
        displayName: "Australian Federation",
        description: "British Australia on the real continent, 1788–1901 — build the six colonies toward Federation. "
            + "Recruit the Australian Pioneers (this world's founding figures). The authored Australia map.",
        specResource: AustraliaSpecResource,
        congressName: "Federation Convention",
        // The presentation reskin (P8, tasks 86d3kwty1/86d3kwtvc/86d3mm25r/86d3mm2nv). Keyed by ruleset SHORT NAME
        // (the id after the last dot) — the underlying ids stay put (transposability anchors, ADR-018); only the
        // player-facing display changes. Sources: docs/australian_federation_mode_md/17 (goods/units/buildings) + 19.
        displayOverrides: AustraliaDisplayOverrides,
        // Chrome labels (doc 19 UI-text renaming table). Australian text under Australia; classic strings are the defaults.
        rebelSentimentName: "Federationists",           // Sons of Liberty
        rebelColonistName: "Federationist",              // Rebel colonist
        loyalistColonistName: "Imperial Loyalist",       // Tory colonist
        rebelFactionName: "Federationists",              // colony-screen "Rebels" count
        loyalistFactionName: "Imperial Loyalists",       // colony-screen "Royalists" count
        expeditionaryForceName: "Imperial Pressure",     // Royal Expeditionary Force
        expeditionaryForceAbbrev: "Imperial Pressure",   // REF (no short military acronym in the reskin)
        declareIndependenceName: "Put Constitution to Referendum", // Declare Independence
        warOfIndependenceName: "Federation Referendum",  // War of Independence
        nativesName: "First Nations",                    // Native nations
        nativeSettlementName: "First Nations community", // Native settlement
        libertyName: "Civic Voice",                      // liberty
        // The 1788→1901 opening cinematic (doc 03 arc; doc 19 tone — sober, not triumphalist). Classic keeps the
        // 1492 default; the Australia variant supplies its own First-Fleet-to-Federation beats.
        openingBeats: AustraliaOpeningBeats,
        // The variant is fixed to the authored Australia continent and to a colonial contest of one — historically the
        // continent was British-settled alone (the Dutch/French charted the coast but founded no colonies). The New-Game
        // dialog reads these to default + lock the map and rival-power pickers (Chris 2026-07-09).
        forcedMapSource: MapSource.Australia,
        defaultForeignPowerCount: 0,
        // The Australian Federation is won ONLY by the referendum → Commonwealth proclamation (Chris 2026-07-09); the
        // classic War-of-Independence / last-power-standing wins do not apply (and would be a turn-1 instant win under
        // 0 rivals). Game.Winner enforces this; the dialog switches off + locks the classic victory checkboxes.
        referendumVictoryOnly: true,
        // The Commonwealth victory screen (doc 19 "Victory text" — verbatim). The honest addendum is a BINDING requirement:
        // Federation is not framed as resolving everything (docs 03/15/19).
        commonwealthVictoryTitle: "The Commonwealth of Australia is proclaimed",
        commonwealthProclamation:
            "The colonies have voted to federate. The Commonwealth of Australia is proclaimed, and the continent's "
            + "colonial governments enter a new constitutional era.",
        commonwealthAddendum:
            "Federation joins the colonies, but it does not resolve every question. Many Aboriginal and Torres Strait "
            + "Islander peoples and other communities were excluded from the political settlement. Country, sovereignty, "
            + "representation, and justice remain unfinished parts of the national story.");

    /// <summary>Every shipped variant, in menu order.</summary>
    public static IReadOnlyList<GameVariant> All { get; } = [ClassicAmerica, Australia];

    /// <summary>The variant used when none is chosen (new game / legacy saves).</summary>
    public static GameVariant Default => ClassicAmerica;

    /// <summary>Looks up a variant by its stable id.</summary>
    /// <exception cref="KeyNotFoundException">No shipped variant has that id (e.g. a save from a build that had it).</exception>
    public static GameVariant ById(string id) =>
        All.FirstOrDefault(v => v.Id == id)
            ?? throw new KeyNotFoundException(
                $"Unknown game variant '{id}'. Installed variants: {string.Join(", ", All.Select(v => v.Id))}.");

    /// <summary>Resolves a (possibly null, from a legacy save) variant id, falling back to <see cref="Default"/>.</summary>
    public static GameVariant Resolve(string? id) => id is null ? Default : ById(id);
}
