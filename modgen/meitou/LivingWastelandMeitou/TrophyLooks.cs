using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal sealed class TrophyLooks(IReadOnlyDictionary<string, object> resolved) : ITickSystem
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
                Latest = new TrophyLooks(Resolve(ctx.Data.Db, context.Folder, fits)), SystemAnchor.End);
            // the fit is a first guess until seen in game: this row moves every charm at once, live, so the right numbers can be found
            context.AddDebugCommand("Wasteland Remembers: neck charm fit (x y z yaw pitch roll)", d => Latest?.Retune(d.Field) ?? "no world yet",
                hasField: true);
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

    internal static TrophyLooks? Latest;
    IReadOnlyDictionary<string, object> looks = resolved;
    int generation;
    readonly ConditionalWeakTable<CharacterCold, StrongBox<int>> appliedAt = new();

    /// <summary>The F10 row: new offsets (and rotations, in degrees) for every charm; worn charms are hung again with them.</summary>
    internal string Retune(string field)
    {
        var parts = field.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var n = new float[6];
        if (parts.Length is not (3 or 6) || parts.Select((t, i) => float.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out n[i])).Contains(false) || n.Any(v => !float.IsFinite(v)))
            return "type x y z (and optionally yaw pitch roll in degrees), e.g. 0 -0.35 1.1";
        if (meshType?.GetProperty("Mesh") is not { } meshProp || meshType.GetProperty("Bone") is not { } boneProp) return "no bone meshes here";
        const float radians = MathF.PI / 180f;
        var turned = Quaternion.CreateFromYawPitchRoll(n[3] * radians, n[4] * radians, n[5] * radians);
        var next = looks.ToDictionary(p => p.Key, p => make!.Invoke([Key, meshProp.GetValue(p.Value), boneProp.GetValue(p.Value),
            new Vector3(n[0], n[1], n[2]), turned]));
        Volatile.Write(ref looks, next);
        Interlocked.Increment(ref generation);
        string text = FormattableString.Invariant($"neck charms now at {n[0]} {n[1]} {n[2]}, turned {n[3]} {n[4]} {n[5]}");
        LwMod.Log($"trophies: {text}");
        return text;
    }

    public void SlowWorld(World world)
    {
        var looks = Volatile.Read(ref this.looks);
        if (!Available || looks.Count == 0 || world.Tick % 30 != 9) return;
        int gen = Volatile.Read(ref generation);
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
                var at = appliedAt.GetValue(character, _ => new StrongBox<int>(-1));
                if (previous?.Record == current && at.Value == gen) continue;
                set!.Invoke(character, [mesh]);
                at.Value = gen;
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
