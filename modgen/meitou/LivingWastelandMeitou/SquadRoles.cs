// Squad roles (Living Wasteland only; the Squads tab of the Squad record window): Meitou 0.19's autonomous player squads
// (docs/simulation.md "Autonomous player squads"): a player squad switched autonomous runs a vanilla AI package, the way NPC squads do; the
// player's orders and jobs always come first and the squad picks the package up again after them; switch, package and home are saved with
// the squad by Meitou itself.
//
// The roles are vanilla packages (the game's own records): guard the home, guard it day and night, patrol nearby (defending only),
// rest at home, wander from town to town; and escort another of the player's squads: the vanilla "Bodyguard" contract (what a hired
// mercenary runs) aimed at that squad's leader, so it follows the squad and fights for it. Meitou keeps no contract target over a save and a
// running contract cannot be re-aimed, so the escort is ours to keep: which squad escorts which is saved in LwCore ("@escort <id>"), and
// Refresh aims it again (autonomy off and on, which drops the old contract silently, then the contract) after a load, when the leader changes
// or before the contract runs out. A home is the nearest finished building of the player's to the squad leader.
//
// The simulation side (Refresh, every second) only reads and publishes a snapshot; the window's buttons queue Meitou's own commands
// (SetSquadAutonomy, ChangeAi, SetSquadHome) through ModContext.Enqueue. Those types are found by name: on Meitou before 0.19 the tab says
// so and nothing else changes.
using System.Numerics;
using System.Reflection;
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
    public static string EscortKey(int squadId) => $"@escort {squadId}";
    static readonly Dictionary<int, long> aimedAt = [];

    public static volatile IReadOnlyList<Row> Squads = [];
    public static volatile string? Problem;
    public static ModContext? Context;

    static readonly Type? AutonomyT = Find("Meitou.Simulation.Ai.SetSquadAutonomy"), HomeT = Find("Meitou.Simulation.Ai.SetSquadHome"),
        ChangeT = Find("Meitou.Simulation.Ai.ChangeAi"), ContractT = Find("Meitou.Simulation.Ai.StartContract");
    static readonly PropertyInfo? AutonomousP = typeof(Squad).GetProperty("Autonomous"), HomeP = typeof(Squad).GetProperty("AssignedHome");
    public static bool Available => AutonomyT is not null && HomeT is not null && ChangeT is not null && ContractT is not null && AutonomousP is not null && HomeP is not null;

    static Type? Find(string name)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetType(name, false) is { } t) return t;
        return typeof(Squad).Assembly.GetType(name, false);
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
            foreach (var sq in SquadManagement.PlayerSquads(world))
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
                int escortOf = core.Get(EscortKey(sq.Id)) - 1;
                var lead = !sq.Leader.IsNone ? sq.Leader : sq.Members.FirstOrDefault();
                rows.Add(new Row(sq.Id, label, alive, auto, role, home is null ? null : HomeName(world, home), nearId, nearName, lead, escortOf));
            }
            // escorts: forget one whose target squad is gone; aim one whose contract is not aimed at its target's leader, or runs out soon
            foreach (var r in rows.Where(r => r.EscortOf >= 0))
            {
                if (rows.FirstOrDefault(x => x.Id == r.EscortOf) is not { } target || target.Id == r.Id) { core.Set(EscortKey(r.Id), 0); continue; }
                if (aimedAt.TryGetValue(r.Id, out long last) && world.Tick - last < 30 * 10 && world.Tick >= last) continue;
                var (aim, end) = Contract(world, r.Id);
                if (r.Autonomous && aim == target.Leader && end - world.GameHours > 24) continue;
                aimedAt[r.Id] = world.Tick;
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

    /// <summary>Aims an escort: autonomy off and on (the old contract goes without its end talk), then the Bodyguard contract on the leader.</summary>
    static void Aim(int squadId, CharacterId leader)
    {
        Send(Activator.CreateInstance(AutonomyT!, squadId, false));
        Send(Activator.CreateInstance(AutonomyT!, squadId, true));
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

    /// <summary>The choices for a squad's role: the seven roles, then escorting each of the player's other squads.</summary>
    public static List<(int Index, string Label)> Choices(int squadId)
    {
        var list = Roles.Select((r, i) => (i, r.Label)).ToList();
        foreach (var (o, i) in Squads.Where(r => r.Id != squadId).Select((o, i) => (o, i))) list.Add((Roles.Length + i, $"Escort {o.Label}"));
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
        var others = rows.Where(r => r.Id != squadId).ToList();
        if (choice < 0 || choice >= Roles.Length + others.Count) return;
        if (row.EscortOf >= 0 && choice < Roles.Length) Send(new SetEscort(squadId, -1) { Tick = 0 });
        if (choice >= Roles.Length)
        {
            var target = others[choice - Roles.Length];
            picked[squadId] = ($"Escort {target.Label}", DateTime.UtcNow);
            Send(new SetEscort(squadId, target.Id) { Tick = 0 });
            Aim(squadId, target.Leader);
            if (LwMod.Settings.LogChanges) LwMod.Log($"squad roles: {row.Label} -> escort {target.Label}");
            return;
        }
        var role = Roles[choice];
        picked[squadId] = (role.Label, DateTime.UtcNow);
        if (role.Package is null) { Send(Activator.CreateInstance(AutonomyT!, squadId, false)); return; }
        if (row.EscortOf >= 0) Send(Activator.CreateInstance(AutonomyT!, squadId, false));     // drop the escort contract first
        if (!row.Autonomous || row.EscortOf >= 0) Send(Activator.CreateInstance(AutonomyT!, squadId, true));
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
