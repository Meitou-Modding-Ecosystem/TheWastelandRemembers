using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>Milestones the squad saw for itself. Annual receipts use the existing squad save record.</summary>
internal static class Anniversaries
{
    const double Year = 100 * 24; // Vanilla Kenshi's days per year.
    const string FirstRival = "@anniversary first rival beaten";

    internal static void WonAgainstARival(World world)
    {
        if (!LwMod.Settings.Anniversaries || LwDeeds.For(world)?.Record is not { } record || record.HasCrewStat(FirstRival)) return;
        _ = record.SetCrewStat(FirstRival, world.GameHours);
    }

    internal static void Look(World world, SquadRecord record)
    {
        if (record.FirstService is { } started)
            Remember("founded", started, "the wilds", "your squad first set out");
        if (OutpostMemories.For(world)?.FirstFoundation() is { } post)
            Remember("outpost", post.Hours, post.Name, $"your squad founded {post.Name}");
        if (!record.HasCrewStat(FirstRival))
        {
            // Older builds already put a confirmed loss by a returning rival into History.
            foreach (var line in record.History)
                if (line.Text.EndsWith(" lost again and got away again.", StringComparison.Ordinal))
                {
                    _ = record.SetCrewStat(FirstRival, line.Hours);
                    break;
                }
        }
        if (record.HasCrewStat(FirstRival))
            Remember("rival", record.CrewStat(FirstRival), "the wilds", "your squad first bested a returning rival");

        bool spoke = false;
        foreach (var fallen in record.FallenMembers)
        {
            string where = fallen.Place == "the wilds" ? "in the wilds" : $"at {fallen.Place}";
            string happened = $"{record.Display(fallen.Name)} fell {where}";
            if (Remember("fallen " + fallen.Name, fallen.Hours, fallen.Place, happened) is not { } line ||
                spoke || record.InFight || !LwMod.Settings.Memorials || LwMod.Settings.Talk == 0) continue;
            var heir = KeepsakeHolder(world, fallen.Name);
            if (heir.IsNone) continue;
            world.Bark(heir, line);
            spoke = true;
        }

        string? Remember(string id, double hours, string place, string happened)
        {
            double elapsed = world.GameHours - hours;
            int years = (int)Math.Floor(elapsed / Year);
            if (years < 1 || elapsed - years * Year >= 24) return null;
            string key = "@anniversary year:" + id;
            if (record.CrewStat(key) >= years) return null;
            string line = (years == 1 ? "A year since " : "Another year since ") + happened + ".";
            record.Note(line, world.GameHours, place);
            _ = record.SetCrewStat(key, years);
            if (PlaytestLog.Enabled) PlaytestLog.Event(world, "anniversary", id, "remembered", line);
            return line;
        }
    }

    internal static CharacterId KeepsakeHolder(World world, string fallen)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (CanSpeak(world, slot)?.Inventory is not { } bag) continue;
            var id = table.IdOf(slot);
            foreach (var item in bag.Items)
                if (KeepsakeBook.Slots.Contains(item.Record) && KeepsakeBook.FallenKeyOf(ItemUses.Tag(item)) == fallen) return id;
        }
        return CharacterId.None;
    }

    internal static CharacterCold? CanSpeak(World world, int slot)
    {
        var table = world.Characters;
        return table.Next[slot].Alive && !table.Next[slot].Dormant && table.Cold(slot) is
            { IsPlayer: true, Animal: null, IsImprisoned: false, InCombat: false, Medical.Dead: false,
                Medical.Incapacitated: false } actor && actor.CarriedBy.IsNone && !OtherModsPeople.Runs(world, actor) &&
            !Sleep.Is(world, table.IdOf(slot)) ? actor : null;
    }
}
