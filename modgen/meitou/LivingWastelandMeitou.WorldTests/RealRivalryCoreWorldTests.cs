using System.Collections;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Modding;
using Meitou.Simulation;
using Xunit;

// TWR's party claim (RivalryLink.cs) against the real Rivalry Core: a published, checksummed build from RivalryCore
// (the pinned production release rivalry-core-v<version>, see release_check.sh), loaded from RIVALRY_CORE_DIR. Rivalry Core targets .NET 11,
// so these run only against .NET 11 Meitou builds; without the variable they return early and report as passed. The system is made as Rivalry Core's own tests make it
// (RivalrySystem.Create, by reflection: TWR never references Rivalry Core).
[Collection("settings")]
public class RealRivalryCoreWorldTests
{
    static Assembly? Rivalry()
    {
        var dir = Environment.GetEnvironmentVariable("RIVALRY_CORE_DIR");
        var path = string.IsNullOrEmpty(dir) ? "" : Path.Combine(dir, "RivalryCore.dll");
        if (!File.Exists(path)) return null;
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RivalryCore") ?? Assembly.LoadFrom(path);
    }

    static ITickSystem Make(Assembly rivalry)
    {
        var create = rivalry.GetType("Rivalry.RivalrySystem")!.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var data = W.Data([]);
        var args = create.GetParameters().Select(p =>
        {
            var t = p.ParameterType;
            if (t.Name == "SystemBuildContext")
            {
                var ctor = t.GetConstructors().First(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType.IsInstanceOfType(data));
                return ctor.Invoke(ctor.GetParameters().Select((q, i) => i == 0 ? data : Empty(q.ParameterType) ?? (q.HasDefaultValue ? q.DefaultValue : null)).ToArray());
            }
            if (t.IsEnum) return Enum.ToObject(t, 0x80001234u);
            if (t == typeof(string)) return "rivalry.core";
            if (t == typeof(Action<string>)) return (Action<string>)(_ => { });
            if (typeof(Delegate).IsAssignableFrom(t))                     // the registration callback: register nothing
            {
                var ps = t.GetMethod("Invoke")!.GetParameters().Select(q => Expression.Parameter(q.ParameterType)).ToArray();
                return Expression.Lambda(t, Expression.Empty(), ps).Compile();
            }
            return p.HasDefaultValue ? p.DefaultValue : Activator.CreateInstance(t);
        }).ToArray();
        return (ITickSystem)create.Invoke(null, args)!;
    }

    /// <summary>An empty array for an array or generic collection type (the build context's systems), else null.</summary>
    static object? Empty(Type t) =>
        t.IsArray ? Array.CreateInstance(t.GetElementType()!, 0)
        : t.IsGenericType && t.GetGenericArguments() is [var e] && t.IsAssignableFrom(e.MakeArrayType()) ? Array.CreateInstance(e, 0) : null;

    static int Hostile(object system)
    {
        var api = system.GetType().GetInterface("Rivalry.IRivalryState")!;
        var kind = api.GetMethod("PartiesOut")!.GetParameters()[0].ParameterType;
        return (int)api.GetMethod("PartiesOut")!.Invoke(system, [Enum.Parse(kind, "Hostile")])!;
    }

    static List<string> Sources(object system) =>
        ((IEnumerable)system.GetType().GetInterface("Rivalry.IRivalryState")!.GetProperty("Parties")!.GetValue(system)!)
            .Cast<object>().Select(p => (string)p.GetType().GetProperty("Source")!.GetValue(p)!).ToList();

    /// <summary>Another mod's hostile claim, made the way any dependent of Rivalry Core makes one.</summary>
    static bool OtherModClaims(object system, string key)
    {
        var claim = system.GetType().GetInterface("Rivalry.IRivalryState")!.GetMethod("ClaimParty")!;
        var ctor = claim.GetParameters()[0].ParameterType.GetConstructors().First(c => c.GetParameters().Length == 5);
        var kind = ctor.GetParameters()[1].ParameterType;
        return (bool)claim.Invoke(system, [ctor.Invoke(["ashen.hand.hunters", Enum.Parse(kind, "Hostile"), key, 6.0, -1])])!;
    }

    [Fact]
    public void Our_crews_and_Rivalry_Cores_parties_share_its_one_hostile_slot()
    {
        if (Rivalry() is not { } rivalry) return;
        var system = Make(rivalry);
        var world = W.World(system);
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        world.RunTicks(2);

        string crew = PartyClaims.Crew("Squin", 10);
        Assert.True(RivalryLink.MaySend(world, crew));
        Assert.Equal(1, Hostile(system));
        Assert.Equal([RivalryLink.Source], Sources(system));
        Assert.False(RivalryLink.MaySend(world, PartyClaims.Rival("Hollin One-Arm")));   // our own second party waits too
        Assert.False(OtherModClaims(system, "Leaf"));                                   // and another mod's hit squad

        RivalryLink.Keep(world, "crew:", []);                                            // the crew ended
        Assert.Equal(0, Hostile(system));
        Assert.True(OtherModClaims(system, "Leaf"));
        Assert.False(RivalryLink.MaySend(world, crew));                                  // now ours waits for theirs
    }

    [Fact]
    public void Our_claim_comes_back_from_Rivalry_Cores_save()
    {
        if (Rivalry() is not { } rivalry) return;
        var system = Make(rivalry);
        var world = W.World(system);
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        world.RunTicks(2);
        string crew = PartyClaims.Crew("Squin", 10);
        Assert.True(RivalryLink.MaySend(world, crew));

        var saved = new MemoryStream();
        ((IModSaveData)system).Write(saved);
        saved.Position = 0;
        var loaded = Make(rivalry);
        ((IModSaveData)loaded).Read(saved);
        var world2 = W.World(loaded);
        W.Spawn(world2, "Leaf", W.Player, new Vector3(0, 150, 0));
        world2.RunTicks(2);

        Assert.Equal(1, Hostile(loaded));
        Assert.True(RivalryLink.MaySend(world2, crew));                                  // still ours: renewed
        Assert.False(OtherModClaims(loaded, "Leaf"));
    }
}
