// Town heat for the Living Wasteland helper (not part of the stand-alone Squad Record mod).
namespace LivingWasteland.MeitouMod;

/// <summary>
/// Heat with a place: each town (by name) remembers what the player's people did there. Two parts: crime (assault, theft, the "I saw that"
/// lines) and blood (killing the town's people), each fading by half every <see cref="CrimeHalfLife"/> / <see cref="BloodHalfLife"/> game hours,
/// but only while none of the player's people is in that town. The scale matches the Kenshi build's reputation drop (heat 6 noticed, 14
/// wanted, 24 hunted, 40 the most). A town of the same faction hears a dimmer echo of the hottest of its sister towns.
/// </summary>
public sealed class HeatMap
{
    public const double Max = 40, Echo = 0.5;
    public double CrimeHalfLife = 72, BloodHalfLife = 288;

    sealed class Town
    {
        public string Faction = "";
        public double Crime, Blood;
    }

    readonly SortedDictionary<string, Town> towns = new(StringComparer.Ordinal);
    // Trade uses the current owner's share. The original town total remains the source for heat, hunts and API v4.
    readonly SortedDictionary<string, SortedDictionary<string, Town>> owners = new(StringComparer.Ordinal);

    internal bool HasOwnedHeat => owners.Count > 0;

    internal double HereOwned(string node, string faction) =>
        owners.TryGetValue(faction, out var places) && places.TryGetValue(node, out var t) ? Math.Min(Max, t.Crime + t.Blood) : 0;

    public double Here(string town) => towns.TryGetValue(town, out var t) ? Math.Min(Max, t.Crime + t.Blood) : 0;

    /// <summary>What a speaker in <paramref name="town"/> (or outside every town: null) of <paramref name="faction"/> feels: its own town's heat,
    /// or the echo of its faction's hottest town, whichever is more.</summary>
    public double For(string? town, string faction)
    {
        double own = town is null ? 0 : Here(town);
        double sister = towns.Where(p => p.Value.Faction == faction).Select(p => Here(p.Key)).DefaultIfEmpty(0).Max();
        return Math.Max(own, sister * Echo);
    }

    public void AddCrime(string town, string faction, double amount) => AddCrime(town, faction, amount, "");

    public void AddBlood(string town, string faction, double amount) => AddBlood(town, faction, amount, "");

    internal void AddCrime(string town, string faction, double amount, string node) => Add(town, faction, amount, node, blood: false);

    internal void AddBlood(string town, string faction, double amount, string node) => Add(town, faction, amount, node, blood: true);

    void Add(string town, string faction, double amount, string node, bool blood)
    {
        if (!towns.TryGetValue(town, out var t)) towns[town] = t = new Town();
        if (faction.Length > 0) t.Faction = faction;
        AddTo(t, amount, blood);
        if (faction.Length == 0 || node.Length == 0) return;
        if (!owners.TryGetValue(faction, out var places)) owners[faction] = places = new(StringComparer.Ordinal);
        if (!places.TryGetValue(node, out var owned)) places[node] = owned = new Town();
        AddTo(owned, amount, blood);
    }

    static void AddTo(Town t, double amount, bool blood)
    {
        if (blood) t.Blood = Math.Min(Max, t.Blood + amount);
        else t.Crime = Math.Min(Max, t.Crime + amount);
    }

    /// <summary>A pardon or a bribe: takes <paramref name="amount"/> off every town of the faction (crime first, then blood).</summary>
    public void Forgive(string faction, double amount)
    {
        Forgive(towns.Values.Where(t => t.Faction == faction), amount);
        if (owners.TryGetValue(faction, out var places)) Forgive(places.Values, amount);
    }

    static void Forgive(IEnumerable<Town> places, double amount)
    {
        foreach (var t in places)
        {
            double fromCrime = Math.Min(t.Crime, amount);
            t.Crime -= fromCrime;
            t.Blood = Math.Max(0, t.Blood - (amount - fromCrime));
        }
    }

    /// <summary>Fades every town nobody of the player's is in by <paramref name="hours"/> game hours.</summary>
    public void Fade(double hours, ISet<string> occupied) => Fade(hours, occupied, null);

    internal void Fade(double hours, ISet<string> occupied, ISet<string>? occupiedNodes)
    {
        Fade(towns, hours, occupied);
        foreach (var places in owners.Values) Fade(places, hours, occupiedNodes);
        foreach (var empty in owners.Where(group => group.Value.Count == 0).Select(group => group.Key).ToArray()) owners.Remove(empty);
    }

    void Fade(SortedDictionary<string, Town> places, double hours, ISet<string>? occupied)
    {
        foreach (var (name, t) in places)
        {
            if (occupied?.Contains(name) == true) continue;
            t.Crime = Decay(t.Crime, hours, CrimeHalfLife);
            t.Blood = Decay(t.Blood, hours, BloodHalfLife);
        }
        foreach (var k in places.Where(p => p.Value.Crime + p.Value.Blood < 0.05).Select(p => p.Key).ToList()) places.Remove(k);
    }

    static double Decay(double v, double hours, double halfLife) => v < 0.05 ? 0 : v * Math.Pow(0.5, hours / halfLife);

    /// <summary>The towns with heat, hottest first: name, faction, crime, blood.</summary>
    public IEnumerable<(string Town, string Faction, double Crime, double Blood)> All() =>
        towns.OrderByDescending(p => p.Value.Crime + p.Value.Blood).Select(p => (p.Key, p.Value.Faction, p.Value.Crime, p.Value.Blood));

    public static string Tier(double heat) => heat >= 24 ? "hunted" : heat >= 14 ? "wanted" : heat >= 6 ? "noticed" : "quiet";

    public void Write(BinaryWriter w)
    {
        w.Write(1);
        w.Write(towns.Count);
        foreach (var (name, t) in towns) { w.Write(name); w.Write(t.Faction); w.Write(t.Crime); w.Write(t.Blood); }
    }

    public void Read(BinaryReader r)
    {
        owners.Clear();
        if (r.ReadInt32() != 1) return;
        towns.Clear();
        for (int n = r.ReadInt32(); n > 0; n--)
            towns[r.ReadString()] = new Town { Faction = r.ReadString(), Crime = r.ReadDouble(), Blood = r.ReadDouble() };
    }

    const int OwnersTag = 0x4C57484F;

    internal void WriteOwners(BinaryWriter writer)
    {
        if (owners.Count == 0) return;
        writer.Write(OwnersTag);
        writer.Write(owners.Count);
        foreach (var (faction, places) in owners)
        {
            writer.Write(faction);
            writer.Write(places.Count);
            foreach (var (name, heat) in places) { writer.Write(name); writer.Write(heat.Crime); writer.Write(heat.Blood); }
        }
    }

    internal void ReadOwners(BinaryReader reader)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(int) * 2 || reader.ReadInt32() != OwnersTag) return;
        for (int n = reader.ReadInt32(); n > 0; n--)
        {
            string faction = reader.ReadString();
            var places = owners[faction] = new(StringComparer.Ordinal);
            for (int count = reader.ReadInt32(); count > 0; count--)
                places[reader.ReadString()] = new Town { Crime = reader.ReadDouble(), Blood = reader.ReadDouble() };
        }
    }

    public IEnumerable<long> HashParts()
    {
        yield return towns.Count;
        foreach (var (name, t) in towns)
        {
            yield return SquadRecord.StableHash(name);
            yield return (long)(t.Crime * 1000);
            yield return (long)(t.Blood * 1000);
        }
    }

    internal IEnumerable<long> OwnerHashParts()
    {
        yield return owners.Count;
        foreach (var (faction, places) in owners)
        {
            yield return SquadRecord.StableHash(faction);
            yield return places.Count;
            foreach (var (name, heat) in places)
            {
                yield return SquadRecord.StableHash(name);
                yield return (long)(heat.Crime * 1000);
                yield return (long)(heat.Blood * 1000);
            }
        }
    }
}

