using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LivingWasteland.MeitouMod;

/// <summary>One piece of news another mod reported (WastelandApi.Report).</summary>
public sealed class NewsItem
{
    public int Id;
    public string Node = "";
    public string Kind = "", Place = "", Faction = "", Text = "", Source = "";
    public double Hours;
    public float X, Z;
    public int Weight;
    public bool Heard;
    public double HeardHours;
}

/// <summary>
/// News other mods report (issue #23 in the Meitou Worldbuilding repo): a town fell, a siege began, a caravan was lost. Kept for
/// <see cref="KeepDays"/> game days; the same kind at the same place within <see cref="SameNewsHours"/> is one piece of news (so a town that
/// falls is announced once, whoever reports it). Towns talk about the <see cref="Slots"/> weightiest live pieces near where they happened.
/// No engine types: unit tested.
/// </summary>
public sealed class NewsBoard
{
    internal const string LeaderSource = "TWR fallen leaders";
    internal const double LeaderLiveHours = 21 * 24;
    public const int Slots = 3, MaxItems = 40, Format = 2;
    public const double KeepDays = 10, LiveDays = 7, SameNewsHours = 12;

    /// <summary>The kinds with a line of their own when the sender gives no text ({place}, {faction}). Others need a text.</summary>
    public static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["town-fell"] = "{place} has fallen to {faction}.",
        ["siege-began"] = "{faction} have laid siege to {place}.",
        ["siege-broken"] = "The siege of {place} is broken.",
        ["raid-sent"] = "{faction} have sent raiders out from {place}.",
        ["garrison-grew"] = "They're putting more soldiers into {place}.",
        ["town-short"] = "{place} is going hungry.",
        ["caravan-lost"] = "A caravan of {faction} went missing near {place}.",
        ["warlord-fell"] = "A warlord of {faction} is dead, near {place}.",
        ["warlord-rose"] = "A new warlord has risen among {faction}, near {place}.",
    };

    public const string TownSource = "TWR town handovers";
    const int RetainedHandovers = 5;
    readonly List<NewsItem> items = [];
    readonly List<NewsItem> handovers = [];
    int nextId = 1;

    public IReadOnlyList<NewsItem> Items => items;
    internal IReadOnlyList<NewsItem> Handovers => handovers;
    internal int NextId => nextId;

    internal void RememberHandover(NewsItem item)
    {
        if (item.Source != TownSource || item.Node.Length == 0 || handovers.Any(n => n.Id == item.Id)) return;
        var oldest = handovers.Where(n => n.Node == item.Node).OrderBy(n => n.Hours).ThenBy(n => n.Id).ToList();
        handovers.Add(item);
        if (oldest.Count == RetainedHandovers) handovers.Remove(oldest[0]);
    }

    /// <summary>The line towns say: the sender's own text, else the kind's template. Null when there is nothing to say.</summary>
    public static string? Line(string kind, string place, string faction, string text)
    {
        string s = text.Trim();
        if (s.Length == 0)
        {
            if (!Templates.TryGetValue(kind, out var t)) return null;
            if (t.Contains("{faction}") && faction.Trim().Length == 0)
                t = kind == "town-fell" ? "{place} has fallen." : kind == "siege-began" ? "{place} is under siege."
                    : kind == "caravan-lost" ? "A caravan went missing near {place}." : kind == "warlord-fell" ? "A warlord is dead, near {place}."
                    : kind == "warlord-rose" ? "A new warlord has risen near {place}." : t.Replace("{faction} ", "").Replace(" {faction}", "");
            s = t.Replace("{place}", place.Trim()).Replace("{faction}", faction.Trim().Length == 0 ? "" : KeepsakeText.Faction(faction.Trim()));
        }
        if (s.Length == 0) return null;
        if (char.IsLower(s[0])) s = char.ToUpperInvariant(s[0]) + s[1..];
        if (!".!?".Contains(s[^1])) s += ".";
        return s;
    }

    /// <summary>Takes a report. Returns the new item, or null with the reason (no place, nothing to say, already reported).</summary>
    public NewsItem? Accept(string kind, string place, string faction, string text, string source, int weight, double hours, float x, float z,
        out string reason, string node = "")
    {
        kind = (kind ?? "").Trim().ToLowerInvariant();
        place = (place ?? "").Trim();
        faction = (faction ?? "").Trim();
        if (place.Length == 0) { reason = "no place"; return null; }
        if (Line(kind, place, faction, text ?? "") is not { } line) { reason = "no text for this kind"; return null; }
        if (items.Any(i => IsDuplicate(i, kind, place, node, faction, source, hours, line)))
        {
            reason = "already reported";
            return null;
        }
        var item = new NewsItem
        {
            Id = nextId++, Kind = kind, Place = place, Faction = faction, Text = line, Source = (source ?? "").Trim(),
            Weight = Math.Clamp(weight, 0, 100), Hours = hours, X = x, Z = z, Node = node,
        };
        items.Add(item);
        if (items.Count > MaxItems) items.RemoveAt(0);
        reason = "";
        return item;
    }

    static bool IsDuplicate(NewsItem item, string kind, string place, string node, string faction, string source, double hours, string line)
    {
        if (item.Kind != kind) return false;
        if (item.Node.Length > 0 && node.Length > 0 ? item.Node != node
            : !string.Equals(item.Place, place, StringComparison.OrdinalIgnoreCase)) return false;
        if (source == TownSource || item.Source == TownSource)
            return source == item.Source && item.Hours == hours && item.Faction == faction && item.Text == line;
        return Math.Abs(hours - item.Hours) < SameNewsHours;
    }

    /// <summary>Forgets news older than <see cref="KeepDays"/> (and all of it if the clock went back: an earlier save).</summary>
    internal static bool IsLive(NewsItem item, double hours) =>
        hours - item.Hours <= (item.Source == LeaderSource ? LeaderLiveHours : LiveDays * 24) &&
        item.Hours <= hours + 1;

    public void Prune(double hours) => items.RemoveAll(i => hours - i.Hours >
        (i.Source == LeaderSource ? LeaderLiveHours + 48 : KeepDays * 24) || i.Hours > hours + 1);

    /// <summary>What the towns talk about now: up to <see cref="Slots"/> pieces younger than <see cref="LiveDays"/>, weightiest first, then
    /// newest. Slot k is the k-th.</summary>
    public List<NewsItem> Talked(double hours) => Talked(hours, null);

    internal List<NewsItem> Talked(double hours, Func<NewsItem, bool>? enabled) =>
        items.Where(i => (enabled is null || enabled(i)) && IsLive(i, hours))
            .OrderByDescending(i => i.Weight).ThenByDescending(i => i.Hours).ThenBy(i => i.Id).Take(Slots).ToList();

    public void Write(BinaryWriter w)
    {
        w.Write(Format);
        w.Write(nextId);
        w.Write(items.Count);
        foreach (var i in items) WriteItem(w, i);
        w.Write(handovers.Count);
        foreach (var i in handovers) WriteItem(w, i);
    }

    public void Read(BinaryReader r)
    {
        items.Clear();
        handovers.Clear();
        nextId = 1;
        if (r.BaseStream.Position >= r.BaseStream.Length) return;
        int format = r.ReadInt32();
        if (format is not (1 or Format)) return;
        nextId = r.ReadInt32();
        for (int n = r.ReadInt32(); n > 0; n--) items.Add(ReadItem(r, format));
        if (format >= 2)
            for (int n = r.ReadInt32(); n > 0; n--) handovers.Add(ReadItem(r, format));
    }

    static void WriteItem(BinaryWriter w, NewsItem i)
    {
        w.Write(i.Id); w.Write(i.Kind); w.Write(i.Place); w.Write(i.Faction); w.Write(i.Text); w.Write(i.Source);
        w.Write(i.Hours); w.Write(i.X); w.Write(i.Z); w.Write(i.Weight); w.Write(i.Heard); w.Write(i.HeardHours);
        w.Write(i.Node);
    }

    static NewsItem ReadItem(BinaryReader r, int format) => new()
    {
        Id = r.ReadInt32(), Kind = r.ReadString(), Place = r.ReadString(), Faction = r.ReadString(), Text = r.ReadString(), Source = r.ReadString(),
        Hours = r.ReadDouble(), X = r.ReadSingle(), Z = r.ReadSingle(), Weight = r.ReadInt32(),
        Heard = r.ReadBoolean(), HeardHours = r.ReadDouble(),
        Node = format >= 2 ? r.ReadString() : "",
    };
}
