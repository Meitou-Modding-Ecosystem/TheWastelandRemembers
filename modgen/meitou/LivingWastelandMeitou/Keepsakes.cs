// Keepsakes written for the fallen (Trophies.cs gives them). Item descriptions come from the item's record, shared by every copy, so the
// data ships a dozen keepsake records (content/trophies.json keepsake_slots) and this book gives each fallen squadmate one of them: the
// record's name becomes "Keepsake of <name>" and its description their life with the squad, death and record (KeepsakeText), written from
// the squad record at the moment of death. The texts are saved here and written back into the records after every load. With more fallen
// than records, the oldest story gives way to the newest. On supported clients, an opt-in item tag lets a right click read an
// individual story from the saved Fallen record even after the slot is reused.
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed class KeepsakeBook(PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    const string Provenance = "lw:keepsake:v1:";
    static readonly MethodInfo? setItemName = typeof(ItemInstance).GetProperty("Name")?.GetSetMethod(true);
    const int Format = 2;                        // 2: entries keep the fallen's key
    public static List<string> Slots = [];       // the keepsake records' string ids (lw_codes.json trophies.keepsakes)

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, KeepsakeBook> byWorld = new();
    public static KeepsakeBook? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;

    sealed record Entry(int Slot, string Name, string Text, double Hours, string Key);
    readonly List<Entry> entries = [];
    World? seen;
    static bool broken;

    public void SlowWorld(World world)
    {
        if (ReferenceEquals(seen, world)) return;
        seen = world;
        byWorld.AddOrUpdate(world, this);
        foreach (var e in entries) Write(e);
        Rename(world);
    }

    /// <summary>The keepsake record to hand over for <paramref name="fallenKey"/>, written for them; null when there is none.</summary>
    public string? Take(World world, string fallenKey, string mournerName) =>
        Take(world, fallenKey, mournerName, out _);

    internal string? Take(World world, string fallenKey, string mournerName, out string? tag)
    {
        tag = null;
        if (Slots.Count == 0 || LwDeeds.For(world)?.Record is not { } rec) return null;
        var f = rec.FallenMembers.LastOrDefault(x => x.Name == fallenKey);
        if (f is null) return Slots[0];
        rec.Members.TryGetValue(fallenKey, out var m);
        var (name, text) = KeepsakeText.For(f, m, mournerName, rec.Display, LostAtTheEnd(rec, f));
        int slot = Enumerable.Range(0, Slots.Count).FirstOrDefault(k => entries.All(e => e.Slot != k), -1);
        if (slot < 0) { slot = entries.OrderBy(e => e.Hours).First().Slot; entries.RemoveAll(e => e.Slot == slot); }
        var entry = new Entry(slot, name, text, world.GameHours, fallenKey);
        entries.Add(entry);
        Write(entry);
        if (LwMod.Settings.KeepsakeProvenance) tag = Provenance + f.Name;
        return Slots[slot];
    }

    /// <summary>A holder turns the keepsake of <paramref name="record"/> over: its story is written again (who holds it now, avenged since?)
    /// and the line they say and the message come back. Null for a keepsake this book has no story for.</summary>
    public (string Bark, string Message)? Remember(World world, string record, string holder)
    {
        int slot = Slots.IndexOf(record);
        if (slot < 0 || entries.FirstOrDefault(e => e.Slot == slot) is not { } entry || LwDeeds.For(world)?.Record is not { } rec) return null;
        string shown = entry.Name.StartsWith("Keepsake of ", StringComparison.Ordinal) ? entry.Name["Keepsake of ".Length..] : entry.Name;
        var f = rec.FallenMembers.LastOrDefault(x => entry.Key.Length > 0 ? x.Name == entry.Key : rec.Display(x.Name) == shown);
        if (f is null) return null;
        rec.Members.TryGetValue(f.Name, out var m);
        var (name, text) = KeepsakeText.For(f, m, holder, rec.Display, LostAtTheEnd(rec, f));
        var now = entry with { Name = name, Text = text, Key = f.Name };
        entries[entries.IndexOf(entry)] = now;
        Write(now);
        return KeepsakeText.Remember(f, holder, rec.Display, (int)world.Tick);
    }

    internal (string Bark, string Message)? Remember(World world, ItemInstance item, string holder)
    {
        if (!Slots.Contains(item.Record)) return null;
        if (FallenKey(item) is not { } key) return Remember(world, item.Record, holder);
        if (LwDeeds.For(world)?.Record is not { } rec || rec.FallenMembers.LastOrDefault(x => x.Name == key) is not { } fallen)
            return null;

        rec.Members.TryGetValue(key, out var member);
        var (name, story) = KeepsakeText.For(fallen, member, holder, rec.Display, LostAtTheEnd(rec, fallen));
        SetName(item, name);
        return (KeepsakeText.Remember(fallen, holder, rec.Display, (int)world.Tick).Bark, story);
    }

    /// <summary>Limbs the fallen lost within the game hour before they died (History's "X lost an arm" lines; no time is kept per limb).</summary>
    static int LostAtTheEnd(SquadRecord rec, Fallen f)
    {
        string lead = rec.Display(f.Name) + " lost a";
        return rec.History.Count(h => h.Text.StartsWith(lead, StringComparison.Ordinal) && h.Hours <= f.Hours + 0.01 && f.Hours - h.Hours <= 1);
    }

    static string? FallenKey(ItemInstance item) => FallenKeyOf(ItemUses.Tag(item));

    /// <summary>The fallen squadmate a keepsake's tag names, or null (an untagged keepsake, or another mod's tag).</summary>
    internal static string? FallenKeyOf(string? tag) =>
        tag is not null && tag.StartsWith(Provenance, StringComparison.Ordinal) ? tag[Provenance.Length..] : null;

    static void SetName(ItemInstance item, string name)
    {
        if (item.Name != name) setItemName?.Invoke(item, [name]);
    }

    /// <summary>Writes an entry's name and story into its record (by reflection: GameRecord's name and strings are internal to Meitou).</summary>
    void Write(Entry e)
    {
        if (broken || data is null || e.Slot >= Slots.Count || data.Db.Find(Slots[e.Slot]) is not { } rec) return;
        try
        {
            var t = rec.GetType();
            t.GetProperty("Name")!.GetSetMethod(true)!.Invoke(rec, [e.Name]);
            var strings = (Dictionary<string, string>)t.GetProperty("StringTable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rec)!;
            strings["description"] = e.Text;
        }
        catch (Exception ex)
        {
            broken = true;
            LwMod.Log($"trophies: could not write a keepsake's story ({ex.GetBaseException().Message}); keepsakes keep the plain text");
        }
    }

    /// <summary>Keepsakes the squad carries take their record's name again (an item copies its name when it is made, so after a load the
    /// ones made earlier show the plain name until this runs).</summary>
    void Rename(World world)
    {
        if (broken || data is null || Slots.Count == 0) return;
        var names = entries.Where(e => e.Slot < Slots.Count).ToDictionary(e => Slots[e.Slot], e => e.Name);
        var record = LwDeeds.For(world)?.Record;
        var fallen = record?.FallenMembers.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var table = world.Characters;
        try
        {
            for (int s = 0; s < table.HighWater; s++)
            {
                if (table.Cold(s) is not { IsPlayer: true, Inventory: { } bag }) continue;
                foreach (var item in bag.Items)
                {
                    if (!Slots.Contains(item.Record)) continue;
                    if (FallenKey(item) is { } key)
                    {
                        if (fallen?.Contains(key) == true) SetName(item, "Keepsake of " + record!.Display(key));
                    }
                    else if (names.TryGetValue(item.Record, out var name)) SetName(item, name);
                }
            }
        }
        catch (Exception ex) { LwMod.Log($"trophies: could not rename carried keepsakes ({ex.GetBaseException().Message})"); }
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Format);
        w.Write(entries.Count);
        foreach (var e in entries) { w.Write(e.Slot); w.Write(e.Name); w.Write(e.Text); w.Write(e.Hours); w.Write(e.Key); }
    }

    public void Read(Stream stream)
    {
        entries.Clear();
        seen = null;
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position >= stream.Length) return;
        int format = r.ReadInt32();
        if (format is not (1 or 2)) return;
        for (int n = r.ReadInt32(); n > 0; n--)
            entries.Add(new Entry(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadDouble(), format >= 2 ? r.ReadString() : ""));
        foreach (var e in entries) Write(e);
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(entries.Count);
        foreach (var e in entries) { h.Add(e.Slot); h.Add(SquadRecord.StableHash(e.Name)); }
    }
}
