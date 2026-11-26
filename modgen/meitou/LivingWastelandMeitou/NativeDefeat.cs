using Meitou.Simulation;
using Meitou.Simulation.Dialogue;

namespace LivingWasteland.MeitouMod;

public sealed partial class LwDeedsSystem
{
    static readonly bool nativeDefeat = typeof(World).Assembly.GetType("Meitou.Simulation.Combat.SquadDefeat") is not null;
    const int RecentDefeatTicks = 61 * LookEvery;
    const int DefeatHistoryLimit = 32;
    Dictionary<CharacterId, long>? nativeDefeats;
    bool wipePending;

    // Meitou defers its melee dialogue to the following input phase. Keep evidence for the actual target,
    // including its generation, so a different opponent or a ranged finish still gets the fallback.
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        if (!nativeDefeat || commands.Count == 0 || Talk is null ||
            !(LwMod.Settings.VictoryLines || LwMod.Settings.EnemyVictoryLines)) return;
        var table = world.Characters;
        for (int i = 0; i < commands.Count; i++)
        {
            if (commands[i] is not DialogueEvent { Event: DefeatedSquadEvent, Dialogue: null } line ||
                !table.TryResolveNext(line.Npc, out int a) || table.Cold(a) is not { } striker ||
                !table.TryResolveNext(line.Target, out int b) || table.Cold(b) is not { } victim ||
                striker.IsPlayer == victim.IsPlayer || OtherModsPeople.Runs(world, striker.IsPlayer ? striker : victim))
                continue;

            var seen = nativeDefeats ??= new();
            if (seen.Count >= DefeatHistoryLimit && !seen.ContainsKey(line.Target))
                seen.Remove(seen.MinBy(entry => entry.Value).Key);
            seen[line.Target] = world.Tick;
        }
    }

    bool WasNativeDefeat(World world, CharacterId target) => nativeDefeats is not null &&
        nativeDefeats.TryGetValue(target, out long tick) && world.Tick - tick is >= 0 and <= RecentDefeatTicks;

    void ResetDefeatDialogue()
    {
        nativeDefeats?.Clear();
        wipeTold = wipePending = false;
    }
}
