// The Atlas's pure parts (no engine types, compiled by the tests too): which gossip line a speech bubble is, what the squad has heard about
// whom and where, and the pages the Atlas tab shows. Atlas.cs feeds it from the world.
using System.Text.RegularExpressions;

namespace LivingWasteland.MeitouMod;

public sealed record AtlasPlace(string Region = "", string Known = "", List<string>? Explored = null);

public sealed record AtlasPerson(string? Id = null, string Epithet = "", string? Place = null, string? Region = null, string About = "",
    string? WantedFor = null, bool Hidden = false, string? Vague = null, bool Outlaw = false);

/// <summary>A line heard: what was said, where, when (game hours).</summary>
public sealed record Heard(string Text, string Place, double Hours);

/// <summary>Tells which gossip template a said line is (a template's /TOKEN/ stands for any text), and whom it is about.</summary>
public sealed class AtlasMatcher
{
    readonly Dictionary<string, List<(Regex Re, string Subject)>> byStart = new(StringComparer.Ordinal);
    readonly List<(Regex Re, string Subject)> loose = [];
    const int Key = 10;

    public AtlasMatcher(IEnumerable<(string Subject, string Template)> templates)
    {
        foreach (var (subject, template) in templates)
        {
            var parts = Regex.Split(template, @"/[A-Z][A-Z0-9_]*/");
            var re = new Regex("^" + string.Join("(.+?)", parts.Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);
            if (parts[0].Length >= Key)
            {
                string k = parts[0][..Key];
                if (!byStart.TryGetValue(k, out var list)) byStart[k] = list = [];
                list.Add((re, subject));
            }
            else loose.Add((re, subject));
        }
    }

    /// <summary>The subject ("" for none) of the gossip line <paramref name="said"/>, or null when it is not one of ours.</summary>
    public string? Match(string said)
    {
        said = said.Trim();
        if (said.Length >= Key && byStart.TryGetValue(said[..Key], out var list))
            foreach (var (re, subject) in list)
                if (re.IsMatch(said)) return subject;
        foreach (var (re, subject) in loose)
            if (re.IsMatch(said)) return subject;
        return null;
    }
}

/// <summary>What the squad knows from listening: heard lines by person and by place (the newest few each), and whom it has met.</summary>
public sealed class AtlasBook
{
    public const int Keep = 5;
    readonly SortedDictionary<string, List<Heard>> about = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, int> heardCount = new(StringComparer.Ordinal);
    readonly SortedSet<string> met = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, Heard> seen = new(StringComparer.Ordinal);    // person -> where and when the squad last saw them (Text unused)

    /// <summary>Notes that the squad saw a person at a place (the latest sighting is kept).</summary>
    public void Saw(string person, string place, double hours) => seen[person] = new Heard("", place, hours);
    public Heard? LastSeen(string person) => seen.TryGetValue(person, out var h) ? h : null;

    public IReadOnlyList<Heard> Of(string key) => about.TryGetValue(key, out var l) ? l : [];
    public int CountOf(string key) => heardCount.TryGetValue(key, out int n) ? n : 0;
    public bool Met(string person) => met.Contains(person);
    public bool Meet(string person) => met.Add(person);

    /// <summary>Notes a line under a key (a person's or a place's name); false when it was already the newest there.</summary>
    public bool Note(string key, Heard h)
    {
        if (!about.TryGetValue(key, out var list)) about[key] = list = [];
        if (list.Any(x => x.Text == h.Text)) return false;
        list.Add(h);
        if (list.Count > Keep) list.RemoveAt(0);
        heardCount[key] = CountOf(key) + 1;
        return true;
    }

    /// <summary>How much the squad knows of a person: 0 nothing, 1 a blur (hidden people after one rumour), 2 known, 3 met.</summary>
    public int Level(string name, AtlasPerson p, bool placeKnown)
    {
        if (Met(name)) return 3;
        int n = CountOf(name);
        if (p.Hidden) return n >= 3 ? 2 : n >= 1 ? 1 : 0;             // a hidden boss: a blur after one rumour, known after three
        return n >= 2 || placeKnown ? 2 : 0;                           // anyone else: listed once heard of twice, or once their town is known
    }

    public void Write(BinaryWriter w)
    {
        w.Write(2);
        w.Write(about.Count);
        foreach (var (k, list) in about)
        {
            w.Write(k);
            w.Write(CountOf(k));
            w.Write(list.Count);
            foreach (var h in list) { w.Write(h.Text); w.Write(h.Place); w.Write(h.Hours); }
        }
        w.Write(met.Count);
        foreach (var m in met) w.Write(m);
        w.Write(seen.Count);
        foreach (var (k, h) in seen) { w.Write(k); w.Write(h.Place); w.Write(h.Hours); }
    }

    public void Read(BinaryReader r)
    {
        about.Clear(); heardCount.Clear(); met.Clear(); seen.Clear();
        int format = r.BaseStream.Position >= r.BaseStream.Length ? 0 : r.ReadInt32();
        if (format is not (1 or 2)) return;
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            string k = r.ReadString();
            heardCount[k] = r.ReadInt32();
            var list = about[k] = [];
            for (int i = r.ReadInt32(); i > 0; i--) list.Add(new Heard(r.ReadString(), r.ReadString(), r.ReadDouble()));
        }
        for (int n = r.ReadInt32(); n > 0; n--) met.Add(r.ReadString());
        if (format >= 2) for (int n = r.ReadInt32(); n > 0; n--) { string k = r.ReadString(); seen[k] = new Heard("", r.ReadString(), r.ReadDouble()); }
    }

    public int HashCount => about.Count * 31 + met.Count;

    /// <summary>"at Squin, day 41" for a heard line.</summary>
    public static string When(Heard h) => $"{h.Place}, day {Math.Floor(h.Hours / 24):0}";
}
