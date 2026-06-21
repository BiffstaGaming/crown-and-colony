using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
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
/// <item><b>Education</b> (`86d3drn6f` — FreeCol's ReportEducationPanel): for each human colony with a teaching building
/// (schoolhouse/college/university), the building, its eligible expert teachers (occupants whose <see cref="UnitType.Skill"/>
/// fits the building's skill window), and the students they will raise — each with its skill-advancement progress
/// (<see cref="Colony.SchoolTrainingTurnsAt"/> / <see cref="Specification.Ruleset.NeededTurnsOfTraining"/>). Read-only
/// — schooling runs in GameLogic's colony turn.</item>
/// <item><b>Production</b> (`86d3drn6g` — FreeCol's ReportProductionPanel): a per-good breakdown for the good chosen in a
/// selector — each human colony's net output of it (<see cref="Game.ColonyNetProduction"/>) with the producers behind it
/// (tile workers via <see cref="Colony.TileWorkers"/> + <see cref="Game.TileYield(World.Position,string)"/>, and the
/// buildings whose <see cref="Specification.BuildingType.Productions"/> make it, with their worker counts).</item>
/// <item><b>Labour</b> (`86d3drn6m` — FreeCol's ReportLabourPanel): every human colonist listed by type, location and job —
/// the colony residents from the worker overlays (<see cref="Colony.TileWorkers"/> / <see cref="Colony.BuildingOccupants"/> /
/// idle) plus the on-map person units, grouped by unit type.</item>
/// <item><b>Foreign</b> / <b>Natives</b> / <b>Religion</b> (`86d3c9x3c`): rival-power, discovered-settlement and
/// immigration summaries.</item>
/// <item><b>Market</b> (`86d3dmn6d` — FreeCol's ReportTradePanel, price subset): every tradeable good's current
/// sell (bid) and buy (ask) price plus a boycott marker, read straight from the human's <see cref="Game.Market"/>.
/// Volume/income columns need accumulated trade history (no oracle yet) and are scoped out.</item>
/// <item><b>Congress</b> (`86d3c9x53` — FreeCol's ReportReligiousPanel sibling, the Continental Congress facet):
/// the founding-father election state — the father currently being recruited (<see cref="Game.CurrentFather"/>),
/// the liberty (bells) progress (<see cref="Game.Liberty"/> / <see cref="Game.TotalFoundingFatherCost"/>), and one
/// row per offered father (<see cref="Game.OfferedFathers"/>) with its category, marking the in-progress pick.
/// Read-only — election lives in <see cref="FoundingFatherPanel"/>.</item>
/// <item><b>History</b> (`86d3c9x53` — FreeCol's ReportHistoryPanel facet): the human's notable past events
/// (<see cref="Game.History"/>) in turn order — colonies founded, wars entered, Founding Fathers elected. The
/// event log is in-memory only this wave (not persisted), so a reloaded game's history starts empty.</item>
/// </list>
/// Pure presentation (ADR-006) — reads <see cref="Game"/> oracles only, never mutates. Built programmatically into
/// the fixed <c>VBox/Dynamic</c> shell.
/// </summary>
public partial class ColonyReportPanel : PanelContainer
{
    private enum Tab { Colonies, Units, Education, Production, Labour, Foreign, Natives, Religion, Market, Congress, History }

    private Game _game = null!;
    private Tab _tab = Tab.Colonies;

    /// <summary>The good the Production tab is currently breaking down (its ruleset id); null = "(nothing selected)".</summary>
    private string? _productionGood;

    private static readonly System.Collections.Generic.Dictionary<Tab, string> Titles = new()
    {
        [Tab.Colonies] = "Colony report",
        [Tab.Units] = "Unit report",
        [Tab.Education] = "Education",
        [Tab.Production] = "Production",
        [Tab.Labour] = "Labour",
        [Tab.Foreign] = "Foreign affairs",
        [Tab.Natives] = "Native nations",
        [Tab.Religion] = "Religion",
        [Tab.Market] = "Trade & market prices",
        [Tab.Congress] = "Continental Congress",
        [Tab.History] = "History",
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
        tabs.AddChild(TabButton("Education", Tab.Education));
        tabs.AddChild(TabButton("Production", Tab.Production));
        tabs.AddChild(TabButton("Labour", Tab.Labour));
        tabs.AddChild(TabButton("Foreign", Tab.Foreign));
        tabs.AddChild(TabButton("Natives", Tab.Natives));
        tabs.AddChild(TabButton("Religion", Tab.Religion));
        tabs.AddChild(TabButton("Market", Tab.Market));
        tabs.AddChild(TabButton("Congress", Tab.Congress));
        tabs.AddChild(TabButton("History", Tab.History));
        dynamic.AddChild(tabs);
        dynamic.AddChild(new HSeparator());

        switch (_tab)
        {
            case Tab.Colonies: BuildColonies(dynamic); break;
            case Tab.Units: BuildUnits(dynamic); break;
            case Tab.Education: BuildEducation(dynamic); break;
            case Tab.Production: BuildProduction(dynamic); break;
            case Tab.Labour: BuildLabour(dynamic); break;
            case Tab.Foreign: BuildForeign(dynamic); break;
            case Tab.Natives: BuildNatives(dynamic); break;
            case Tab.Religion: BuildReligion(dynamic); break;
            case Tab.Market: BuildMarket(dynamic); break;
            case Tab.Congress: BuildCongress(dynamic); break;
            case Tab.History: BuildHistory(dynamic); break;
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

    /// <summary>The human's colonies, ordered by id — the common spine of the colony-facing report tabs.</summary>
    private List<Colony> HumanColonies() => _game.Colonies
        .Where(c => c.OwnerId == _game.HumanPlayer.PlayerId)
        .OrderBy(c => c.Id)
        .ToList();

    // ── Education tab (FreeCol ReportEducationPanel: teachers + students per school) ───────────────────────

    private void BuildEducation(VBoxContainer dynamic)
    {
        // FreeCol's education report: for every colony with a teaching building (schoolhouse/college/university), list
        // the eligible expert teachers and the students they raise. We compose colony reads only (ADR-006) — the actual
        // schooling (least-skilled student pick + per-turn training) runs in GameLogic's colony turn.
        List<Colony> colonies = HumanColonies()
            .Where(c => c.Buildings.Any(b => _game.Ruleset.Building(b).Teaches))
            .ToList();
        if (colonies.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No colony has a school yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (Colony c in colonies)
        {
            dynamic.AddChild(new Label { Name = $"School_{c.Id}", Text = $"{c.Name}" });
            foreach (string buildingId in c.Buildings.Where(b => _game.Ruleset.Building(b).Teaches))
            {
                BuildingType school = _game.Ruleset.Building(buildingId);
                // Teachers: occupants whose skill fits the school's window (FreeCol Building.canAdd MINIMUM/MAXIMUM_SKILL —
                // only an expert in range teaches; an over- or under-skilled colonist can't).
                List<string> teachers = c.BuildingOccupants(buildingId)
                    .Where(t => SkillFitsSchool(t, school))
                    .OrderBy(t => _game.Ruleset.Unit(t).Skill)
                    .ToList();
                dynamic.AddChild(new Label { Text = $"  {Display(school.ShortName)} — teachers: {TeacherList(teachers)}" });

                foreach (string teacherType in teachers)
                {
                    if (FindStudent(c, teacherType) is not { } studentType)
                    {
                        dynamic.AddChild(new Label { Text = $"    {Display(Strip(teacherType))}: no one to teach right now" });
                        continue;
                    }
                    UnitType learns = _game.Ruleset.GetTeachingType(teacherType, studentType)!;
                    int needed = System.Math.Max(1, _game.Ruleset.NeededTurnsOfTraining(teacherType, studentType) - c.ProductionBonus);
                    int done = System.Math.Min(c.SchoolTrainingTurnsAt(buildingId), needed);
                    dynamic.AddChild(new Label
                    {
                        Name = $"Student_{c.Id}_{Strip(buildingId)}",
                        Text = $"    {Display(Strip(studentType))} → {Display(learns.ShortName)} (training {done}/{needed} turns)",
                    });
                }
            }
            dynamic.AddChild(new HSeparator());
        }
    }

    /// <summary>An occupant fits a school when its skill sits within the building's [minimum, maximum] skill window (FreeCol).</summary>
    private bool SkillFitsSchool(string unitTypeId, BuildingType school)
    {
        int skill = _game.Ruleset.Unit(unitTypeId).Skill;
        return skill >= school.MinimumSkill && skill <= school.MaximumSkill;
    }

    private string TeacherList(IEnumerable<string> teacherTypes)
    {
        List<string> names = teacherTypes.Select(t => Display(Strip(t))).ToList();
        return names.Count > 0 ? string.Join(", ", names) : "none assigned";
    }

    /// <summary>
    /// The colony's least-skilled teachable colonist for a teacher of <paramref name="teacherType"/>, mirroring the
    /// engine's automatic pick (FreeCol <c>Colony.findStudent</c>, least-skill-first) over the colony's worker overlays —
    /// a read-only echo for display (the binding selection is GameLogic's; ADR-006). Null when no one is teachable.
    /// </summary>
    private string? FindStudent(Colony colony, string teacherType)
    {
        string? best = null;
        void Consider(string type)
        {
            if (_game.Ruleset.GetTeachingType(teacherType, type) is null)
            {
                return; // already at/above the taught skill, or no education rung
            }
            if (best is null || _game.Ruleset.Unit(type).Skill < _game.Ruleset.Unit(best).Skill)
            {
                best = type;
            }
        }

        foreach (Position tile in colony.TileWorkers.Keys)
        {
            Consider(colony.WorkerTypeAt(tile));
        }
        foreach (string buildingId in colony.Buildings)
        {
            if (_game.Ruleset.Building(buildingId).Teaches)
            {
                continue; // a colonist inside a school is staff, never a student
            }
            foreach (string occupant in colony.BuildingOccupants(buildingId))
            {
                Consider(occupant);
            }
        }
        foreach (string idle in colony.IdleWorkerTypes)
        {
            Consider(idle);
        }
        if (colony.IdleColonists > 0)
        {
            Consider(Colony.FreeColonistTypeId);
        }
        return best;
    }

    // ── Production tab (FreeCol ReportProductionPanel: per-good, per-colony, per-producer) ─────────────────

    private void BuildProduction(VBoxContainer dynamic)
    {
        // FreeCol's production report breaks down ONE selectable good across the player's colonies, showing each colony's
        // net output and the buildings that make it. We mirror the "non-farmed goods" selector and add the tile producers
        // too. All reads (ADR-006): Game.ColonyNetProduction for the colony total, TileWorkers/TileYield for tile output,
        // and BuildingType.Productions for which buildings produce the good.
        // FreeCol's selector lists the non-farmed goods (manufactured goods plus bells/crosses/hammers — building output,
        // not tile crops); a farmed good's producers are its tiles, covered by the Colonies tab.
        List<GoodsType> selectable = _game.Ruleset.GoodsTypes
            .Where(g => !g.IsFarmed)
            .OrderBy(g => g.Id, System.StringComparer.Ordinal)
            .ToList();

        var selector = new OptionButton { Name = "ProductionGood" };
        selector.AddItem("(select a good)", -1);
        for (int i = 0; i < selectable.Count; i++)
        {
            GoodsType g = selectable[i];
            selector.AddItem(Display(g.ShortName), i);
            if (g.Id == _productionGood)
            {
                selector.Select(selector.ItemCount - 1);
            }
        }
        selector.ItemSelected += index =>
        {
            int id = (int)selector.GetItemId((int)index);
            _productionGood = id >= 0 ? selectable[id].Id : null;
            Rebuild();
        };
        dynamic.AddChild(selector);
        dynamic.AddChild(new HSeparator());

        if (_productionGood is not { } good)
        {
            dynamic.AddChild(new Label { Text = "Pick a good to see who produces it.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }
        string storedAs = _game.Ruleset.Goods(good).StoredAs;

        List<Colony> colonies = HumanColonies();
        if (colonies.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no colonies yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (Colony c in colonies)
        {
            int net = _game.ColonyNetProduction(c).GetValueOrDefault(storedAs);
            dynamic.AddChild(new Label
            {
                Name = $"Production_{c.Id}",
                Text = $"{c.Name} — net {Signed(net)} {Display(_game.Ruleset.Goods(good).ShortName)}/turn",
            });
            foreach (string producer in Producers(c, good))
            {
                dynamic.AddChild(new Label { Text = $"    {producer}" });
            }
        }
    }

    /// <summary>The producers behind one good in a colony: each worked tile yielding it, then each building type that makes it (with staffing).</summary>
    private IEnumerable<string> Producers(Colony colony, string goodsId)
    {
        var lines = new List<string>();
        foreach ((Position tile, string good) in colony.TileWorkers.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            if (good == goodsId)
            {
                lines.Add($"tile ({tile.X},{tile.Y}): +{_game.TileYield(tile, good)}");
            }
        }
        foreach (string buildingId in colony.Buildings)
        {
            BuildingType b = _game.Ruleset.Building(buildingId);
            if (b.Productions.Any(p => p.Outputs.Any(o => o.GoodsId == goodsId)))
            {
                int workers = colony.BuildingWorkers.GetValueOrDefault(buildingId);
                lines.Add($"{Display(b.ShortName)}: {workers}/{b.Workplaces} workers");
            }
        }
        if (lines.Count == 0)
        {
            lines.Add("no local producer");
        }
        return lines;
    }

    // ── Labour tab (FreeCol ReportLabourPanel: every colonist by type/location/job) ────────────────────────

    private void BuildLabour(VBoxContainer dynamic)
    {
        // FreeCol's labour report tallies every colonist by type and location. We list each human colonist with its
        // location and job: colony residents come from the worker overlays (tile good / building / idle), on-map people
        // from the unit list. Grouped by unit type for a stable, scannable roster. Reads only (ADR-006).
        var rows = new List<(string Type, string Location, string Job)>();

        foreach (Colony c in HumanColonies())
        {
            foreach ((Position tile, string good) in c.TileWorkers.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
            {
                rows.Add((c.WorkerTypeAt(tile), c.Name, $"farming {Display(_game.Ruleset.Goods(good).ShortName)}"));
            }
            foreach (string buildingId in c.Buildings)
            {
                if (_game.Ruleset.Building(buildingId).Teaches)
                {
                    foreach (string occupant in c.BuildingOccupants(buildingId))
                    {
                        rows.Add((occupant, c.Name, $"teaching in {Display(_game.Ruleset.Building(buildingId).ShortName)}"));
                    }
                    continue;
                }
                foreach (string occupant in c.BuildingOccupants(buildingId))
                {
                    rows.Add((occupant, c.Name, $"working {Display(_game.Ruleset.Building(buildingId).ShortName)}"));
                }
            }
            // Idle colonists: the named non-free types, then the free-colonist remainder.
            foreach (string idle in c.IdleWorkerTypes)
            {
                rows.Add((idle, c.Name, "idle"));
            }
            int freeIdle = c.IdleColonists - c.IdleWorkerTypes.Count;
            for (int i = 0; i < freeIdle; i++)
            {
                rows.Add((Colony.FreeColonistTypeId, c.Name, "idle"));
            }
        }

        foreach (Unit u in _game.PlayerUnits.Where(u => u.Type.IsPerson).OrderBy(u => u.Id))
        {
            rows.Add((u.Type.Id, "in the field", Where(u)));
        }

        if (rows.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no colonists.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        // Grouped by unit type (FreeCol's per-type tally), each group a named header then its colonists.
        foreach (IGrouping<string, (string Type, string Location, string Job)> group in rows
            .GroupBy(r => r.Type)
            .OrderBy(g => g.Key, System.StringComparer.Ordinal))
        {
            dynamic.AddChild(new Label
            {
                Name = $"Labour_{Strip(group.Key)}",
                Text = $"— {Display(Strip(group.Key))} ({group.Count()}) —",
            });
            foreach ((string _, string location, string job) in group)
            {
                dynamic.AddChild(new Label { Text = $"    {location}: {job}" });
            }
        }
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

    // ── Trade & market prices tab (faithful subset: per-good bid/ask + boycott) ──────────────────────────

    private void BuildMarket(VBoxContainer dynamic)
    {
        // FreeCol's ReportTradePanel lists every tradeable good with its sale (bid) and purchase (ask) price plus
        // a boycott marker. Volume/income columns need accumulated trade history (no oracle yet), so they are
        // scoped out here — this is the price/boycott subset, read straight from the human's Market (ADR-006).
        List<GoodsType> goods = _game.Ruleset.GoodsTypes
            .Where(g => _game.Market.IsTradeable(g.Id))
            .ToList();
        if (goods.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No goods are tradeable.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        dynamic.AddChild(new Label { Name = "MarketHeader", Text = "Good — sell / buy (gold per unit)" });
        foreach (GoodsType g in goods)
        {
            int bid = _game.Market.BidPrice(g.Id);
            int ask = _game.Market.AskPrice(g.Id);
            string boycott = _game.Market.CanTrade(g.Id)
                ? ""
                : $"  ·  BOYCOTT (arrears {_game.Market.Arrears(g.Id)} gold)";
            dynamic.AddChild(new Label
            {
                Name = $"Market_{Strip(g.Id)}",
                Text = $"{g.ShortName} — sell {bid} / buy {ask}{boycott}",
            });
        }
    }

    // ── Continental Congress tab (faithful subset: the founding-father election state) ───────────────────

    private void BuildCongress(VBoxContainer dynamic)
    {
        // FreeCol's Continental Congress report: the father currently being recruited, the liberty (bells) banked
        // toward the next election, and the fathers presently on offer (one per category). Read-only — the actual
        // election lives in FoundingFatherPanel (ADR-006). Mirrors FoundingFatherPanel's reads exactly.
        string current = _game.CurrentFather is { } cf ? _game.Ruleset.Father(cf).ShortName : "(none)";
        dynamic.AddChild(new Label
        {
            Name = "CongressProgress",
            Text = $"Recruiting: {current}  ·  liberty {_game.Liberty} / {_game.TotalFoundingFatherCost()}",
        });
        dynamic.AddChild(new HSeparator());

        if (_game.OfferedFathers.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No fathers are on offer right now.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (string id in _game.OfferedFathers)
        {
            FoundingFather father = _game.Ruleset.Father(id);
            string recruiting = _game.CurrentFather == id ? "  — recruiting" : "";
            dynamic.AddChild(new Label
            {
                Name = $"Father_{father.ShortName}",
                Text = $"{father.ShortName}  ({father.Type}){recruiting}",
            });
        }
    }

    // ── History tab (the human's notable past events; FreeCol's ReportHistoryPanel) ──────────────────────

    private void BuildHistory(VBoxContainer dynamic)
    {
        // FreeCol's player history log: colonies founded, wars entered, fathers elected — in turn order, already
        // formatted to player-facing strings by GameLogic. In-memory only this wave (not saved); a reloaded game's
        // history begins empty (a header note flags that). Read-only over Game.History (ADR-006).
        dynamic.AddChild(new Label
        {
            Name = "HistoryNote",
            Text = "Notable events this game (not carried across save/load yet):",
        });

        if (_game.History.Count == 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "HistoryEmpty",
                Text = "Nothing of note has happened yet.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        int i = 0;
        foreach (HistoryEvent e in _game.History)
        {
            dynamic.AddChild(new Label
            {
                Name = $"History_{i++}",
                Text = $"Turn {e.Turn}: {e.Description}",
            });
        }
    }

    /// <summary>The readable tail of a <c>model.*.foo</c> id (e.g. <c>model.nation.dutch</c> → <c>dutch</c>).</summary>
    private static string Strip(string? id) => id is null ? "?" : id[(id.LastIndexOf('.') + 1)..];

    /// <summary>Title-cases a camelCase short name for label text (e.g. <c>expertFarmer</c> → "Expert Farmer"); presentation-only (ADR-006, no model data).</summary>
    private static string Display(string shortName)
    {
        var sb = new System.Text.StringBuilder(shortName.Length + 4);
        for (int i = 0; i < shortName.Length; i++)
        {
            char c = shortName[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(shortName[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }

    private static string Signed(int n) => (n > 0 ? "+" : "") + n;
}
