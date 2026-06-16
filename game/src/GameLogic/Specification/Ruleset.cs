using System.Reflection;
using System.Xml.Linq;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The parsed game ruleset. Loaded from a FreeCol-format <c>specification.xml</c>
/// (ADR-004); the classic ruleset ships embedded in this assembly so every host
/// (game, tests, CI) reads the identical bytes.
/// </summary>
public sealed class Ruleset
{
    private readonly Dictionary<string, TerrainType> _terrainById;
    private readonly Dictionary<string, UnitType> _unitById;
    private readonly Dictionary<string, GoodsType> _goodsById;
    private readonly Dictionary<string, BuildingType> _buildingById;
    private readonly Dictionary<string, FoundingFather> _fatherById;
    private readonly Dictionary<string, ResourceType> _resourceById;
    private readonly Dictionary<string, NativeNationType> _nativeNationById;
    private readonly Dictionary<string, SettlementType> _settlementById;
    private readonly Dictionary<string, RoleType> _roleById;
    private readonly Dictionary<string, Dictionary<string, UnitChange>> _unitChangeByType;
    private readonly Dictionary<string, EuropeanNation> _europeanNationById;

    private Ruleset(
        Dictionary<string, TerrainType> terrainById,
        Dictionary<string, UnitType> unitById,
        Dictionary<string, GoodsType> goodsById,
        Dictionary<string, BuildingType> buildingById,
        Dictionary<string, FoundingFather> fatherById,
        Dictionary<string, ResourceType> resourceById,
        Dictionary<string, NativeNationType> nativeNationById,
        Dictionary<string, SettlementType> settlementById,
        Dictionary<string, RoleType> roleById,
        Dictionary<string, Dictionary<string, UnitChange>> unitChangeByType,
        Dictionary<string, EuropeanNation> europeanNationById)
    {
        _terrainById = terrainById;
        _unitById = unitById;
        _goodsById = goodsById;
        _buildingById = buildingById;
        _fatherById = fatherById;
        _resourceById = resourceById;
        _nativeNationById = nativeNationById;
        _settlementById = settlementById;
        _roleById = roleById;
        _unitChangeByType = unitChangeByType;
        _europeanNationById = europeanNationById;
        TerrainTypes = _terrainById.Values.ToList();
        UnitTypes = _unitById.Values.ToList();
        GoodsTypes = _goodsById.Values.ToList();
        BuildingTypes = _buildingById.Values.ToList();
        // Building-material goods = every goods id any building requires to construct (classic: hammers + tools).
        // DEVIATION: FreeCol derives `isBuildingMaterial` over ALL buildable types (buildings + units + roles), so
        // its set also includes model.goods.food (the freeColonist "costs" 200 food to grow) and the role goods
        // muskets/horses. We derive from the building subset only — we don't model unit/role required-goods. The
        // role goods are military (caught earlier in the native tribute-demand ladder), so only FOOD is missing:
        // under Angry/Hateful FreeCol natives often demand food via this rung where we instead demand the priciest
        // storable stack (food has no market value, so a trade/raw good wins). Documented in natives.md; a faithful
        // food-demand would require parsing unit/role required-goods (follow-up).
        BuildingMaterials = _buildingById.Values
            .SelectMany(b => b.BuildCost)
            .Select(g => g.GoodsId)
            .ToHashSet();
        FoundingFathers = _fatherById.Values.ToList();
        ResourceTypes = _resourceById.Values.ToList();
        NativeNationTypes = _nativeNationById.Values.ToList();
        SettlementTypes = _settlementById.Values.ToList();
        Roles = _roleById.Values.ToList();
        EuropeanNations = _europeanNationById.Values.ToList();
    }

    /// <summary>All terrain types, in specification order.</summary>
    public IReadOnlyList<TerrainType> TerrainTypes { get; }

    /// <summary>All concrete (non-abstract) unit types, in specification order.</summary>
    public IReadOnlyList<UnitType> UnitTypes { get; }

    /// <summary>Looks up a terrain type by ruleset id (e.g. <c>model.tile.plains</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public TerrainType Terrain(string id) =>
        _terrainById.TryGetValue(id, out var t)
            ? t
            : throw new KeyNotFoundException($"Unknown terrain type '{id}'.");

    /// <summary>Looks up a unit type by ruleset id (e.g. <c>model.unit.freeColonist</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public UnitType Unit(string id) =>
        _unitById.TryGetValue(id, out var u)
            ? u
            : throw new KeyNotFoundException($"Unknown unit type '{id}'.");

    /// <summary>All goods types, in specification order.</summary>
    public IReadOnlyList<GoodsType> GoodsTypes { get; }

    /// <summary>Looks up a goods type by ruleset id (e.g. <c>model.goods.sugar</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public GoodsType Goods(string id) =>
        _goodsById.TryGetValue(id, out var g)
            ? g
            : throw new KeyNotFoundException($"Unknown goods type '{id}'.");

    /// <summary>
    /// The warehouse id a goods type stores as (grain → food); unknown ids pass
    /// through unchanged so test rulesets without goods stay usable.
    /// </summary>
    public string StorageIdOf(string goodsId) =>
        _goodsById.TryGetValue(goodsId, out var g) ? g.StoredAs : goodsId;

    /// <summary>All building types, in specification order.</summary>
    public IReadOnlyList<BuildingType> BuildingTypes { get; }

    /// <summary>
    /// The set of goods ids that any building requires to construct (classic: <c>model.goods.hammers</c> +
    /// <c>model.goods.tools</c>) — the "building material" category used by native tribute-demand goods selection
    /// (FreeCol <c>GoodsType.isBuildingMaterial</c>, derived from buildable required-goods).
    /// </summary>
    public IReadOnlySet<string> BuildingMaterials { get; }

    /// <summary>Looks up a building type by ruleset id (e.g. <c>model.building.townHall</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public BuildingType Building(string id) =>
        _buildingById.TryGetValue(id, out var b)
            ? b
            : throw new KeyNotFoundException($"Unknown building type '{id}'.");

    /// <summary>Looks up a building type by id without throwing; returns null when no such building exists (used to tell a queued building id from a unit id).</summary>
    public BuildingType? FindBuilding(string id) => _buildingById.GetValueOrDefault(id);

    /// <summary>
    /// Looks up a <em>colony-constructable</em> unit type by id; null when the id is not one. A build-queue unit
    /// is one that needs a <b>building material</b> (hammers/tools) — this excludes the free colonist, whose
    /// <c>required-goods food=200</c> is the born-in-colony growth threshold (a separate mechanism), not a
    /// build-menu item (FreeCol keeps colonists in a distinct population queue).
    /// </summary>
    public UnitType? FindBuildableUnit(string id) =>
        _unitById.TryGetValue(id, out var u) && IsColonyBuildableUnit(u) ? u : null;

    /// <summary>Unit types that can be constructed in a colony (artillery, wagon train, ships), in specification order.</summary>
    public IEnumerable<UnitType> BuildableUnitTypes => UnitTypes.Where(IsColonyBuildableUnit);

    /// <summary>A unit the colony build queue can hold: it costs a building material (hammers/tools) and is not a person (colonist).</summary>
    private bool IsColonyBuildableUnit(UnitType unit) =>
        !unit.IsPerson && unit.BuildCostOrEmpty.Any(c => BuildingMaterials.Contains(c.GoodsId));

    /// <summary>All Founding Fathers, in specification order.</summary>
    public IReadOnlyList<FoundingFather> FoundingFathers { get; }

    /// <summary>All bonus-resource types, in specification order.</summary>
    public IReadOnlyList<ResourceType> ResourceTypes { get; }

    /// <summary>Looks up a bonus-resource type by ruleset id (e.g. <c>model.resource.minerals</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public ResourceType Resource(string id) =>
        _resourceById.TryGetValue(id, out var r)
            ? r
            : throw new KeyNotFoundException($"Unknown resource type '{id}'.");

    /// <summary>All native nation types (Apache, Sioux, …), in specification order.</summary>
    public IReadOnlyList<NativeNationType> NativeNationTypes { get; }

    /// <summary>Looks up a native nation type by ruleset id (e.g. <c>model.nationType.apache</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public NativeNationType NativeNation(string id) =>
        _nativeNationById.TryGetValue(id, out var n)
            ? n
            : throw new KeyNotFoundException($"Unknown native nation type '{id}'.");

    /// <summary>All native settlement types (camp/village/city + capital variants), in specification order.</summary>
    public IReadOnlyList<SettlementType> SettlementTypes { get; }

    /// <summary>Looks up a native settlement type by ruleset id (e.g. <c>model.settlement.camp</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public SettlementType Settlement(string id) =>
        _settlementById.TryGetValue(id, out var s)
            ? s
            : throw new KeyNotFoundException($"Unknown settlement type '{id}'.");

    /// <summary>All military/equipment roles (default, soldier, dragoon, scout, brave roles, …), in specification order.</summary>
    public IReadOnlyList<RoleType> Roles { get; }

    /// <summary>Looks up a role by ruleset id (e.g. <c>model.role.soldier</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public RoleType Role(string id) =>
        _roleById.TryGetValue(id, out var r)
            ? r
            : throw new KeyNotFoundException($"Unknown role '{id}'.");

    /// <summary>
    /// All European nations (the colonial powers and their Royal Expeditionary Forces), in specification
    /// order. The classic playable powers are the non-REF selectable ones (Dutch, French, English, Spanish).
    /// </summary>
    public IReadOnlyList<EuropeanNation> EuropeanNations { get; }

    /// <summary>Looks up a European nation by ruleset id (e.g. <c>model.nation.dutch</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public EuropeanNation EuropeanNation(string id) =>
        _europeanNationById.TryGetValue(id, out var n)
            ? n
            : throw new KeyNotFoundException($"Unknown European nation '{id}'.");

    /// <summary>
    /// The unit-type change of a given change-type for a unit type, or null if that type
    /// does not change (FreeCol <c>UnitType.getUnitChange</c>). E.g.
    /// <c>GetUnitChange(UnitChangeTypeIds.Promotion, "model.unit.freeColonist")</c> → veteran soldier.
    /// </summary>
    public UnitChange? GetUnitChange(string changeTypeId, string fromUnitId) =>
        _unitChangeByType.TryGetValue(changeTypeId, out var byFrom)
        && byFrom.TryGetValue(fromUnitId, out var change)
            ? change
            : null;

    /// <summary>
    /// The role a victor in <paramref name="winnerRoleId"/> upgrades to by capturing the equipment of a
    /// defeated unit in <paramref name="loserRoleId"/>, or null if none is available to that owner
    /// (FreeCol <c>Unit.canCaptureEquipment</c>): the military role whose <c>role-change</c> matches, gated
    /// to the winner's side (native roles for natives, non-REF roles for non-REF units).
    /// </summary>
    public RoleType? CaptureRole(string winnerRoleId, string loserRoleId, bool winnerIsNative) =>
        Roles.FirstOrDefault(r =>
            r.RequiresNative == winnerIsNative
            && !r.RequiresRef
            && r.RoleChanges.Any(rc => rc.From == winnerRoleId && rc.Capture == loserRoleId));

    /// <summary>Looks up a Founding Father by ruleset id (e.g. <c>model.foundingFather.adamSmith</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public FoundingFather Father(string id) =>
        _fatherById.TryGetValue(id, out var f)
            ? f
            : throw new KeyNotFoundException($"Unknown founding father '{id}'.");

    /// <summary>
    /// Loads the classic (1994-faithful, Colonial-America) ruleset embedded in this
    /// assembly. Convenience for the default variant; equivalent to
    /// <c>GameVariants.ClassicAmerica.LoadRuleset()</c>.
    /// </summary>
    public static Ruleset LoadClassic() => LoadEmbedded(GameVariants.ClassicSpecResource);

    /// <summary>
    /// Loads a ruleset from a specification embedded in this assembly (used by
    /// <see cref="GameVariant.LoadRuleset"/> to load the selected variant's data).
    /// </summary>
    /// <param name="resourceName">Manifest resource name of the embedded <c>specification.xml</c>.</param>
    /// <exception cref="InvalidOperationException">No embedded resource with that name exists.</exception>
    public static Ruleset LoadEmbedded(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded ruleset '{resourceName}' missing from assembly.");
        // Per-nation colony names ship as a sibling resource (FreeCol keeps them out of the spec XML).
        // A variant supplies its own by following the naming convention; absent names → no colony-name lists.
        string colonyNamesResource = resourceName.Replace("specification.xml", "european-nation-names.properties");
        using Stream? colonyNames = assembly.GetManifestResourceStream(colonyNamesResource);
        return Load(stream, colonyNames);
    }

    /// <summary>Parses a ruleset from FreeCol-format specification XML.</summary>
    /// <param name="xml">The specification XML stream.</param>
    /// <param name="colonyNames">Optional FreeCol-format per-nation colony-name properties (null → European nations get empty colony-name lists).</param>
    /// <exception cref="RulesetFormatException">The XML is missing required elements or attributes.</exception>
    public static Ruleset Load(Stream xml, Stream? colonyNames = null)
    {
        XDocument doc = XDocument.Load(xml);
        XElement root = doc.Root
            ?? throw new RulesetFormatException("Specification has no root element.");

        XElement tileTypes = root.Element("tile-types")
            ?? throw new RulesetFormatException("Specification has no <tile-types> section.");

        var terrain = new Dictionary<string, TerrainType>();
        foreach (XElement el in tileTypes.Elements("tile-type"))
        {
            TerrainType parsed = ParseTileType(el);
            if (!terrain.TryAdd(parsed.Id, parsed))
            {
                throw new RulesetFormatException($"Duplicate tile-type id '{parsed.Id}'.");
            }
        }

        if (terrain.Count == 0)
        {
            throw new RulesetFormatException("Specification defines no tile types.");
        }

        XElement unitTypes = root.Element("unit-types")
            ?? throw new RulesetFormatException("Specification has no <unit-types> section.");
        Dictionary<string, UnitType> units = ParseUnitTypes(unitTypes);

        var goods = new Dictionary<string, GoodsType>();
        foreach (XElement el in root.Element("goods-types")?.Elements("goods-type") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            XElement? market = el.Element("market");
            goods[id] = new GoodsType(
                Id: id,
                IsFood: (bool?)el.Attribute("is-food") ?? false,
                StoredAs: (string?)el.Attribute("stored-as") ?? id,
                MadeFrom: (string?)el.Attribute("made-from"),
                IsFarmed: (bool?)el.Attribute("is-farmed") ?? false,
                BreedingNumber: (int?)el.Attribute("breeding-number"),
                IsNewWorldGoods: (bool?)el.Attribute("new-world-goods") ?? false,
                IsStorable: (bool?)el.Attribute("storable") ?? true,
                IsMilitary: (bool?)el.Attribute("is-military") ?? false,
                IsTradeGoods: (bool?)el.Attribute("trade-goods") ?? false,
                Market: market is null ? null : new GoodsMarket(
                    InitialAmount: (int?)market.Attribute("initial-amount")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks initial-amount."),
                    InitialPrice: (int?)market.Attribute("initial-price")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks initial-price."),
                    PriceDifference: (int?)market.Attribute("price-difference")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks price-difference.")));
        }

        var buildings = new Dictionary<string, BuildingType>();
        var buildingElements = new Dictionary<string, XElement>();
        foreach (XElement el in root.Element("building-types")?.Elements("building-type") ?? [])
        {
            buildingElements[RequiredAttribute(el, "id")] = el;
        }
        foreach ((string id, XElement el) in buildingElements)
        {
            // Productions: the building's own if defined, else inherited up the
            // extends chain. Attributes: nearest definition wins (FreeCol default
            // workplaces = 3).
            XElement? productionSource = el;
            while (productionSource is not null && !productionSource.Elements("production").Any())
            {
                string? parent = (string?)productionSource.Attribute("extends");
                productionSource = parent is not null ? buildingElements.GetValueOrDefault(parent) : null;
            }

            buildings[id] = new BuildingType(
                Id: id,
                UpgradesFrom: (string?)el.Attribute("upgrades-from"),
                Workplaces: ResolveIntAttribute(el, "workplaces", buildingElements) ?? 3,
                RequiredPopulation: ResolveIntAttribute(el, "required-population", buildingElements) ?? 1,
                Productions: (productionSource?.Elements("production") ?? [])
                    .Select(ParseProduction)
                    .ToList(),
                BuildCost: el.Elements("required-goods")
                    .Select(g => new GoodsOutput(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in '{id}' lacks value.")))
                    .ToList(),
                // The colony defence bonus (stockade +100, fort +150, fortress +200): the building's own
                // model.modifier.defence percentage. Skip any `delete="true"` marker (fort/fortress delete the
                // inherited modifier then re-add their own), taking the valued one. 0 for non-defence buildings.
                DefenceBonus: el.Elements("modifier")
                    .Where(m => (string?)m.Attribute("id") == "model.modifier.defence"
                                && (string?)m.Attribute("delete") != "true")
                    .Select(m => (int?)m.Attribute("value") ?? 0)
                    .DefaultIfEmpty(0)
                    .Last(),
                // Warehouse capacity: the model.modifier.warehouseStorage additive, summed up the extends chain
                // (depot 100; warehouse extends depot → 200; expansion extends warehouse → 300) — FreeCol resolves
                // an extending building's modifiers cumulatively, unlike the redefined (delete+readd) defence one.
                WarehouseStorage: SumModifierUpChain(el, "model.modifier.warehouseStorage", buildingElements),
                // Bell-production bonus (printing press +50, newspaper +100): the building's own model.goods.bells
                // percentage modifier, taking the valued (non-delete) one — the newspaper deletes the inherited 50
                // and re-adds 100, the same redefine pattern as the defence modifier above.
                BellBonus: el.Elements("modifier")
                    .Where(m => (string?)m.Attribute("id") == "model.goods.bells"
                                && (string?)m.Attribute("delete") != "true")
                    .Select(m => (int?)m.Attribute("value") ?? 0)
                    .DefaultIfEmpty(0)
                    .Last(),
                // Build-gating abilities (factory tier → buildFactory, custom house → buildCustomHouse, docks →
                // hasPort), collected down the extends chain so drydock/shipyard inherit docks' hasPort (FreeCol
                // BuildableType.getRequiredAbilities; nearest definition wins for a re-stated id).
                RequiredAbilities: CollectRequiredAbilitiesUpChain(el, buildingElements),
                // Ship repair: drydock grants model.ability.repairUnits, shipyard inherits it up the extends chain.
                RepairsNavalUnits: ResolveAbility(el, "model.ability.repairUnits", buildingElements),
                // Unit construction: model.ability.build scopes (carpenter's house → wagon train, armory →
                // artillery, shipyard → any naval unit), collected down the extends chain (magazine/arsenal inherit
                // armory's artillery scope) — drives the unit build-ability gate.
                BuildableUnitTypeIds: CollectBuildUnitTypeScopes(el, buildingElements),
                BuildsNavalUnits: GrantsNavalBuildScope(el, buildingElements));
        }

        var fathers = new Dictionary<string, FoundingFather>();
        foreach (XElement el in root.Element("founding-fathers")?.Elements("founding-father") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            string typeName = RequiredAttribute(el, "type");
            if (!Enum.TryParse(typeName, ignoreCase: true, out FatherType type))
            {
                throw new RulesetFormatException($"Founding father '{id}' has unknown type '{typeName}'.");
            }
            fathers[id] = new FoundingFather(
                Id: id,
                Type: type,
                Weight1: (int?)el.Attribute("weight1") ?? 0,
                Weight2: (int?)el.Attribute("weight2") ?? 0,
                Weight3: (int?)el.Attribute("weight3") ?? 0,
                Modifiers: el.Elements("modifier").Select(ParseModifier).ToList(),
                Abilities: el.Elements("ability").Select(ParseAbility).ToList(),
                // Free buildings (FreeCol model.event.freeBuilding): La Salle → a free stockade per qualifying colony.
                FreeBuildings: el.Elements("event")
                    .Where(e => (string?)e.Attribute("id") == "model.event.freeBuilding")
                    .Select(e => RequiredAttribute(e, "value"))
                    .ToList());
        }

        var resources = new Dictionary<string, ResourceType>();
        foreach (XElement el in root.Element("resource-types")?.Elements("resource-type") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            resources[id] = new ResourceType(
                Id: id,
                Modifiers: el.Elements("modifier").Select(ParseResourceModifier).ToList());
        }

        (Dictionary<string, NativeNationType> nativeNations, Dictionary<string, SettlementType> settlements) =
            ParseNativeNationTypes(root.Element("indian-nation-types"));

        Dictionary<string, RoleType> roles = ParseRoles(root.Element("roles"));
        Dictionary<string, Dictionary<string, UnitChange>> unitChanges =
            ParseUnitChanges(root.Element("unit-change-types"));

        Dictionary<string, EuropeanNationType> europeanNationTypes =
            ParseEuropeanNationTypes(root.Element("european-nation-types"));
        Dictionary<string, EuropeanNation> europeanNations = ParseEuropeanNations(
            root.Element("nations"), europeanNationTypes, ParseColonyNames(colonyNames));

        return new Ruleset(
            terrain, units, goods, buildings, fathers, resources, nativeNations, settlements,
            roles, unitChanges, europeanNations);
    }

    /// <summary>
    /// Parses the <c>&lt;european-nation-types&gt;</c> section, resolving <c>extends</c> chains: starting
    /// units are taken from the nearest level that defines a slot (keeping a slot's expert variant), while
    /// abilities and modifiers accumulate from the whole chain. A null section yields no types.
    /// </summary>
    private static Dictionary<string, EuropeanNationType> ParseEuropeanNationTypes(XElement? section)
    {
        var types = new Dictionary<string, EuropeanNationType>();
        if (section is null)
        {
            return types;
        }

        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in section.Elements("european-nation-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate european-nation-type id '{id}'.");
            }
        }

        foreach ((string id, XElement el) in elements)
        {
            var chain = ExtendsChain(el, elements).ToList(); // leaf → root
            var bySlot = new Dictionary<string, List<EuropeanStartingUnit>>();
            foreach (XElement level in chain)
            {
                foreach (var slot in level.Elements("unit").GroupBy(u => RequiredAttribute(u, "id")))
                {
                    if (bySlot.ContainsKey(slot.Key))
                    {
                        continue; // a nearer level already defined this slot (override)
                    }
                    bySlot[slot.Key] = slot.Select(u => new EuropeanStartingUnit(
                        Slot: slot.Key,
                        UnitTypeId: RequiredAttribute(u, "type"),
                        RoleId: (string?)u.Attribute("role"),
                        Mounted: (bool?)u.Attribute("mounted") ?? false,
                        Expert: (bool?)u.Attribute("expert-starting-units") ?? false)).ToList();
                }
            }
            types[id] = new EuropeanNationType(
                Id: id,
                IsRef: (bool?)el.Attribute("ref") ?? false,
                StartingUnits: bySlot.Values.SelectMany(u => u).ToList(),
                Abilities: chain.SelectMany(c => c.Elements("ability")).Select(ParseAbility).ToList(),
                Modifiers: chain.SelectMany(c => c.Elements("modifier")).Select(ParseModifier).ToList());
        }
        return types;
    }

    /// <summary>
    /// Parses the European <c>&lt;nation&gt;</c> rows, resolving each to its <see cref="EuropeanNationType"/>
    /// and per-nation colony names. Nations whose <c>nation-type</c> is not a European type (the native
    /// nations) and the <c>unknownEnemy</c> pseudo-nation are skipped. A null section yields no nations.
    /// </summary>
    private static Dictionary<string, EuropeanNation> ParseEuropeanNations(
        XElement? section,
        Dictionary<string, EuropeanNationType> types,
        Dictionary<string, IReadOnlyList<string>> colonyNamesByNation)
    {
        var nations = new Dictionary<string, EuropeanNation>();
        foreach (XElement el in section?.Elements("nation") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            if (id == "model.nation.unknownEnemy")
            {
                continue; // the no-owner pseudo-nation, not a real power
            }
            // Native nations carry an indian-nation-type here — they are handled separately; skip them.
            if (!types.TryGetValue(RequiredAttribute(el, "nation-type"), out EuropeanNationType? type))
            {
                continue;
            }
            nations[id] = new EuropeanNation(
                Id: id,
                DisplayName: DeriveNationDisplayName(id),
                NationType: type,
                Color: (string?)el.Attribute("color"),
                Selectable: (bool?)el.Attribute("selectable") ?? false,
                RefNationId: (string?)el.Attribute("ref"),
                ColonyNames: colonyNamesByNation.GetValueOrDefault(id, []));
        }
        return nations;
    }

    /// <summary>A player-facing nation name derived from its id: <c>model.nation.dutch</c> → <c>Dutch</c>.</summary>
    private static string DeriveNationDisplayName(string id)
    {
        string shortName = id[(id.LastIndexOf('.') + 1)..];
        return shortName.Length == 0 ? shortName : char.ToUpperInvariant(shortName[0]) + shortName[1..];
    }

    /// <summary>
    /// Parses FreeCol-format colony-name properties (<c>model.nation.&lt;id&gt;.settlementName.classic.&lt;n&gt;=Name</c>)
    /// into per-nation lists ordered by index. A null stream yields no names.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> ParseColonyNames(Stream? properties)
    {
        var byNation = new Dictionary<string, List<(int Index, string Name)>>();
        if (properties is not null)
        {
            const string marker = ".settlementName.classic.";
            using var reader = new StreamReader(properties);
            for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            {
                int eq = line.IndexOf('=');
                if (line.StartsWith('#') || eq < 0)
                {
                    continue;
                }
                string key = line[..eq].Trim();
                int m = key.IndexOf(marker, StringComparison.Ordinal);
                if (m < 0 || !int.TryParse(key[(m + marker.Length)..], out int index))
                {
                    continue;
                }
                string nationId = key[..m];
                if (!byNation.TryGetValue(nationId, out var list))
                {
                    byNation[nationId] = list = [];
                }
                list.Add((index, line[(eq + 1)..].Trim()));
            }
        }
        return byNation.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.OrderBy(x => x.Index).Select(x => x.Name).ToList());
    }

    /// <summary>
    /// Parses the <c>&lt;roles&gt;</c> section into role types. Offence/defence are summed from each
    /// role's index-30 additive combat modifiers; required-goods, abilities, downgrade and capture
    /// rules carry over verbatim. A null section (minimal test rulesets) yields no roles.
    /// </summary>
    private static Dictionary<string, RoleType> ParseRoles(XElement? section)
    {
        var roles = new Dictionary<string, RoleType>();
        foreach (XElement el in section?.Elements("role") ?? [])
        {
            string id = RequiredAttribute(el, "id");

            double SumModifier(string modifierId) => el.Elements("modifier")
                .Where(m => (string?)m.Attribute("id") == modifierId
                            && (string?)m.Attribute("type") is null or "additive")
                .Sum(m => (double?)m.Attribute("value") ?? 0);

            var parsed = new RoleType(
                Id: id,
                Downgrade: (string?)el.Attribute("downgrade"),
                MaximumCount: (int?)el.Attribute("maximum-count") ?? 1,
                ExpertUnit: (string?)el.Attribute("expert-unit"),
                Offence: SumModifier("model.modifier.offence"),
                Defence: SumModifier("model.modifier.defence"),
                MovementBonus: SumModifier("model.modifier.movementBonus"),
                LineOfSightBonus: SumModifier("model.modifier.lineOfSightBonus"),
                RequiredGoods: el.Elements("required-goods")
                    .Select(g => new RoleRequiredGoods(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in role '{id}' lacks value.")))
                    .ToList(),
                RequiredAbilities: el.Elements("required-ability")
                    .ToDictionary(a => RequiredAttribute(a, "id"), a => (bool?)a.Attribute("value") ?? true),
                GrantedAbilities: el.Elements("ability")
                    .ToDictionary(a => RequiredAttribute(a, "id"), a => (bool?)a.Attribute("value") ?? true),
                RoleChanges: el.Elements("role-change")
                    .Select(rc => new RoleChange(RequiredAttribute(rc, "from"), RequiredAttribute(rc, "capture")))
                    .ToList());

            if (!roles.TryAdd(id, parsed))
            {
                throw new RulesetFormatException($"Duplicate role id '{id}'.");
            }
        }
        return roles;
    }

    /// <summary>
    /// Parses the <c>&lt;unit-change-types&gt;</c> section into a [change-type id][from-unit id] → change
    /// map. A null section yields no changes (combat then never promotes/demotes/captures by type).
    /// </summary>
    private static Dictionary<string, Dictionary<string, UnitChange>> ParseUnitChanges(XElement? section)
    {
        var byType = new Dictionary<string, Dictionary<string, UnitChange>>();
        foreach (XElement typeEl in section?.Elements("unit-change-type") ?? [])
        {
            string changeTypeId = RequiredAttribute(typeEl, "id");
            var byFrom = new Dictionary<string, UnitChange>();
            foreach (XElement change in typeEl.Elements("unit-type-change"))
            {
                string from = RequiredAttribute(change, "from");
                // Nearest definition wins on the rare duplicate from-row (none in classic combat sets).
                byFrom[from] = new UnitChange(
                    From: from,
                    To: RequiredAttribute(change, "to"),
                    Probability: (int?)change.Attribute("probability") ?? 0);
            }
            byType[changeTypeId] = byFrom;
        }
        return byType;
    }

    private static ResourceModifier ParseResourceModifier(XElement m) => new(
        GoodsId: RequiredAttribute(m, "id"),
        Type: (string?)m.Attribute("type") switch
        {
            "multiplicative" => ModifierType.Multiplicative,
            "percentage" => ModifierType.Percentage,
            _ => ModifierType.Additive,
        },
        Value: (double?)m.Attribute("value") ?? 0,
        Index: (int?)m.Attribute("index") ?? 0,
        ScopeUnitTypes: m.Elements("scope")
            .Select(s => (string?)s.Attribute("type"))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList());

    private static FatherModifier ParseModifier(XElement m) => new(
        TargetId: RequiredAttribute(m, "id"),
        Type: (string?)m.Attribute("type") switch
        {
            "multiplicative" => ModifierType.Multiplicative,
            "percentage" => ModifierType.Percentage,
            _ => ModifierType.Additive,
        },
        Value: (double?)m.Attribute("value") ?? 0,
        Index: (int?)m.Attribute("index") ?? 0,
        // <scope type="model.unit.privateer"/> children restrict the modifier (Francis Drake → privateers only).
        ScopeUnitTypes: m.Elements("scope")
            .Select(s => (string?)s.Attribute("type"))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList());

    private static FatherAbility ParseAbility(XElement a) => new(
        Id: RequiredAttribute(a, "id"),
        Value: (bool?)a.Attribute("value") ?? true,
        ScopeTypes: a.Elements("scope")
            .Select(s => (string?)s.Attribute("type"))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList());

    private static ProductionEntry ParseProduction(XElement p) => new(
        Unattended: (bool?)p.Attribute("unattended") ?? false,
        Outputs: p.Elements("output")
            .Select(o => new GoodsOutput(
                RequiredAttribute(o, "goods-type"),
                (int?)o.Attribute("value")
                    ?? throw new RulesetFormatException("<output> lacks a value.")))
            .ToList(),
        Inputs: p.Elements("input")
            .Select(o => new GoodsOutput(
                RequiredAttribute(o, "goods-type"),
                (int?)o.Attribute("value")
                    ?? throw new RulesetFormatException("<input> lacks a value.")))
            .ToList());

    /// <summary>
    /// Parses unit types, resolving the spec's <c>extends</c> inheritance chains
    /// (attributes: nearest definition wins; abilities: any ancestor may set them).
    /// Abstract types participate in resolution but are not exposed.
    /// </summary>
    private static Dictionary<string, UnitType> ParseUnitTypes(XElement unitTypes)
    {
        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in unitTypes.Elements("unit-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate unit-type id '{id}'.");
            }
        }

        var units = new Dictionary<string, UnitType>();
        foreach ((string id, XElement el) in elements)
        {
            if ((bool?)el.Attribute("abstract") ?? false)
            {
                continue;
            }

            units[id] = new UnitType(
                Id: id,
                Movement: ResolveIntAttribute(el, "movement", elements) ?? 3,
                LineOfSight: ResolveIntAttribute(el, "line-of-sight", elements) ?? 1,
                IsNaval: ResolveAbility(el, "model.ability.navalUnit", elements),
                CanFoundColony: ResolveAbility(el, "model.ability.foundColony", elements),
                // recruit-probability is a direct attribute in the spec (not inherited
                // via extends), so it is read off the concrete type only.
                RecruitProbability: (int?)el.Attribute("recruit-probability") ?? 0,
                IsPerson: ResolveAbility(el, "model.ability.person", elements),
                // space (cargo capacity) and spaceTaken (carry cost) inherit up the
                // extends chain in FreeCol; defaults match UnitType (0 and 1).
                Space: ResolveIntAttribute(el, "space", elements) ?? 0,
                SpaceTaken: ResolveIntAttribute(el, "spaceTaken", elements) ?? 1,
                // price = Europe purchase/training cost (0 if absent; manOWar uses
                // mercenary-price, not price, so it stays non-purchasable here).
                Price: ResolveIntAttribute(el, "price", elements) ?? 0,
                // Combat power, split at the role-modifier index (30) so a unit's role additive can
                // fold in at the correct point: the pre-role additive base (attribute + the type's own
                // index-<30 additive modifiers, e.g. king's regular +4) and the post-role multiplier
                // (the type's own index-≥30 percentage modifiers, e.g. veteran soldier +50% → ×1.5).
                // Offence/Defence are the unarmed fold (additive × multiplier), kept for callers that
                // want a unit's bare power; the combat code recombines with the role (see Game.OffenceBase).
                OffenceAdditive: ResolveCombatPower(el, "offence", "model.modifier.offence", elements).Additive,
                DefenceAdditive: ResolveCombatPower(el, "defence", "model.modifier.defence", elements).Additive,
                OffenceMultiplier: ResolveCombatPower(el, "offence", "model.modifier.offence", elements).Multiplier,
                DefenceMultiplier: ResolveCombatPower(el, "defence", "model.modifier.defence", elements).Multiplier,
                Offence: ResolveCombatPower(el, "offence", "model.modifier.offence", elements).Folded,
                Defence: ResolveCombatPower(el, "defence", "model.modifier.defence", elements).Folded,
                // Combat-outcome abilities (drive the loser/winner precedence; default false).
                DisposeOnCombatLoss: ResolveAbility(el, "model.ability.disposeOnCombatLoss", elements),
                CanBeCaptured: ResolveAbility(el, "model.ability.canBeCaptured", elements),
                CaptureUnits: ResolveAbility(el, "model.ability.captureUnits", elements),
                CaptureEquipment: ResolveAbility(el, "model.ability.captureEquipment", elements),
                DisposeOnAllEquipmentLost: ResolveAbility(el, "model.ability.disposeOnAllEquipLost", elements),
                DemoteOnAllEquipmentLost: ResolveAbility(el, "model.ability.demoteOnAllEquipLost", elements),
                Bombard: ResolveAbility(el, "model.ability.bombard", elements),
                // hit-points: every concrete ship sets 6 directly (the abstract `ship` base omits it); resolved
                // through the extends chain like other ints. Default 1 for the non-naval types that never set it.
                MaxHitPoints: ResolveIntAttribute(el, "hit-points", elements) ?? 1,
                // captureGoods: a naval raider loots a beaten ship's hold (frigate, privateer, man-o-war).
                CaptureGoods: ResolveAbility(el, "model.ability.captureGoods", elements),
                // piracy: a privateer attacks rivals without declaring war, flying no flag.
                Piracy: ResolveAbility(el, "model.ability.piracy", elements),
                // Colony construction: the unit's own required-goods (artillery hammers 192 + tools 40, wagon
                // train hammers 40); required-population (FreeCol default 1) and required-abilities (collected
                // down the extends chain, as for buildings — ships' navalUnit-scoped build gate rides here).
                BuildCost: el.Elements("required-goods")
                    .Select(g => new GoodsOutput(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in unit '{id}' lacks value.")))
                    .ToList(),
                RequiredPopulation: ResolveIntAttribute(el, "required-population", elements) ?? 1,
                RequiredAbilities: CollectRequiredAbilitiesUpChain(el, elements),
                // Build cap (classic: the wagon-train limit "units lt settlements" at player scope — at most one
                // wagon train per colony). Only the units/settlements operands are evaluated (see UnitBuildLimit).
                BuildLimit: el.Element("limit") is { } lim
                    ? new UnitBuildLimit(
                        (string?)lim.Attribute("operator") ?? "lt",
                        (string?)lim.Element("left-hand-side")?.Attribute("operand-type") ?? "",
                        (string?)lim.Element("right-hand-side")?.Attribute("operand-type") ?? "")
                    : null);
        }

        if (units.Count == 0)
        {
            throw new RulesetFormatException("Specification defines no concrete unit types.");
        }
        return units;
    }

    /// <summary>The index at which a unit's role contributes its offence/defence (FreeCol roles use index 30).</summary>
    private const int RoleCombatIndex = 30;

    /// <summary>
    /// A unit type's combat power, split at the role-modifier index so a role additive can fold at the
    /// correct point (FreeCol applies modifiers in one ascending-index pass; roles sit at index 30
    /// between the type's additive modifiers at 20 and its percentage modifiers at 40):
    /// <list type="bullet">
    /// <item><c>Additive</c> — the base <paramref name="attribute"/> with the type's own index-&lt;30
    /// modifiers folded in (king's regular +4, colonial regular +3); a role additive adds to this.</item>
    /// <item><c>Multiplier</c> — the product of the type's index-≥30 percentage/multiplicative modifiers
    /// (veteran soldier +50% → 1.5), applied to <c>Additive + roleAdditive</c>.</item>
    /// <item><c>Folded</c> — <c>Additive × Multiplier</c>, the unit's bare (unarmed) power.</item>
    /// </list>
    /// </summary>
    private static (double Additive, double Multiplier, double Folded) ResolveCombatPower(
        XElement el, string attribute, string modifierId, Dictionary<string, XElement> elements)
    {
        var modifiers = new List<(double Value, ModifierType Type, int Index)>();
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement m in current.Elements("modifier")
                         .Where(m => (string?)m.Attribute("id") == modifierId))
            {
                modifiers.Add((
                    (double?)m.Attribute("value") ?? 0,
                    (string?)m.Attribute("type") switch
                    {
                        "multiplicative" => ModifierType.Multiplicative,
                        "percentage" => ModifierType.Percentage,
                        _ => ModifierType.Additive,
                    },
                    (int?)m.Attribute("index") ?? 0));
            }
        }

        double additive = ResolveIntAttribute(el, attribute, elements) ?? 0;
        double multiplier = 1.0;
        foreach ((double value, ModifierType type, int index) in modifiers.OrderBy(m => m.Index))
        {
            if (index < RoleCombatIndex)
            {
                additive = ModifierMath.Apply(type, additive, value); // pre-role: fold into the base
            }
            else
            {
                // Post-role: applies after the role additive. Percentage/multiplicative accumulate as a
                // multiplier (×(1+v/100) / ×v); classic combat data has no post-role additive.
                multiplier = ModifierMath.Apply(type, multiplier, value);
            }
        }
        return (additive, multiplier, additive * multiplier);
    }

    /// <summary>Walks the extends chain until an element defines the attribute. Defaults match FreeCol's UnitType.</summary>
    private static int? ResolveIntAttribute(
        XElement el, string name, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            if ((int?)current.Attribute(name) is int value)
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>
    /// Sums an additive modifier's value across the whole extends chain (cumulative inheritance) — used for
    /// <c>model.modifier.warehouseStorage</c>, where an extending building <em>adds to</em> its parent's value
    /// (depot 100 → warehouse 200 → expansion 300), unlike a redefined modifier.
    /// </summary>
    private static int SumModifierUpChain(XElement el, string modifierId, Dictionary<string, XElement> elements)
    {
        int total = 0;
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            total += current.Elements("modifier")
                .Where(m => (string?)m.Attribute("id") == modifierId)
                .Sum(m => (int?)m.Attribute("value") ?? 0);
        }
        return total;
    }

    /// <summary>
    /// Collects a building's <c>required-ability</c> entries (id → required value) down the whole <c>extends</c>
    /// chain — a child building inherits its parent's requirements (drydock/shipyard keep docks' <c>hasPort</c>),
    /// and a re-stated id on the nearer element wins (FreeCol <c>BuildableType.getRequiredAbilities</c>).
    /// </summary>
    private static IReadOnlyDictionary<string, bool> CollectRequiredAbilitiesUpChain(
        XElement el, Dictionary<string, XElement> elements)
    {
        var result = new Dictionary<string, bool>();
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement ability in current.Elements("required-ability"))
            {
                string id = RequiredAttribute(ability, "id");
                if (!result.ContainsKey(id)) // leaf → root: the nearer (already-seen) definition wins
                {
                    result[id] = (bool?)ability.Attribute("value") ?? true;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Collects the unit-type ids a building enables constructing — the <c>&lt;scope type="…"/&gt;</c> of every
    /// <c>model.ability.build</c> ability (value true) down the whole <c>extends</c> chain (so magazine/arsenal
    /// inherit armory's artillery scope, lumber mill carpenter's-house's wagon-train scope). FreeCol
    /// <c>UnitType.canBeBuiltInColony</c> matches a colony's scoped build ability to the target unit.
    /// </summary>
    private static IReadOnlySet<string> CollectBuildUnitTypeScopes(XElement el, Dictionary<string, XElement> elements)
    {
        var result = new HashSet<string>();
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            foreach (XElement ability in current.Elements("ability")
                .Where(a => (string?)a.Attribute("id") == "model.ability.build" && ((bool?)a.Attribute("value") ?? true)))
            {
                foreach (XElement scope in ability.Elements("scope"))
                {
                    if ((string?)scope.Attribute("type") is { } unitTypeId)
                    {
                        result.Add(unitTypeId);
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// True when a building grants <c>model.ability.build</c> scoped to <c>model.ability.navalUnit</c> (the
    /// shipyard) anywhere up its <c>extends</c> chain — it can construct any ship. (Ship construction is deferred,
    /// so this is parsed for completeness only.)
    /// </summary>
    private static bool GrantsNavalBuildScope(XElement el, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            if (current.Elements("ability")
                .Where(a => (string?)a.Attribute("id") == "model.ability.build" && ((bool?)a.Attribute("value") ?? true))
                .SelectMany(a => a.Elements("scope"))
                .Any(s => (string?)s.Attribute("ability-id") == "model.ability.navalUnit"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when any element in the extends chain sets the ability to true (nearest wins).</summary>
    private static bool ResolveAbility(
        XElement el, string abilityId, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            XElement? ability = current.Elements("ability")
                .FirstOrDefault(a => (string?)a.Attribute("id") == abilityId);
            if (ability is not null)
            {
                return (bool?)ability.Attribute("value") ?? true;
            }
        }
        return false;
    }

    private static XElement? ParentOf(XElement el, Dictionary<string, XElement> elements)
    {
        string? parentId = (string?)el.Attribute("extends");
        if (parentId is null)
        {
            return null;
        }
        return elements.TryGetValue(parentId, out XElement? parent)
            ? parent
            : throw new RulesetFormatException(
                $"unit-type '{(string?)el.Attribute("id")}' extends unknown type '{parentId}'.");
    }

    private static TerrainType ParseTileType(XElement el)
    {
        string id = RequiredAttribute(el, "id");

        var productions = el.Elements("production").Select(ParseProduction).ToList();

        XElement? gen = el.Element("gen");

        return new TerrainType(
            Id: id,
            MoveCost: (int?)el.Attribute("basic-move-cost")
                ?? throw new RulesetFormatException($"Tile-type '{id}' lacks basic-move-cost."),
            WorkTurns: (int?)el.Attribute("basic-work-turns")
                ?? throw new RulesetFormatException($"Tile-type '{id}' lacks basic-work-turns."),
            IsForest: (bool?)el.Attribute("is-forest") ?? false,
            IsWater: (bool?)el.Attribute("is-water") ?? false,
            IsElevation: (bool?)el.Attribute("is-elevation") ?? false,
            CanSettle: (bool?)el.Attribute("can-settle") ?? true,
            IsConnected: (bool?)el.Attribute("is-connected") ?? false,
            Productions: productions,
            Gen: gen is null ? null : new GenRanges(
                HumidityMin: (int?)gen.Attribute("humidity-minimum") ?? 0,
                HumidityMax: (int?)gen.Attribute("humidity-maximum") ?? 100,
                TemperatureMin: (int?)gen.Attribute("temperature-minimum") ?? -20,
                TemperatureMax: (int?)gen.Attribute("temperature-maximum") ?? 40,
                AltitudeMin: (int?)gen.Attribute("altitude-minimum") ?? 0,
                AltitudeMax: (int?)gen.Attribute("altitude-maximum") ?? 30),
            Resources: el.Elements("resource")
                .Select(r => new ResourceChance(
                    RequiredAttribute(r, "type"),
                    (int?)r.Attribute("probability") ?? 100))
                .ToList(),
            // Combat defence bonus a unit gains while on this terrain (percentage modifier).
            DefenceBonus: (double?)el.Elements("modifier")
                .FirstOrDefault(m => (string?)m.Attribute("id") == "model.modifier.defence")
                ?.Attribute("value") ?? 0,
            // Concealing terrain (forests + hills) that enables an ambush.
            AmbushTerrain: el.Elements("ability")
                .Any(a => (string?)a.Attribute("id") == "model.ability.ambushTerrain" && ((bool?)a.Attribute("value") ?? true)));
    }

    /// <summary>
    /// Parses native nation types and their settlement templates from the
    /// <c>&lt;indian-nation-types&gt;</c> section, resolving the spec's <c>extends</c>
    /// chains (camp/village/city → default): attributes and the settlement template
    /// use nearest-wins; skills and regions accumulate from the chain (root first).
    /// Abstract types (default/camp/village/city) participate but are not exposed.
    /// A null section (minimal test rulesets) yields no native nations.
    /// </summary>
    private static (Dictionary<string, NativeNationType> nations, Dictionary<string, SettlementType> settlements)
        ParseNativeNationTypes(XElement? section)
    {
        var nations = new Dictionary<string, NativeNationType>();
        var settlements = new Dictionary<string, SettlementType>();
        if (section is null)
        {
            return (nations, settlements);
        }

        // Index every element (incl. abstract default/camp/village/city) for extends resolution.
        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in section.Elements("indian-nation-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate indian-nation-type id '{id}'.");
            }
        }

        // Each distinct <settlement> template (camp, camp.capital, village…, inca…, aztec…).
        foreach (XElement el in elements.Values.SelectMany(e => e.Elements("settlement")))
        {
            SettlementType parsed = ParseSettlementType(el);
            settlements[parsed.Id] = parsed; // ids are unique across the section
        }

        // Concrete nation types, in specification order.
        foreach (XElement el in section.Elements("indian-nation-type"))
        {
            if ((bool?)el.Attribute("abstract") ?? false)
            {
                continue;
            }
            string id = RequiredAttribute(el, "id");
            XElement baseSettlement = ResolveSettlementElement(el, elements, capital: false)
                ?? throw new RulesetFormatException($"indian-nation-type '{id}' has no settlement template.");
            XElement capitalSettlement = ResolveSettlementElement(el, elements, capital: true)
                ?? throw new RulesetFormatException($"indian-nation-type '{id}' has no capital settlement template.");

            // Chain ordered root → leaf so inherited skills/regions come before the nation's own.
            var rootToLeaf = ExtendsChain(el, elements).Reverse().ToList();
            nations[id] = new NativeNationType(
                Id: id,
                SettlementTypeId: RequiredAttribute(baseSettlement, "id"),
                CapitalSettlementTypeId: RequiredAttribute(capitalSettlement, "id"),
                NumberOfSettlements: ParseSettlementNumber(
                    ResolveStringAttribute(el, "number-of-settlements", elements)),
                Aggression: ParseAggression(ResolveStringAttribute(el, "aggression", elements)),
                Skills: rootToLeaf
                    .SelectMany(c => c.Elements("skill"))
                    .Select(s => new NativeSkill(
                        RequiredAttribute(s, "id"), (int?)s.Attribute("probability") ?? 0))
                    .ToList(),
                Regions: rootToLeaf
                    .SelectMany(c => c.Elements("region"))
                    .Select(r => RequiredAttribute(r, "id"))
                    .ToList());
        }

        return (nations, settlements);
    }

    private static SettlementType ParseSettlementType(XElement el)
    {
        XElement? defence = el.Elements("modifier")
            .FirstOrDefault(m => (string?)m.Attribute("id") == "model.modifier.defence");
        return new SettlementType(
            Id: RequiredAttribute(el, "id"),
            Capital: (bool?)el.Attribute("capital") ?? false,
            ClaimableRadius: (int?)el.Attribute("claimable-radius") ?? 0,
            ExtraClaimableRadius: (int?)el.Attribute("extra-claimable-radius") ?? 0,
            MinimumSize: (int?)el.Attribute("minimum-size") ?? 0,
            MaximumSize: (int?)el.Attribute("maximum-size") ?? 0,
            MinimumGrowth: (int?)el.Attribute("minimum-growth") ?? 0,
            MaximumGrowth: (int?)el.Attribute("maximum-growth") ?? 0,
            TradeBonus: (int?)el.Attribute("trade-bonus") ?? 0,
            ConvertThreshold: (int?)el.Attribute("convert-threshold") ?? 0,
            DefenceModifier: (double?)defence?.Attribute("value") ?? 0,
            Plunder: el.Elements("plunder")
                .Select(p => new SettlementPlunder(
                    Probability: (int?)p.Attribute("probability") ?? 0,
                    Minimum: (int?)p.Attribute("minimum") ?? 0,
                    Maximum: (int?)p.Attribute("maximum") ?? 0,
                    Factor: (int?)p.Attribute("factor") ?? 0,
                    RequiresPlunderAbility:
                        (bool?)p.Element("scope")?.Attribute("ability-value") ?? false))
                .ToList());
    }

    /// <summary>The chain of indian-nation-type elements from <paramref name="el"/> up its extends ancestors (leaf → root).</summary>
    private static IEnumerable<XElement> ExtendsChain(XElement el, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null;)
        {
            yield return current;
            string? parentId = (string?)current.Attribute("extends");
            current = parentId is not null ? elements.GetValueOrDefault(parentId) : null;
        }
    }

    /// <summary>The nearest <c>&lt;settlement&gt;</c> of the requested capital flag up the extends chain, or null.</summary>
    private static XElement? ResolveSettlementElement(
        XElement el, Dictionary<string, XElement> elements, bool capital)
    {
        foreach (XElement current in ExtendsChain(el, elements))
        {
            XElement? settlement = current.Elements("settlement")
                .FirstOrDefault(s => ((bool?)s.Attribute("capital") ?? false) == capital);
            if (settlement is not null)
            {
                return settlement;
            }
        }
        return null;
    }

    /// <summary>The nearest definition of a string attribute up the extends chain, or null.</summary>
    private static string? ResolveStringAttribute(
        XElement el, string name, Dictionary<string, XElement> elements)
    {
        foreach (XElement current in ExtendsChain(el, elements))
        {
            if ((string?)current.Attribute(name) is string value)
            {
                return value;
            }
        }
        return null;
    }

    private static SettlementNumber ParseSettlementNumber(string? value) => value switch
    {
        "low" => SettlementNumber.Low,
        "high" => SettlementNumber.High,
        _ => SettlementNumber.Average,
    };

    private static NativeAggression ParseAggression(string? value) => value switch
    {
        "low" => NativeAggression.Low,
        "high" => NativeAggression.High,
        _ => NativeAggression.Average,
    };

    private static string RequiredAttribute(XElement el, string name) =>
        el.Attribute(name)?.Value
            ?? throw new RulesetFormatException($"<{el.Name}> lacks required attribute '{name}'.");
}

/// <summary>Thrown when a ruleset specification file is malformed.</summary>
public sealed class RulesetFormatException : Exception
{
    /// <summary>Creates the exception with a description of the format problem.</summary>
    public RulesetFormatException(string message) : base(message) { }
}
