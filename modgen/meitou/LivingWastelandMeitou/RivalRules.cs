namespace LivingWasteland.MeitouMod;

/// <summary>
/// What a fight's end does to a rival (no engine types, so it is unit tested). Learnt in play: a rival who is knocked down gets back up and
/// fights on, and each time the squad knocks them down again the fight "ends" again; counting every end as a return made the rival "get away
/// again" over and over, pushed their return back each time and made them stronger each time. So:
/// * only a rival who came hunting the squad (Hunting) can lose and get away; a fight while they are still licking their wounds changes nothing;
/// * a rival who beats the squad counts a win, and goes back to recovering if they were hunting (their return is not pushed back otherwise);
/// * one fight counts once: ends within <see cref="SameFight"/> game hours of the last counted one are the same fight.
/// </summary>
public static class RivalRules
{
    public const double SameFight = 12;

    public enum Outcome { None, GotAway, BeatSquad }

    public static Outcome OnFightEnd(bool hunting, bool won, double now, double lastCounted)
    {
        if (now - lastCounted < SameFight) return Outcome.None;
        if (won) return hunting ? Outcome.GotAway : Outcome.None;
        return Outcome.BeatSquad;
    }
}
