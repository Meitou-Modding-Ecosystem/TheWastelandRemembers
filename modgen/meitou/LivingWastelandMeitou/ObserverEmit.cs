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
    public static Type? Build(Type observer, string method, MethodInfo target, string name)
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
        lock (gate)
        {
            var tb = module.DefineType($"LivingWasteland.MeitouMod.{name}{Guid.NewGuid():N}", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(object), [typeof(ITickSystem), observer]);
            tb.DefineDefaultConstructor(MethodAttributes.Public);
            var impl = tb.DefineMethod(m.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot, CallingConventions.HasThis, m.ReturnType, null, null, ps.Select(p => p.ParameterType).ToArray(),
                ps.Select(p => p.GetRequiredCustomModifiers()).ToArray(), ps.Select(p => p.GetOptionalCustomModifiers()).ToArray());
            for (int i = 0; i < ps.Length; i++) impl.DefineParameter(i + 1, ps[i].Attributes, ps[i].Name);
            var il = impl.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            if (byRef) il.Emit(OpCodes.Ldobj, arg);
            if (arg.IsValueType) il.Emit(OpCodes.Box, arg);
            if (priceFilter) il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(impl, m);
            return tb.CreateType();
        }
    }
}
