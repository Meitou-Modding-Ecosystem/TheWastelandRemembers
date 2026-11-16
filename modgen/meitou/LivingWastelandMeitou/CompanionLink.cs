// What two other Meitou mods add to the squad's story, when they are installed (both code mods by BeatTheCount; read by reflection, as
// TransportLink.cs reads Meitou Transport, so this mod loads the same with them or without them; nothing of theirs is changed or copied).
//
// * Guild & Quests: its GuildSystem (one of world.Systems) keeps a private `state` (GuildState) with QuestsDone, SealCount and Standing.Rank
//   (F to S). We copy those into the squad's own numbers (guild_jobs, guild_seals, guild_rank: squad titles read them) and note a rank
//   gained or a seal set in History, and for Recap. The first look in a save only takes the numbers in, so an old save is not retold.
// * Raid Commander: its RaidCommanderSystem keeps a private `commanders` (one Commander per raid on the player's base: Faction, TargetTown,
//   Won, Reason). When a raid ends because it lost, broke, thought better of it or gave up its siege, it was beaten off (raids_repelled);
//   when it won, the base was overrun. Each raid is noted once (its key is remembered in the record).
// Anything not where we expect it switches that part off with one log line.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class CompanionLink
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    sealed class Found(object? guild, object? raids) { public readonly object? Guild = guild, Raids = raids; }
    static readonly ConditionalWeakTable<World, Found> systems = new();
    static bool guildOff, raidsOff, told;
    static readonly string[] Ranks = ["F", "E", "D", "C", "B", "A", "S"];

    /// <summary>Reads both mods once; returns announcements (titles earned) for the caller to show.</summary>
    public static List<string> Poll(World world, LwDeeds deeds)
    {
        var said = new List<string>();
        var found = systems.GetValue(world, static w => new Found(
            w.Systems.FirstOrDefault(s => s.GetType().FullName == "GuildQuests.Game.GuildSystem"),
            w.Systems.FirstOrDefault(s => s.GetType().FullName == "RaidCommander.Game.RaidCommanderSystem")));
        if (!told)
        {
            told = true;
            if (found.Guild is not null) RecordHost.Log("Guild & Quests: found; guild ranks, seals and jobs go into the squad record");
            if (found.Raids is not null) RecordHost.Log("Raid Commander: found; raids on your base beaten off or lost go into the squad record");
        }
        if (found.Guild is not null && !guildOff) Guard(ref guildOff, "Guild & Quests", () => Guild(found.Guild, deeds, said));
        if (found.Raids is not null && !raidsOff) Guard(ref raidsOff, "Raid Commander", () => Raids(found.Raids, deeds, said));
        return said;
    }

    static void Guard(ref bool off, string name, Action read)
    {
        try { read(); }
        catch (Exception e)
        {
            off = true;
            RecordHost.Log($"{name}: reading it failed ({e.GetBaseException().Message}); that part of the record is off");
        }
    }

    static object? Get(object o, string name)
    {
        var t = o.GetType();
        if (t.GetProperty(name, Any) is { } p) return p.GetValue(o);
        if (t.GetField(name, Any) is { } f) return f.GetValue(o);
        throw new MissingMemberException(t.Name, name);
    }

    static void Guild(object system, LwDeeds deeds, List<string> said)
    {
        var state = Get(system, "state") ?? throw new MissingMemberException("GuildSystem", "state");
        int jobs = Convert.ToInt32(Get(state, "QuestsDone"));
        int seals = Convert.ToInt32(Get(state, "SealCount"));
        int rank = Convert.ToInt32(Get(Get(state, "Standing")!, "Rank"));
        var record = deeds.Record;
        bool first = !record.HasCrewStat("guild_rank");
        int wasRank = (int)record.CrewStat("guild_rank"), wasSeals = (int)record.CrewStat("guild_seals"), wasJobs = (int)record.CrewStat("guild_jobs");
        said.AddRange(record.SetCrewStat("guild_jobs", jobs));
        said.AddRange(record.SetCrewStat("guild_seals", seals));
        said.AddRange(record.SetCrewStat("guild_rank", rank));
        if (first) return;
        if (rank > wasRank && rank >= 0 && rank < Ranks.Length)
        {
            string text = rank == Ranks.Length - 1 ? "The squad reached guild rank S, the top of the Adventurers Guild." : $"The squad reached guild rank {Ranks[rank]}.";
            record.Note(text);
            record.OnMoment?.Invoke(text, rank == Ranks.Length - 1 ? 85 : 60);
        }
        if (seals > wasSeals)
        {
            string text = seals >= 20 ? "Every guild has set its seal on the squad's charter." : $"Another guild set its seal on the squad's charter ({seals} of 20).";
            record.Note(text);
            if (seals >= 20 || seals % 5 == 0) record.OnMoment?.Invoke(text, seals >= 20 ? 85 : 55);
        }
        if (jobs / 10 > wasJobs / 10) record.Note($"The squad has done {jobs / 10 * 10} jobs for the guild.");
    }

    static void Raids(object system, LwDeeds deeds, List<string> said)
    {
        if (Get(system, "commanders") is not IDictionary commanders) throw new MissingMemberException("RaidCommanderSystem", "commanders");
        var record = deeds.Record;
        foreach (DictionaryEntry e in commanders)
        {
            var c = e.Value;
            if (c is null) continue;
            string reason = Get(c, "Reason")?.ToString() ?? "None";
            bool won = Get(c, "Won") is true;
            if (reason == "None" && !won) continue;
            if (!record.NewEvent($"raidcommander:{e.Key}")) continue;
            string faction = deeds.FactionName(Convert.ToInt32(Get(c, "Faction")));
            string who = faction.Length > 0 ? $"{faction} raiders" : "Raiders";
            string town = deeds.SiteName(Convert.ToInt32(Get(c, "TargetTown"))) ?? "your base";
            string? text = won || reason == "Overrun" ? $"{who} overran {town}."
                : reason is "Losses" or "Rout" ? $"{who} were beaten back from {town}."
                : reason == "Hopeless" ? $"{who} took one look at {town}'s defenders and turned back."
                : reason == "SiegeGivenUp" ? $"{who} gave up their siege of {town}."
                : null;                                     // weather, a timeout, the game ending it, and the like: nothing to tell
            if (text is null) continue;
            record.Note(text);
            record.OnMoment?.Invoke(text, won || reason == "Overrun" ? 75 : 70);
            if (!(won || reason == "Overrun")) said.AddRange(record.SetCrewStat("raids_repelled", record.CrewStat("raids_repelled") + 1));
        }
    }
}
