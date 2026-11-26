using System.Linq.Expressions;
using Meitou.Modding;

namespace LivingWasteland.MeitouMod;

internal static class MoonscriptLink
{
    const string HostId = "pza.moonscript";
    const string Empty = "No world yet.";

    internal static void Register(ModContext context)
    {
        try
        {
            var type = typeof(ModContext);
            var has = type.GetMethod("HasDependency", [typeof(string)]);
            if (has?.Invoke(context, [HostId]) is not true) return;
            var get = type.GetMethods().SingleOrDefault(m => m.Name == "Dependency" && m.IsGenericMethodDefinition &&
                m.GetParameters() is [{ ParameterType: var id }] && id == typeof(string));
            if (get?.MakeGenericMethod(typeof(object)).Invoke(context, [HostId]) is { } host &&
                TryRegister(host, context.Id))
                context.Log("Moonscript: registered the twr.read capability");
        }
        catch (Exception error)
        {
            context.Log($"Moonscript: {error.GetBaseException().Message}");
        }
    }

    // Host functions execute on the Lua thread. Each reader gets an immutable published string.
    internal static bool TryRegister(object host, string owner)
    {
        var hook = host.GetType().GetInterface("PZA.Moonscript.IPackExtensionHost")?.GetMethod("RegisterPackCapability");
        if (hook?.ReturnType != typeof(bool) || hook.GetParameters() is not
            [{ ParameterType: var ownerType }, { ParameterType: var permission }, { ParameterType: var list }] ||
            ownerType != typeof(string) || permission != typeof(string) || !list.IsGenericType ||
            list.GetGenericTypeDefinition() != typeof(IReadOnlyList<>)) return false;
        var fn = list.GetGenericArguments()[0];
        var constructor = fn.GetConstructors().SingleOrDefault(c => c.GetParameters().Length == 3);
        if (constructor?.GetParameters() is not
            [{ ParameterType: var table }, { ParameterType: var name }, { ParameterType: var call }] ||
            table != typeof(string) || name != typeof(string) ||
            call.GetMethod("Invoke") is not { } invoke || invoke.GetParameters() is not [{ ParameterType: var args }])
            return false;
        var makeValue = invoke.ReturnType.GetMethod("Of", [typeof(string)]);
        if (makeValue is not { IsStatic: true } || makeValue.ReturnType != invoke.ReturnType) return false;

        (string Name, Func<string> Read)[] readers =
        [
            ("heat", Heat),
            ("hunters", () => HuntersSystem.Latest?.Shown.Text ?? Empty),
            ("news", () => NewsSystem.Latest?.Shown.Text ?? Empty),
            ("rivals", () => RivalsSystem.Latest?.Shown.Text ?? Empty),
            ("titles", () => RecordHost.CatalogText),
        ];
        var functions = Array.CreateInstance(fn, readers.Length);
        for (int i = 0; i < readers.Length; i++)
        {
            var (label, read) = readers[i];
            var value = Expression.Call(makeValue, Expression.Invoke(Expression.Constant(read)));
            var callback = Expression.Lambda(call, value, Expression.Parameter(args)).Compile();
            functions.SetValue(constructor.Invoke(["twr", label, callback]), i);
        }
        return hook.Invoke(host, [owner, "twr.read", functions]) is true;
    }

    static string Heat()
    {
        // Heat is the final chapter of the squad panel and already limited to six towns.
        string panel = RecordHost.Panel;
        int start = panel.LastIndexOf("\n-- Heat", StringComparison.Ordinal);
        return start < 0 ? Empty : panel[(start + 1)..];
    }
}
