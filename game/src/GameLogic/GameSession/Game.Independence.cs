using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;

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
    private const int LastColonialYear = 1800;        // model.option.lastColonialYear (classic) — TODO(86d3c9rg6) ruleset

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
    /// past the last colonial year.
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
        if (CurrentYear > LastColonialYear)
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

        MusterContinentalArmy(player);
        CreateRefPlayer(player);
    }

    /// <summary>
    /// Upgrades the rebel's veteran soldiers to colonial regulars (FreeCol continental-army muster): per colony with
    /// SoL &gt; 50, the cap is <c>(unitCount + 2) · (SoL − 50) / 100</c>; the strongest rebels rise first.
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

    /// <summary>The Royal Expeditionary Force's turn — it sails in and assaults the rebel (filled in the REF-combat slice).</summary>
    private void RunRefTurn(Player refPlayer)
    {
        // REF arrival + War-of-Independence combat lands in item 8 (86d3c9v8k); the REF takes no action yet.
    }
}
