using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal sealed class TrophyLooks(IReadOnlyDictionary<string, object> looks) : ITickSystem
{
    const string Key = "livingwasteland.neck";
    static readonly Type? meshType = typeof(CharacterCold).Assembly.GetType("Meitou.Simulation.BoneMesh");
    static readonly ConstructorInfo? make = meshType?.GetConstructor([typeof(string), typeof(string), typeof(string), typeof(Vector3), typeof(Quaternion)]);
    static readonly MethodInfo? set = meshType is null ? null : typeof(CharacterCold).GetMethod("SetBoneMesh", [meshType]);
    static readonly MethodInfo? remove = typeof(CharacterCold).GetMethod("RemoveBoneMesh", [typeof(string)]);

    sealed record Fit(string Item, string Mesh, string Bone, float X, float Y, float Z, float Yaw, float Pitch, float Roll);
    sealed class Applied(string record) { internal string Record = record; }
    readonly ConditionalWeakTable<CharacterCold, Applied> applied = new();
    internal static bool Available => make is not null && set is not null && remove is not null;

    internal static void Register(ModContext context)
    {
        if (!Available) return;
        string path = Path.Combine(context.Folder, "lw_neck_meshes.json");
        if (!File.Exists(path)) return;
        try
        {
            var fits = JsonSerializer.Deserialize<Fit?[]>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            context.AddSystem("lw-neck-meshes", ctx =>
                new TrophyLooks(Resolve(ctx.Data.Db, context.Folder, fits)), SystemAnchor.End);
            context.Log("trophies: neck meshes available in Squad record settings");
        }
        catch (Exception e) { context.Log($"trophies: neck meshes unavailable ({e.GetBaseException().Message})"); }
    }

    static Dictionary<string, object> Resolve(GameDatabase db, string folder, IEnumerable<Fit?> fits)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var fit in fits)
        {
            if (fit is null || string.IsNullOrWhiteSpace(fit.Item) || string.IsNullOrWhiteSpace(fit.Bone) ||
                string.IsNullOrWhiteSpace(fit.Mesh) || Path.GetFileName(fit.Mesh) != fit.Mesh ||
                !fit.Mesh.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(folder, fit.Mesh)) ||
                !float.IsFinite(fit.X) || !float.IsFinite(fit.Y) || !float.IsFinite(fit.Z) ||
                !float.IsFinite(fit.Yaw) || !float.IsFinite(fit.Pitch) || !float.IsFinite(fit.Roll)) continue;
            var items = db.OfType(FcsRecordType.Item).Where(r =>
                r.StringId.EndsWith("-LivingWasteland.mod", StringComparison.OrdinalIgnoreCase) &&
                r.BaseName == fit.Item && r.GetInt("slot") == Trophies.NeckSlot).Take(2).ToArray();
            if (items.Length != 1) continue;
            const float radians = MathF.PI / 180f;
            result[items[0].StringId] = make!.Invoke([Key, fit.Mesh, fit.Bone, new Vector3(fit.X, fit.Y, fit.Z),
                Quaternion.CreateFromYawPitchRoll(fit.Yaw * radians, fit.Pitch * radians, fit.Roll * radians)]);
        }
        return result;
    }

    public void SlowWorld(World world)
    {
        if (!Available || looks.Count == 0 || world.Tick % 30 != 9) return;
        bool enabled = LwMod.Settings.VisibleNeckCharms && LwMod.Settings.Trophies;
        var table = world.Characters;
        for (int i = 0; i < table.HighWater; i++)
        {
            if (table.Cold(i) is not { } character) continue;
            applied.TryGetValue(character, out var previous);
            string? current = enabled && character is { IsPlayer: true, Animal: null, Inventory: { } inventory } &&
                table.Next[i].Alive && !table.Next[i].Dormant
                ? inventory.Items.Find(item => item.Section == Trophies.Section)?.Record : null;
            if (current is not null && looks.TryGetValue(current, out var mesh))
            {
                if (previous?.Record == current) continue;
                set!.Invoke(character, [mesh]);
                if (previous is null) applied.Add(character, new Applied(current));
                else previous.Record = current;
            }
            else if (previous is not null)
            {
                remove!.Invoke(character, [Key]);
                applied.Remove(character);
            }
        }
    }
}
