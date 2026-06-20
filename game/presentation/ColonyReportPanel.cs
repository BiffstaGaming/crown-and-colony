using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The empire reports screen (FreeCol's Report* panels) with switchable tabs:
/// <list type="bullet">
/// <item><b>Colonies</b> (`86d3c9wz6` — Colony / Production / Requirements): a row per human colony with population,
/// Sons-of-Liberty %, production bonus, net food, what it is building (with the materials still required), and its
/// net per-turn production (the shared <see cref="Game.ColonyNetProduction"/> oracle).</item>
/// <item><b>Units</b> (`86d3c9x15` — Labour / Military / Naval / Cargo): the human's units grouped into military
/// (<see cref="Game.IsMilitaryUnit"/>), naval, cargo carriers (with hold contents) and labour (the person residual),
/// each with its role and location.</item>
/// </list>
/// Pure presentation (ADR-006) — reads <see cref="Game"/> oracles only, never mutates. Built programmatically into
/// the fixed <c>VBox/Dynamic</c> shell. (Status reports + a Trade/market report are follow-up tabs.)
/// </summary>
public partial class ColonyReportPanel : PanelContainer
{
    private enum Tab { Colonies, Units }

    private Game _game = null!;
    private Tab _tab = Tab.Colonies;

    /// <summary>Opens the reports screen on the Colonies tab over the current game state.</summary>
    public void Open(Game game)
    {
        _game = game;
        _tab = Tab.Colonies;
        Rebuild();
        Show();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/ReportTitle").Text = _tab == Tab.Colonies ? "Colony report" : "Unit report";
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        // Tab row (named buttons for the L3 tests; the active tab is disabled).
        var tabs = new HBoxContainer { Name = "Tabs" };
        tabs.AddChild(TabButton("Colonies", Tab.Colonies));
        tabs.AddChild(TabButton("Units", Tab.Units));
        dynamic.AddChild(tabs);
        dynamic.AddChild(new HSeparator());

        if (_tab == Tab.Colonies)
        {
            BuildColonies(dynamic);
        }
        else
        {
            BuildUnits(dynamic);
        }
    }

    private Button TabButton(string label, Tab tab)
    {
        var b = new Button { Name = $"Tab_{tab}", Text = label, Disabled = _tab == tab };
        b.Pressed += () => { _tab = tab; Rebuild(); };
        return b;
    }

    // ── Colonies tab ─────────────────────────────────────────────────────────────────────────────────────

    private void BuildColonies(VBoxContainer dynamic)
    {
        List<Colony> colonies = _game.Colonies
            .Where(c => c.OwnerId == _game.HumanPlayer.PlayerId)
            .OrderBy(c => c.Id)
            .ToList();

        if (colonies.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no colonies yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (Colony c in colonies)
        {
            IReadOnlyDictionary<string, int> net = _game.ColonyNetProduction(c);
            int food = net.GetValueOrDefault(Colony.FoodId);
            string bonus = c.ProductionBonus != 0 ? $", bonus {Signed(c.ProductionBonus)}" : "";
            // The header label carries the per-colony name + summary; named for the L3 test to find by colony id.
            dynamic.AddChild(new Label
            {
                Name = $"Colony_{c.Id}",
                Text = $"{c.Name} — pop {c.Population}, SoL {c.SonsOfLiberty}%{bonus}",
            });
            dynamic.AddChild(new Label { Text = $"    Food {Signed(food)}/turn  ·  Building: {BuildingSummary(c)}" });
            dynamic.AddChild(new Label { Text = $"    Producing: {ProductionSummary(net)}" });
            dynamic.AddChild(new HSeparator());
        }
    }

    /// <summary>The current build + the materials still required to finish it (FreeCol's "Requirements"), or a dash.</summary>
    private string BuildingSummary(Colony colony)
    {
        if (colony.CurrentBuild is not { } id || _game.DescribeBuildable(id) is not { } info)
        {
            return "—";
        }
        IEnumerable<string> reqs = info.BuildCost
            .Select(o => $"{_game.Ruleset.Goods(o.GoodsId).ShortName} {colony.StoreOf(o.GoodsId)}/{o.Amount}");
        string needs = reqs.Any() ? $" (needs {string.Join(", ", reqs)})" : "";
        return $"{info.ShortName}{needs}";
    }

    /// <summary>The colony's non-food net production, signed and named, or "nothing".</summary>
    private string ProductionSummary(IReadOnlyDictionary<string, int> net)
    {
        List<string> parts = net
            .Where(kv => kv.Key != Colony.FoodId && kv.Value != 0)
            .OrderBy(kv => kv.Key, System.StringComparer.Ordinal)
            .Select(kv => $"{Signed(kv.Value)} {_game.Ruleset.Goods(kv.Key).ShortName}")
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
    }

    // ── Units tab ────────────────────────────────────────────────────────────────────────────────────────

    private void BuildUnits(VBoxContainer dynamic)
    {
        List<Unit> units = _game.PlayerUnits.OrderBy(u => u.Id).ToList();
        if (units.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no units.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        // Four FreeCol report groups. A carrier (ship) legitimately appears in both Naval and Cargo, mirroring
        // FreeCol's separate ReportNavalPanel / ReportCargoPanel; labour is the non-military person residual.
        Section(dynamic, "Military", units.Where(_game.IsMilitaryUnit), u => $"{TypeAndRole(u)}  ·  {Where(u)}");
        Section(dynamic, "Naval", units.Where(u => u.Type.IsNaval), u => $"{TypeAndRole(u)}  ·  {Where(u)}{CargoSummary(u)}");
        Section(dynamic, "Cargo", units.Where(u => u.Type.IsCarrier || u.Type.CarryTreasure), u => $"{TypeAndRole(u)}  ·  {Where(u)}{CargoSummary(u)}");
        Section(dynamic, "Labour", units.Where(u => u.Type.IsPerson && !u.Type.IsNaval && !_game.IsMilitaryUnit(u)), u => $"{TypeAndRole(u)}  ·  {Where(u)}");
    }

    private static void Section(VBoxContainer dynamic, string name, IEnumerable<Unit> members, System.Func<Unit, string> describe)
    {
        List<Unit> list = members.ToList();
        var header = new Label { Name = $"UnitSection_{name}", Text = $"— {name} ({list.Count}) —" };
        dynamic.AddChild(header);
        foreach (Unit u in list)
        {
            dynamic.AddChild(new Label { Text = $"    {describe(u)}" });
        }
    }

    private string TypeAndRole(Unit u)
    {
        string role = u.HasDefaultRole ? "" : $" ({_game.Ruleset.Role(u.RoleId).Id[(u.RoleId.LastIndexOf('.') + 1)..]})";
        return $"{u.Type.ShortName}{role}";
    }

    private static string Where(Unit u)
    {
        if (u.IsUnderRepair)
        {
            return $"repairing ({u.RepairTurnsRemaining})";
        }
        if (u.Location == UnitLocation.InEurope)
        {
            return "Europe";
        }
        if (u.SailTurnsRemaining > 0)
        {
            return $"sailing ({u.SailTurnsRemaining})";
        }
        if (u.IsAboard)
        {
            return "aboard";
        }
        return $"({u.Position.X},{u.Position.Y})";
    }

    private string CargoSummary(Unit carrier)
    {
        var parts = new List<string>();
        if (carrier.TreasureAmount > 0)
        {
            parts.Add($"{carrier.TreasureAmount} gold");
        }
        parts.AddRange(carrier.Cargo
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key, System.StringComparer.Ordinal)
            .Select(kv => $"{kv.Value} {_game.Ruleset.Goods(kv.Key).ShortName}"));
        return parts.Count > 0 ? $"  [{string.Join(", ", parts)}]" : "";
    }

    private static string Signed(int n) => (n > 0 ? "+" : "") + n;
}
