using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
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
    private enum Tab { Colonies, Units, Foreign, Natives, Religion }

    private Game _game = null!;
    private Tab _tab = Tab.Colonies;

    private static readonly System.Collections.Generic.Dictionary<Tab, string> Titles = new()
    {
        [Tab.Colonies] = "Colony report",
        [Tab.Units] = "Unit report",
        [Tab.Foreign] = "Foreign affairs",
        [Tab.Natives] = "Native nations",
        [Tab.Religion] = "Religion",
    };

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
        GetNode<Label>("VBox/ReportTitle").Text = Titles[_tab];
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        // Tab row (named buttons for the L3 tests; the active tab is disabled).
        var tabs = new HBoxContainer { Name = "Tabs" };
        tabs.AddChild(TabButton("Colonies", Tab.Colonies));
        tabs.AddChild(TabButton("Units", Tab.Units));
        tabs.AddChild(TabButton("Foreign", Tab.Foreign));
        tabs.AddChild(TabButton("Natives", Tab.Natives));
        tabs.AddChild(TabButton("Religion", Tab.Religion));
        dynamic.AddChild(tabs);
        dynamic.AddChild(new HSeparator());

        switch (_tab)
        {
            case Tab.Colonies: BuildColonies(dynamic); break;
            case Tab.Units: BuildUnits(dynamic); break;
            case Tab.Foreign: BuildForeign(dynamic); break;
            case Tab.Natives: BuildNatives(dynamic); break;
            case Tab.Religion: BuildReligion(dynamic); break;
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

    // ── Foreign affairs tab ──────────────────────────────────────────────────────────────────────────────

    private void BuildForeign(VBoxContainer dynamic)
    {
        long human = _game.HumanPlayer.PlayerId;
        var powers = _game.Players
            .Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial)
            .OrderBy(p => p.PlayerId)
            .ToList();
        if (powers.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No foreign powers are known.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        // FreeCol's NationSummary shows a rival's stance, #colonies, #units and gold to everyone; SoL%, father
        // count and tax% stay hidden without the De Witt ability (which we omit faithfully).
        foreach (Player p in powers)
        {
            Stance stance = _game.StanceBetween((int)human, p.PlayerId);
            int colonies = _game.Colonies.Count(c => c.OwnerId == p.PlayerId);
            int units = _game.Units.Count(u => u.OwnerId == p.PlayerId);
            dynamic.AddChild(new Label
            {
                Name = $"Foreign_{p.PlayerId}",
                Text = $"{Strip(p.NationId)} — {stance}, {colonies} colonies, {units} units, {p.Gold} gold",
            });
        }
    }

    // ── Native nations tab ───────────────────────────────────────────────────────────────────────────────

    private void BuildNatives(VBoxContainer dynamic)
    {
        var settlements = _game.NativeSettlements
            .Where(s => _game.IsExplored(s.Position))
            .OrderBy(s => s.NationTypeId, System.StringComparer.Ordinal)
            .ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
            .ToList();
        if (settlements.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No native settlements discovered yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (NativeSettlement s in settlements)
        {
            string capital = s.IsCapital ? "★" : "";
            string skill = s.LearnableSkill is { } sk ? Strip(sk) : "none";
            string mission = s.HasMission ? "  ·  mission" : "";
            string wanted = s.WantedGoods.Count > 0
                ? "  ·  wants " + string.Join(", ", s.WantedGoods.Select(Strip))
                : "";
            dynamic.AddChild(new Label
            {
                Name = $"Native_{s.Position.X}_{s.Position.Y}",
                Text = $"{Strip(s.NationTypeId)}{capital} ({s.Position.X},{s.Position.Y}) — alarm {s.AlarmLevel}, teaches {skill}{mission}{wanted}",
            });
        }
    }

    // ── Religion tab (faithful subset: the immigration bar) ──────────────────────────────────────────────

    private void BuildReligion(VBoxContainer dynamic)
    {
        dynamic.AddChild(new Label
        {
            Name = "ReligionImmigration",
            Text = $"Crosses toward the next emigrant: {_game.Immigration} / {_game.ImmigrationRequired}",
        });
        dynamic.AddChild(new Label { Text = $"Recruit a waiting emigrant now: {_game.RecruitPrice} gold" });
        dynamic.AddChild(new Label
        {
            Text = "(Per-church cross output is a follow-up; map-region exploration discovery is deferred.)",
        });
    }

    /// <summary>The readable tail of a <c>model.*.foo</c> id (e.g. <c>model.nation.dutch</c> → <c>dutch</c>).</summary>
    private static string Strip(string? id) => id is null ? "?" : id[(id.LastIndexOf('.') + 1)..];

    private static string Signed(int n) => (n > 0 ? "+" : "") + n;
}
