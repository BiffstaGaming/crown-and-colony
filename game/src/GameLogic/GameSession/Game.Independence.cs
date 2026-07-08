using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.Natives;
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
    /// A player's national Sons-of-Liberty percentage (FreeCol <c>Player.getSoL</c>, Player.java:1409-1413): the
    /// <b>unweighted average of each colony's own Sons-of-Liberty percentage</b> (<see cref="Colony.SonsOfLiberty"/>),
    /// with Java integer (floor) division; 0 with no colonies. <b>Not</b> population-weighted — a pop-10 colony at
    /// 100% SoL plus a pop-1 colony at 0% average to <c>(100 + 0) / 2 = 50</c>, where the old rebels-over-population
    /// formula gave 90. Drives the Declaration-of-Independence gate, the AI-declare gate, the limit engine's
    /// <c>getSoL</c> operand (Spanish-Succession year limit), the succession's weak/strong ranking, and the
    /// de-Witt foreign-report column.
    /// </summary>
    public int NationalSonsOfLiberty(Player player)
    {
        List<Colony> colonies = ColoniesOf(player).ToList();
        return colonies.Count == 0 ? 0 : colonies.Sum(c => c.SonsOfLiberty) / colonies.Count;
    }

    /// <summary>
    /// The player-facing label for <paramref name="player"/>'s nation (FreeCol <c>Player.getNationLabel</c>): the free
    /// nation's chosen <see cref="Player.IndependentNationName"/> once it has declared independence (it is a
    /// <see cref="PlayerType.Rebel"/> or <see cref="PlayerType.Independent"/>), otherwise the colonial nation's normal
    /// display name (e.g. <c>model.nation.dutch</c> → "Dutch"), or "Anonymous" for a nation-less default game. A rebel
    /// that named itself blank also falls back to the colonial name. The single oracle presentation reads to title the
    /// HUD and reports (ADR-006), so the rebranded nation name appears everywhere after a declaration.
    /// </summary>
    /// <param name="player">The player whose nation label is wanted.</param>
    public string NationLabelOf(Player player) => PlayerDisplayName(player);

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
    /// The multiple of the King's amassed Royal Expeditionary Force land strength an <b>AI</b> colonial power must
    /// command before it will declare independence (<see cref="ShouldAiDeclareIndependence"/>). Deliberately set to the
    /// same <see cref="RefDefeatPowerRatio"/> (1.5×) the <see cref="CheckForRefDefeat"/> win uses, so an AI only rebels
    /// when it already holds the strength that would let it <em>win</em> the war — a conservative, faithful-spirit gate.
    /// </summary>
    private const double AiDeclareStrengthRatio = RefDefeatPowerRatio;

    /// <summary>
    /// The combined land-offence strength of the King's amassed Royal Expeditionary Force <em>as it currently stands</em>
    /// (FreeCol <c>Player.calculateStrength(false)</c> applied to <c>Monarch.getExpeditionaryForce()</c>): the same
    /// per-unit <see cref="OffenceBase"/> the live yardstick <see cref="LandPowerOf"/> sums, but computed directly from
    /// the un-spawned <see cref="Force"/> blocks (each block's unit-type offence-additive + role offence, times the type
    /// multiplier, times the count) so it can be read <b>before</b> a declaration spawns the REF into units. The REF
    /// holds no Founding Fathers, so its per-unit combat factor is 1.0 — making this directly comparable to
    /// <see cref="LandPowerOf"/>. RNG-free; reads only the ruleset and the current REF force.
    /// </summary>
    internal double RefLandStrength()
    {
        Force force = EnsureRefForce();
        return force.LandUnits.Sum(entry =>
            (Ruleset.Unit(entry.UnitTypeId).OffenceAdditive + Ruleset.Role(entry.RoleId ?? RoleType.DefaultRoleId).Offence)
            * Ruleset.Unit(entry.UnitTypeId).OffenceMultiplier
            * entry.Count);
    }

    /// <summary>
    /// Whether a <b>non-human</b> colonial power should declare independence this turn — the AI trigger
    /// (<see cref="MaybeDeclareIndependence"/>) for kanban <c>86d3e49jp</c>. It declares only when it both
    /// <see cref="CheckDeclareIndependence">may legally declare</see> (still colonial, national SoL ≥ 50%, a connected
    /// port, before the last colonial year) <b>and</b> its own land strength (<see cref="LandPowerOf"/>) is at least
    /// <see cref="AiDeclareStrengthRatio"/> (1.5×) the King's amassed REF land strength (<see cref="RefLandStrength"/>) —
    /// the same yardstick <see cref="CheckForRefDefeat"/> measures the win by, so an AI rebels only when it already holds
    /// a war-winning army. The human is never auto-declared (the human pulls its own trigger from the UI).
    /// <para><b>Faithful-SPIRIT addition (documented deviation, docs/systems/independence.md §2).</b> Vanilla FreeCol's
    /// AI does <em>not</em> autonomously declare independence — <c>Player.getRebelStrengthRatio</c> appears only in a
    /// debug log line in <c>EuropeanAIPlayer.startWorking</c>, never driving a declaration. This gate is the
    /// faithful-<em>spirit</em> version of that latent yardstick: it reuses the same strength-ratio concept to let a
    /// dominant AI break away, where vanilla leaves the trigger human/Monarch-only.</para>
    /// <para><b>ADR-009.</b> Wholly RNG-free — it reads SoL, ports, the year, and the two strength sums — so it draws
    /// <em>no</em> stream (least of all the human's stream 0). Because no AI reaches a 1.5×-REF army within the soak
    /// window, it never fires in the default game, which therefore stays byte-identical.</para>
    /// </summary>
    internal bool ShouldAiDeclareIndependence(Player power)
    {
        if (power.IsHuman || !CheckDeclareIndependence(power).Allowed)
        {
            return false;
        }
        double refLand = RefLandStrength();
        // A zero-strength REF (a ruleset with no land force) can't gate a multiple — require a real, winnable army.
        return refLand > 0 && LandPowerOf(power) >= AiDeclareStrengthRatio * refLand;
    }

    /// <summary>
    /// The AI declaration step (kanban <c>86d3e49jp</c>), run once for each non-human colonial power on its turn
    /// (<see cref="RunPlayerTurn"/>): if <see cref="ShouldAiDeclareIndependence"/> holds, the power declares through the
    /// very same <see cref="DeclareIndependence(Player)"/> the human UI calls — flipping to a <see cref="PlayerType.Rebel"/>,
    /// forfeiting its Europe units, mustering its continental army, and bringing the REF into the field at war with it.
    /// From the next turn <see cref="RunRefTurn"/> prosecutes the war and <see cref="ResolveWarOfIndependence"/> resolves
    /// it, both of which already handle a rebel of either kind; the AI rebel itself runs the colonial economy path and
    /// <b>defends</b> (arms idle colonists, garrisons and digs into its colonies — A1's fortify/arming). Offensive
    /// rebel-vs-REF AI is deliberately <b>descoped</b> to a follow-up (the rebel's offensive in
    /// <see cref="RunForeignPowerTurn"/> is keyed to the human, and the REF is the aggressor) — documented in
    /// docs/systems/independence.md §5.
    /// <para>RNG-free up to the declaration; <see cref="OfferWarMercenaries"/> self-gates to the human, so an AI rebel
    /// draws no monarch stream. The default game never reaches the gate, so this is byte-identical there (ADR-009).</para>
    /// </summary>
    private void MaybeDeclareIndependence(Player power)
    {
        if (ShouldAiDeclareIndependence(power))
        {
            DeclareIndependence(power);
        }
    }

    /// <summary>
    /// Declares independence under the nation's <em>default</em> name (the colonial nation's display name) — the
    /// name-less overload kept for callers and tests that do not name the new nation. Routes through
    /// <see cref="DeclareIndependence(Player, string?)"/> with a null name, so the label falls back to the default.
    /// </summary>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDeclareIndependence"/>.</exception>
    public void DeclareIndependence(Player player) => DeclareIndependence(player, null);

    /// <summary>
    /// Declares independence (FreeCol <c>csDeclareIndependence(nationName, countryName)</c>): the player becomes a
    /// <see cref="PlayerType.Rebel"/> under the free nation's chosen <paramref name="nationName"/>, loses every unit in
    /// or sailing to Europe (and its recruiting), musters its veterans into colonial regulars, the King's Royal
    /// Expeditionary Force takes the field at war with the new nation, the natives who most resented the departing Crown
    /// swing behind the rebel, and the King offers a one-off war-mercenary (Hessian) force for hire.
    /// <para>The <paramref name="nationName"/> becomes the player's <see cref="Player.IndependentNationName"/> and thereby
    /// its display label everywhere the nation is named (FreeCol <c>getNationLabel</c>); a blank/whitespace name is left
    /// unset, so the label falls back to the colonial nation's normal display name. The <em>country</em> half of
    /// FreeCol's two-name declaration is the New-World name, already modelled as <see cref="NewWorldName"/> (86d3fq1fn) —
    /// this overload names only the nation, reusing that field for the land.</para>
    /// </summary>
    /// <param name="player">The colonial power declaring independence.</param>
    /// <param name="nationName">The name the free nation takes (e.g. "United States"); blank/whitespace ⇒ the default nation name.</param>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDeclareIndependence"/>.</exception>
    public void DeclareIndependence(Player player, string? nationName)
    {
        MoveCheck check = CheckDeclareIndependence(player);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        player.PlayerType = PlayerType.Rebel;
        player.DeclaredIndependenceTurn = Turn;
        // The free nation takes the name it chose (FreeCol setIndependentNationName); a blank answer is left unset so the
        // label falls back to the colonial nation's display name (PlayerDisplayName / NationDisplayName handle it).
        player.IndependentNationName = string.IsNullOrWhiteSpace(nationName) ? null : nationName.Trim();
        _units.RemoveAll(u => u.OwnerId == player.PlayerId && !u.IsNative && !u.IsOnMap); // units in/bound for Europe are forfeit
        player.RecruitDockList.Clear(); // Europe is closed to a rebel
        player.Market.Reinitialise();   // the new nation trades on a clean market — colonial boycotts/drift cleared (FreeCol reinitialiseMarket)

        ExpelTories(player);                    // the loyalists who won't fight the King desert (Col1 addition; FreeCol omits it)
        MusterContinentalArmy(player);
        CreateRefPlayer(player);
        ShiftNativeStanceOnDeclaration(player); // the natives who hated the Crown most side with the rebel
        OfferWarMercenaries(player);            // the King dangles a Hessian force for hire (a pending offer, never auto-applied)

        // FreeCol's csDeclareIndependence records the DECLARE_INDEPENDENCE history event with an early-declaration
        // bonus score of max(0, independenceTurn − turn): the further ahead of the historical independence turn the
        // nation breaks away, the larger the bonus, folded into the player score via HistoryEventScore (86d3f674b/0dc).
        int earlyBonus = Math.Max(0, Ruleset.IndependenceTurn - Turn);
        RecordHistory(HistoryEventKind.DeclaredIndependence, "Declared independence from the Crown.", earlyBonus);
    }

    /// <summary>The per-turn UI scratch list of loyalist (Tory) desertions on the most recent declaration.</summary>
    private readonly List<Colonies.ToryExpulsionNotice> _toryExpulsionNotices = [];

    /// <summary>
    /// Loyalist (Tory) colonists who abandoned each colony on the human's <see cref="DeclareIndependence(Player)">declaration of
    /// independence</see> — the royalists who would not bear arms against their King deserted the new nation. <b>Col1
    /// reconstruction</b> (FreeCol does not model this); see <see cref="ExpelTories"/> for the exact fraction. Transient
    /// UI scratch: refreshed each declaration (a one-off player action) and never saved — the colony's reduced
    /// <see cref="Colony.Population"/> is what persists. Only the declaring nation's colonies appear; a colony that
    /// loses no loyalists (e.g. a fervent 100%-SoL colony) is absent. The presentation reads it once after declaring.
    /// </summary>
    public IReadOnlyList<Colonies.ToryExpulsionNotice> ToryExpulsionNotices => _toryExpulsionNotices;

    /// <summary>
    /// The share of a colony's loyalists who desert per missing percentage-point of Sons-of-Liberty support
    /// (<c>1/100</c>): a colony at SoL <c>s</c> loses <c>floor(toryCount · (100 − s) / 100)</c> of its loyalists. So a
    /// 100%-SoL colony loses none; a 0%-SoL colony loses every loyalist it can (down to the one-colonist floor). The
    /// linear <c>(100 − SoL)/100</c> scaling reconstructs Col1's "the less loyal the colony, the more royalists flee"
    /// behaviour from the original game's design; the precise per-tenth tables of Col1 are not recoverable, so this is a
    /// faithful-spirit linear approximation documented as such (independence.md §Tory expulsion).
    /// </summary>
    private const int ToryDesertionPerSolPoint = 100; // divisor: lost = toryCount · (100 − SoL) / 100

    /// <summary>
    /// Removes the loyalist (Tory) colonists who desert each of the declaring nation's colonies on the Declaration of
    /// Independence — a <b>Col1-faithful reconstruction</b> our model adds (FreeCol's <c>csDeclareIndependence</c> has
    /// no such step). Each colony loses <c>floor(toryCount · (100 − SoL%) / 100)</c> of its non-rebel population — the
    /// lower its Sons-of-Liberty support, the more royalists flee — capped so at least one colonist always remains (a
    /// colony is never emptied by the exodus). A fully-committed 100%-SoL colony loses no-one. Each affected colony's
    /// worker assignments are re-trimmed to its new size (<see cref="TrimAssignments"/>), and a
    /// <see cref="Colonies.ToryExpulsionNotice"/> records the loss for the human's presentation.
    /// <para><b>ADR-009.</b> Wholly RNG-free — the loss is a deterministic fraction of the colony's current Tory count —
    /// so it draws no stream (least of all the human's stream 0). A 100%-SoL roster (the only state a default game ever
    /// declares from in tests/soak) loses nothing, leaving such a declaration's outcome unchanged.</para>
    /// </summary>
    /// <param name="player">The nation declaring independence; only its colonies are affected.</param>
    private void ExpelTories(Player player)
    {
        _toryExpulsionNotices.Clear();
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
        {
            // Lower SoL → a larger share of the loyalists desert. Floored, and capped so the colony keeps ≥ 1 colonist.
            int lost = colony.ToryCount * (100 - colony.SonsOfLiberty) / ToryDesertionPerSolPoint;
            lost = Math.Min(lost, colony.Population - 1);
            if (lost <= 0)
            {
                continue; // a fervent (high-SoL) or single-colonist colony keeps everyone
            }
            colony.Population -= lost;
            TrimAssignments(colony); // the colony now seats fewer workers — drop the over-cap assignments
            if (player.PlayerId == _human.PlayerId)
            {
                _toryExpulsionNotices.Add(new Colonies.ToryExpulsionNotice(
                    colony.Name, colony.Position, lost, colony.Population));
            }
        }
    }

    /// <summary>
    /// The native realignment that follows a declaration of independence (FreeCol <c>declareIndependence</c>'s native
    /// block, <c>InGameController.java:1482-1548</c>). Among the native nations the rebel has <b>contacted</b> (a chief
    /// visited — our proxy for FreeCol's <c>hasContacted</c>), sorted ascending by tribe-wide tension toward the rebel:
    /// <list type="bullet">
    /// <item>The <b>least-hostile</b> nation becomes the <b>ally</b>: its tension toward the rebel is <em>set to</em>
    /// <c>CONTENT</c> (600) if it was formally at war, to <c>HAPPY</c> (100) from a cease-fire, and left unchanged at
    /// peace — and it turns <b>hateful (1000)</b> toward the freshly-arrived REF (<c>makeContact</c> +
    /// <c>csModifyTension(HATEFUL.limit)</c>).</item>
    /// <item>Scanning back from the <b>most hostile</b>, the first nation that is not the ally (stopping early at an
    /// allied nation), scanning <em>past</em> nations already at war with the rebel, becomes the <b>enemy</b>: its
    /// tension toward the rebel is set to <c>HATEFUL</c> (1000) when at peace/cease-fire (unchanged when already at
    /// war), it takes an immediate transient <see cref="Stance.War"/> (see <see cref="SetNativeStance"/> for why the
    /// write is direct), and its tension toward the REF is zeroed (<c>csModifyTension(−current)</c> — a no-op for the
    /// freshly-created REF, kept for faithfulness). A single contacted nation yields the ally half only.</item>
    /// </list>
    /// <para>Every tension change goes through <see cref="SetNationTension"/> — the same delta lands on the nation
    /// channel and every settlement of the nation, mirroring FreeCol <c>ServerPlayer.csModifyTension</c>
    /// (<c>ServerPlayer.java:1415-1447</c>) and giving save persistence via the per-settlement alarm channels (v55);
    /// no save field, no version bump. The ally gets <b>no</b> direct stance write — its calmed tension de-escalates
    /// through the faithful <see cref="DetermineNativeStances"/> hysteresis.</para>
    /// <para><b>Documented deviations:</b> contact = ≥ 1 settlement <see cref="NativeSettlement.HasBeenVisitedBy"/>
    /// the rebel (FreeCol's stance-based <c>hasContacted</c> isn't modelled); tension ties break by nation id
    /// (FreeCol uses player-list order); the REF-channel settlement writes touch <em>all</em> the nation's settlements
    /// (FreeCol filters to REF-contacted settlements — none exist, the REF being brand new). RNG-free (ADR-009); both
    /// the human and the AI declare path run it.</para>
    /// </summary>
    private void ShiftNativeStanceOnDeclaration(Player rebel)
    {
        Player? refPlayer = _players.FirstOrDefault(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        // Contacted native nations, ASCENDING by tribe-wide tension toward the rebel (FreeCol sorts
        // getLiveNativePlayers by getTension(serverPlayer); deterministic nation-id tie-break — a documented deviation).
        List<string> natives = _nativeSettlements
            .Where(s => s.HasBeenVisitedBy(rebel.PlayerId))
            .Select(s => s.NationTypeId)
            .Distinct()
            .OrderBy(n => TribeTensionFor(n, rebel.PlayerId))
            .ThenBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (natives.Count == 0)
        {
            return; // the rebel has met no natives — nobody realigns
        }

        // The ALLY (`good = first(natives)`): calmed toward the rebel by current stance — WAR → CONTENT (600),
        // CEASE_FIRE → HAPPY (100), PEACE/ALLIANCE/default → delta 0 — then made hateful (1000) toward the King's REF.
        string good = natives[0];
        int allyTarget = NativeStanceToward(good, rebel.PlayerId) switch
        {
            Stance.War => NativeSettlement.AlarmContentMax,     // 600 — Tension.Level.CONTENT.getLimit()
            Stance.CeaseFire => NativeSettlement.AlarmHappyMax, // 100 — Tension.Level.HAPPY.getLimit()
            _ => TribeTensionFor(good, rebel.PlayerId),         // Peace/Alliance: no calming needed (delta 0)
        };
        SetNationTension(good, rebel.PlayerId, allyTarget);
        if (refPlayer is not null)
        {
            SetNationTension(good, refPlayer.PlayerId, NativeSettlement.MaxAlarm); // hateful (1000, HATEFUL.limit) toward the REF
        }

        // The ENEMY (FreeCol's reverse scan): from the most hostile downwards, the first contacted nation that is not
        // the ally (and not allied to the rebel — unreachable for natives today, kept for exactness), remembering but
        // scanning PAST nations already at war and stopping at the first that is not.
        string? bad = null;
        for (int i = natives.Count - 1; i >= 0; i--)
        {
            string p = natives[i];
            if (p == good || NativeStanceToward(p, rebel.PlayerId) == Stance.Alliance)
            {
                break;
            }
            bad = p;
            if (NativeStanceToward(p, rebel.PlayerId) != Stance.War)
            {
                break;
            }
        }
        if (bad is null)
        {
            return; // a single contacted nation is the ally only — no enemy half
        }
        if (NativeStanceToward(bad, rebel.PlayerId) is Stance.Peace or Stance.CeaseFire)
        {
            SetNationTension(bad, rebel.PlayerId, NativeSettlement.MaxAlarm); // hateful (1000) toward the rebel (already-at-war: delta 0)
        }
        SetNativeStance(bad, rebel.PlayerId, Stance.War); // FreeCol announces this war at the declaration — see SetNativeStance
        if (refPlayer is not null)
        {
            SetNationTension(bad, refPlayer.PlayerId, 0); // at peace (tension zeroed) with the King's force
        }
    }

    /// <summary>
    /// Pins <paramref name="nationTypeId"/>'s tribe-wide tension toward the player <paramref name="playerId"/> to
    /// exactly <paramref name="target"/>, first applying the same delta to <b>every settlement</b> of the nation
    /// (FreeCol <c>ServerPlayer.csModifyTension</c>, <c>ServerPlayer.java:1415-1447</c>: a native player's tension
    /// change propagates the identical delta as settlement alarm nation-wide — the incite precedent,
    /// <see cref="InciteNatives(Unit, NativeSettlement, int)"/>). The settlement writes both <b>persist</b> the
    /// relationship (the v55 per-settlement alarm channels are saved; the transient nation channel re-derives from
    /// them on load) and feed 25% of any <em>positive</em> delta back into the nation channel
    /// (<see cref="PropagateToTribe"/>) — so the exact pin comes <b>after</b> them, correcting any overshoot.
    /// A zero delta writes nothing. RNG-free (ADR-009).
    /// </summary>
    /// <param name="nationTypeId">The native nation whose tension channel is pinned.</param>
    /// <param name="playerId">The player the tension is held toward.</param>
    /// <param name="target">The exact tension the nation channel ends at (0..<see cref="MaxTension"/>).</param>
    private void SetNationTension(string nationTypeId, int playerId, int target)
    {
        int delta = target - TribeTensionFor(nationTypeId, playerId);
        if (delta == 0)
        {
            return;
        }
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == nationTypeId))
        {
            ChangeNativeAlarm(settlement, playerId, delta);
        }
        RaiseTribeTension(nationTypeId, playerId, target - TribeTensionFor(nationTypeId, playerId)); // exact pin, after the 25% propagation
    }

    /// <summary>
    /// The King's parting offer of a war-mercenary (Hessian) force on the very turn of the declaration (FreeCol
    /// <c>csDeclareIndependence</c>'s <c>loadMercenaryForce</c> + <c>csMercenaries(HESSIAN_MERCENARIES)</c>): a one-off
    /// professional force the new nation may hire for gold to face the REF. Surfaced as a
    /// <see cref="PendingMonarchDemand"/> (the same accept/decline seam as the King's in-game mercenary offers), built
    /// from the <b>fixed</b> declaration-of-independence <see cref="MonarchOptions.MercenaryForce"/> and applied only on
    /// accept via <see cref="RespondToMonarch"/> — never auto-applied, so the rebel's stream 0 stays byte-identical until
    /// the player chooses (ADR-009). An offer is made only when at least one unit is affordable; otherwise none.
    /// <para><b>Faithful to FreeCol (86d3fq0eg/86d3fpztm).</b> Unlike the land-only periodic <see cref="LoadMercenaries"/>
    /// generator (armed veterans only — used by the in-game <c>MONARCH_MERCENARIES</c>/monarch-tick Hessian offers), the
    /// declaration force is the fixed ruleset roster — classic 3 armed + 3 mounted veterans, 3 artillery, and <b>2
    /// men-o-war</b> — so a well-funded rebel can hire a navy to face the REF at sea (the parity gap this closes). It is
    /// affordability-trimmed FreeCol-style (<c>Monarch.loadMercenaryForce</c>): random units are dropped on the monarch
    /// RNG until the total <see cref="UnitType.MercenaryHirePrice">hire price</see> is payable. The offer rides the monarch
    /// RNG (an ephemeral stream off the rebel's current state), never stream 0.</para>
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
        if (LoadMercenaryForce(rebel, rng) is { } offer) // the fixed force, affordability-trimmed, or none
        {
            _pendingMonarchDemand = new PendingMonarchDemand(
                MonarchAction.HessianMercenaries, Offer: offer.Force, Price: offer.Price);
        }
    }

    /// <summary>
    /// Builds the declaration-of-independence mercenary offer from the fixed <see cref="MonarchOptions.MercenaryForce"/>,
    /// trimmed to what <paramref name="rebel"/> can afford (FreeCol <c>Monarch.loadMercenaryForce</c>). Each block's
    /// per-unit price is the unit type's <see cref="UnitType.MercenaryHirePrice"/> (its <c>mercenary-price</c> when set —
    /// classic only the man-o-war, 10000 — otherwise its Europe price); blocks whose per-unit price is ≤ 0 are dropped as
    /// un-hireable. If the whole roster is unaffordable, single units are removed one at a time — a <b>random</b> surviving
    /// block on the monarch <paramref name="rng"/> — until the running total is payable (so an expensive man-o-war may be
    /// dropped to fit the treasury). Returns the trimmed force and its total price, or null when nothing is affordable.
    /// <para><b>ADR-009.</b> Every draw is on the ephemeral monarch stream (<see cref="MonarchStreamId"/>), never stream 0;
    /// the human's economy stream is untouched until the offer is answered.</para>
    /// </summary>
    private (IReadOnlyList<ForceEntry> Force, int Price)? LoadMercenaryForce(Player rebel, IGameRandom rng)
    {
        // Start from the whole fixed roster, pairing each block with its per-unit hire price; drop un-hireable blocks
        // (price ≤ 0), mirroring FreeCol's price==0/INFINITY prune before the downsizing loop.
        var blocks = new List<(string UnitTypeId, string? RoleId, int Count, int UnitPrice)>();
        foreach (MonarchSupportUnit block in MonarchOpts.MercenaryForce)
        {
            int unitPrice = Ruleset.Unit(block.UnitTypeId).MercenaryHirePrice;
            if (unitPrice > 0 && block.Number > 0)
            {
                blocks.Add((block.UnitTypeId, block.RoleId, block.Number, unitPrice));
            }
        }
        int total = blocks.Sum(b => b.Count * b.UnitPrice);

        // Downsize FreeCol-style: while the roster is unaffordable, drop one unit from a random surviving block on the
        // monarch RNG (removing the block when its last unit goes), until it fits the treasury or nothing remains.
        while (blocks.Count > 0 && total > rebel.Gold)
        {
            int r = rng.Next(blocks.Count);
            var b = blocks[r];
            total -= b.UnitPrice;
            if (b.Count > 1)
            {
                blocks[r] = b with { Count = b.Count - 1 };
            }
            else
            {
                blocks.RemoveAt(r);
            }
        }
        if (blocks.Count == 0)
        {
            return null; // nothing affordable — no offer (FreeCol's negative price)
        }
        return (blocks.Select(b => new ForceEntry(b.UnitTypeId, b.RoleId, b.Count)).ToList(), total);
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

    // ── REF reinforcement waves + the King's morale (FreeCol Monarch ADD_TO_REF cadence + checkForREFDefeat) ──

    /// <summary>The number of successive waves the King lands his expeditionary force in (Col1/FreeCol-spirit: the REF
    /// comes ashore in echelons rather than all at once). The whole force still musters in Europe at the declaration —
    /// so it counts fully toward the King's strength — but only ~<c>1/<see cref="RefWaveCount"/></c> of the units still
    /// waiting in Europe are brought ashore per landing, the rest following in later waves.</summary>
    private const int RefWaveCount = 3;

    /// <summary>Turns between successive REF reinforcement waves coming ashore (the next echelon lands this many turns
    /// after the last). Deterministic (no RNG) so the cadence is byte-stable and soak-safe (ADR-009).</summary>
    private const int RefWaveInterval = 4;

    /// <summary>Turns until the next REF reinforcement wave may come ashore (counts down each REF turn); 0 or less lands a wave. Persisted (v62).</summary>
    private int _refWaveCountdown;

    /// <summary>The high-water mark of the King's <b>morale</b> — the full land army he committed at the declaration,
    /// the denominator the <see cref="RefMoraleBreakFloor"/> break test measures the surviving force against (FreeCol's
    /// <c>checkForREFDefeat</c> "the REF is arrogant" resolve). Persisted (v62); the <em>current</em> morale is derived
    /// live from the survivors (<see cref="RefMorale"/>), so it never goes stale. 0 means "not yet at war" (no REF).</summary>
    private int _refMoralePeak;

    /// <summary>The fraction of its peak the King's morale must fall below for his resolve to break and the REF to
    /// withdraw (1/4): once three-quarters of the army he committed has been ground down, a beaten King gives up even
    /// before the raw unit-count thresholds (<see cref="CheckForRefDefeat"/>) are met. A faithful-spirit reconstruction
    /// of FreeCol's morale/arrogance resolve (documented as a simplification in independence.md).</summary>
    private const int RefMoraleBreakFloor = 4; // break when morale < peak / 4

    /// <summary>
    /// The King's <b>current morale</b> — his resolve to keep prosecuting the war, derived live as the number of REF
    /// land units still surviving (ashore <em>or</em> still mustering in Europe), capped at the <see cref="RefMoralePeak"/>.
    /// Falls as the expeditionary force is ground down; once it breaks (<see cref="RefMoraleBroken"/>) a beaten King's
    /// resolve fails and the surviving force <see cref="GiveIndependence">withdraws</see>. A <b>pure read</b> (no stored
    /// scalar) so it is always current — disbanding REF units lowers it immediately. 0 before the REF takes the field.
    /// </summary>
    internal int RefMorale
    {
        get
        {
            if (_refMoralePeak <= 0)
            {
                return 0; // no REF afield
            }
            Player? refPlayer = _players.FirstOrDefault(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
            int surviving = refPlayer is null ? 0 : _units.Count(u => u.OwnerId == refPlayer.PlayerId && !u.Type.IsNaval);
            return Math.Min(_refMoralePeak, surviving);
        }
    }

    /// <summary>The King's peak morale (save state, v62).</summary>
    internal int RefMoralePeak => _refMoralePeak;

    /// <summary>Turns until the next REF wave comes ashore (save state, v62).</summary>
    internal int RefWaveCountdown => _refWaveCountdown;

    /// <summary>Re-installs the restored REF wave + morale-peak state on load (v62). The current morale is derived live, so only the peak persists.</summary>
    internal void SetRefReinforcementState(int waveCountdown, int moralePeak)
    {
        _refWaveCountdown = waveCountdown;
        _refMoralePeak = moralePeak;
    }

    /// <summary>
    /// Brings the Royal Expeditionary Force into play as a new <see cref="PlayerType.RoyalExpeditionaryForce"/> AI
    /// player at war with the rebel, realising the King's amassed <see cref="Force"/> into real units mustering in
    /// Europe. They come ashore in <b>successive waves</b> (<see cref="LandRefUnits"/> lands only an echelon at a time),
    /// so the whole force still counts toward the King's strength from the outset but reaches the field over several
    /// turns. The King's morale-<see cref="_refMoralePeak">peak</see> is set to the full land army so his resolve
    /// (<see cref="RefMorale"/>) can erode as the force is ground down. The REF draws from its own RNG stream (ADR-009).
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

        // The King's resolve peaks at the full committed army (FreeCol's REF strength), so it can break as the force is
        // whittled down across the war (the morale/withdrawal trigger). The current morale derives live from survivors.
        _refMoralePeak = force.LandUnitCount;
        _refWaveCountdown = 0; // the first wave may come ashore on the REF's very first turn

        // The whole force musters in Europe now (so it counts toward the King's strength immediately); LandRefUnits
        // brings it ashore an echelon at a time.
        foreach (ForceEntry entry in force.LandUnits.Concat(force.NavalUnits))
        {
            for (int i = 0; i < entry.Count; i++)
            {
                SpawnInEurope(entry.UnitTypeId, entry.RoleId, refId);
            }
        }
    }

    /// <summary>The size of a landing wave: ~<c>1/<see cref="RefWaveCount"/></c> of the whole committed army (at least 1),
    /// so the King's army reaches the field over roughly <see cref="RefWaveCount"/> echelons. Reads the peak (the full
    /// army), so the cap is stable across the war; RNG-free.</summary>
    private int RefWaveSize() => Math.Max(1, (_refMoralePeak > 0 ? _refMoralePeak : EnsureRefForce().LandUnitCount) / RefWaveCount);

    /// <summary>
    /// Whether the King's resolve has <b>broken</b> (his current <see cref="RefMorale">morale</see> has fallen below
    /// <c>peak / <see cref="RefMoraleBreakFloor"/></c>): three-quarters of the army he committed has been destroyed, so a
    /// beaten King abandons the war. False before the REF takes the field (peak 0). The earlier of this and the raw
    /// <see cref="CheckForRefDefeat"/> thresholds ends the war for the King. A faithful-spirit reconstruction of
    /// FreeCol's morale resolve. A pure read — reflects the surviving force immediately (no per-turn lag).
    /// </summary>
    internal bool RefMoraleBroken => _refMoralePeak > 0 && RefMorale < _refMoralePeak / RefMoraleBreakFloor;

    /// <summary>
    /// The Royal Expeditionary Force's turn (item 8 + <c>86d3drn5a</c> specialised combat AI): bring any units still
    /// mustering in Europe ashore near a rebel port, then prosecute the war with a <b>bespoke REF doctrine</b> that
    /// mirrors FreeCol's <c>REFAIPlayer</c> rather than the generic foreign-power war AI.
    /// <para>FreeCol's REF priorities, faithful-subset:
    /// <list type="number">
    /// <item><b>Colonies first, ports first</b> (<c>findColonyTargets</c> / <c>adjustMission</c>): land units make for
    /// the rebel's <em>connected-port</em> colonies before any inland one (a connected port is valued +500), scoring
    /// each by SoL (lower = easier), military/repair buildings, and stockade level (fortifications avoided).</item>
    /// <item><b>No unit-chase until a colony falls</b> (<c>adjustMission</c>: "Do not chase units until at least one
    /// colony is captured"; "The REF is more interested in colonies."): a REF land unit ignores loose rebel field
    /// units and marches on colonies until the REF holds a settlement, then it will hunt field units too.</item>
    /// <item><b>Navy hunts the rebel's ships &amp; supports the landings</b> (<c>REFNavyGoalDecider</c> / the navy block
    /// in <c>initialize</c>): men-o-war seek-and-destroy rebel naval units, falling back to escorting toward the
    /// invasion when no rebel ship is in reach.</item>
    /// <item><b>Thin garrisons</b> (<c>adjustMission</c> DefendSettlementMission → MIN unless badly defended): a REF unit
    /// standing in a captured colony presses on to the next objective rather than digging in.</item>
    /// </list>
    /// Everything draws from the REF's OWN RNG stream (<see cref="RandomFor"/>), never the human's stream 0 (ADR-009).
    /// <b>Faithful-subset note:</b> we model the priorities (target selection + the colony-first gate + the naval split),
    /// not FreeCol's full mission/transport scheduler — there is no separate amphibious transport doctrine, units march
    /// overland from the beachhead. See docs/systems/independence.md.</para>
    /// </summary>
    private void RunRefTurn(Player refPlayer)
    {
        LandRefUnits(refPlayer); // bring this turn's echelon ashore (staggered — only a wave at a time)

        Player? rebel = RefRebel(refPlayer);
        if (rebel is null)
        {
            return; // no rebel to fight (defensive — the REF exists only while a rebel does)
        }

        // Once the REF holds a captured rebel colony it may hunt loose rebel field units; until then it is colony-only
        // (FreeCol adjustMission: "Do not chase units until at least one colony is captured").
        bool holdsAColony = ColoniesOf(refPlayer).Any();

        foreach (Unit unit in _units.Where(u => IsOwnedBy(u, refPlayer)).OrderBy(u => u.Id).ToList())
        {
            if (!unit.IsOnMap || unit.MovementLeft <= 0 || OffenceBase(unit) <= 0)
            {
                continue; // still mustering in Europe, spent, or unarmed — nothing to do this turn
            }
            if (unit.Type.IsNaval)
            {
                RunRefNavalUnit(refPlayer, rebel, unit);
            }
            else
            {
                RunRefLandUnit(refPlayer, rebel, unit, holdsAColony);
            }
        }
    }

    /// <summary>The rebel the REF is prosecuting: the (single) player it is at war with that has declared independence
    /// (<see cref="PlayerType.Rebel"/> or, briefly, <see cref="PlayerType.Independent"/>), or null if none survives.</summary>
    private Player? RefRebel(Player refPlayer) =>
        _players.FirstOrDefault(p =>
            p.PlayerType is PlayerType.Rebel or PlayerType.Independent
            && StanceBetween(refPlayer.PlayerId, p.PlayerId) == Stance.War);

    /// <summary>
    /// Whether the player owning <paramref name="unit"/> is the Royal Expeditionary Force (FreeCol <c>Player.isREF</c>).
    /// Native-owned units are never the REF. Drives the REF-only siege bonus (<c>model.modifier.bombardBonus</c>) and
    /// the ambush-penalty mirror.
    /// </summary>
    private bool IsRefUnit(Unit unit) =>
        unit.OwnerNationId is null && PlayerById(unit.OwnerId) is { PlayerType: PlayerType.RoyalExpeditionaryForce };

    /// <summary>
    /// Whether a combat between the player owning <paramref name="attacker"/> and the colonial player
    /// <paramref name="defenderOwnerId"/> is a War-of-Independence battle for a colony (FreeCol
    /// <c>CombatModel.combatIsWarOfIndependence</c>): one side is the Royal Expeditionary Force and the other its
    /// rebelling colonial power (<see cref="PlayerType.Rebel"/>/<see cref="PlayerType.Independent"/>). Drives the
    /// popular-support defence bonus. Returns the defending colony's owner so the caller can read its SoL.
    /// </summary>
    private bool IsWarOfIndependenceColonyBattle(Unit attacker, int defenderOwnerId) =>
        attacker.OwnerNationId is null
        && PlayerById(attacker.OwnerId) is { } aPlayer
        && PlayerById(defenderOwnerId) is { } dPlayer
        && ((aPlayer.PlayerType is PlayerType.RoyalExpeditionaryForce
                && dPlayer.PlayerType is PlayerType.Rebel or PlayerType.Independent)
            || (aPlayer.PlayerType is PlayerType.Rebel or PlayerType.Independent
                && dPlayer.PlayerType is PlayerType.RoyalExpeditionaryForce));

    /// <summary>
    /// A REF <b>land</b> unit's turn (FreeCol <c>REFAIPlayer.giveNormalMissions</c> land branch): make for the rebel's
    /// colonies — connected ports first — capturing an undefended one when adjacent and fighting a garrison as a field
    /// unit; only once the REF holds a colony does it also hunt loose rebel field units. Standing in a just-captured
    /// colony, it presses on to the next objective (FreeCol's "garrison thinly") rather than digging in. All combat and
    /// movement draw the REF's own stream.
    /// </summary>
    private void RunRefLandUnit(Player refPlayer, Player rebel, Unit unit, bool holdsAColony)
    {
        // Decisive move: capture an adjacent undefended rebel colony right now (the war objective).
        if (AdjacentCapturableRebelColony(unit, rebel) is { } capture)
        {
            CaptureRebelColony(refPlayer, unit, capture);
            return;
        }

        // Best scored rebel colony within reach (ports favoured, fortifications/high-SoL avoided) — march on it,
        // fighting its garrison as a field unit when adjacent.
        if (PickRefColonyTarget(unit, rebel) is { } colonyTarget)
        {
            if (unit.Position.IsAdjacentTo(colonyTarget.Position))
            {
                if (CheckAttackColony(unit, colonyTarget.Position).Allowed)
                {
                    CaptureRebelColony(refPlayer, unit, colonyTarget.Position); // undefended → take it
                }
                else if (DefenderAt(unit, colonyTarget.Position) is not null && CheckAttack(unit, colonyTarget.Position).Allowed)
                {
                    AttackRebelUnit(refPlayer, unit, colonyTarget.Position); // garrisoned → fight the defender
                }
                else if (StepToward(refPlayer, unit, colonyTarget.Position) is { } sidestep)
                {
                    MoveUnit(unit, sidestep); // hemmed in beside a colony it can't take this turn — reposition
                }
            }
            else if (StepToward(refPlayer, unit, colonyTarget.Position) is { } toColony)
            {
                MoveUnit(unit, toColony);
            }
            return;
        }

        // Colony captured already → the REF may now also seek-and-destroy loose rebel field units (FreeCol's gate).
        if (holdsAColony && PickRefUnitTarget(unit, rebel) is { } prey)
        {
            if (unit.Position.IsAdjacentTo(prey) && CheckAttack(unit, prey).Allowed)
            {
                AttackRebelUnit(refPlayer, unit, prey);
            }
            else if (StepToward(refPlayer, unit, prey) is { } chase)
            {
                MoveUnit(unit, chase);
            }
            return;
        }

        // Nothing scored in range: close on the nearest rebel colony at any distance so the army besieges rather than
        // idling (FreeCol keeps land units heading for a port even when the scored search comes up empty).
        if (NearestRebelColony(unit, rebel) is { } distantColony && StepToward(refPlayer, unit, distantColony.Position) is { } march)
        {
            MoveUnit(unit, march);
        }
    }

    /// <summary>
    /// A REF <b>naval</b> unit's turn (FreeCol <c>REFNavyGoalDecider</c> + the navy block in <c>REFAIPlayer.initialize</c>):
    /// a man-o-war seek-and-destroys the rebel's warships — closing on the best-scored rebel naval tile in reach and
    /// engaging when adjacent — and, with no rebel ship to hunt, escorts toward the invasion (the nearest rebel port),
    /// supporting the landings rather than wandering. Draws the REF's own stream.
    /// </summary>
    private void RunRefNavalUnit(Player refPlayer, Player rebel, Unit unit)
    {
        if (PickRefUnitTarget(unit, rebel) is { } enemyShip)
        {
            if (unit.Position.IsAdjacentTo(enemyShip) && CheckAttack(unit, enemyShip).Allowed)
            {
                AttackRebelUnit(refPlayer, unit, enemyShip);
            }
            else if (StepToward(refPlayer, unit, enemyShip) is { } hunt)
            {
                MoveUnit(unit, hunt);
            }
            return;
        }
        // No rebel ship in reach — patrol toward the invasion (the nearest rebel port), supporting the landings.
        if (NearestRebelColony(unit, rebel) is { } port && StepToward(refPlayer, unit, port.Position) is { } support)
        {
            MoveUnit(unit, support);
        }
    }

    // ── REF target selection (86d3drn5a, FreeCol REFAIPlayer.findColonyTargets / adjustMission / REFNavyGoalDecider) ──

    /// <summary>+500 score for a rebel colony with a connected port (FreeCol <c>adjustMission</c> values connected-port
    /// settlements +500 — the REF strikes the rebel's coast first to cut it off from any foreign ally).</summary>
    private const int RefConnectedPortBonus = 500;

    /// <summary>
    /// The best scored rebel colony for a REF land unit to assault within the escalating seek range (FreeCol
    /// <c>findColonyTargets</c> + <c>adjustMission</c>): each rebel colony in reach is scored by the shared
    /// <see cref="ScoreColonyTarget"/> heuristic (value − distance, attacker strength, loot, fortifications down) plus a
    /// <see cref="RefConnectedPortBonus"/> for a connected port — so the REF strikes coastal/port colonies before inland
    /// ones, and the weakest-defended (low SoL, low stockade) of those first. A garrisoned colony is excluded here (its
    /// garrison is fought via the unit-tile path on the adjacent step); ties resolve by stable position order, no RNG.
    /// </summary>
    private ScoredTarget? PickRefColonyTarget(Unit unit, Player rebel)
    {
        foreach (int range in SeekRangeLadder)
        {
            ScoredTarget? best = null;
            foreach (Colony colony in _colonies
                         .Where(c => c.OwnerId == rebel.PlayerId
                                     && Chebyshev(c.Position, unit.Position) <= range
                                     && !_units.Any(u => u.IsOnMap && u.Position == c.Position))
                         .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X))
            {
                int score = ScoreColonyTarget(unit, colony) + (IsColonyCoastal(colony) ? RefConnectedPortBonus : 0);
                if (best is null || score > best.Value.Score)
                {
                    best = new ScoredTarget(colony.Position, IsColony: true, score);
                }
            }
            if (best is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// The tile of the best rebel unit for a REF unit to seek-and-destroy within the escalating seek range, scored by
    /// the shared <see cref="ScoreUnitTarget"/> heuristic (value − distance; weak/valuable targets favoured) — same
    /// domain as the attacker (a man-o-war hunts ships, a land unit hunts land units, FreeCol <c>REFNavyGoalDecider</c>
    /// + <c>UnitSeekAndDestroyMission</c>). Only rebel-owned tiles are eligible; ties resolve by stable id order, no RNG.
    /// Null when no rebel unit is in reach.
    /// </summary>
    private Position? PickRefUnitTarget(Unit unit, Player rebel)
    {
        foreach (int range in SeekRangeLadder)
        {
            Position? best = null;
            int bestScore = int.MinValue;
            foreach (Position tile in _units
                         .Where(u => u.IsOnMap && u.OwnerId == rebel.PlayerId && !u.IsNative
                                     && u.Type.IsNaval == unit.Type.IsNaval
                                     && Chebyshev(u.Position, unit.Position) <= range)
                         .OrderBy(u => u.Id).Select(u => u.Position).Distinct())
            {
                int score = ScoreUnitTarget(unit, tile);
                if (score != int.MinValue && score > bestScore)
                {
                    bestScore = score;
                    best = tile;
                }
            }
            if (best is { } found)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>
    /// The tile of an undefended rebel colony this REF land unit may capture right now — adjacent and ungarrisoned, as
    /// gated by <see cref="CheckAttackColony"/> (ties by position), or null. Mirror of
    /// <see cref="AdjacentCapturableEnemyColony"/> but rebel-scoped (the REF targets the rebel, not generically "the
    /// human" — they coincide in single-player, but this keeps the doctrine correct against any rebel).
    /// </summary>
    private Position? AdjacentCapturableRebelColony(Unit attacker, Player rebel) =>
        _colonies
            .Where(c => c.OwnerId == rebel.PlayerId && CheckAttackColony(attacker, c.Position).Allowed)
            .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .Select(c => (Position?)c.Position)
            .FirstOrDefault();

    /// <summary>The nearest rebel colony to <paramref name="unit"/> (Chebyshev, ties by position), or null if the rebel
    /// holds none — the besiege/patrol fallback when the scored search finds nothing in range. Pure (no RNG).</summary>
    private Colony? NearestRebelColony(Unit unit, Player rebel) =>
        _colonies.Where(c => c.OwnerId == rebel.PlayerId)
            .OrderBy(c => Chebyshev(c.Position, unit.Position))
            .ThenBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .FirstOrDefault();

    /// <summary>
    /// Resolves a REF unit's attack on the rebel unit at <paramref name="target"/> through the REF's OWN RNG stream
    /// (never stream 0), recording a <see cref="CombatNotice"/> for the presentation <b>only when the rebel victim is
    /// human-owned</b> — the notices are the human's victim log, and the REF fighting an <em>AI</em> rebel (a rival that
    /// self-declared via <see cref="ShouldAiDeclareIndependence"/>) is not the human's business (the same human-victim
    /// gate as <see cref="AttackEnemyUnit"/> / the <see cref="RaidForeignUnit"/> precedent, 86d3jgwhj). The REF sibling
    /// of <see cref="AttackEnemyUnit"/>: the defender is rebel-owned (filtered upstream by <see cref="PickRefUnitTarget"/>).
    /// </summary>
    private void AttackRebelUnit(Player refPlayer, Unit attacker, Position target)
    {
        Unit defender = DefenderAt(attacker, target)!;          // rebel-owned (filtered upstream)
        string defenderTypeId = defender.Type.Id;               // capture before the attack — a beaten loser is removed
        bool humanVictim = IsHumanOwned(defender);              // read before the attack — a beaten loser is removed
        CombatResult result = Attack(attacker, target, RandomFor(refPlayer)); // INTERNAL overload → the REF's stream
        if (humanVictim)
        {
            _combatNotices.Add(new CombatNotice(refPlayer.NationId!, defenderTypeId, result, target));
        }
    }

    /// <summary>
    /// Resolves the REF's assault on the undefended rebel colony at <paramref name="target"/> through the REF's OWN RNG
    /// stream (never stream 0), recording a <see cref="ColonyLossNotice"/> on a win <b>only when the captured colony was
    /// human-owned</b> (read before the assault hands it over) — the notice is the human's loss log, and the REF taking
    /// an <em>AI</em> rebel's colony is not the human's business (mirrors <see cref="CapturePlayerColony"/>, 86d3jgwhj).
    /// The REF sibling of <see cref="CapturePlayerColony"/>; a win hands the colony to the REF (its people/buildings/
    /// stores), a loss disarms/demotes the repelled attacker. Capturing the first colony unlocks the REF's field-unit
    /// hunt next turn.
    /// </summary>
    private void CaptureRebelColony(Player refPlayer, Unit attacker, Position target)
    {
        Colony colony = ColonyAt(target)!;
        string colonyName = colony.Name;         // read before AttackColony hands the colony over
        bool humanVictim = IsHumanOwned(colony); // likewise — after a win the owner is the REF
        CombatResult result = AttackColony(attacker, target, RandomFor(refPlayer)); // INTERNAL overload → the REF's stream
        if (humanVictim && result is CombatResult.GreatWin or CombatResult.Win)
        {
            _colonyLossNotices.Add(new ColonyLossNotice(refPlayer.NationId!, colonyName, target));
        }
    }

    /// <summary>
    /// Brings the REF's in-Europe units ashore in <b>successive waves</b> (the staggered reinforcement, 86d3fq0ak): on
    /// each REF turn the wave timer counts down, and when it elapses one echelon — the whole fleet plus up to
    /// <see cref="RefWaveSize"/> land units still waiting in Europe — comes ashore, the timer resetting to
    /// <see cref="RefWaveInterval"/> until the next wave. So the King's army reaches the field over several turns rather
    /// than all at once, fresh redcoats landing as the war drags on. The King's fleet makes landfall at its fixed entry
    /// tile (chosen near the human's start at game creation, FreeCol <c>Player.entryTile</c>): each unit takes the
    /// nearest empty tile (land for land units, water for ships) around that beachhead, falling back to the ring around
    /// a rebel's connected-port colonies when the entry tile is unset (pre-v47 save) or its surroundings are full.
    /// Deterministic — the cadence is a fixed interval and the wave a fixed cap, so no stream is drawn (ADR-009).
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

        // The next echelon is still crossing the Atlantic — count down and land nothing this turn.
        if (_refWaveCountdown > 0)
        {
            _refWaveCountdown--;
            return;
        }

        // Has any REF unit already reached the map? An RNG-free read that gates the one-off "the REF has landed" warning
        // below: it fires only on the FIRST landing (no REF unit ashore yet), never on the later staggered waves.
        bool refAlreadyAshore = _units.Any(u => IsOwnedBy(u, refPlayer) && u.IsOnMap);

        int landed = 0;
        int waveCap = RefWaveSize();
        bool anyLeftBehind = false;
        Position? firstLandingSpot = null; // where the first REF land unit came ashore this turn — anchors the warning
        foreach (Unit unit in _units
            .Where(u => IsOwnedBy(u, refPlayer) && u.Location == UnitLocation.InEurope)
            .OrderBy(u => u.Id)
            .ToList())
        {
            // Only a wave of LAND units lands per echelon (the fleet escorts and always sails in); when the wave is
            // full the rest of the army waits for the next echelon (the countdown below paces it).
            if (!unit.Type.IsNaval && landed >= waveCap)
            {
                anyLeftBehind = true;
                continue;
            }
            // Land at the fixed entry-tile beachhead first (the King's fleet arrives at the human's coast), then fall
            // back to the rebel colonies' surroundings if that beachhead is full this turn.
            Position? spot = (_refEntryTile is { } entry ? FindLandingTileNear(entry, unit.Type.IsNaval) : null)
                ?? FindLandingTile(targets, unit.Type.IsNaval);
            if (spot is { } s)
            {
                unit.Position = s;
                unit.Location = UnitLocation.OnMap;
                RevealForOwner(unit);
                if (!unit.Type.IsNaval)
                {
                    landed++;
                    firstLandingSpot ??= s; // the first land unit ashore anchors the landing warning
                }
            }
            else
            {
                anyLeftBehind = true; // no room ashore this turn — try again next wave
            }
        }
        // One-off REF-landing warning for the HUMAN rebel (FreeCol's REF-arrival message). Fired the first time the
        // King's army actually comes ashore (no REF unit was on the map before this turn, and a land unit landed now),
        // never on the later reinforcement waves (gated by refAlreadyAshore). RNG-free read; the rebel is the player the
        // REF is at war with — only a HUMAN rebel gets the player-facing notice. The nearest rebel colony to the
        // beachhead anchors the message (empty when the rebel holds none — a besieged-out rebel).
        if (!refAlreadyAshore && firstLandingSpot is { } landingSpot && RefRebel(refPlayer) is { IsHuman: true } humanRebel)
        {
            string nearest = NearestColonyOf(humanRebel, landingSpot, int.MaxValue)?.Name ?? string.Empty;
            _refLandingNotices.Add(new RefLandingNotice(nearest));
        }
        // If an echelon is still owed (units waited this turn), the next wave lands after the interval; otherwise the
        // army is fully ashore and the timer rests at 0.
        _refWaveCountdown = anyLeftBehind ? RefWaveInterval : 0;
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
    private const int SpanishSuccessionYear = 1600;       // classic model.limit.spanishSuccession.year (engine fallback)

    /// <summary>The spec event whose limits gate the Spanish Succession (FreeCol <c>model.event.spanishSuccession</c>).</summary>
    private const string SpanishSuccessionEventId = "model.event.spanishSuccession";

    /// <summary>The year-gate limit on that event (FreeCol <c>model.limit.spanishSuccession.year</c>): <c>year ≥ 1600</c>.</summary>
    private const string SpanishSuccessionYearLimitId = "model.limit.spanishSuccession.year";

    private bool _spanishSuccessionDone;

    /// <summary>Whether the Spanish Succession consolidation has already happened (save state).</summary>
    internal bool SpanishSuccessionDone => _spanishSuccessionDone;

    /// <summary>Installs the restored Spanish-Succession flag (save load).</summary>
    internal void SetSpanishSuccessionDone(bool done) => _spanishSuccessionDone = done;

    /// <summary>
    /// Whether the winner chose to <see cref="ContinuePlaying">keep playing</see> after victory, which disables the
    /// game's victory conditions (FreeCol <c>continuePlaying</c> writes the three <c>VICTORY_*</c> game options false; we
    /// short-circuit <see cref="Winner"/> at game scope instead, to avoid mutating a possibly-shared ruleset). Persisted
    /// (v62) so a game saved after "keep playing" reloads with the win still disabled — without it a reload would re-fire
    /// the victory and re-show the screen. Defaults false (the common case).
    /// </summary>
    private bool _victoryConditionsDisabled;

    /// <summary>Whether the player chose to keep playing after winning, so the victory conditions are off (save state, v62).</summary>
    internal bool VictoryConditionsDisabled => _victoryConditionsDisabled;

    /// <summary>Re-applies the restored "kept playing after winning" state on load (v62): disables the victory conditions exactly as <see cref="ContinuePlaying"/> did.</summary>
    internal void SetVictoryConditionsDisabled(bool disabled) => _victoryConditionsDisabled = disabled;

    /// <summary>
    /// The player that has won the game (FreeCol <c>ServerGame.checkForWinner</c>), or null while it continues — a
    /// <b>pure read</b> over the live players, evaluated against the ruleset's enabled victory conditions (each a
    /// parsed-or-defaulted boolean, so the classic defaults leave the default game's outcome unchanged):
    /// <list type="bullet">
    /// <item><b>Federation</b> (<see cref="Specification.Ruleset.VictoryFederation"/>, classic <b>off</b>, Australia on):
    /// checked first — when the <see cref="FederationPhase"/> reaches <see cref="GameSession.FederationPhase.Commonwealth"/>
    /// the human wins by Federation (Phase-4a, ADR-021). Off for classic, so its outcome is unchanged.</item>
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
            if (_victoryConditionsDisabled)
            {
                return null; // the winner chose to keep playing — the victory conditions are off (FreeCol continuePlaying)
            }
            // Australian-Federation win (Phase-4a, ADR-021): when the Federation victory is enabled and the phase has
            // reached the Commonwealth proclamation, the human wins by Federation. Checked FIRST for the Australia
            // variant; off (and so skipped) for the classic ruleset, which leaves the phase at ColonialMaturity forever,
            // so this changes nothing for the default game (ADR-009).
            if (Ruleset.VictoryFederation && _federationPhase == FederationPhase.Commonwealth)
            {
                return _human;
            }
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

    // ── Voluntary retirement (FreeCol InGameController.retire → ServerPlayer.csWithdraw) ──────────────────────

    /// <summary>
    /// Whether <paramref name="player"/> may <see cref="Retire">retire</see> from the game right now (FreeCol gates the
    /// Retire menu action on a live, in-play player): the player is still <em>playing</em> — a colonial power, a rebel
    /// mid-war, or an independent nation — and has not already retired, been defeated, or had the game won out from
    /// under it. A pure, RNG-free read; the presentation gates the Retire affordance on it (ADR-006).
    /// </summary>
    public MoveCheck CheckRetire(Player player)
    {
        if (player.PlayerType is not (PlayerType.Colonial or PlayerType.Rebel or PlayerType.Independent))
        {
            return MoveCheck.No("Only an active nation can retire.");
        }
        if (IsRebelDefeated(player))
        {
            return MoveCheck.No("A defeated nation cannot retire.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>
    /// Voluntarily <b>retires</b> <paramref name="player"/> from the game (FreeCol <c>InGameController.retire</c>):
    /// builds the player's leaderboard <see cref="HighScore"/> entry from the current state (the same pure
    /// <see cref="RecordHighScore"/> read the win/defeat path uses, with <paramref name="gameId"/> for de-duplication),
    /// then withdraws the player by flipping it to <see cref="PlayerType.Retired"/> — the game is over for it. The
    /// caller persists the returned score to the leaderboard store (kept entirely separate from the game save, exactly
    /// as FreeCol keeps high scores in their own file). A retired player's <see cref="PlayerType"/> ordinal is saved by
    /// the existing player-state path, so a game saved mid-retirement reloads retired.
    /// <para><b>Faithful-subset note.</b> FreeCol's <c>csWithdraw</c> also kills the player's units/colonies and emits a
    /// "nation withdrawn" history+message. Our single-human model treats retirement as an end-of-game state for the
    /// human (the presentation stops play and shows the end screen) rather than dismantling the board, so we keep only
    /// the score-record + player-type-flip half; the world is simply frozen behind the end-game screen. Documented in
    /// independence.md. <b>ADR-009:</b> wholly RNG-free (the score is a pure read and the type flip draws nothing), so
    /// retiring leaves the human's stream 0 byte-identical.</para>
    /// </summary>
    /// <param name="player">The player retiring — normally the human.</param>
    /// <param name="gameId">An optional per-game id for leaderboard de-duplication; empty if unknown.</param>
    /// <returns>The completed high-score entry for the caller to persist.</returns>
    /// <exception cref="InvalidMoveException">The player cannot retire now; see <see cref="CheckRetire"/>.</exception>
    public HighScore Retire(Player player, string gameId = "")
    {
        MoveCheck check = CheckRetire(player);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        HighScore score = RecordHighScore(player, won: player.PlayerType == PlayerType.Independent, gameId);
        player.PlayerType = PlayerType.Retired; // withdraw — the game is over for this player (FreeCol changePlayerType(RETIRED))
        return score;
    }

    /// <summary>Whether the human has voluntarily <see cref="Retire">retired</see> — the game is over for them (the presentation reads this for the end-game screen, like <see cref="IsHumanDefeated"/>).</summary>
    public bool IsHumanRetired => _human.PlayerType == PlayerType.Retired;

    // ── Continue playing after winning (FreeCol InGameController.continuePlaying) ─────────────────────────────

    /// <summary>
    /// Whether the human may choose to <see cref="ContinuePlaying">keep playing</see> right now — there is a
    /// <see cref="Winner"/>, that winner is the human, and the victory conditions have not already been disabled by a
    /// previous "keep playing" (FreeCol restricts <c>continuePlaying</c> to single-player and to the actual winner). A
    /// pure read; the victory screen gates its "keep playing" button on it (ADR-006).
    /// </summary>
    public bool CanContinuePlaying =>
        !_victoryConditionsDisabled && Winner is { } w && w.PlayerId == _human.PlayerId;

    /// <summary>
    /// Lets a single-player <b>winner keep playing</b> after victory (FreeCol <c>InGameController.continuePlaying</c>,
    /// which writes the three <c>VICTORY_*</c> game options false): sets the game-level
    /// <see cref="VictoryConditionsDisabled"/> flag, so <see cref="Winner"/> returns null thereafter and the game
    /// proceeds. The flag is persisted (v62) so a reload after "keep playing" does not re-fire the win. A no-op unless
    /// the human <see cref="CanContinuePlaying">may continue</see>.
    /// <para><b>Why a game-level flag, not a ruleset mutation.</b> FreeCol's spec is per-game, so it disables the win by
    /// flipping the spec's victory options; our <see cref="Specification.Ruleset"/> may be shared across games (and is in
    /// tests), so mutating it would leak the disabled win into other games. The flag short-circuits <see cref="Winner"/>
    /// at the game scope, achieving the same effect without touching the shared ruleset.</para>
    /// <para><b>ADR-009:</b> RNG-free — it only sets a boolean, drawing no stream — so continuing leaves the human's
    /// stream 0 byte-identical. The default game never wins inside the soak window, so this never fires there and the
    /// soak stays byte-identical.</para>
    /// </summary>
    public void ContinuePlaying()
    {
        if (!CanContinuePlaying)
        {
            return; // not a winner, or already continuing — nothing to disable
        }
        _victoryConditionsDisabled = true;
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
    /// The combined attack power of a player's <b>naval</b> units (the sea counterpart of <see cref="LandPowerOf"/>;
    /// FreeCol <c>Player.calculateStrength(true)</c>). Counts every owned ship regardless of location, summing each
    /// hull's <see cref="OffenceBase"/>.
    /// </summary>
    internal double NavalPowerOf(Player player) =>
        _units.Where(u => u.OwnerId == player.PlayerId && !u.IsNative && u.Type.IsNaval).Sum(OffenceBase);

    /// <summary>
    /// A colonial player's military strength for the Foreign-Affairs report (a read-only oracle, ADR-006): its combined
    /// land attack power (<see cref="LandPowerOf"/>) and naval attack power (<see cref="NavalPowerOf"/>), mirroring
    /// FreeCol's <c>NationSummary.getMilitaryStrength</c> / <c>getNavalStrength</c> (<c>Player.calculateStrength</c>).
    /// FreeCol surfaces these two figures for every rival unconditionally — unlike the De-Witt-gated SoL/tax/father
    /// columns — so the presentation can show a rival's force without revealing its internal politics. RNG-free; never
    /// mutates.
    /// </summary>
    /// <param name="player">The colonial player whose forces to total (typically a foreign power).</param>
    /// <returns>The player's combined land attack power and naval attack power.</returns>
    public (double LandPower, double NavalPower) ColonialStrength(Player player) =>
        (LandPowerOf(player), NavalPowerOf(player));

    /// <summary>
    /// The human's military strength for the Military report (a read-only oracle, ADR-006): the combined land attack
    /// power of every owned land unit (<see cref="LandPowerOf"/>, the War-of-Independence yardstick), plus a count of
    /// the player's land and naval units. The presentation surfaces these next to the King's
    /// <see cref="ExpeditionaryForceStrength"/> so the player can gauge the rebellion's odds. RNG-free; never mutates.
    /// </summary>
    /// <returns>The human's combined land attack power and its land / naval unit counts.</returns>
    public (double LandPower, int LandUnits, int NavalUnits) HumanMilitaryStrength()
    {
        double landPower = LandPowerOf(_human);
        int land = _units.Count(u => u.OwnerId == _human.PlayerId && !u.IsNative && !u.Type.IsNaval);
        int naval = _units.Count(u => u.OwnerId == _human.PlayerId && !u.IsNative && u.Type.IsNaval);
        return (landPower, land, naval);
    }

    /// <summary>
    /// The Royal Expeditionary Force as it currently stands, for the Military report (a read-only oracle, ADR-006):
    /// the King's growing army that lands if the human declares independence (FreeCol
    /// <c>Monarch.getExpeditionaryForce</c>). Returns the REF <see cref="Force"/>'s land and naval <b>unit counts</b>;
    /// the force materialises its base composition for the read without storing it (no save change — the persisted REF
    /// is whatever <see cref="RefForceOrNull"/> holds). The presentation surfaces this beside
    /// <see cref="HumanMilitaryStrength"/> as the strength comparison.
    /// </summary>
    /// <returns>The REF's land and naval unit counts.</returns>
    public (int LandUnits, int NavalUnits) ExpeditionaryForceStrength()
    {
        Force ref_ = _refForce ?? BuildBaseRef(); // read-only: don't store a lazily-built base (RefForceOrNull stays the saved truth)
        return (ref_.LandUnitCount, ref_.NavalUnitCount);
    }

    /// <summary>
    /// Whether the rebel has broken the Royal Expeditionary Force (FreeCol <c>checkForREFDefeat</c>): the REF holds no
    /// settlements, and <b>either</b> the King's <see cref="RefMoraleBroken">morale has broken</see> (a beaten King's
    /// resolve fails) <b>or</b> the rebel's land power is at least 1.5× the REF's and the REF is reduced below 7 land or
    /// 2 naval units. The unit counts include the King's units still mustering in Europe (the un-landed waves), so the
    /// win can't fire before his reinforcements are committed. When either path fires the surviving force surrenders.
    /// </summary>
    internal bool CheckForRefDefeat(Player refPlayer, Player rebel)
    {
        if (ColoniesOf(refPlayer).Any())
        {
            return false; // the REF still holds captured colonies — the rebellion isn't won
        }

        // Count every REF unit the King still owns — ashore AND the waves still mustering in Europe — so a war is never
        // declared won while fresh reinforcements survive (they keep him in the fight until they are destroyed too).
        int land = _units.Count(u => u.OwnerId == refPlayer.PlayerId && !u.Type.IsNaval);
        int naval = _units.Count(u => u.OwnerId == refPlayer.PlayerId && u.Type.IsNaval);

        if (land >= RefDefeatLandThreshold && naval >= RefDefeatNavalThreshold)
        {
            return false; // the REF still has its minimum credible force (FreeCol's 7-land/2-naval hang-on floor)
        }

        // Below the hang-on floor: the King gives up if EITHER the rebel now out-musters him 1.5× (FreeCol's raw test)
        // OR his morale has broken — three-quarters of the army he committed is gone, so a beaten King abandons the war
        // even when neither side is the clear stronger (the morale/withdrawal trigger, 86d3fq0ak). The morale path is
        // gated behind the floor above, so a still-credible force (≥7 land, ≥2 naval) never withdraws on morale alone.
        return RefMoraleBroken || LandPowerOf(rebel) >= RefDefeatPowerRatio * LandPowerOf(refPlayer);
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

        // The war is over: the King's wave/morale state is retired so a reload of the won game carries no pending
        // reinforcement (the REF is gone, so there would be nothing to land anyway).
        _refWaveCountdown = 0;
        _refMoralePeak = 0;
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
    /// The Intervention Force composition <em>as it has grown by the current turn</em> (FreeCol
    /// <c>Monarch.updateInterventionForce</c>): the longer the war drags on, the larger the friendly ally's landing.
    /// FreeCol grows the standing force once at declaration by <c>updates = turnNumber / interventionTurns</c> extra
    /// units per land block; our model lands the ally repeatedly (each time the rebel re-banks
    /// <see cref="Specification.Ruleset.InterventionBells"/>), so we recompute the growth from the <em>current</em> turn
    /// at each landing — a progressively bigger ally arriving every <c>interventionTurns</c>-worth of war, derived purely
    /// from existing state (the turn counter), so nothing new is persisted (no save bump, ADR-009).
    /// <para><b>Growth (exactly FreeCol's formula).</b> With <c>updates = turn / interventionTurns</c> (integer
    /// division; 0 when <c>interventionTurns ≤ 0</c> or before the first interval): every <b>land</b> block of base
    /// count <c>c</c> becomes <c>c + updates</c> (so the classic-medium 2+2+2 land force is 3+3+3 after one interval,
    /// 4+4+4 after two…). The <b>naval</b> blocks keep their base count and are then topped up with transport ships so
    /// the fleet can still carry the enlarged land force — FreeCol's <c>Force.prepareToBoard</c>: if the land force's
    /// required hold slots exceed the fleet's capacity, add <c>(deficit / shipSpace) + 1</c> more of the first
    /// unit-carrying ship type. This keeps the grown land units from being left behind for want of a berth.</para>
    /// <para>Pure and RNG-free — it reads only the ruleset force and the turn, drawing no stream (least of all the
    /// human's stream 0). <c>internal</c> so the growth is unit-testable at a chosen turn without driving the war.</para>
    /// </summary>
    /// <param name="turn">The current turn (FreeCol <c>getTurn().getNumber()</c>); the growth scales with it.</param>
    /// <returns>The grown force composition: land blocks enlarged by the interval count, naval blocks plus transport.</returns>
    internal InterventionForceComposition GrownInterventionForce(int turn)
    {
        InterventionForceComposition baseForce = Ruleset.InterventionForce;
        int updates = Ruleset.InterventionTurns > 0 ? turn / Ruleset.InterventionTurns : 0;
        if (updates <= 0)
        {
            return baseForce; // before the first interval (or no growth period) the base force lands unchanged
        }

        // Land blocks grow by `updates` each (FreeCol: ivf.add(type, role, updates) merges onto the matching block).
        var grown = new List<InterventionForceUnit>();
        int spaceRequired = 0; // hold slots the enlarged land force needs (FreeCol Force.spaceRequired)
        int capacity = 0;      // hold slots the fleet provides (FreeCol Force.capacity)
        foreach (InterventionForceUnit block in baseForce.Units)
        {
            UnitType type = Ruleset.Unit(block.UnitTypeId);
            if (type.IsNaval)
            {
                grown.Add(block); // naval blocks keep their base count; transport is topped up below
                capacity += type.Space * block.Count;
            }
            else
            {
                int count = block.Count + updates;
                grown.Add(block with { Count = count });
                spaceRequired += type.CarrySlots * count;
            }
        }

        // prepareToBoard: top up the first carrier ship type so the fleet can berth the enlarged land force.
        if (spaceRequired > capacity)
        {
            InterventionForceUnit? carrierBlock = grown
                .FirstOrDefault(b => Ruleset.Unit(b.UnitTypeId) is { IsNaval: true, Space: > 0 });
            if (carrierBlock is { } carrier)
            {
                int shipSpace = Ruleset.Unit(carrier.UnitTypeId).Space;
                int more = (spaceRequired - capacity) / shipSpace + 1;
                int index = grown.IndexOf(carrier);
                grown[index] = carrier with { Count = carrier.Count + more };
            }
        }

        return new InterventionForceComposition(grown);
    }

    /// <summary>
    /// Lands the foreign Intervention Force to aid <paramref name="rebel"/>: a friendly power's
    /// <see cref="Specification.Ruleset.InterventionForce"/> (classic medium: 2 colonial-regular soldiers + 2 dragoons
    /// + 2 artillery + 2 men-o-war) — grown for the current turn by <see cref="GrownInterventionForce"/> so a long war
    /// brings progressively larger landings — arrives off one of the rebel's connected ports, owned by — and fighting
    /// for — the rebel (FreeCol <c>createUnits(ivf…, entry, …)</c> on the rebel itself). The men-o-war drop onto water
    /// near the port and <b>carry the land units as passengers</b> (FreeCol <c>loadShips</c>): the ally fleet arrives
    /// offshore with the troops aboard, and the player disembarks them where they choose. This is what lets the ally
    /// reach a <em>besieged</em> port (its whole purpose) — it needs only water for the ships, not an open ring of beach
    /// tiles. Land units that don't fit in the fleet's holds are left behind (FreeCol disposes the left-overs).
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

        // The force grows with the war's length (FreeCol updateInterventionForce): each landing is sized for the
        // current turn, so a protracted rebellion draws ever-larger ally reinforcements. Pure — no stream drawn.
        InterventionForceComposition force = GrownInterventionForce(Turn);

        // Deterministic ally stream, seeded off the rebel's own stream state + the turn (never stream 0). For a human
        // rebel RandomFor returns stream 0, so we seed off the REF's persisted stream instead when present, else the
        // rebel's — either way the seed is reproducible and the draws come from InterventionStreamId, not stream 0.
        Player? refPlayer = _players.FirstOrDefault(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        ulong seed = (refPlayer?.Rng ?? rebel.Rng)?.SaveState().State ?? _random.SaveState().State;
        var rng = new Pcg32Random(seed + (ulong)Turn, InterventionStreamId);

        Colony port = ports[rng.Next(ports.Count)];

        // Pass 1: the men-o-war make landfall on the water around the port (each takes the next free sea tile).
        var fleet = new List<Unit>();
        foreach (InterventionForceUnit block in force.Units.Where(b => Ruleset.Unit(b.UnitTypeId).IsNaval))
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
        foreach (InterventionForceUnit block in force.Units.Where(b => !Ruleset.Unit(b.UnitTypeId).IsNaval))
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
        if (_spanishSuccessionDone || !SpanishSuccessionYearReached())
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

    /// <summary>
    /// Whether the Spanish Succession's year gate is met (FreeCol <c>ServerGame.spanishSuccessionReady</c>'s
    /// <c>event.getLimit("model.limit.spanishSuccession.year").evaluate(this)</c>): routed through the generic
    /// limit-evaluation engine (<see cref="EvaluateLimit"/>) when the spec carries the event/limit, so a variant
    /// retunes the trigger year by editing the XML alone. Falls back to the hardcoded <see cref="SpanishSuccessionYear"/>
    /// (1600) only when the spec defines no such limit. The classic spec's limit is <c>year ≥ 1600</c> — identical to
    /// the fallback — so the default game's trigger is byte-identical (ADR-006/009). Player-agnostic (a game-scoped
    /// year limit ignores the player), so any live player is passed.
    /// </summary>
    private bool SpanishSuccessionYearReached()
    {
        if (Ruleset.Event(SpanishSuccessionEventId)?.Limit(SpanishSuccessionYearLimitId) is { } yearLimit)
        {
            return EvaluateLimit(yearLimit, _players[0]); // game-scoped: the player argument is unused
        }
        return CurrentYear >= SpanishSuccessionYear; // spec without the limit → the classic 1600 fallback
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
