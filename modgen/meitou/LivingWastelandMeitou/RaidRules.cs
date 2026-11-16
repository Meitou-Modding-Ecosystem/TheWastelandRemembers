// Harassment raids' rules (pure; compiled by the tests too). Raids.cs runs them.
namespace LivingWasteland.MeitouMod;

/// <summary>A raid out now, or the last one: where (its town's place in the news towns), when it set out, how it ended.</summary>
public sealed record RaidRecord(int Town, double SentHours, float X, float Z, double EndedHours = -1, bool Caught = false, bool Helped = false, bool Broke = false)
{
    public bool Out => EndedHours < 0;
}

public static class RaidRules
{
    public const double Lurk = 48;          // game hours a party stays out
    public const double Talked = 120;       // game hours the outcome is talked about
    public const double GapMin = 96, GapMax = 216;   // game hours between one raid's end and the next of the same faction

    /// <summary>When the next raid of <paramref name="raid"/> sets out: seeded per raid and per last end, so a reload gives the same.</summary>
    public static double NextStart(ulong seed, int raid, double lastEnd)
    {
        ulong h = seed ^ (ulong)(raid + 11) * 0x9E3779B97F4A7C15UL ^ (ulong)(long)Math.Max(0, lastEnd) * 0xC2B2AE3D27D4EB4FUL;
        h = (h ^ (h >> 29)) * 0xBF58476D1CE4E5B9UL;
        return Math.Max(0, lastEnd) + GapMin + (h >> 11) % (ulong)(GapMax - GapMin);
    }

    /// <summary>Which of <paramref name="count"/> candidate towns a raid goes to (seeded per raid and start).</summary>
    public static int PickTown(ulong seed, int raid, double start, int count)
    {
        if (count <= 0) return -1;
        ulong h = seed * 0x2545F4914F6CDD1DUL ^ (ulong)(raid + 3) * 0x9E3779B97F4A7C15UL ^ (ulong)(long)start;
        h ^= h >> 31;
        return (int)(h % (ulong)count);
    }

    public const float Beside = 1500;       // 150 m: fighting the target's people this close to a raid party is helping it
    public const int Thanks = 6;            // relations for helping a raid, or breaking one

    /// <summary>What the squad's part in a raid earns when it ends: (the raider's thanks, the target's thanks).</summary>
    public static (int Raider, int Target) Settle(RaidRecord r) => r.Broke ? (0, Thanks) : r.Helped ? (Thanks, 0) : (0, 0);

    /// <summary>The state the gossip reads: 1 out, 2 caught, 3 got away (while talked about), else 0.</summary>
    public static int State(RaidRecord? r, double now) =>
        r is null ? 0 : r.Out ? 1 : now - r.EndedHours < Talked && now >= r.EndedHours ? (r.Caught ? 2 : 3) : 0;
}
