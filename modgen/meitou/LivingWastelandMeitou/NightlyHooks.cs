// Three features on hooks Meitou added in v0.20.2-nightly.20261117, each behind a setting (off by default, not play-tested) and found by
// reflection, so the mod still builds against and loads on Meitou 0.17.3 and later unchanged:
//
// * Map marks (UiRegistration.AddMapMarkers): rivals still about, outlaw camps the Atlas knows, places of news heard this week. The map asks
//   on the host thread every frame it shows, so the marks are worked out in a slow tick (NewsSystem) and published here.
// * "Ask for news" (UiRegistration.AddCharacterMenuVerbs): a row in a townsperson's right-click menu. The menu is built on the host thread
//   and must not touch the world, so choosing it only queues the ask; NewsSystem answers in its tick (a bark, and the news is heard).
// * Relation turns (IRelationChangeObserver): when a faction's view of the squad crosses one of the game's thresholds, History says who and
//   why. The engine finds observers among registered systems by interface; this mod can't name that interface at build time, so a small
//   system type implementing it is made at load (System.Reflection.Emit) and forwards each change to RelationWatch.Changed.
// The pure part: NightlyRules.cs.
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class NightlyHooks
{
    /// <summary>The marks to draw, worked out in NewsSystem's slow tick (empty when the setting is off).</summary>
    public static volatile IReadOnlyList<MapMark> Marks = [];

    /// <summary>Town centres (x, z) for the menu's "near a town" test, set when news first sees the world's data.</summary>
    public static volatile (float X, float Z)[] Towns = [];

    /// <summary>People the player asked for news, waiting for NewsSystem's tick.</summary>
    public static readonly ConcurrentQueue<CharacterId> Asks = new();

    /// <summary>Adds the map marks and the menu row where the engine has them. Never throws; logs what happened.</summary>
    public static void RegisterUi(object registration, Action<string> log)
    {
        try { RegisterMarks(registration, log); } catch (Exception e) { log($"map marks: not added ({e.GetBaseException().Message})"); }
        try { RegisterAsk(registration, log); } catch (Exception e) { log($"ask for news: not added ({e.GetBaseException().Message})"); }
    }

    // ---- map marks ----

    static void RegisterMarks(object registration, Action<string> log)
    {
        var add = registration.GetType().GetMethod("AddMapMarkers");
        if (add is null) { log("map marks: this Meitou has no map markers for mods (v0.20.2-nightly.20261117 and later do)"); return; }
        var funcType = add.GetParameters()[0].ParameterType;                 // Func<UiMapContext, IEnumerable<UiMapMarker>>
        var contextType = funcType.GetGenericArguments()[0];
        var markerType = funcType.GetGenericArguments()[1].GetGenericArguments()[0];
        var ctor = markerType.GetConstructor([typeof(Vector2), typeof(string), typeof(Vector4), typeof(string), typeof(string), typeof(int), typeof(float)]);
        if (ctor is null) { log("map marks: the engine's marker type changed; none added"); return; }
        var m = Expression.Parameter(typeof(MapMark), "m");
        var at = Expression.New(typeof(Vector2).GetConstructor([typeof(float), typeof(float)])!, Expression.Property(m, nameof(MapMark.X)), Expression.Property(m, nameof(MapMark.Z)));
        var make = Expression.Lambda<Func<MapMark, object>>(Expression.Convert(Expression.New(ctor, at,
            Expression.Call(typeof(NightlyHooks).GetMethod(nameof(Group), BindingFlags.NonPublic | BindingFlags.Static)!, m),
            Expression.Call(typeof(NightlyHooks).GetMethod(nameof(Tint), BindingFlags.NonPublic | BindingFlags.Static)!, m),
            Expression.Property(m, nameof(MapMark.Label)), Expression.Property(m, nameof(MapMark.Tooltip)),
            Expression.Property(m, nameof(MapMark.FromLevel)), Expression.Constant(1f)), typeof(object)), m).Compile();
        var ctx = Expression.Parameter(contextType, "context");
        var marks = Expression.Call(typeof(NightlyHooks).GetMethod(nameof(Markers), BindingFlags.NonPublic | BindingFlags.Static)!, Expression.Constant(make));
        var cast = Expression.Call(typeof(Enumerable).GetMethod(nameof(Enumerable.Cast))!.MakeGenericMethod(markerType), marks);
        add.Invoke(registration, [Expression.Lambda(funcType, cast, ctx).Compile()]);
        log("map marks: added (setting \"Marks on the world map\")");
    }

    static IEnumerable<object> Markers(Func<MapMark, object> make) => LwMod.Settings.MapMarks ? Marks.Select(make) : [];

    static string Group(MapMark m) => MapMarkRules.Look(m.Kind).Group;

    static Vector4 Tint(MapMark m)
    {
        var (_, r, g, b) = MapMarkRules.Look(m.Kind);
        return new Vector4(r, g, b, 1);
    }

    // ---- ask for news ----

    static void RegisterAsk(object registration, Action<string> log)
    {
        var add = registration.GetType().GetMethod("AddCharacterMenuVerbs");
        if (add is null) { log("ask for news: this Meitou has no menu rows for mods (v0.20.2-nightly.20261117 and later do)"); return; }
        var funcType = add.GetParameters()[0].ParameterType;                 // Func<UiMenuContext, IEnumerable<UiMenuVerb>>
        var contextType = funcType.GetGenericArguments()[0];
        var verbType = funcType.GetGenericArguments()[1].GetGenericArguments()[0];
        var verbCtor = verbType.GetConstructor([typeof(string), typeof(Action)]);
        var targetProp = contextType.GetProperty("Target");
        var snap = targetProp?.PropertyType;
        if (verbCtor is null || snap?.GetProperty("Id") is null || snap.GetProperty("IsPlayer") is null || snap.GetProperty("Position") is null)
        {
            log("ask for news: the engine's menu types changed; not added");
            return;
        }
        var ctx = Expression.Parameter(contextType, "context");
        var target = Expression.Property(ctx, targetProp!);
        var id = Expression.Property(target, "Id");
        Expression fights = snap.GetProperty("FightsPlayer") is { } f ? Expression.Property(target, f) : Expression.Constant(false);
        var offer = Expression.Call(typeof(NightlyHooks).GetMethod(nameof(Offer), BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Property(target, "IsPlayer"), fights, Expression.Property(target, "Position"));
        var choose = Expression.Lambda<Action>(Expression.Call(Expression.Constant(Asks), typeof(ConcurrentQueue<CharacterId>).GetMethod("Enqueue")!, id));
        var rows = Expression.NewArrayInit(verbType, Expression.New(verbCtor, Expression.Constant(NewsAskRules.Caption), choose));
        var none = Expression.NewArrayInit(verbType);
        var body = Expression.Convert(Expression.Condition(Expression.AndAlso(Expression.NotEqual(target, Expression.Constant(null, snap)), offer), rows, none),
            funcType.GetGenericArguments()[1]);
        add.Invoke(registration, [Expression.Lambda(funcType, body, ctx).Compile()]);
        log("ask for news: added to townsfolk's right-click menu (setting \"Ask townsfolk for news\")");
    }

    static bool Offer(bool isPlayer, bool fightsPlayer, Vector3 at)
    {
        if (!LwMod.Settings.AskForNews) return false;
        float best = float.MaxValue;
        foreach (var (x, z) in Towns) best = MathF.Min(best, (x - at.X) * (x - at.X) + (z - at.Z) * (z - at.Z));
        return NewsAskRules.Offer(isPlayer, fightsPlayer, MathF.Sqrt(best));
    }
}

/// <summary>Faction relation changes from the engine (IRelationChangeObserver), as History lines when one crosses a threshold.</summary>
public static class RelationWatch
{
    static PropertyInfo? faction, towards, before, after, cause, causeType, deed;

    /// <summary>Registers the observer where the engine has the hook. Never throws; logs what happened.</summary>
    public static void Register(ModContext c)
    {
        try
        {
            var observer = typeof(World).Assembly.GetType("Meitou.Simulation.Factions.IRelationChangeObserver");
            if (observer is null) { c.Log("relation turns: this Meitou has no relation observer (v0.20.2-nightly.20261117 and later do)"); return; }
            if (Build(observer) is not { } type) { c.Log("relation turns: the engine's observer changed; not added"); return; }
            c.AddSystem("lw-relations", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
            c.Log("relation turns: watching faction relations (setting \"Factions' change of heart in History\")");
        }
        catch (Exception e) { c.Log($"relation turns: not added ({e.GetBaseException().Message})"); }
    }

    /// <summary>A system type implementing ITickSystem and the engine's relation observer, forwarding each change to <see cref="Changed"/>.
    /// Null when the observer's shape is not the one expected.</summary>
    public static Type? Build(Type observer) =>
        ObserverEmit.Build(observer, "RelationChanged", typeof(RelationWatch).GetMethod(nameof(Changed))!, "RelationWatchSystem");

    /// <summary>One relation change (the engine's RelationChange, boxed). Called serially inside the step that made it.</summary>
    public static void Changed(World world, object change)
    {
        if (!LwMod.Settings.RelationNews) return;
        try
        {
            if (faction is null)
            {
                var t = change.GetType();
                (faction, towards, before, after, cause) = (t.GetProperty("Faction"), t.GetProperty("Towards"), t.GetProperty("Before"), t.GetProperty("After"), t.GetProperty("Cause"));
                causeType = cause?.PropertyType.GetProperty("Type");
                deed = cause?.PropertyType.GetProperty("Deed");
            }
            if (faction?.GetValue(change) is not int f || towards?.GetValue(change) is not int to || before?.GetValue(change) is not float b
                || after?.GetValue(change) is not float a) return;
            if (to != world.Player.Faction || f == to || LwDeeds.For(world) is not { } deeds) return;
            string name = deeds.FactionName(f);
            if (name.Length == 0 || Meitou.Simulation.Factions.PlayerRelations.Notice(name, b, a) is null) return;    // no threshold crossed
            var c = cause?.GetValue(change);
            string type = c is null ? "" : causeType?.GetValue(c)?.ToString() ?? "";
            string what = c is null ? "" : deed?.GetValue(c)?.ToString() ?? "";
            if (RelationRules.Line(name, a < b, type, what) is not { } line) return;
            deeds.Record.Note(line);
            RecapLink.Moment(line, "the wastes", 55);
            LwMod.Log($"relation turns: {line} ({b:0} to {a:0}, {type} {what})");
            LogTally.Add("relation turns");
        }
        catch (Exception e) { LwMod.Log($"relation turns: skipped a change ({e.GetBaseException().Message})"); }
    }
}

/// <summary>
/// Makes, at load, a system type for an engine observer interface this mod can't name at build time (it builds against Meitou 0.17.3): it
/// implements ITickSystem and the interface, and its one method <c>M(World, in T)</c> boxes the struct and calls a static
/// <c>target(World, object)</c> of this mod. The engine finds observers among registered systems by interface.
/// </summary>
public static class ObserverEmit
{
    static readonly ModuleBuilder module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LivingWastelandObservers"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Observers");
    static readonly object gate = new();

    /// <summary>The type, or null when the interface's method is not <c>void M(World, in T)</c>.</summary>
    public static Type? Build(Type observer, string method, MethodInfo target, string name)
    {
        var m = observer.GetMethod(method);
        if (m is null || m.ReturnType != typeof(void) || m.GetParameters() is not [{ ParameterType: var w }, { ParameterType: { IsByRef: true } byRef }]
            || w != typeof(World)) return null;
        var arg = byRef.GetElementType()!;
        lock (gate)
        {
            var tb = module.DefineType($"LivingWasteland.MeitouMod.{name}{Guid.NewGuid():N}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(object), [typeof(ITickSystem), observer]);
            tb.DefineDefaultConstructor(MethodAttributes.Public);
            var ps = m.GetParameters();
            var impl = tb.DefineMethod(m.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot, CallingConventions.HasThis, m.ReturnType, null, null, ps.Select(p => p.ParameterType).ToArray(),
                ps.Select(p => p.GetRequiredCustomModifiers()).ToArray(), ps.Select(p => p.GetOptionalCustomModifiers()).ToArray());
            for (int i = 0; i < ps.Length; i++) impl.DefineParameter(i + 1, ps[i].Attributes, ps[i].Name);
            var il = impl.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldobj, arg);
            il.Emit(OpCodes.Box, arg);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(impl, m);
            return tb.CreateType();
        }
    }
}
