// Pacing for the talk this mod adds unprompted (the TALK_CODE condition every such line carries): without it, arriving in a town (or
// loading a save) lets every townsperson who sees the squad start a line in the same moment, a wall of bubbles. So: none of our unprompted
// lines in the first 20 seconds after a world loads, then at most one every 3 seconds (any number of checks for the same speaker in the same
// tick still pass: the game weighs several of one speaker's lines at once). Lines our helper fires on purpose (lookouts, criers, mourning,
// bar talk, victory lines) are paced by their own rules and pass: those call sites run inside Exempt.
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class TalkPacer
{
    const long QuietStart = 30 * 20, Gap = 30 * 3;      // ticks
    static World? seen;
    static long firstTick, lastTick = long.MinValue;
    static CharacterId lastSpeaker = CharacterId.None;
    [ThreadStatic] static int exempt;

    /// <summary>Runs <paramref name="fire"/> (one of our own FireEvent calls) without the pacing.</summary>
    public static bool Exempt(Func<bool> fire)
    {
        exempt++;
        try { return fire(); }
        finally { exempt--; }
    }

    public static bool Allow(World world, CharacterId speaker)
    {
        if (exempt > 0) return true;
        long now = world.Tick;
        if (!ReferenceEquals(seen, world)) { seen = world; firstTick = now; lastTick = long.MinValue; lastSpeaker = CharacterId.None; }
        if (now < firstTick) firstTick = now;
        if (now - firstTick < QuietStart) return false;
        if (now == lastTick && speaker == lastSpeaker) return true;
        if (lastTick != long.MinValue && now - lastTick < Gap && now >= lastTick) return false;
        lastTick = now;
        lastSpeaker = speaker;
        return true;
    }
}
