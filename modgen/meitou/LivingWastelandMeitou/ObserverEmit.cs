using System.Reflection;
using System.Reflection.Emit;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

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

    /// <summary>Adapts a two-argument observer or three-argument price filter. Boxes the payload and passes the price through unchanged.</summary>
    public static Type? Build(Type observer, string method, MethodInfo target, string name) =>
        Build(observer, method, target, name, null);

    internal static Type? Build(Type observer, string method, MethodInfo target, string name, (Type Hook, MethodInfo Target)? removal)
    {
        if (NoticeForwarder(observer, method, target) is not { } main) return null;
        return Emit(observer, main.Method, name, main.Body, removal is { } veto ? RemovalForwarder(veto.Hook, veto.Target) : null);
    }

    internal static Type? BuildPair(Type observer, string method, MethodInfo target, string name,
        Type secondary, string secondaryMethod, MethodInfo secondaryTarget)
    {
        if (NoticeForwarder(observer, method, target) is not { } first || NoticeForwarder(secondary, secondaryMethod, secondaryTarget) is not { } second)
            return null;
        return Emit(observer, first.Method, name, first.Body, second);
    }

    static Forwarder? NoticeForwarder(Type observer, string method, MethodInfo target)
    {
        var m = observer.GetMethod(method);
        bool priceFilter = target.ReturnType == typeof(long);
        if (m is null || m.ReturnType != target.ReturnType || (!priceFilter && m.ReturnType != typeof(void))) return null;
        var ps = m.GetParameters();
        if (ps.Length != (priceFilter ? 3 : 2) || ps[0].ParameterType != typeof(World) ||
            (priceFilter && ps[2].ParameterType != typeof(long))) return null;
        var second = ps[1].ParameterType;
        bool byRef = second.IsByRef;
        var arg = byRef ? second.GetElementType()! : second;
        return new Forwarder(m, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            if (byRef) il.Emit(OpCodes.Ldobj, arg);
            if (arg.IsValueType) il.Emit(OpCodes.Box, arg);
            if (priceFilter) il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, target);
        });
    }

    readonly record struct Forwarder(MethodInfo Method, Action<ILGenerator> Body);

    /// <summary>Adapts the removal hook without boxing its payload in the off-screen population step.</summary>
    static Forwarder? RemovalForwarder(Type observer, MethodInfo target)
    {
        var m = observer.GetMethod("Keep");
        if (m?.ReturnType != typeof(bool) || m.GetParameters() is not [var world, var request] ||
            world.ParameterType != typeof(World) || request.ParameterType.GetElementType() is not { IsValueType: true } ask ||
            !request.ParameterType.IsByRef) return null;
        if (ask.GetProperty("Kind") is not { PropertyType.IsEnum: true, GetMethod: { } kind } ||
            ask.GetProperty("Platoon") is not { GetMethod: { } platoon } id || id.PropertyType != typeof(int)) return null;

        return new Forwarder(m, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, kind);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, platoon);
            il.Emit(OpCodes.Call, target);
        });
    }

    static Type Emit(Type observer, MethodInfo m, string name, Action<ILGenerator> body, Forwarder? extra)
    {
        lock (gate)
        {
            var tb = module.DefineType($"LivingWasteland.MeitouMod.{name}{Guid.NewGuid():N}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(object), [typeof(ITickSystem), observer]);
            tb.DefineDefaultConstructor(MethodAttributes.Public);
            DefineForwarder(tb, m, body);
            if (extra is { } forwarder)
            {
                tb.AddInterfaceImplementation(forwarder.Method.DeclaringType!);
                DefineForwarder(tb, forwarder.Method, forwarder.Body);
            }
            return tb.CreateType();
        }
    }

    static void DefineForwarder(TypeBuilder tb, MethodInfo method, Action<ILGenerator> body)
    {
        var ps = method.GetParameters();
        var impl = tb.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig
            | MethodAttributes.NewSlot, CallingConventions.HasThis, method.ReturnType, null, null, ps.Select(p => p.ParameterType).ToArray(),
            ps.Select(p => p.GetRequiredCustomModifiers()).ToArray(), ps.Select(p => p.GetOptionalCustomModifiers()).ToArray());
        for (int i = 0; i < ps.Length; i++) impl.DefineParameter(i + 1, ps[i].Attributes, ps[i].Name);
        var il = impl.GetILGenerator();
        body(il);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(impl, method);
    }
}
