using System.Numerics;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>A witnessed crime can prompt one remark from the same local, after the same squad member leaves and returns.</summary>
internal sealed class WitnessMemories
{
    const int Capacity = 64, PerTown = 4, Tag = 0x31544957; // WIT1
    const double Hours = 72;

    readonly record struct Pending(string Site, CharacterId Culprit, CharacterId Witness, double Until);
    readonly record struct Memory(string Site, ulong Culprit, ulong Witness, double Until, bool Away);
    readonly List<Pending> pending = [];
    readonly List<Memory> memories = [];

    static readonly Type? observer = typeof(World).Assembly.GetType("Meitou.Simulation.Crime.ICrimeObserver");
    static readonly Type? notice = observer?.GetMethod("CrimeNoted")?.GetParameters().ElementAtOrDefault(1)?.ParameterType.GetElementType();
    static readonly PropertyInfo? kind = notice?.GetProperty("Kind"), culprit = notice?.GetProperty("Culprit"),
        witness = notice?.GetProperty("Witness");
    static readonly object? witnessed = kind?.PropertyType is { IsEnum: true } kinds &&
        Enum.TryParse(kinds, "Witnessed", out var value) ? value : null;

    internal static bool Available => LastingActors.Available && observer is not null && witnessed is not null &&
        culprit?.PropertyType == typeof(CharacterId) && witness?.PropertyType == typeof(CharacterId) &&
        LifecycleWatch.BuildWatches().TryGetValue(nameof(LifecycleWatch.Held), out var custody) &&
        observer.IsAssignableFrom(custody);

    internal static void Crime(World world, object notice)
    {
        if (!LwMod.Settings.WitnessesRememberFaces || witnessed?.Equals(kind?.GetValue(notice)) != true ||
            culprit?.GetValue(notice) is not CharacterId actor || witness?.GetValue(notice) is not CharacterId guard ||
            LwState.For(world) is not { Data: { } data } state ||
            SiteOf(world, data, actor, guard) is not { } site) return;

        var book = state.Witnesses ??= new();
        if (book.pending.Count >= Capacity) return;
        foreach (var next in book.pending)
            if (next.Site == site && next.Culprit == actor && next.Witness == guard) return;
        // The observer may run between ticks. Retain generation-checked handles and grant lasting IDs in the simulation pass.
        book.pending.Add(new(site, actor, guard, world.GameHours + Hours));
    }

    static string? SiteOf(World world, PopulationData data, CharacterId culprit, CharacterId witnessId)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(culprit, out int c) || table.Cold(c) is not { IsPlayer: true, Animal: null } member ||
            OtherModsPeople.Runs(world, member) ||
            !table.TryResolveNext(witnessId, out int w) || !table.Next[w].Alive ||
            table.Cold(w) is not { IsPlayer: false, Animal: null } npc) return null;
        int town = data.TownAt(table.Next[c].Position);
        if (!Local(data, town, npc) || data.TownAt(table.Next[w].Position) != town) return null;
        return data.Sites[town].InstanceId;
    }

    static bool Local(PopulationData data, int town, CharacterCold npc) =>
        town >= 0 && town < data.Sites.Count && town < data.SiteFactions.Count && npc.Faction >= 0 &&
        data.SiteFactions[town] == npc.Faction && npc.Faction < data.Factions.Count &&
        !NewsAskRules.NoConversationNews(data.Factions[npc.Faction].Name);

    internal void Look(World world, PopulationData data)
    {
        if (!LwMod.Settings.WitnessesRememberFaces || !LastingActors.Available)
        {
            pending.Clear();
            return;
        }

        foreach (var next in pending)
        {
            if (!world.Characters.TryResolveNext(next.Culprit, out _) || !world.Characters.TryResolveNext(next.Witness, out _)) continue;
            ulong actor = LastingActors.Grant(world, next.Culprit), guard = LastingActors.Grant(world, next.Witness);
            if (actor == 0 || guard == 0) continue;
            memories.RemoveAll(m => m.Until <= world.GameHours || (m.Site == next.Site && m.Culprit == actor && m.Witness == guard));
            int inTown = 0, oldest = -1;
            for (int i = 0; i < memories.Count; i++)
                if (memories[i].Site == next.Site) { inTown++; if (oldest < 0) oldest = i; }
            if (inTown >= PerTown) memories.RemoveAt(oldest);
            if (memories.Count >= Capacity) memories.RemoveAt(0);
            memories.Add(new(next.Site, actor, guard, next.Until, Away: false));
        }
        pending.Clear();

        if (memories.Count == 0 || world.Tick % 150 != 97) return;
        memories.RemoveAll(m => m.Until <= world.GameHours);
        var table = world.Characters;
        for (int i = 0; i < memories.Count; i++)
        {
            var m = memories[i];
            var actor = LastingActors.Find(world, m.Culprit);
            if (!table.TryResolveNext(actor, out int c) || table.Next[c] is not { Alive: true, Dormant: false } ||
                table.Cold(c) is not { IsPlayer: true } suspect || OtherModsPeople.Runs(world, suspect)) continue;
            var at = table.Next[c].Position;
            int town = data.TownAt(at);
            if (town < 0 || town >= data.Sites.Count || data.Sites[town].InstanceId != m.Site)
            {
                if (!m.Away) memories[i] = m with { Away = true };
                continue;
            }
            if (!m.Away || LwMod.Settings.Talk <= 0 || suspect.InCombat) continue;
            var guard = LastingActors.Find(world, m.Witness);
            if (!table.TryResolveNext(guard, out int w) || table.Next[w] is not { Alive: true, Dormant: false } ||
                table.Cold(w) is not { IsPlayer: false, Animal: null, InCombat: false, CarriedBy.IsNone: true,
                    IsImprisoned: false, Medical.Incapacitated: false } local ||
                !Local(data, town, local) || Sleep.Is(world, guard)) continue;
            var there = table.Next[w].Position;
            if (Vector3.DistanceSquared(at, there) > 150 * 150 || data.TownAt(there) != town ||
                !PrisonerHandover.ClearSight(world, at, there)) continue;

            world.Bark(guard, "You again. I remember what you did here.");
            memories.RemoveAt(i);
            if (PlaytestLog.Enabled)
                PlaytestLog.Event(world, "witness memories", m.Site, "recognised", $"{local.Name} recognised {suspect.Name}");
            break;
        }
    }

    internal void Write(BinaryWriter writer)
    {
        if (memories.Count == 0) return;
        writer.Write(Tag);
        writer.Write(memories.Count);
        foreach (var m in memories)
        {
            writer.Write(m.Site); writer.Write(m.Culprit); writer.Write(m.Witness); writer.Write(m.Until); writer.Write(m.Away);
        }
    }

    internal static WitnessMemories? Read(BinaryReader reader)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(int)) return null;
        long start = reader.BaseStream.Position;
        if (reader.ReadInt32() != Tag) { reader.BaseStream.Position = start; return null; }
        if (reader.BaseStream.Length - reader.BaseStream.Position < sizeof(int) ||
            reader.ReadInt32() is not (>= 1 and <= Capacity and var count)) return null;
        var book = new WitnessMemories();
        try
        {
            for (int i = 0; i < count; i++)
                book.memories.Add(new(reader.ReadString(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadDouble(), reader.ReadBoolean()));
        }
        catch (Exception e) when (e is IOException or FormatException) { return null; }
        return book;
    }

    internal void Hash(ref StateHasher hash)
    {
        if (memories.Count == 0 && pending.Count == 0) return;
        hash.Add(Tag);
        hash.Add(memories.Count);
        foreach (var m in memories)
        {
            hash.Add(Rng.StableHash(m.Site)); hash.Add(m.Culprit); hash.Add(m.Witness); hash.Add(m.Until); hash.Add(m.Away ? 1 : 0);
        }
        hash.Add(pending.Count);
        foreach (var p in pending)
        {
            hash.Add(Rng.StableHash(p.Site)); hash.Add(p.Culprit.Slot); hash.Add(p.Culprit.Generation);
            hash.Add(p.Witness.Slot); hash.Add(p.Witness.Generation); hash.Add(p.Until);
        }
    }
}
