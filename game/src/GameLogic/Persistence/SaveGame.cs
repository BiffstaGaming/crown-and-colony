using System.Text.Json;
using System.Text.Json.Serialization;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

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
    public const int CurrentVersion = 31;

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
    /// Each of v23–v31 is additive + omitted-when-empty, so a feature-free game round-trips byte-identically to the
    /// prior version and older saves load with the feature absent.
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

    /// <summary>The game-wide custom-house auto-export mode (v28; null/omitted for the <see cref="GameSession.AutoExportMode.PerGood"/> default, so a default game stays byte-identical to v27). Stored as the enum ordinal.</summary>
    public AutoExportMode? AutoExportMode { get; init; }

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
                    u.TreasureAmount == 0 ? null : u.TreasureAmount))
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
                    c.IdleWorkerTypes.Count > 0 ? c.IdleWorkerTypes.ToList() : null))
                .ToList(),
            Resources = game.Map.Resources.Count > 0
                ? game.Map.Resources
                    .Select(r => new SavedResource(r.Key.Y * game.Map.Width + r.Key.X, r.Value))
                    .OrderBy(r => r.Index)
                    .ToList()
                : null,
            // Lost City Rumours by row-major index; omitted when none so a rumour-free game stays byte-identical to v24.
            Rumours = game.Map.Rumours.Count > 0
                ? game.Map.Rumours.Select(p => p.Y * game.Map.Width + p.X).OrderBy(i => i).ToList()
                : null,
            // Tiles bought/taken from the natives by row-major index; omitted when none (byte-identical to v25).
            ClaimedTiles = game.Map.ClaimedFromNatives.Count > 0
                ? game.Map.ClaimedFromNatives.Select(p => p.Y * game.Map.Width + p.X).OrderBy(i => i).ToList()
                : null,
            // Custom-house auto-export mode; omitted for the PerGood default so a default game stays byte-identical to v27.
            AutoExportMode = game.AutoExportMode == GameSession.AutoExportMode.PerGood ? null : game.AutoExportMode,
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
                        s.WantedGoods.Count > 0 ? s.WantedGoods.ToList() : null))
                    .ToList()
                : null,
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
            ClaimedTiles?.Select(i => new Position(i % MapWidth, i / MapWidth)).ToList());
        return Game.Restore(
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
                u.TreasureAmount ?? 0)),    // pre-v27 / non-treasure → 0
            Colonies?.Select(c =>
            {
                var colony = new CrownAndColony.GameLogic.Colonies.Colony(
                    c.Id, c.Name, new Position(c.X, c.Y), c.Population, c.OwnerId ?? 0);
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
                // Rebuild the construction queue: the front (CurrentBuild) then the saved tail (v24; absent → ≤1 item).
                colony.SetBuildQueue(
                    (c.CurrentBuild is null ? Enumerable.Empty<string>() : [c.CurrentBuild])
                        .Concat(c.BuildQueueRest ?? []));
                colony.Liberty = c.Liberty ?? 0; // ≤v21 saves had no liberty → SoL 0%
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
                colony.ReconcileWorkerTypes(); // belt-and-braces: the overlay never exceeds the restored counts
                return colony;
            }),
            NativeSettlements?.Select(s => new NativeSettlement(
                s.Id, s.NationTypeId, s.SettlementTypeId, s.IsCapital,
                new Position(s.X, s.Y), s.Size, s.LearnableSkill)
            {
                Alarm = s.Alarm,
                HasBeenVisited = s.HasBeenVisited,
                SkillConsumed = s.SkillConsumed,
                WantedGoods = s.WantedGoods ?? [],
            }),
            AutoExportMode.GetValueOrDefault()); // pre-v28 / omitted → PerGood (the enum's 0 default)
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
        p.Stances, p.Tensions, p.UnitPrices);

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
            p.UnitPriceOverrides.Count > 0 ? new Dictionary<string, int>(p.UnitPriceOverrides) : null);
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
    IReadOnlyList<string>? IdleWorkerTypes = null);

/// <summary>A colony's custom-house export setting for one good (v28+; only non-default goods are stored).</summary>
/// <param name="Exported">Whether the good auto-exports.</param>
/// <param name="Level">The amount to retain before exporting the surplus.</param>
public sealed record SavedExport(bool Exported, int Level);

/// <summary>A bonus resource on a tile inside a <see cref="SaveGame"/>.</summary>
/// <param name="Index">Row-major tile index (<c>y * MapWidth + x</c>).</param>
/// <param name="ResourceId">Ruleset resource id.</param>
public sealed record SavedResource(int Index, string ResourceId);

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
public sealed record SavedNativeSettlement(
    int Id, string NationTypeId, string SettlementTypeId, bool IsCapital,
    int X, int Y, int Size, string? LearnableSkill = null,
    int Alarm = 0, bool HasBeenVisited = false, bool SkillConsumed = false,
    IReadOnlyList<string>? WantedGoods = null);

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
public sealed record SavedUnit(
    int Id, string? TypeId, int X, int Y, int MovementLeft,
    int Location = 0, int SailTurns = 0, IReadOnlyDictionary<string, int>? Cargo = null,
    int? CarrierId = null,
    string? Owner = null, string? Role = null, int? RoleCount = null,
    int? OwnerId = null, int? RepairTurns = null, int? Orders = null, int? TreasureAmount = null);

/// <summary>
/// A player inside a <see cref="SaveGame"/> (v20+). Holds the player-scoped state that used to sit as
/// flat top-level fields: treasury/tax, the per-player market, liberty/Congress, immigration + dock,
/// and explored fog (compact row-major indexes). Optional fields are omitted when empty/default.
/// </summary>
/// <param name="PlayerId">Stable player id (the human is 0).</param>
/// <param name="NationId">Nation type id (null for the classic human until European nations land).</param>
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
    IReadOnlyDictionary<string, int>? UnitPrices = null);
