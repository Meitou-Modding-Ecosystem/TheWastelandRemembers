using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public sealed partial class LwDeedsSystem
{
    const int FightTag = 0x4C574631; // LWF1
    const int MaxFightFoes = 4096;
    const int RestoreFoeLooks = 5;

    readonly record struct FightFoe(ulong Lasting, string Identity);
    readonly Dictionary<CharacterId, FightFoe> restoredFoes = [];
    List<(FightFoe Foe, bool Waking)>? savedFoes;
    int restoreFoeLooks;

    void RememberFoe(World world, CharacterId id, CharacterCold actor)
    {
        if (foes.ContainsKey(id)) return;
        ulong lasting = 0;
#if !SQUAD_RECORD_ONLY
        lasting = LastingActors.Of(actor);
        if (lasting == 0) lasting = LastingActors.Grant(world, id);
#endif
        foes.Add(id, new(lasting, StableFoe(world, id, actor)));
    }

    // Named uniques and portrait serials have stable provenance. A generic template or display name can be shared by many fighters.
    static string StableFoe(World world, CharacterId id, CharacterCold actor) =>
        world.Uniques.RecordOf(id) is { } record ? "unique:" + record :
        actor.PortraitSerial is not null ? "portrait:" + LwDeeds.Identity(actor) : "";

    void BindSavedFoes(World world)
    {
        if (savedFoes is not { Count: > 0 } pending) return;
        restoreFoeLooks++;
        var matches = new Dictionary<FightFoe, CharacterId>();
        var wanted = pending.Select(row => row.Foe).ToHashSet();
        bool needsIdentity = pending.Any(row => row.Foe.Lasting == 0);
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: false, Medical.Dead: false } actor) continue;
            var id = table.IdOf(slot);
            ulong lasting = 0;
#if !SQUAD_RECORD_ONLY
            lasting = LastingActors.Of(actor);
#endif
            if (lasting != 0) Match(new(lasting, ""), id);
            if (needsIdentity)
            {
                var identity = StableFoe(world, id, actor);
                if (identity.Length > 0) Match(new(0, identity), id);
            }
        }
        // Saved actors can stream in after the squad. Retry within the normal settling window without delaying it.
        pending.RemoveAll(row =>
        {
            if (!matches.TryGetValue(row.Foe, out var id) || id.IsNone) return false;
            restoredFoes[id] = row.Foe;
#if !SQUAD_RECORD_ONLY
            if (row.Waking) wakingFoes.Add(id);
#endif
            return true;
        });
        if (pending.Count == 0 || restoreFoeLooks >= RestoreFoeLooks) savedFoes = null;

        void Match(FightFoe foe, CharacterId id)
        {
            if (wanted.Contains(foe) && !matches.TryAdd(foe, id)) matches[foe] = CharacterId.None;
        }
    }

    void WriteFightTrailer(BinaryWriter writer)
    {
        if (!deeds.Record.InFight) return;
        var stream = writer.BaseStream;
        long start = stream.Position;
        writer.Write(FightTag);
        writer.Write(lastFightPlace);
        deeds.Record.WriteFight(writer);
        var live = restoredFoes.Concat(foes).Select(pair => (Foe: pair.Value, Waking:
#if !SQUAD_RECORD_ONLY
            wakingFoes.Contains(pair.Key)
#else
            false
#endif
        ));
        var saved = (savedFoes ?? []).Concat(live)
            .Select(row => (Foe: row.Foe.Lasting != 0 ? new FightFoe(row.Foe.Lasting, "") : row.Foe, row.Waking))
            .Where(row => row.Foe.Lasting != 0 || row.Foe.Identity.Length > 0)
            .GroupBy(row => row.Foe).OrderBy(group => group.Key.Lasting).ThenBy(group => group.Key.Identity, StringComparer.Ordinal)
            .Take(MaxFightFoes).ToArray();
        writer.Write(saved.Length);
        foreach (var group in saved)
        {
            writer.Write(group.Key.Lasting);
            writer.Write(group.Key.Identity);
            writer.Write(group.Any(row => row.Waking));
        }
        // A footer keeps this independent of optional older extensions, some of whose readers peek at the next tag.
        writer.Write(checked((int)(stream.Position - start)));
        writer.Write(FightTag);
    }

    void ReadFightTrailer(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        if (stream.Length < 3 * sizeof(int)) return;
        stream.Position = stream.Length - 2 * sizeof(int);
        int length = reader.ReadInt32();
        if (reader.ReadInt32() != FightTag || length < sizeof(int) || length > 4 * 1024 * 1024 ||
            length > stream.Length - 2 * sizeof(int)) return;
        stream.Position -= length + 2 * sizeof(int);
        if (reader.ReadInt32() != FightTag) return;

        try
        {
            lastFightPlace = reader.ReadString();
            deeds.Record.ReadFight(reader);
            int count = reader.ReadInt32();
            if (count is < 0 or > MaxFightFoes) throw new InvalidDataException("Invalid fight opponent count.");
            savedFoes = new(count);
            for (int i = 0; i < count; i++)
                savedFoes.Add((new(reader.ReadUInt64(), reader.ReadString()), reader.ReadBoolean()));
            if (stream.Length - stream.Position != 2 * sizeof(int)) throw new InvalidDataException("Invalid fight trailer length.");
        }
        catch (Exception e) when (e is IOException or FormatException or InvalidDataException)
        {
            ResetFightTrailer();
            RecordHost.Log($"the saved fight could not be read ({e.Message}); keeping the rest of the squad record");
        }
    }

    void ResetFightTrailer()
    {
        deeds.Record.ResetFight();
        foes.Clear();
        restoredFoes.Clear();
        savedFoes = null;
        restoreFoeLooks = 0;
        lastEnemy = CharacterId.None;
        lastFightPlace = "the wilds";
#if !SQUAD_RECORD_ONLY
        wakingFoes.Clear();
        ResetDefeatDialogue();
#endif
    }
}
