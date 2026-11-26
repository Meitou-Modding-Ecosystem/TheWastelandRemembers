// The squad record in the game's own panels (Meitou 0.23+, docs/modding.md "Character panel lines" and "Item tooltip lines"): a squad
// member's titles and record under their stats in the character panel, and on a keepsake's tooltip how its owner fell and whether they were
// avenged; on a bounty hunter's warrant, which town signed it. Setting "Squad record in the game's panels".
// The sources run on the interface thread every frame and must not touch the world: the squad look (LwDeedsSystem, every PanelEvery ticks)
// publishes what they show (Publish), and they only look it up. Registered by reflection, as OutpostTownPanel, so the 0.17.3 build loads.
using System.Reflection;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class PanelLines
{
    /// <summary>One row: label, value (or none), tooltip (or none).</summary>
    public readonly record struct Row(string Label, string? Value, string? Tooltip = null);

    static WeakReference<World>? squadWorld;  // The serial publisher owns the snapshot; UI reads only the immutable rows.
    static readonly IReadOnlyDictionary<CharacterId, IReadOnlyList<Row>> noMembers = new Dictionary<CharacterId, IReadOnlyList<Row>>();
    static volatile IReadOnlyDictionary<CharacterId, IReadOnlyList<Row>> squad = noMembers;
    static readonly IReadOnlyDictionary<string, IReadOnlyList<Row>> noFallen = new Dictionary<string, IReadOnlyList<Row>>(StringComparer.Ordinal);
    static volatile IReadOnlyDictionary<string, IReadOnlyList<Row>> fallen = noFallen;

    // ---- what the panels show (pure: the squad record only) ----

    /// <summary>A squad member's rows: their titles, and their record once they have one.</summary>
    public static List<Row> MemberRows(SquadRecord record, string key)
    {
        var rows = new List<Row>();
        if (!record.Members.TryGetValue(key, out var m)) return rows;
        if (m.Titles.Count > 0)
        {
            var names = m.Titles.Select(record.TitleName).ToList();
            rows.Add(new Row("Known as", names[^1], names.Count > 1 ? "Titles: " + string.Join(", ", names) + "." : null));
        }
        if (m.Kills > 0 || m.Fights > 0)
            rows.Add(new Row("Record", $"{m.Kills} {(m.Kills == 1 ? "kill" : "kills")}, {m.FightsWon} of {m.Fights} fights won"));
        return rows;
    }

    /// <summary>A keepsake's rows: how and where its owner fell, and whether they were avenged.</summary>
    public static List<Row> FallenRows(Fallen f, Func<string, string> display)
    {
        int day = (int)(f.Hours / 24);
        var rows = new List<Row>
        {
            new(f.Killer is null && f.Cause is { } how ? how : "Fell", $"day {day} at {f.Place}"),
        };
        if (f.Killer is { } k) rows.Add(new Row("Killed by", string.IsNullOrEmpty(f.KillerFaction) ? k : $"{k} ({f.KillerFaction})"));
        rows.Add(f.AvengedBy is { } a ? new Row("Avenged by", display(a)) : new Row("Not yet avenged", null));
        return rows;
    }

    // ---- published by the squad look, read by the panels ----

    /// <summary>Called from the squad look: what the panels may show until the next look.</summary>
    internal static void Publish(World world, LwDeeds deeds)
    {
        var members = new List<(CharacterId Id, string Key)>();
        if (LwMod.Settings.RecordInPanels)
        {
            var table = world.Characters;
            for (int s = 0; s < table.HighWater; s++)
                if (table.Cold(s) is { IsPlayer: true, Animal: null } c && c.Medical is not { Dead: true } && !OtherModsPeople.Runs(world, c))
                    members.Add((table.IdOf(s), deeds.Who(c)));
        }
        Publish(deeds.Record, members, world);
    }

    internal static void Publish(SquadRecord record, List<(CharacterId Id, string Key)> members, World world)
    {
        if (!LwMod.Settings.RecordInPanels) { squad = noMembers; return; }
        if (squadWorld is null) squadWorld = new WeakReference<World>(world);
        else if (!squadWorld.TryGetTarget(out var publisher) || !ReferenceEquals(publisher, world)) squadWorld.SetTarget(world);
        var next = new Dictionary<CharacterId, IReadOnlyList<Row>>();
        foreach (var (id, key) in members)
        {
            var rows = MemberRows(record, key);
            if (rows.Count > 0) next[id] = rows;
        }
        var dead = new Dictionary<string, IReadOnlyList<Row>>(StringComparer.Ordinal);
        foreach (var f in record.FallenMembers)
        {
            var rows = FallenRows(f, record.Display);
            dead[f.Name] = rows;                                             // by key (a keepsake's tag)
            dead["Keepsake of " + record.Display(f.Name)] = rows;              // by the keepsake's name
        }
        squad = next;
        fallen = dead;
    }

    internal static void Reset()
    {
        squadWorld = null;
        squad = noMembers;
        fallen = noFallen;
    }

    internal static void Forget(World world, CharacterId id)
    {
        if (squadWorld?.TryGetTarget(out var publisher) != true || !ReferenceEquals(publisher, world) || !squad.ContainsKey(id)) return;
        var remaining = new Dictionary<CharacterId, IReadOnlyList<Row>>(squad);
        remaining.Remove(id);
        squad = remaining;
    }

    internal static IReadOnlyList<Row>? ForCharacter(CharacterId id) => squad.TryGetValue(id, out var rows) ? rows : null;

    internal static IReadOnlyList<Row>? ForItem(string record, string name, string? tag)
    {
        if (!LwMod.Settings.RecordInPanels) return null;
        if (Trophies.Warrant is { } warrant && record == warrant && !string.IsNullOrEmpty(tag))
            return [new Row("Signed in", tag)];
        if (!KeepsakeBook.Slots.Contains(record)) return null;
        var dead = fallen;
        if (KeepsakeBook.FallenKeyOf(tag) is { } key && dead.TryGetValue(key, out var byKey)) return byKey;
        return dead.TryGetValue(name, out var byName) ? byName : null;
    }

    // ---- registration (Meitou 0.23+) ----

    static readonly MethodInfo? addCharacter = typeof(UiRegistration).GetMethod("AddCharacterPanelLines");
    static readonly MethodInfo? addTooltip = typeof(UiRegistration).GetMethod("AddItemTooltipLines");

    /// <summary>Whether this client has both hooks.</summary>
    public static bool Available => addCharacter is not null && addTooltip is not null;

    internal static bool Register(UiRegistration registration, Action<string> log)
    {
        if (!Available) return false;
        try
        {
            addCharacter!.Invoke(registration, [Source(addCharacter, nameof(CharacterLines))]);
            addTooltip!.Invoke(registration, [Source(addTooltip, nameof(TooltipLines))]);
            log("panels: the squad record in the character panel and keepsake tooltips");
            return true;
        }
        catch (Exception e)
        {
            log($"panels: not added ({e.GetBaseException().Message})");
            return false;
        }
    }

    /// <summary>A Func&lt;TContext, IEnumerable&lt;TLine&gt;&gt; of the hook's own types, calling our generic source.</summary>
    static Delegate Source(MethodInfo add, string source)
    {
        var func = add.GetParameters().Single().ParameterType;                         // Func<TContext, IEnumerable<TLine>>
        var types = func.GetGenericArguments();
        var line = types[1].GetGenericArguments().Single();
        var method = typeof(PanelLines).GetMethod(source, BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(types[0], line);
        return Delegate.CreateDelegate(func, method);
    }

    static IEnumerable<TLine> CharacterLines<TContext, TLine>(TContext context)
    {
        if (context is null || ForCharacter(Prop<CharacterId>(context, "Id")) is not { } rows) return [];
        // UiCharacterLine(Label, Value, Bar, Tooltip, Press)
        return rows.Select(r => (TLine)Make<TLine>(r.Label, r.Value, null, r.Tooltip, null)).ToList();
    }

    static IEnumerable<TLine> TooltipLines<TContext, TLine>(TContext context)
    {
        if (context is null || ForItem(Prop<string>(context, "Record") ?? "", Prop<string>(context, "Name") ?? "", Prop<string>(context, "ModTag")) is not { } rows)
            return [];
        // UiItemTooltipLine(Label, Value)
        return rows.Select(r => (TLine)Make<TLine>(r.Label, r.Value)).ToList();
    }

    static readonly Dictionary<(Type, string), PropertyInfo?> props = [];
    static T? Prop<T>(object o, string name)
    {
        PropertyInfo? p;
        lock (props) if (!props.TryGetValue((o.GetType(), name), out p)) props[(o.GetType(), name)] = p = o.GetType().GetProperty(name);
        return p?.GetValue(o) is T v ? v : default;
    }

    static readonly Dictionary<Type, ConstructorInfo> ctors = [];
    static object Make<TLine>(params object?[] args)
    {
        ConstructorInfo? c;
        lock (ctors)
            if (!ctors.TryGetValue(typeof(TLine), out c))
                ctors[typeof(TLine)] = c = typeof(TLine).GetConstructors().First(k => k.GetParameters().Length == args.Length);
        return c.Invoke(args);
    }
}
