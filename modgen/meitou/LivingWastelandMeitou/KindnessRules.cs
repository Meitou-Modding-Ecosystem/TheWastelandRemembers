namespace LivingWasteland.MeitouMod;

internal enum KindnessKind : byte { Freed, Saved, EscapedTogether }

internal readonly record struct KindnessSkills(float Strength, float Toughness, float Attack);

internal sealed record KindnessDeed(ulong Actor, KindnessKind Kind, string Name, string Record, string Faction,
    string Place, string Helper, double Hours, KindnessSkills Skills, bool Critical, bool Named,
    double HeldHours = 0, bool Alone = false, string Captor = "");

internal sealed record KindnessHome(string Place, float X, float Z, float Radius, double SettledAt,
    bool WasAway = false, bool Approached = false)
{
    internal bool Contains(float x, float z)
    {
        float dx = x - X, dz = z - Z;
        return dx * dx + dz * dz <= Radius * Radius;
    }
}

internal static class KindnessRules
{
    internal const int Remembered = 6;
    internal const double ReceiptHours = 24 * 7;

    internal static bool InDanger(float blood, float bleeding, bool unconscious, bool vitalInjury) =>
        vitalInjury || blood <= 20 || (unconscious && bleeding > 0 && blood < 40);

    internal static int Weight(KindnessDeed deed) =>
        (deed.Critical ? 8 : 0) + (deed.HeldHours >= 24 ? 4 : 0) + (deed.Named ? 2 : 0) + (deed.Alone ? 1 : 0);

    internal static string HowLongAgo(double hours)
    {
        if (!double.IsFinite(hours) || hours < 24) return "earlier today";
        int days = (int)Math.Min(int.MaxValue, hours / 24);
        if (days == 1) return "yesterday";
        if (days is >= 28 and <= 34) return "a month or so ago";
        return FormattableString.Invariant($"{days} days ago");
    }
}

internal sealed class KindnessBook
{
    const int Tag = 0x4C574B44, HomesTag = 0x4C574B48, ReunionTag = 0x4C574B52;
    const int ReceiptLimit = 512;
    readonly List<KindnessDeed> people = [];
    readonly SortedDictionary<ulong, KindnessHome> homes = [];
    readonly SortedSet<ulong> reunited = [];
    readonly SortedDictionary<(ulong Actor, KindnessKind Kind), double> receipts = [];
    readonly SortedDictionary<ulong, double> squadDowned = [];

    internal IReadOnlyList<KindnessDeed> People => people;
    internal int Freed { get; private set; }
    internal int Saved { get; private set; }
    internal int EscapedTogether { get; private set; }
    internal double NextReunion { get; private set; }
    internal int PresentationRevision { get; private set; }

    internal KindnessHome? Home(ulong actor) => homes.GetValueOrDefault(actor);

    internal void SetHome(ulong actor, KindnessHome home)
    {
        homes[actor] = home;
        PresentationRevision++;
    }

    internal KindnessDeed? Close(ulong actor)
    {
        for (int i = 0; i < people.Count; i++)
        {
            if (people[i].Actor != actor) continue;
            var deed = people[i];
            people.RemoveAt(i);
            PresentationRevision++;
            homes.Remove(actor);
            reunited.Remove(actor);
            return deed;
        }
        return null;
    }

    internal bool HasReunited(ulong actor) => reunited.Contains(actor);

    internal void MarkReunited(ulong actor, double next)
    {
        reunited.Add(actor);
        NextReunion = next;
        PresentationRevision++;
    }

    static bool Recent(double then, double now) => now >= then && now - then < KindnessRules.ReceiptHours;

    internal bool WasRecorded(ulong actor, KindnessKind kind, double now) =>
        receipts.TryGetValue((actor, kind), out double then) && Recent(then, now);

    internal bool DownedBySquad(ulong actor, double now) =>
        squadDowned.TryGetValue(actor, out double then) && Recent(then, now);

    internal void NoteSquadDown(ulong actor, double now)
    {
        if (actor == 0 || !double.IsFinite(now)) return;
        Put(squadDowned, actor, now);
    }

    static void Put<TKey>(SortedDictionary<TKey, double> entries, TKey key, double now) where TKey : notnull
    {
        if (entries.Count >= ReceiptLimit && !entries.ContainsKey(key))
        {
            var oldest = entries.MinBy(item => item.Value);
            entries.Remove(oldest.Key);
        }
        entries[key] = now;
    }

    internal bool Add(KindnessDeed deed)
    {
        if (deed.Actor == 0 || !double.IsFinite(deed.Hours) || WasRecorded(deed.Actor, deed.Kind, deed.Hours)) return false;
        Put(receipts, (deed.Actor, deed.Kind), deed.Hours);
        if (deed.Kind == KindnessKind.Freed) Freed++;
        else if (deed.Kind == KindnessKind.Saved) Saved++;
        else EscapedTogether++;

        int index = people.FindIndex(item => item.Actor == deed.Actor);
        if (index < 0) people.Add(deed);
        else if (Compare(deed, people[index]) < 0) people[index] = deed;
        people.Sort(Compare);
        PresentationRevision++;
        if (people.Count > KindnessRules.Remembered)
        {
            homes.Remove(people[^1].Actor);
            reunited.Remove(people[^1].Actor);
            people.RemoveAt(KindnessRules.Remembered);
        }
        return true;
    }

    // Equally telling stories keep the earlier deed. Lasting identity breaks ties for releases on the same tick.
    static int Compare(KindnessDeed first, KindnessDeed second)
    {
        int weight = KindnessRules.Weight(second).CompareTo(KindnessRules.Weight(first));
        if (weight != 0) return weight;
        int when = first.Hours.CompareTo(second.Hours);
        return when == 0 ? first.Actor.CompareTo(second.Actor) : when;
    }

    internal void Write(BinaryWriter writer)
    {
        if (receipts.Count == 0 && squadDowned.Count == 0) return;
        writer.Write(Tag);
        writer.Write(1);
        writer.Write(Freed); writer.Write(Saved); writer.Write(EscapedTogether);
        writer.Write(people.Count);
        foreach (var deed in people)
        {
            writer.Write(deed.Actor); writer.Write((byte)deed.Kind);
            writer.Write(deed.Name); writer.Write(deed.Record); writer.Write(deed.Faction);
            writer.Write(deed.Place); writer.Write(deed.Helper); writer.Write(deed.Hours);
            writer.Write(deed.Skills.Strength); writer.Write(deed.Skills.Toughness); writer.Write(deed.Skills.Attack);
            writer.Write(deed.Critical); writer.Write(deed.Named); writer.Write(deed.HeldHours);
            writer.Write(deed.Alone); writer.Write(deed.Captor);
        }
        writer.Write(receipts.Count);
        foreach (var (key, hours) in receipts) { writer.Write(key.Actor); writer.Write((byte)key.Kind); writer.Write(hours); }
        writer.Write(squadDowned.Count);
        foreach (var (actor, hours) in squadDowned) { writer.Write(actor); writer.Write(hours); }
        if (homes.Count == 0 && NextReunion == 0) return;
        writer.Write(HomesTag);
        writer.Write(homes.Count);
        foreach (var (actor, home) in homes)
        {
            writer.Write(actor);
            writer.Write(home.Place); writer.Write(home.X); writer.Write(home.Z); writer.Write(home.Radius); writer.Write(home.SettledAt);
            writer.Write(home.WasAway); writer.Write(home.Approached);
        }
        if (NextReunion != 0)
        {
            writer.Write(ReunionTag);
            writer.Write(NextReunion);
            writer.Write(reunited.Count);
            foreach (ulong actor in reunited) writer.Write(actor);
        }
    }

    internal void Clear()
    {
        people.Clear(); receipts.Clear(); squadDowned.Clear(); homes.Clear(); reunited.Clear();
        NextReunion = 0;
        PresentationRevision++;
        Freed = Saved = EscapedTogether = 0;
    }

    internal void Read(BinaryReader reader)
    {
        Clear();
        if (reader.BaseStream.Length - reader.BaseStream.Position < 8 || reader.ReadInt32() != Tag || reader.ReadInt32() != 1) return;
        Freed = reader.ReadInt32(); Saved = reader.ReadInt32(); EscapedTogether = reader.ReadInt32();
        for (int i = Count(reader, KindnessRules.Remembered); i > 0; i--)
        {
            people.Add(new KindnessDeed(reader.ReadUInt64(), (KindnessKind)reader.ReadByte(), reader.ReadString(), reader.ReadString(),
                reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadDouble(),
                new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), reader.ReadBoolean(), reader.ReadBoolean(),
                reader.ReadDouble(), reader.ReadBoolean(), reader.ReadString()));
        }
        for (int i = Count(reader, ReceiptLimit); i > 0; i--)
            receipts[(reader.ReadUInt64(), (KindnessKind)reader.ReadByte())] = reader.ReadDouble();
        for (int i = Count(reader, ReceiptLimit); i > 0; i--)
            squadDowned[reader.ReadUInt64()] = reader.ReadDouble();
        if (reader.BaseStream.Length - reader.BaseStream.Position < 8 || reader.ReadInt32() != HomesTag) return;
        for (int i = Count(reader, KindnessRules.Remembered); i > 0; i--)
        {
            ulong actor = reader.ReadUInt64();
            var home = new KindnessHome(reader.ReadString(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadDouble(), reader.ReadBoolean(), reader.ReadBoolean());
            if (people.Any(deed => deed.Actor == actor)) homes[actor] = home;
        }
        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(int) * 2 + sizeof(double) ||
            reader.ReadInt32() != ReunionTag) return;
        NextReunion = reader.ReadDouble();
        for (int i = Count(reader, KindnessRules.Remembered); i > 0; i--)
        {
            ulong actor = reader.ReadUInt64();
            if (homes.ContainsKey(actor)) reunited.Add(actor);
        }
    }

    static int Count(BinaryReader reader, int limit)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > limit) throw new InvalidDataException("Kindness record exceeds its saved limit.");
        return count;
    }

    internal IEnumerable<long> HashParts()
    {
        if (receipts.Count == 0 && squadDowned.Count == 0) yield break;
        yield return Tag;
        yield return Freed; yield return Saved; yield return EscapedTogether;
        foreach (var deed in people)
        {
            yield return unchecked((long)deed.Actor);
            yield return (int)deed.Kind;
            yield return SquadRecord.StableHash(deed.Name);
            yield return SquadRecord.StableHash(deed.Record);
            yield return SquadRecord.StableHash(deed.Faction);
            yield return SquadRecord.StableHash(deed.Place);
            yield return SquadRecord.StableHash(deed.Helper);
            yield return BitConverter.DoubleToInt64Bits(deed.Hours);
            yield return BitConverter.SingleToInt32Bits(deed.Skills.Strength);
            yield return BitConverter.SingleToInt32Bits(deed.Skills.Toughness);
            yield return BitConverter.SingleToInt32Bits(deed.Skills.Attack);
            yield return KindnessRules.Weight(deed);
            yield return BitConverter.DoubleToInt64Bits(deed.HeldHours);
            yield return SquadRecord.StableHash(deed.Captor);
        }
        foreach (var (key, hours) in receipts) { yield return unchecked((long)key.Actor); yield return (int)key.Kind; yield return BitConverter.DoubleToInt64Bits(hours); }
        foreach (var (actor, hours) in squadDowned) { yield return unchecked((long)actor); yield return BitConverter.DoubleToInt64Bits(hours); }
        if (homes.Count == 0 && NextReunion == 0) yield break;
        yield return HomesTag;
        foreach (var (actor, home) in homes)
        {
            yield return unchecked((long)actor);
            yield return SquadRecord.StableHash(home.Place);
            yield return BitConverter.SingleToInt32Bits(home.X);
            yield return BitConverter.SingleToInt32Bits(home.Z);
            yield return BitConverter.SingleToInt32Bits(home.Radius);
            yield return BitConverter.DoubleToInt64Bits(home.SettledAt);
            yield return home.WasAway ? 1 : 0;
            yield return home.Approached ? 1 : 0;
        }
        if (NextReunion == 0) yield break;
        yield return ReunionTag;
        yield return BitConverter.DoubleToInt64Bits(NextReunion);
        foreach (ulong actor in reunited) yield return unchecked((long)actor);
    }
}
