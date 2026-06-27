using System.Text.Json;
using System.Text.Json.Serialization;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
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
    public const int CurrentVersion = 67;

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
    /// v51 added per-region <b>discovery</b> state (86d3c9w2f): a discovered region's
    /// <see cref="SavedRegion.DiscoveredBy"/> player id + <see cref="SavedRegion.Name"/> + <see cref="SavedRegion.DiscoveredInTurn"/>,
    /// all additive + omitted while the region is undiscovered (<c>DiscoveredBy</c> null — the common case, and every region
    /// in a fresh game). An undiscovered map therefore serializes byte-identically to v50, and pre-v51 saves load every
    /// region undiscovered. The discovery <i>score</i> rides the (in-memory, un-persisted) history log, not a saved field —
    /// so the persisted per-region discovered flag is what prevents a re-revealed region from being re-discovered.
    /// v52 added a unit's <b>nationality</b> + <b>ethnicity</b> (its origin nation, FreeCol <c>Unit.nationality</c>/
    /// <c>Unit.ethnicity</c> — 86d3drmzz) and its individual <b>custom name</b> (FreeCol <c>Unit.name</c>, a
    /// <c>Nameable</c> — 86d3drmzu): three additive fields (<see cref="SavedUnit.Nationality"/>,
    /// <see cref="SavedUnit.Ethnicity"/>, <see cref="SavedUnit.Name"/>). <b>Name</b> is omitted when un-christened (the
    /// common case). <b>Nationality/ethnicity</b> are omitted when they still equal the unit's <em>owner-derived</em>
    /// nation (<c>Game.OwnerNationOf</c>) — every freshly-raised unit (a nation-less human's colonist → null; a native
    /// brave → its nation, which already rides <see cref="SavedUnit.Owner"/>) — and re-derived from the owner on load;
    /// only a unit whose origin has <em>diverged</em> from its owner (a captured colonist) writes an explicit token. So a
    /// default game serialises byte-identically to v51, and pre-v52 saves (no tokens) re-derive every unit's origin from
    /// its owner on load.
    /// v53 added each colonial player's <b>peace-turn</b> stamps (<see cref="SavedPlayer.PeaceTurns"/> — the turn its peace
    /// with each other player took force, FreeCol <c>EuropeanAIPlayer.peaceHolds</c>' <c>peaceTurn</c>), so Benjamin
    /// Franklin's <c>peaceTreaty</c> peace-hold can decay the hold-probability over turns-since-peace
    /// (<c>(PEACE_PROBABILITY/100)^n</c>). Additive + <b>omitted when empty</b>: a player with no recorded peace (every
    /// player in a no-contact game, and the common pre-contact case) emits no map, so a default game serialises
    /// byte-identically to v52, and pre-v53 saves load with no stamps — the peace-hold gate is inert without Franklin
    /// anyway (probability 0, no roll), so the default game is unaffected.
    /// v54 added a native settlement's <b>military stock</b> (<see cref="SavedNativeSettlement.MilitaryStock"/> — the
    /// muskets/horses it retains to arm its braves, FreeCol <c>IndianSettlement</c>'s military-goods retention,
    /// 86d3e49gq). The generator now <b>seeds</b> this stock at game creation (sized by settlement type/capital/size),
    /// so a real game's threatened camps actually arm their braves via the existing equip chain. Additive +
    /// <b>omitted when empty</b> (a goods-id → amount map, serialized only when the settlement holds something — and
    /// the seed never writes a zero entry), so an empty-stock settlement serialises byte-identically to v53 and pre-v54
    /// saves load every settlement with no stock (the equip chain stays inert, exactly as before this version). The
    /// seed and the equip spend are both RNG-free, so the human's stream 0 stays byte-stable (ADR-009).
    /// v55 made a native settlement's alarm <b>per-player</b> (<see cref="SavedNativeSettlement.AlarmChannels"/> — a
    /// player-id → alarm map, FreeCol <c>IndianSettlement</c>'s <c>Map&lt;Player, Tension&gt;</c>, 86d3e4bfb), so a
    /// tribe can be friendly to one colonial power and hostile to another and a rival power builds its own native
    /// relations. Additive + <b>omit-when-default</b>: the map is written only when the settlement holds a non-zero
    /// alarm toward someone (the fresh / fully-at-peace case writes nothing), and any zero channel is dropped, so a
    /// human-only-at-peace settlement serialises byte-identically to v54. <b>Migration:</b> a pre-v55 save's single
    /// <see cref="SavedNativeSettlement.Alarm"/> scalar (which was effectively the human's alarm) loads into the human's
    /// channel (player id 0); a v55 save omits that legacy scalar (writes 0). Determinism (ADR-009): a single-power
    /// (human-only) game keeps exactly its old behaviour — only channel 0 ever exists, so raid-targeting, ambient
    /// pressure and decay are unchanged. (Making alarm per-player DOES change native raid-targeting in a multi-power
    /// game — that is the intended behaviour change; the soak runs multiple powers but its determinism twin-run stays
    /// byte-identical because nothing draws new randomness.)
    /// v56 added per-good <b>trade accounting</b> for the classic Trade report (<see cref="SavedPlayer.TradeAccounts"/> —
    /// each good's cumulative net <c>Sales</c>/<c>IncomeBeforeTaxes</c>/<c>IncomeAfterTaxes</c>, FreeCol <c>MarketData</c>'s
    /// <c>sales</c>/<c>incomeBeforeTaxes</c>/<c>incomeAfterTaxes</c>, 86d3e4bdm). A sell adds the units + pre/post-tax
    /// revenue, a buy subtracts the units + cost (a buy is a negative sale; FreeCol charges no tax on a buy, so the same
    /// cost comes off both incomes). Additive + <b>omit-when-default</b>: the map is written only for goods that have
    /// actually been traded (any of the three counters non-zero), so a never-traded market emits nothing and a default
    /// game (no trade yet) serialises byte-identically to v55; pre-v56 saves load with all counters 0. Determinism
    /// (ADR-009): the counters accumulate on the existing sells/buys with no new RNG and never feed the market price
    /// model, so the default game's market evolution is unchanged and the soak twin-run stays byte-identical.
    /// v57 added a native settlement's <b>general goods stock</b> (<see cref="SavedNativeSettlement.GeneralStock"/> — the
    /// non-military wares it offers a trader to buy and the basis of its stock-driven wanted goods, FreeCol
    /// <c>IndianSettlement</c>'s goods container, 86d3e4bh2), enabling <b>buy-from-settlement</b> two-way trade. The
    /// generator now seeds this store at game creation (a per-settlement-varied spread of New World raw goods, drawn on
    /// the native stream) and each settlement's <see cref="SavedNativeSettlement.WantedGoods"/> is <b>derived from it</b>
    /// (FreeCol <c>updateWantedGoods</c>) rather than being 3 fixed random goods. Additive + <b>omit-when-default</b> (a
    /// goods-id → amount map, written only when the settlement holds general stock), so a no-stock settlement serialises
    /// without the field; pre-v57 saves load with no general stock. <b>Determinism / behaviour shift (ADR-009):</b>
    /// stock-driven wanted goods <em>do</em> change the default game's native cravings vs. the old random pick — an
    /// intended, documented shift — but the seeding is RNG-free of stream 0 (drawn on the native stream at generation)
    /// and buying/recompute draw no RNG, so the soak twin-run stays byte-identical (the soak never buys from natives) and
    /// every save round-trips byte-identically. Haggling (interactive, human-only) draws on a dedicated native-trade
    /// stream that is not serialized (it is not replayed), so it never perturbs stream 0.
    /// v58 added the human's <b>history log</b> (<see cref="History"/> — the turn-stamped, score-bearing notable events
    /// FreeCol keeps on the player: colonies founded, wars entered, regions discovered, cities of gold found,
    /// settlements/nations razed, 86d3e4btq). Persisting it makes the discovery / independence / destruction events
    /// <b>and their scores</b> survive reload, so the History report and the history-event summand of
    /// <see cref="Game.PlayerScore"/> are stable across save/load (before v58 the log was in-memory only — a reloaded
    /// game lost its discovery score and any destruction penalty). Additive + <b>omitted when the log is empty</b> (a
    /// goods-id-free list written only when something has been recorded), so a fresh / event-free game serialises
    /// byte-identically to v57 and pre-v58 saves load with an empty log exactly as before persistence. Determinism
    /// (ADR-009): the log is pure UI/score scratch with no feedback into game evolution, so a reloaded game continues on
    /// the identical random sequence and the soak twin-run stays byte-identical (it never records the score-bearing
    /// events the human earns by exploring/razing).
    /// v59 added the in-session <b>message log</b> (<see cref="MessageLog"/> — the per-turn player notices the
    /// dismissible turn-message panel showed and then forgot: combat raids, native pillages, custom-house sales,
    /// monarch decrees, famines, …, FreeCol persisting <c>ModelMessage</c>s on the Player, 86d3e959t). Each entry keeps
    /// its turn, its <see cref="App.MessageCategory"/> (for the log's category filter) and the already-formatted
    /// player-facing text. Before v59 the log lived only in the controller and was lost on save/load; persisting it lets
    /// the re-openable message log survive a reload. Because the formatting is presentation-side (ADR-006), the
    /// <em>controller</em> populates this field via a <c>with</c> after <see cref="From"/> and reads it back on load —
    /// <see cref="From"/> itself never touches it (it has no formatted strings). Additive + <b>omitted when the log is
    /// empty</b> (null), so a fresh game (which has logged no notices yet) serialises byte-identically to v58 and pre-v59
    /// saves (or any with no log) load with an empty log exactly as before persistence. Determinism (ADR-009): the log
    /// is pure UI scratch with no feedback into game evolution, so a reloaded game continues on the identical random
    /// sequence and the headless soak (which never builds the controller, so never fills this field) stays byte-identical.
    /// v60 added the turn the King last raised a player's tax (<see cref="SavedPlayer.LastTaxRaiseTurn"/>, 86d3f674b),
    /// which enforces the inter-raise tax <b>grace period</b> (<see cref="Game.TaxRaiseGraceTurns"/>) deterministically
    /// across save/load — without it a reload mid-cooldown would let the King raise again immediately (a save-scum hole).
    /// Additive + <b>omitted when null</b> (the King has not yet raised tax — every fresh game and the whole early game),
    /// so a game with no tax raise yet serialises byte-identically to v59 and pre-v60 saves load with no cooldown active.
    /// Determinism (ADR-009): the cooldown only gates which actions the chooser offers (it draws nothing itself), and the
    /// stamp is persisted, so a reloaded game offers the identical action set and the soak stays twin-deterministic.
    /// v61 changed school training to <b>per-teacher-slot</b> counters (<see cref="SavedColony.SchoolTrainingSlots"/>,
    /// 86d3fpyc0): a college/university now teaches one student per teacher in parallel, so each teacher carries its own
    /// accrued-turn counter (a list per school) rather than one counter per building. The legacy single-int field
    /// (<see cref="SavedColony.SchoolTraining"/>, v32..v60) is still <em>read</em> for older saves — restored as a
    /// school's slot-0 (first teacher's) turns — but no longer written. Additive + <b>omitted when no school is
    /// mid-training</b> (null), so a non-teaching game serialises byte-identically to v60 and pre-v61 saves load with each
    /// in-progress school continuing on its first teacher. Determinism (ADR-009): teaching draws no RNG and the soak's
    /// all-free colonies never staff a school, so the schema change is soak-inert and twin-deterministic.
    /// v62 added the <b>endgame niceties</b> (86d3fq125 / 86d3fq161 / 86d3fq0ak): (a) the winner's <b>keep-playing</b>
    /// flag (<see cref="VictoryConditionsDisabled"/> — FreeCol <c>continuePlaying</c> writes the three <c>VICTORY_*</c>
    /// options false), so a game saved after choosing to play on past victory reloads with the win still disabled; and
    /// (b) the REF's <b>staggered reinforcement</b> state — the turns until the next landing wave
    /// (<see cref="RefWaveCountdown"/>) and the King's morale <b>peak</b> (<see cref="RefMoralePeak"/>, the army he
    /// committed; the current morale is derived live from the survivors, so it needs no field), so a war saved
    /// mid-reinforcement reloads with the remaining waves and the King's resolve intact. (The un-landed units themselves
    /// are ordinary REF units sitting in Europe, already persisted by the unit list — no force snapshot is needed.)
    /// (Voluntary <b>retirement</b> needed no new field — a retired player rides the existing
    /// <see cref="GameSession.PlayerType"/> ordinal.) All three are additive + <b>omitted when default</b>
    /// (false / null / 0 — every game that has not won-and-continued and is not mid-REF-war), so a default game serialises
    /// byte-identically to v61 and pre-v62 saves load with no kept-playing override and no pending-wave timer (a war
    /// reloaded from a pre-v62 save resumes under the v62 rules — its REF comes ashore in staggered waves from the next
    /// REF turn, with the wave timer starting at 0 — and never breaks on morale, since the peak loads as 0).
    /// Determinism (ADR-009): the kept-playing flag and the wave/morale state draw no RNG (the cadence is a fixed
    /// interval, morale a pure count of losses), so a reloaded game continues on the identical random sequence and the
    /// soak — which never wins or reaches the war in its window — stays byte-identical and twin-deterministic.
    /// v63 added the <b>Seven Cities of Gold</b> counter (<see cref="CitiesOfCibolaRemaining"/> — how many named
    /// Cibola cities are still undiscovered, FreeCol's finite <c>NameCache</c> Cibola list, 86d3fpxrc). Once all seven
    /// are found, a CIBOLA lost-city rumour degrades to an ordinary ruins find instead of always spawning a big
    /// treasure train. Additive + <b>omitted when default</b> (the field equals <see cref="Game.CibolaCityCount"/> = 7,
    /// i.e. none found — every fresh game and the whole early game), so a game where no Cibola has been discovered
    /// serialises byte-identically to v62 and pre-v63 saves load with the full count of seven (the classic count).
    /// Determinism (ADR-009): the counter only gates which Cibola outcome resolves (the treasure draw it skips when
    /// exhausted matches FreeCol's null-name fall-through), so the human's stream 0 stays consistent across reload and
    /// the soak — which never explores a rumour — stays byte-identical and twin-deterministic.
    /// v64 made the river <b>magnitude</b> generation state the map generator assigns (the per-tile river size, small =
    /// 1 / large = 2): a river section widens to large at and downstream of a tributary confluence (FreeCol
    /// <c>River.grow</c>/<c>RiverSection.grow</c>, 86d3fpxbx), and the renderer draws size from this stored magnitude
    /// rather than re-deriving it from connectivity. The magnitude already rode in the <see cref="Improvements"/> token
    /// (per-tile <see cref="SavedImprovement.Magnitude"/>, v47), so v64 adds <b>no field</b> — it is the first version
    /// whose generator emits large magnitudes, so a v64 river save round-trips large sections exactly. Omitted-when-
    /// default still holds (a riverless map writes no <see cref="Improvements"/> token, byte-identical to v63); a
    /// pre-v64 save loads with whatever magnitudes it stored (an all-small pre-this-feature map, or none), so old saves
    /// open unchanged. Determinism (ADR-009): the magnitude growth is a pure, RNG-free post-process of the already-
    /// walked river paths + recorded confluences — it draws no randomness, so the default map's stream-0 sequence is
    /// untouched and the soak stays byte-identical and twin-deterministic.
    /// v65 added the human nation's <b>year-by-year demographic series</b> (<see cref="Demographics"/> — the
    /// population / gold / score recorded at each year-rollover, driving the end-game demographics graph; 86d3fq27v) as
    /// a new top-level field. Additive + <b>omitted when the series is empty</b> (null), so a brand-new game whose first
    /// year has not yet rolled over serialises byte-identically to v64; pre-v65 saves (or any with no snapshots) load
    /// with an empty series exactly as before, and the series accrues again as play continues. Determinism (ADR-009):
    /// the series is pure UI scratch — recorded by reading existing population/gold/score totals and never fed back into
    /// game evolution — so a reloaded game continues on the identical random sequence whether or not it was persisted,
    /// and the soak stays byte-identical and twin-deterministic. (The history log — v58, <see cref="History"/> — is
    /// unchanged at v65; it already round-trips, so the History report has been save-stable since v58.)
    /// v66 added the human's <b>name for the New World</b> (<see cref="NewWorldName"/> — the continent name the player
    /// types at the first-landfall prompt, FreeCol <c>Player.newLandName</c> / <c>NameCache.getNewLandName</c>; 86d3fq1fn)
    /// as a new top-level field. Additive + <b>omitted when the world is unnamed</b> (null), so a game that has not yet
    /// made landfall (or that never answered the prompt) serialises byte-identically to v65; pre-v66 saves (or any with no
    /// name) load with the world unnamed exactly as before — the prompt then fires naturally on the next first landfall.
    /// A non-null name marks the world named <em>and</em> landed-on, so a reloaded named game never re-prompts. Determinism
    /// (ADR-009): the name is pure UI scratch — set by the player, never fed back into game evolution, drawing no RNG (the
    /// default suggested name is a fixed string) — so a reloaded game continues on the identical random sequence whether or
    /// not it was persisted, and the soak (which never makes a human landfall in its window) stays byte-identical and
    /// twin-deterministic. (The one-shot first-landfall flag is <b>not</b> separately persisted: it is implied by the
    /// name's presence once answered, and a save taken in the one-turn gap before the player names the world simply
    /// re-prompts on load — harmless and idempotent.)
    /// v67 added a colony's per-good custom-house <b>import level</b> (<see cref="SavedExport.ImportLevel"/> — the ceiling
    /// an automatic delivery, i.e. a trade-route drop-off, will not stock a good past, FreeCol <c>ExportData.importLevel</c> /
    /// <c>getEffectiveImportLevel</c>; 86d3fpz0t). It rides the <em>existing</em> per-colony <see cref="SavedColony.Exports"/>
    /// token (which already serialised the export flag + retain level for non-default goods) as a third field on
    /// <see cref="SavedExport"/>. Additive + <b>omitted when default</b>: the field defaults to <see cref="Colony.ImportLevelUnset"/>
    /// (−1, "not set" → the effective cap is the warehouse capacity, i.e. no extra limit), and a good is only written to the
    /// <see cref="SavedColony.Exports"/> map when it is non-default in <em>any</em> dimension (exported, retain ≠ 50, or import
    /// level set), so a good with only an import level now serialises and a good at the all-default state still emits nothing.
    /// A default game (no export toggles, no import levels) therefore serialises <b>byte-identically to v66</b> — the
    /// <see cref="SavedColony.Exports"/> token is still omitted entirely — and pre-v67 saves (v28..v66, whose
    /// <see cref="SavedExport"/> rows carried only the two export fields) load with every good's import level unset, i.e.
    /// auto-delivery bounded only by the warehouse exactly as before this field existed. Determinism (ADR-009): the import
    /// cap acts only on the trade-route delivery path (a pure min against the effective level), draws no RNG, and is inert
    /// until a player sets a level, so a reloaded game continues on the identical random sequence and the soak — which runs
    /// no import-capped trade routes — stays byte-identical and twin-deterministic.
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

    /// <summary>
    /// The next unit id the allocator will hand out (v54). The id counter is monotonic and never rewound when a unit is
    /// destroyed, so it can run ahead of <c>max(existing unit id) + 1</c>; persisting it keeps a save/load round-trip
    /// byte-identical when the highest-id unit ever created has since been removed. <b>Omitted</b> (null) whenever it
    /// equals <c>max(existing unit id) + 1</c> — the common case — so a typical game serialises byte-identically and
    /// only a game that has destroyed its highest-id unit writes the field; pre-v54 / omitted saves re-derive it from
    /// the surviving units exactly as before.
    /// </summary>
    public int? NextUnitId { get; init; }

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

    /// <summary>Whether the winner chose to keep playing past victory, disabling the victory conditions (v62; FreeCol <c>continuePlaying</c>). Null/omitted when false — every game that has not won-and-continued — so a default game stays byte-identical to v61; pre-v62 saves load with the win still enabled.</summary>
    public bool? VictoryConditionsDisabled { get; init; }

    /// <summary>Turns until the King's next REF reinforcement wave comes ashore (v62; null/omitted when 0 — no wave pending). Pre-v62 saves load with the timer at 0, so the REF resumes coming ashore in staggered waves from the next REF turn (the v62 rules), not all at once.</summary>
    public int? RefWaveCountdown { get; init; }

    /// <summary>The high-water mark of the King's morale — the full land army he committed at the declaration, the denominator of the morale-break test (v62; null/omitted when 0, i.e. no REF at war). The <em>current</em> morale is derived live from the survivors, so only this peak persists. Pre-v62 saves load with peak 0 (a reloaded war never breaks on morale, only on the raw unit thresholds, exactly as before this version).</summary>
    public int? RefMoralePeak { get; init; }

    /// <summary>How many of the Seven Cities of Gold are still undiscovered (v63; null/omitted when the full count of <see cref="Game.CibolaCityCount"/> = 7 remains — every game where no Cibola has been found, so a default game stays byte-identical to v62). At 0 a Cibola rumour degrades to ordinary ruins. Pre-v63 saves load with the full count of seven (the classic count).</summary>
    public int? CitiesOfCibolaRemaining { get; init; }

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

    /// <summary>
    /// The human's history log — notable events in chronological order (FreeCol's player <c>HistoryEvent</c> list:
    /// colonies founded, wars entered, regions discovered, cities of gold found, settlements razed), each with the
    /// turn-stamped, score-bearing detail (v58). Persisting it makes the discovery / independence / destruction events
    /// <b>and their scores</b> survive a save/load round-trip, so the History report and the history-event score
    /// summand of <see cref="Game.PlayerScore"/> are stable across reload. Additive + <b>omitted when the log is
    /// empty</b> (null), so a game with no recorded events serialises byte-identically to v57; pre-v58 saves (or any
    /// with no log) load with an empty history exactly as before persistence — events then accrue again as play
    /// continues. Determinism (ADR-009): the log is pure UI/score scratch with no feedback into game evolution, so a
    /// reloaded game continues on the identical random sequence whether or not the log was persisted.
    /// </summary>
    public IReadOnlyList<SavedHistoryEvent>? History { get; init; }

    /// <summary>
    /// The human nation's year-by-year demographic series — for each completed year, the colonial population, treasury
    /// gold and player score recorded at the year-rollover (v65; the end-game demographics graph's source). Persisting it
    /// makes the trend graph survive a save/load round-trip. Additive + <b>omitted when the series is empty</b> (null), so
    /// a brand-new game whose first year has not yet rolled over serialises byte-identically to v64; pre-v65 saves (or any
    /// with no snapshots) load with an empty series exactly as before, then the series accrues again as play continues.
    /// Determinism (ADR-009): the series is pure UI scratch with no feedback into game evolution, so a reloaded game
    /// continues on the identical random sequence whether or not it was persisted.
    /// </summary>
    public IReadOnlyList<SavedDemographicSnapshot>? Demographics { get; init; }

    /// <summary>
    /// The human's name for the New World — the continent name the player typed at the first-landfall prompt (FreeCol
    /// <c>Player.newLandName</c>; v66, 86d3fq1fn). Additive + <b>omitted when null</b> (the world has not been named — every
    /// game before the human's first landfall, and the whole pre-landing early game), so an unnamed game serialises
    /// byte-identically to v65 and pre-v66 saves load the world unnamed (the prompt fires on the next first landfall). A
    /// non-null name marks the world named <b>and</b> landed-on, so a reloaded named game never re-prompts. Pure UI scratch
    /// (set by the player, never fed back into game evolution, RNG-free), so a reloaded game continues on the identical
    /// random sequence whether or not it was persisted.
    /// </summary>
    public string? NewWorldName { get; init; }

    /// <summary>
    /// The in-session message log — the per-turn player notices the dismissible turn-message panel surfaced after each
    /// End Turn and then forgot (combat raids, native pillages, custom-house sales, monarch decrees, famines, …),
    /// kept so the player can re-open the log at any time (FreeCol persists <c>ModelMessage</c>s on the Player; v59).
    /// Each entry carries its turn, its <see cref="App.MessageCategory"/> (for the log's category filter) and the
    /// already-formatted player-facing text. <b>Presentation-owned (ADR-006):</b> the strings are formatted by the
    /// controller, so the controller sets this field via a <c>with</c> after <see cref="From"/> and reads it back on
    /// load; <see cref="From"/> never populates it. Additive + <b>omitted when the log is empty</b> (null), so a fresh
    /// game (no notices logged yet) serialises byte-identically to v58; pre-v59 saves (or any with no log) load with an
    /// empty log exactly as before persistence. Determinism (ADR-009): pure UI scratch — no feedback into game
    /// evolution, so a reloaded game continues on the identical random sequence.
    /// </summary>
    public IReadOnlyList<SavedLogMessage>? MessageLog { get; init; }

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
            // Persist the id counter only when it runs ahead of max+1 (a higher-id unit was created then destroyed);
            // otherwise omit it so the common case stays byte-identical and re-derives from the units on load (v54).
            NextUnitId = game.NextUnitId > (game.Units.Count > 0 ? game.Units.Max(u => u.Id) + 1 : 1)
                ? game.NextUnitId
                : null,
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
                    u.Attrition == 0 ? null : u.Attrition,
                    // Origin nationality + ethnicity (v52); omitted when they equal the owner-derived nation (the spawn
                    // default — every freshly-raised unit, incl. a native brave whose nation already rides OwnerNationId),
                    // so the default game stays byte-identical to v51. Only a DIVERGED origin (a captured colonist) writes
                    // an explicit token; on load a null re-derives the owner's nation (see Restore).
                    u.Nationality == game.OwnerNationOf(u) ? null : u.Nationality,
                    u.Ethnicity == game.OwnerNationOf(u) ? null : u.Ethnicity,
                    // Individual custom name (v52); omitted when un-christened (the common case), so a default game stays byte-identical to v51.
                    u.Name))
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
                    // Custom-house export/import settings; only non-default goods are stored, omitted when none (v28; the
                    // import level rides as a third field, v67 — a good with only an import level is non-default, so it is
                    // written; a good's import level defaults to ImportLevelUnset so an export-only good serialises as before).
                    c.Exports.Count > 0
                        ? c.Exports.ToDictionary(kv => kv.Key, kv => new SavedExport(
                            kv.Value.Exported, kv.Value.ExportLevel,
                            // ImportLevelUnset (−1) → null so WhenWritingNull omits it, keeping an export-only row byte-identical to v66.
                            kv.Value.ImportLevel >= 0 ? kv.Value.ImportLevel : null))
                        : null,
                    // Per-colonist worker types (v30): a building's non-free occupants + the non-free idle colonists,
                    // omitted when all free so a free-colonist-only colony stays byte-identical to v29.
                    c.BuildingWorkerTypes.Count > 0
                        ? c.BuildingWorkerTypes.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToList())
                        : null,
                    c.IdleWorkerTypes.Count > 0 ? c.IdleWorkerTypes.ToList() : null,
                    SchoolTraining: null, // v60: superseded by SchoolTrainingSlots (per-teacher parallel teaching); never written
                    // Boston-Tea-Party bell-surge turns remaining; omitted when none (v37, byte-identical to v36).
                    TeaPartyBellTurns: c.TeaPartyBellTurns == 0 ? null : c.TeaPartyBellTurns,
                    // Per-teacher-slot school training (v60): one accrued-turn counter per parallel teacher; omitted when
                    // no school is mid-training, so a non-teaching game stays byte-identical to v59.
                    SchoolTrainingSlots: c.SchoolTrainingTurns.Count > 0
                        ? c.SchoolTrainingTurns.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value.ToList())
                        : null))
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
                    // Discovery fields (v51) are omitted while undiscovered (DiscoveredBy null), so an undiscovered
                    // region serializes byte-identically to v50; a discovered region carries its discoverer/name/turn.
                    .Select(r => new SavedRegion(
                        r.Id, (int)r.Type, r.ScoreValue, r.Key, r.ParentId,
                        r.DiscoveredBy, r.Name, r.DiscoveredInTurn))
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
            // The winner's keep-playing flag (v62); omitted when false so a game that has not won-and-continued stays byte-identical to v61.
            VictoryConditionsDisabled = game.VictoryConditionsDisabled ? true : null,
            // The REF wave timer + the King's morale-peak (v62); both omitted when 0 (no REF at war), so a game not
            // mid-REF-war stays byte-identical to v61. The current morale is derived live, so it is not persisted.
            RefWaveCountdown = game.RefWaveCountdown != 0 ? game.RefWaveCountdown : null,
            RefMoralePeak = game.RefMoralePeak != 0 ? game.RefMoralePeak : null,
            // The Seven Cities of Gold counter (v63); omitted when the full count remains (none found), so a game where
            // no Cibola has been discovered stays byte-identical to v62. Only a game that has found at least one writes it.
            CitiesOfCibolaRemaining = game.CitiesOfCibolaRemaining != Game.CibolaCityCount
                ? game.CitiesOfCibolaRemaining
                : null,
            // Player-scoped state: authoritative in (and written only to) Players[]. The legacy flat
            // top-level fields are no longer written as of FP-7 — they remain readable for ≤v19 / pre-FP-7
            // v20 saves (the fold path), but the v20 load path was always Players[]-only, so the format
            // version stays 20 and new v20 saves are simply smaller.
            Players = game.Players.Select(p => ToSavedPlayer(p, game.Map)).ToList(),
            // The human's history log (v58); omitted when empty so a game with no recorded events stays byte-identical
            // to v57. Each event keeps its kind/turn/description/score so the report and the score summand round-trip.
            History = game.History.Count > 0
                ? game.History
                    .Select(h => new SavedHistoryEvent((int)h.Kind, h.Turn, h.Description, h.Score))
                    .ToList()
                : null,
            // The human's year-by-year demographic series (v65); omitted when empty so a game whose first year has not yet
            // rolled over stays byte-identical to v64. Each snapshot keeps its year/population/gold/score so the trend graph round-trips.
            Demographics = game.Demographics.Count > 0
                ? game.Demographics
                    .Select(d => new SavedDemographicSnapshot(d.Year, d.Population, d.Gold, d.Score))
                    .ToList()
                : null,
            // The human's New-World name (v66); omitted when the world is unnamed (the common pre-landfall case), so an
            // unnamed game stays byte-identical to v65. Only a named world writes the field (WhenWritingNull omits it).
            NewWorldName = game.NewWorldName,
            NativeSettlements = game.NativeSettlements.Count > 0
                ? game.NativeSettlements
                    .Select(s => new SavedNativeSettlement(
                        s.Id, s.NationTypeId, s.SettlementTypeId, s.IsCapital,
                        s.Position.X, s.Position.Y, s.Size, s.LearnableSkill,
                        // v55: the single Alarm scalar is retired — per-player alarm rides AlarmChannels below. We write
                        // 0 here (omitted) so it never duplicates channel 0; old v54 saves still populate it on read and
                        // migrate it into the human's channel.
                        0, s.HasBeenVisited, s.SkillConsumed,
                        s.WantedGoods.Count > 0 ? s.WantedGoods.ToList() : null,
                        s.MissionOwnerId, s.HasMission ? s.MissionIsExpert : null, // omitted when no mission
                        s.ConvertProgress != 0 ? s.ConvertProgress : null,         // omitted when no progress banked
                        s.VisitedByPowers.Count > 0 ? s.VisitedByPowers.ToList() : null, // v44; omitted when no foreign power has visited
                        // v54; the muskets/horses the camp holds to arm its braves — omitted when it holds none, so an
                        // empty-stock settlement is byte-identical to v53.
                        s.MilitaryStock.Any()
                            ? s.MilitaryStock.ToDictionary(kv => kv.Key, kv => kv.Value)
                            : null,
                        // v55; per-player alarm (player id → alarm, including the human as channel 0) — omitted when the
                        // settlement is at peace with everyone, so a fully-calm camp is byte-identical to v54.
                        s.AlarmChannels.Any()
                            ? s.AlarmChannels.ToDictionary(kv => kv.Key, kv => kv.Value)
                            : null,
                        // v57; the general goods store a trader can buy from — omitted when the settlement holds none, so
                        // a no-general-stock settlement is byte-identical to v56.
                        s.GeneralStock.Any()
                            ? s.GeneralStock.ToDictionary(kv => kv.Key, kv => kv.Value)
                            : null))
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
            Regions?.Select(r => new Region(
                r.Id, (RegionType)r.Type, r.ScoreValue, r.Key, r.ParentId,
                r.DiscoveredBy, r.Name, r.DiscoveredInTurn)).ToList(), // v51; pre-v51 / omitted → undiscovered (null)
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
                u.Attrition ?? 0, // pre-v50 / no attrition → 0
                u.Nationality, u.Ethnicity, u.Name)), // pre-v52 / nation-less / un-christened → null
            Colonies?.Select(c =>
            {
                var colony = new CrownAndColony.GameLogic.Colonies.Colony(
                    c.Id, c.Name, new Position(c.X, c.Y), c.Population, c.OwnerId ?? 0)
                {
                    Government = ruleset.Difficulty.Government, // production-bonus thresholds from the difficulty level (re-derived, not persisted)
                    RebelLibertyDivisor = ruleset.ColonyConstants.LibertyPerRebel, // liberty-per-rebel from the ruleset (re-derived, not persisted; 86d3drpgg)
                    ExportRetainDefault = ruleset.ColonyConstants.DefaultExportLevel, // custom-house default retain from the ruleset (86d3drpgg)
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
                    if (export.ImportLevel is { } importLevel) // per-good import level (v67; null/pre-v67 → leave unset)
                    {
                        colony.SetImport(goods, importLevel);
                    }
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
                // Per-teacher-slot school training (v60). Pre-v60 saves carry a single counter per school in the legacy
                // SchoolTraining field — restored as the building's slot-0 (first teacher's) turns. Pre-v32 → none.
                foreach ((string buildingId, int turns) in c.SchoolTraining ?? new Dictionary<string, int>())
                {
                    colony.RestoreSchoolTraining(buildingId, turns); // v32..v59 single-counter → slot 0
                }
                foreach ((string buildingId, IReadOnlyList<int> slots) in
                         c.SchoolTrainingSlots ?? new Dictionary<string, IReadOnlyList<int>>())
                {
                    colony.RestoreSchoolTraining(buildingId, slots); // v60 per-teacher-slot turns
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
                    HasBeenVisited = s.HasBeenVisited,        // the human's first-contact flag (rides player 0)
                    SkillConsumed = s.SkillConsumed,
                    WantedGoods = s.WantedGoods ?? [],
                    MissionOwnerId = s.MissionOwnerId,           // v33; pre-v33 → null = no mission
                    MissionIsExpert = s.MissionIsExpert ?? false, // v33; default free-colonist missionary
                    ConvertProgress = s.ConvertProgress ?? 0,    // v34; pre-v34 → 0
                };
                // v55: restore per-player alarm. A v55 save carries AlarmChannels (player id → alarm, the human as
                // channel 0). A pre-v55 save (or a v55 save written before this slice) carries only the single Alarm
                // scalar, which was effectively the human's alarm — migrate it into channel 0.
                if (s.AlarmChannels is { } channels)
                {
                    foreach ((int playerId, int alarm) in channels)
                    {
                        settlement.SetAlarm(playerId, alarm);
                    }
                }
                else if (s.Alarm != 0)
                {
                    settlement.SetAlarm(0, s.Alarm); // pre-v55 single scalar → human channel (MostHated is rebuilt by the first alarm change / decay)
                }
                foreach (int powerId in s.VisitedByPowers ?? []) // v44; pre-v44 → no foreign power has visited
                {
                    settlement.MarkVisitedBy(powerId);
                }
                foreach ((string goodsId, int amount) in // v54; pre-v54 / omitted → no stock
                    s.MilitaryStock ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>())
                {
                    settlement.AddStock(goodsId, amount);
                }
                foreach ((string goodsId, int amount) in // v57; pre-v57 / omitted → no general stock (untradeable until visited again)
                    s.GeneralStock ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>())
                {
                    settlement.AddGoods(goodsId, amount);
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
        if (VictoryConditionsDisabled == true) // v62; pre-v62 / omitted → the win is still enabled
        {
            game.SetVictoryConditionsDisabled(true); // re-disables the victory conditions exactly as ContinuePlaying did
        }
        // The REF reinforcement + morale state (v62; pre-v62 / omitted → wave timer 0, peak 0 — a reloaded war resumes
        // under the v62 rules: the REF comes ashore in staggered waves from the next REF turn, and with peak 0 it never
        // breaks on morale, only on the raw unit thresholds).
        if (RefWaveCountdown is not null || RefMoralePeak is not null)
        {
            game.SetRefReinforcementState(RefWaveCountdown ?? 0, RefMoralePeak ?? 0);
        }
        if (PendingMonarchDemand is { } md) // v46; pre-v46 / omitted → no demand was pending
        {
            game.RestorePendingMonarchDemand(md.ToDemand());
        }
        if (NextUnitId is { } nextUnitId) // v54; pre-v54 / omitted → the counter stays re-derived from the units above
        {
            game.RestoreNextUnitId(nextUnitId);
        }
        if (History is { } history) // v58; pre-v58 / omitted → the log stays empty (it accrues again as play continues)
        {
            game.RestoreHistory(history.Select(h =>
                new HistoryEvent((HistoryEventKind)h.Kind, h.Turn, h.Description, h.Score)));
        }
        if (Demographics is { } demographics) // v65; pre-v65 / omitted → the series stays empty (it accrues again as play continues)
        {
            game.RestoreDemographics(demographics.Select(d =>
                new DemographicSnapshot(d.Year, d.Population, d.Gold, d.Score)));
        }
        game.RestoreNewWorldName(NewWorldName); // v66; pre-v66 / omitted (null) → the world stays unnamed (the prompt fires on the next first landfall)
        if (CitiesOfCibolaRemaining is { } cibolaLeft) // v63; pre-v63 / omitted → the full count of seven remains (the classic count)
        {
            game.SetCitiesOfCibolaRemaining(cibolaLeft);
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
        p.LastTaxRaiseTurn,
        p.DeclaredIndependenceTurn, p.InterventionBells ?? 0,
        p.TradeRoutes?.Select(r => new TradeRoute(r.Id, r.Name,
            r.Stops.Select(stop => new TradeRouteStop(stop.ColonyId, stop.Load ?? [])).ToList())).ToList(),
        p.NextTradeRouteId,
        p.PeaceTurns, // v53; pre-v53 / omitted → no recorded peace
        // v56; per-good trade accounting → the live TradeAccount triples. Pre-v56 / omitted → no counters (all 0).
        p.TradeAccounts?.ToDictionary(
            kv => kv.Key,
            kv => new TradeAccount(kv.Value.Sales, kv.Value.IncomeBeforeTaxes, kv.Value.IncomeAfterTaxes)));

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
            p.NextTradeRouteId == 1 ? null : p.NextTradeRouteId, // omit-when-default (1) → byte-identical to v44 until a route is made
            p.PeaceTurns.Count > 0 ? new Dictionary<int, int>(p.PeaceTurns) : null, // v53; omit-when-empty → byte-identical to v52 with no recorded peace
            // v56; per-good trade accounting for the Trade report — only goods actually traded (a non-zero counter) are
            // written, so a never-traded market omits the map and a default game stays byte-identical to v55.
            p.Market.SaveCounters() is { Count: > 0 } accounts
                ? accounts.ToDictionary(kv => kv.Key, kv => new SavedTradeAccount(
                    kv.Value.Sales, kv.Value.IncomeBeforeTaxes, kv.Value.IncomeAfterTaxes))
                : null,
            p.LastTaxRaiseTurn); // v60; omit-when-null → byte-identical to v59 until the King first raises tax
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
/// <param name="Exports">Custom-house export/import settings by good (only non-default goods; null/omitted when none; v28, additive — the per-good import level rides as a third field on <see cref="SavedExport"/>, v67).</param>
/// <param name="BuildingWorkerTypes">Per building, its NON-FREE occupant unit-type ids (v30; null/omitted when every building worker is a free colonist). The free occupants are implicit (count − non-free).</param>
/// <param name="IdleWorkerTypes">The colony's NON-FREE idle colonists' unit-type ids (v30; null/omitted when all idle are free colonists).</param>
/// <param name="SchoolTraining">Pre-v60 per-school single-counter training turns (v32..v59; one int per school). Read-only legacy field for loading older saves — restored as the building's slot-0 teacher turns; never written by v60+ (which uses <paramref name="SchoolTrainingSlots"/>).</param>
/// <param name="TeaPartyBellTurns">Turns remaining of the Boston-Tea-Party bell surge (v37; null/omitted when 0, so a no-party colony is byte-identical to v36).</param>
/// <param name="SchoolTrainingSlots">Per school building, the per-teacher-slot accrued training turns (v60; a college/university teaches one student per teacher in parallel, so each teacher carries its own counter). Null/omitted when no school is mid-training, so a non-teaching game is byte-identical to v59.</param>
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
    int? TeaPartyBellTurns = null,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? SchoolTrainingSlots = null);

/// <summary>A colony's custom-house export/import setting for one good (v28+; only non-default goods are stored).</summary>
/// <param name="Exported">Whether the good auto-exports.</param>
/// <param name="Level">The amount to retain before exporting the surplus.</param>
/// <param name="ImportLevel">
/// The amount to import to — an automatic delivery (a trade-route drop-off) will not stock the good past this (FreeCol
/// <c>ExportData.importLevel</c>; v67). <b>Null/omitted</b> when "not set" (the serializer's <c>WhenWritingNull</c> drops
/// it, mapping to <see cref="Colony.ImportLevelUnset"/> = −1 → the effective cap is the warehouse capacity), so a pre-v67
/// save (whose <see cref="SavedExport"/> rows lacked this field) loads with every good's import level unset, and a good
/// with only an export setting serialises <b>byte-identically to v66</b> (no extra field). A set import level writes the
/// non-negative ceiling.
/// </param>
public sealed record SavedExport(bool Exported, int Level, int? ImportLevel = null);

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

/// <summary>A map <see cref="Region"/> inside a <see cref="SaveGame"/> (v35; discovery fields v51).</summary>
/// <param name="Id">Region id (indexed by <see cref="SaveGame.RegionIds"/>).</param>
/// <param name="Type">The <see cref="RegionType"/> enum ordinal.</param>
/// <param name="ScoreValue">Discovery score.</param>
/// <param name="Key">Fixed-region key (e.g. <c>model.region.arctic</c>); null/omitted for a dynamic land/mountain region.</param>
/// <param name="ParentId">Parent region id (an ocean quadrant's parent ocean); null/omitted at the top level.</param>
/// <param name="DiscoveredBy">The player id that first discovered this region (v51; null/omitted while undiscovered, so an undiscovered map stays byte-identical to v50).</param>
/// <param name="Name">The name assigned on discovery (v51; null/omitted while undiscovered).</param>
/// <param name="DiscoveredInTurn">The turn the region was discovered in (v51; null/omitted while undiscovered).</param>
public sealed record SavedRegion(
    int Id, int Type, int ScoreValue, string? Key = null, int? ParentId = null,
    int? DiscoveredBy = null, string? Name = null, int? DiscoveredInTurn = null);

/// <summary>A serialised <see cref="GameSession.Force"/> (the Royal Expeditionary Force) inside a <see cref="SaveGame"/> (v40).</summary>
/// <param name="Land">Land-unit blocks.</param>
/// <param name="Naval">Naval-unit blocks.</param>
public sealed record SavedForce(IReadOnlyList<ForceEntry> Land, IReadOnlyList<ForceEntry> Naval);

/// <summary>
/// One <see cref="HistoryEvent"/> in the human's history log inside a <see cref="SaveGame"/> (v58). The whole log is
/// additive + <b>omitted when empty</b> (see <see cref="SaveGame.History"/>), so a game with no recorded events stays
/// byte-identical to v57; pre-v58 saves load with an empty log exactly as before persistence.
/// </summary>
/// <param name="Kind">The <see cref="HistoryEventKind"/> enum ordinal.</param>
/// <param name="Turn">The game turn the event happened on.</param>
/// <param name="Description">The player-facing one-line description (carries no ids — safe to round-trip verbatim).</param>
/// <param name="Score">The score the event contributed (region discovery positive; −5/−50 destruction penalties; 0 otherwise). Omitted when 0 so a score-less event (the common case) stays compact.</param>
public sealed record SavedHistoryEvent(int Kind, int Turn, string Description, int Score = 0);

/// <summary>
/// One year's <see cref="GameSession.DemographicSnapshot"/> in the human nation's demographic series inside a
/// <see cref="SaveGame"/> (v65). The whole series is additive + <b>omitted when empty</b> (see
/// <see cref="SaveGame.Demographics"/>), so a game whose first year has not yet rolled over stays byte-identical to v64;
/// pre-v65 saves load with an empty series exactly as before persistence.
/// </summary>
/// <param name="Year">The calendar year the snapshot was taken.</param>
/// <param name="Population">The human's total colonial population that year.</param>
/// <param name="Gold">The human's treasury gold that year.</param>
/// <param name="Score">The human's player score that year.</param>
public sealed record SavedDemographicSnapshot(int Year, int Population, int Gold, int Score);

/// <summary>
/// One per-turn player notice in the in-session message log inside a <see cref="SaveGame"/> (v59). The whole log is
/// additive + <b>omitted when empty</b> (see <see cref="SaveGame.MessageLog"/>), so a fresh game stays byte-identical
/// to v58; pre-v59 saves load with an empty log. The text is the already-formatted, player-facing string the
/// controller produced (ADR-006) — it carries no ids, so it round-trips verbatim.
/// </summary>
/// <param name="Turn">The game turn the notice was surfaced on.</param>
/// <param name="Category">The <see cref="App.MessageCategory"/> enum ordinal (for the log's category filter).</param>
/// <param name="Text">The already-formatted player-facing notice text.</param>
public sealed record SavedLogMessage(int Turn, int Category, string Text);

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
/// <param name="Alarm"><b>Legacy</b> single alarm scalar toward the human, 0–1000 (v16–v54). Retired at v55 in favour of <paramref name="AlarmChannels"/>; v55 saves write 0 (omitted) and load migrates a non-zero pre-v55 value into the human's channel (player id 0).</param>
/// <param name="HasBeenVisited">Whether the chief has been spoken with (v16+).</param>
/// <param name="SkillConsumed">Whether the skill has been taught/consumed (v16+).</param>
/// <param name="WantedGoods">Goods the settlement most wants to buy, most-wanted first (v17+).</param>
/// <param name="MissionOwnerId">Colonial player id whose missionary resides here (v33+; null = no mission, omitted).</param>
/// <param name="MissionIsExpert">Whether the resident missionary is a jesuit (v33+; null when no mission, omitted).</param>
/// <param name="ConvertProgress">Accrued convert progress under a mission (v34+; null/omitted when 0).</param>
/// <param name="VisitedByPowers">Non-human player ids that have spoken with the chief (v44+; null/omitted when none, so a game where only the human (or nobody) has visited is byte-identical to v43). The human rides <see cref="HasBeenVisited"/>.</param>
/// <param name="MilitaryStock">The muskets/horses the settlement holds to arm its braves (FreeCol <c>IndianSettlement</c>'s military-goods retention), as goods-id → amount (v54+; null/omitted when the settlement holds none, so an empty-stock settlement is byte-identical to v53). Seeded by <see cref="World.NativeSettlementGenerator"/>, spent by <see cref="GameSession.Game.TryEquipBrave"/>.</param>
/// <param name="AlarmChannels">Per-player alarm as player-id → alarm 0–1000 (v55+; FreeCol <c>IndianSettlement</c>'s <c>Map&lt;Player, Tension&gt;</c>). The human is player id 0. Null/omitted when the settlement is at peace with everyone, and any zero channel is dropped, so a fully-calm settlement is byte-identical to v54. A pre-v55 save has no map — its single <paramref name="Alarm"/> scalar migrates into channel 0 on load.</param>
/// <param name="GeneralStock">The general (non-military) goods store a trader can buy from and the basis of the stock-driven <paramref name="WantedGoods"/> (FreeCol <c>IndianSettlement</c>'s goods container), as goods-id → amount (v57+; null/omitted when the settlement holds no general stock, so a no-stock settlement is byte-identical to v56). Seeded by <see cref="World.NativeSettlementGenerator"/>, drained by <see cref="GameSession.Game.BuyFromNatives(Units.Unit, Natives.NativeSettlement, string, int)"/> and grown by selling to the settlement.</param>
public sealed record SavedNativeSettlement(
    int Id, string NationTypeId, string SettlementTypeId, bool IsCapital,
    int X, int Y, int Size, string? LearnableSkill = null,
    int Alarm = 0, bool HasBeenVisited = false, bool SkillConsumed = false,
    IReadOnlyList<string>? WantedGoods = null,
    int? MissionOwnerId = null, bool? MissionIsExpert = null,
    int? ConvertProgress = null,
    IReadOnlyList<int>? VisitedByPowers = null,
    IReadOnlyDictionary<string, int>? MilitaryStock = null,
    IReadOnlyDictionary<int, int>? AlarmChannels = null,
    IReadOnlyDictionary<string, int>? GeneralStock = null);

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
/// <param name="Nationality">The unit's origin nation id — its personal allegiance (FreeCol <c>Unit.nationality</c>; v52+). Omitted (null) when it equals the unit's owner-derived nation — the spawn default for every fresh unit — and re-derived from the owner on load; only a <em>diverged</em> origin (a captured colonist) writes an explicit value. So a default game serializes byte-identically to v51.</param>
/// <param name="Ethnicity">The unit's ethnicity nation id — the look of the people it was born among (FreeCol <c>Unit.ethnicity</c>; v52+). Inherited through capture (a former convert keeps a native look). Omitted/re-derived exactly as <paramref name="Nationality"/>.</param>
/// <param name="Name">The unit's individual custom name (FreeCol <c>Unit.name</c>, a <c>Nameable</c>; v52+). Null when un-christened (the common case); omitted so an unnamed unit serializes byte-identically to v51.</param>
public sealed record SavedUnit(
    int Id, string? TypeId, int X, int Y, int MovementLeft,
    int Location = 0, int SailTurns = 0, IReadOnlyDictionary<string, int>? Cargo = null,
    int? CarrierId = null,
    string? Owner = null, string? Role = null, int? RoleCount = null,
    int? OwnerId = null, int? RepairTurns = null, int? Orders = null, int? TreasureAmount = null,
    int? DestX = null, int? DestY = null,
    int? TradeRouteId = null, int? TradeRouteStop = null,
    string? WorkImprovement = null, int? WorkTurnsLeft = null,
    int? Attrition = null,
    string? Nationality = null, string? Ethnicity = null, string? Name = null);

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
/// <param name="PeaceTurns">The turn this player's peace took force with each other player, by their id (v53 additive, FreeCol <c>EuropeanAIPlayer.peaceHolds</c>' <c>peaceTurn</c>; null/omitted when it has no recorded peace — the common case and every player in a no-contact game, so a default game stays byte-identical to v52). Feeds the decaying peace-hold (<c>(PEACE_PROBABILITY/100)^(turn − peaceTurn)</c>); pre-v53 saves load with no stamps (the gate is inert without Franklin anyway).</param>
/// <param name="TradeAccounts">This player's cumulative per-good trade accounting for the Trade report (v56 additive; FreeCol <c>MarketData</c>'s <c>sales</c>/<c>incomeBeforeTaxes</c>/<c>incomeAfterTaxes</c>), as goods-id → net <see cref="SavedTradeAccount"/>. Null/omitted when the player has traded nothing (every counter still 0 — the common pre-trade case and a fresh game), so a never-traded market stays byte-identical to v55; only goods that have actually been traded are written. Pre-v56 saves load with all counters 0.</param>
/// <param name="LastTaxRaiseTurn">The turn the King last raised this player's tax (v60 additive; null/omitted when he never has, so a game with no tax raise yet stays byte-identical to v59). Enforces the inter-raise tax grace period (<see cref="Game.TaxRaiseGraceTurns"/>) deterministically across save/load. Pre-v60 saves load with null (no cooldown active).</param>
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
    int? NextTradeRouteId = null,
    IReadOnlyDictionary<int, int>? PeaceTurns = null,
    IReadOnlyDictionary<string, SavedTradeAccount>? TradeAccounts = null,
    int? LastTaxRaiseTurn = null);

/// <summary>
/// One good's cumulative trade accounting inside a <see cref="SavedPlayer"/> (v56; FreeCol <c>MarketData</c>'s
/// <c>sales</c>/<c>incomeBeforeTaxes</c>/<c>incomeAfterTaxes</c>). All three are <b>net</b> totals — a sell adds, a buy
/// subtracts. Only written for goods that have been traded; mirrors <see cref="Trade.TradeAccount"/>.
/// </summary>
/// <param name="Sales">Net units sold (sells +, buys −).</param>
/// <param name="IncomeBeforeTaxes">Net pre-tax income.</param>
/// <param name="IncomeAfterTaxes">Net after-tax income.</param>
public sealed record SavedTradeAccount(int Sales, int IncomeBeforeTaxes, int IncomeAfterTaxes);

/// <summary>A saved trade route (v43): a player's named ring of stops a carrier hauls along automatically. Omitted entirely when the player has none.</summary>
/// <param name="Id">Per-player route id.</param>
/// <param name="Name">Route name.</param>
/// <param name="Stops">The ordered stops.</param>
public sealed record SavedTradeRoute(int Id, string Name, IReadOnlyList<SavedTradeRouteStop> Stops);

/// <summary>One <see cref="SavedTradeRoute"/> stop (v43): a colony id and the goods to load there (null = load nothing — a pure delivery stop).</summary>
public sealed record SavedTradeRouteStop(int ColonyId, IReadOnlyList<string>? Load);
