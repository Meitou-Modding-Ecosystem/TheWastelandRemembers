// Keepsakes written for the fallen (Trophies.cs gives them). Item descriptions come from the item's record, shared by every copy, so the
// data ships a dozen keepsake records (content/trophies.json keepsake_slots) and this book gives each fallen squadmate one of them: the
// record's name becomes "Keepsake of <name>" and its description their life with the squad, death and record (KeepsakeText), written from
// the squad record at the moment of death. The texts are saved here and written back into the records after every load. With more fallen
// than records, the oldest story gives way to the newest.
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed class KeepsakeBook(PopulationData? data) : ITickSystem, IModSaveData, IStateHashed
{
    const int Format = 1;
    public static List<string> Slots = [];       // the keepsake records' string ids (lw_codes.json trophies.keepsakes)

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, KeepsakeBook> byWorld = new();
    public static KeepsakeBook? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;

    sealed record Entry(int Slot, string Name, string Text, double Hours);
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
    public string? Take(World world, string fallenKey, string mournerName)
    {
        if (Slots.Count == 0 || LwDeeds.For(world)?.Record is not { } rec) return null;
        var f = rec.FallenMembers.LastOrDefault(x => x.Name == fallenKey);
        if (f is null) return Slots[0];
        rec.Members.TryGetValue(fallenKey, out var m);
        var (name, text) = KeepsakeText.For(f, m, mournerName, rec.Display);
        int slot = Enumerable.Range(0, Slots.Count).FirstOrDefault(k => entries.All(e => e.Slot != k), -1);
        if (slot < 0) { slot = entries.OrderBy(e => e.Hours).First().Slot; entries.RemoveAll(e => e.Slot == slot); }
        var entry = new Entry(slot, name, text, world.GameHours);
        entries.Add(entry);
        Write(entry);
        return Slots[slot];
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
        if (broken || data is null || entries.Count == 0) return;
        var names = entries.Where(e => e.Slot < Slots.Count).ToDictionary(e => Slots[e.Slot], e => e.Name);
        var table = world.Characters;
        try
        {
            for (int s = 0; s < table.HighWater; s++)
            {
                if (table.Cold(s) is not { IsPlayer: true, Inventory: { } bag }) continue;
                foreach (var item in bag.Items)
                    if (names.TryGetValue(item.Record, out var n) && item.Name != n)
                        item.GetType().GetProperty("Name")!.GetSetMethod(true)!.Invoke(item, [n]);
            }
        }
        catch (Exception ex) { LwMod.Log($"trophies: could not rename carried keepsakes ({ex.GetBaseException().Message})"); }
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Format);
        w.Write(entries.Count);
        foreach (var e in entries) { w.Write(e.Slot); w.Write(e.Name); w.Write(e.Text); w.Write(e.Hours); }
    }

    public void Read(Stream stream)
    {
        entries.Clear();
        seen = null;
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position >= stream.Length || r.ReadInt32() != Format) return;
        for (int n = r.ReadInt32(); n > 0; n--) entries.Add(new Entry(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadDouble()));
        foreach (var e in entries) Write(e);
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(entries.Count);
        foreach (var e in entries) { h.Add(e.Slot); h.Add(SquadRecord.StableHash(e.Name)); }
    }
}
