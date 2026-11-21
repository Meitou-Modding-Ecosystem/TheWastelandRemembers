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

    readonly Queue<Event> events = new();
    readonly object gate = new();
    long sequence;

    internal Event Add(int? world, long? tick, double? hours, string system, string key, string phase, string detail)
    {
        if (detail.Length > MaxDetail) detail = detail[..MaxDetail] + " [truncated]";
        lock (gate)
        {
            if (events.Count == capacity) events.Dequeue();
            var row = new Event(++sequence, DateTimeOffset.UtcNow, world, tick, hours, system, key, phase, detail);
            events.Enqueue(row);
            return row;
        }
    }

    internal Snapshot Read()
    {
        lock (gate) return new(events.ToArray(), sequence - events.Count);
    }

    internal static string Format(Snapshot snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine(FormattableString.Invariant($"Events retained {snapshot.Events.Length} | Older events discarded {snapshot.Discarded}"));
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
