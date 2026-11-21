using System.Numerics;
using System.Reflection;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

internal sealed class Couriers(PopulationData data, PopulationSystem population) : ITickSystem, IModSaveData, IStateHashed
{
    const string Prefix = "lw:courier:";
    const double DispatchInterval = 24, JourneyHours = 72;
    static readonly MethodInfo? spawn = typeof(PopulationSystem).GetMethod("SpawnPlatoon",
        [typeof(World), typeof(int), typeof(string), typeof(int), typeof(Vector2), typeof(float), typeof(float), typeof(long)]);
    static readonly MethodInfo? hold = typeof(PopulationSystem).GetMethod("HoldPlatoon", [typeof(World), typeof(int), typeof(bool)]);
    static readonly MethodInfo? send = typeof(PopulationSystem).GetMethod("SendPlatoon", [typeof(World), typeof(int), typeof(Vector2?)]);
    static readonly ConstructorInfo? visit = typeof(PopulationSystem).Assembly.GetType("Meitou.Simulation.Ai.VisitTown")
        ?.GetConstructor([typeof(int), typeof(int), typeof(float)]);
    static readonly PropertyInfo? tag = typeof(Platoon).GetProperty("ModTag");
    static readonly PropertyInfo? modSpawned = typeof(Platoon).GetProperty("ModSpawned");

    sealed record Trip(int Report, string Town, double Until)
    {
        public string Tag => FormattableString.Invariant($"{Prefix}{Report}");
    }

    Trip? trip;
    double next;
    int orderedSquad = -1, orderedTown;
    List<(string Template, int Faction)>? templates;

    internal static bool Available => spawn is not null && hold is not null && send is not null && visit is not null &&
        tag is { CanWrite: true, PropertyType: var t } && t == typeof(string) &&
        modSpawned is { CanWrite: true, PropertyType: var m } && m == typeof(bool);

    internal static void Register(ModContext c)
    {
        if (!Available) return;
        c.AddSystem("lw-couriers", ctx => ctx.Find<PopulationSystem>() is { } pop ? new Couriers(ctx.Data, pop) : null!, SystemAnchor.End);
    }

    public void SlowWorld(World world)
    {
        if (!Available || world.Tick % 60 != 49) return;
        if (trip is { } active)
        {
            var party = world.Platoons.All.FirstOrDefault(p => p is not null && (string?)tag!.GetValue(p) == active.Tag);
            var town = OutpostMemories.For(world)?.CourierTowns(world).FirstOrDefault(x => x.Key == active.Town).Town;
            var news = NewsSystem.For(world);
            var report = news?.CourierReport(world.GameHours, active.Report);
            if (!LwMod.Settings.OutpostCouriers || party is null || party.Dead || town is null || report is null ||
                data.Relations.Get(party.Faction, town.Owner) <= -30 || world.GameHours >= active.Until)
            {
                End(world, party, town is null ? "the outpost is closed or gone" : "the journey ended before delivery");
                return;
            }
            if (Deliver(world, party, town, news!, report))
            {
                End(world, party, "report delivered");
                return;
            }
            if (!Route(world, party, town)) End(world, party, "the route was refused");
            return;
        }

        if (!LwMod.Settings.OutpostCouriers || !LwMod.Settings.RememberOutposts || world.GameHours < next ||
            NewsSystem.For(world) is not { } board || board.CourierReport(world.GameHours) is not { } candidate ||
            OutpostMemories.For(world) is not { } outposts) return;

        int home = board.CourierOrigin(candidate);
        if (home < 0) return;
        var source = Flat(data.Sites[home].Position);
        var target = outposts.CourierTowns(world).OrderBy(t => Vector2.DistanceSquared(source, Flat(t.Town.Position)))
            .ThenBy(t => t.Key, StringComparer.Ordinal).FirstOrDefault();
        if (target.Town is not { } destination || world.Platoons.All.Any(p => p is not null && !p.Dead &&
            tag!.GetValue(p) is string owner && owner.StartsWith(Prefix, StringComparison.Ordinal))) return;

        next = world.GameHours + DispatchInterval;
        templates ??= SafeTraders(data).Where(t => data.Factions[t.Faction].Name is "Nomads" or "Traders Guild").ToList();
        var (template, faction) = templates.FirstOrDefault(t => data.Relations.Get(t.Faction, world.Player.Faction) > -30);
        if (template is null) return;

        if (Dispatch(world, population, faction, template, home, source) is not { } spawned) return;
        trip = new(candidate.Id, target.Key, world.GameHours + JourneyHours);
        tag!.SetValue(spawned, trip.Tag);
        if (!Route(world, spawned, destination)) { End(world, spawned, "the route was refused"); return; }
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "courier", trip.Tag, "dispatched", $"report {trip.Report}, {candidate.Place} to {trip.Town} ({destination.Name})");
        LwMod.Log($"couriers: a trader is taking news from {candidate.Place} to {destination.Name}");
    }

    internal static Platoon? Dispatch(World world, PopulationSystem population, int faction, string template, int home, Vector2 source, long money = 0)
    {
        if (!Available) return null;
        int id = (int)spawn!.Invoke(population, [world, faction, template, home, source, 900f, 1600f, money])!;
        return id >= 0 ? world.Platoons.Find(id) : null;
    }

    internal static IEnumerable<(string Template, int Faction)> SafeTraders(PopulationData data)
    {
        foreach (var record in data.Db.OfType(FcsRecordType.SquadTemplate).OrderBy(x => x.StringId, StringComparer.Ordinal))
        {
            var squad = SquadTemplate.From(record);
            if (squad.Faction is not { } owner || data.FactionIndex(owner) is not (>= 0 and var index) ||
                squad.Slaves.Count > 0 || squad.Prisoners.Count > 0 || squad.Housemates.Count > 0 ||
                squad.WorldState.Count > 0 || AiCompat.JobsFor(data.Ai, squad).Package?.Signal != 16) continue;
            var members = squad.Squad.Concat(squad.Squad2).Concat(squad.ChooseFrom).Concat(squad.Animals).Concat(squad.Animals2);
            if (squad.Leader is { } leader) members = members.Append(leader);
            if (members.Any(member => UniqueCharacters.IsUnique(data.Db.Find(member.Id)))) continue;
            yield return (record.StringId, index);
        }
    }

    bool Route(World world, Platoon party, PlayerTown town) => Route(world, party, town, ref orderedSquad, ref orderedTown);

    internal static bool Route(World world, Platoon party, PlayerTown town, ref int orderedSquad, ref int orderedTown)
    {
        if (party.State != PlatoonState.Loaded || world.Squads.Find(party.SquadId) is not { } squad)
        {
            orderedSquad = -1;
            return SendHeld(world, party, Flat(town.Position));
        }

        if (hold!.Invoke(null, [world, party.Id, false]) is not true) return false;
        if (orderedSquad == squad.Id && orderedTown == town.Number ||
            squad.Ai is not { CurrentClass: PackageClass.WanderingTrader }) return true;

        var command = (SimCommand)visit!.Invoke([squad.Id, town.Number, 6f]);
        world.Defer(command with { Tick = world.Tick + 1 });
        orderedSquad = squad.Id;
        orderedTown = town.Number;
        return true;
    }

    static bool Deliver(World world, Platoon party, PlayerTown town, NewsSystem news, NewsItem report)
    {
        var trader = MetTrader(world, party, town);
        if (trader.IsNone) return false;
        world.Bark(trader, report.Text);
        return news.DeliverCourierReport(world, report);
    }

    internal static CharacterId MetTrader(World world, Platoon party, PlayerTown town)
    {
        if (party.State != PlatoonState.Loaded || world.Squads.Find(party.SquadId) is not { Ai: { } board } squad ||
            board.TraderState is not (1 or 2) || board.InTown.PlayerTown != town.Number || board.Destination != board.InTown)
            return CharacterId.None;
        foreach (var trader in squad.Members)
            if (OutpostMemories.CanHearCourier(world, town, trader)) return trader;
        return CharacterId.None;
    }

    void End(World world, Platoon? party, string why)
    {
        if (PlaytestLog.Enabled && trip is { } active)
            PlaytestLog.Event(world, "courier", active.Tag, "ended", why);
        if (party is not null) Release(world, party);
        trip = null;
        orderedSquad = -1;
        next = Math.Max(next, world.GameHours + DispatchInterval);
        LwMod.Log($"couriers: {why}");
    }

    internal static bool SendHeld(World world, Platoon party, Vector2 point) =>
        hold!.Invoke(null, [world, party.Id, true]) is true && send!.Invoke(null, [world, party.Id, point]) is true;

    internal static void LeaveVisit(World world, Platoon party, bool clearTraderVisit = true)
    {
        hold!.Invoke(null, [world, party.Id, false]);
        if (clearTraderVisit && party.SquadId >= 0 && world.Squads.Find(party.SquadId)?.Ai is { } board &&
            board.Destination.PlayerTown > 0)
        {
            board.Destination = CampaignTown.None;
            board.TraderState = 0;
        }
    }

    internal static void Release(World world, Platoon party, bool clearTraderVisit = true)
    {
        LeaveVisit(world, party, clearTraderVisit);
        tag!.SetValue(party, "");
        modSpawned!.SetValue(party, false);
    }

    internal static Vector2 Flat(Vector3 at) => new(at.X, at.Z);

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(1);
        w.Write(next);
        w.Write(trip is not null);
        if (trip is not { } active) return;
        w.Write(active.Report); w.Write(active.Town); w.Write(active.Until);
    }

    public void Read(Stream stream)
    {
        next = 0;
        trip = null;
        orderedSquad = -1;
        orderedTown = 0;
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Position == stream.Length || r.ReadInt32() != 1) return;
        next = r.ReadDouble();
        trip = r.ReadBoolean() ? new Trip(r.ReadInt32(), r.ReadString(), r.ReadDouble()) : null;
    }

    public void Hash(ref StateHasher h)
    {
        h.Add(next);
        h.Add(trip is null ? 0 : 1);
        if (trip is not { } active) return;
        h.Add(active.Report); h.Add(SquadRecord.StableHash(active.Town)); h.Add(active.Until);
    }
}
