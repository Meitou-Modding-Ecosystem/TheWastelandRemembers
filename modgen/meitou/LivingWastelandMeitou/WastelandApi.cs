// The Wasteland Remembers' public surface for other Meitou mods. Read it by reflection, so your mod never depends on ours:
//
//   var api = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "LivingWastelandMeitou")
//       ?.GetType("LivingWasteland.MeitouMod.WastelandApi");
//   double heat = (double)(api?.GetMethod("Heat")?.Invoke(null, ["Squin"]) ?? 0.0);
//
// Everything takes and returns plain types (strings, numbers, bools, lists of string-keyed dictionaries). Positions are world units on the
// map (x, z). Call from the game thread (a system tick, a dialogue condition or effect). Nothing here throws: before a game is loaded the
// reads return empty and the writes return false. Version goes up when something is added; nothing is removed or changes meaning.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingWasteland.MeitouMod;

public static class WastelandApi
{
    /// <summary>1: heat (read, add, forgive), rivals, hunts, squad titles, history notes and Recap moments. 2: Report (news).
    /// 3: SendHunters (a crew now), LeaveAlone (another mod's named characters). 4: Outlaws, OutlawAt, HideoutStruck, TownNode, and a Node
    /// column in HeatTowns.</summary>
    public const int Version = 4;

    /// <summary>The mod's build id, e.g. "20261115ai".</summary>
    public static string Build => LwMod.HelperBuild;

    /// <summary>A game is loaded and the mod is running.</summary>
    public static bool Ready => LwDeeds.Latest is not null;

    // ---- town heat: how much a town has it in for the player (0 to 40) ----

    /// <summary>The heat a town feels for the player's squad (0 when unknown or the Town heat setting is off).</summary>
    public static double Heat(string town) => LwMod.Settings.Heat && LwDeeds.Latest is { } d ? d.Heat.Here(town) : 0;

    /// <summary>"quiet", "noticed" (6+), "wanted" (14+) or "hunted" (24+: a town with bounty hunters on may hire a crew).</summary>
    public static string HeatTier(string town) => HeatMap.Tier(Heat(town));

    /// <summary>Every town with heat: Town, Faction, Heat, Tier.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> HeatTowns() =>
        LwMod.Settings.Heat && LwDeeds.Latest is { } d
            ? d.Heat.All().Select(t => Row(("Town", t.Town), ("Faction", t.Faction), ("Heat", Math.Min(HeatMap.Max, t.Crime + t.Blood)),
                ("Tier", HeatMap.Tier(t.Crime + t.Blood)), ("Node", TownNode(t.Town)))).ToList()
            : [];

    /// <summary>
    /// Adds heat in a town (by its name, as the game shows it) for the player's squad, e.g. an unpaid loan or a broken contract. It fades like
    /// crime heat (half in three game days). <paramref name="amount"/> is clamped to 0-40; 24 makes the town "hunted". The reason is written
    /// to the log. False when the town is unknown, nothing is loaded, or the Town heat setting is off.
    /// </summary>
    public static bool AddHeat(string town, double amount, string reason)
    {
        if (!LwMod.Settings.Heat || LwDeeds.Latest is not { Data: { } data } d || string.IsNullOrWhiteSpace(town)) return false;
        int site = -1;
        for (int i = 0; i < data.Sites.Count; i++)
            if (string.Equals(data.Sites[i].Town.Name, town, StringComparison.OrdinalIgnoreCase)) { site = i; break; }
        if (site < 0) return false;
        int f = site < data.SiteFactions.Count ? data.SiteFactions[site] : -1;
        d.Heat.AddCrime(data.Sites[site].Town.Name, d.FactionName(f), Math.Clamp(amount, 0, HeatMap.Max));
        LwMod.Log($"api: heat +{amount:0.#} in {data.Sites[site].Town.Name} ({reason})");
        return true;
    }

    /// <summary>Takes heat off every town of a faction (a pardon, a paid debt). False when nothing is loaded.</summary>
    public static bool Forgive(string faction, double amount, string reason)
    {
        if (LwDeeds.Latest is not { } d) return false;
        d.Heat.Forgive(faction, Math.Max(0, amount));
        LwMod.Log($"api: {faction} forgives {amount:0.#} ({reason})");
        return true;
    }

    /// <summary>
    /// <b>Version 3.</b> A town sends a bounty crew after the squad now, for your mod's reason (an unpaid loan, a broken contract; MeitouMods
    /// #26), whatever its heat. The crew sets out at the next pass (a few game seconds), tracks the squad wherever it is and is beaten once
    /// nobody in it is left standing. With Rivalry Core installed it waits its turn for the one hostile-party slot, a game day at most. The
    /// town's own crews then rest three days as usual. The reason is written to the log. False when the Bounty hunters setting is off, the
    /// town is unknown, nothing is loaded, or that town already has a crew out or asked for.
    /// </summary>
    public static bool SendHunters(string town, string reason)
    {
        if (!LwMod.Settings.BountyHunters || LwDeeds.Latest is not { Data: { } data } || HuntersSystem.Latest is not { } hunters
            || string.IsNullOrWhiteSpace(town)) return false;
        var site = data.Sites.FirstOrDefault(s => string.Equals(s.Town.Name, town.Trim(), StringComparison.OrdinalIgnoreCase));
        if (site is null) return false;
        return hunters.Request(site.Town.Name, string.IsNullOrWhiteSpace(reason) ? "another mod" : reason.Trim());
    }

    /// <summary>
    /// <b>Version 3.</b> Marks a character your mod has named (a war leader, a raid captain) so TWR leaves it alone: never made a rival,
    /// never renamed (MeitouMods #25). By name and faction (as the game shows them), so two mods' identical generated names stay apart; an
    /// empty faction covers the name in any faction. Saved with the game; call it when you name the character. The game's own named
    /// characters are always left alone. False when the name is empty, already marked, or nothing is loaded.
    /// </summary>
    public static bool LeaveAlone(string name, string faction, string source)
    {
        if (RivalsSystem.Latest is not { } rivals || !rivals.LeftAlone.Add(name, faction)) return false;
        LwMod.Log($"api: {source} asked to leave {name}{(string.IsNullOrWhiteSpace(faction) ? "" : $" ({faction})")} alone");
        return true;
    }

    // ---- who is after the player ----

    /// <summary>The rivals the player has been told about: Name, Faction, Phase ("recovering", "hunting", "dead", "jailed", "lost"),
    /// X, Z (last known), Returns (times they came back and got away), Wins (times they beat the squad).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> Rivals() =>
        RivalsSystem.Latest?.Told().Select(r => Row(("Name", r.Name), ("Faction", r.Faction), ("Phase", r.Phase), ("X", r.X), ("Z", r.Z),
            ("Returns", r.Returns), ("Wins", r.Wins))).ToList() ?? [];

    /// <summary>Everyone out hunting the squad now: Kind ("bounty crew", "reprisal", "rival"), Name (the town, outlaw or rival), X, Z.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> Hunts()
    {
        var rows = new List<IReadOnlyDictionary<string, object>>();
        if (LwMod.Settings.BountyHunters)
            rows.AddRange(HuntersSystem.Latest?.Crews.Select(c => Row(("Kind", "bounty crew"), ("Name", c.Town), ("X", c.X), ("Z", c.Z))) ?? []);
        if (LwMod.Settings.OutlawReprisals)
            rows.AddRange(OutlawCampsSystem.Latest?.ReprisalParties().Select(p => Row(("Kind", "reprisal"), ("Name", p.Outlaw), ("X", p.X), ("Z", p.Z))) ?? []);
        if (LwMod.Settings.Rivals)
            rows.AddRange(RivalsSystem.Latest?.Told().Where(r => r.Phase == "hunting").Select(r => Row(("Kind", "rival"), ("Name", r.Name), ("X", r.X), ("Z", r.Z))) ?? []);
        return rows;
    }

    /// <summary>Someone sent by this mod is hunting the squad right now.</summary>
    public static bool HuntUnderway() => Hunts().Count > 0;

    /// <summary>This mod may send hunters after the player (bounty crews, reprisals or rivals are on). A mod with hunters of its own can
    /// hold back while this is true, so the player is not hunted twice over.</summary>
    public static bool SendsHunters => LwMod.Settings.BountyHunters || LwMod.Settings.OutlawReprisals || LwMod.Settings.Rivals;

    // ---- the squad's story ----

    /// <summary>The titles held by the squad as a whole and by any member (ids as in the record, e.g. "narko").</summary>
    public static IReadOnlyList<string> SquadTitles() =>
        LwDeeds.Latest is { Record: var r } ? r.CrewTitles.Concat(r.Members.Values.SelectMany(m => m.Titles)).Distinct().ToList() : [];

    /// <summary>Writes a line to the squad's History (U > History), e.g. "Stack's bank called in the loan." False when nothing is loaded.</summary>
    public static bool Note(string text)
    {
        if (LwDeeds.Latest is not { } d || string.IsNullOrWhiteSpace(text)) return false;
        d.Record.Note(text.Trim());
        return true;
    }

    /// <summary>Sends a moment to BeatTheCount's Recap through this mod's link (weight 0-100, as Recap uses it; place: a town name or
    /// "the wastes"). False when Recap is not installed.</summary>
    public static bool Moment(string caption, string place, int weight) =>
        !string.IsNullOrWhiteSpace(caption) && RecapLink.Moment(caption.Trim(), string.IsNullOrWhiteSpace(place) ? "the wastes" : place, Math.Clamp(weight, 0, 100));

    // ---- news from other mods (version 2) ----

    /// <summary>
    /// Reports something that happened in the world, so the towns talk about it (Meitou Worldbuilding issue #23). <paramref name="kind"/>:
    /// town-fell, siege-began, siege-broken, raid-sent, garrison-grew, town-short, caravan-lost, warlord-fell, warlord-rose (each has a line
    /// of its own when <paramref name="text"/> is empty), or any other kind with a text. <paramref name="place"/>: a town's name as the game
    /// shows it (the talk stays near it). <paramref name="faction"/>: a faction's name, or empty. <paramref name="weight"/>: 0 to 100 (the
    /// weightiest live news is talked about; 60 and up is also posted when the squad hears it). <paramref name="source"/>: your mod's id, for
    /// the log. The same kind at the same place within 12 game hours is taken once, whoever reports it. False when it is not taken (unknown
    /// town, nothing to say, already reported, the setting is off, or no game is loaded); the reason is in the log.
    /// </summary>
    public static bool Report(string kind, string place, string faction, string text, int weight, string source) =>
        NewsSystem.Latest?.Report(kind ?? "", place ?? "", faction ?? "", text ?? "", weight, source ?? "") ?? false;

    // ---- outlaws and places (version 4) ----

    /// <summary>
    /// <b>Version 4.</b> TWR's named outlaws, one row each: <c>Name</c>; <c>Faction</c>; <c>Status</c> ("alive", "succeeded" (dead or jailed,
    /// and one of the gang has taken over), "dead", "jailed", "joined" (the player's now)); <c>Successor</c> (the name now running the gang, or
    /// ""); <c>Mood</c> ("raiding", "lying-low", "gathering", "wounded", or "" when nobody runs the gang); <c>Bounty</c> (cats, grown);
    /// <c>Regions</c> (their home regions' BIOME_GROUP ids, comma-separated); <c>HasCamp</c>, <c>CampX</c>, <c>CampZ</c> (a live camp, with
    /// the "Moving outlaw camps" setting on); <c>Known</c> (the squad has heard of them). Empty before a game loads.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object>> Outlaws() =>
        OutlawCampsSystem.Latest is { } c && c.LastWorld is { } w ? c.ApiRows(w) : [];

    /// <summary><b>Version 4.</b> The outlaw (or successor) whose ground a map point is on or nearest, within <paramref name="within"/>
    /// world units (default 2000, about 200 m); "" when none.</summary>
    public static string OutlawAt(float x, float z, float within = 2000) =>
        OutlawCampsSystem.Latest is { } c && c.LastWorld is { } w ? c.OutlawAt(w, x, z, within) : "";

    /// <summary>
    /// <b>Version 4.</b> Another mod's band struck an outlaw's camp (a garrison, a war band): the outlaw lies low, or licks its wounds when
    /// <paramref name="bloodied"/>, as after the squad's blows, and no reprisal comes after the squad (it wasn't them). <paramref name="by"/>
    /// (who did it) and <paramref name="source"/> (your mod's id) go to the log. False for an unknown or dead outlaw, or before a game loads.
    /// </summary>
    public static bool HideoutStruck(string name, string by, bool bloodied, string source)
    {
        if (OutlawCampsSystem.Latest is not { } c || c.LastWorld is not { } w || !c.StruckByOthers(w, name ?? "", bloodied)) return false;
        LwMod.Log($"api: {source} reports {name}'s camp struck by {by}{(bloodied ? ", bloodied" : "")}");
        return true;
    }

    /// <summary><b>Version 4.</b> A town's stable id, <c>town:&lt;InstanceId&gt;</c> (the key shared frameworks use: it doesn't change when a
    /// town is taken over and renamed), from its name as the game shows it; "" when unknown.</summary>
    public static string TownNode(string town)
    {
        if (LwDeeds.Latest is not { Data: { } data } || string.IsNullOrWhiteSpace(town)) return "";
        foreach (var s in data.Sites)
            if (string.Equals(s.Town.Name, town.Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(s.InstanceId)) return "town:" + s.InstanceId;
        return "";
    }

    static IReadOnlyDictionary<string, object> Row(params (string Key, object Value)[] cells) => cells.ToDictionary(c => c.Key, c => c.Value);
}
