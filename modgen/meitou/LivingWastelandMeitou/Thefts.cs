// Towns miss stolen goods (Meitou 0.21+, setting "Towns miss stolen goods"): the engine reports every take of goods that are not the
// player's characters' own (IGoodsTakenObserver, Meitou.Simulation.Crime). This mod can't name that interface at build time (it builds
// against Meitou 0.17.3), so ObserverEmit makes a small system implementing it at load, forwarding each take to Thefts.Taken. Seen takes add
// heat now; unseen ones wait in TheftsSystem (saved) until the town misses them. The pure part: TheftRules.cs.
using System.Runtime.CompilerServices;
using Meitou.Data.World;
using Meitou.Simulation.Population;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class Thefts
{
    /// <summary>Registers the goods-taken observer where the engine has it. Never throws; logs what happened.</summary>
    public static void Register(ModContext c)
    {
        try
        {
            var observer = typeof(World).Assembly.GetType("Meitou.Simulation.Crime.IGoodsTakenObserver");
            if (observer is null) { c.Log("thefts: this Meitou does not report goods taken (0.21 and later do)"); return; }
            if (ObserverEmit.Build(observer, "GoodsTaken", typeof(Thefts).GetMethod(nameof(Taken))!, "TheftWatchSystem") is not { } type)
            {
                c.Log("thefts: the engine's goods-taken observer changed; not added");
                return;
            }
            c.AddSystem("lw-theft-watch", _ => (ITickSystem)Activator.CreateInstance(type)!, SystemAnchor.End);
            c.Log("thefts: towns miss goods taken from them (setting \"Towns miss stolen goods\")");
        }
        catch (Exception e) { c.Log($"thefts: not added ({e.GetBaseException().Message})"); }
    }

    /// <summary>One take (the engine's GoodsTaken, boxed, or anything with the same property names). Serial, once the goods have moved.</summary>
    public static void Taken(World world, object take)
    {
        if (!LwMod.Settings.TheftHeat) return;
        try
        {
            if (TheftsSystem.For(world) is not { } system || LwDeeds.For(world) is not { Data: { } data } deeds) return;
            var t = take.GetType();
            int owner = (int)(t.GetProperty("OwnerFaction")?.GetValue(take) ?? -1);
            if (!TheftRules.Counts(owner, world.Player.Faction)) return;
            var taker = (CharacterId)(t.GetProperty("Taker")?.GetValue(take) ?? CharacterId.None);
            string record = t.GetProperty("Record")?.GetValue(take) as string ?? "";
            int units = Math.Max(1, (int)(t.GetProperty("Units")?.GetValue(take) ?? 1));
            bool caught = t.GetProperty("Caught")?.GetValue(take) is true;
            string site = t.GetProperty("Site")?.GetValue(take) as string ?? "";
            var rec = data.Db.Find(record);
            double value = (rec is not null && rec.Ints.TryGetValue("value", out int v) ? v : 0) * (double)units;
            if (value < TheftRules.MinValue) return;

            string? town = world.Characters.TryResolveNext(taker, out int s) ? deeds.TownAt(world.Characters.Next[s].Position).Town : null;
            town ??= SiteTown(data, site);
            if (town is null) return;                                     // out in the wilds: no town to miss it
            string faction = deeds.FactionName(owner);
            string item = rec?.Name ?? record;
            if (caught)
            {
                double h = TheftRules.Heat(value, seen: true);
                deeds.Heat.AddCrime(town, faction, h);
                LwMod.Log($"thefts: seen taking {item} x{units} ({value:0} cats) in {town}: heat +{h:0.0}");
            }
            else
            {
                system.Pending(town, faction, item, units, value, world.GameHours);
                LwMod.Log($"thefts: took {item} x{units} ({value:0} cats) in {town} unseen; missed in {TheftRules.MissedAfter:0} game hours");
            }
            LogTally.Add(caught ? "thefts seen" : "thefts unseen");
        }
        catch (Exception e) { LwMod.Log($"thefts: skipped a take ({e.GetBaseException().Message})"); }
    }

    static string? SiteTown(PopulationData data, string site)
    {
        if (site.Length == 0) return null;
        foreach (var s in data.Sites)
            if (string.Equals(s.Town.Name, site, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Record.Name, site, StringComparison.OrdinalIgnoreCase)) return s.Town.Name;
        return null;
    }
}

/// <summary>Unseen thefts waiting to be missed, per town; saved with the game.</summary>
public sealed class TheftsSystem : ITickSystem, IModSaveData, IStateHashed
{
    const int PassEvery = 300, Format = 1;
    static readonly ConditionalWeakTable<World, TheftsSystem> byWorld = new();
    readonly SortedDictionary<string, PendingTheft> pending = new(StringComparer.Ordinal);
    World? seen;

    public static TheftsSystem? For(World world) => byWorld.TryGetValue(world, out var s) ? s : null;

    public IReadOnlyCollection<PendingTheft> Waiting => pending.Values;

    public void Pending(string town, string faction, string item, int units, double value, double hours) =>
        pending[town] = TheftRules.Add(pending.GetValueOrDefault(town), town, faction, item, units, value, hours);

    public void SlowWorld(World world)
    {
        if (!ReferenceEquals(seen, world)) { seen = world; byWorld.AddOrUpdate(world, this); }
        if (world.Tick % PassEvery != 170 || pending.Count == 0) return;
        Settle(world, world.GameHours);
    }

    /// <summary>Thefts missed by <paramref name="now"/> heat their town; History says so when the goods were worth a lot.</summary>
    public void Settle(World world, double now)
    {
        if (LwDeeds.For(world) is not { } deeds) return;
        foreach (var p in pending.Values.ToList())
        {
            if (!TheftRules.Missed(p, now, out bool drop)) { if (drop) pending.Remove(p.Town); continue; }
            pending.Remove(p.Town);
            double h = TheftRules.Heat(p.Value, seen: false);
            string tierBefore = HeatMap.Tier(deeds.Heat.Here(p.Town));
            deeds.Heat.AddCrime(p.Town, p.Faction, h);
            if (TheftRules.Line(p) is { } line) { deeds.Record.Note(line); RecapLink.Moment(line, p.Town, 55); }
            LwMod.Log($"thefts: {p.Town} missed {p.Items} ({p.Value:0} cats): heat +{h:0.0} ({tierBefore} to {HeatMap.Tier(deeds.Heat.Here(p.Town))})");
        }
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Format);
        w.Write(pending.Count);
        foreach (var p in pending.Values) { w.Write(p.Town); w.Write(p.Faction); w.Write(p.Items); w.Write(p.Value); w.Write(p.TakenHours); }
    }

    public void Read(Stream stream)
    {
        pending.Clear();
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position >= stream.Length || r.ReadInt32() != Format) return;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var p = new PendingTheft(r.ReadString(), r.ReadString(), r.ReadString(), r.ReadDouble(), r.ReadDouble());
            pending[p.Town] = p;
        }
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(pending.Count);
        foreach (var p in pending.Values) { h.Add(SquadRecord.StableHash(p.Town)); h.Add(p.TakenHours); }
    }
}
