using System.Numerics;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>The original destination is fixed. Only a local faction witness can establish one follow-up in the same custody episode.</summary>
internal sealed class RecoveryLeads
{
    const double ReportHours = 6;
    sealed record Lead(int Episode, string First, string Reported = "", string Cage = "", double Ready = 0, bool Used = false);
    readonly SortedDictionary<string, Lead> byCaptive = new(StringComparer.Ordinal);

    internal void Dispatched(string record, int episode, string outpost, bool followUp)
    {
        byCaptive[record] = followUp ? byCaptive[record] with { Used = true } : new(episode, outpost);
    }

    internal bool CanFollow(World world, PrisonerCases.Held held, string outpost)
    {
        if (byCaptive.GetValueOrDefault(held.Record) is not { Used: false } lead ||
            lead.Episode != held.Episode) return false;
        if (lead.Reported == outpost && lead.Cage == held.Cage.Id) return world.GameHours >= lead.Ready;
        // A different verified cell, including the original, invalidates the earlier report.
        if (lead.Reported.Length != 0) byCaptive[held.Record] = lead = lead with { Reported = "", Cage = "", Ready = 0 };
        if (outpost == lead.First || !Witness(world, held)) return false;

        byCaptive[held.Record] = lead with { Reported = outpost, Cage = held.Cage.Id, Ready = world.GameHours + ReportHours };
        return false;
    }

    static bool Witness(World world, PrisonerCases.Held held)
    {
        var table = world.Characters;
        var at = held.Cage.Position;
        for (int i = 0; i < table.HighWater; i++)
        {
            var other = table.Next[i];
            if (other is not { Alive: true, Dormant: false } || table.IdOf(i) == held.Actor ||
                table.Cold(i) is not { IsPlayer: false, Animal: null, InCombat: false, IsImprisoned: false, CarriedBy.IsNone: true,
                    Medical: { Dead: false, Incapacitated: false } } witness || witness.Faction != held.Captive.Faction ||
                MathF.Abs(other.Position.Y - at.Y) > 20 || Vector3.DistanceSquared(other.Position, at) > 180 * 180) continue;
            if (PrisonerHandover.ClearSight(world, other.Position, at)) return true;
        }
        return false;
    }

    internal void Write(BinaryWriter w)
    {
        w.Write(byCaptive.Count);
        foreach (var (record, lead) in byCaptive)
        {
            w.Write(record); w.Write(lead.Episode); w.Write(lead.First); w.Write(lead.Reported); w.Write(lead.Cage);
            w.Write(lead.Ready); w.Write(lead.Used);
        }
    }

    internal void Read(BinaryReader? r)
    {
        byCaptive.Clear();
        if (r is null) return;
        for (int n = r.ReadInt32(); n > 0; n--)
            byCaptive[r.ReadString()] = new(r.ReadInt32(), r.ReadString(), r.ReadString(), r.ReadString(), r.ReadDouble(), r.ReadBoolean());
    }

    internal void Hash(ref StateHasher h)
    {
        h.Add(byCaptive.Count);
        foreach (var (record, lead) in byCaptive)
        {
            h.Add(SquadRecord.StableHash(record)); h.Add(lead.Episode); h.Add(SquadRecord.StableHash(lead.First));
            h.Add(SquadRecord.StableHash(lead.Reported)); h.Add(SquadRecord.StableHash(lead.Cage));
            h.Add(lead.Ready); h.Add(lead.Used ? 1 : 0);
        }
    }
}
