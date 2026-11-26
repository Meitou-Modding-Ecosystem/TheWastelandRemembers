using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal static class KindnessReunions
{
    const float SpeakingRange = 300;

    internal static void Listen(World world, LwDeeds deeds, List<(string Name, CharacterId Id, Vector3 At, bool Down)> squad)
    {
        var book = deeds.Kindness;
        if (KindnessVoices.Current is not { } voices || world.GameHours < book.NextReunion || squad.Count == 0) return;
        var table = world.Characters;
        for (int i = 0; i < book.People.Count; i++)
        {
            var memory = book.People[i];
            if (book.HasReunited(memory.Actor) || book.Home(memory.Actor) is not { Approached: true } home) continue;
            var id = LastingActors.Find(world, memory.Actor);
            if (!table.TryResolveNext(id, out int slot) || !table.Next[slot].Alive || table.Cold(slot) is not
                { IsPlayer: false, Animal: null, IsImprisoned: false, InCombat: false, CarriedBy.IsNone: true, Medical.Incapacitated: false } npc ||
                (deeds.Data is { } data && world.Player.Faction >= 0 && npc.Faction >= 0 && data.Relations.Get(world.Player.Faction, npc.Faction) <= -50)) continue;
            var at = table.Next[slot].Position;
            if (!home.Contains(at.X, at.Z) || !SomeoneIsListening(table, squad, at)) continue;
            var now = npc.Stats is { } stats
                ? new KindnessSkills(stats[StatsEnumerated.Strength], stats[StatsEnumerated.Toughness], stats[StatsEnumerated.MeleeAttack])
                : default;
            if (voices.Line(memory, deeds.FactionName(npc.Faction), world.GameHours, now) is not { } line) continue;

            book.MarkReunited(memory.Actor, world.GameHours + 24 * Math.Clamp(LwMod.Settings.KindnessReunionDays, 1, 14));
            line += PayBack(world, npc, at, deeds.FactionName(npc.Faction), memory.Actor, voices);
            world.Bark(id, line);
            var history = $"The squad met {npc.Name}, {KindnessWatch.What(memory)}, again in {home.Place}.";
            deeds.Record.Note(history, world.GameHours, home.Place);
            RecapLink.Moment(history, home.Place, 45, at);
            if (PlaytestLog.Enabled)
                PlaytestLog.Event(world, "kindness", FormattableString.Invariant($"person:{memory.Actor}"), "reunited", $"{npc.Name} at {home.Place}");
            LogTally.Add("kindness reunions");
            return;
        }
    }

    static string PayBack(World world, CharacterCold neighbour, Vector3 at, string faction, ulong actor, KindnessVoices voices)
    {
        if (NewsSystem.For(world)?.ShareNearbyReport(world, at) is { } report)
        {
            if (PlaytestLog.Enabled)
                PlaytestLog.Event(world, "kindness", FormattableString.Invariant($"person:{actor}"), "news shared", report.Place);
            return " " + voices.Word + " " + report.Text;
        }

        if (world.Squads.Find(neighbour.SquadId) is not { Money: > 0 } purse ||
            purse.Faction != neighbour.Faction || purse.Faction == world.Player.Faction) return "";
        int ceiling = ProstheticRules.Of(faction) switch
        {
            ProstheticRules.Tier.Wealthy => 250,
            ProstheticRules.Tier.Middle => 90,
            _ => 30,
        };
        long gift = Math.Min(purse.Money, ceiling);
        if (world.Player.Money > long.MaxValue - gift) return "";
        purse.Money -= gift;
        world.Player.Money += gift;
        if (world.Platoons.Find(purse.PlatoonId) is { } party && party.SquadId == purse.Id) party.Money = purse.Money;
        world.Post(FormattableString.Invariant($"{neighbour.Name} gave the squad {gift} cats."));
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "kindness", FormattableString.Invariant($"person:{actor}"), "gave cats", FormattableString.Invariant($"{gift} cats"));
        return " " + voices.Gift;
    }

    static bool SomeoneIsListening(CharacterTable table, List<(string Name, CharacterId Id, Vector3 At, bool Down)> squad, Vector3 at)
    {
        foreach (var player in squad)
            if (!player.Down && Vector3.DistanceSquared(player.At, at) <= SpeakingRange * SpeakingRange &&
                table.Cold(player.Id.Slot) is { InCombat: false }) return true;
        return false;
    }
}
