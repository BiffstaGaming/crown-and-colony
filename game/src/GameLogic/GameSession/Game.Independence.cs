using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
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
    /// loses every unit in or sailing to Europe (and its recruiting), musters its veterans into colonial regulars, and
    /// the King's Royal Expeditionary Force takes the field at war with the new nation.
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
    }

    /// <summary>
    /// Upgrades the rebel's veteran soldiers to colonial regulars (FreeCol continental-army muster). The cap is summed
    /// over colonies with SoL &gt; 50 as <c>(unitCount + 2) · (SoL − 50) / 100</c>, and the earliest veterans rise first.
    /// <para><b>Faithful-subset deviation:</b> FreeCol caps and draws this <em>per colony</em> from each colony's own
    /// resident units (<c>getAllUnitsList</c>); our colony workers are not in the unit list, so we use the rebel's
    /// nationwide unit count in each term and upgrade veterans from the whole map. This can over-muster in a
    /// multi-colony rebellion versus FreeCol — a per-colony port is a follow-up (TODO 86d3c9rg6 / a muster-fidelity task).</para>
    /// </summary>
    private void MusterContinentalArmy(Player player)
    {
        int unitCount = _units.Count(u => u.OwnerId == player.PlayerId && !u.IsNative);
        int limit = ColoniesOf(player)
            .Where(c => c.SonsOfLiberty > 50)
            .Sum(c => (unitCount + 2) * (c.SonsOfLiberty - 50) / 100);
        if (limit <= 0)
        {
            return;
        }
        foreach (Unit veteran in _units
            .Where(u => u.OwnerId == player.PlayerId && u.IsOnMap && u.Type.Id == VeteranSoldierUnitTypeId)
            .OrderBy(u => u.Id)
            .Take(limit)
            .ToList())
        {
            UpgradeUnitType(veteran, ColonialRegularUnitTypeId);
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

    /// <summary>The player that has won by securing independence (FreeCol VICTORY_DEFEAT_REF), or null while the game continues.</summary>
    public Player? Winner => _players.FirstOrDefault(p => p.PlayerType == PlayerType.Independent);

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

    /// <summary>Per-turn War-of-Independence resolution: a rebel that has broken the REF wins its independence.</summary>
    private void ResolveWarOfIndependence()
    {
        Player? refPlayer = _players.FirstOrDefault(p => p.PlayerType == PlayerType.RoyalExpeditionaryForce);
        if (refPlayer is null)
        {
            return; // no war under way
        }
        foreach (Player rebel in _players.Where(p => p.PlayerType == PlayerType.Rebel).ToList())
        {
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
