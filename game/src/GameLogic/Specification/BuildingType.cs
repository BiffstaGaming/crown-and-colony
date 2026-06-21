namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A building type from the ruleset (town hall, carpenter's house, lumber mill…)
/// — immutable rule data with inherited attributes resolved.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.building.carpenterHouse</c>.</param>
/// <param name="UpgradesFrom">Building this one replaces (null for base buildings).</param>
/// <param name="Workplaces">Colonists who can work here (FreeCol default 3).</param>
/// <param name="RequiredPopulation">Minimum colony population to build it.</param>
/// <param name="Productions">Per-worker (or unattended) conversions, e.g. lumber 3 → hammers 3.</param>
/// <param name="BuildCost">Goods required to construct it (hammers, tools…).</param>
/// <param name="DefenceBonus">
/// Percentage defence bonus this building grants a unit defending in the colony (spec
/// <c>model.modifier.defence</c>; default 0). Stockade +100, fort +150, fortress +200 — the colony's
/// fortification tier (FreeCol applies it to the colony's defender, garrison or last colonist).
/// </param>
/// <param name="WarehouseStorage">
/// Warehouse capacity this building grants, in goods per type (spec <c>model.modifier.warehouseStorage</c>,
/// additive, summed up the <c>extends</c> chain; default 0). Depot 100, warehouse 200, expansion 300 — a colony
/// holds one of these tiers, capping each storable good (FreeCol <c>Settlement.getWarehouseCapacity</c>).
/// </param>
/// <param name="BellBonus">
/// Percentage boost this building gives the colony's bell output (spec <c>model.goods.bells</c> percentage;
/// default 0). Printing press +50, newspaper +100 (the newspaper deletes + redefines the inherited modifier —
/// own-valued one taken, like <see cref="DefenceBonus"/>). Speeds Sons-of-Liberty + founding-father progress.
/// </param>
/// <param name="RequiredAbilities">
/// Abilities the colony must satisfy before this building may be constructed (spec <c>required-ability</c>,
/// id → required value; inherited down the <c>extends</c> chain so drydock/shipyard keep docks' <c>hasPort</c>).
/// Empty for an unconditional building. Classic gates: the factory tier + arsenal need <c>buildFactory</c>
/// (Adam Smith), the custom house needs <c>buildCustomHouse</c> (Stuyvesant), docks/drydock/shipyard need a
/// coastal colony (<c>hasPort</c>). FreeCol <c>Colony.getNoBuildReason</c> MISSING_ABILITY.
/// </param>
/// <param name="RepairsNavalUnits">
/// Whether this building lets the colony repair damaged ships (spec <c>model.ability.repairUnits</c>, resolved
/// down the <c>extends</c> chain; drydock grants it, shipyard inherits it). A damaged ship limps to the nearest
/// owned colony with this ability rather than all the way to Europe (FreeCol <c>Unit.getRepairLocation</c>).
/// </param>
/// <param name="BuildableUnitTypeIds">
/// Unit-type ids this building lets the colony construct (spec <c>model.ability.build</c> with a
/// <c>&lt;scope type="…"/&gt;</c>, collected down the <c>extends</c> chain): carpenter's house → wagon train,
/// armory → artillery (magazine/arsenal inherit it). Drives the unit build-ability gate (FreeCol
/// <c>UnitType.canBeBuiltInColony</c> MISSING_BUILD_ABILITY).
/// </param>
/// <param name="BuildsNavalUnits">
/// Whether this building grants the build ability scoped to <c>model.ability.navalUnit</c> (spec
/// <c>model.ability.build</c> with <c>&lt;scope ability-id="model.ability.navalUnit"/&gt;</c>) — the shipyard,
/// which enables building any ship. (Ship construction is not yet wired up; this is parsed for completeness.)
/// </param>
/// <param name="BombardsShips">
/// Whether this building lets the colony bombard adjacent enemy ships at the start of its owner's turn (spec
/// <c>model.ability.bombardShips</c>, resolved down the <c>extends</c> chain; the fort grants it, the fortress
/// inherits it). FreeCol <c>Settlement.canBombardEnemyShip</c>.
/// </param>
/// <param name="GrantsExport">
/// Whether this building grants the colony the auto-export ability (spec <c>model.ability.export</c>, resolved down
/// the <c>extends</c> chain) — the custom house declares it (building it is gated on Stuyvesant's
/// <c>buildCustomHouse</c>). FreeCol <c>Ability.EXPORT</c>; drives the per-turn custom-house auto-sell.
/// </param>
/// <param name="BreedingDivisor">
/// Herd-growth divisor for an auto-production breeder (spec <c>model.modifier.breedingDivisor</c>, resolved
/// additive-then-multiplicative down the <c>extends</c> chain: pasture/country 50, stables ×0.5 → 25; default 0 =
/// not a breeder). Drives the FreeCol horse-breeding formula <c>((herd−1)/divisor + 1) × factor</c>.
/// </param>
/// <param name="BreedingFactor">
/// Herd-growth multiplier for an auto-production breeder (spec <c>model.modifier.breedingFactor</c>, country 2;
/// default 0). The <c>× factor</c> term in the breeding formula (see <see cref="BreedingDivisor"/>).
/// </param>
/// <param name="RebelFactor">
/// Multiplier applied to the colony's Sons-of-Liberty production bonus before it is folded into a worker's output
/// here (spec <c>rebel-factor</c> attribute; default 1, nearest definition wins up the <c>extends</c> chain). The
/// lumber mill and cathedral set 2, the factory tier 1.5 — so good government boosts those buildings more. FreeCol
/// <c>ProductionUtils.getRebelProductionModifiersForBuilding</c> (<c>floor(productionBonus × rebelFactor)</c>).
/// </param>
/// <param name="CompetenceFactor">
/// Multiplier applied to a SPECIALIST/expert worker's <em>additive</em> production bonus here (spec
/// <c>competence-factor</c> attribute; default 1, nearest definition wins up the <c>extends</c> chain). The upgraded
/// manufactories set it &gt; 1 — lumber mill 2, blacksmith shop / tobacconist shop / weaver shop / fur trading post /
/// distillery 2, iron works / cigar factory / textile mill / fur factory / rum factory 3 — so an expert in them earns
/// a bigger bonus than in the base house (a master carpenter's +3 hammers becomes +6 in the lumber mill). FreeCol
/// <c>BuildingType.getCompetenceModifiers</c> multiplies only the unit's <b>additive</b> modifiers, NOT its
/// multiplicative ones (a master distiller's ×2 rum is untouched — competence scales the flat expert bonus, never the
/// doubling). Free colonists have no production modifier, so competence does not affect them.
/// </param>
/// <param name="Upkeep">
/// Gold this building costs its owner to maintain each turn (spec <c>upkeep</c> attribute; default 0, resolved up the
/// <c>extends</c> chain). The upgraded buildings charge it (lumber mill 10, blacksmith shop 5, iron works 15, …);
/// summed over a colony's buildings it is the colony's per-turn upkeep (FreeCol <c>Colony.getUpkeep</c> →
/// <c>ServerPlayer.csPayUpkeep</c>). Deducted from the player's gold each turn ONLY when the ruleset's
/// <see cref="Ruleset.UpkeepEnabled"/> game option is on — which the classic ruleset leaves <b>off</b>, so the classic
/// game charges no upkeep by default.
/// </param>
/// <param name="MaximumSkill">
/// The highest unit <see cref="UnitType.Skill"/> an expert may teach from this building (spec <c>maximum-skill</c>;
/// schoolhouse 1, college 2, university 4; 0 = not a school). An over-skilled expert (e.g. an elder statesman, skill 3,
/// in a schoolhouse) cannot teach there. FreeCol <c>Building.canAddType</c> / <c>getNoAddReason</c> MAXIMUM_SKILL.
/// </param>
/// <param name="Teaches">
/// Whether this building can teach (spec <c>model.ability.teach</c>, inherited down the <c>extends</c> chain — only the
/// schoolhouse declares it; college and university inherit it). The schoolhouse/college/university trio; FreeCol
/// <c>Ability.CAN_TEACH</c>.
/// </param>
/// <param name="MinimumSkill">
/// The lowest unit <see cref="UnitType.Skill"/> that may occupy this building (spec <c>minimum-skill</c>; the school
/// trio set 1, inherited down the <c>extends</c> chain — so only an expert teaches, never a free colonist/servant/
/// criminal; default 0 = no floor). FreeCol <c>Building.getNoAddReason</c> MINIMUM_SKILL.
/// </param>
/// <param name="DressesMissionary">
/// Whether this building lets the colony ordain a missionary — i.e. grants the colony <c>model.ability.dressMissionary</c>
/// (spec, resolved down the <c>extends</c> chain; the <b>church</b> declares it, the <b>cathedral</b> inherits it, the
/// chapel does not). It is the colony-side requirement of the <c>model.role.missionary</c> role, so a colonist can only
/// be equipped as a missionary at a colony with a church or cathedral. FreeCol <c>Ability.DRESS_MISSIONARY</c>.
/// </param>
/// <param name="ProducesInWater">
/// Whether this building lets colonists work the colony's <b>water</b> tiles (ocean/lake) — i.e. grants the colony
/// <c>model.ability.produceInWater</c>, resolved down the <c>extends</c> chain (the docks grant it; the drydock and
/// shipyard inherit it). Without it, a colonist cannot be assigned to a sea tile to fish (FreeCol
/// <c>ColonyTile.getNoWorkReason</c> → <c>MISSING_ABILITY</c> when <c>!hasAbility(PRODUCE_IN_WATER) &amp;&amp; !tile.isLand()</c>).
/// </param>
public sealed record BuildingType(
    string Id,
    string? UpgradesFrom,
    int Workplaces,
    int RequiredPopulation,
    IReadOnlyList<ProductionEntry> Productions,
    IReadOnlyList<GoodsOutput> BuildCost,
    int DefenceBonus = 0,
    int WarehouseStorage = 0,
    int BellBonus = 0,
    IReadOnlyDictionary<string, bool>? RequiredAbilities = null,
    bool RepairsNavalUnits = false,
    IReadOnlySet<string>? BuildableUnitTypeIds = null,
    bool BuildsNavalUnits = false,
    bool BombardsShips = false,
    bool GrantsExport = false,
    int BreedingDivisor = 0,
    int BreedingFactor = 0,
    double RebelFactor = 1.0,
    double CompetenceFactor = 1.0,
    int Upkeep = 0,
    int MaximumSkill = 0,
    bool Teaches = false,
    int MinimumSkill = 0,
    bool DressesMissionary = false,
    bool ProducesInWater = false)
{
    private static readonly IReadOnlyDictionary<string, bool> NoAbilities = new Dictionary<string, bool>();
    private static readonly IReadOnlySet<string> NoUnits = new HashSet<string>();

    /// <summary>Short name derived from the id: <c>model.building.townHall</c> → <c>townHall</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>Abilities required to build this, id → required value (an empty map when unconditional).</summary>
    public IReadOnlyDictionary<string, bool> RequiredAbilitiesOrEmpty => RequiredAbilities ?? NoAbilities;

    /// <summary>Unit-type ids this building lets the colony construct (an empty set when it enables none).</summary>
    public IReadOnlySet<string> BuildableUnitTypeIdsOrEmpty => BuildableUnitTypeIds ?? NoUnits;
}
