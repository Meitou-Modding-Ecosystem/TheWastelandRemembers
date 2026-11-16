// Bounty hunters, the pure part (no engine types; unit-tested). The engine part is Hunters.cs.
namespace LivingWasteland.MeitouMod;

/// <summary>The pure part: when a town sends a crew, and where it starts.</summary>
public static class HunterRules
{
    public const double Hunted = 24;                 // the heat tier "hunted" (HeatMap.Tier)
    public const double Cooldown = 72;               // game hours between crews from one town
    public const double GiveUpAfter = 72;            // game hours a crew keeps hunting
    public const float StartMin = 25000f, StartMax = 35000f;   // 2.5 to 3.5 km from the player (world units are 0.1 m)

    public static bool Sends(double heat, double hoursNow, double? lastSent) =>
        heat >= Hunted && (lastSent is not { } t || hoursNow - t >= Cooldown || hoursNow < t);

    /// <summary>A start point around (x, z) at a seeded angle and distance; try a few until <paramref name="ok"/> accepts one.</summary>
    public static (float X, float Z)? Start(ulong seed, string town, double hours, float x, float z, Func<float, float, bool> ok)
    {
        ulong h = (ulong)SquadRecord.StableHash(town) ^ seed ^ (ulong)(long)hours * 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 24; i++)
        {
            h = (h ^ (h >> 31)) * 0xBF58476D1CE4E5B9UL + 0x632BE59BD9B4E019UL;
            double angle = (h & 0xFFFF) / 65536.0 * 2 * Math.PI;
            float dist = StartMin + (StartMax - StartMin) * ((h >> 16) & 0xFFFF) / 65536f;
            float px = x + (float)Math.Cos(angle) * dist, pz = z + (float)Math.Sin(angle) * dist;
            if (ok(px, pz)) return (px, pz);
        }
        return null;
    }
}

/// <summary>One crew on the hunt: the town that hired it, when, and where its stand-in was last seen.</summary>
public sealed record Crew(string Town, double SentHours, float X, float Z);


/// <summary>Outlaw reprisals, the pure part: when an outlaw the squad bloodied (and who lived) sends men after it.</summary>
public static class ReprisalRules
{
    public const double Window = 72;          // game hours after the delay in which the party may still set out
    public const double GiveUpAfter = 72;     // game hours a party keeps hunting

    /// <summary>Two to four game days after the beating (seeded per outlaw and beating, so a reload gives the same).</summary>
    public static double Delay(ulong seed, int outlaw, double struckHours)
    {
        ulong h = seed ^ (ulong)(outlaw + 1) * 0x9E3779B97F4A7C15UL ^ (ulong)(long)struckHours * 0xC2B2AE3D27D4EB4FUL;
        h = (h ^ (h >> 31)) * 0xBF58476D1CE4E5B9UL;
        return 48 + (h >> 11) % 48;
    }

    /// <summary>Whether a beating is due its answer now: bloody, not answered yet, past the delay and still within the window.</summary>
    public static bool Due(ulong seed, int outlaw, StruckMark? s, double now, double? answered)
    {
        if (s is not { Bloodied: true } || answered == s.Hours) return false;
        double at = s.Hours + Delay(seed, outlaw, s.Hours);
        return now >= at && now < at + Window;
    }
}

/// <summary>An outlaw's party out after the squad: who sent it, when, and where its stand-in was last seen.</summary>
public sealed record Reprisal(int Outlaw, double SentHours, float X, float Z);
