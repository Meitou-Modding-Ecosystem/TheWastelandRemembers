using System.Globalization;
using System.Text;

namespace LivingWasteland.MeitouMod;

// Process-local diagnostics. No simulation state or save data depends on this.
internal sealed class PlaytestJournal(int capacity = 4096)
{
    const int MaxDetail = 2048;
    internal sealed record Event(long Number, DateTimeOffset Utc, int? World, long? Tick, double? Hours,
        string System, string Key, string Phase, string Detail);
    internal sealed record Snapshot(Event[] Events, long Discarded);

    // Two queues of `capacity` each: the general mod log ("mod log" rows) and everything else (each feature's own rows, marks, settings).
    // One queue let a chatty system push out the whole session: in a play test of bf 36 outlaw-camp lines per refresh left 4,095 of
    // 4,096 rows to the camps and lost every custody, outpost and news row.
    // Keep the first 1/16 of the feature lane for session context. A busy end of a session can no longer
    // replace its world, settings and early events in the next automatic export.
    readonly List<Event> start = [];
    readonly int startCapacity = capacity / 16;
    readonly Queue<Event> events = new(), messages = new();
    readonly object gate = new();
    long sequence;

    internal const string ModLog = "mod log";

    internal Event Add(int? world, long? tick, double? hours, string system, string key, string phase, string detail)
    {
        if (detail.Length > MaxDetail) detail = detail[..MaxDetail] + " [truncated]";
        lock (gate)
        {
            bool general = phase == ModLog && world is null;
            var row = new Event(++sequence, DateTimeOffset.UtcNow, world, tick, hours, system, key, phase, detail);
            if (!general && start.Count < startCapacity) start.Add(row);
            else
            {
                var queue = general ? messages : events;
                int limit = general ? capacity : capacity - startCapacity;
                if (queue.Count == limit) queue.Dequeue();
                queue.Enqueue(row);
            }
            return row;
        }
    }

    internal Snapshot Read()
    {
        lock (gate)
        {
            var all = start.Concat(events).Concat(messages).OrderBy(e => e.Number).ToArray();
            return new(all, sequence - all.Length);
        }
    }

    internal static string Format(Snapshot snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine(FormattableString.Invariant($"Events retained {snapshot.Events.Length} | Events omitted {snapshot.Discarded}"));
        text.AppendLine("Sequence\tUTC\tWorld\tTick\tGame hours\tSystem\tKey\tPhase\tDetails");
        foreach (var row in snapshot.Events) text.AppendLine(FormatRow(row));
        return text.ToString();
    }

    internal static string FormatRow(Event row)
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join('\t', row.Number.ToString(culture), row.Utc.ToString("O", culture), row.World?.ToString(culture) ?? "-",
            row.Tick?.ToString(culture) ?? "-", row.Hours?.ToString("0.0000", culture) ?? "-",
            Field(row.System), Field(row.Key), Field(row.Phase), Field(row.Detail));
    }

    static string Field(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
