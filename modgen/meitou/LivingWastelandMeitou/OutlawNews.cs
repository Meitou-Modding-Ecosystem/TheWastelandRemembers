// What each living named outlaw is up to (pure; see WorldNews.cs for how the rumour lines use it).
namespace LivingWasteland.MeitouMod;

/// <summary>The pure part: which activity an outlaw is on at a given game hour.</summary>
public static class OutlawNews
{
    public const int Raiding = 1, LyingLow = 2, Gathering = 3, Wounded = 4;

    /// <summary>Game hours one activity lasts (each outlaw's stretches are offset so they do not all change at once).</summary>
    public const double Stretch = 60;

    /// <summary>The number of the outlaw's current stretch (a new one every <see cref="Stretch"/> game hours, offset per outlaw).</summary>
    public static long Period(int outlaw, double gameHours) => (long)Math.Floor((gameHours + outlaw * 13.7) / Stretch);

    public static int Doing(ulong seed, int outlaw, double gameHours) => DoingIn(seed, outlaw, Period(outlaw, gameHours));

    /// <summary>What the outlaw does in one stretch (by its number).</summary>
    public static int DoingIn(ulong seed, int outlaw, long period)
    {
        ulong h = Mix(seed ^ (ulong)(outlaw + 1) * 0x9E3779B97F4A7C15UL ^ (ulong)period * 0xC2B2AE3D27D4EB4FUL);

        // raiding comes up most often; a fresh period never repeats "licking wounds" for long
        int roll = (int)(h % 10);
        return roll < 4 ? Raiding : roll < 6 ? LyingLow : roll < 8 ? Gathering : Wounded;
    }

    static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>Town names as people say them.</summary>
public static class TownNames
{
    /// <summary>A town's name without the "(override ...)" part a taken-over or changed town's record carries ("Bad Teeth (override) takeover").</summary>
    public static string Clean(string record)
    {
        int i = record.IndexOf("(override", StringComparison.OrdinalIgnoreCase);
        return (i >= 0 ? record[..i] : record).Trim();
    }
}
