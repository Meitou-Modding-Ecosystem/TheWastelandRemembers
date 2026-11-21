using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed partial class RivalsSystem
{
    const int OffersTag = 0x4C57524F;
    const float OfferRange = 250;  // 25 m
    readonly SortedSet<string> fatalRivals = new(StringComparer.Ordinal);
    readonly HashSet<CharacterId> unkeyedKillers = [];
    object offerWorld = new();
    internal PopulationSystem? Population { get; init; }

    static bool CanRecruitFrom(string faction) => faction is "Dust Bandits" or "Starving Bandits";

    static string PortraitKey(CharacterCold actor) =>
        actor.PortraitSerial is not null ? LwDeeds.Identity(actor) : "";

    static string LastingKey(ulong id) => id == 0 ? "" : FormattableString.Invariant($"lasting:{id}");
    static string OfferKey(Rival rival) => rival.ActorId != 0 && LastingActors.Available ? LastingKey(rival.ActorId) : rival.RecruitPortrait;

    internal void ConfirmedSquadmateKill(CharacterId who, CharacterCold actor)
    {
        if (who.IsNone || !CanRecruitFrom(FactionOf(actor))) return;
        string portrait = PortraitKey(actor), lasting = LastingKey(LastingActors.Of(actor));
        if (portrait.Length > 0) fatalRivals.Add(portrait);
        if (lasting.Length > 0) fatalRivals.Add(lasting);
        if (portrait.Length == 0 && lasting.Length == 0) unkeyedKillers.Add(who);

        foreach (var r in rivals.Where(r => r.OfferUntil > 0 && Barred(r)))
        {
            r.OfferUntil = 0;
            LwMod.Log($"rival offers: {r.Name}'s offer ended after a squadmate died");
        }
    }

    void BindRecruitment(Rival r, CharacterCold actor)
    {
        if (!CanRecruitFrom(r.Faction)) return;
        r.RecruitPortrait = PortraitKey(actor);
        if (!unkeyedKillers.Remove(r.Id)) return;
        if (OfferKey(r) is { Length: > 0 } key) fatalRivals.Add(key);
        if (r.RecruitPortrait.Length > 0) fatalRivals.Add(r.RecruitPortrait);
    }

    bool Barred(Rival r) =>
        unkeyedKillers.Contains(r.Id) || fatalRivals.Contains(r.RecruitPortrait) || fatalRivals.Contains(LastingKey(r.ActorId));

    void RecordDefeat(World world, Rival r)
    {
        if (OfferKey(r).Length == 0 || !CanRecruitFrom(r.Faction) || world.GameHours - r.LastDefeat < RivalRules.SameFight) return;
        r.Defeats++;
        r.LastDefeat = world.GameHours;
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "rival recruitment", OfferKey(r), "defeat verified",
                $"{r.Name}, defeats {r.Defeats}, barred {Barred(r)}");
    }

    bool Eligible(Rival r) => r.Active && r.Phase == Phase.Recovering && OfferKey(r).Length > 0 && r.Defeats >= 3 &&
        !Barred(r) && CanRecruitFrom(r.Faction) &&
        !LeftAlone.Covers(r.OldName, r.Faction) && !LeftAlone.Covers(r.Name, r.Faction) &&
        !ArenaLink.IsTownFighter(r.OldName) && !ArenaLink.IsTownFighter(r.Name);

    static bool CanTalk(CharacterCold? cold) => cold is
        { InCombat: false, IsImprisoned: false, Animal: null, Medical: { Dead: false, Incapacitated: false } } && cold.CarriedBy.IsNone;

    static bool Near(World world, int a, int b) =>
        Vector3.DistanceSquared(world.Characters.Next[a].Position, world.Characters.Next[b].Position) <= OfferRange * OfferRange;

    bool TalkableRival(World world, Rival r, out int slot)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(r.Id, out slot) || !table.Next[slot].Alive || !IsRival(world, r, r.Id) ||
            table.Cold(slot) is not { } cold || !CanTalk(cold) || FactionOf(cold) != r.Faction) return false;
        return Platoon(world, cold) is not { } party || Pursuit.IsUntagged(party) || Pursuit.ReadTag(party) == TagOf(r);
    }

    static bool ReadyPlayer(World world, int player, int rival)
    {
        var table = world.Characters;
        return table.Next[player].Alive && table.Cold(player) is { IsPlayer: true, SquadId: >= 0 } cold &&
            CanTalk(cold) && Near(world, player, rival);
    }

    bool HoldOffer(World world, Rival r, CharacterCold? cold)
    {
        if (!LwMod.Settings.RecruitRivals || !RivalOffers.Available || Population is null || !Eligible(r)) return false;
        if (r.OfferUntil > world.GameHours) return true;
        if (r.OfferUntil > 0) r.Next = Math.Max(r.Next, world.GameHours + 72);
        r.OfferUntil = 0;
        if (r.LastOfferDefeats == r.Defeats || !CanTalk(cold) || !TalkableRival(world, r, out int rival)) return false;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!ReadyPlayer(world, s, rival)) continue;
            r.LastOfferDefeats = r.Defeats;
            r.OfferUntil = world.GameHours + 48;
            world.Bark(r.Id, "Had enough of this. Got room for one more?");
            LwMod.Log($"rival offers: {r.Name} offered to join after {r.Defeats} defeats");
            LogTally.Add("rival offers");
            return true;
        }
        return false;
    }

    void PublishOffers(World world)
    {
        var rows = new Dictionary<CharacterId, RivalOffers.Offer>();
        if (LwMod.Settings.RecruitRivals && LwMod.Settings.Rivals && RivalOffers.Available)
            foreach (var r in rivals)
                if (r.OfferUntil > world.GameHours && Eligible(r) && TalkableRival(world, r, out _))
                    rows[r.Id] = new RivalOffers.Offer(offerWorld, world.Characters.Cold(r.Id.Slot)!.Name, OfferKey(r), r.OfferUntil);
        RivalOffers.Publish(rows, rivals.Count(Barred));
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        if (!ReferenceEquals(world, seen) || !LwMod.Settings.RecruitRivals || !LwMod.Settings.Rivals || !RivalOffers.Available || Population is null) return;
        foreach (var command in commands)
        {
            if (command is not RivalOfferChoice choice || !ReferenceEquals(offerWorld, choice.WorldKey)) continue;
            try { ChooseOffer(world, choice); }
            catch (Exception e) { LwMod.Log($"rival offers: transfer skipped ({e.GetBaseException().Message})"); }
        }
    }

    void ChooseOffer(World world, RivalOfferChoice choice)
    {
        var table = world.Characters;
        var r = rivals.FirstOrDefault(r => r.Id == choice.Rival && OfferKey(r) == choice.Identity && r.OfferUntil == choice.OfferUntil &&
            r.OfferUntil > world.GameHours && Eligible(r));
        if (r is null || !TalkableRival(world, r, out int slot)) return;
        foreach (var selected in choice.Selected)
        {
            if (!table.TryResolveNext(selected, out int actor) || !ReadyPlayer(world, actor, slot)) continue;
            if (!choice.Accept)
            {
                r.OfferUntil = 0;
                r.Next = Math.Max(r.Next, world.GameHours + 72);
                LwMod.Log($"rival offers: {r.Name} was turned away");
                PublishOffers(world);
                return;
            }
            var former = Platoon(world, table.Cold(slot)!);
            if (!RivalOffers.Join(Population, world, r.Id, table.Cold(actor)!.SquadId)) continue;
            if (former is not null && Pursuit.ReadTag(former) == TagOf(r)) Pursuit.Mark(former, "");
            r.OfferUntil = 0;
            r.Phase = Phase.Recruited;
            if (LwDeeds.For(world) is { } deeds)
            {
                deeds.Record.Hours = world.GameHours;
                deeds.Record.Place = deeds.TownAt(table.Next[slot].Position).Town ?? "the wilds";
                deeds.Record.Note($"{r.Name} left their old crew and joined your squad.");
            }
            Words(world);
            world.Post($"{r.Name} has joined your squad.");
            LwMod.Log($"rival offers: {r.Name} joined the squad");
            LogTally.Add("rivals recruited");
            PublishOffers(world);
            return;
        }
    }

    bool HasRecruitmentState => fatalRivals.Count > 0 || rivals.Any(r => r.RecruitPortrait.Length > 0 || r.Defeats > 0);

    void WriteOffers(BinaryWriter w)
    {
        if (!HasRecruitmentState) return;
        w.Write(OffersTag);
        w.Write(fatalRivals.Count);
        foreach (string identity in fatalRivals) w.Write(identity);
        foreach (var r in rivals)
        {
            w.Write(r.RecruitPortrait);
            w.Write(r.Defeats);
            w.Write(r.LastDefeat);
            w.Write(r.LastOfferDefeats);
            w.Write(r.OfferUntil);
        }
    }

    void ReadOffers(BinaryReader r)
    {
        fatalRivals.Clear();
        unkeyedKillers.Clear();
        if (r.BaseStream.Length - r.BaseStream.Position < sizeof(int) || r.ReadInt32() != OffersTag) return;
        for (int n = r.ReadInt32(); n > 0; n--) fatalRivals.Add(r.ReadString());
        foreach (var rival in rivals)
        {
            rival.RecruitPortrait = r.ReadString();
            rival.Defeats = r.ReadInt32();
            rival.LastDefeat = r.ReadDouble();
            rival.LastOfferDefeats = r.ReadInt32();
            rival.OfferUntil = r.ReadDouble();
        }
    }

    void HashOffers(ref StateHasher hash)
    {
        hash.Add(fatalRivals.Count);
        foreach (string identity in fatalRivals) hash.Add(SquadRecord.StableHash(identity));
        hash.Add(unkeyedKillers.Count);
        foreach (var who in unkeyedKillers.OrderBy(id => id.Slot).ThenBy(id => id.Generation))
        {
            hash.Add(who.Slot);
            hash.Add(who.Generation);
        }
        foreach (var r in rivals)
        {
            hash.Add(SquadRecord.StableHash(r.RecruitPortrait));
            hash.Add(r.Defeats);
            hash.Add(r.LastDefeat);
            hash.Add(r.LastOfferDefeats);
            hash.Add(r.OfferUntil);
        }
    }
}
