using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace LivingWasteland.MeitouMod;

internal static class KindnessWatch
{
    static readonly System.Reflection.PropertyInfo? probablyDying = typeof(MedicalState).GetProperty("ProbablyDying");

    internal static bool Available => LastingActors.Available && AidUseWatch.Available;
    internal static bool Enabled => Available && LwMod.Settings.KindnessRemembered;

    internal static void Aid(World world, LwDeeds deeds, CharacterId medic, CharacterCold helper)
    {
        if (!Enabled || helper.Animal is not null || helper.TreatSplint || OtherModsPeople.Runs(world, helper)) return;
        var patient = helper.TreatTarget;
        var table = world.Characters;
        if (patient == medic || !table.TryResolveNext(patient, out int slot) || !table.Next[slot].Alive ||
            table.Cold(slot) is not { IsPlayer: false, Animal: null, Medical: { Dead: false } health } actor) return;
        ulong actorId = LastingActors.Of(actor);
        double now = world.GameHours;
        if (deeds.Kindness.WasRecorded(actorId, KindnessKind.Saved, now) || deeds.Kindness.DownedBySquad(actorId, now)) return;
        if (!KindnessRules.InDanger(health.Blood, health.Bleeding, health.Unconscious, false) && probablyDying?.GetValue(health) is not true) return;
        Remember(world, deeds, patient, actor, KindnessKind.Saved, helper.Name, critical: true);
    }

    internal static void Remember(World world, LwDeeds deeds, CharacterId who, CharacterCold actor, KindnessKind kind,
        string helper, bool critical = false, double heldHours = 0, bool alone = false, string captor = "")
    {
        ulong id = LastingActors.Of(actor);
        if (id == 0) id = LastingActors.Grant(world, who);
        if (id == 0) return;
        var skills = actor.Stats is { } stats
            ? new KindnessSkills(stats[StatsEnumerated.Strength], stats[StatsEnumerated.Toughness], stats[StatsEnumerated.MeleeAttack])
            : default;
        string? named = world.Uniques.RecordOf(who);
        string place = deeds.TownAt(world.Characters.Next[who.Slot].Position).Town ?? "the wilds";
        var memory = new KindnessDeed(id, kind, actor.Name, named ?? actor.RecordId ?? "", deeds.FactionName(actor.Faction),
            place, helper, world.GameHours, skills, critical, named is not null, heldHours, alone, captor);
        if (!deeds.Kindness.Add(memory)) return;
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "kindness", FormattableString.Invariant($"person:{id}"), kind.ToString(), $"{helper} helped {actor.Name} at {place}");
        LogTally.Add("kindness deeds");
    }

    internal static string What(KindnessDeed deed) => deed.Kind switch
    {
        KindnessKind.Freed => $"whom {deed.Helper} freed in {deed.Place}",
        KindnessKind.EscapedTogether => "who escaped Rebirth with the squad",
        _ => $"whom {deed.Helper} tended in {deed.Place}",
    };

    internal static void Death(World world, LwDeeds deeds, CharacterId id, CharacterCold actor, int killerFaction)
    {
        if (!Enabled || deeds.Kindness.People.Count == 0 || LastingActors.Of(actor) is not (> 0 and var stable) ||
            deeds.Kindness.Close(stable) is not { } memory) return;
        var at = world.Characters.Next[id.Slot].Position;
        string place = deeds.TownAt(at).Town ?? "the wilds";
        string faction = deeds.FactionName(killerFaction);
        string ending = faction.Length == 0 ? "died" : killerFaction == world.Player.Faction ? "was killed by the squad" : $"was killed by {faction}";
        string line = $"{actor.Name}, {What(memory)}, {ending} in {place}.";
        deeds.Record.Note(line, world.GameHours, place);
        RecapLink.Moment(line, place, 45, at);
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "kindness", FormattableString.Invariant($"person:{stable}"), "died", line);
    }
}
