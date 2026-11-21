using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;

namespace LivingWasteland.MeitouMod;

internal sealed partial class HiveEnvoys
{
    internal const int RansomCats = 12000;
    World? ransomWorld;
    object ransomSession = new();

    bool DealsAvailable => LwMod.Settings.PrisonerDeals && PrisonerHandover.Available && PrisonerMenu.Available;

    void BeginRansomWorld(World world)
    {
        if (ReferenceEquals(ransomWorld, world)) return;
        ransomWorld = world;
        ransomSession = new object();
        PrisonerMenu.Ransom([]);
    }

    bool CanContinue(PrisonerCases cases, Visit visit) => cases.QueenEpisode == visit.Episode &&
        cases.RouteOf(PrisonerCases.WesternQueen) is PrisonerCases.Route.Open or PrisonerCases.Route.Ransom;

    void AfterContact(World world, PrisonerCases cases, PlayerTown town, Platoon party, Visit visit)
    {
        if (!DealsAvailable || visit.Price <= 0 || !cases.CanRansom(PrisonerCases.WesternQueen, visit.Episode))
        {
            Finish(world, party, "the enquiry reached the squad");
            return;
        }
        PublishRansom(world, cases, town, party, visit);
    }

    CharacterId RansomReceiver(World world, Platoon party, PlayerTown town, Visit visit)
    {
        var table = world.Characters;
        if (party.State != PlatoonState.Loaded || world.Squads.Find(party.SquadId) is not { } squad || squad.Money < visit.Price ||
            squad.Ai is not { } ai || ai.TraderState is not (1 or 2) || ai.InTown.PlayerTown != town.Number ||
            ai.Destination != ai.InTown) return CharacterId.None;

        foreach (var member in squad.Members)
            if (table.TryResolveNext(member, out int slot) && table.Next[slot].Alive &&
                table.Cold(slot) is { IsPlayer: false, InCombat: false, Animal: null, IsImprisoned: false,
                    Medical: { Dead: false, Incapacitated: false } } cold &&
                cold.Faction == party.Faction && cold.SquadId == party.SquadId && cold.Carrying.IsNone && cold.CarriedBy.IsNone)
                return member;
        return CharacterId.None;
    }

    void PublishRansom(World world, PrisonerCases cases, PlayerTown town, Platoon party, Visit visit)
    {
        PrisonerMenu.Ransom([]);
        if (!cases.TryHeld(world, PrisonerCases.WesternQueen, out var held) || held.Episode != visit.Episode || held.Site.Town != town.Number ||
            RansomReceiver(world, party, town, visit) is not { IsNone: false } trader) return;

        var route = cases.RouteOf(PrisonerCases.WesternQueen);
        if (route == PrisonerCases.Route.Ransom)
        {
            if (!PrisonerHandover.AtExit(world, trader.Slot, held.Cage))
            {
                PrisonerHandover.Approach(world, trader.Slot, held.Cage);
                return;
            }
            PrisonerMenu.Ransom([new(ransomSession, visit.Episode, trader, world.Characters.Cold(trader.Slot)!.Name,
                "Hand over the Queen", PrisonerAction.CompleteRansom)]);
        }
        else
        {
            PrisonerMenu.Ransom([new(ransomSession, visit.Episode, trader, world.Characters.Cold(trader.Slot)!.Name,
                FormattableString.Invariant($"Accept {visit.Price:N0}-cat ransom"), PrisonerAction.AcceptRansom)]);
        }
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        if (!ReferenceEquals(ransomWorld, world) || !Enabled || !DealsAvailable) return;
        foreach (var command in commands)
            if (command is PrisonerChoice { Action: not PrisonerAction.LawfulDelivery } choice && ReferenceEquals(choice.Session, ransomSession))
                ChooseRansom(world, choice);
    }

    void ChooseRansom(World world, PrisonerChoice choice)
    {
        if (active is not { Contacted: true, Price: > 0 } visit || visit.Episode != choice.Episode || world.GameHours >= visit.Until ||
            PrisonerCases.For(world) is not { } cases || !cases.CanRansom(PrisonerCases.WesternQueen, visit.Episode) ||
            !cases.TryHeld(world, PrisonerCases.WesternQueen, out var held) ||
            OutpostMemories.For(world)?.CourierTowns(world).FirstOrDefault(t => t.Key == visit.Outpost).Town is not { } town ||
            town.Number != held.Site.Town || FindParty(world, visit) is not { Dead: false } party ||
            !IsAuthority(visit.Origin, party.Faction) || data.Relations.Get(party.Faction, world.Player.Faction) <= -30 ||
            RansomReceiver(world, party, town, visit) != choice.Target || !world.Characters.TryResolveNext(choice.Target, out int actor) ||
            !PrisonerCases.PlayerNear(world, choice.Party, world.Characters.Next[actor].Position, 250)) return;

        if (choice.Action == PrisonerAction.AcceptRansom)
        {
            if (cases.ReserveRansom(held))
            {
                world.Post("The envoy will meet your squad at the Queen's cage.");
                if (PlaytestLog.Enabled)
                    PlaytestLog.Event(world, "Hive envoy", visit.Tag, "ransom agreed", $"{visit.Price} cats at {held.Cage.Id}");
                LwMod.Log($"prisoner deals: {visit.Price} cats agreed, waiting for handover");
                PublishRansom(world, cases, town, party, visit);
            }
            return;
        }

        if (choice.Action != PrisonerAction.CompleteRansom || !PrisonerHandover.AtExit(world, actor, held.Cage) ||
            !PrisonerCases.PlayerNear(world, choice.Party, PrisonerHandover.Exit(held.Cage), 100) ||
            world.Squads.Find(party.SquadId) is not { } purse || !cases.PayRansom(world, held, purse, visit.Price)) return;
        party.Money = purse.Money;
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "Hive envoy", visit.Tag, "payment settled", $"{visit.Price} cats at {held.Cage.Id}");
        world.Post(FormattableString.Invariant($"The Queen is free. The envoy paid {visit.Price:N0} cats."));
        var home = data.Sites.First(s => NewsSystem.NodeOf(s) == visit.Origin);
        bool escorted = PrisonerEscorts.Begin(world, party, held, data.Factions[party.Faction].Id, home.Position, town: visit.Origin);
        Finish(world, escorted ? null : party, "the ransom was paid and the Queen was handed over");
    }

    static Platoon? FindParty(World world, Visit visit)
    {
        var list = world.Platoons.All.Where(p => p is not null && Pursuit.ReadTag(p) == visit.Tag).Take(2).ToArray();
        return list.Length == 1 ? list[0] : null;
    }
}
