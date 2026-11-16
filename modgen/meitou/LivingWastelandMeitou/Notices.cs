// Floating notices (shared by the Living Wasteland helper and Squad Record; the "Notices" setting): a short stack of messages drawn by the
// Squad record overlay (StatsPanel.cs) that fade after a few seconds:
// * a skill level gained: "Beep  Melee Attack 23" (one of the squad, the skill as Kenshi's skill screen names it, the new level);
// * a change in a faction's standing with you: "Holy Nation +3" in green, "-3" in red (factions you know of; a change of at least 1, shown
//   once the standing has held still a few seconds, so a fight's many small drops show as one).
// The first look at a character or a faction only takes its numbers in, so loading a save or recruiting someone never floods the screen.
// The simulation side (Watch) runs in the record's slow tick and only reads; the overlay reads the list under a lock with wall-clock times.
using System.Diagnostics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public static class Notices
{
    public enum Kind { Skill, Up, Down }
    public sealed record Notice(string Text, Kind Kind, long At);

    public static readonly string[] Modes = ["Skills and standing", "Skills only", "Standing only", "Off"];
    public static volatile string Mode = Modes[0];
    public static readonly string[] Places = ["Top centre", "Left side", "Right side"];
    public static volatile string Place = Places[0];
    public const double ShowSeconds = 6, FadeSeconds = 1.2;
    const int Max = 6;

    static readonly Stopwatch clock = Stopwatch.StartNew();
    static readonly List<Notice> shown = [];
    public static double Now => clock.Elapsed.TotalSeconds;

    /// <summary>The notices still on screen, newest first, with their age in seconds.</summary>
    public static List<(Notice N, double Age)> Current()
    {
        double now = Now;
        lock (shown)
        {
            shown.RemoveAll(n => now - n.At / 1000.0 > ShowSeconds);
            return shown.AsEnumerable().Reverse().Select(n => (n, now - n.At / 1000.0)).ToList();
        }
    }

    static void Add(string text, Kind kind)
    {
        lock (shown)
        {
            shown.Add(new Notice(text, kind, (long)(Now * 1000)));
            if (shown.Count > Max) shown.RemoveAt(0);
        }
    }

    // Kenshi's names for the skills its skill screen shows (the stats that only mirror another, or that it never shows, are left out).
    static readonly Dictionary<StatsEnumerated, string> SkillNames = new()
    {
        [StatsEnumerated.Strength] = "Strength", [StatsEnumerated.Dexterity] = "Dexterity", [StatsEnumerated.Toughness] = "Toughness",
        [StatsEnumerated.Perception] = "Perception", [StatsEnumerated.MeleeAttack] = "Melee Attack", [StatsEnumerated.MeleeDefence] = "Melee Defence",
        [StatsEnumerated.Dodge] = "Dodge", [StatsEnumerated.MartialArts] = "Martial Arts", [StatsEnumerated.Katanas] = "Katanas",
        [StatsEnumerated.Sabres] = "Sabres", [StatsEnumerated.Hackers] = "Hackers", [StatsEnumerated.HeavyWeapons] = "Heavy Weapons",
        [StatsEnumerated.Blunt] = "Blunt", [StatsEnumerated.Polearms] = "Polearms", [StatsEnumerated.Crossbows] = "Crossbows",
        [StatsEnumerated.FriendlyFire] = "Precision Shooting", [StatsEnumerated.Turrets] = "Turrets", [StatsEnumerated.Athletics] = "Athletics",
        [StatsEnumerated.Swimming] = "Swimming", [StatsEnumerated.Stealth] = "Stealth", [StatsEnumerated.Assassination] = "Assassination",
        [StatsEnumerated.Lockpicking] = "Lockpicking", [StatsEnumerated.Thieving] = "Thievery", [StatsEnumerated.Medic] = "Field Medic",
        [StatsEnumerated.Engineering] = "Engineering", [StatsEnumerated.Robotics] = "Robotics", [StatsEnumerated.Science] = "Science",
        [StatsEnumerated.SmithingWeapon] = "Weapon Smith", [StatsEnumerated.SmithingArmour] = "Armour Smith", [StatsEnumerated.SmithingBow] = "Bow Smith",
        [StatsEnumerated.Labouring] = "Labouring", [StatsEnumerated.Farming] = "Farming", [StatsEnumerated.Cooking] = "Cooking",
    };

    static readonly Dictionary<string, int[]> levels = new(StringComparer.Ordinal);   // character identity -> level per skill (-1 unknown)
    static readonly Dictionary<int, float> standing = [];                              // faction -> the relation last shown
    static World? seen;

    /// <summary>Looks at the squad's skills and the known factions' standing (every second or so) and adds notices for what went up or down.</summary>
    public static void Watch(World world, PopulationData? data)
    {
        if (!ReferenceEquals(seen, world)) { seen = world; levels.Clear(); standing.Clear(); moving.Clear(); rising.Clear(); }
        string mode = Mode;
        if (mode == "Off") return;
        if (mode != "Standing only") Skills(world);
        if (mode != "Skills only" && data is not null) Standing(world, data);
    }

    static void Skills(World world)
    {
        var table = world.Characters;
        var hot = table.Previous;
        for (int s = 0; s < hot.Length; s++)
        {
            if (!hot[s].Alive || table.Cold(s) is not { IsPlayer: true, Animal: null, Stats: { } stats } c) continue;
            string key = LwDeeds.Identity(c);
            bool first = !levels.TryGetValue(key, out int[]? got);
            int[] was = got ?? (levels[key] = new int[(int)StatsEnumerated.End + 1]);
            foreach (var (stat, name) in SkillNames)
            {
                int now = (int)MathF.Floor(stats[stat]);
                int i = (int)stat;
                if (first || now <= was[i] || now > 100) { if (first || now < was[i]) was[i] = now; continue; }
                // a skill still climbing (several levels in one fight) shows once, at the level it reached, after holding 5 seconds
                var rk = (key, i);
                if (!rising.TryGetValue(rk, out var r) || r.Level != now) { rising[rk] = (now, world.Tick); continue; }
                if (world.Tick - r.Since < Settle && world.Tick >= r.Since) continue;
                Add($"{c.Name}   {name} {now}", Kind.Skill);
                was[i] = now;
                rising.Remove(rk);
            }
        }
    }

    static readonly Dictionary<(string, int), (int Level, long Since)> rising = [];   // (character, skill) -> the level it is climbing to
    static readonly Dictionary<int, (float Value, long Since)> moving = [];   // faction -> the value last seen and the tick it last changed
    const long Settle = 30 * 5;                                                // ticks a standing must hold still before its notice shows

    /// <summary>
    /// A faction's standing shows once it has settled: in a fight with a town every blow moves it, and a notice for each would bury the
    /// screen, so the changes add up while they keep coming and show as one ("United Cities  -17") once the number has held still 5 seconds.
    /// </summary>
    static void Standing(World world, PopulationData data)
    {
        int player = world.Player.Faction;
        if (player < 0) return;
        foreach (int f in world.Player.KnownFactions)
        {
            if (f < 0 || f >= data.Factions.Count || f == player) continue;
            float now = data.Relations.Get(player, f);
            if (!standing.TryGetValue(f, out float last)) { standing[f] = now; moving[f] = (now, world.Tick); continue; }
            if (!moving.TryGetValue(f, out var mv) || MathF.Abs(mv.Value - now) >= 0.01f) { moving[f] = (now, world.Tick); continue; }
            if (world.Tick - mv.Since < Settle && world.Tick >= mv.Since) continue;
            float delta = now - last;
            if (MathF.Abs(delta) < 1) continue;
            standing[f] = now;
            int d = (int)MathF.Round(delta);
            string name = data.Factions[f].Name;
            if (name.StartsWith("The ", StringComparison.Ordinal)) name = name[4..];
            Add($"{name}  {(d > 0 ? "+" : "")}{d}", d > 0 ? Kind.Up : Kind.Down);
        }
    }
}
