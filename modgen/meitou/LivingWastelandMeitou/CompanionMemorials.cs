using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Animal memorials sit beside the human Fallen, without starting a feud or a keepsake.</summary>
internal sealed class CompanionMemorials
{
    const int Tag = 0x31544550; // PET1
    const int Capacity = 64;
    internal const int Battles = 10;
    readonly record struct Memorial(string Name, double Hours, string Place, bool Decorated);
    readonly List<Memorial> fallen = [];

    internal static void Survived(World world, SquadRecord record, string key)
    {
        var member = record.Member(key);
        if (member.Titles.Contains(SquadRecord.CompanionTitleId)) return;
        string count = "@companion fights " + key;
        double total = record.CrewStat(count) + 1;
        record.SetCrewStat(count, total);
        if (total < Battles) return;
        member.Titles.Add(SquadRecord.CompanionTitleId);
        string line = $"{record.Display(key)} is now known as {SquadRecord.CompanionTitleName}.";
        record.Note(line, world.GameHours, record.Place);
        if (RecordHost.Announce()) world.Post(line);
        if (PlaytestLog.Enabled) PlaytestLog.Event(world, "animal companions", key, "earned title", line);
    }

    internal static void Died(World world, SquadRecord record, CharacterCold animal, string? town)
    {
        if (!LwMod.Settings.AnimalCompanions || animal.PortraitSerial is null ||
            OtherModsPeople.Runs(world, animal) || LwState.RegisteredIn(world) is not { } state) return;
        string key = record.KeyFor(LwDeeds.Identity(animal), animal.Name);
        if (!record.NewEvent("@companion died " + key)) return;
        var book = state.Companions ??= new();
        string place = town ?? "the wilds";
        bool titled = record.Member(key).Titles.Contains(SquadRecord.CompanionTitleId);
        if (book.fallen.Count >= Capacity) book.fallen.RemoveAt(0);
        book.fallen.Add(new(animal.Name, world.GameHours, place, titled));
        string line = $"{animal.Name}, a companion of the squad, died {(town is null ? "in" : "at")} {place}.";
        record.Note(line, world.GameHours, place);
        if (PlaytestLog.Enabled) PlaytestLog.Event(world, "animal companions", key, "fallen", line);
    }

    internal void AppendFallen(List<string> lines, bool humans)
    {
        if (fallen.Count == 0) return;
        if (humans) lines.Add("");
        else lines.Clear();
        lines.Add("-- Animal companions --");
        for (int i = fallen.Count - 1; i >= 0; i--)
        {
            var row = fallen[i];
            lines.Add($"{row.Name}, a companion of the squad, died on day {(int)(row.Hours / 24)} {(row.Place == "the wilds" ? "in" : "at")} {row.Place}.");
            if (row.Decorated) lines.Add("   Known as " + SquadRecord.CompanionTitleName + ".");
        }
    }

    internal void Write(BinaryWriter writer)
    {
        if (fallen.Count == 0) return;
        writer.Write(Tag);
        writer.Write(fallen.Count);
        foreach (var row in fallen)
        {
            writer.Write(row.Name); writer.Write(row.Hours); writer.Write(row.Place); writer.Write(row.Decorated);
        }
    }

    internal static CompanionMemorials? Read(BinaryReader reader)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < 2 * sizeof(int)) return null;
        long start = reader.BaseStream.Position;
        if (reader.ReadInt32() != Tag) { reader.BaseStream.Position = start; return null; }
        if (reader.ReadInt32() is not (>= 1 and <= Capacity and var count)) return null;
        var book = new CompanionMemorials();
        try
        {
            for (int i = 0; i < count; i++)
                book.fallen.Add(new(reader.ReadString(), reader.ReadDouble(), reader.ReadString(), reader.ReadBoolean()));
        }
        catch (Exception e) when (e is IOException or FormatException) { return null; }
        return book;
    }

    internal void Hash(ref StateHasher hasher)
    {
        hasher.Add(Tag); hasher.Add(fallen.Count);
        foreach (var row in fallen)
        {
            hasher.Add(Rng.StableHash(row.Name)); hasher.Add(row.Hours); hasher.Add(Rng.StableHash(row.Place));
            hasher.Add(row.Decorated ? 1 : 0);
        }
    }
}
