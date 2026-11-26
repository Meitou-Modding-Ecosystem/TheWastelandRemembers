using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal static class SquadMemories
{
    const double FirstQuietHours = 48, Gap = 7 * 24;
    const string LastMemory = "@squad-memory last";
    const string MemoryOf = "@squad-memory spoke about ";

    internal static void Look(World world, LwDeeds deeds)
    {
        var record = deeds.Record;
        if (!LwMod.Settings.Memorials || LwMod.Settings.Talk == 0 || record.InFight ||
            record.HasCrewStat(LastMemory) && world.GameHours - record.CrewStat(LastMemory) < Gap) return;

        var fallen = record.FallenMembers;
        for (int i = fallen.Count - 1; i >= 0; i--)
        {
            var friend = fallen[i];
            string key = MemoryOf + friend.Name;
            if (world.GameHours - friend.Hours < FirstQuietHours || record.HasCrewStat(key)) continue;
            var avenger = friend.AvengedBy is { } by ? Squadmate(world, record, by) : CharacterId.None;
            var speaker = avenger.IsNone ? Anniversaries.KeepsakeHolder(world, friend.Name) : avenger;
            if (speaker.IsNone) continue;

            string name = record.Display(friend.Name);
            string line = avenger.IsNone ? $"I still think of {name}, when things go quiet."
                : $"I settled the score for {name}. I still miss them.";
            _ = record.SetCrewStat(LastMemory, world.GameHours);
            _ = record.SetCrewStat(key, world.GameHours);
            world.Bark(speaker, line);
            if (PlaytestLog.Enabled) PlaytestLog.Event(world, "squad memory", friend.Name, "remembered", line);
            break;
        }
    }

    static CharacterId Squadmate(World world, SquadRecord record, string key)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
            if (Anniversaries.CanSpeak(world, slot) is { } actor &&
                record.PeekKey(LwDeeds.Identity(actor), actor.Name) == key) return table.IdOf(slot);
        return CharacterId.None;
    }
}
