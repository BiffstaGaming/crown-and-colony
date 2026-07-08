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
/// <item><b>Requirements</b> (`86d3e4buh` — FreeCol's ReportRequirementsPanel): per colony, the production
/// shortfalls — a worked tile / manned building whose good wants an expert that isn't doing the job (bad assignment),
/// or that no expert in the colony produces (missing expert). Reads the colony worker overlays + the ruleset's
/// <see cref="Specification.Ruleset.ExpertForProducing"/> good→expert map; "All requirements met" when a colony has none.</item>
/// <item><b>Military</b> (`86d3e4buh` — FreeCol's ReportMilitaryPanel): the human's land/naval unit counts and combined
/// land attack power (<see cref="Game.HumanMilitaryStrength"/>) shown beside the Royal Expeditionary Force the King
/// would send (<see cref="Game.ExpeditionaryForceStrength"/>) — the strength comparison ahead of independence.</item>
/// <item><b>Foreign</b> (`86d3c9x3c` / `86d3f0wcg` / `86d3ha4cv` — FreeCol's ReportForeignAffairPanel): one row per rival
/// colonial power with its stance, colony/unit counts, gold, and — shown unconditionally, like FreeCol's
/// <see cref="NationSummary"/> — its combined land + naval attack strength (<see cref="Game.ColonialStrength"/>). When
/// the human holds <b>Jan de Witt</b> (<see cref="Game.HasBetterForeignAffairsReport"/>), each row also carries the
/// rival's SoL%, elected-father count and tax% (the <see cref="Game.ForeignReportRows"/> oracle) — FreeCol's de-Witt
/// gate, faithfully reproduced (those columns are absent for a Congress without de Witt).</item>
/// <item><b>Natives</b> (`86d3c9x3c` / `86d3f0wav` — FreeCol's ReportIndianPanel): the discovered settlements grouped
/// under their owning native NATION, each nation a header (name + settlement count + the tribe-wide tension band/stance
/// toward the human) over its settlement rows; each settlement shows its alarm, the colonial nation it
/// <b>most hates</b> (<see cref="Natives.NativeSettlement.MostHated"/>) or "—", its teachable skill, mission and
/// wanted goods.</item>
/// <item><b>Religion</b> (`86d3c9x3c`): the crosses-to-immigration bar and the per-colony church/cathedral cross
/// producers.</item>
/// <item><b>Trade</b> (`86d3e4buh` — FreeCol's ReportTradePanel): every tradeable good's current sell (bid) / buy (ask)
/// price, plus its cumulative <b>net units sold</b> and <b>income before & after tax</b> (B4's market trade counters —
/// <see cref="Trade.Market.SalesOf"/> / <see cref="Trade.Market.IncomeBeforeTaxesOf"/> /
/// <see cref="Trade.Market.IncomeAfterTaxesOf"/>), plus a boycott marker. Read straight from the human's <see cref="Game.Market"/>.</item>
/// <item><b>Exploration</b> (`86d3e4buh` — FreeCol's ReportExplorationPanel): the regions the human has discovered
/// (<see cref="Game.Map"/>'s <see cref="World.GameMap.Regions"/> filtered to the human's), newest first, with each
/// region's type, discovery turn and score.</item>
/// <item><b>Congress</b> (`86d3c9x53` — FreeCol's ReportReligiousPanel sibling, the Continental Congress facet):
/// the founding-father election state — the father currently being recruited (<see cref="Game.CurrentFather"/>),
/// the liberty (bells) progress (<see cref="Game.Liberty"/> / <see cref="Game.TotalFoundingFatherCost"/>), and one
/// row per offered father (<see cref="Game.OfferedFathers"/>) with its category, marking the in-progress pick.
/// Read-only — election lives in <see cref="FoundingFatherPanel"/>.</item>
/// <item><b>History</b> (`86d3c9x53` / `86d3fq0h4` — FreeCol's ReportHistoryPanel facet): the human's notable past events
/// (<see cref="Game.History"/>) in turn order — colonies founded, wars entered, Founding Fathers elected, regions
/// discovered, settlements razed. The event log is <b>persisted</b> (save v58), so a reloaded game's history survives.</item>
/// <item><b>Demographics</b> (`86d3fq27v` — Sid Meier's Colonization's end-game demographics screen; FreeCol's
/// score/history trend reporting): the human nation's year-by-year series (<see cref="Game.Demographics"/>) — population,
/// gold and score over time — as a code-drawn line graph (<see cref="DemographicGraph"/>). The series is recorded at each
/// year-rollover and <b>persisted</b> (save v65), so the trend survives a save/load round-trip.</item>
/// </list>
/// Pure presentation (ADR-006) — reads <see cref="Game"/> oracles only, never mutates. Built programmatically into
/// the fixed <c>VBox/Dynamic</c> shell.
/// </summary>
public partial class ColonyReportPanel : PanelContainer
{
    private enum Tab { Colonies, Units, Education, Production, Labour, Requirements, Military, Naval, Cargo, Ref, Foreign, Natives, Religion, Trade, Score, Exploration, Congress, History, Demographics }

    private Game _game = null!;
    private Tab _tab = Tab.Colonies;

    /// <summary>
    /// The report → Colopedia deep-link (<c>86d3fymc5</c>; FreeCol's <c>ReportPanel.showColopediaPanel</c>): invoked with
    /// a Colopedia category + an entity anchor (a ruleset <c>ShortName</c>) when the human clicks a report entity cell
    /// (a colony's build, a trade good, a native settlement's skill, a unit type). Injected by <c>GameController</c>
    /// (which hides this report and calls <c>ColopediaPanel.OpenTo</c>); <c>null</c> when unwired (tests that open the
    /// panel directly), in which case the entity cells render as plain, non-clickable labels.
    /// </summary>
    private System.Action<ColopediaPanel.Category, string>? _openColopedia;

    /// <summary>The good the Production tab is currently breaking down (its ruleset id); null = "(nothing selected)".</summary>
    private string? _productionGood;

    /// <summary>The unit type the Labour tab is currently drilling into (its ruleset id); null = no drill-down (the flat list).</summary>
    private string? _labourDetailType;

    private static readonly System.Collections.Generic.Dictionary<Tab, string> Titles = new()
    {
        [Tab.Colonies] = "Colony report",
        [Tab.Units] = "Unit report",
        [Tab.Education] = "Education",
        [Tab.Production] = "Production",
        [Tab.Labour] = "Labour",
        [Tab.Requirements] = "Requirements",
        [Tab.Military] = "Military",
        [Tab.Naval] = "Naval",
        [Tab.Cargo] = "Cargo",
        [Tab.Ref] = "Expeditionary Force",
        [Tab.Foreign] = "Foreign affairs",
        [Tab.Natives] = "Native nations",
        [Tab.Religion] = "Religion",
        [Tab.Trade] = "Trade",
        [Tab.Score] = "Score",
        [Tab.Exploration] = "Exploration",
        [Tab.Congress] = "Continental Congress",
        [Tab.History] = "History",
        [Tab.Demographics] = "Demographics",
    };

    /// <summary>Opens the reports screen on the Colonies tab over the current game state.</summary>
    /// <param name="game">The current game (read-only oracle source; ADR-006).</param>
    /// <param name="openColopedia">
    /// Optional report → Colopedia deep-link (<c>86d3fymc5</c>): when supplied, report entity cells become link buttons
    /// that invoke it with the target category + anchor; when <c>null</c> they render as plain labels.
    /// </param>
    /// <param name="congressName">The variant's name for the father-electing body (classic "Continental Congress", Australia "Federation Convention" — 86d3kwtjb); titles the Congress tab.</param>
    /// <param name="displayOverrides">The active variant's display-name overrides (<see cref="GameLogic.Specification.GameVariant.DisplayOverrides"/>) — Australia renames goods/units; <c>null</c>/empty (classic) leaves names classic.</param>
    /// <param name="nativesName">The variant's chrome word for the indigenous nations (classic "Native nations", Australia "First Nations"); titles the Natives tab.</param>
    /// <param name="expeditionaryForceName">The variant's name for the Crown's army (classic "Royal Expeditionary Force", Australia "Imperial Pressure"); the REF/Military headers.</param>
    public void Open(Game game, System.Action<ColopediaPanel.Category, string>? openColopedia = null, string congressName = "Continental Congress",
        IReadOnlyDictionary<string, string>? displayOverrides = null, string nativesName = "Native nations",
        string expeditionaryForceName = "Royal Expeditionary Force")
    {
        ColonyArt.FramePanel(this, dense: true); // parchment image frame + dark-ink in-game theme (not Godot's default gray box)
        _game = game;
        _openColopedia = openColopedia;
        _congressName = congressName;
        _displayOverrides = displayOverrides;
        _nativesName = nativesName;
        _expeditionaryForceName = expeditionaryForceName;
        _tab = Tab.Colonies;
        Rebuild();
        Show();
    }

    private string _congressName = "Continental Congress";
    private IReadOnlyDictionary<string, string>? _displayOverrides;
    private string _nativesName = "Native nations";
    private string _expeditionaryForceName = "Royal Expeditionary Force";

    private void Rebuild()
    {
        GetNode<Label>("VBox/ReportTitle").Text = _tab switch
        {
            Tab.Congress => _congressName,   // "Continental Congress" / "Federation Convention" (86d3kwtjb)
            Tab.Natives => _nativesName,     // "Native nations" / "First Nations" (86d3kwu0c)
            _ => Titles[_tab],
        };
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            // Detach immediately (so the freshly-built tab row below never collides with a stale one) but QUEUE the
            // free: a tab button's Pressed handler calls Rebuild(), so freeing that button synchronously would free it
            // mid-signal ("object freed while a signal is being emitted"). RemoveChild + QueueFree is the Godot-safe idiom.
            dynamic.RemoveChild(child);
            child.QueueFree();
        }

        // Tab row (named buttons for the L3 tests; the active tab is disabled). An HFlowContainer wraps the row onto a
        // second line rather than overflowing the panel — there are now more report tabs than fit one line.
        var tabs = new HFlowContainer { Name = "Tabs" };
        tabs.AddChild(TabButton("Colonies", Tab.Colonies));
        tabs.AddChild(TabButton("Units", Tab.Units));
        tabs.AddChild(TabButton("Education", Tab.Education));
        tabs.AddChild(TabButton("Production", Tab.Production));
        tabs.AddChild(TabButton("Labour", Tab.Labour));
        tabs.AddChild(TabButton("Requirements", Tab.Requirements));
        tabs.AddChild(TabButton("Military", Tab.Military));
        tabs.AddChild(TabButton("Naval", Tab.Naval));
        tabs.AddChild(TabButton("Cargo", Tab.Cargo));
        tabs.AddChild(TabButton("REF", Tab.Ref));
        tabs.AddChild(TabButton("Foreign", Tab.Foreign));
        tabs.AddChild(TabButton("Natives", Tab.Natives));
        tabs.AddChild(TabButton("Religion", Tab.Religion));
        tabs.AddChild(TabButton("Trade", Tab.Trade));
        tabs.AddChild(TabButton("Score", Tab.Score));
        tabs.AddChild(TabButton("Exploration", Tab.Exploration));
        tabs.AddChild(TabButton("Congress", Tab.Congress));
        tabs.AddChild(TabButton("History", Tab.History));
        tabs.AddChild(TabButton("Demographics", Tab.Demographics));
        dynamic.AddChild(tabs);
        dynamic.AddChild(new HSeparator());

        switch (_tab)
        {
            case Tab.Colonies: BuildColonies(dynamic); break;
            case Tab.Units: BuildUnits(dynamic); break;
            case Tab.Education: BuildEducation(dynamic); break;
            case Tab.Production: BuildProduction(dynamic); break;
            case Tab.Labour: BuildLabour(dynamic); break;
            case Tab.Requirements: BuildRequirements(dynamic); break;
            case Tab.Military: BuildMilitary(dynamic); break;
            case Tab.Naval: BuildNaval(dynamic); break;
            case Tab.Cargo: BuildCargo(dynamic); break;
            case Tab.Ref: BuildRef(dynamic); break;
            case Tab.Foreign: BuildForeign(dynamic); break;
            case Tab.Natives: BuildNatives(dynamic); break;
            case Tab.Religion: BuildReligion(dynamic); break;
            case Tab.Trade: BuildTrade(dynamic); break;
            case Tab.Score: BuildScore(dynamic); break;
            case Tab.Exploration: BuildExploration(dynamic); break;
            case Tab.Congress: BuildCongress(dynamic); break;
            case Tab.History: BuildHistory(dynamic); break;
            case Tab.Demographics: BuildDemographics(dynamic); break;
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
            // The header carries the per-colony name + summary; named for the L3 test to find by colony id. When the
            // colony is building something it becomes a Colopedia deep-link to that build item's entry (86d3fymc5 —
            // FreeCol's report cells open the colopedia); with an idle build queue there is no entry to point at, so it
            // stays a plain label.
            string headerText = $"{c.Name} — pop {c.Population}, SoL {c.SonsOfLiberty}%{bonus}";
            if (c.CurrentBuild is { } buildId && _game.DescribeBuildable(buildId) is { } build)
            {
                dynamic.AddChild(EntityLink(
                    $"Colony_{c.Id}",
                    headerText,
                    build.IsUnit ? ColopediaPanel.Category.Units : ColopediaPanel.Category.Buildings,
                    build.ShortName));
            }
            else
            {
                dynamic.AddChild(new Label { Name = $"Colony_{c.Id}", Text = headerText });
            }
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
            .Select(o => $"{Display(_game.Ruleset.Goods(o.GoodsId).ShortName)} {colony.StoreOf(o.GoodsId)}/{o.Amount}");
        string needs = reqs.Any() ? $" (needs {string.Join(", ", reqs)})" : "";
        return $"{Display(info.ShortName)}{needs}";
    }

    /// <summary>The colony's non-food net production, signed and named, or "nothing".</summary>
    private string ProductionSummary(IReadOnlyDictionary<string, int> net)
    {
        List<string> parts = net
            .Where(kv => kv.Key != Colony.FoodId && kv.Value != 0)
            .OrderBy(kv => kv.Key, System.StringComparer.Ordinal)
            .Select(kv => $"{Signed(kv.Value)} {Display(_game.Ruleset.Goods(kv.Key).ShortName)}")
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
        string role = u.HasDefaultRole ? "" : $" ({Display(_game.Ruleset.Role(u.RoleId).Id[(u.RoleId.LastIndexOf('.') + 1)..])})";
        return $"{Display(u.Type.ShortName)}{role}";
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
            .Select(kv => $"{kv.Value} {Display(_game.Ruleset.Goods(kv.Key).ShortName)}"));
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
            _labourDetailType = null; // nothing to drill into
            return;
        }

        // The unit types present in the roster, in stable order — the drill-down selector's items (FreeCol's
        // ReportLabourPanel lets you click a type to open its ReportLabourDetailPanel).
        List<string> types = rows
            .Select(r => r.Type)
            .Distinct()
            .OrderBy(t => t, System.StringComparer.Ordinal)
            .ToList();
        if (_labourDetailType is { } sel && !types.Contains(sel))
        {
            _labourDetailType = null; // a previously drilled-into type the human no longer has any of
        }

        // Drill-down selector: "(all colonists)" leaves the flat list; picking a type shows the per-location breakdown.
        var selector = new OptionButton { Name = "LabourDetailSelector" };
        selector.AddItem("(all colonists)", -1);
        for (int i = 0; i < types.Count; i++)
        {
            selector.AddItem(Display(Strip(types[i])), i);
            if (types[i] == _labourDetailType)
            {
                selector.Select(selector.ItemCount - 1);
            }
        }
        selector.ItemSelected += index =>
        {
            int id = (int)selector.GetItemId((int)index);
            _labourDetailType = id >= 0 ? types[id] : null;
            Rebuild();
        };
        dynamic.AddChild(selector);
        dynamic.AddChild(new HSeparator());

        // Drill-down view (FreeCol ReportLabourDetailPanel): for the chosen type, where those colonists are, by location.
        if (_labourDetailType is { } detailType)
        {
            var detail = _game.LabourDetail(detailType);
            int total = detail.Sum(l => l.Count);
            dynamic.AddChild(new Label
            {
                Name = $"LabourDetail_{Strip(detailType)}",
                Text = $"— {Display(Strip(detailType))}: {total} total —",
            });
            foreach (Game.LabourLocation loc in detail)
            {
                dynamic.AddChild(new Label { Text = $"    {loc.Location}: {loc.Count}" });
            }
            return;
        }

        // Flat list: grouped by unit type (FreeCol's per-type tally), each group a named header then its colonists.
        foreach (IGrouping<string, (string Type, string Location, string Job)> group in rows
            .GroupBy(r => r.Type)
            .OrderBy(g => g.Key, System.StringComparer.Ordinal))
        {
            // Each unit-type group header deep-links to that unit's Colopedia entry (86d3fymc5).
            dynamic.AddChild(EntityLink(
                $"Labour_{Strip(group.Key)}",
                $"— {Display(Strip(group.Key))} ({group.Count()}) —",
                ColopediaPanel.Category.Units,
                _game.Ruleset.Unit(group.Key).ShortName));
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

        // FreeCol's NationSummary shows a rival's stance, #colonies, #units, military + naval strength and gold to
        // everyone unconditionally; SoL%, father count and tax% are hidden UNLESS the viewer holds Jan de Witt's
        // betterForeignAffairsReport (FreeCol NationSummary sets them −1 without it, and the panel omits the negative
        // columns). We reproduce that gate: HasBetterForeignAffairsReport(human) → append the extra columns from the
        // Game.ForeignReportRows oracle (86d3ha4cv), else leave them off. Strength is the combined land / naval attack
        // power (Game.ColonialStrength → FreeCol NationSummary.getMilitaryStrength/getNavalStrength), rounded to a whole
        // figure for the row. The tension/attitude band is the human's tension toward the rival, banded by the
        // Game.TensionLevelBetween oracle (FreeCol Tension.getLevel) — the report's attitude column.
        bool deWitt = _game.HasBetterForeignAffairsReport((int)human);
        // The de-Witt intelligence keyed by rival player id (only queried when the human holds the ability).
        Dictionary<int, Game.ForeignReportRow> extra = deWitt
            ? _game.ForeignReportRows((int)human).ToDictionary(r => r.Player.PlayerId)
            : new Dictionary<int, Game.ForeignReportRow>();
        foreach (Player p in powers)
        {
            Stance stance = _game.StanceBetween((int)human, p.PlayerId);
            AlarmLevel attitude = _game.TensionLevelBetween((int)human, p.PlayerId);
            int colonies = _game.Colonies.Count(c => c.OwnerId == p.PlayerId);
            int units = _game.Units.Count(u => u.OwnerId == p.PlayerId);
            (double landPower, double navalPower) = _game.ColonialStrength(p);
            // The de-Witt columns (SoL% · fathers · tax%) — appended only when the human holds the ability, faithful to
            // FreeCol's report omitting the negative NationSummary figures.
            string deWittColumns = deWitt && extra.TryGetValue(p.PlayerId, out Game.ForeignReportRow row)
                ? $"  ·  SoL {row.SonsOfLiberty}%, fathers {row.FoundingFathers}, tax {row.TaxRate}%"
                : "";
            dynamic.AddChild(new Label
            {
                Name = $"Foreign_{p.PlayerId}",
                Text = $"{Display(Strip(p.NationId))} — {stance}, attitude {attitude}, {colonies} colonies, {units} units  ·  " +
                       $"military {landPower:0.#}, naval {navalPower:0.#}  ·  {p.Gold} gold{deWittColumns}",
            });
        }
    }

    // ── Native nations tab ───────────────────────────────────────────────────────────────────────────────

    private void BuildNatives(VBoxContainer dynamic)
    {
        // FreeCol's ReportIndianPanel (the Native Affairs Advisor) groups the discovered settlements under their owning
        // native NATION: a per-nation header (nation name + settlement count + the tribe-wide tension/stance toward the
        // human) over one row per settlement, each showing the colonial nation it most hates (FreeCol getMostHated).
        // We read settlements + Game.NativeSettlements (ADR-006) and resolve MostHated (NativeSettlement.MostHated, an
        // int? player id Game recomputes every turn) to that rival's nation via Game.Players.
        List<NativeSettlement> discovered = _game.NativeSettlements
            .Where(s => _game.IsExplored(s.Position))
            .ToList();
        if (discovered.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No native settlements discovered yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (IGrouping<string, NativeSettlement> nation in discovered
            .GroupBy(s => s.NationTypeId)
            .OrderBy(g => g.Key, System.StringComparer.Ordinal))
        {
            // Tribe-wide tension toward the human (channel 0): the worst (most alarmed) band across the nation's
            // discovered settlements — our per-settlement-alarm model has no nation-level tension scalar, so the
            // advisor reports the angriest known settlement's band (and a stance derived from it; natives don't carry a
            // colonial stance in this model). Settlement "known/total" is approximated by the discovered count (FreeCol
            // reads NationSummary.getNumberOfSettlements; we have no such summary, so total == discovered here).
            List<NativeSettlement> nationSettlements = nation
                .OrderByDescending(s => s.IsCapital) // FreeCol lists the capital first
                .ThenBy(s => s.Position.Y).ThenBy(s => s.Position.X)
                .ToList();
            // The tribe-wide tension band toward the human (FreeCol the native player's Tension) — the distinct
            // nation-level channel, not the angriest single settlement (86d3fpzkq).
            AlarmLevel tribeAlarm = _game.TribeAlarmLevelFor(nation.Key, _game.HumanPlayer.PlayerId);
            // The nation-level WAR / cease-fire / peace stance toward the human — the authoritative derived stance
            // (Game.NativeStanceToward, FreeCol determineStances hysteresis), NOT a naive banding of the current alarm,
            // so it de-escalates through cease-fire as tension cools rather than snapping with the band (86d3fpzqf).
            Stance tribeStance = _game.NativeStanceToward(nation.Key, _game.HumanPlayer.PlayerId);
            dynamic.AddChild(new Label
            {
                Name = $"NativeNation_{Strip(nation.Key)}",
                Text = $"{Display(Strip(nation.Key))} — {nationSettlements.Count} settlement(s)  ·  " +
                       $"tension {tribeAlarm} ({NativeStanceLabel(tribeStance)})",
            });

            foreach (NativeSettlement s in nationSettlements)
            {
                string capital = s.IsCapital ? "★" : "";
                string skill = s.LearnableSkill is { } sk ? Strip(sk) : "none";
                string mission = s.HasMission ? "  ·  mission" : "";
                string wanted = s.WantedGoods.Count > 0
                    ? "  ·  wants " + string.Join(", ", s.WantedGoods.Select(Strip))
                    : "";
                string settlementText =
                    $"    {Display(Strip(s.NationTypeId))}{capital} ({s.Position.X},{s.Position.Y}) — alarm {s.AlarmLevel}, " +
                    $"most hated {MostHatedNation(s)}, teaches {skill}{mission}{wanted}";
                // A settlement teaching a skill deep-links to that skill's (a unit type) Colopedia entry (86d3fymc5);
                // a settlement with no teachable skill has no entry to point at, so it stays a plain label.
                if (s.LearnableSkill is { } skillId)
                {
                    dynamic.AddChild(EntityLink(
                        $"Native_{s.Position.X}_{s.Position.Y}",
                        settlementText,
                        ColopediaPanel.Category.Units,
                        _game.Ruleset.Unit(skillId).ShortName));
                }
                else
                {
                    dynamic.AddChild(new Label { Name = $"Native_{s.Position.X}_{s.Position.Y}", Text = settlementText });
                }
            }
            dynamic.AddChild(new HSeparator());
        }
    }

    /// <summary>The colonial nation a settlement most hates (FreeCol <c>getMostHated</c>), resolved to a readable nation name, or "—" when none.</summary>
    private string MostHatedNation(NativeSettlement settlement)
    {
        if (settlement.MostHated is not { } hatedId)
        {
            return "—";
        }
        Player? hated = _game.Players.FirstOrDefault(p => p.PlayerId == hatedId);
        return hated?.NationId is { } nationId ? Display(Strip(nationId)) : "—";
    }

    /// <summary>A presentation-derived tribe stance from its worst alarm band (our natives carry no colonial stance): hateful → at war, displeased/angry → uneasy, else at peace.</summary>
    private static string NativeStanceLabel(Stance stance) => stance switch
    {
        Stance.War => "at war",
        Stance.CeaseFire => "cease-fire",
        _ => "at peace",
    };

    // ── Religion tab (FreeCol ReportReligiousPanel: the immigration bar + per-church cross producers) ─────

    private void BuildReligion(VBoxContainer dynamic)
    {
        // FreeCol's religion report: the crosses-toward-immigration progress bar, then for each colony the building(s)
        // that produce crosses (church/cathedral) with their cross output. We read the immigration bar from the human
        // and the per-church output from each colony's net production + building staffing (ADR-006 reads only).
        dynamic.AddChild(new Label
        {
            Name = "ReligionImmigration",
            Text = $"Crosses toward the next emigrant: {_game.Immigration} / {_game.ImmigrationRequired}",
        });
        dynamic.AddChild(new Label { Text = $"Recruit a waiting emigrant now: {_game.RecruitPrice} gold" });
        dynamic.AddChild(new HSeparator());

        // Per-church cross breakdown: each human colony's churches/cathedrals and the crosses they make.
        const string crossesId = "model.goods.crosses";
        if (!_game.Ruleset.GoodsTypes.Any(g => g.Id == crossesId))
        {
            return; // a ruleset with no crosses good (defensive — classic always has it)
        }

        List<Colony> colonies = HumanColonies();
        bool anyChurch = false;
        foreach (Colony c in colonies)
        {
            List<string> churches = c.Buildings
                .Where(b => _game.Ruleset.Building(b).Productions
                    .Any(p => p.Outputs.Any(o => o.GoodsId == crossesId)))
                .ToList();
            if (churches.Count == 0)
            {
                continue;
            }
            anyChurch = true;
            int colonyCrosses = _game.ColonyNetProduction(c).GetValueOrDefault(crossesId);
            dynamic.AddChild(new Label
            {
                Name = $"Church_{c.Id}",
                Text = $"{c.Name} — {Signed(colonyCrosses)} crosses/turn",
            });
            foreach (string buildingId in churches)
            {
                BuildingType church = _game.Ruleset.Building(buildingId);
                int workers = c.BuildingWorkers.GetValueOrDefault(buildingId);
                dynamic.AddChild(new Label { Text = $"    {Display(church.ShortName)}: {workers}/{church.Workplaces} preachers" });
            }
        }
        if (!anyChurch)
        {
            dynamic.AddChild(new Label
            {
                Name = "ReligionNoChurch",
                Text = "No colony is producing crosses (every colony's town hall aside).",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }

    // ── Trade tab (FreeCol ReportTradePanel: per-good prices + cumulative units sold + income before/after tax) ──

    private void BuildTrade(VBoxContainer dynamic)
    {
        // FreeCol's ReportTradePanel shows, per tradeable good, its current sale/purchase price plus the cumulative
        // units sold and the income before & after taxes. The volume/income columns are B4's market trade counters —
        // SalesOf / IncomeBeforeTaxesOf / IncomeAfterTaxesOf — read straight from the human's Market (ADR-006); each is
        // a NET total (a sale adds, a buy back subtracts). A boycott marker follows FreeCol's warn colour.
        List<GoodsType> goods = _game.Ruleset.GoodsTypes
            .Where(g => _game.Market.IsTradeable(g.Id))
            .ToList();
        if (goods.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No goods are tradeable.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        // Player-wide totals (FreeCol's report.trade.afterTaxes summary line).
        int totalAfterTax = goods.Sum(g => _game.Market.IncomeAfterTaxesOf(g.Id));
        dynamic.AddChild(new Label
        {
            Name = "TradeHeader",
            Text = "Good — sell / buy price · units sold · income before / after tax",
        });
        dynamic.AddChild(new Label { Name = "TradeTotal", Text = $"Net trade income after tax (all goods): {totalAfterTax} gold" });
        dynamic.AddChild(new HSeparator());

        foreach (GoodsType g in goods)
        {
            int bid = _game.Market.BidPrice(g.Id);
            int ask = _game.Market.AskPrice(g.Id);
            int sold = _game.Market.SalesOf(g.Id);
            int before = _game.Market.IncomeBeforeTaxesOf(g.Id);
            int after = _game.Market.IncomeAfterTaxesOf(g.Id);
            string boycott = _game.Market.CanTrade(g.Id)
                ? ""
                : $"  ·  BOYCOTT (arrears {_game.Market.Arrears(g.Id)} gold)";
            // The good is a Colopedia deep-link (86d3fymc5): click it to open the Goods entry (FreeCol report → colopedia).
            dynamic.AddChild(EntityLink(
                $"Trade_{Strip(g.Id)}",
                $"{Display(g.ShortName)} — sell {bid} / buy {ask}  ·  sold {sold}  ·  income {before} / {after}{boycott}",
                ColopediaPanel.Category.Goods,
                g.ShortName));
        }
    }

    // ── Exploration tab (FreeCol ReportExplorationPanel: discovered regions, newest first) ────────────────

    private void BuildExploration(VBoxContainer dynamic)
    {
        // FreeCol's exploration report lists every discovered region with its name, type, the turn discovered and a
        // score. We read the map's region table (Game.Map.Regions), filtered to those the HUMAN discovered, ordered
        // newest-first then by score (FreeCol's regionComparator). Reads only (ADR-006) — discovery is GameLogic's.
        List<Region> regions = _game.Map.Regions
            .Where(r => r.IsDiscovered && r.DiscoveredBy == _game.HumanPlayer.PlayerId)
            .OrderByDescending(r => r.DiscoveredInTurn ?? 0)
            .ThenByDescending(r => r.ScoreValue)
            .ThenBy(r => r.Id)
            .ToList();
        if (regions.Count == 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "ExplorationEmpty",
                Text = "You have not discovered any named regions yet.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        dynamic.AddChild(new Label { Name = "ExplorationHeader", Text = "Region — type · discovered turn · score" });
        foreach (Region r in regions)
        {
            dynamic.AddChild(new Label
            {
                Name = $"Region_{r.Id}",
                Text = $"{r.Name} — {r.Type} · turn {r.DiscoveredInTurn} · {r.ScoreValue} pts",
            });
        }
    }

    // ── Requirements tab (FreeCol ReportRequirementsPanel: expert mis-assignment / missing-expert warnings) ──

    private void BuildRequirements(VBoxContainer dynamic)
    {
        // FreeCol's ReportRequirementsPanel warns, per colony, where production is sub-optimal: a non-expert doing a
        // job a resident expert should do (bad assignment), a job done with no expert anywhere in the colony (missing
        // expert), a manned building below max output because an input good is net-short (production shortage), and a
        // worked tile that would gain from a plow/road or is still unexplored (tile suggestions). We read the colony's
        // worker overlays + Ruleset.ExpertForProducing plus the Game.ColonyProductionWarnings /
        // ColonyTileImprovementSuggestions oracles (pure reads, rules live in GameLogic; ADR-006). "Requirements met"
        // when a colony has none.
        List<Colony> colonies = HumanColonies();
        if (colonies.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no colonies yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (Colony c in colonies)
        {
            dynamic.AddChild(new Label { Name = $"Requirements_{c.Id}", Text = $"{c.Name}" });
            var warnings = new List<string>();

            // Each worked tile: the good it farms wants an expert; warn if the worker isn't that expert.
            foreach ((Position tile, string good) in c.TileWorkers.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
            {
                if (_game.Ruleset.ExpertForProducing(good) is not { } expert)
                {
                    continue;
                }
                string worker = c.WorkerTypeAt(tile);
                if (worker != expert)
                {
                    warnings.Add(MissingOrMisassigned(c, good, expert, worker));
                }
            }
            // Each manned building output: warn when no expert for that output works the colony.
            foreach (string buildingId in c.Buildings)
            {
                BuildingType b = _game.Ruleset.Building(buildingId);
                if (b.Teaches)
                {
                    continue; // teachers are staff, not producers
                }
                int workers = c.BuildingWorkers.GetValueOrDefault(buildingId);
                if (workers == 0)
                {
                    continue;
                }
                foreach (ProductionEntry prod in b.Productions)
                {
                    foreach (GoodsOutput output in prod.Outputs)
                    {
                        if (_game.Ruleset.ExpertForProducing(output.GoodsId) is not { } expert)
                        {
                            continue;
                        }
                        if (!c.BuildingOccupants(buildingId).Contains(expert))
                        {
                            warnings.Add(
                                $"    {Display(b.ShortName)} makes {Display(_game.Ruleset.Goods(output.GoodsId).ShortName)} " +
                                $"with no {Display(Strip(expert))}.");
                        }
                    }
                }
            }

            // Production shortage: a manned building below max output because an input good is net-negative colony-wide
            // (FreeCol's "not enough input" warning). Read from the Game.Reports oracle — the panel stays rules-free.
            foreach (Game.ColonyProductionWarning pw in _game.ColonyProductionWarnings(c))
            {
                warnings.Add(
                    $"    {Display(_game.Ruleset.Goods(pw.OutputGoodsId).ShortName)} production is short of " +
                    $"{Display(_game.Ruleset.Goods(pw.InputGoodsId).ShortName)}.");
            }
            // Tile-improvement suggestions: a worked tile that would gain from a plow/road, or an unexplored worked tile.
            foreach (Game.TileImprovementSuggestion tis in _game.ColonyTileImprovementSuggestions(c))
            {
                warnings.Add(tis.ImprovementId is { } impId
                    ? $"    The tile at ({tis.Tile.X}, {tis.Tile.Y}) would yield more with a {Display(Strip(impId))}."
                    : $"    The tile at ({tis.Tile.X}, {tis.Tile.Y}) is unexplored — send a scout.");
            }

            foreach (string w in warnings.Distinct())
            {
                dynamic.AddChild(new Label { Name = $"RequirementWarning_{c.Id}", Text = w, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            }
            if (warnings.Count == 0)
            {
                dynamic.AddChild(new Label { Name = $"RequirementsMet_{c.Id}", Text = "    All requirements met." });
            }
            dynamic.AddChild(new HSeparator());
        }
    }

    /// <summary>A bad-assignment / missing-expert warning line for a worked tile (FreeCol's two requirement messages).</summary>
    private string MissingOrMisassigned(Colony colony, string good, string expert, string worker)
    {
        string goodName = Display(_game.Ruleset.Goods(good).ShortName);
        // A resident expert sitting elsewhere → bad assignment; no expert at all → missing expert.
        bool expertPresent = colony.TileWorkers.Values.Contains(expert)
            || colony.Buildings.Any(b => colony.BuildingOccupants(b).Contains(expert));
        return expertPresent
            ? $"    {goodName}: worked by a {Display(Strip(worker))}, but a {Display(Strip(expert))} could do it better."
            : $"    {goodName}: no {Display(Strip(expert))} to work it.";
    }

    // ── Military tab (FreeCol ReportMilitaryPanel: strength totals + REF comparison) ──────────────────────

    private void BuildMilitary(VBoxContainer dynamic)
    {
        // FreeCol's ReportMilitaryPanel tallies the player's offensive forces and shows the Royal Expeditionary Force
        // beside them. We read the strength oracles (Game.HumanMilitaryStrength / ExpeditionaryForceStrength; ADR-006)
        // — combined land attack power + land/naval unit counts for the human, and the REF's land/naval unit counts.
        (double landPower, int land, int naval) = _game.HumanMilitaryStrength();
        dynamic.AddChild(new Label { Name = "MilitaryTitle", Text = "Your forces" });
        dynamic.AddChild(new Label
        {
            Name = "MilitaryHuman",
            Text = $"    Land units: {land}  ·  Naval units: {naval}  ·  Land attack power: {landPower:0.#}",
        });
        dynamic.AddChild(new HSeparator());

        (int refLand, int refNaval) = _game.ExpeditionaryForceStrength();
        dynamic.AddChild(new Label { Name = "MilitaryRefTitle", Text = $"{_expeditionaryForceName} (the army the King would send)" });
        dynamic.AddChild(new Label
        {
            Name = "MilitaryRef",
            Text = $"    Land units: {refLand}  ·  Naval units: {refNaval}",
        });
    }

    // ── Naval tab (FreeCol ReportNavalPanel: the standalone fleet breakdown) ──────────────────────────────

    private void BuildNaval(VBoxContainer dynamic)
    {
        // FreeCol's ReportNavalPanel lists the player's ships on their own (the Military tab folds naval into a count).
        // We list every naval unit with its type/role, location and hold contents (CargoSummary), and a fleet total.
        // Reads only (ADR-006): Game.PlayerUnits filtered to naval types.
        List<Unit> ships = _game.PlayerUnits.Where(u => u.Type.IsNaval).OrderBy(u => u.Id).ToList();
        dynamic.AddChild(new Label { Name = "NavalHeader", Text = $"Your fleet — {ships.Count} ship(s)" });
        dynamic.AddChild(new HSeparator());
        if (ships.Count == 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "NavalEmpty",
                Text = "You have no ships.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }
        foreach (Unit u in ships)
        {
            dynamic.AddChild(new Label
            {
                Name = $"Ship_{u.Id}",
                Text = $"{TypeAndRole(u)}  ·  {Where(u)}{CargoSummary(u)}",
            });
        }
    }

    // ── Cargo tab (FreeCol ReportCargoPanel: where goods/units are loaded across the carriers) ────────────

    private void BuildCargo(VBoxContainer dynamic)
    {
        // FreeCol's ReportCargoPanel shows each carrier (ships + treasure wagons) and what it is hauling. We list every
        // carrier with its location and hold contents (CargoSummary), and call out the empty ones (FreeCol greys them).
        // Reads only (ADR-006): Game.PlayerUnits filtered to carriers / treasure-carriers.
        List<Unit> carriers = _game.PlayerUnits
            .Where(u => u.Type.IsCarrier || u.Type.CarryTreasure)
            .OrderBy(u => u.Id)
            .ToList();
        dynamic.AddChild(new Label { Name = "CargoHeader", Text = $"Your carriers — {carriers.Count}" });
        dynamic.AddChild(new HSeparator());
        if (carriers.Count == 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "CargoEmpty",
                Text = "You have no carriers.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }
        foreach (Unit u in carriers)
        {
            string cargo = CargoSummary(u);
            dynamic.AddChild(new Label
            {
                Name = $"Cargo_{u.Id}",
                Text = $"{TypeAndRole(u)}  ·  {Where(u)}{(cargo.Length > 0 ? cargo : "  [empty]")}",
            });
        }
    }

    // ── Expeditionary Force tab (FreeCol's REF intelligence: the King's army, by type, as it massess) ─────

    private void BuildRef(VBoxContainer dynamic)
    {
        // The REF intelligence report: the King's army growing against the player (via ADD_TO_REF), broken down by unit
        // type/role rather than the bare land/naval totals the Military tab shows. Reads only (ADR-006): the
        // Game.ExpeditionaryForceComposition oracle projects the REF Force's blocks; the panel just formats them.
        IReadOnlyList<Game.RefForceBlock> blocks = _game.ExpeditionaryForceComposition();
        int land = blocks.Where(b => !b.IsNaval).Sum(b => b.Count);
        int naval = blocks.Where(b => b.IsNaval).Sum(b => b.Count);
        dynamic.AddChild(new Label
        {
            Name = "RefHeader",
            Text = $"The {_expeditionaryForceName} currently massing: {land} land · {naval} naval",
        });
        dynamic.AddChild(new Label
        {
            Text = "This is the army the King will land if you declare independence. It grows as your rebel sentiment rises.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        dynamic.AddChild(new HSeparator());

        dynamic.AddChild(new Label { Name = "RefLandTitle", Text = "— Land —" });
        BuildRefBlocks(dynamic, blocks.Where(b => !b.IsNaval));
        dynamic.AddChild(new HSeparator());
        dynamic.AddChild(new Label { Name = "RefNavalTitle", Text = "— Naval —" });
        BuildRefBlocks(dynamic, blocks.Where(b => b.IsNaval));
    }

    /// <summary>Renders one section of REF blocks ("N × Type (role)"), or an em-dash placeholder when the section is empty.</summary>
    private void BuildRefBlocks(VBoxContainer dynamic, IEnumerable<Game.RefForceBlock> blocks)
    {
        var list = blocks.ToList();
        if (list.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "    —" });
            return;
        }
        foreach (Game.RefForceBlock b in list)
        {
            string type = Display(_game.Ruleset.Unit(b.UnitTypeId).ShortName);
            string role = b.RoleId is { } rid && !rid.EndsWith(".default")
                ? $" ({Display(_game.Ruleset.Role(rid).ShortName)})"
                : "";
            // Each REF block deep-links to its unit type's Colopedia entry (86d3fymc5).
            dynamic.AddChild(EntityLink(
                $"Ref_{Strip(b.UnitTypeId)}_{(b.RoleId is { } r ? Strip(r) : "none")}",
                $"    {b.Count} × {type}{role}",
                ColopediaPanel.Category.Units,
                _game.Ruleset.Unit(b.UnitTypeId).ShortName));
        }
    }

    // ── Score tab (the in-game score / nation ranking — FreeCol's score reports) ──────────────────────────

    private void BuildScore(VBoxContainer dynamic)
    {
        // The score / nation report: the human's itemised score (Game.ScoreBreakdown — units, liberty, fathers, gold,
        // history, independence bonus) over the colonial-power ranking by final score (Game.NationRanking). Reads only
        // (ADR-006): the engine computes every figure; the panel formats the lines.
        ScoreComponents sc = _game.ScoreBreakdown(_game.HumanPlayer);
        dynamic.AddChild(new Label { Name = "ScoreTotal", Text = $"Your score: {sc.Total} ({ScoreLevels.ForScore(sc.Total)})" });
        dynamic.AddChild(new Label { Text = $"    Units: {sc.UnitValues}  ·  Liberty: {sc.ColonyLiberty}  ·  Founding Fathers: {sc.FoundingFatherPoints}" });
        dynamic.AddChild(new Label { Text = $"    Gold: {sc.GoldPoints}  ·  History: {sc.HistoryPoints}" });
        if (sc.IndependenceBonusPercent > 0)
        {
            dynamic.AddChild(new Label { Text = $"    Independence bonus: +{sc.IndependenceBonus} ({sc.IndependenceBonusPercent}%)" });
        }
        dynamic.AddChild(new HSeparator());

        dynamic.AddChild(new Label { Name = "ScoreRankingHeader", Text = "Nation ranking — score · colonies · units" });
        IReadOnlyList<Game.NationStanding> ranking = _game.NationRanking();
        int rank = 1;
        foreach (Game.NationStanding s in ranking)
        {
            string you = s.Player.IsHuman ? "  (you)" : "";
            dynamic.AddChild(new Label
            {
                Name = $"Standing_{s.Player.PlayerId}",
                Text = $"{rank++}. {Display(Strip(s.Player.NationId))}{you} — {s.Score} pts · {s.ColonyCount} colonies · {s.UnitCount} units",
            });
        }
    }

    // ── Continental Congress tab (faithful subset: the founding-father election state) ───────────────────

    private void BuildCongress(VBoxContainer dynamic)
    {
        // FreeCol's Continental Congress report: the father currently being recruited, the liberty (bells) banked
        // toward the next election, and the fathers presently on offer (one per category). Read-only — the actual
        // election lives in FoundingFatherPanel (ADR-006). Mirrors FoundingFatherPanel's reads exactly.
        string current = _game.CurrentFather is { } cf ? Display(_game.Ruleset.Father(cf).ShortName) : "(none)";
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
                Name = $"Father_{father.ShortName}", // node name stays raw (test lookup)
                Text = $"{Display(father.ShortName)}  ({father.Type}){recruiting}",
            });
        }
    }

    // ── History tab (the human's notable past events; FreeCol's ReportHistoryPanel) ──────────────────────

    private void BuildHistory(VBoxContainer dynamic)
    {
        // FreeCol's player history log: colonies founded, wars entered, fathers elected, regions discovered,
        // settlements razed — in turn order, already formatted to player-facing strings by GameLogic. Persisted from
        // save v58, so the log survives a save/load round-trip. Read-only over Game.History (ADR-006).
        dynamic.AddChild(new Label
        {
            Name = "HistoryNote",
            Text = "Notable events this game:",
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

    // ── Demographics tab (the year-by-year trend graph; Col1's end-game demographics screen) ─────────────────

    private void BuildDemographics(VBoxContainer dynamic)
    {
        // The human nation's year-by-year series (population / gold / score), recorded at each year-rollover and persisted
        // from save v65, so the graph survives a save/load round-trip. Read-only over Game.Demographics (ADR-006). The
        // graph itself is drawn in code by DemographicGraph._Draw — deterministic, asset-free.
        dynamic.AddChild(new Label
        {
            Name = "DemographicsNote",
            Text = "Your nation over time (one point per year):",
        });

        var series = _game.Demographics;
        if (series.Count == 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "DemographicsEmpty",
                Text = "No years have passed yet — end a turn to start the record.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        // A legend of the three plotted series, in the graph's colours.
        dynamic.AddChild(new Label
        {
            Name = "DemographicsLegend",
            Text = "Population (green)   ·   Gold (gold)   ·   Score (cyan)",
        });

        DemographicSnapshot latest = series[^1];
        dynamic.AddChild(new Label
        {
            Name = "DemographicsLatest",
            Text = $"{latest.Year}: population {latest.Population}, gold {latest.Gold}, score {latest.Score}",
        });

        var graph = new DemographicGraph
        {
            Name = "DemographicGraph",
            CustomMinimumSize = new Vector2(560, 240),
        };
        graph.SetSeries(series);
        dynamic.AddChild(graph);
    }

    /// <summary>
    /// A small, asset-free line graph of the human nation's <see cref="DemographicSnapshot"/> series — population, gold
    /// and score over the years — drawn entirely in code (<see cref="_Draw"/>). Each metric is normalised to its own
    /// range so all three fit the same box (their absolute scales differ wildly), plotted as a connected polyline in its
    /// legend colour over a framed plot area with axis labels. Pure presentation (ADR-006): it reads only the snapshots
    /// handed to it and draws deterministically — no randomness, no model mutation.
    /// </summary>
    public partial class DemographicGraph : Control
    {
        private IReadOnlyList<DemographicSnapshot> _series = [];

        private static readonly Color PopulationColor = new("4caf50"); // green
        private static readonly Color GoldColor = new("ffd54f");       // gold
        private static readonly Color ScoreColor = new("4dd0e1");      // cyan
        private static readonly Color FrameColor = new("aaaaaa");

        /// <summary>Sets the series to plot and requests a redraw.</summary>
        public void SetSeries(IReadOnlyList<DemographicSnapshot> series)
        {
            _series = series;
            QueueRedraw();
        }

        public override void _Draw()
        {
            const float pad = 28f;
            Vector2 size = Size;
            var origin = new Vector2(pad, pad);
            float plotW = Mathf.Max(1f, size.X - pad * 2);
            float plotH = Mathf.Max(1f, size.Y - pad * 2);

            // Plot frame.
            DrawRect(new Rect2(origin, new Vector2(plotW, plotH)), FrameColor, filled: false, width: 1f);

            if (_series.Count == 0)
            {
                return;
            }

            // X spans the recorded years; a single point is drawn as a dot in the middle (no line to draw).
            DrawMetric(_series.Select(d => (float)d.Population).ToList(), origin, plotW, plotH, PopulationColor);
            DrawMetric(_series.Select(d => (float)d.Gold).ToList(), origin, plotW, plotH, GoldColor);
            DrawMetric(_series.Select(d => (float)d.Score).ToList(), origin, plotW, plotH, ScoreColor);

            // Year axis labels (first and last), small and unobtrusive.
            Font font = ThemeDB.FallbackFont;
            DrawString(font, new Vector2(origin.X, origin.Y + plotH + 18f), _series[0].Year.ToString(), HorizontalAlignment.Left, -1, 12);
            if (_series.Count > 1)
            {
                DrawString(font, new Vector2(origin.X + plotW - 36f, origin.Y + plotH + 18f), _series[^1].Year.ToString(), HorizontalAlignment.Left, -1, 12);
            }
        }

        // Plots one metric as a polyline, normalised to its own [min,max] over the plot box (each series fills the height,
        // so the SHAPE of each trend is legible despite the metrics' very different absolute scales).
        private void DrawMetric(List<float> values, Vector2 origin, float plotW, float plotH, Color color)
        {
            float min = values.Min();
            float max = values.Max();
            float range = max - min;
            int n = values.Count;

            Vector2 Point(int i)
            {
                float x = n == 1 ? origin.X + plotW / 2 : origin.X + plotW * i / (n - 1);
                // Flat series (range 0) sit on the mid-line; otherwise normalise into the box (y grows downward).
                float t = range > 0 ? (values[i] - min) / range : 0.5f;
                float y = origin.Y + plotH * (1f - t);
                return new Vector2(x, y);
            }

            if (n == 1)
            {
                DrawCircle(Point(0), 3f, color);
                return;
            }
            for (int i = 1; i < n; i++)
            {
                DrawLine(Point(i - 1), Point(i), color, 2f);
            }
        }
    }

    /// <summary>
    /// A report entity cell that deep-links into the Colopedia (<c>86d3fymc5</c>; FreeCol's report cells →
    /// <c>showColopediaPanel</c>). When the deep-link delegate is wired it returns a <b>flat link Button</b> (reads like a
    /// hyperlink, pointing-hand cursor) whose Pressed forwards <paramref name="category"/> / <paramref name="anchor"/> to
    /// <c>GameController</c> (which hides this report and opens the Colopedia there); when unwired (tests opening the panel
    /// directly) it returns a plain <see cref="Label"/>. Either way the control keeps <paramref name="nodeName"/> so the
    /// L3 tests and layout find it by the same name. Pure presentation (ADR-006) — the delegate reads oracles only.
    /// </summary>
    /// <param name="nodeName">The node name to keep (e.g. <c>Colony_3</c>, <c>Trade_tobacco</c>).</param>
    /// <param name="text">The cell's display text.</param>
    /// <param name="category">The Colopedia category the entity lives in.</param>
    /// <param name="anchor">The entity's ruleset <c>ShortName</c> (the Colopedia row anchor).</param>
    private Control EntityLink(string nodeName, string text, ColopediaPanel.Category category, string anchor)
    {
        if (_openColopedia is not { } open)
        {
            return new Label { Name = nodeName, Text = text };
        }
        var button = new Button
        {
            Name = nodeName,
            Text = text,
            Flat = true,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"Open the Colopedia entry for {text}",
        };
        button.Pressed += () => open(category, anchor);
        return button;
    }

    /// <summary>The readable tail of a <c>model.*.foo</c> id (e.g. <c>model.nation.dutch</c> → <c>dutch</c>).</summary>
    private static string Strip(string? id) => id is null ? "?" : id[(id.LastIndexOf('.') + 1)..];

    /// <summary>Title-cases a camelCase short name for label text (e.g. <c>expertFarmer</c> → "Expert Farmer") — the shared <see cref="Naming.Humanize"/>, honouring the active variant's <see cref="_displayOverrides"/> first (Australia: <c>cotton</c> → "Wool"). One humaniser everywhere so overrides apply uniformly.</summary>
    private string Display(string shortName) => Naming.Humanize(shortName, _displayOverrides);

    private static string Signed(int n) => (n > 0 ? "+" : "") + n;
}
