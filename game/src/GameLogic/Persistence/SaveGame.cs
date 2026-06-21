using System.Text.Json;
using System.Text.Json.Serialization;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.Persistence;

/// <summary>
/// Serializable snapshot of a complete game, including the RNG state so a loaded
/// game continues with the identical future random sequence (ADR-009). Terrain and
/// unit types are stored as ruleset ids — the ruleset itself is not saved, so the
/// matching ruleset is required to load.
/// </summary>
public sealed record SaveGame
{
    /// <summary>Current save format version.</summary>
    public const int CurrentVersion = 50;

    /// <summary>
    /// Save format version. v1 lacked <see cref="Explored"/> and unit type ids;
    /// v2 lacked <see cref="Colonies"/>; v3 colonies lacked goods stores;
    /// v4 lacked tile workers; v5 lacked buildings; v9 added gold/tax/market;
    /// v10 added liberty/congress; v11 added unit location/cargo; v12 added
    /// immigration + the Europe recruitment dock; v13 added unit carrier ids
    /// (passengers aboard ships); v14 added native settlements; v15 added the
    /// game variant id (which ruleset the game plays under); v16 added native
    /// settlement interaction state (alarm, visited, skill-consumed); v17 added
    /// native settlement wanted goods; v18 added unit owner nation + role/roleCount
    /// (native braves and armed soldiers); v19 = settlement assault (a destroyed
    /// settlement is simply absent from the list; plunder folds into gold — no new field);
    /// v20 introduced <see cref="Players"/> as the source of truth for player-scoped state
    /// (gold/tax/market/liberty/Congress/immigration/dock/explored). The load path is chosen by
    /// version: v20+ reads <see cref="Players"/>; a v19-and-earlier save (or any save with no
    /// <see cref="Players"/>) folds the legacy flat top-level fields into one human player. As of <b>FP-7</b> a
    /// v20 save no longer writes those flat fields — player state lives only in <see cref="Players"/>; the
    /// version stays 20 because the v20 load path was already <see cref="Players"/>-only, so new v20 saves are
    /// simply smaller. The flat field properties remain (read-only) so ≤v19 saves still fold and pre-FP-7 v20
    /// saves (which carry both) still load. v20 also
    /// gained optional unit/colony owner ids (FP-2, additive — null = the human, id 0) and, additively through
    /// the foreign-powers wave, per-player RNG streams (FP-4) and per-player diplomacy stance + tension maps
    /// (FP-6a; omitted when empty, so a no-contact game is byte-identical; older saves load Uncontacted/0).
    /// v21 added a damaged ship's repair-turns-remaining (1c-3b, additive — omitted when 0/healthy, so a
    /// fleet with no damaged ship is byte-identical; older saves load 0 = healthy).
    /// v22 added a colony's accumulated liberty for per-colony Sons-of-Liberty (additive — omitted when 0, so a
    /// colony with no banked liberty is byte-identical; ≤v21 saves load 0 = SoL 0%, production bonus 0).
    /// v23 added a unit's standing order (fortify/sentry; omitted when Active). v24 added a colony's build-queue
    /// tail beyond the front item (omitted for a ≤1-item queue). v25 added Lost City Rumour tile positions
    /// (omitted when none). v26 added tiles bought/taken from the natives (<see cref="ClaimedTiles"/>; omitted
    /// when none, so a game with no land purchases stays byte-identical to v25). v27 added a unit's carried treasure
    /// (<see cref="SavedUnit.TreasureAmount"/>; omitted when 0 → a non-treasure unit is byte-identical to v26). v28
    /// added the game-wide custom-house auto-export mode (<see cref="AutoExportMode"/>, omitted for the PerGood
    /// default) and per-colony custom-house export settings (<see cref="SavedColony.Exports"/>, only non-default
    /// goods, omitted when none). v29 added per-player escalated Europe purchase prices
    /// (<see cref="SavedPlayer.UnitPrices"/>, omitted when none have escalated → a game where no one has bought
    /// artillery is byte-identical to v28). v30 added per-colonist worker unit types — a tile worker's
    /// (<see cref="SavedWorker.UnitTypeId"/>), a building's non-free occupants (<see cref="SavedColony.BuildingWorkerTypes"/>)
    /// and the non-free idle colonists (<see cref="SavedColony.IdleWorkerTypes"/>) — all omitted when the worker is a
    /// free colonist, so a free-colonist-only game stays byte-identical to v29; pre-v30 saves load every worker free.
    /// v31 added a tile worker's accrued on-the-job experience (<see cref="SavedWorker.Experience"/>, omitted when 0, so
    /// a game where no colonist has accrued experience is byte-identical to v30; pre-v31 saves load every worker at 0).
    /// v32 added per-school accrued training turns (<see cref="SavedColony.SchoolTraining"/>, omitted when no school is
    /// mid-training, so a non-teaching game is byte-identical to v31; pre-v32 saves load with no training in progress).
    /// v33 added a settlement's resident missionary (<see cref="SavedNativeSettlement.MissionOwnerId"/> +
    /// <see cref="SavedNativeSettlement.MissionIsExpert"/>, both omitted when the settlement has no mission, so a
    /// mission-free game is byte-identical to v32; pre-v33 saves load with no missions).
    /// v34 added a mission's accrued <see cref="SavedNativeSettlement.ConvertProgress"/> (omitted when 0, so a game
    /// with no banked convert progress is byte-identical to v33; pre-v34 saves load at 0).
    /// v35 added the map's geographic regions (<see cref="RegionIds"/> + <see cref="Regions"/>; omitted when the map
    /// has no region layer, so a regionless fixture is byte-identical to v34). A pre-v35 save (or any save without a
    /// region layer) re-derives regions deterministically on load via <see cref="World.RegionGenerator"/>.
    /// (From 2026-06-20 the generator additionally classifies fully-enclosed water as <see cref="World.RegionType.Lake"/>
    /// (ordinal 4); this is an existing-field value change within the v35 layer, not a format change, so it carries
    /// <b>no version bump</b> — a newly-generated map with a lake writes <c>Type = 4</c>, while older saves load their
    /// persisted region types verbatim. No loader logic branches on lake-vs-ocean.)
    /// v36 added a unit's standing "go to" destination (<see cref="SavedUnit.DestX"/> + <see cref="SavedUnit.DestY"/>;
    /// both omitted when the unit has no goto, so a goto-free game is byte-identical to v35; pre-v36 saves load with
    /// no destination).
    /// v37 added boycott back-tax (<see cref="SavedPlayer.Arrears"/>, omitted when nothing is boycotted) and a colony's
    /// Boston-Tea-Party bell-surge turns (<see cref="SavedColony.TeaPartyBellTurns"/>, omitted when 0), so a game with
    /// no tea party is byte-identical to v36; pre-v37 saves load with neither.
    /// v38 added the King's displeasure (<see cref="SavedPlayer.MonarchDispleasure"/>, omitted when false), so a game
    /// where the King is content is byte-identical to v37; pre-v38 saves load content.
    /// v39 added whether naval support has been granted (<see cref="SavedPlayer.SupportSeaGranted"/>, omitted when
    /// false), so a game with no SUPPORT_SEA is byte-identical to v38; pre-v39 saves load not-yet-granted.
    /// v40 added the Royal Expeditionary Force (<see cref="RefForce"/>, omitted until grown beyond its re-derivable
    /// base), so a pre-rebellion game is byte-identical to v39; pre-v40 saves re-derive the base on demand.
    /// v41 added the rebellion lifecycle: <see cref="SavedPlayer.DeclaredIndependenceTurn"/> + <see cref="SavedPlayer.InterventionBells"/>
    /// (both omitted before independence) and the <see cref="GameSession.PlayerType"/> Rebel/Independent/REF ordinals +
    /// the REF player row; a pre-independence game is byte-identical to v40; pre-v41 saves load with no rebellion.
    /// v42 added the <see cref="SpanishSuccession"/> flag (omitted until it fires), so a game before 1600 is
    /// byte-identical to v41; pre-v42 saves load with the succession not yet done.
    /// Each of v23–v42 is additive + omitted-when-empty, so a feature-free game round-trips byte-identically to the
    /// prior version and older saves load with the feature absent.
    /// v46 added three additive omit-when-default fields: the chosen difficulty level (<see cref="DifficultyLevel"/>,
    /// omitted for the default <c>model.difficulty.medium</c> so a default game stays byte-identical to v45; pre-v46
    /// saves load the default level, 86d3c9y08); each finite bonus resource's rolled starting quantity
    /// (<see cref="ResourceQuantities"/>, omitted when no placed resource carries one, so a typical game stays
    /// byte-identical; pre-v46 saves load with none, 86d3c9wbp); and a pending monarch demand awaiting the human's
    /// accept/reject (<see cref="PendingMonarchDemand"/>, omitted when none is pending — the common case, so a game with
    /// no open demand stays byte-identical; pre-v46 saves load with no pending demand, 86d3c9rk6). All three are additive
    /// + omitted-when-default, so a default game round-trips byte-identically to v45 and older saves load with the
    /// feature absent.
    /// v47 added the map's natural tile improvements (<see cref="Improvements"/> — today only rivers, stamped at game
    /// start by the map generator), by row-major tile index with the improvement's id + magnitude; omitted when the map
    /// carries none (a pre-river fixture), so a riverless map stays byte-identical. Pre-v47 saves load with no
    /// improvements (no rivers); the river layer is part of map generation, so a reloaded river map round-trips its
    /// rivers exactly rather than re-deriving them (86d3b3qdx). v47 also added the REF's fixed entry tile
    /// (<see cref="RefEntryTile"/>, chosen near the human's start at game creation, 86d3c9w5n), omitted when unset so a
    /// game that never fixed one stays byte-identical; pre-v47 saves load with none (the REF falls back to landing
    /// around the rebel's coastal colonies).
    /// v48 added a pioneer's in-progress tile-improvement work-state — the improvement being built
    /// (<see cref="SavedUnit.WorkImprovement"/>) and the turns of work left (<see cref="SavedUnit.WorkTurnsLeft"/>) —
    /// both additive + omitted when the unit is not improving (the common case), so a game with no active pioneering
    /// stays byte-identical to v47; pre-v48 saves load with no unit improving. The improvement layer also became
    /// multi-valued per tile (a river plus a pioneer-built road/plow), which a v47 river save round-trips identically
    /// — one entry per tile as before (86d3dqr62).
    /// v49 added the human's chosen <b>nation</b> (the New-Game nation pick, 86d3drn5x): the human <see cref="Player"/>
    /// may now carry a <see cref="GameSession.Player.NationId"/>, persisted in the existing <see cref="SavedPlayer.NationId"/>
    /// field that already round-trips every player's nation. No structural field was added — a nation-less human still
    /// <b>omits</b> the field (serializer <c>WhenWritingNull</c>), so a default (nation-less) game stays byte-identical to
    /// v48 and pre-v49 saves load the human nation-less (the previously-always-null value). Only a picked nation writes
    /// the id; the version bump records that the human's nation is now a meaningful, selectable value.
    /// v50 added a unit's accrued <b>attrition</b> (<see cref="SavedUnit.Attrition"/> — turns spent standing in the open
    /// wilderness, 86d3drmzp): additive + omitted when 0, so a unit that has accrued none (every unit in the classic
    /// default game, where only the Indian Convert is even subject to attrition and a fresh game has none wandering the
    /// open map) is byte-identical to v49, and pre-v50 saves load every unit at attrition 0. The cap (FreeCol
    /// <c>UnitType.getMaximumAttrition</c>) is re-derived from the ruleset on load, not persisted.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// The game variant this save was played under (e.g. <c>classic</c>), so it
    /// reloads with the matching ruleset (ADR-018). Null in pre-v15 saves → the
    /// caller treats it as the default variant.
    /// </summary>
    public string? Variant { get; init; }

    /// <summary>Current turn number.</summary>
    public required int Turn { get; init; }

    /// <summary>RNG internal state word.</summary>
    public required ulong RandomStateValue { get; init; }

    /// <summary>RNG stream increment.</summary>
    public required ulong RandomIncrement { get; init; }

    /// <summary>Map width in tiles.</summary>
    public required int MapWidth { get; init; }

    /// <summary>Map height in tiles.</summary>
    public required int MapHeight { get; init; }

    /// <summary>Row-major terrain ids for every tile.</summary>
    public required IReadOnlyList<string> Terrain { get; init; }

    /// <summary>All units.</summary>
    public required IReadOnlyList<SavedUnit> Units { get; init; }

    /// <summary>
    /// Explored tile indexes (row-major <c>y * MapWidth + x</c>), fog of war.
    /// Null in v1 saves — loading reveals around units instead.
    /// </summary>
    public IReadOnlyList<int>? Explored { get; init; }

    /// <summary>All colonies. Null in pre-v3 saves (no colonies existed).</summary>
    public IReadOnlyList<SavedColony>? Colonies { get; init; }

    /// <summary>Bonus resources by row-major tile index. Null in pre-v8 saves (none).</summary>
    public IReadOnlyList<SavedResource>? Resources { get; init; }

    /// <summary>Tiles holding an unexplored Lost City Rumour, by row-major tile index (v25; null/omitted when none, so a rumour-free game stays byte-identical to v24).</summary>
    public IReadOnlyList<int>? Rumours { get; init; }

    /// <summary>Tiles the player has bought or taken from the natives, by row-major tile index (v26; null/omitted when none, so a game with no land purchases stays byte-identical to v25). The native-ownership re-derivation honours these so a claimed tile never reverts to the natives.</summary>
    public IReadOnlyList<int>? ClaimedTiles { get; init; }

    /// <summary>The region id of every tile, row-major (<c>y * MapWidth + x</c>) (v35; null/omitted when the map has no region layer, so a regionless fixture stays byte-identical to v34). Indexes into <see cref="Regions"/>; a pre-v35 save re-derives the layer on load.</summary>
    public IReadOnlyList<int>? RegionIds { get; init; }

    /// <summary>The map's region table, indexed by region id (v35; null/omitted alongside <see cref="RegionIds"/>).</summary>
    public IReadOnlyList<SavedRegion>? Regions { get; init; }

    /// <summary>The game-wide custom-house auto-export mode (v28; null/omitted for the <see cref="GameSession.AutoExportMode.PerGood"/> default, so a default game stays byte-identical to v27). Stored as the enum ordinal.</summary>
    public AutoExportMode? AutoExportMode { get; init; }

    /// <summary>The Royal Expeditionary Force the King has amassed (v40; null/omitted until grown beyond its re-derivable base, so a pre-rebellion game stays byte-identical to v39).</summary>
    public SavedForce? RefForce { get; init; }

    /// <summary>Whether the Spanish Succession consolidation has happened (v42; null/omitted until it fires, so a game before 1600 stays byte-identical to v41).</summary>
    public bool? SpanishSuccession { get; init; }

    /// <summary>The spec id of the difficulty level this game plays under (v46; null/omitted for the default <c>model.difficulty.medium</c>, so a default game stays byte-identical to v45). On load the ruleset is re-loaded under this level so the balance matches; pre-v46 saves load the default level.</summary>
    public string? DifficultyLevel { get; init; }

    /// <summary>The difficulty level id to load this save's ruleset under: the persisted <see cref="DifficultyLevel"/>, or the default medium when omitted (pre-v46 / a default game). Hosts pass this to <c>GameVariant.LoadRuleset</c> so the reloaded balance matches the save.</summary>
    public string DifficultyLevelOrDefault => DifficultyLevel ?? DifficultyLevels.DefaultId;

    /// <summary>Each finite bonus resource's rolled starting quantity, by row-major tile index (v46; null/omitted when no placed resource carries a quantity, so a typical map stays byte-identical to v45). Only finite (min/max-ranged) resources carry one — a limitless resource is absent. Pre-v46 saves load with none.</summary>
    public IReadOnlyList<SavedResourceQuantity>? ResourceQuantities { get; init; }

    /// <summary>The map's natural tile improvements (today only rivers) by row-major tile index, each with its improvement id + magnitude (v47; null/omitted when the map carries none, so a riverless map stays byte-identical to v46). Pre-v47 saves load with no improvements.</summary>
    public IReadOnlyList<SavedImprovement>? Improvements { get; init; }

    /// <summary>The Royal Expeditionary Force's entry tile, row-major index (v47; null/omitted when unset — a pre-v47 save or a map with no water — so a game that never fixed one stays byte-identical). Chosen near the human's start at game creation; on independence the King's fleet lands here.</summary>
    public int? RefEntryTile { get; init; }

    /// <summary>A monarch demand awaiting the human's accept/reject when the game was saved (v46; null/omitted when none is pending — the common case, so a game with no open demand stays byte-identical to v45). Pre-v46 saves load with no pending demand. (FreeCol persists the <c>MonarchSession</c>.)</summary>
    public SavedMonarchDemand? PendingMonarchDemand { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only player treasury (v9+). Player state lives in <see cref="Players"/> (v20+); no longer written as of FP-7. Nullable so new saves omit it.</summary>
    public int? Gold { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only sales tax (v9+). See <see cref="Gold"/>.</summary>
    public int? Tax { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only moved-market inventories (sparse; v9+). See <see cref="Gold"/>.</summary>
    public IReadOnlyDictionary<string, int>? MarketState { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only liberty (v10+). See <see cref="Gold"/>.</summary>
    public int? Liberty { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only elected Founding Father ids (null when none; v10+). See <see cref="Gold"/>.</summary>
    public IReadOnlyList<string>? Congress { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only current father (v10+). See <see cref="Gold"/>.</summary>
    public string? CurrentFather { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only offered fathers (v10+). See <see cref="Gold"/>.</summary>
    public IReadOnlyList<string>? OfferedFathers { get; init; }

    /// <summary>Legacy ≤v19 / pre-FP-7 read-only immigration points (v12+). See <see cref="Gold"/>.</summary>
    public int? Immigration { get; init; }

    /// <summary>Immigration points required for the next emigrant; null pre-v12 → classic default 15.</summary>
    public int? ImmigrationRequired { get; init; }

    /// <summary>Escalating base recruit price; null pre-v12 → classic default 200.</summary>
    public int? BaseRecruitPrice { get; init; }

    /// <summary>Recruit-price floor; null pre-v12 → classic default 80.</summary>
    public int? RecruitLowerCap { get; init; }

    /// <summary>Unit types waiting on the Europe dock; null pre-v12 → a fresh dock is drawn on load.</summary>
    public IReadOnlyList<string>? RecruitDock { get; init; }

    /// <summary>Native settlements on the map. Null in pre-v14 saves (none existed).</summary>
    public IReadOnlyList<SavedNativeSettlement>? NativeSettlements { get; init; }

    /// <summary>
    /// Per-player state (v20+; one human element today). Null in pre-v20 saves, whose flat top-level
    /// fields are folded into a single human player on load (ADR-019).
    /// </summary>
    public IReadOnlyList<SavedPlayer>? Players { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Captures the complete state of a running game, tagged with the variant it plays under.</summary>
    /// <param name="game">The running game.</param>
    /// <param name="variantId">The variant id to record (defaults to the standard variant).</param>
    public static SaveGame From(Game game, string? variantId = null)
    {
        RandomState rng = game.RandomState;
        return new SaveGame
        {
            Variant = variantId ?? Specification.GameVariants.Default.Id,
            Turn = game.Turn,
            RandomStateValue = rng.State,
            RandomIncrement = rng.Increment,
            MapWidth = game.Map.Width,
            MapHeight = game.Map.Height,
            Terrain = game.Map.AllPositions().Select(p => game.Map.TerrainAt(p).Id).ToList(),
            Units = game.Units
                .Select(u => new SavedUnit(
                    u.Id, u.Type.Id, u.Position.X, u.Position.Y, u.MovementLeft,
                    (int)u.Location, u.SailTurnsRemaining,
                    u.Cargo.Count > 0 ? new Dictionary<string, int>(u.Cargo) : null,
                    u.CarrierId,
                    u.OwnerNationId,
                    // The unarmed default role is the common case → omit role + count so a default-role
                    // player unit serializes byte-identically to a v17 save (no churn, no golden drift).
                    u.RoleId == Specification.RoleType.DefaultRoleId ? null : u.RoleId,
                    u.RoleCount == 0 ? null : u.RoleCount,
                    // Owner player id omitted for the human (id 0) so human-only saves stay byte-identical (FP-2).
                    u.OwnerId == 0 ? null : u.OwnerId,
                    // Repair turns omitted for a healthy ship (0) so an undamaged fleet stays byte-identical (1c-3b).
                    u.RepairTurnsRemaining == 0 ? null : u.RepairTurnsRemaining,
                    // Standing order omitted for an active unit (0) so a no-orders game stays byte-identical to v22.
                    u.Orders == UnitOrders.Active ? null : (int)u.Orders,
                    // Treasure carried omitted for the common 0 so every non-treasure unit stays byte-identical to v26.
                    u.TreasureAmount == 0 ? null : u.TreasureAmount,
                    // Goto destination split into two ints; both omitted when no goto so a goto-free unit stays byte-identical to v35.
                    u.Destination?.X, u.Destination?.Y,
                    // Trade-route assignment (v43); omitted for a route-less unit so it stays byte-identical to v42.
                    u.TradeRouteId, u.TradeRouteStopIndex == 0 ? null : u.TradeRouteStopIndex,
                    // In-progress pioneer improvement (v48); both omitted when not improving so a unit with no build order stays byte-identical to v47.
                    u.WorkImprovementId, u.WorkTurnsLeft == 0 ? null : u.WorkTurnsLeft,
                    // Accrued attrition (v50); omitted for the common 0 so a unit that has wasted no turns in the open stays byte-identical to v49.
                    u.Attrition == 0 ? null : u.Attrition))
                .ToList(),
            Colonies = game.Colonies
                .Select(c => new SavedColony(
                    c.Id, c.Name, c.Position.X, c.Position.Y, c.Population,
                    c.Stores.Count > 0 ? new Dictionary<string, int>(c.Stores) : null,
                    c.TileWorkers.Count > 0
                        ? c.TileWorkers.Select(w => new SavedWorker(
                            w.Key.X, w.Key.Y, w.Value,
                            c.TileWorkerTypes.GetValueOrDefault(w.Key),
                            c.TileWorkerExperienceAt(w.Key) is var xp && xp > 0 ? xp : null)).ToList()
                        : null,
                    c.Buildings.ToList(),
                    c.BuildingWorkers.Count > 0 ? new Dictionary<string, int>(c.BuildingWorkers) : null,
                    c.CurrentBuild,
                    c.OwnerId == 0 ? null : c.OwnerId,
                    // Liberty omitted for a colony with none (0) so a no-liberty colony stays byte-identical (v22).
                    c.Liberty == 0 ? null : c.Liberty,
                    // The queued tail after the front; omitted for a 0/1-item queue so it stays byte-identical to v23.
                    c.BuildQueue.Count > 1 ? c.BuildQueue.Skip(1).ToList() : null,
                    // Custom-house export settings; only non-default goods are stored, omitted when none (v28).
                    c.Exports.Count > 0
                        ? c.Exports.ToDictionary(kv => kv.Key, kv => new SavedExport(kv.Value.Exported, kv.Value.ExportLevel))
                        : null,
                    // Per-colonist worker types (v30): a building's non-free occupants + the non-free idle colonists,
                    // omitted when all free so a free-colonist-only colony stays byte-identical to v29.
                    c.BuildingWorkerTypes.Count > 0
                        ? c.BuildingWorkerTypes.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToList())
                        : null,
                    c.IdleWorkerTypes.Count > 0 ? c.IdleWorkerTypes.ToList() : null,
                    c.SchoolTrainingTurns.Count > 0 ? new Dictionary<string, int>(c.SchoolTrainingTurns) : null,
                    // Boston-Tea-Party bell-surge turns remaining; omitted when none (v37, byte-identical to v36).
                    c.TeaPartyBellTurns == 0 ? null : c.TeaPartyBellTurns))
                .ToList(),
            Resources = game.Map.Resources.Count > 0
                ? game.Map.Resources
                    .Select(r => new SavedResource(r.Key.Y * game.Map.Width + r.Key.X, r.Value))
                    .OrderBy(r => r.Index)
                    .ToList()
                : null,
            // Each finite resource's rolled starting quantity by row-major index (v46); omitted when none placed,
            // so a typical game stays byte-identical to v45.
            ResourceQuantities = game.Map.ResourceQuantities.Count > 0
                ? game.Map.ResourceQuantities
                    .Select(q => new SavedResourceQuantity(q.Key.Y * game.Map.Width + q.Key.X, q.Value))
                    .OrderBy(q => q.Index)
                    .ToList()
                : null,
            // Tile improvements by row-major index + id/magnitude (v47); one entry per (tile, improvement) — a tile
            // with a river and a road emits two entries at the same index, ordered by id for a stable byte layout.
            // Omitted when none placed, so an improvement-free map stays byte-identical to v46.
            Improvements = game.Map.AllImprovements().Any()
                ? game.Map.AllImprovements()
                    .Select(i => new SavedImprovement(
                        i.Position.Y * game.Map.Width + i.Position.X, i.Improvement.Id, i.Improvement.Magnitude))
                    .OrderBy(i => i.Index)
                    .ThenBy(i => i.ImprovementId, StringComparer.Ordinal)
                    .ToList()
                : null,
            // The REF's fixed entry tile (v47); omitted when unset so a game that never fixed one stays byte-identical.
            RefEntryTile = game.RefEntryTile is { } e ? e.Y * game.Map.Width + e.X : null,
            // Lost City Rumours by row-major index; omitted when none so a rumour-free game stays byte-identical to v24.
            Rumours = game.Map.Rumours.Count > 0
                ? game.Map.Rumours.Select(p => p.Y * game.Map.Width + p.X).OrderBy(i => i).ToList()
                : null,
            // Tiles bought/taken from the natives by row-major index; omitted when none (byte-identical to v25).
            ClaimedTiles = game.Map.ClaimedFromNatives.Count > 0
                ? game.Map.ClaimedFromNatives.Select(p => p.Y * game.Map.Width + p.X).OrderBy(i => i).ToList()
                : null,
            // Geographic regions: a row-major region id per tile + the region table. Omitted when the map has no
            // region layer (a regionless fixture stays byte-identical to v34); a real generated map always has one.
            RegionIds = game.Map.Regions.Count > 0
                ? game.Map.AllPositions().Select(game.Map.RegionIdAt).ToList()
                : null,
            Regions = game.Map.Regions.Count > 0
                ? game.Map.Regions
                    .Select(r => new SavedRegion(r.Id, (int)r.Type, r.ScoreValue, r.Key, r.ParentId))
                    .ToList()
                : null,
            // Custom-house auto-export mode; omitted for the PerGood default so a default game stays byte-identical to v27.
            AutoExportMode = game.AutoExportMode == GameSession.AutoExportMode.PerGood ? null : game.AutoExportMode,
            // The Royal Expeditionary Force; omitted until grown beyond its (re-derivable) base, so a pre-rebellion game stays byte-identical to v39.
            RefForce = game.RefForceOrNull is { } ref_
                ? new SavedForce(ref_.LandUnits.ToList(), ref_.NavalUnits.ToList())
                : null,
            // The Spanish Succession flag; omitted until it fires so a pre-1600 game stays byte-identical to v41.
            SpanishSuccession = game.SpanishSuccessionDone ? true : null,
            // Player-scoped state: authoritative in (and written only to) Players[]. The legacy flat
            // top-level fields are no longer written as of FP-7 — they remain readable for ≤v19 / pre-FP-7
            // v20 saves (the fold path), but the v20 load path was always Players[]-only, so the format
            // version stays 20 and new v20 saves are simply smaller.
            Players = game.Players.Select(p => ToSavedPlayer(p, game.Map)).ToList(),
            NativeSettlements = game.NativeSettlements.Count > 0
                ? game.NativeSettlements
                    .Select(s => new SavedNativeSettlement(
                        s.Id, s.NationTypeId, s.SettlementTypeId, s.IsCapital,
                        s.Position.X, s.Position.Y, s.Size, s.LearnableSkill,
                        s.Alarm, s.HasBeenVisited, s.SkillConsumed,
                        s.WantedGoods.Count > 0 ? s.WantedGoods.ToList() : null,
                        s.MissionOwnerId, s.HasMission ? s.MissionIsExpert : null, // omitted when no mission
                        s.ConvertProgress != 0 ? s.ConvertProgress : null,         // omitted when no progress banked
                        s.VisitedByPowers.Count > 0 ? s.VisitedByPowers.ToList() : null)) // v44; omitted when no foreign power has visited
                    .ToList()
                : null,
            // The chosen difficulty level (v46); omitted for the default medium so a default game stays byte-identical to v45.
            DifficultyLevel = game.DifficultyLevelId == DifficultyLevels.DefaultId ? null : game.DifficultyLevelId,
            // A pending monarch demand awaiting the human's accept/reject (v46); omitted when none — the common case.
            PendingMonarchDemand = game.PendingMonarchDemand is { } md ? SavedMonarchDemand.From(md) : null,
        };
    }

    /// <summary>Reconstructs a running game from this snapshot.</summary>
    /// <exception cref="KeyNotFoundException">A saved terrain or unit type id is missing from the ruleset.</exception>
    public Game Restore(Ruleset ruleset)
    {
        var terrain = Terrain.Select(ruleset.Terrain).ToList();
        var map = new GameMap(
            MapWidth, MapHeight, terrain,
            Resources?.ToDictionary(
                r => new Position(r.Index % MapWidth, r.Index / MapWidth),
                r => r.ResourceId),
            Rumours?.Select(i => new Position(i % MapWidth, i / MapWidth)).ToList(),
            ClaimedTiles?.Select(i => new Position(i % MapWidth, i / MapWidth)).ToList(),
            RegionIds,
            Regions?.Select(r => new Region(r.Id, (RegionType)r.Type, r.ScoreValue, r.Key, r.ParentId)).ToList(),
            // Finite resource quantities by tile (v46; pre-v46 / omitted → none).
            ResourceQuantities?.ToDictionary(
                q => new Position(q.Index % MapWidth, q.Index / MapWidth),
                q => q.Quantity),
            // Tile improvements by tile (v47; pre-v47 / omitted → none) — grouped by index so a tile with several
            // (a river + a road) restores its full list. The ruleset re-supplies each improvement type's rule data
            // (modifiers, movement cost, scopes); the saved magnitude is re-stamped onto it.
            Improvements?
                .GroupBy(i => i.Index)
                .ToDictionary(
                    g => new Position(g.Key % MapWidth, g.Key / MapWidth),
                    g => (IReadOnlyList<TileImprovementType>)g
                        .Select(i => ruleset.Improvement(i.ImprovementId) with { Magnitude = i.Magnitude })
                        .ToList()));

        // Pre-v35 saves (and any save without a persisted region layer) re-derive regions deterministically from
        // the terrain — exactly the layer the generator would have produced (mirrors the native-land re-derivation).
        if (map.Regions.Count == 0)
        {
            (int[] regionIds, IReadOnlyList<Region> regions) = RegionGenerator.Assign(map);
            map.SetRegions(regionIds, regions);
        }
        Game game = Game.Restore(
            ruleset,
            map,
            new RandomState(RandomStateValue, RandomIncrement),
            Turn,
            RestorePlayers(),
            Units.Select(u => (
                u.Id,
                // v1 saves carry no type id; everything was effectively a colonist.
                ruleset.Unit(u.TypeId ?? Game.StartingUnitTypeId),
                new Position(u.X, u.Y),
                u.MovementLeft,
                (UnitLocation)u.Location,   // pre-v11 → 0 = OnMap
                u.SailTurns,
                u.Cargo,
                u.CarrierId,                // pre-v13 → null = not aboard
                u.Owner,                    // pre-v18 → null = colonial-owned
                u.Role,                     // pre-v18 → null = default role
                u.RoleCount ?? 0,           // pre-v18 / default role → 0
                u.OwnerId ?? 0,             // pre-v20 / human → 0
                u.RepairTurns ?? 0,         // pre-v21 / healthy ship → 0
                (UnitOrders)(u.Orders ?? 0), // pre-v23 / active → Active
                u.TreasureAmount ?? 0,      // pre-v27 / non-treasure → 0
                u.DestX is { } dx && u.DestY is { } dy ? new Position(dx, dy) : (Position?)null, // pre-v36 / no goto → null
                u.TradeRouteId, u.TradeRouteStop ?? 0, // pre-v43 / route-less → null/0
                u.WorkImprovement, u.WorkTurnsLeft ?? 0, // pre-v48 / not improving → null/0
                u.Attrition ?? 0)), // pre-v50 / no attrition → 0
            Colonies?.Select(c =>
            {
                var colony = new CrownAndColony.GameLogic.Colonies.Colony(
                    c.Id, c.Name, new Position(c.X, c.Y), c.Population, c.OwnerId ?? 0)
                {
                    Government = ruleset.Difficulty.Government, // production-bonus thresholds from the difficulty level (re-derived, not persisted)
                };
                foreach ((string goods, int amount) in
                         c.Stores ?? new Dictionary<string, int>())
                {
                    // Normalizes pre-v6 saves that stored raw grain/fish.
                    colony.AddGoods(ruleset.StorageIdOf(goods), amount);
                }
                foreach (SavedWorker worker in c.Workers ?? [])
                {
                    // Tile workers first (the idle pool is empty here, so the type just stamps the tile overlay).
                    var workerTile = new Position(worker.X, worker.Y);
                    colony.SetWorker(workerTile, worker.GoodsId,
                        worker.UnitTypeId ?? CrownAndColony.GameLogic.Colonies.Colony.FreeColonistTypeId);
                    colony.SetTileWorkerExperience(workerTile, worker.Experience ?? 0); // v31; pre-v31 ⇒ 0
                }
                // Pre-v6 saves carry no buildings: re-derive the free base set.
                var buildings = c.Buildings
                    ?? ruleset.BuildingTypes
                        .Where(b => b.BuildCost.Count == 0 && b.UpgradesFrom is null)
                        .Select(b => b.Id)
                        .ToList() as IReadOnlyList<string>;
                foreach (string buildingId in buildings)
                {
                    colony.AddBuilding(buildingId);
                }
                foreach ((string buildingId, int workers) in
                         c.BuildingWorkers ?? new Dictionary<string, int>())
                {
                    colony.SetBuildingWorkers(buildingId, workers);
                }
                // Defence-in-depth at the save boundary: a save written before the produceInWater gate could hold a
                // colonist fishing a sea tile on a colony with no Docks. Now that buildings are restored, drop any such
                // worker (it returns to the idle pool) so a loaded game obeys the rule the live game enforces. A valid
                // save has none, so this is a no-op there and the byte-identical round-trip is preserved.
                bool worksWater = colony.Buildings.Any(b => ruleset.Building(b).ProducesInWater);
                if (!worksWater)
                {
                    foreach (Position seaTile in colony.TileWorkers.Keys
                                 .Where(t => map.TerrainAt(t).IsWater).ToList())
                    {
                        colony.RemoveWorker(seaTile);
                    }
                }
                // Rebuild the construction queue: the front (CurrentBuild) then the saved tail (v24; absent → ≤1 item).
                colony.SetBuildQueue(
                    (c.CurrentBuild is null ? Enumerable.Empty<string>() : [c.CurrentBuild])
                        .Concat(c.BuildQueueRest ?? []));
                colony.Liberty = c.Liberty ?? 0; // ≤v21 saves had no liberty → SoL 0%
                colony.TeaPartyBellTurns = c.TeaPartyBellTurns ?? 0; // v37; pre-v37 / no party → 0
                foreach ((string goods, SavedExport export) in c.Exports ?? new Dictionary<string, SavedExport>())
                {
                    colony.SetExport(goods, export.Exported, export.Level); // custom-house export settings (v28; pre-v28 → none)
                }
                // Per-colonist worker types (v30; pre-v30 / absent → all free colonists).
                foreach ((string buildingId, IReadOnlyList<string> types) in
                         c.BuildingWorkerTypes ?? new Dictionary<string, IReadOnlyList<string>>())
                {
                    colony.RestoreBuildingWorkerTypes(buildingId, types);
                }
                foreach (string type in c.IdleWorkerTypes ?? [])
                {
                    colony.AddIdleColonist(type);
                }
                foreach ((string buildingId, int turns) in c.SchoolTraining ?? new Dictionary<string, int>())
                {
                    colony.RestoreSchoolTraining(buildingId, turns); // v32; pre-v32 → no training in progress
                }
                colony.ReconcileWorkerTypes(); // belt-and-braces: the overlay never exceeds the restored counts
                return colony;
            }),
            NativeSettlements?.Select(s =>
            {
                var settlement = new NativeSettlement(
                    s.Id, s.NationTypeId, s.SettlementTypeId, s.IsCapital,
                    new Position(s.X, s.Y), s.Size, s.LearnableSkill)
                {
                    Alarm = s.Alarm,
                    HasBeenVisited = s.HasBeenVisited,        // the human's first-contact flag (rides player 0)
                    SkillConsumed = s.SkillConsumed,
                    WantedGoods = s.WantedGoods ?? [],
                    MissionOwnerId = s.MissionOwnerId,           // v33; pre-v33 → null = no mission
                    MissionIsExpert = s.MissionIsExpert ?? false, // v33; default free-colonist missionary
                    ConvertProgress = s.ConvertProgress ?? 0,    // v34; pre-v34 → 0
                };
                foreach (int powerId in s.VisitedByPowers ?? []) // v44; pre-v44 → no foreign power has visited
                {
                    settlement.MarkVisitedBy(powerId);
                }
                return settlement;
            }),
            AutoExportMode.GetValueOrDefault(), // pre-v28 / omitted → PerGood (the enum's 0 default)
            DifficultyLevel ?? DifficultyLevels.DefaultId); // v46; pre-v46 / omitted → the default medium level

        if (RefForce is { } ref_) // v40; pre-v40 / omitted → the base REF is re-derived on demand
        {
            game.SetRefForce(new GameSession.Force(ref_.Land, ref_.Naval));
        }
        if (RefEntryTile is { } refEntry) // v47; pre-v47 / omitted → unset (REF falls back to rebel-colony landings)
        {
            game.SetRefEntryTile(new Position(refEntry % MapWidth, refEntry / MapWidth));
        }
        if (SpanishSuccession == true) // v42; pre-v42 / omitted → not yet done
        {
            game.SetSpanishSuccessionDone(true);
        }
        if (PendingMonarchDemand is { } md) // v46; pre-v46 / omitted → no demand was pending
        {
            game.RestorePendingMonarchDemand(md.ToDemand());
        }
        return game;
    }

    /// <summary>
    /// Builds the per-player state for <see cref="Game.Restore"/>: from <see cref="Players"/> for a v20+
    /// save, or — for a v19-and-earlier save — by folding the legacy flat top-level fields into one human
    /// player. Keyed on <see cref="Version"/> (not on <c>Players != null</c>) so a test that simulates an
    /// old save with <c>with { Version = N }</c> still takes the legacy fold path.
    /// </summary>
    private IReadOnlyList<RestoredPlayer> RestorePlayers() =>
        Version >= 20 && Players is not null
            ? Players.Select(ToRestored).ToList()
            : [FoldFlatFieldsToHumanPlayer()];

    /// <summary>Maps a saved (v20+) player to its restore form, expanding row-major explored indexes to positions.</summary>
    private RestoredPlayer ToRestored(SavedPlayer p) => new(
        p.PlayerId, p.NationId, p.IsHuman, (PlayerType)p.PlayerType,
        p.Gold, p.Tax, p.MarketState,
        p.Liberty, p.Congress, p.CurrentFather, p.OfferedFathers,
        p.Immigration, p.ImmigrationRequired ?? Game.InitialImmigration,
        p.BaseRecruitPrice ?? Game.InitialRecruitPrice, p.RecruitLowerCap ?? Game.InitialRecruitLowerCap,
        p.RecruitDock,
        p.Explored?.Select(i => new Position(i % MapWidth, i / MapWidth)),
        p.RngState is { } s && p.RngIncrement is { } inc ? new RandomState(s, inc) : null,
        p.Stances, p.Tensions, p.UnitPrices, p.Arrears, p.MonarchDispleasure ?? false, p.SupportSeaGranted ?? false,
        p.DeclaredIndependenceTurn, p.InterventionBells ?? 0,
        p.TradeRoutes?.Select(r => new TradeRoute(r.Id, r.Name,
            r.Stops.Select(stop => new TradeRouteStop(stop.ColonyId, stop.Load ?? [])).ToList())).ToList(),
        p.NextTradeRouteId);

    /// <summary>
    /// Folds the legacy flat top-level fields into the single human player — taken for a ≤v19 save, or any save
    /// with no <see cref="Players"/> (the JourneyTests fixtures). The flat value fields are nullable since FP-7
    /// (new saves omit them); they coalesce to their former <c>default(int)</c> 0, so a genuine old save (whose
    /// flat tokens are present) is unaffected and a field that was always absent still yields 0. Null explored = pre-fog.
    /// </summary>
    private RestoredPlayer FoldFlatFieldsToHumanPlayer() => new(
        PlayerId: 0, NationId: null, IsHuman: true, PlayerType.Colonial,
        Gold ?? 0, Tax ?? 0, MarketState,
        Liberty ?? 0, Congress, CurrentFather, OfferedFathers,
        Immigration ?? 0, ImmigrationRequired ?? Game.InitialImmigration,
        BaseRecruitPrice ?? Game.InitialRecruitPrice, RecruitLowerCap ?? Game.InitialRecruitLowerCap,
        RecruitDock,
        Explored?.Select(i => new Position(i % MapWidth, i / MapWidth)));

    /// <summary>Captures one player's state for a v20+ save (explored stored as compact row-major indexes).</summary>
    private static SavedPlayer ToSavedPlayer(Player p, GameMap map)
    {
        RandomState? rng = p.Rng?.SaveState(); // non-human players carry their own stream (FP-4); the human's is stream 0
        return new SavedPlayer(
            p.PlayerId, p.NationId, p.IsHuman, (int)p.PlayerType,
            p.Gold, p.TaxRate,
            p.Market.SaveDeltas() is { Count: > 0 } deltas ? new Dictionary<string, int>(deltas) : null,
            p.Liberty, p.Congress.Count > 0 ? p.Congress.ToList() : null, p.CurrentFather,
            p.OfferedFathers.Count > 0 ? p.OfferedFathers.ToList() : null,
            p.Immigration, p.ImmigrationRequired, p.BaseRecruitPrice, p.RecruitLowerCap,
            p.RecruitDock.Count > 0 ? p.RecruitDock.ToList() : null,
            p.Explored.Select(pos => pos.Y * map.Width + pos.X).OrderBy(i => i).ToList(),
            rng?.State, rng?.Increment,
            p.Stances.Count > 0 ? new Dictionary<int, Stance>(p.Stances) : null,
            p.Tensions.Count > 0 ? new Dictionary<int, int>(p.Tensions) : null,
            p.UnitPriceOverrides.Count > 0 ? new Dictionary<string, int>(p.UnitPriceOverrides) : null,
            p.Market.SaveArrears() is { Count: > 0 } arrears ? new Dictionary<string, int>(arrears) : null,
            p.MonarchDispleasure ? true : null,
            p.SupportSeaGranted ? true : null,
            p.DeclaredIndependenceTurn,
            p.InterventionBells == 0 ? null : p.InterventionBells,
            p.TradeRoutes.Count > 0
                ? p.TradeRoutes.Select(r => new SavedTradeRoute(r.Id, r.Name,
                    r.Stops.Select(s => new SavedTradeRouteStop(
                        s.ColonyId, s.LoadGoodsIds.Count > 0 ? s.LoadGoodsIds.ToList() : null)).ToList())).ToList()
                : null,
            p.NextTradeRouteId == 1 ? null : p.NextTradeRouteId); // omit-when-default (1) → byte-identical to v44 until a route is made
    }

    /// <summary>Serializes to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes from JSON produced by <see cref="ToJson"/>.</summary>
    /// <exception cref="JsonException">The JSON is not a valid save.</exception>
    public static SaveGame FromJson(string json) =>
        JsonSerializer.Deserialize<SaveGame>(json, JsonOptions)
            ?? throw new JsonException("Save file deserialized to null.");
}

/// <summary>A colony inside a <see cref="SaveGame"/>.</summary>
/// <param name="Id">Colony id.</param>
/// <param name="Name">Display name.</param>
/// <param name="X">Map column.</param>
/// <param name="Y">Map row.</param>
/// <param name="Population">Colonists living in the colony.</param>
/// <param name="Stores">Warehouse contents by goods id (null in pre-v4 saves / when empty).</param>
/// <param name="Workers">Tile work assignments (null in pre-v5 saves / when none).</param>
/// <param name="Buildings">Building type ids (null in pre-v6 saves → free base set re-derived).</param>
/// <param name="BuildingWorkers">Building staffing (null when none).</param>
/// <param name="CurrentBuild">Building under construction — the front of the build queue (null when idle / pre-v7).</param>
/// <param name="OwnerId">Owning colonial player id (null = the human, id 0; v20+, FP-2).</param>
/// <param name="Liberty">Accumulated Sons-of-Liberty points (null = 0; v22, additive).</param>
/// <param name="BuildQueueRest">Queued buildables after the front (<see cref="CurrentBuild"/>); null/omitted for a 0- or 1-item queue (v24, additive — a colony with no queued tail serializes byte-identically to v23).</param>
/// <param name="Exports">Custom-house export settings by good (only non-default goods; null/omitted when none; v28, additive).</param>
/// <param name="BuildingWorkerTypes">Per building, its NON-FREE occupant unit-type ids (v30; null/omitted when every building worker is a free colonist). The free occupants are implicit (count − non-free).</param>
/// <param name="IdleWorkerTypes">The colony's NON-FREE idle colonists' unit-type ids (v30; null/omitted when all idle are free colonists).</param>
/// <param name="SchoolTraining">Per school building, the accrued training turns toward its current student (v32; null/omitted when no school is mid-training, so a non-teaching game is byte-identical to v31).</param>
/// <param name="TeaPartyBellTurns">Turns remaining of the Boston-Tea-Party bell surge (v37; null/omitted when 0, so a no-party colony is byte-identical to v36).</param>
public sealed record SavedColony(
    int Id, string Name, int X, int Y, int Population,
    IReadOnlyDictionary<string, int>? Stores = null,
    IReadOnlyList<SavedWorker>? Workers = null,
    IReadOnlyList<string>? Buildings = null,
    IReadOnlyDictionary<string, int>? BuildingWorkers = null,
    string? CurrentBuild = null,
    int? OwnerId = null,
    int? Liberty = null,
    IReadOnlyList<string>? BuildQueueRest = null,
    IReadOnlyDictionary<string, SavedExport>? Exports = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? BuildingWorkerTypes = null,
    IReadOnlyList<string>? IdleWorkerTypes = null,
    IReadOnlyDictionary<string, int>? SchoolTraining = null,
    int? TeaPartyBellTurns = null);

/// <summary>A colony's custom-house export setting for one good (v28+; only non-default goods are stored).</summary>
/// <param name="Exported">Whether the good auto-exports.</param>
/// <param name="Level">The amount to retain before exporting the surplus.</param>
public sealed record SavedExport(bool Exported, int Level);

/// <summary>A bonus resource on a tile inside a <see cref="SaveGame"/>.</summary>
/// <param name="Index">Row-major tile index (<c>y * MapWidth + x</c>).</param>
/// <param name="ResourceId">Ruleset resource id.</param>
public sealed record SavedResource(int Index, string ResourceId);

/// <summary>A finite bonus resource's remaining quantity on a tile inside a <see cref="SaveGame"/> (v46; only finite/limited resources carry one — a limitless resource is absent).</summary>
/// <param name="Index">Row-major tile index (<c>y * MapWidth + x</c>).</param>
/// <param name="Quantity">The remaining quantity (FreeCol <c>Resource.quantity</c>).</param>
public sealed record SavedResourceQuantity(int Index, int Quantity);

/// <summary>A natural tile improvement on a tile inside a <see cref="SaveGame"/> (v47; today only rivers). The improvement's rule data (modifiers, movement cost) is re-supplied from the ruleset on load; only the id + magnitude are stored.</summary>
/// <param name="Index">Row-major tile index (<c>y * MapWidth + x</c>).</param>
/// <param name="ImprovementId">Ruleset improvement id (e.g. <c>model.improvement.river</c>).</param>
/// <param name="Magnitude">The improvement's per-tile magnitude (1 = small river, 2 = large; FreeCol river section size).</param>
public sealed record SavedImprovement(int Index, string ImprovementId, int Magnitude);

/// <summary>
/// A monarch demand awaiting the human's accept/reject inside a <see cref="SaveGame"/> (v46; FreeCol persists the
/// <c>MonarchSession</c>). Mirrors <see cref="GameSession.PendingMonarchDemand"/>; only the fields a restored demand
/// needs are stored (a mercenary offer's force is a list of unit-type/role/count blocks).
/// </summary>
/// <param name="Action">The monarch action ordinal (<see cref="GameSession.MonarchAction"/>).</param>
/// <param name="TaxRaise">The proposed new tax rate (a RAISE_TAX demand; 0 otherwise).</param>
/// <param name="GoodsId">The taxed goods' id (a RAISE_TAX demand; null otherwise).</param>
/// <param name="ColonyId">The colony holding those goods (a RAISE_TAX demand; 0 otherwise).</param>
/// <param name="GoodsAmount">How much of the goods the demand concerns (a RAISE_TAX demand; 0 otherwise).</param>
/// <param name="Offer">The units offered (a mercenary offer; null otherwise).</param>
/// <param name="Price">The gold price of the offer (a mercenary offer; 0 otherwise).</param>
public sealed record SavedMonarchDemand(
    int Action, int TaxRaise = 0, string? GoodsId = null, int ColonyId = 0, int GoodsAmount = 0,
    IReadOnlyList<SavedForceEntry>? Offer = null, int Price = 0)
{
    /// <summary>Captures a live <see cref="GameSession.PendingMonarchDemand"/> for the save.</summary>
    public static SavedMonarchDemand From(GameSession.PendingMonarchDemand d) => new(
        (int)d.Action, d.TaxRaise, d.GoodsId, d.ColonyId, d.GoodsAmount,
        d.Offer?.Select(e => new SavedForceEntry(e.UnitTypeId, e.RoleId, e.Count)).ToList(), d.Price);

    /// <summary>Rebuilds the live demand on load.</summary>
    public GameSession.PendingMonarchDemand ToDemand() => new(
        (GameSession.MonarchAction)Action, TaxRaise, GoodsId, ColonyId, GoodsAmount,
        Offer?.Select(e => new GameSession.ForceEntry(e.UnitTypeId, e.RoleId, e.Count)).ToList(), Price);
}

/// <summary>One block of like units in a saved monarch offer (mirrors <see cref="GameSession.ForceEntry"/>; v46).</summary>
/// <param name="UnitTypeId">The unit type id.</param>
/// <param name="RoleId">The military role id (null = the default role).</param>
/// <param name="Count">How many.</param>
public sealed record SavedForceEntry(string UnitTypeId, string? RoleId, int Count);

/// <summary>A map <see cref="Region"/> inside a <see cref="SaveGame"/> (v35).</summary>
/// <param name="Id">Region id (indexed by <see cref="SaveGame.RegionIds"/>).</param>
/// <param name="Type">The <see cref="RegionType"/> enum ordinal.</param>
/// <param name="ScoreValue">Discovery score.</param>
/// <param name="Key">Fixed-region key (e.g. <c>model.region.arctic</c>); null/omitted for a dynamic land/mountain region.</param>
/// <param name="ParentId">Parent region id (an ocean quadrant's parent ocean); null/omitted at the top level.</param>
public sealed record SavedRegion(int Id, int Type, int ScoreValue, string? Key = null, int? ParentId = null);

/// <summary>A serialised <see cref="GameSession.Force"/> (the Royal Expeditionary Force) inside a <see cref="SaveGame"/> (v40).</summary>
/// <param name="Land">Land-unit blocks.</param>
/// <param name="Naval">Naval-unit blocks.</param>
public sealed record SavedForce(IReadOnlyList<ForceEntry> Land, IReadOnlyList<ForceEntry> Naval);

/// <summary>A colonist's tile assignment inside a <see cref="SavedColony"/>.</summary>
/// <param name="X">Worked tile column.</param>
/// <param name="Y">Worked tile row.</param>
/// <param name="GoodsId">Goods being produced there.</param>
/// <param name="UnitTypeId">The worker's unit-type id when it is NOT a free colonist (v30; null/omitted for a free colonist, so an all-free game is byte-identical to v29).</param>
/// <param name="Experience">A free colonist's accrued on-the-job experience toward an expert upgrade (v31; null/omitted when 0, so a game with no accrued experience is byte-identical to v30).</param>
public sealed record SavedWorker(int X, int Y, string GoodsId, string? UnitTypeId = null, int? Experience = null);

/// <summary>A native settlement inside a <see cref="SaveGame"/> (v14+).</summary>
/// <param name="Id">Settlement id.</param>
/// <param name="NationTypeId">Owning native nation type id (e.g. <c>model.nationType.apache</c>).</param>
/// <param name="SettlementTypeId">Settlement template id (e.g. <c>model.settlement.camp</c>).</param>
/// <param name="IsCapital">Whether this is the nation's capital.</param>
/// <param name="X">Map column.</param>
/// <param name="Y">Map row.</param>
/// <param name="Size">Resident population.</param>
/// <param name="LearnableSkill">Expert unit type the settlement can teach (null = none).</param>
/// <param name="Alarm">Alarm toward the player, 0–1000 (v16+; pre-v16 = 0).</param>
/// <param name="HasBeenVisited">Whether the chief has been spoken with (v16+).</param>
/// <param name="SkillConsumed">Whether the skill has been taught/consumed (v16+).</param>
/// <param name="WantedGoods">Goods the settlement most wants to buy, most-wanted first (v17+).</param>
/// <param name="MissionOwnerId">Colonial player id whose missionary resides here (v33+; null = no mission, omitted).</param>
/// <param name="MissionIsExpert">Whether the resident missionary is a jesuit (v33+; null when no mission, omitted).</param>
/// <param name="ConvertProgress">Accrued convert progress under a mission (v34+; null/omitted when 0).</param>
/// <param name="VisitedByPowers">Non-human player ids that have spoken with the chief (v44+; null/omitted when none, so a game where only the human (or nobody) has visited is byte-identical to v43). The human rides <see cref="HasBeenVisited"/>.</param>
public sealed record SavedNativeSettlement(
    int Id, string NationTypeId, string SettlementTypeId, bool IsCapital,
    int X, int Y, int Size, string? LearnableSkill = null,
    int Alarm = 0, bool HasBeenVisited = false, bool SkillConsumed = false,
    IReadOnlyList<string>? WantedGoods = null,
    int? MissionOwnerId = null, bool? MissionIsExpert = null,
    int? ConvertProgress = null,
    IReadOnlyList<int>? VisitedByPowers = null);

/// <summary>A unit inside a <see cref="SaveGame"/>.</summary>
/// <param name="Id">Unit id.</param>
/// <param name="TypeId">Ruleset unit type id (null in v1 saves → free colonist).</param>
/// <param name="X">Map column.</param>
/// <param name="Y">Map row.</param>
/// <param name="MovementLeft">Movement points remaining this turn.</param>
/// <param name="Location">Where the unit is (0 = on map; sailing/Europe in v11+).</param>
/// <param name="SailTurns">Turns left in transit (0 when not sailing).</param>
/// <param name="Cargo">Goods in the unit's hold (null when empty / pre-v11).</param>
/// <param name="CarrierId">Id of the ship carrying this unit as a passenger (null when not aboard / pre-v13).</param>
/// <param name="Owner">Owning native nation type id (null = a colonial player; pre-v18 default; v18+).</param>
/// <param name="Role">Military role id (null = the unarmed default role; pre-v18 default; v18+).</param>
/// <param name="RoleCount">Equipment count held for the role (null/0 for the default role; v18+). Nullable so a default-role unit emits no token and serializes identically to v17.</param>
/// <param name="OwnerId">Owning colonial player id (null = the human, id 0; v20+, FP-2). Foreign-power units carry their player id.</param>
/// <param name="RepairTurns">Turns left repairing a damaged ship (null/0 = healthy; v21+, 1c-3b). Nullable so a healthy fleet serializes byte-identically to v20.</param>
/// <param name="Orders">Standing order (<see cref="Units.UnitOrders"/> ordinal: 0 = active, 1 = fortifying, 2 = fortified, 3 = sentry; null/0 = active; v23+). Nullable so an active unit serializes byte-identically to v22.</param>
/// <param name="TreasureAmount">Gold carried by a treasure train (null/0 = none; v27+). Nullable so a non-treasure unit serializes byte-identically to v26.</param>
/// <param name="DestX">X of the unit's standing goto destination (null = no goto; v36+). Both DestX/DestY omitted when none, so a goto-free unit serializes byte-identically to v35.</param>
/// <param name="DestY">Y of the unit's standing goto destination (null = no goto; v36+).</param>
/// <param name="TradeRouteId">Id of the trade route this carrier is assigned to (null = none; v43+). Omitted for a route-less unit, so it serializes byte-identically to v42.</param>
/// <param name="TradeRouteStop">The carrier's current route stop index (null/0 = the first stop; v43+). Omitted when 0.</param>
/// <param name="WorkImprovement">The tile-improvement type id this pioneer is building (null = not improving; v48+). Omitted when not improving, so a unit with no build order serializes byte-identically to v47.</param>
/// <param name="WorkTurnsLeft">Turns of work left on the in-progress improvement (null/0 = none; v48+). Omitted when 0.</param>
/// <param name="Attrition">Turns this unit has spent standing in the open wilderness (null/0 = none; v50+). Nullable so a unit that has wasted no turns in the open serializes byte-identically to v49.</param>
public sealed record SavedUnit(
    int Id, string? TypeId, int X, int Y, int MovementLeft,
    int Location = 0, int SailTurns = 0, IReadOnlyDictionary<string, int>? Cargo = null,
    int? CarrierId = null,
    string? Owner = null, string? Role = null, int? RoleCount = null,
    int? OwnerId = null, int? RepairTurns = null, int? Orders = null, int? TreasureAmount = null,
    int? DestX = null, int? DestY = null,
    int? TradeRouteId = null, int? TradeRouteStop = null,
    string? WorkImprovement = null, int? WorkTurnsLeft = null,
    int? Attrition = null);

/// <summary>
/// A player inside a <see cref="SaveGame"/> (v20+). Holds the player-scoped state that used to sit as
/// flat top-level fields: treasury/tax, the per-player market, liberty/Congress, immigration + dock,
/// and explored fog (compact row-major indexes). Optional fields are omitted when empty/default.
/// </summary>
/// <param name="PlayerId">Stable player id (the human is 0).</param>
/// <param name="NationId">Nation id: a foreign power's/native's nation, or the human's chosen New-Game nation (v49, 86d3drn5x); null for the classic nation-less human (omitted, so a nation-less game stays byte-identical and pre-v49 saves load the human nation-less).</param>
/// <param name="IsHuman">Whether this is the local human player.</param>
/// <param name="PlayerType">0 = Colonial, 1 = Native (see <see cref="GameSession.PlayerType"/>).</param>
/// <param name="Gold">Treasury in gold.</param>
/// <param name="Tax">Sales tax percentage.</param>
/// <param name="MarketState">Market inventories that have moved from their ruleset seed (sparse; null when none).</param>
/// <param name="Liberty">Liberty banked toward the next Founding Father.</param>
/// <param name="Congress">Elected Founding Father ids, in order (null when none).</param>
/// <param name="CurrentFather">The father currently being recruited (null when none).</param>
/// <param name="OfferedFathers">The fathers offered this round (null when none).</param>
/// <param name="Immigration">Immigration points banked toward the next emigrant.</param>
/// <param name="ImmigrationRequired">Immigration points required for the next emigrant (null → classic default 15).</param>
/// <param name="BaseRecruitPrice">Escalating base recruit price (null → classic default 200).</param>
/// <param name="RecruitLowerCap">Recruit-price floor (null → classic default 80).</param>
/// <param name="RecruitDock">Unit types waiting on the Europe dock (null when none).</param>
/// <param name="Explored">Explored tile indexes (row-major <c>y * MapWidth + x</c>); null = pre-fog fallback.</param>
/// <param name="RngState">A non-human player's own PCG stream state word (v20 additive, FP-4; null = the human / no stream).</param>
/// <param name="RngIncrement">That stream's increment (paired with <paramref name="RngState"/>; null = the human / no stream).</param>
/// <param name="Stances">This player's diplomatic stance toward each other player it has met, by their player id (v20 additive, FP-6a; null/omitted when it has met no one). An ordinal of <see cref="GameSession.Stance"/>.</param>
/// <param name="Tensions">This player's tension toward each other player, by their player id (v20 additive, FP-6a; null/omitted when all zero).</param>
/// <param name="UnitPrices">This player's escalated Europe purchase prices by unit-type id (v29 additive; null/omitted when none have escalated, so a game where nobody has bought artillery stays byte-identical to v28). Today only artillery escalates.</param>
/// <param name="Arrears">This player's boycott back-tax by good (v37 additive; null/omitted when nothing is boycotted, so a boycott-free game stays byte-identical to v36). A non-zero entry means the good cannot be sold until paid off.</param>
/// <param name="MonarchDispleasure">Whether the King is displeased with this player (v38 additive; null/omitted when content). While displeased the King offers no mercenaries or military support.</param>
/// <param name="SupportSeaGranted">Whether the King has granted this player naval support (v39 additive; null/omitted when not — a one-shot so SUPPORT_SEA cannot repeat).</param>
/// <param name="DeclaredIndependenceTurn">The turn this player declared independence (v41 additive; null/omitted if it never did).</param>
/// <param name="InterventionBells">Bells accrued toward the Foreign Intervention Force (v41 additive; null/omitted when 0).</param>
/// <param name="TradeRoutes">This player's trade routes (v43 additive; null/omitted when it has none, so a route-free game stays byte-identical to v42).</param>
/// <param name="NextTradeRouteId">The player's monotonic next-trade-route id counter (v45 additive; null/omitted when still 1 — the default — so a game that never created a route stays byte-identical to v44). Persisted (FreeCol persists <c>Game.nextId</c>) so ids are never reused after a route is deleted and the game is reloaded; pre-v45 saves fall back to <c>max(route id) + 1</c>.</param>
public sealed record SavedPlayer(
    int PlayerId, string? NationId, bool IsHuman, int PlayerType,
    int Gold = 0, int Tax = 0,
    IReadOnlyDictionary<string, int>? MarketState = null,
    int Liberty = 0, IReadOnlyList<string>? Congress = null, string? CurrentFather = null,
    IReadOnlyList<string>? OfferedFathers = null,
    int Immigration = 0, int? ImmigrationRequired = null,
    int? BaseRecruitPrice = null, int? RecruitLowerCap = null,
    IReadOnlyList<string>? RecruitDock = null,
    IReadOnlyList<int>? Explored = null,
    ulong? RngState = null, ulong? RngIncrement = null,
    IReadOnlyDictionary<int, Stance>? Stances = null,
    IReadOnlyDictionary<int, int>? Tensions = null,
    IReadOnlyDictionary<string, int>? UnitPrices = null,
    IReadOnlyDictionary<string, int>? Arrears = null,
    bool? MonarchDispleasure = null,
    bool? SupportSeaGranted = null,
    int? DeclaredIndependenceTurn = null,
    int? InterventionBells = null,
    IReadOnlyList<SavedTradeRoute>? TradeRoutes = null,
    int? NextTradeRouteId = null);

/// <summary>A saved trade route (v43): a player's named ring of stops a carrier hauls along automatically. Omitted entirely when the player has none.</summary>
/// <param name="Id">Per-player route id.</param>
/// <param name="Name">Route name.</param>
/// <param name="Stops">The ordered stops.</param>
public sealed record SavedTradeRoute(int Id, string Name, IReadOnlyList<SavedTradeRouteStop> Stops);

/// <summary>One <see cref="SavedTradeRoute"/> stop (v43): a colony id and the goods to load there (null = load nothing — a pure delivery stop).</summary>
public sealed record SavedTradeRouteStop(int ColonyId, IReadOnlyList<string>? Load);
