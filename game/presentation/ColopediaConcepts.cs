using System.Collections.Generic;

namespace CrownAndColony.Presentation;

/// <summary>
/// The curated free-text <b>help topics</b> shown in the Colopedia's Concepts tab (FreeCol's
/// <c>ConceptDetailPanel</c> / <c>colopedia.concepts.*</c> message keys). FreeCol pulls this prose from the
/// ruleset's help-strings; we have none yet, so this is a hand-written, GPL-clean curated set covering the core
/// systems a new player needs — the seed of an in-game help/tutorial surface. <b>Presentation data only</b>
/// (ADR-006): no rule logic, no game state. Deliberately a <i>curated</i> set, not a ruleset-driven help engine —
/// that fuller, tutorial-depth surface is an explicit follow-up (see <c>ColopediaPanel</c> docs).
/// <para>
/// Each topic carries structured <see cref="ColopediaConceptTopic.Links"/> — cross-references to other Colopedia
/// entries (a good, a building, a unit, or another concept). <see cref="ColopediaPanel"/> renders them as clickable
/// buttons under the detail text so the reader can jump straight to the referenced entry, mirroring FreeCol's
/// in-text <c>&lt;a href="…/id/model.goods.bells"&gt;</c> help anchors.
/// </para>
/// </summary>
internal static class ColopediaConcepts
{
    /// <summary>The Colopedia category a cross-link points into (mirrors <c>ColopediaPanel.Category</c>).</summary>
    internal enum LinkCategory { Goods, Terrain, Units, Buildings, Fathers, Nations, Resources, Concepts }

    /// <summary>
    /// A cross-reference from a Concepts topic (or, later, any entry) to another Colopedia entry: the target
    /// <see cref="Category"/> and the <see cref="Anchor"/> that identifies the row within it — a ruleset
    /// <c>ShortName</c> (e.g. <c>bells</c>, <c>townHall</c>, <c>freeColonist</c>) for the ruleset categories, or a
    /// concept <b>title</b> for <see cref="LinkCategory.Concepts"/>. <see cref="Label"/> is the button caption.
    /// </summary>
    internal readonly record struct ConceptLink(string Label, LinkCategory Category, string Anchor);

    /// <summary>
    /// A free-text help topic: a short <see cref="Title"/>, an explanatory <see cref="Text"/> (a few plain-English
    /// sentences, no jargon), and zero or more <see cref="Links"/> to related Colopedia entries.
    /// </summary>
    internal readonly record struct ColopediaConceptTopic(string Title, string Text, IReadOnlyList<ConceptLink> Links);

    /// <summary>Convenience link to another help topic by its title.</summary>
    private static ConceptLink Concept(string title) => new(title, LinkCategory.Concepts, title);

    /// <summary>Convenience link to a goods entry by its ruleset short name.</summary>
    private static ConceptLink Goods(string label, string shortName) => new(label, LinkCategory.Goods, shortName);

    /// <summary>Convenience link to a building entry by its ruleset short name.</summary>
    private static ConceptLink Building(string label, string shortName) => new(label, LinkCategory.Buildings, shortName);

    /// <summary>Convenience link to a unit entry by its ruleset short name.</summary>
    private static ConceptLink Unit(string label, string shortName) => new(label, LinkCategory.Units, shortName);

    /// <summary>
    /// The curated help topics, in reading order (founding → economy → politics → war → independence). Each is a
    /// short, plain-English explanation with cross-links to the entries it mentions. Titles are stable: the L3 tests
    /// and the slug-based node names depend on them.
    /// </summary>
    internal static readonly IReadOnlyList<ColopediaConceptTopic> Topics = new[]
    {
        new ColopediaConceptTopic("Founding a colony",
            "Move a colonist onto a buildable land tile and give the Build Colony order to found a new settlement. "
            + "A colony grows by gathering food, works the surrounding tiles, and is where you produce goods, raise "
            + "buildings and train colonists. Pick the first sites for good land and a coast you can ship goods from.",
            new[] { Concept("Working tiles & production"), Unit("Free colonist", "freeColonist"), Building("Town hall", "townHall") }),

        new ColopediaConceptTopic("Working tiles & production",
            "Each colonist in a colony works either a surrounding tile — for food or a raw good — or a building, which "
            + "refines goods or produces liberty bells. Terrain, any bonus resource on the tile and the worker's "
            + "expertise decide how much that tile or building yields each turn. Match colonists to the work they are "
            + "expert in to get the most out of them.",
            new[] { Goods("Food", "food"), Concept("Sons of Liberty & bells"), Building("Town hall", "townHall") }),

        new ColopediaConceptTopic("Trade & Europe",
            "Sail a ship loaded with goods across the high seas to your home port in Europe, sell them for gold, and "
            + "buy goods or recruit colonists to bring back. Prices drift as the market is flooded or runs short, and "
            + "the Crown takes a cut of every sale as tax. A custom house can sell surplus for you without the voyage.",
            new[] { Concept("Taxes & boycotts"), Building("Custom house", "customHouse"), Unit("Merchantman", "merchantman") }),

        new ColopediaConceptTopic("Taxes & boycotts",
            "Early on the Crown levies little or no tax, but over the game the Monarch keeps raising the sales tax on "
            + "your trade. Each time you may refuse the rise — but a refused good is then boycotted: you can no longer "
            + "buy or sell it in Europe until you pay off your tax arrears or elect a Founding Father who lifts "
            + "boycotts. Weigh the saving against losing that market.",
            new[] { Concept("Trade & Europe"), Concept("Founding Fathers") }),

        new ColopediaConceptTopic("Sons of Liberty & bells",
            "Statesmen working the town hall produce liberty bells, which raise a colony's Sons of Liberty membership. "
            + "Once half the colony backs the rebel cause it earns a production bonus, and full membership doubles it; "
            + "a colony with too many royalists suffers a penalty instead. Bells are the engine of both your "
            + "production and your road to independence.",
            new[] { Goods("Liberty bells", "bells"), Building("Town hall", "townHall"), Concept("Founding Fathers"), Concept("Declaring Independence") }),

        new ColopediaConceptTopic("Founding Fathers",
            "Liberty bells also accrue toward your Continental Congress: spend enough and a Founding Father joins, "
            + "granting a lasting bonus. Each round you are offered one candidate per category — trade, exploration, "
            + "military, political and religious — and choose which to work toward. Their effects range from free "
            + "units or buildings to lifting every boycott.",
            new[] { Goods("Liberty bells", "bells"), Concept("Sons of Liberty & bells"), Concept("Taxes & boycotts") }),

        new ColopediaConceptTopic("Combat basics",
            "A unit's strength comes from its base power, its role and equipment, plus bonuses from terrain, "
            + "fortification and ambush. The defender's modifiers matter as much as the attacker's, so well-equipped "
            + "troops on good ground are hard to dislodge. Arm colonists with muskets and mount them on horses to turn "
            + "them into soldiers and dragoons.",
            new[] { Goods("Muskets", "muskets"), Goods("Horses", "horses"), Concept("Fortification") }),

        new ColopediaConceptTopic("Fortification",
            "A unit can fortify itself on a tile, digging in over one turn to defend better and avoid being ambushed. "
            + "Fortification only helps where the ground offers no defence of its own, so there is no extra benefit "
            + "from digging in on mountains or inside a colony that already has a fort or fortress. A stockade, fort or "
            + "fortress protects every defender in the colony.",
            new[] { Concept("Combat basics"), Building("Stockade", "stockade"), Building("Fort", "fort") }),

        new ColopediaConceptTopic("Education & training",
            "Build a schoolhouse and assign an expert colonist as a teacher to pass their skill to a free colonist; "
            + "larger schools teach the higher skills. Colonists can also learn a skill by living a turn in a native "
            + "settlement, or pick one up slowly through experience working the land. A college or university teaches "
            + "the rarer experts the schoolhouse cannot.",
            new[] { Building("Schoolhouse", "schoolhouse"), Unit("Free colonist", "freeColonist"), Concept("Native relations") }),

        new ColopediaConceptTopic("Native relations",
            "Indian settlements start wary and grow alarmed as your colonies and troops encroach on their land or you "
            + "attack them. Trade, gifts and missionaries calm them; raids and land-grabs provoke retaliation. Keep an "
            + "eye on each tribe's mood — an angry tribe will demand tribute and, if pushed, burn your colonies.",
            new[] { Concept("Combat basics"), Concept("Education & training") }),

        new ColopediaConceptTopic("Declaring Independence",
            "Once at least half of your colonists support the rebellion — which you build by producing liberty bells — "
            + "you may declare independence from the Crown. The King then sends his Royal Expeditionary Force to crush "
            + "you. Survive the war of independence and your bid succeeds; lose your coastal colonies or most of your "
            + "people and it fails.",
            new[] { Goods("Liberty bells", "bells"), Concept("Sons of Liberty & bells"), Concept("The Royal Expeditionary Force"), Concept("The Intervention Force") }),

        new ColopediaConceptTopic("The Royal Expeditionary Force",
            "The Royal Expeditionary Force (REF) is the army and navy the Monarch holds ready to put down any bid for "
            + "independence. It grows over the game as your trade and population swell, so a long, prosperous game "
            + "faces a larger force. After you declare, the REF lands to retake your colonies — you must destroy it to "
            + "win your freedom.",
            new[] { Concept("Declaring Independence"), Concept("The Intervention Force") }),

        new ColopediaConceptTopic("The Intervention Force",
            "Your fight for independence weakens your home country, which suits its rivals. If you produce enough "
            + "liberty bells, one of those rival powers sends an Intervention Force to fight alongside you against the "
            + "Royal Expeditionary Force. Both forces are reinforced over the war, so keep the bells ringing to tip "
            + "the balance your way.",
            new[] { Goods("Liberty bells", "bells"), Concept("Declaring Independence"), Concept("The Royal Expeditionary Force") }),
    };
}
