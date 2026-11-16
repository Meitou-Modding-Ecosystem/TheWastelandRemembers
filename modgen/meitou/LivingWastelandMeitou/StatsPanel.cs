// The squad record window: a "Squad record" button on the HUD opens a panel listing each member's numbers and titles, the crew's titles and
// the heat of the towns that remember you. The text is built in the tick (RecordHost.Panel); the interface only shows the latest copy.
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class StatsPanel
{
    static readonly string[] Shown = ["fighting", "first aid", "working", "building", "hauling", "travelling", "sneak attacks", "looting"];

    /// <summary>The panel's lines: one block per member, then the crew, then heat.</summary>
    public static string Text(LwDeeds deeds, World world, ISet<string> occupied)
    {
        var record = deeds.Record;
        var lines = new List<string>();
#if !SQUAD_RECORD_ONLY
        // a new game: what to look for (gone once the squad has a little history)
        if (record.History.Count < 4)
        {
            lines.Add("!!#The Wasteland Remembers");
            lines.Add("!!This window keeps your squad's story: what each of you has done, the titles you earn, who you lost.");
            lines.Add("!!Win a fight and leave someone alive, and they may not forget it. Rivals come back.");
            lines.Add("!!Listen in towns and bars: the talk is about what really happens, and it fills the Atlas tab.");
            lines.Add("!!Nearly everything can be turned down or off in the Settings tab.");
            lines.Add("");
        }
#endif
        var here = world.Characters;
        var present = new HashSet<string>(StringComparer.Ordinal);
        var hot = here.Previous;
        for (int s = 0; s < hot.Length; s++)
            if (hot[s].Alive && here.Cold(s) is { IsPlayer: true } c) present.Add(record.KeyFor(LwDeeds.Identity(c), c.Name));

        foreach (var (name, m) in record.Members.Where(p => present.Contains(p.Key)))
        {
            lines.AddRange(Member(record, name, m));
            lines.Add("");
        }
        lines.Add("-- The squad --");
        lines.Add(record.CrewTitles.Count == 0 ? "Known as: no name yet" : "Known as: " + string.Join(", ", record.CrewTitles.Select(record.TitleName)));
        if (record.NamedDead.Count > 0) lines.Add("Named dead: " + string.Join(", ", record.NamedDead));
#if !SQUAD_RECORD_ONLY
        lines.Add("");
        lines.AddRange(Heat(deeds.Heat, occupied));
#endif
        return string.Join("\n", lines);
    }

    static string N(long n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>One member's block: a header (name, titles), then labelled lines, each only when there is something to say.</summary>
    public static IEnumerable<string> Member(SquadRecord record, string name, MemberRecord m)
    {
        string titles = m.Titles.Count == 0 ? "" : ": " + string.Join(", ", m.Titles.Select(record.TitleName));
        yield return $"-- {record.Display(name)}{titles} --";

        var combat = new List<string>();
        if (m.Kills > 0)
        {
            var extra = new List<string>();
            if (m.LeadersKilled > 0) extra.Add(N(m.LeadersKilled, "leader", "leaders"));
            if (m.NamedKills > 0) extra.Add($"{m.NamedKills} named");
            combat.Add(N(m.Kills, "kill", "kills") + (extra.Count > 0 ? $" ({string.Join(", ", extra)})" : ""));
        }
        if (m.Knockouts > 0) combat.Add(N(m.Knockouts, "knockout", "knockouts"));
        if (m.LimbsCut > 0) combat.Add(N(m.LimbsCut, "limb cut off", "limbs cut off"));
        if (m.DamageDealt >= 1) combat.Add($"{m.DamageDealt:0} damage dealt");
        if (m.Fights > 0) combat.Add($"{N(m.Fights, "fight", "fights")} ({m.FightsWon} won, {N(m.LastStands, "last stand", "last stands")})");
        yield return "Combat:   " + (combat.Count > 0 ? string.Join("   ", combat) : "nothing yet");

        var wounds = new List<string>();
        if (m.DamageTaken >= 1) wounds.Add($"{m.DamageTaken:0} damage taken");
        if (m.TimesDowned > 0) wounds.Add($"downed {N(m.TimesDowned, "time", "times")}");
        if (m.LimbsLost > 0) wounds.Add(N(m.LimbsLost, "limb lost", "limbs lost"));
        if (wounds.Count > 0) yield return "Wounds:   " + string.Join("   ", wounds);

        if (m.Kills > 0) yield return "Most killed:   " + string.Join(", ", m.KillsBy.OrderByDescending(p => p.Value).Take(3).Select(p => $"{p.Key} {p.Value}"));

        var travel = new List<string> { $"walked {m.Distance / 10000:0.0} km" };
        if (m.Ridden > 0) travel.Add($"rode {m.Ridden / 10000:0.0} km");
        if (m.Sailed > 0) travel.Add($"sailed {m.Sailed / 10000:0.0} km");
        yield return "Travel:   " + string.Join("   ", travel);

        var time = Shown.Where(a => m.SecondsOf(a) >= 60).Select(a => $"{a} {Hours(m.SecondsOf(a))}").ToList();
        if (time.Count > 0) yield return "Time spent:   " + string.Join("   ", time);

        if (m.Bites > 0 || m.Crafted > 0)
        {
            var parts = new List<string>();
            if (m.Bites > 0) parts.Add($"{m.Food:0} food eaten ({N(m.Bites, "bite", "bites")})");
            if (m.Crafted > 0) parts.Add($"{m.Crafted} crafted (" + string.Join(", ", m.CraftedBy.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")) + ")");
            yield return "Food and crafting:   " + string.Join("   ", parts);
        }
    }

#if !SQUAD_RECORD_ONLY
    public static IEnumerable<string> Heat(HeatMap heat, ISet<string> occupied)
    {
        var towns = heat.All().Take(6).ToList();
        if (towns.Count == 0)
        {
            yield return "-- Heat --";
            yield return "No town remembers you.";
            yield break;
        }
        yield return "-- Heat (fades while you stay away; blood fades slower) --";
        foreach (var (town, faction, crime, blood) in towns)
        {
            double total = Math.Min(HeatMap.Max, crime + blood);
            string where = occupied.Contains(town) ? "  (you are here: not fading)" : "";
            yield return $"   {town} ({faction}): {HeatMap.Tier(total)} {total:0}  [crime {crime:0}, blood {blood:0}]{where}";
        }
    }

#endif

    static string Hours(double seconds) => seconds >= 3600 ? $"{seconds / 3600:0.0} h" : $"{seconds / 60:0} min";
}

/// <summary>
/// The HUD button and the window: two views (the record, the title catalog), each in pages that fit the window. Laid out the way the working
/// windows of other Meitou mods are (KUO's CHRONICLE): every node at an absolute position worked out from the screen (UiFrameContext.Viewport),
/// sizes and text scaled to the screen height, the game's default font, styles registered as they are first needed.
/// </summary>
public sealed class StatsUi(string id = "livingwasteland") : IUiExtension
{
    internal static volatile bool open;
    static volatile bool earnedOnly;   // the Titles page: only the titles someone holds
    static volatile int tab;   // 0 record, 1 titles, 2 history, 3 fallen
    static int page;
    static readonly string[] TabArgs = ["record", "titles", "history", "fallen", "atlas", "settings", "squads"];   // tab number -> its argument

    /// <summary>Where the button sits (a settings choice). "Top right, lower" sits below the FEEDBACK button and other mods' top-right buttons.</summary>
    public static readonly string[] Positions = ["Top, beside Feedback", "Top right, lower", "Top right", "Top left, lower", "Top left"];

    public static volatile string Position = Positions[0];

    static readonly System.Numerics.Vector4 WindowBg = new(0.07f, 0.065f, 0.055f, 0.94f), HeaderBg = new(0.13f, 0.115f, 0.09f, 1f),
        ButtonBg = new(0.16f, 0.14f, 0.11f, 0.95f), ButtonHover = new(0.26f, 0.23f, 0.17f, 1f), ButtonDown = new(0.34f, 0.29f, 0.2f, 1f),
        TabOn = new(0.36f, 0.3f, 0.19f, 1f), Ink = new(0.92f, 0.9f, 0.84f, 1f), Muted = new(0.68f, 0.65f, 0.58f, 1f), Line = new(0.4f, 0.35f, 0.26f, 1f);

    readonly Dictionary<string, string> styles = [];
    UiRegistration? reg;
    string toggle = "", close = "", view = "", turn = "", pick = "", change = "", preset = "", export = "", earned = "", search = "", clear = "", about = "";
    string? aboutKey;
#if !SQUAD_RECORD_ONLY
    string roleAct = "", homeAct = "", pickAct = "";
    int? roleMenu;          // the squad whose role drop-down is open
#endif       // the setting whose description was clicked (the hover box without a mouse)
    string lastQuery = "";
    string? exported;       // where the last "Save as text" went (or why it did not)
    string? entry;          // the Atlas entry open, null for the list
    int listPage;
    int ids;

    public string Id => id;

    public void Register(UiRegistration r)
    {
        reg = r;
        toggle = r.RegisterAction("toggle", _ => open = !open);
        close = r.RegisterAction("close", _ => open = false);
        view = r.RegisterAction("view", c => { tab = c.Argument == "titles" ? 1 : c.Argument == "history" ? 2 : c.Argument == "fallen" ? 3 : c.Argument == "atlas" ? 4 : c.Argument == "settings" ? 5 : c.Argument == "squads" ? 6 : 0; page = 0; entry = null; });
        export = r.RegisterAction("export", _ => exported = Export());
        earned = r.RegisterAction("earned", _ => { earnedOnly = !earnedOnly; page = 0; });
        preset = r.RegisterAction("preset", c => { if (c.Argument == "record") SettingsFile.RecordOnly(); else SettingsFile.Defaults(); });
        change = r.RegisterAction("set", c => { int bar = c.Argument.LastIndexOf('|'); if (bar > 0) SettingsFile.Step(c.Argument[..bar], c.Argument[(bar + 1)..] == "-" ? -1 : 1); });
        pick = r.RegisterAction("atlas", c => { entry = c.Argument.Length == 0 ? null : c.Argument; listPage = entry is null ? listPage : page; page = entry is null ? listPage : 0; });
        turn = r.RegisterAction("page", c => page = Math.Max(0, page + (c.Argument == "next" ? 1 : -1)));
        search = r.RegisterAction("search", _ => UiInput.Typing = UiInput.CanType && !UiInput.Typing);
        clear = r.RegisterAction("clear", _ => { UiInput.Query = ""; UiInput.Typing = false; });
        about = r.RegisterAction("about", c => aboutKey = aboutKey == c.Argument ? null : c.Argument);
#if !SQUAD_RECORD_ONLY
        roleAct = r.RegisterAction("role", c => { if (int.TryParse(c.Argument, out int id)) roleMenu = roleMenu == id ? null : id; });
        pickAct = r.RegisterAction("pickrole", c =>
        {
            int bar = c.Argument.IndexOf('|');
            if (bar > 0 && int.TryParse(c.Argument[..bar], out int id) && int.TryParse(c.Argument[(bar + 1)..], out int choice)) SquadRoles.SetRole(id, choice);
            roleMenu = null;
        });
        homeAct = r.RegisterAction("home", c => { if (int.TryParse(c.Argument, out int id)) SquadRoles.ToggleHome(id); });
#endif
        r.Contribute(UiSlots.Overlay, Build);
    }

    UiNode Build(UiFrameContext ctx)
    {
        ids = 0;
        var screen = ctx.Viewport;
        float s = Math.Clamp(screen.Height / 1080f, 0.75f, 2.5f);
        var root = new UiNode(reg!.Key("root")) { Pickable = false };
        if (RecordHost.Off) return root;

        var (bx, by) = ButtonAt(screen, s);
        root.Children.Add(Node(bx, by, 130 * s, 26 * s, Style(open ? "tabOn" : "button", s), UiNodeKind.Button, "Squad record", toggle));
        if (open) root.Children.Add(Window(screen, s));
        if (!open || tab != 5) UiInput.Typing = false;
        AddNotices(root, screen, s);
        return root;
    }

    /// <summary>The floating notices (Notices.cs): newest on top, each fading out over its last second.</summary>
    void AddNotices(UiNode root, UiRect screen, float s)
    {
        var list = Notices.Current();
        if (list.Count == 0) return;
        float lineH = 26 * s, gap = 4 * s;
        string place = Notices.Place;
        float y = place == "Top centre" ? screen.Height * 0.09f : screen.Height * 0.22f;
        foreach (var (n, age) in list)
        {
            float wNotice = Math.Min(screen.Width * 0.5f, (n.Text.Length * 9.5f + 28) * s);
            float x = place == "Left side" ? 12 * s : place == "Right side" ? screen.Width - wNotice - 12 * s : (screen.Width - wNotice) / 2;
            double left = Notices.ShowSeconds - age;
            int alpha = left >= Notices.FadeSeconds ? 4 : Math.Max(0, (int)(left / Notices.FadeSeconds * 4));
            root.Children.Add(Node(x, y, wNotice, lineH, NoticeStyle("bg", alpha, s)));
            root.Children.Add(Node(x, y, wNotice, lineH, NoticeStyle(n.Kind.ToString(), alpha, s), UiNodeKind.Label, n.Text));
            y += lineH + gap;
        }
    }

    static readonly System.Numerics.Vector4 SkillInk = new(0.96f, 0.83f, 0.46f, 1f), UpInk = new(0.48f, 0.86f, 0.45f, 1f), DownInk = new(0.92f, 0.42f, 0.36f, 1f);

    /// <summary>A notice style: its background or its text colour (by kind) at an opacity step 0-4.</summary>
    string NoticeStyle(string role, int alpha, float s)
    {
        string key = $"notice-{role}-{alpha}-{(int)MathF.Round(s * 20)}";
        if (styles.TryGetValue(key, out var made)) return made;
        float a = alpha / 4f;
        var ink = role switch { "Up" => UpInk, "Down" => DownInk, _ => SkillInk };
        var style = role == "bg"
            ? new UiStyle { Background = WindowBg with { W = 0.82f * a } }
            : new UiStyle { Text = ink with { W = a }, TextScale = 0.9f * s, TextAlign = "Center VCenter" };
        made = reg!.RegisterStyle(key, style);
        styles[key] = made;
        return made;
    }

    static (float X, float Y) ButtonAt(UiRect screen, float s)
    {
        float w = 130 * s, gap = 8 * s;
        // the default: on the top line, just left of Meitou's FEEDBACK button (about 8% of the width), out of the corner other mods use
        if (Position.StartsWith("Top, beside", StringComparison.Ordinal)) return (screen.Width - screen.Width * 0.085f - w - gap, 2 * s);
        bool left = Position.StartsWith("Top left", StringComparison.Ordinal);
        bool lower = Position.EndsWith("lower", StringComparison.Ordinal);
        float x = left ? gap : screen.Width - w - gap;
        float y = left ? gap + 34 * s : screen.Height * 0.03f + 38 * s;
        // Kenshi Ultimate Overhaul 1.0.1+ keeps a chronicle box on the top right from this same height, up to 420 wide (its default
        // place): step to its left so the button is never under it.
        if (!left && KuoBoxLikely) x = screen.Width - Math.Min(420 * s, screen.Width * 0.38f) - 8 * s - w - gap;
        return (x, lower ? y + 34 * s : y);
    }

    static bool? kuo;

    /// <summary>Whether Kenshi Ultimate Overhaul's living-world part is loaded (looked up once; its box may still be moved or hidden in its
    /// own settings, in which case our button just sits a little further left than it needs to).</summary>
    static bool KuoBoxLikely => kuo ??= AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is { } n && (n.StartsWith("KuoLivingWorld", StringComparison.Ordinal) || n.StartsWith("KuoCode", StringComparison.Ordinal)));

    UiNode Window(UiRect screen, float s)
    {
        float w = Math.Min(900 * s, screen.Width - 40 * s), h = Math.Min(640 * s, screen.Height - 80 * s);
        var win = new UiNode(reg!.Key("window"))
        {
            Placement = new UiPlacement(w, h, (screen.Width - w) / 2, (screen.Height - h) / 2), Style = Style("window", s), Pickable = true,
        };

        // header: title, close, the two views
        win.Children.Add(Node(0, 0, w, 40 * s, Style("header", s)));
        string[] titles = ["Squad record", "Squad record - titles", "Squad record - history", "Squad record - the fallen", "Atlas", "Settings", "Squads - roles"];
        win.Children.Add(Node(0, 0, w - 50 * s, 40 * s, Style("title", s), UiNodeKind.Label, titles[tab]));
        win.Children.Add(Node(w - 44 * s, 6 * s, 36 * s, 28 * s, Style("button", s), UiNodeKind.Button, "X", close));
#if SQUAD_RECORD_ONLY
        string[] names = ["Record", "Titles", "History", "Fallen", "Settings"], args = ["record", "titles", "history", "fallen", "settings"];
#else
        string[] names = ["Record", "Titles", "History", "Fallen", "Atlas", "Squads", "Settings"], args = ["record", "titles", "history", "fallen", "atlas", "squads", "settings"];
#endif
        for (int i = 0; i < names.Length; i++)
            win.Children.Add(Node((10 + i * 112) * s, 48 * s, 106 * s, 28 * s, Style(tab == Array.IndexOf(TabArgs, args[i]) ? "tabOn" : "button", s), UiNodeKind.Button, names[i], view, args[i]));
        win.Children.Add(Node(10 * s, 82 * s, w - 20 * s, 1 * s, Style("line", s)));
#if !SQUAD_RECORD_ONLY
        if (tab == 6) return SquadsPage(win, w, h, s);
#endif
        if (tab == 5) return SettingsPage(win, w, h, s, (screen.Width - w) / 2, (screen.Height - h) / 2);

        // the lines of this page
        float lineH = 22 * s, top = 90 * s, bottom = h - 46 * s;
        int perPage = Math.Max(1, (int)((bottom - top) / lineH));
        var all = (tab switch
        {
            1 => RecordHost.CatalogText, 2 => RecordHost.HistoryText, 3 => RecordHost.FallenText,
            4 => entry is null ? RecordHost.AtlasList : RecordHost.AtlasPages.GetValueOrDefault(entry, "Nothing more is known."),
            _ => RecordHost.Panel,
        }).Split('\n');
        if (tab == 1)
        {
            if (earnedOnly) all = EarnedOnly(all);
            win.Children.Add(Node(w - 170 * s, h - 40 * s, 156 * s, 28 * s, Style("button", s), UiNodeKind.Button, earnedOnly ? "Show all" : "Earned only", earned));
        }
        if (tab is 2 or 3)
            win.Children.Add(Node(w - 170 * s, h - 40 * s, 156 * s, 28 * s, Style("button", s), UiNodeKind.Button, "Save as text", export));
        if (tab == 4 && entry is not null)
            win.Children.Add(Node(w - 134 * s, h - 40 * s, 120 * s, 28 * s, Style("button", s), UiNodeKind.Button, "< Atlas", pick, ""));
        // an Atlas person page shows the person's portrait at the top right (Meitou 0.19+ image nodes); the text wraps to the room left
        float textW = w - 20 * s;
        if (tab == 4 && entry is not null && RecordHost.AtlasPortraits.GetValueOrDefault(entry) is { } face
            && PortraitNode(w - Math.Min(180 * s, h * 0.3f) - 14 * s, top, Math.Min(180 * s, h * 0.3f), face) is { } portrait)
        {
            float size = Math.Min(180 * s, h * 0.3f);
            win.Children.Add(Node(w - size - 16 * s, top - 2 * s, size + 4 * s, size + 4 * s, Style("line", s)));
            win.Children.Add(Node(w - size - 14 * s, top, size, size, Style("tip", s)));
            win.Children.Add(portrait);
            textW = w - size - 40 * s;
            all = all.SelectMany(l => l.StartsWith("@@", StringComparison.Ordinal) || l.Length * 8.4f * s <= textW ? [l]
                : Wrap(l, Math.Max(30, (int)(textW / (8.4f * s))), 20)).ToArray();
        }
        int pages = Math.Max(1, (all.Length + perPage - 1) / perPage);
        page = Math.Min(page, pages - 1);
        float y = top;
        foreach (var line in all.Skip(page * perPage).Take(perPage))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal) && line.IndexOf('|') is > 2 and var bar)
            {
                win.Children.Add(Node(10 * s, y + 1 * s, w - 20 * s, lineH - 2 * s, Style("row", s), UiNodeKind.Button, line[(bar + 1)..], pick, line[2..bar]));
                y += lineH;
                continue;
            }
            // "!!" lines: the new-game welcome, in the notices' gold ("!!#" its heading, larger)
            bool welcome = line.StartsWith("!!", StringComparison.Ordinal);
            string shown = welcome ? line[(line.StartsWith("!!#", StringComparison.Ordinal) ? 3 : 2)..] : line;
            string role = welcome ? (line.StartsWith("!!#", StringComparison.Ordinal) ? "welcomeHead" : "welcome") : line.StartsWith("--", StringComparison.Ordinal) ? "section" : "text";
            win.Children.Add(Node(10 * s, y, textW, lineH, Style(role, s), UiNodeKind.Label, shown));
            y += lineH;
        }

        // footer: pages
        float fy = h - 40 * s;
        win.Children.Add(Node(10 * s, fy, 100 * s, 28 * s, Style("button", s), UiNodeKind.Button, "< Back", turn, "prev"));
        win.Children.Add(Node(116 * s, fy, 100 * s, 28 * s, Style("button", s), UiNodeKind.Button, "Next >", turn, "next"));
        win.Children.Add(Node(224 * s, fy, w - 404 * s, 28 * s, Style("muted", s), UiNodeKind.Label,
            $"page {page + 1} of {pages}" + (tab is 2 or 3 && exported is not null ? "   " + exported : "")));
        return win;
    }

    /// <summary>
    /// The Settings tab: every setting of this mod, a switch as an On/Off button, a number with - and + around its value, a choice as a button
    /// that moves to the next one. Changes apply at once and are kept (SettingsFile); they also show on Esc > Settings > Mods.
    /// </summary>
#if !SQUAD_RECORD_ONLY
    /// <summary>The Squads tab (SquadRoles.cs): each of the player's squads with its role (click for the next one) and its home (click to
    /// make the nearest building of yours its home, again to clear it). Needs Meitou 0.19 or later.</summary>
    UiNode SquadsPage(UiNode win, float w, float h, float s)
    {
        float y = 92 * s, rowH = 34 * s;
        void Text(string t, string style = "muted") { win.Children.Add(Node(10 * s, y, w - 20 * s, 22 * s, Style(style, s), UiNodeKind.Label, t)); y += 22 * s; }
        if (!SquadRoles.Available)
        {
            Text("Squad roles need Meitou 0.19 or later (player squads on AI packages).", "text");
            return win;
        }
        Text("Squads with a role act on their own, like NPC squads. Your orders and jobs still come first.");
        Text("Home: the building of yours nearest the squad leader.");
        y += 8 * s;
        var rows = SquadRoles.Squads;
        if (rows.Count == 0) Text(SquadRoles.Problem is { } p ? $"Could not read your squads ({p})." : "No squads yet.", "text");
        (int Id, float Y, float X, float W)? menu = null;
        foreach (var r in rows.Take(10))
        {
            string role = SquadRoles.RoleLabel(r) + "  v";
            float bw = 250 * s, hw = 230 * s;
            win.Children.Add(Node(10 * s, y, w - bw - hw - 40 * s, rowH - 6 * s, Style("text", s), UiNodeKind.Label, $"{r.Label}  ({r.Members})"));
            win.Children.Add(Node(w - bw - hw - 20 * s, y, bw, rowH - 6 * s, Style(r.Autonomous ? "tabOn" : "button", s), UiNodeKind.Button, role, roleAct, r.Id.ToString()));
            if (roleMenu == r.Id) menu = (r.Id, y + rowH - 4 * s, w - bw - hw - 20 * s, bw);
            string home = r.Home is { } hn ? $"Home: {hn}" : r.NearName is { } nn ? $"Make home: {nn}" : "No building of yours near";
            win.Children.Add(Node(w - hw - 10 * s, y, hw, rowH - 6 * s, Style(r.Home is not null ? "tabOn" : "button", s), UiNodeKind.Button, home,
                r.Home is not null || r.NearId is not null ? homeAct : "", r.Id.ToString()));
            y += rowH;
        }
        y += 8 * s;
        Text("Roles:");
        foreach (var role in SquadRoles.Roles.Skip(1)) Text($"  {role.Label}: {role.About}");
        Text("  Escort (another squad): follows that squad and protects it.");
        // the open drop-down, drawn last so it sits on top: every choice for that squad
        if (menu is { } m)
        {
            var choices = SquadRoles.Choices(m.Id);
            float itemH = 26 * s;
            win.Children.Add(Node(m.X - 1 * s, m.Y - 1 * s, m.W + 2 * s, choices.Count * itemH + 2 * s, Style("line", s)));
            foreach (var (index, label) in choices)
            {
                win.Children.Add(Node(m.X, m.Y, m.W, itemH, Style("row", s), UiNodeKind.Button, label, pickAct, $"{m.Id}|{index}"));
                m.Y += itemH;
            }
        }
        return win;
    }
#endif

    /// <summary>Which part of the mod a setting belongs to (the Settings tab's sections, and words the search finds it by).</summary>
    static readonly Dictionary<string, string> Sections = new(StringComparer.Ordinal)
    {
        ["Talk"] = "Talk", ["Gossip"] = "Talk", ["BarTalk"] = "Talk", ["BarTalkPause"] = "Talk", ["VictoryLines"] = "Talk",
        ["EnemyVictoryLines"] = "Talk", ["LookoutCalls"] = "Talk", ["TownCriers"] = "Talk", ["Memorials"] = "Talk", ["TitleReactions"] = "Talk",
        ["Jobs"] = "Jobs and outlaws", ["JobCap"] = "Jobs and outlaws", ["CapMessages"] = "Jobs and outlaws", ["ContractReach"] = "Jobs and outlaws", ["NewsReach"] = "Jobs and outlaws",
        ["DistancePay"] = "Jobs and outlaws", ["GrowingBounties"] = "Jobs and outlaws", ["ContractFavours"] = "Jobs and outlaws", ["Alms"] = "The world",
        ["OutlawCamps"] = "Jobs and outlaws", ["OutlawReprisals"] = "Jobs and outlaws", ["OutlawSuccessors"] = "Jobs and outlaws", ["Rivals"] = "Jobs and outlaws",
        ["Heat"] = "The world", ["BountyHunters"] = "The world", ["HarassmentRaids"] = "The world", ["Trophies"] = "The world",
        ["NeckSlotX"] = "The world", ["NeckSlotY"] = "The world",
    };

    static string SectionOf(string key) => Sections.TryGetValue(key, out var v) ? v : "Squad record";

    /// <summary>A description cut into lines of about <paramref name="width"/> characters, at most <paramref name="max"/> lines.</summary>
    static List<string> Wrap(string text, int width, int max)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line.ToString()); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        if (lines.Count > max) { lines = lines.Take(max).ToList(); lines[^1] += " ..."; }
        return lines;
    }

    /// <summary>
    /// The Settings tab: every setting of this mod in sections, a switch as an On/Off button, a number with - and + around its value, a choice
    /// as a button that moves to the next one. A search box at the top filters by name, description and section (click it, then type). Hovering
    /// over a setting (or clicking its name) shows a box with what it does. Changes apply at once and are kept (SettingsFile); they also show
    /// on Esc > Settings > Mods. <paramref name="ox"/>, <paramref name="oy"/>: where the window sits on screen (for the mouse).
    /// </summary>
    UiNode SettingsPage(UiNode win, float w, float h, float s, float ox, float oy)
    {
        var all = SettingsFile.Group?.Settings ?? [];
        float right = 154 * s;
#if !SQUAD_RECORD_ONLY
        win.Children.Add(Node(w - 300 * s, 88 * s, 140 * s, 26 * s, Style("button", s), UiNodeKind.Button, "Record only", preset, "record"));
        right = 300 * s;
#endif
        win.Children.Add(Node(w - 154 * s, 88 * s, 144 * s, 26 * s, Style("button", s), UiNodeKind.Button, "Defaults", preset, "defaults"));

        // the search box (the tabs' row is free on this page: the box sits under them)
        string q = UiInput.Query.Trim();
        if (q != lastQuery) { lastQuery = q; page = 0; }
        float sy = 88 * s, sw = w - 20 * s - right - 6 * s - (q.Length > 0 ? 34 * s : 0);
        string boxText = !UiInput.CanType ? "Search is not available on this Meitou version"
            : UiInput.Typing ? $"Search: {q}_   (Enter when done)"
            : q.Length > 0 ? $"Search: {q}   (click to change)" : "Search settings: click here, then type";
        win.Children.Add(Node(10 * s, sy, sw, 26 * s, Style(UiInput.Typing ? "tabOn" : "row", s), UiNodeKind.Button, boxText, search));
        if (q.Length > 0) win.Children.Add(Node(10 * s + sw + 4 * s, sy, 30 * s, 26 * s, Style("button", s), UiNodeKind.Button, "X", clear));

        string[] words = q.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = all.Where(st => words.Length == 0 || words.All(word =>
            $"{st.Label} {st.Hint} {SectionOf(st.Key)}".Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();

        // rows: a section header before the first setting of each section
        var rows = new List<(string? Section, ModSetting? Setting)>();
        string? last = null;
        foreach (var st in shown)
        {
            string sec = SectionOf(st.Key);
            if (sec != last) { rows.Add((sec, null)); last = sec; }
            rows.Add((null, st));
        }

        float rowH = 30 * s, top = 120 * s, bottom = h - 46 * s, valueW = 220 * s;
        int perPage = Math.Max(1, (int)((bottom - top) / rowH));
        int pages = Math.Max(1, (rows.Count + perPage - 1) / perPage);
        page = Math.Min(page, pages - 1);
        float y = top;
        var mouse = UiInput.Mouse;
        (ModSetting St, float Y)? box = null;
        foreach (var (section, st) in rows.Skip(page * perPage).Take(perPage))
        {
            if (st is null)
            {
                win.Children.Add(Node(10 * s, y + 6 * s, w - 20 * s, rowH - 6 * s, Style("section", s), UiNodeKind.Label, $"-- {section} --"));
                y += rowH;
                continue;
            }
            bool hovered = UiInput.HasMouse && mouse.X >= ox + 10 * s && mouse.X <= ox + w - 10 * s && mouse.Y >= oy + y && mouse.Y < oy + y + rowH;
            if (hovered || aboutKey == st.Key) box = (st, y);
            win.Children.Add(Node(10 * s, y, w - valueW - 30 * s, rowH - 4 * s, Style("row", s), UiNodeKind.Button, st.Label, about, st.Key));
            float vx = w - valueW - 10 * s;
            if (st.Kind == ModSettingKind.Slider)
            {
                win.Children.Add(Node(vx, y, 40 * s, rowH - 4 * s, Style("button", s), UiNodeKind.Button, "-", change, st.Key + "|-"));
                win.Children.Add(Node(vx + 44 * s, y, valueW - 88 * s, rowH - 4 * s, Style("muted", s), UiNodeKind.Label, st.Text));
                win.Children.Add(Node(vx + valueW - 40 * s, y, 40 * s, rowH - 4 * s, Style("button", s), UiNodeKind.Button, "+", change, st.Key + "|+"));
            }
            else
            {
                string text = st.Kind == ModSettingKind.Switch ? (st.Get() is true ? "On" : "Off") : st.Text;
                bool on = st.Kind == ModSettingKind.Switch && st.Get() is true;
                win.Children.Add(Node(vx, y, valueW, rowH - 4 * s, Style(on ? "tabOn" : "button", s), UiNodeKind.Button, text, change, st.Key + "|+"));
            }
            y += rowH;
        }
        if (rows.Count == 0)
            win.Children.Add(Node(10 * s, top, w - 20 * s, rowH, Style("muted", s), UiNodeKind.Label, $"No setting matches \"{q}\"."));

        // the description box: under the setting (above it near the bottom), drawn last so it sits on top
        if (box is { } b && b.St.Hint is { Length: > 0 } hint)
        {
            float bw = Math.Min(w - 40 * s, 620 * s), lineH = 22 * s;
            var lines = Wrap(hint, Math.Max(30, (int)(bw / (8.2f * s))), 6);
            float bh = lines.Count * lineH + 12 * s;
            float by = b.Y + rowH + bh <= h - 46 * s ? b.Y + rowH : b.Y - bh - 2 * s;
            win.Children.Add(Node(20 * s - 1 * s, by - 1 * s, bw + 2 * s, bh + 2 * s, Style("line", s)));
            win.Children.Add(Node(20 * s, by, bw, bh, Style("tip", s)));
            for (int i = 0; i < lines.Count; i++)
                win.Children.Add(Node(20 * s, by + 6 * s + i * lineH, bw, lineH, Style("tipText", s), UiNodeKind.Label, lines[i]));
        }

        float fy = h - 40 * s;
        win.Children.Add(Node(10 * s, fy, 100 * s, 28 * s, Style("button", s), UiNodeKind.Button, "< Back", turn, "prev"));
        win.Children.Add(Node(116 * s, fy, 100 * s, 28 * s, Style("button", s), UiNodeKind.Button, "Next >", turn, "next"));
        win.Children.Add(Node(224 * s, fy, w - 234 * s, 28 * s, Style("muted", s), UiNodeKind.Label,
            $"page {page + 1} of {pages}   Hover over a setting for details."));
        return win;
    }

    /// <summary>
    /// <summary>The titles catalog with only the held titles (lines marked "*"), each group's header kept only when it has one.</summary>
    static string[] EarnedOnly(string[] lines)
    {
        var keep = new List<string>();
        string? header = null;
        foreach (var l in lines)
        {
            if (l.StartsWith("--", StringComparison.Ordinal)) { header = l; continue; }
            if (l.Length == 0) continue;
            if (!l.StartsWith('*')) { if (keep.Count == 0) keep.Add(l); continue; }
            if (header is not null) { keep.Add(""); keep.Add(header); header = null; }
            keep.Add(l);
        }
        if (keep.Count <= 1) keep.Add("No titles earned yet.");
        return [.. keep];
    }

    /// "Save as text": the squad's whole story (record, titles, history, the fallen) as a plain text file in Documents (The Wasteland Remembers, or Squad Record), to keep
    /// or share. Written from the copies the tick already made, so it never touches the world.
    /// </summary>
    static string Export()
    {
        try
        {
#if SQUAD_RECORD_ONLY
            const string Folder = "Squad Record";
#else
            const string Folder = "The Wasteland Remembers";
#endif
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Folder);
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"Squad story {DateTime.Now:yyyy-MM-dd HHmm}.txt");
            File.WriteAllText(file, string.Join(Environment.NewLine + Environment.NewLine,
                "THE SQUAD" + Environment.NewLine + RecordHost.Panel, "TITLES" + Environment.NewLine + RecordHost.CatalogText,
                "HISTORY" + Environment.NewLine + RecordHost.HistoryText, "THE FALLEN" + Environment.NewLine + RecordHost.FallenText));
            return $"Saved to Documents\\{Folder}\\{Path.GetFileName(file)}";
        }
        catch (Exception e)
        {
            RecordHost.Log($"save as text failed: {e.GetBaseException().Message}");
            return "Could not save the file (see log.txt).";
        }
    }

    // Meitou 0.19 image nodes, found by name so older versions (no faces) still load: UiNodeKind.Image and UiNode.Portrait.
    static readonly UiNodeKind? ImageKind = Enum.TryParse<UiNodeKind>("Image", out var k) ? k : null;
    static readonly System.Reflection.PropertyInfo? PortraitProp = typeof(UiNode).GetProperty("Portrait");
    public static bool CanShowFaces => ImageKind is not null && PortraitProp is not null;

    /// <summary>A portrait of a CHARACTER record, drawn as the squad bar draws portraits; null on a Meitou without image nodes.</summary>
    UiNode? PortraitNode(float x, float y, float size, string record)
    {
        if (ImageKind is not { } kind || PortraitProp is null) return null;
        try
        {
            var node = new UiNode(reg!.Key($"n{ids++}"), kind) { Placement = new UiPlacement(size, size, x, y), Pickable = false };
            PortraitProp.SetValue(node, record);
            return node;
        }
        catch { return null; }
    }

    UiNode Node(float x, float y, float w, float h, string style, UiNodeKind kind = UiNodeKind.Panel, string text = "", string? action = null,
        string argument = "") =>
        new(reg!.Key($"n{ids++}"), kind)
        {
            Placement = new UiPlacement(Math.Max(0, w), Math.Max(0, h), x, y), Style = style, Text = text, Action = action ?? "",
            Argument = argument, Pickable = kind == UiNodeKind.Button,
        };

    /// <summary>A style for a role at this scale, registered the first time it is asked for.</summary>
    string Style(string role, float s)
    {
        string key = $"{role}-{(int)MathF.Round(s * 20)}";
        if (styles.TryGetValue(key, out var made)) return made;
        var pad = new UiInsets(8 * s, 0, 6 * s, 0);
        var style = role switch
        {
            "window" => new UiStyle { Background = WindowBg },
            "header" => new UiStyle { Background = HeaderBg },
            "line" => new UiStyle { Background = Line },
            "title" => new UiStyle { Text = Ink, TextScale = 1.2f * s, TextPadding = pad },
            "welcome" => new UiStyle { Text = SkillInk, TextScale = 0.9f * s, TextPadding = pad },
            "welcomeHead" => new UiStyle { Text = SkillInk, TextScale = 1.12f * s, TextPadding = pad },
            "section" => new UiStyle { Text = Line, TextScale = 0.95f * s, TextPadding = pad },
            "muted" => new UiStyle { Text = Muted, TextScale = 0.82f * s, TextPadding = pad },
            "button" => new UiStyle { Background = ButtonBg, Hover = ButtonHover, Active = ButtonDown, Text = Ink, TextScale = 0.9f * s,
                TextAlign = "Center VCenter" },
            "tip" => new UiStyle { Background = HeaderBg },
            "tipText" => new UiStyle { Text = Ink, TextScale = 0.82f * s, TextPadding = pad },
            "row" => new UiStyle { Background = WindowBg, Hover = ButtonHover, Active = ButtonDown, Text = Ink, TextScale = 0.86f * s, TextPadding = pad },
            "tabOn" => new UiStyle { Background = TabOn, Hover = TabOn, Active = ButtonDown, Text = Ink, TextScale = 0.9f * s, TextAlign = "Center VCenter" },
            _ => new UiStyle { Text = Ink, TextScale = 0.86f * s, TextPadding = pad },
        };
        made = reg!.RegisterStyle(key, style);
        styles[key] = made;
        return made;
    }
}
