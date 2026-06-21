using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The War of Independence (FreeCol <c>InGameController.csDeclareIndependence</c> + the REF): the rebel sentiment
/// gate, declaring independence (continental-army muster + losing Europe + the King's expeditionary force taking
/// the field), then — in later slices — the REF landing/combat and the win/lose resolution.
/// </summary>
public sealed partial class Game
{
    private const string VeteranSoldierUnitTypeId = "model.unit.veteranSoldier";
    private const string ColonialRegularUnitTypeId = "model.unit.colonialRegular"; // veterans muster into these
    private const int IndependenceSoLThreshold = 50;  // model.limit.independence.rebels (≥ 50% national SoL)

    /// <summary>
    /// A player's national Sons-of-Liberty percentage (FreeCol <c>Player.getSoL</c>): rebels across all its colonies
    /// as a percentage of total population. 0 with no colonists. Drives the Declaration-of-Independence gate.
    /// </summary>
    public int NationalSonsOfLiberty(Player player)
    {
        int population = ColoniesOf(player).Sum(c => c.Population);
        return population <= 0 ? 0 : ColoniesOf(player).Sum(c => c.RebelCount) * 100 / population;
    }

    /// <summary>
    /// Whether <paramref name="player"/> may declare independence now (FreeCol <c>model.event.declareIndependence</c>
    /// limits): still a colonial power, national Sons of Liberty ≥ 50%, at least one connected-port colony, and not
    /// past the ruleset's last colonial year (<see cref="Specification.Ruleset.LastColonialYear"/>; classic 1800).
    /// </summary>
    public MoveCheck CheckDeclareIndependence(Player player)
    {
        if (player.PlayerType != PlayerType.Colonial)
        {
            return MoveCheck.No("Independence can only be declared once, by a colonial power.");
        }
        if (NationalSonsOfLiberty(player) < IndependenceSoLThreshold)
        {
            return MoveCheck.No($"Rebel sentiment must reach {IndependenceSoLThreshold}% before independence can be declared.");
        }
        if (!ColoniesOf(player).Any(IsColonyCoastal))
        {
            return MoveCheck.No("Independence needs at least one colony with a connected port.");
        }
        if (CurrentYear > Ruleset.LastColonialYear)
        {
            return MoveCheck.No("It is too late in history to declare independence.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Declares independence (FreeCol <c>csDeclareIndependence</c>): the player becomes a <see cref="PlayerType.Rebel"/>,
    /// loses every unit in or sailing to Europe (and its recruiting), musters its veterans into colonial regulars, the
    /// King's Royal Expeditionary Force takes the field at war with the new nation, the natives who most resented the
    /// departing Crown swing behind the rebel, and the King offers a one-off war-mercenary (Hessian) force for hire.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDeclareIndependence"/>.</exception>
    public void DeclareIndependence(Player player)
    {
        MoveCheck check = CheckDeclareIndependence(player);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        player.PlayerType = PlayerType.Rebel;
        player.DeclaredIndependenceTurn = Turn;
        _units.RemoveAll(u => u.OwnerId == player.PlayerId && !u.IsNative && !u.IsOnMap); // units in/bound for Europe are forfeit
        player.RecruitDockList.Clear(); // Europe is closed to a rebel
        player.Market.Reinitialise();   // the new nation trades on a clean market — colonial boycotts/drift cleared (FreeCol reinitialiseMarket)

        MusterContinentalArmy(player);
        CreateRefPlayer(player);
        ShiftNativeStanceOnDeclaration(player); // the natives who hated the Crown most side with the rebel
        OfferWarMercenaries(player);            // the King dangles a Hessian force for hire (a pending offer, never auto-applied)
        RecordHistory(HistoryEventKind.DeclaredIndependence, "Declared independence from the Crown.");
    }

    /// <summary>
    /// The native realignment that follows a declaration of independence (FreeCol <c>csDeclareIndependence</c>'s
    /// native block): the most-hostile contacted native nation throws in with the new rebel and is <b>calmed</b>
    /// toward it. FreeCol shifts the friendliest such nation's tension toward the rebel down to <c>CONTENT</c> (from
    /// war) or <c>HAPPY</c> (from a cease-fire) and makes it hateful toward the freshly-arrived REF, while the
    /// <em>least</em>-hostile contacted nation turns on the rebel.
    /// <para><b>Faithful-subset deviation.</b> Our native model is single-player: a settlement tracks one
    /// <see cref="NativeSettlement.Alarm"/> figure toward the human, and there is <em>no</em> native↔REF relationship
    /// (the REF is a colonial-like player the natives never meet) and no second human to turn hostile. So we keep only
    /// the faithful, representable half: the nation the human-rebel has angered the most (highest alarm, among the
    /// nations whose chief the human has met) is <b>brought onside</b> — every one of its settlements is calmed to at
    /// most FreeCol's <c>CONTENT</c> band (it backs the rebellion against the departed Crown). The REF-hostility and
    /// the "least-hostile turns on you" halves have no analogue here and are omitted (documented in independence.md).
    /// RNG-free; touches only native alarm, which is not persisted at nation scope beyond the per-settlement field.</para>
    /// </summary>
    private void ShiftNativeStanceOnDeclaration(Player rebel)
    {
        // Among nations the human-rebel has actually contacted (spoken with a chief, FreeCol hasContacted), pick the
        // one whose settlements are angriest at the rebel (highest peak alarm) — FreeCol's "most hostile" ally pick.
        string? ally = _nativeSettlements
            .Where(s => s.HasBeenVisitedBy(rebel.PlayerId))
            .GroupBy(s => s.NationTypeId)
            .OrderByDescending(g => g.Max(s => s.Alarm))
            .ThenBy(g => g.Key) // deterministic tie-break
            .Select(g => g.Key)
            .FirstOrDefault();
        if (ally is null)
        {
            return; // the rebel has met no natives — nobody to swing behind it
        }

        // The ally is calmed to at most the CONTENT band: it stops resenting the rebel now the Crown it really hated
        // has gone (FreeCol sets the tension into the CONTENT/HAPPY range). Settlements calmer than CONTENT are left.
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == ally && s.Alarm > NativeAllyCalmedAlarm))
        {
            ChangeNativeAlarm(settlement, NativeAllyCalmedAlarm - settlement.Alarm); // down to the CONTENT limit
        }
    }

    /// <summary>The alarm a native nation that backs the rebellion is calmed <em>to</em> (FreeCol <c>Tension.Level.CONTENT.getLimit()</c> = 600, the band an ally settles into when the war-time Crown departs).</summary>
    private const int NativeAllyCalmedAlarm = NativeSettlement.AlarmContentMax;

    /// <summary>
    /// The King's parting offer of a war-mercenary (Hessian) force on the very turn of the declaration (FreeCol
    /// <c>csDeclareIndependence</c>'s <c>loadMercenaryForce</c> + <c>csMercenaries(HESSIAN_MERCENARIES)</c>): a one-off
    /// professional force the new nation may hire for gold to face the REF. Surfaced as a
    /// <see cref="PendingMonarchDemand"/> (the same accept/decline seam as the King's in-game mercenary offers), built
    /// from the in-game <see cref="LoadMercenaries"/> generator and applied only on accept via
    /// <see cref="RespondToMonarch"/> — never auto-applied, so the rebel's stream 0 stays byte-identical until the
    /// player chooses (ADR-009). An offer is made only when one affordable to the rebel can be built; otherwise none.
    /// <para><b>Faithful-subset note.</b> FreeCol draws the Hessian force from the Monarch's pre-built
    /// <c>mercenaryForce</c> (a fixed ruleset force, priced by hire price). We reuse our existing affordability-trimmed
    /// mercenary generator (veteran soldiers, armed or mounted) so the offer is consistent with the King's other
    /// mercenary offers and never exceeds the treasury — the same documented simplification the in-game offer makes.
    /// The offer rides the monarch RNG (an ephemeral stream off the rebel's current state), never stream 0.</para>
    /// </summary>
    private void OfferWarMercenaries(Player rebel)
    {
        if (!rebel.IsHuman)
        {
            return; // the offer is for the human rebel (the pending-offer seam is the human's UI); an AI rebel auto-fights
        }

        // The offer draws on the monarch's ephemeral stream (off the human's live state + turn), never stream 0 — the
        // same isolation the monarch tick uses (a human rebel's economy stream stays byte-identical until it answers).
        RandomState humanState = _random.SaveState();
        var rng = new Pcg32Random(humanState.State + (ulong)Turn, MonarchStreamId);
        if (LoadMercenaries(rng) is { } offer) // an affordability-trimmed force, or none
        {
            _pendingMonarchDemand = new PendingMonarchDemand(
                MonarchAction.HessianMercenaries, Offer: offer.Force, Price: offer.Price);
        }
    }

    /// <summary>
    /// Upgrades the rebel's veteran soldiers to colonial regulars, colony by colony (FreeCol continental-army muster,
    /// <c>csDeclareIndependence</c>). For every colony with SoL &gt; 50 the cap is <c>(unitCount + 2) · (SoL − 50) / 100</c>,
    /// counting <em>that colony's own</em> units, and up to that many of <em>that colony's own</em> veteran soldiers
    /// (earliest first by id) rise to colonial regulars. Colonies are processed in id order for determinism.
    /// <para><b>Faithful-subset deviation:</b> FreeCol's per-colony unit list (<c>Settlement.getAllUnitsList</c>) is the
    /// colony's worker units <em>plus</em> the units garrisoning its tile. In our model a colony's workers are a
    /// <see cref="Colony.Population"/> count, not <see cref="Unit"/> objects, so (a) the cap term counts the colony's
    /// population (its workers) plus the non-native units standing on its tile (the garrison) — faithfully reproducing
    /// <c>allUnits.size()</c> — but (b) the veterans actually drawn can only come from the <em>garrison on the colony's
    /// tile</em>, never from worker colonists (which aren't units that can be upgraded). A veteran soldier serving as a
    /// colony worker therefore isn't mustered until it is taken out as a unit; otherwise this matches FreeCol per colony.</para>
    /// </summary>
    private void MusterContinentalArmy(Player player)
    {
        foreach (Colony colony in ColoniesOf(player).Where(c => c.SonsOfLiberty > 50).OrderBy(c => c.Id).ToList())
        {
            // FreeCol allUnits.size() = getUnitList() (worker units) + getTile().getUnitList() (units on the tile).
            // Our workers are a Population count (not units); the garrison is the on-map units standing on the tile.
            int garrison = _units.Count(u => u.OwnerId == player.PlayerId && !u.IsNative && u.IsOnMap && u.Position == colony.Position);
            int unitCount = colony.Population + garrison;
            int limit = (unitCount + 2) * (colony.SonsOfLiberty - 50) / 100;
            if (limit <= 0)
            {
                continue;
            }
            // Draw the veterans from this colony's own garrison (earliest first by id), upgrading up to the cap.
            foreach (Unit veteran in _units
                .Where(u => u.OwnerId == player.PlayerId && u.IsOnMap && u.Position == colony.Position
                    && u.Type.Id == VeteranSoldierUnitTypeId)
                .OrderBy(u => u.Id)
                .Take(limit)
                .ToList())
            {
                UpgradeUnitType(veteran, ColonialRegularUnitTypeId);
            }
        }
    }

    /// <summary>
    /// Brings the Royal Expeditionary Force into play as a new <see cref="PlayerType.RoyalExpeditionaryForce"/> AI
    /// player at war with the rebel, realising the King's amassed <see cref="Force"/> into real units mustering in
    /// Europe (they sail and land in the next slice). The REF draws from its own RNG stream (ADR-009).
    /// </summary>
    private void CreateRefPlayer(Player rebel)
    {
        Force force = EnsureRefForce();
        int refId = _players.Max(p => p.PlayerId) + 1;
        var refPlayer = new Player(refId, null, isHuman: false, PlayerType.RoyalExpeditionaryForce, new Market(Ruleset));
        // Seeded deterministically off the human's current state (read non-destructively) — stream 0 untouched; the
        // REF stream's state is persisted thereafter so it resumes across save/load like any foreign power.
        refPlayer.Rng = new Pcg32Random(_random.SaveState().State, refPlayer.RngStreamId);
        _players.Add(refPlayer);

        refPlayer.StanceMap[rebel.PlayerId] = Stance.War;
        rebel.StanceMap[refPlayer.PlayerId] = Stance.War;

        foreach (ForceEntry entry in force.LandUnits.Concat(force.NavalUnits))
        {
            for (int i = 0; i < entry.Count; i++)
            {
                SpawnInEurope(entry.UnitTypeId, entry.RoleId, refId);
            }
        }
    }

    /// <summary>
    /// The Royal Expeditionary Force's turn (item 8): bring any units still mustering in Europe ashore near a rebel
    /// port, then prosecute the war. The REF is at war with the rebel (who is the human), so it reuses the
    /// foreign-power war AI — hunting and assaulting the rebel's units and colonies — drawing entirely from its OWN
    /// RNG stream (<see cref="RandomFor"/>), never the human's stream 0 (ADR-009).
    /// </summary>
    private void RunRefTurn(Player refPlayer)
    {
        LandRefUnits(refPlayer);
        RunForeignPowerTurn(refPlayer); // the rebel == the human, so the at-war hunt/assault logic targets it
    }

    /// <summary>
    /// Brings the REF's in-Europe units ashore. The King's fleet makes landfall at its fixed entry tile (chosen near
    /// the human's start at game creation, FreeCol <c>Player.entryTile</c>): each unit takes the nearest empty tile
    /// (land for land units, water for ships) around that beachhead, falling back to the ring around a rebel's
    /// connected-port colonies when the entry tile is unset (pre-v47 save) or its surroundings are full.
    /// </summary>
    private void LandRefUnits(Player refPlayer)
    {
        var targets = _players
            .Where(p => StanceBetween(refPlayer.PlayerId, p.PlayerId) == Stance.War)
            .SelectMany(ColoniesOf)
            .Where(IsColonyCoastal)
            .OrderBy(c => c.Id)
            .ToList();
        if (targets.Count == 0 && _refEntryTile is null)
        {
            return; // no rebel port to invade and no fixed beachhead
        }

        foreach (Unit unit in _units
            .Where(u => IsOwnedBy(u, refPlayer) && u.Location == UnitLocation.InEurope)
            .OrderBy(u => u.Id)
            .ToList())
        {
            // Land at the fixed entry-tile beachhead first (the King's fleet arrives at the human's coast), then fall
            // back to the rebel colonies' surroundings if that beachhead is full this turn.
            Position? spot = (_refEntryTile is { } entry ? FindLandingTileNear(entry, unit.Type.IsNaval) : null)
                ?? FindLandingTile(targets, unit.Type.IsNaval);
            if (spot is { } s)
            {
                unit.Position = s;
                unit.Location = UnitLocation.OnMap;
                RevealForOwner(unit);
            }
            // Units that don't fit this turn wait in Europe and land next turn.
        }
    }

    /// <summary>The nearest empty landing tile (water for ships, land for land units) to <paramref name="centre"/>, scanning outward in rings up to radius 4. Deterministic; null if none free.</summary>
    private Position? FindLandingTileNear(Position centre, bool naval)
    {
        for (int radius = 0; radius <= 4; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                    {
                        continue; // only the ring at this radius
                    }
                    var p = new Position(centre.X + dx, centre.Y + dy);
                    if (!Map.InBounds(p) || Map.TerrainAt(p).IsWater != naval)
                    {
                        continue;
                    }
                    if (ColonyAt(p) is not null || NativeSettlementAt(p) is not null || _units.Any(u => u.IsOnMap && u.Position == p))
                    {
                        continue; // occupied
                    }
                    return p;
                }
            }
        }
        return null;
    }

    // Victory thresholds (FreeCol ServerPlayer.checkForREFDefeat) + the Spanish Succession.
    private const double RefDefeatPowerRatio = 1.5;   // the rebel must hold 1.5× the REF's land power
    private const int RefDefeatLandThreshold = 7;     // …and the REF be reduced below 7 land
    private const int RefDefeatNavalThreshold = 2;    //    or below 2 naval units
    private const int SpanishSuccessionYear = 1600;

    private bool _spanishSuccessionDone;

    /// <summary>Whether the Spanish Succession consolidation has already happened (save state).</summary>
    internal bool SpanishSuccessionDone => _spanishSuccessionDone;

    /// <summary>Installs the restored Spanish-Succession flag (save load).</summary>
    internal void SetSpanishSuccessionDone(bool done) => _spanishSuccessionDone = done;

    /// <summary>
    /// The player that has won the game (FreeCol <c>ServerGame.checkForWinner</c>), or null while it continues — a
    /// <b>pure read</b> over the live players, evaluated against the ruleset's enabled victory conditions (each a
    /// parsed-or-defaulted boolean, so the classic defaults leave the default game's outcome unchanged):
    /// <list type="bullet">
    /// <item><b>Defeat the REF</b> (<see cref="Specification.Ruleset.VictoryDefeatRef"/>, classic on): the first
    /// nation to secure its <see cref="PlayerType.Independent"/>ence wins.</item>
    /// <item><b>Defeat all Europeans</b> (<see cref="Specification.Ruleset.VictoryDefeatEuropeans"/>, classic on):
    /// when only one non-REF European power is still alive, it wins.</item>
    /// <item><b>Defeat all humans</b> (<see cref="Specification.Ruleset.VictoryDefeatHumans"/>, classic off): when
    /// only one non-AI European power is still alive, it wins.</item>
    /// </list>
    /// Conditions are checked in FreeCol's order (REF, then Europeans, then Humans); the first to fire names the
    /// winner. Like all the independence reads this changes nothing and draws no RNG — <see cref="EndTurn"/> never
    /// short-circuits on it (ADR-009 byte-stability); the presentation reads it for the victory screen.
    /// </summary>
    public Player? Winner
    {
        get
        {
            if (Ruleset.VictoryDefeatRef
                && _players.FirstOrDefault(p => p.PlayerType == PlayerType.Independent) is { } independent)
            {
                return independent;
            }
            if (Ruleset.VictoryDefeatEuropeans)
            {
                List<Player> survivors = LiveEuropeanPowers().ToList();
                if (survivors.Count == 1)
                {
                    return survivors[0];
                }
            }
            if (Ruleset.VictoryDefeatHumans)
            {
                List<Player> survivors = LiveEuropeanPowers().Where(p => p.IsHuman).ToList();
                if (survivors.Count == 1)
                {
                    return survivors[0];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The live European powers (FreeCol <c>getLiveEuropeanPlayers</c> minus the REF): every colonial, rebel or
    /// independent player that has <b>not</b> been wiped out — it still holds at least one colony or one non-native
    /// unit. The Royal Expeditionary Force and the native nations are excluded. Pure; the victory reads count these.
    /// </summary>
    private IEnumerable<Player> LiveEuropeanPowers() =>
        _players.Where(p =>
            p.PlayerType is PlayerType.Colonial or PlayerType.Rebel or PlayerType.Independent
            && (ColoniesOf(p).Any() || _units.Any(u => u.OwnerId == p.PlayerId && !u.IsNative)));

    /// <summary>
    /// The combined attack power of a player's land units (the War-of-Independence strength yardstick). Counts every
    /// owned land unit regardless of location — the REF's un-landed reinforcements still count toward its strength.
    /// </summary>
    internal double LandPowerOf(Player player) =>
        _units.Where(u => u.OwnerId == player.PlayerId && !u.IsNative && !u.Type.IsNaval).Sum(OffenceBase);

    /// <summary>
    /// Whether the rebel has broken the Royal Expeditionary Force (FreeCol <c>checkForREFDefeat</c>): the REF holds no
    /// settlements, the rebel's land power is at least 1.5× the REF's, and the REF is reduced below 7 land or 2 naval
    /// units (counting the whole force, including reinforcements still mustering — so the win can't fire before it lands).
    /// </summary>
    internal bool CheckForRefDefeat(Player refPlayer, Player rebel)
    {
        if (ColoniesOf(refPlayer).Any())
        {
            return false; // the REF still holds captured colonies — the rebellion isn't won
        }
        int land = _units.Count(u => u.OwnerId == refPlayer.PlayerId && !u.Type.IsNaval);
        int naval = _units.Count(u => u.OwnerId == refPlayer.PlayerId && u.Type.IsNaval);
        if (land >= RefDefeatLandThreshold && naval >= RefDefeatNavalThreshold)
        {
            return false; // the REF is still a credible force
        }
        return LandPowerOf(rebel) >= RefDefeatPowerRatio * LandPowerOf(refPlayer);
    }

    /// <summary>
    /// Grants independence (FreeCol <c>csGiveIndependence</c>): peace with the King, the rebel becomes
    /// <see cref="PlayerType.Independent"/> with no tax, surviving REF land units surrender to the new nation, and the
    /// rest of the expeditionary force withdraws. The first independent nation has won (<see cref="Winner"/>).
    /// </summary>
    internal void GiveIndependence(Player refPlayer, Player rebel)
    {
        // Set the peace directly — SetStance is a no-op between non-Colonial players (the REF/rebel pair), the same
        // reason CreateRefPlayer wrote the war stance directly.
        refPlayer.StanceMap[rebel.PlayerId] = Stance.Peace;
        rebel.StanceMap[refPlayer.PlayerId] = Stance.Peace;
        rebel.PlayerType = PlayerType.Independent;
        rebel.TaxRate = 0;

        foreach (Unit unit in _units.Where(u => u.OwnerId == refPlayer.PlayerId).ToList())
        {
            if (unit.IsOnMap && !unit.Type.IsNaval)
            {
                unit.OwnerId = rebel.PlayerId; // the redcoats lay down their arms for the victors
                RevealForOwner(unit);
            }
            else
            {
                _units.Remove(unit); // the fleet and any in Europe sail home
            }
        }
    }

    /// <summary>The number of a player's colonies with a connected port (FreeCol <c>Player.getNumberOfPorts</c>).</summary>
    public int GetNumberOfPorts(Player player) => ColoniesOf(player).Count(IsColonyCoastal);

    /// <summary>
    /// Whether a rebel/independent nation has been crushed (FreeCol <c>checkForDeath</c> REBEL/INDEPENDENT): once it
    /// has declared, holding <b>no connected port</b> means it has lost the War of Independence. Derived and
    /// recomputed on demand — never saved; a plain colonial power is never "rebel-defeated" this way. The presentation
    /// reads this for the defeat screen; <see cref="EndTurn"/> does NOT short-circuit on it (ADR-009 byte-stability).
    /// </summary>
    public bool IsRebelDefeated(Player player) =>
        player.PlayerType is PlayerType.Rebel or PlayerType.Independent && GetNumberOfPorts(player) == 0;

    // ── Foreign Intervention Force (FreeCol ServerPlayer intervention handling + Monarch.getInterventionForce) ──

    /// <summary>RNG stream reserved for the foreign Intervention Force's landfall draws (which rebel port, which beach
    /// tile) — a high id like <see cref="LcrStreamId"/>/<see cref="ResourceQuantityStreamId"/> so the friendly ally's
    /// arrival never correlates with, or shifts, the human's economy stream 0 (ADR-009). The default game never reaches
    /// the war, so this stream is never drawn there — the default game stays byte-identical.</summary>
    private const ulong InterventionStreamId = 103;

    /// <summary>Per-rebel snapshot of <see cref="Player.Liberty"/> as the intervention accrual last saw it, so each
    /// turn's net liberty gain can be banked toward <see cref="Player.InterventionBells"/> (FreeCol accrues the same
    /// figure in <c>Player.modifyLiberty</c>). Transient: reseeded from the rebel's current liberty on first sight
    /// (e.g. after a save/load), which can drop a single straddling turn's bells — negligible against the threshold.</summary>
    private readonly Dictionary<int, int> _interventionLibertySnapshot = [];

    /// <summary>
    /// Banks a rebel's net liberty this turn toward its Foreign Intervention Force, and — once the accrued total
    /// reaches the ruleset's <see cref="Specification.Ruleset.InterventionBells"/> threshold (classic medium 5000) —
    /// lands a friendly foreign power's <see cref="Specification.Ruleset.InterventionForce"/> near one of the rebel's
    /// ports and resets the counter. Faithful to FreeCol <c>ServerPlayer.csNewTurn</c> (the <c>isRebel()</c> branch):
    /// the rebel accrues the liberty it generates each turn, and the ally's troops join the rebel's own army.
    /// <para>The landfall's only random choices (which port, which beach tile) draw from the dedicated
    /// <see cref="InterventionStreamId"/> stream, never the human's stream 0 (ADR-009) — so even a human rebel's game
    /// is not perturbed off-band.</para>
    /// </summary>
    private void AccrueInterventionBells(Player rebel)
    {
        // The liberty banked this turn = the rise in the rebel's spendable liberty since we last looked (FreeCol's
        // modifyLiberty amount). On first sight (fresh declaration or a reload) the snapshot seeds to the current
        // value, so nothing pre-rebellion is counted. A father election can drop liberty between snapshots; we clamp
        // the gain at 0 (that one turn's contribution is lost — a tiny, documented deviation).
        int previous = _interventionLibertySnapshot.TryGetValue(rebel.PlayerId, out int seen) ? seen : rebel.Liberty;
        int gain = Math.Max(0, rebel.Liberty - previous);
        _interventionLibertySnapshot[rebel.PlayerId] = rebel.Liberty;
        rebel.InterventionBells += gain;

        if (rebel.InterventionBells < Ruleset.InterventionBells)
        {
            return; // not yet — the foreign power is still weighing whether to commit
        }
        rebel.InterventionBells = 0; // the ally has committed; the count restarts (FreeCol can repeat on interventionTurns)
        SpawnInterventionForce(rebel);
    }

    /// <summary>
    /// Lands the foreign Intervention Force to aid <paramref name="rebel"/>: a friendly power's
    /// <see cref="Specification.Ruleset.InterventionForce"/> (classic medium: 2 colonial-regular soldiers + 2 dragoons
    /// + 2 artillery + 2 men-o-war) arrives off one of the rebel's connected ports, owned by — and fighting for — the
    /// rebel (FreeCol <c>createUnits(ivf…, entry, …)</c> on the rebel itself). The men-o-war drop onto water near the
    /// port and <b>carry the land units as passengers</b> (FreeCol <c>loadShips</c>): the ally fleet arrives offshore
    /// with the troops aboard, and the player disembarks them where they choose. This is what lets the ally reach a
    /// <em>besieged</em> port (its whole purpose) — it needs only water for the ships, not an open ring of beach tiles.
    /// Land units that don't fit in the fleet's holds are left behind (FreeCol disposes the left-overs).
    /// <para>The port and landing tiles are chosen on the <see cref="InterventionStreamId"/> stream (never the human's
    /// stream 0, ADR-009). No port, or no water for even one man-o-war → no landing (the rebel has lost its coast).</para>
    /// </summary>
    private void SpawnInterventionForce(Player rebel)
    {
        var ports = ColoniesOf(rebel).Where(IsColonyCoastal).OrderBy(c => c.Id).ToList();
        if (ports.Count == 0)
        {
            return; // nowhere to land — the rebel holds no connected port
        }

        // Deterministic ally stream, seeded off the rebel's own stream state + the turn (never stream 0). For a human
        // rebel RandomFor returns stream 0, so we seed off the REF's persisted stream instead when present, else the
        // rebel's — either way the seed is reproducible and the draws come from InterventionStreamId, not stream 0.
        Player? refPlayer = _players.FirstOrDefault(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        ulong seed = (refPlayer?.Rng ?? rebel.Rng)?.SaveState().State ?? _random.SaveState().State;
        var rng = new Pcg32Random(seed + (ulong)Turn, InterventionStreamId);

        Colony port = ports[rng.Next(ports.Count)];

        // Pass 1: the men-o-war make landfall on the water around the port (each takes the next free sea tile).
        var fleet = new List<Unit>();
        foreach (InterventionForceUnit block in Ruleset.InterventionForce.Units.Where(b => Ruleset.Unit(b.UnitTypeId).IsNaval))
        {
            for (int i = 0; i < block.Count; i++)
            {
                if (FindLandingTileNear(port.Position, naval: true) is not { } spot)
                {
                    continue; // no sea room this turn — that warship can't arrive
                }
                var ship = new Unit(_nextUnitId++, Ruleset.Unit(block.UnitTypeId), spot)
                {
                    Location = UnitLocation.OnMap,
                    OwnerId = rebel.PlayerId,
                    RoleId = block.RoleId ?? RoleType.DefaultRoleId,
                };
                _units.Add(ship);
                RevealForOwner(ship);
                fleet.Add(ship);
            }
        }
        if (fleet.Count == 0)
        {
            return; // the ally couldn't even bring a ship in — the land troops have no transport, nothing lands
        }

        // Pass 2: the land units board the fleet as passengers (FreeCol loadShips), spread across the ships by free
        // hold space. Any that don't fit are left behind (disposed) rather than scattered ashore.
        int shipIndex = 0;
        foreach (InterventionForceUnit block in Ruleset.InterventionForce.Units.Where(b => !Ruleset.Unit(b.UnitTypeId).IsNaval))
        {
            UnitType type = Ruleset.Unit(block.UnitTypeId);
            for (int i = 0; i < block.Count; i++)
            {
                Unit? carrier = null;
                for (int probe = 0; probe < fleet.Count; probe++)
                {
                    Unit candidate = fleet[(shipIndex + probe) % fleet.Count];
                    if (CargoSlotsFree(candidate) >= type.CarrySlots)
                    {
                        carrier = candidate;
                        shipIndex = (shipIndex + probe + 1) % fleet.Count; // round-robin so the troops spread across the fleet
                        break;
                    }
                }
                if (carrier is null)
                {
                    continue; // the holds are full — this regiment is left behind (FreeCol disposes the left-over)
                }
                var unit = new Unit(_nextUnitId++, type, carrier.Position)
                {
                    Location = carrier.Location,
                    Position = carrier.Position,
                    CarrierId = carrier.Id,
                    OwnerId = rebel.PlayerId,
                    RoleId = block.RoleId ?? RoleType.DefaultRoleId,
                };
                _units.Add(unit);
            }
        }
    }

    /// <summary>Per-turn War-of-Independence resolution: each rebel accrues its intervention bells (landing a foreign
    /// ally at the threshold), then a rebel that has broken the REF wins its independence. Runs in <see cref="EndTurn"/>
    /// before the REF's own turn; <c>internal</c> so tests can exercise the intervention landing in isolation, before
    /// the King's army lands and assaults the freshly-arrived ally fleet.</summary>
    internal void ResolveWarOfIndependence()
    {
        Player? refPlayer = _players.FirstOrDefault(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        if (refPlayer is null)
        {
            return; // no war under way
        }
        foreach (Player rebel in _players.Where(p => p.PlayerType == PlayerType.Rebel).ToList())
        {
            AccrueInterventionBells(rebel); // hold out long enough and a foreign ally sends troops
            if (CheckForRefDefeat(refPlayer, rebel))
            {
                GiveIndependence(refPlayer, rebel);
            }
        }
    }

    /// <summary>
    /// The Spanish Succession (FreeCol <c>ServerGame</c>): once, from 1600, a fading European AI (SoL &lt; 50) is
    /// absorbed by the dominant one (SoL &gt; 50) — its colonies and units change hands. RNG-free; draws nothing before
    /// the trigger year.
    /// </summary>
    private void RunSpanishSuccession()
    {
        if (_spanishSuccessionDone || CurrentYear < SpanishSuccessionYear)
        {
            return;
        }
        var powers = _players
            .Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial && ColoniesOf(p).Any())
            .OrderBy(p => p.PlayerId)
            .ToList();
        if (powers.Count < 2)
        {
            return;
        }
        Player weakest = powers.OrderBy(NationalSonsOfLiberty).ThenBy(p => p.PlayerId).First();
        Player strongest = powers.OrderByDescending(NationalSonsOfLiberty).ThenBy(p => p.PlayerId).First();
        if (weakest.PlayerId == strongest.PlayerId
            || NationalSonsOfLiberty(weakest) >= 50 || NationalSonsOfLiberty(strongest) <= 50)
        {
            return; // no clear fading-vs-dominant pair yet — try again next turn
        }

        foreach (Colony colony in ColoniesOf(weakest).ToList())
        {
            colony.OwnerId = strongest.PlayerId;
        }
        foreach (Unit unit in _units.Where(u => u.OwnerId == weakest.PlayerId && !u.IsNative).ToList())
        {
            unit.OwnerId = strongest.PlayerId;
        }
        _spanishSuccessionDone = true;
    }

    /// <summary>The first empty tile (in expanding rings around the targets) matching the unit's domain, or null if none within reach.</summary>
    private Position? FindLandingTile(IReadOnlyList<Colony> targets, bool naval)
    {
        foreach (Colony colony in targets)
        {
            for (int radius = 1; radius <= 4; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        {
                            continue; // only the ring at this radius
                        }
                        var p = new Position(colony.Position.X + dx, colony.Position.Y + dy);
                        if (!Map.InBounds(p) || Map.TerrainAt(p).IsWater != naval)
                        {
                            continue;
                        }
                        if (ColonyAt(p) is not null || NativeSettlementAt(p) is not null || _units.Any(u => u.IsOnMap && u.Position == p))
                        {
                            continue; // occupied
                        }
                        return p;
                    }
                }
            }
        }
        return null;
    }
}
