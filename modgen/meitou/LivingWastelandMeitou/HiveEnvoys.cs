using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

/// <summary>One neutral Western Hive contact for a verified queen custody episode.</summary>
internal sealed partial class HiveEnvoys(PopulationData data, PopulationSystem population) : ITickSystem, IModSaveData, IStateHashed
{
    const string Prefix = "lw:hive-envoy:";
    const double JourneyHours = 72, RetryHours = 12;
    sealed record Visit(int Episode, string Outpost, string Origin, double Until, int Price = 0, bool Contacted = false)
    {
        internal string Tag => FormattableString.Invariant($"{Prefix}{Episode}");
    }

    static string shown = "no enquiry";
    Visit? active;
    int dispatched, orderedSquad = -1, orderedTown = 0;
    double retry;
    List<(string Template, int Faction)>? templates;

    internal static void Register(ModContext context)
    {
        if (!Couriers.Available) return;
        context.AddSystem("lw-hive-envoys", ctx => ctx.Find<PopulationSystem>() is { } pop ? new HiveEnvoys(ctx.Data, pop) : null!, SystemAnchor.End);
        context.AddDebugCommand("Wasteland Remembers: Hive envoy", _ => LwMod.Settings.HiveEnvoys ? Volatile.Read(ref shown) : "off", hasField: false);
    }

    public void SlowWorld(World world)
    {
        BeginRansomWorld(world);
        if (!Couriers.Available || world.Tick % 60 != 53) return;
        if (active is { } visit)
        {
            var parties = world.Platoons.All.Where(p => p is not null && Pursuit.ReadTag(p) == visit.Tag).Take(2).ToArray();
            if (parties.Length == 2)
            {
                Volatile.Write(ref shown, "ambiguous envoy party");
                return;
            }

            var party = parties.SingleOrDefault();
            if (!Enabled || party is null || party.Dead || world.GameHours >= visit.Until ||
                !IsAuthority(visit.Origin, party.Faction) || data.Relations.Get(party.Faction, world.Player.Faction) <= -30)
            {
                Finish(world, party, "the enquiry was withdrawn");
                return;
            }
            // The two saved systems rebind during their own serial passes. Wait for them after a load.
            if (PrisonerCases.For(world) is not { } cases || OutpostMemories.For(world) is not { } knownOutposts) return;
            var town = knownOutposts.CourierTowns(world).FirstOrDefault(t => t.Key == visit.Outpost).Town;
            if (town is null || !CanContinue(cases, visit))
            {
                Finish(world, party, "the captive or outpost is no longer available");
                return;
            }

            if (visit.Contacted)
            {
                AfterContact(world, cases, town, party, visit);
                return;
            }
            if (cases.QueenAt(world, out _, out int cageTown))
            {
                if (cageTown != town.Number)
                {
                    Finish(world, party, "the captive was moved to another outpost");
                    return;
                }
                var trader = Couriers.MetTrader(world, party, town);
                if (!trader.IsNone)
                {
                    world.Bark(trader, "The Queen. Is she alive? We need to hear it.");
                    string line = "A Western Hive envoy came to ask about the Queen.";
                    LwDeeds.For(world)?.Record.Note(line, world.GameHours, town.Name);
                    active = visit with { Contacted = true };
                    PlaytestLog.Event(world, "Hive envoy", visit.Tag, "contacted", town.Name);
                    AfterContact(world, cases, town, party, active);
                    return;
                }
            }
            if (!Couriers.Route(world, party, town, ref orderedSquad, ref orderedTown))
                Finish(world, party, "the route was refused");
            return;
        }

        if (!Enabled || world.GameHours < retry || world.Player.Faction < 0 || PrisonerCases.For(world) is not { } prisoners) return;
        // the F10 row says what holds the envoy back (play test of bg: "no enquiry" for hours, with no way to tell why)
        if (!prisoners.QueenAt(world, out int episode, out int number)) { Wait("no enquiry: the Queen is not in one of your cages"); return; }
        if (episode <= dispatched) { Wait("no enquiry: an envoy already came for this capture"); return; }
        if (prisoners.RouteOf(PrisonerCases.WesternQueen) != PrisonerCases.Route.Open) { Wait("no enquiry: her case is already settled"); return; }
        if (OutpostMemories.For(world)?.CourierTowns(world).FirstOrDefault(t => t.Town.Number == number) is not { Town: { } destination } outpost)
        {
            Wait("no enquiry: her cage is not in a recorded outpost that is open to the public");
            return;
        }

        retry = world.GameHours + RetryHours;
        templates ??= Couriers.SafeTraders(data).Where(t => data.Factions[t.Faction].Name == "Western Hive").ToList();
        var (template, faction) = templates.FirstOrDefault(t => data.Relations.Get(t.Faction, world.Player.Faction) > -30);
        if (template is null)
        {
            Wait(templates.Count == 0 ? "no enquiry: no Western Hive trader to send" : "no enquiry: the Western Hive is hostile to you");
            return;
        }
        if (world.Platoons.All.Any(p => p is not null && !p.Dead && Pursuit.ReadTag(p)?.StartsWith(Prefix, StringComparison.Ordinal) == true))
        {
            Wait("no enquiry: an envoy party is still out");
            return;
        }

        var home = Enumerable.Range(0, Math.Min(data.Sites.Count, data.SiteFactions.Count))
            .Where(i => data.SiteFactions[i] == faction && NewsSystem.NodeOf(data.Sites[i]).Length > 0)
            .OrderBy(i => Vector2.DistanceSquared(Couriers.Flat(data.Sites[i].Position), Couriers.Flat(destination.Position)))
            .ThenBy(i => data.Sites[i].InstanceId, StringComparer.Ordinal).DefaultIfEmpty(-1).First();
        int price = DealsAvailable && prisoners.CanRansom(PrisonerCases.WesternQueen, episode) ? RansomCats : 0;
        if (home < 0 || Couriers.Dispatch(world, population, faction, template, home, Couriers.Flat(data.Sites[home].Position), price) is not { } spawned)
        {
            Wait(home < 0 ? "no enquiry: no Western Hive home to send from" : "no enquiry: the envoy party could not be made (next try in 12 hours)");
            return;
        }

        active = new Visit(episode, outpost.Key, NewsSystem.NodeOf(data.Sites[home]), world.GameHours + JourneyHours, price);
        dispatched = episode;
        Pursuit.Mark(spawned, active.Tag);
        if (!Couriers.Route(world, spawned, destination, ref orderedSquad, ref orderedTown))
        {
            Finish(world, spawned, "the route was refused");
            return;
        }
        Volatile.Write(ref shown, $"envoy travelling to {destination.Name}");
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "Hive envoy", active.Tag, "dispatched",
                $"from {active.Origin} to {active.Outpost}, quote {active.Price}");
        LwMod.Log($"envoys: a Western Hive caravan set out for {destination.Name}");
    }

    internal void Recall(World world, int episode)
    {
        if (active is not { } visit || visit.Episode != episode) return;
        var matches = world.Platoons.All.Where(p => p is not null && Pursuit.ReadTag(p) == visit.Tag).Take(2).ToArray();
        Finish(world, matches.Length == 1 ? matches[0] : null, "the envoy withdrew after a lawful claim");
    }

    static bool Enabled => LwMod.Settings.PrisonerCases && LwMod.Settings.HiveEnvoys && LwMod.Settings.RememberOutposts;

    bool IsAuthority(string node, int faction)
    {
        if (faction < 0 || faction >= data.Factions.Count || data.Factions[faction].Name != "Western Hive") return false;
        for (int i = 0; i < data.Sites.Count && i < data.SiteFactions.Count; i++)
            if (data.SiteFactions[i] == faction && NewsSystem.NodeOf(data.Sites[i]) == node) return true;
        return false;
    }

    void Finish(World world, Platoon? party, string why)
    {
        if (party is not null) Couriers.Release(world, party);
        // An envoy can withdraw before the restored ledger has registered its first custody sweep.
        if (active is { } visit)
        {
            (PrisonerCases.For(world) ?? world.Systems.OfType<PrisonerCases>().FirstOrDefault())?.CancelRansom(visit.Episode);
            PlaytestLog.Event(world, "Hive envoy", visit.Tag, "ended", why);
        }
        active = null;
        PrisonerMenu.Ransom([]);
        orderedSquad = -1;
        Volatile.Write(ref shown, why);
        LwMod.Log("envoys: " + why);
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(2);
        w.Write(dispatched);
        w.Write(retry);
        w.Write(active is not null);
        if (active is not { } trip) return;
        w.Write(trip.Episode);
        w.Write(trip.Outpost);
        w.Write(trip.Origin);
        w.Write(trip.Until);
        w.Write(trip.Price);
        w.Write(trip.Contacted);
    }

    public void Read(Stream stream)
    {
        active = null;
        dispatched = 0;
        retry = 0;
        orderedSquad = -1;
        orderedTown = 0;
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        ransomWorld = null;
        if (stream.Position == stream.Length) return;
        int format = r.ReadInt32();
        if (format is not (1 or 2)) return;
        dispatched = r.ReadInt32();
        retry = r.ReadDouble();
        if (!r.ReadBoolean()) return;
        active = new Visit(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadDouble());
        if (format >= 2) active = active with { Price = r.ReadInt32(), Contacted = r.ReadBoolean() };
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(dispatched);
        h.Add(retry);
        h.Add(active is null ? 0 : 1);
        if (active is not { } trip) return;
        h.Add(trip.Episode);
        h.Add(SquadRecord.StableHash(trip.Outpost));
        h.Add(SquadRecord.StableHash(trip.Origin));
        h.Add(trip.Until);
        h.Add(trip.Price);
        h.Add(trip.Contacted ? 1 : 0);
    }

    void Wait(string why)
    {
        if (Volatile.Read(ref shown) == why) return;
        Volatile.Write(ref shown, why);
        LwMod.Log($"Hive envoy: {why}");
    }
}
