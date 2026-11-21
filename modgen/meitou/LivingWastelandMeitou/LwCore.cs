// Living Wasteland, Meitou side: the quest bookkeeping with no engine types in it, so it can be unit-tested without the game.
// LwState (in LwMod.cs) wraps one LwCore per world and adds the engine parts: posting messages, the world registry, the state hash.
namespace LivingWasteland.MeitouMod;

public sealed class LwCore
{
    readonly Dictionary<string, int> values = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> initial;
    readonly object gate = new();

    public LwCore(IReadOnlyDictionary<string, int>? initial = null)
    {
        this.initial = initial is null ? new(StringComparer.Ordinal) : new(initial, StringComparer.Ordinal);
    }

    public int Get(string name)
    {
        lock (gate) return GetUnlocked(name);
    }

    int GetUnlocked(string name) => values.TryGetValue(name, out int v) ? v : initial.GetValueOrDefault(name);

    /// <summary>Sets a variable; returns the value before and after.</summary>
    public (int Before, int After) Set(string name, int value) => Change(name, _ => value);

    /// <summary>Adds to a variable (the delta may be negative); returns the value before and after.</summary>
    public (int Before, int After) Add(string name, int delta) => Change(name, old => old + delta);

    (int, int) Change(string name, Func<int, int> next)
    {
        lock (gate)
        {
            int before = GetUnlocked(name);
            int after = next(before);
            values[name] = after;
            return (before, after);
        }
    }

    /// <summary>The message to post when the open-job counter moves from before to after, or null.</summary>
    public static string? CapMessage(int before, int after, int cap)
    {
        if (before < cap && after >= cap) return $"You carry {cap} unfinished jobs. Nobody will offer you more until you hand one in.";
        if (before >= cap && after < cap) return "You have room for another job.";
        return null;
    }

    /// <summary>A quest variable is open (taken, not paid): contracts run 1 (taken) to 2 (paid), quests 1..n to 99 (paid).</summary>
    public static bool IsOpen(string name, int value)
    {
        if (value <= 0) return false;
        if (name.StartsWith("Contract ", StringComparison.Ordinal)) return value < 2;
        if (name.StartsWith("Quest ", StringComparison.Ordinal)) return value < 99;
        return false;
    }

    /// <summary>"Contract ol_teshin_ro_contract" -> "teshin ro"; "Quest qc_hunt" -> "qc hunt".</summary>
    public static string Pretty(string name)
    {
        string s = name.Replace("Contract ol_", "").Replace("_contract", "").Replace("Quest ", "");
        return s.Replace('_', ' ');
    }

    /// <summary>The open jobs, in a fixed order (by variable name), with their step.</summary>
    public List<(string Name, int Step)> OpenJobs()
    {
        lock (gate)
            return values.Where(p => IsOpen(p.Key, p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
    }

    /// <summary>
    /// Gives up the open job at a 1-based position in <see cref="OpenJobs"/> (or the one whose pretty name contains the text): its variable back to 0
    /// (so it can be offered again) and the open-job counter one lower. Returns the job's pretty name, or null when nothing matched.
    /// </summary>
    public string? GiveUp(string which, string jobVariable)
    {
        var open = OpenJobs();
        (string Name, int Step)? pick = null;
        if (int.TryParse(which.Trim(), out int n) && n >= 1 && n <= open.Count) pick = open[n - 1];
        else if (which.Trim().Length > 0)
            pick = open.Cast<(string Name, int Step)?>().FirstOrDefault(o => Pretty(o!.Value.Name).Contains(which.Trim(), StringComparison.OrdinalIgnoreCase));
        if (pick is not { } p) return null;
        Set(p.Name, 0);
        if (Get(jobVariable) > 0) Add(jobVariable, -1);
        return Pretty(p.Name);
    }

    public string Summary(int cap) => FormatSummary(OpenJobs(), cap);

    public string FullList() => FormatFullList(OpenJobs());

    internal (string Short, string Full) SummaryAndFullList(int cap)
    {
        var open = OpenJobs();
        return (FormatSummary(open, cap), FormatFullList(open));
    }

    static string FormatSummary(List<(string Name, int Step)> open, int cap)
    {
        if (open.Count == 0) return "no open jobs";
        string first = string.Join(", ", open.Take(3).Select((o, i) => $"{i + 1}. {Pretty(o.Name)}"));
        return $"{open.Count} open (limit {cap}): {first}{(open.Count > 3 ? ", ..." : "")} (full list in log.txt)";
    }

    static string FormatFullList(List<(string Name, int Step)> open) =>
        open.Count == 0 ? "none" : string.Join("; ", open.Select((o, i) => $"{i + 1}. {Pretty(o.Name)} step {o.Step}"));

    public void Write(BinaryWriter w)
    {
        lock (gate)
        {
            w.Write(values.Count);
            foreach (var (k, v) in values.OrderBy(p => p.Key, StringComparer.Ordinal)) { w.Write(k); w.Write(v); }
        }
    }

    public void Read(BinaryReader r)
    {
        lock (gate)
        {
            values.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) values[r.ReadString()] = r.ReadInt32();
        }
    }

    // ---- rumours: when did a world state first turn true? Stored as a variable "@seen <state id>" = game hour + 1 (0 = never), so it is
    // saved and hashed with the rest and never counts as a job.

    public static string SeenKey(string stateId) => "@seen " + stateId;

    /// <summary>Records the first time a state was seen true; later calls keep the first time. Returns true when this call recorded it.</summary>
    public bool RecordSeen(string stateId, double gameHours)
    {
        string key = SeenKey(stateId);
        lock (gate)
        {
            if (GetUnlocked(key) != 0) return false;
            values[key] = (int)Math.Floor(gameHours) + 1;
            return true;
        }
    }

    /// <summary>Set once the first rumour poll of a world has run (states true at that poll are old news, not fresh).</summary>
    public const string RumoursStarted = "@rumours started";

    /// <summary>Marks a state that was already true when the mod first looked as old news (stored as -1: "seen, time unknown").</summary>
    public void MarkOld(string stateId)
    {
        string key = SeenKey(stateId);
        lock (gate)
        {
            if (GetUnlocked(key) == 0) values[key] = -1;
        }
    }

    /// <summary>Game days since the state was first seen true; a very large number when it never was (so "under N days" fails and "at least N days" passes,
    /// which is right for a state that was already true before the mod was installed).</summary>
    public double DaysSince(string stateId, double gameHours)
    {
        int seen = Get(SeenKey(stateId));
        return seen <= 0 ? 1e6 : Math.Max(0, (gameHours - (seen - 1)) / 24.0);
    }

    /// <summary>The values in a fixed order, for the world's state hash.</summary>
    public List<(string Name, int Value)> Ordered()
    {
        lock (gate) return values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ToList();
    }
}
