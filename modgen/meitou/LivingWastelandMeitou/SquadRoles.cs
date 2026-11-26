// Squad roles (Living Wasteland only; the Squads tab of the Squad record window): Meitou 0.19's autonomous player squads
// (docs/simulation.md "Autonomous player squads"): a player squad switched autonomous runs a vanilla AI package, the way NPC squads do; the
// player's orders and jobs always come first and the squad picks the package up again after them; switch, package and home are saved with
// the squad by Meitou itself.
//
// The roles are vanilla packages (the game's own records): guard the home, guard it day and night, patrol nearby (defending only),
// rest at home, wander from town to town; and escort another of the player's squads: the vanilla "Bodyguard" contract (what a hired
// mercenary runs) aimed at that squad's leader, so it follows the squad and fights for it. Meitou keeps no contract target over a save and a
// running contract cannot be re-aimed. We save the link in LwCore using saved squad tags on Meitou versions that expose them.
// Older Meitou can keep the link during the current session. Refresh aims the escort again (autonomy off and on, which drops the old contract silently, then the contract) after a load, when the leader changes
// or before the contract runs out. A home is the nearest finished building of the player's to the squad leader.
//
// The simulation side (Refresh, every second) only reads and publishes a snapshot; the window's buttons queue Meitou's own commands
// (SetSquadAutonomy, ChangeAi, SetSquadHome) through ModContext.Enqueue. Those types are found by name: on Meitou before 0.19 the tab says
// so and nothing else changes.
using System.Numerics;
using System.Reflection;
using System.Globalization;
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

public static class SquadRoles
{
    public sealed record Role(string Label, string? Package, string About);

    // "Patrol the town" (Group patrol town) is left out: in play it never started, as it needs a home in a town and the Home button can only
    // give a building of the player's.
    public static readonly Role[] Roles =
    [
        new("Off (your orders only)", null, "Only follows your orders, as in vanilla."),
        new("Guard home", "2823-gamedata.base", "Guards its home and sleeps there."),
        new("Guard home day and night", "51815-rebirth.mod", "Guards its home in shifts, day and night."),
        new("Patrol nearby (defend only)", "50852-rebirth.mod", "Patrols around its home. Fights only if attacked."),
        new("Rest at home", "96408-rebirth.mod", "Stays at home."),
        new("Wander from town to town", "2675-gamedata.base", "Travels from town to town."),
    ];

    public sealed record Row(int Id, string Label, int Members, bool Autonomous, int Role, string? Home, string? NearId, string? NearName,
        CharacterId Leader, int EscortOf);

    public const string EscortPackage = "5090-gamedata.base";       // Bodyguard: guard and follow the mission target, protect the squad, medic
    const float EscortHours = 24 * 60;
    const string LegacyEscortPrefix = "@escort ";
    const string NextEscortId = "@escort-tag next";
    const string ModId = "LivingWastelandMeitou";
    public static string EscortKey(int squadId) => LegacyEscortPrefix + squadId;
    static string TaggedEscortKey(int id) => FormattableString.Invariant($"@escort-tag {id}");

    sealed class AimTick { public long Tick; }
    static readonly ConditionalWeakTable<Squad, AimTick> aimedAt = new();

    static readonly PropertyInfo? squadTags = typeof(Squad).GetProperty("ModTags") is { CanRead: true } tags &&
        typeof(IDictionary<string, string>).IsAssignableFrom(tags.PropertyType) ? tags : null;

    public static volatile IReadOnlyList<Row> Squads = [];
    public static volatile string? Problem;
    public static ModContext? Context;

    static readonly Type? AutonomyT = Find("Meitou.Simulation.Ai.SetSquadAutonomy"), HomeT = Find("Meitou.Simulation.Ai.SetSquadHome"),
        ChangeT = Find("Meitou.Simulation.Ai.ChangeAi"), ContractT = Find("Meitou.Simulation.Ai.StartContract");
    static readonly PropertyInfo? AutonomousP = typeof(Squad).GetProperty("Autonomous"), HomeP = typeof(Squad).GetProperty("AssignedHome");
    static readonly ConstructorInfo? autonomy = AutonomyT?.GetConstructors()
        .Where(c => c.GetParameters() is [{ ParameterType: var squad }, { ParameterType: var on }, .. var rest] &&
            squad == typeof(int) && on == typeof(bool) && rest.All(p => p.HasDefaultValue))
        .OrderBy(c => c.GetParameters().Length).FirstOrDefault();
    static readonly object?[] autonomyDefaults = autonomy?.GetParameters().Skip(2).Select(p => p.DefaultValue).ToArray() ?? [];
    public static bool Available => autonomy is not null && HomeT is not null && ChangeT is not null && ContractT is not null && AutonomousP is not null && HomeP is not null;

    static Type? Find(string name)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetType(name, false) is { } t) return t;
        return typeof(Squad).Assembly.GetType(name, false);
    }

    static IDictionary<string, string>? Tags(Squad squad) => squadTags?.GetValue(squad) as IDictionary<string, string>;

    static int TagOf(Squad squad) => Tags(squad) is { } tags && tags.TryGetValue(ModId, out var tag) &&
        int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : 0;

    static int Identify(Squad squad, LwCore core, IReadOnlyList<Squad> squads)
    {
        int id = TagOf(squad);
        if (id > 0) return id;
        var tags = Tags(squad);
        if (tags is null || tags.ContainsKey(ModId)) return 0;
        // Account for a save that already has tags but has lost its counter.
        id = 1 + Math.Max(core.Get(NextEscortId), squads.Select(TagOf).DefaultIfEmpty().Max());
        if (id <= 0) return 0;
        tags.Add(ModId, id.ToString(CultureInfo.InvariantCulture));
        core.Set(NextEscortId, id);
        return id;
    }

    internal static void SetEscortPreference(World world, LwCore core, int escort, int target)
    {
        var squads = SquadManagement.PlayerSquads(world);
        if (escort == target || squads.FirstOrDefault(s => s.Id == escort) is not { } from) return;
        var to = squads.FirstOrDefault(s => s.Id == target);
        if (target >= 0 && to is null) return;
        if (squadTags is null)
        {
            core.Set(EscortKey(escort), target + 1);
            return;
        }

        int source = to is null ? TagOf(from) : Identify(from, core, squads);
        int destination = to is null ? 0 : Identify(to, core, squads);
        if (source > 0 && (to is null || destination > 0))
        {
            core.Set(TaggedEscortKey(source), destination);
            aimedAt.Remove(from);
        }
    }

    internal static void ClearLegacyEscorts(LwCore core)
    {
        // A numeric link in an old save lacks evidence of which player squads owned those numbers.
        foreach (var (key, value) in core.Ordered())
            if (value != 0 && key.StartsWith(LegacyEscortPrefix, StringComparison.Ordinal)) core.Set(key, 0);
    }

    /// <summary>The player's squads as the window shows them (read only).</summary>
    public static void Refresh(World world, LwCore core)
    {
        if (!Available) return;
        try
        {
            var rows = new List<Row>();
            var table = world.Characters;
            int player = world.Player.Faction;
            var squads = SquadManagement.PlayerSquads(world);
            var tagged = squadTags is null ? null : squads.Select(s => (Tag: TagOf(s), s.Id))
                .Where(s => s.Tag > 0).GroupBy(s => s.Tag).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Id);
            foreach (var sq in squads)
            {
                int alive = 0;
                string? leaderName = null;
                Vector3? at = null;
                foreach (var id in sq.Members)
                {
                    if (!table.TryResolveNext(id, out int s) || !table.Next[s].Alive || table.Cold(s) is not { Animal: null } c) continue;
                    alive++;
                    if (leaderName is null || id == sq.Leader) { leaderName = c.Name; at = table.Next[s].Position; }
                }
                if (alive == 0) continue;
                bool auto = AutonomousP!.GetValue(sq) is true;
                string? home = HomeP!.GetValue(sq) as string;
                string? package = auto ? CurrentPackage(sq) : null;
                int role = auto ? Math.Max(1, Array.FindIndex(Roles, r => r.Package == package)) : 0;
                if (auto && package is not null && Array.FindIndex(Roles, r => r.Package == package) < 0) role = -1;     // a package not ours
                var (nearId, nearName) = at is { } p ? Nearest(world, player, p) : (null, null);
                string label = sq.Name is { Length: > 0 } n ? n : $"{leaderName}'s squad";
                int escortOf = tagged is null ? core.Get(EscortKey(sq.Id)) - 1 :
                    tagged.GetValueOrDefault(core.Get(TaggedEscortKey(TagOf(sq))), -1);
                var lead = !sq.Leader.IsNone ? sq.Leader : sq.Members.FirstOrDefault();
                rows.Add(new Row(sq.Id, label, alive, auto, role, home is null ? null : HomeName(world, home), nearId, nearName, lead, escortOf));
            }
            // A target can temporarily have no row (for example when its members are off screen). Keep its assignment.
            if (Context is not null)
                foreach (var r in rows.Where(r => r.EscortOf >= 0))
                {
                    if (rows.FirstOrDefault(x => x.Id == r.EscortOf) is not { } target || target.Id == r.Id ||
                        world.Squads.Find(r.Id) is not { } squad) continue;
                    if (aimedAt.TryGetValue(squad, out var last) && world.Tick >= last.Tick && world.Tick - last.Tick < 30 * 10) continue;
                    var (aim, end) = Contract(world, r.Id);
                    if (r.Autonomous && aim == target.Leader && end - world.GameHours > 24) continue;
                    aimedAt.GetOrCreateValue(squad).Tick = world.Tick;
                    Aim(r.Id, target.Leader);
                }
            Squads = rows;
            Problem = null;
        }
        catch (Exception e) { Problem = e.GetBaseException().Message; }
    }

    /// <summary>The squad's running contract: whom it is aimed at, and the game hour it ends (None, 0 when there is none).</summary>
    static (CharacterId, double) Contract(World world, int squadId)
    {
        if (world.Squads.Find(squadId) is not { } sq) return (CharacterId.None, 0);
        object? board = sq.GetType().GetProperty("Ai")?.GetValue(sq);
        if (board is null || board.GetType().GetProperty("Contract")?.GetValue(board) is null) return (CharacterId.None, 0);
        var aim = board.GetType().GetProperty("MissionTarget")?.GetValue(board) is CharacterId c ? c : CharacterId.None;
        double end = board.GetType().GetProperty("ContractEnd")?.GetValue(board) is double d ? d : 0;
        return (aim, end);
    }

    // Meitou added optional Independent/Owner arguments. Reflection needs their defaults supplied explicitly.
    static object? AutonomyCommand(int squadId, bool on) => autonomy?.Invoke([squadId, on, .. autonomyDefaults]);

    /// <summary>Aims an escort: autonomy off and on (the old contract goes without its end talk), then the Bodyguard contract on the leader.</summary>
    static void Aim(int squadId, CharacterId leader)
    {
        Send(AutonomyCommand(squadId, false));
        Send(AutonomyCommand(squadId, true));
        Send(Activator.CreateInstance(ContractT!, squadId, EscortPackage, EscortHours, (CharacterId?)leader));
    }

    static string? CurrentPackage(Squad sq)
    {
        object? board = sq.GetType().GetProperty("Ai")?.GetValue(sq);
        object? current = board?.GetType().GetProperty("Current")?.GetValue(board);
        return current?.GetType().GetProperty("Id")?.GetValue(current) as string;
    }

    /// <summary>The nearest finished building of the player's within 80 m of a point: (its id, its name).</summary>
    static (string?, string?) Nearest(World world, int player, Vector3 at)
    {
        (string? Id, string? Name, float D) best = (null, null, 800);
        foreach (var b in world.Buildings.All)
        {
            if (!b.Complete || b.Owner != player) continue;
            float d = Vector3.Distance(b.Position, at);
            if (d < best.D) best = (b.Id, b.Name is { Length: > 0 } n ? n : "a building", d);
        }
        return (best.Id, best.Name);
    }

    static string HomeName(World world, string key) =>
        world.Buildings.Find(key) is { } b ? (b.Name is { Length: > 0 } n ? n : "a building") : "a town building";

    /// <summary>
    /// Gives any squad a contract (Meitou 0.19+ StartContract): a package aimed at a target character for some hours. Used to send a loaded
    /// pursuer at the player (vanilla's "Assault target squad (agg)"); false where the command does not exist (Meitou before 0.19).
    /// </summary>
    public static bool Contract(int squadId, string package, float hours, CharacterId target)
    {
        if (ContractT is null || Context is null) return false;
        Send(Activator.CreateInstance(ContractT, squadId, package, hours, (CharacterId?)target));
        return true;
    }

    /// <summary>Queues a command for the current tick (Meitou applies them in the order queued).</summary>
    static void Send(object? command)
    {
        if (command is SimCommand c && Context is { } ctx) ctx.Enqueue(c with { Tick = ctx.Tick });
    }

    /// <summary>The choices for a squad's role. Nonnegative choices index Roles. Negative choices encode the target squad ID.</summary>
    public static List<(int Index, string Label)> Choices(int squadId)
    {
        var list = Roles.Select((r, i) => (i, r.Label)).ToList();
        foreach (var target in Squads)
            if (target.Id >= 0 && target.Id != squadId) list.Add((~target.Id, $"Escort {target.Label}"));
        return list;
    }

    // what the player last picked for each squad, and when (wall clock): the window says "starting..." until the squad runs it, and says so
    // when it did not take (UI thread only)
    static readonly Dictionary<int, (string Label, DateTime At)> picked = [];

    /// <summary>What the Role button shows for a squad: what it runs, or what was just picked and is starting, or that it did not take.</summary>
    public static string RoleLabel(Row row)
    {
        string now = row.EscortOf >= 0 ? $"Escort {Squads.FirstOrDefault(x => x.Id == row.EscortOf)?.Label ?? "a squad"}"
            : row.Role >= 0 ? Roles[row.Role].Label : "a package of its own";
        if (!picked.TryGetValue(row.Id, out var p) || p.Label == now) return now;
        return (DateTime.UtcNow - p.At).TotalSeconds < 10 ? $"{p.Label} (starting...)" : $"{now} ({p.Label} did not take)";
    }

    /// <summary>Sets a squad's role from the drop-down: Off turns autonomy off; a package role switches it on and gives the package; an escort
    /// is saved (SetEscort) and aimed at once.</summary>
    public static void SetRole(int squadId, int choice)
    {
        var rows = Squads;
        if (!Available || rows.FirstOrDefault(r => r.Id == squadId) is not { } row) return;
        if (choice >= Roles.Length) return;
        if (choice < 0)
        {
            var target = rows.FirstOrDefault(r => r.Id != squadId && r.Id == ~choice);
            if (target is null) return;
            picked[squadId] = ($"Escort {target.Label}", DateTime.UtcNow);
            Send(new SetEscort(squadId, target.Id) { Tick = 0 });
            Aim(squadId, target.Leader);
            if (LwMod.Settings.LogChanges) LwMod.Log($"squad roles: {row.Label} -> escort {target.Label}");
            return;
        }
        if (row.EscortOf >= 0) Send(new SetEscort(squadId, -1) { Tick = 0 });
        var role = Roles[choice];
        picked[squadId] = (role.Label, DateTime.UtcNow);
        if (role.Package is null) { Send(AutonomyCommand(squadId, false)); return; }
        if (row.EscortOf >= 0) Send(AutonomyCommand(squadId, false));     // drop the escort contract first
        if (!row.Autonomous || row.EscortOf >= 0) Send(AutonomyCommand(squadId, true));
        Send(Activator.CreateInstance(ChangeT!, squadId, role.Package));
        if (LwMod.Settings.LogChanges) LwMod.Log($"squad roles: {row.Label} -> {role.Label} ({role.Package})");
    }

    /// <summary>The window's Home button: the nearest building of the player's becomes the squad's home (again: none).</summary>
    public static void ToggleHome(int squadId)
    {
        if (!Available || Squads.FirstOrDefault(r => r.Id == squadId) is not { } row) return;
        Send(Activator.CreateInstance(HomeT!, squadId, row.Home is not null ? null : row.NearId));
    }
}
