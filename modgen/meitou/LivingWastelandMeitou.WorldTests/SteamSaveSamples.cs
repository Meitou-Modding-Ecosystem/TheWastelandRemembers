using System.Numerics;
using LivingWasteland.MeitouMod;

static class SteamSaveSamples
{
    internal static readonly string[] Names =
    [
        nameof(AtlasSystem), nameof(Couriers), nameof(HiveEnvoys), nameof(HuntersSystem), nameof(KeepsakeBook),
        nameof(LwDeedsSystem), nameof(LwSystem), nameof(NewsSystem), nameof(OutlawCampsSystem), nameof(OutpostMemories),
        nameof(PrisonerCases), nameof(PrisonerEscorts), nameof(PrisonerRecovery), nameof(RaidsSystem), nameof(RecoveryClues),
        nameof(RivalsSystem), nameof(TheftsSystem),
    ];

    // One nonempty sample of each section as it stood on Steam, with no game data dependencies.
    internal static byte[] For(string name, bool taggedReprisal = false)
    {
        using var bytes = new MemoryStream();
        using var w = new BinaryWriter(bytes);
        switch (name)
        {
            case nameof(AtlasSystem):
                w.Write(2); w.Write(1); w.Write("outlaw"); w.Write(1); w.Write(1);
                w.Write("They headed for Squin."); w.Write("Squin"); w.Write(110d);
                w.Write(1); w.Write("outlaw"); w.Write(1); w.Write("outlaw"); w.Write("Squin"); w.Write(105d);
                break;
            case nameof(Couriers):
                w.Write(1); w.Write(190d); w.Write(true); w.Write(3); w.Write("outpost:house"); w.Write(220d);
                break;
            case nameof(HiveEnvoys):
                w.Write(2); w.Write(4); w.Write(155d); w.Write(true);
                w.Write(3); w.Write("outpost:house"); w.Write("western-hive"); w.Write(220d); w.Write(2200); w.Write(true);
                break;
            case nameof(HuntersSystem):
                w.Write(1); w.Write(1); w.Write("Squin"); w.Write(145d); w.Write(8000f); w.Write(1500f);
                w.Write(1); w.Write("Squin"); w.Write(145d);
                w.Write(0x4C525153); w.Write(1); w.Write("Stack"); w.Write("theft"); w.Write(140d);
                w.Write(0x4C435447); w.Write(true);
                break;
            case nameof(KeepsakeBook):
                w.Write(2); w.Write(1); w.Write(0); w.Write("Kite");
                w.Write("A keepsake from Squin."); w.Write(132d); w.Write("name:Kite");
                break;
            case nameof(LwDeedsSystem):
                var deeds = new LwDeedsSystem([], null).Deeds;
                deeds.Record.Kill("Leaf", "Dust Bandits", true, "Dust King");
                deeds.Record.Died("Kite", 132, "Squin", "Dust King", "Dust Bandits", "king");
                deeds.Heat.AddCrime("Squin", "Shek Kingdom", 27, "town:squin");
                deeds.Record.Write(w);
                deeds.Heat.Write(w);
                w.Write(3); w.Write(1); w.Write("king"); w.Write("Leaf"); w.Write("Dust King"); w.Write("Dust Bandits");
                w.Write(true); w.Write("Dust King"); w.Write("Squin"); w.Write("Shek Kingdom"); w.Write(true);
                w.Write(133d); w.Write(false); w.Write("town:squin");
                w.Write(0x4C4D524E); w.Write(1); w.Write("Kite"); w.Write(132d);
                deeds.Heat.WriteOwners(w);
                break;
            case nameof(LwSystem):
                w.Write(1); w.Write("scout squire"); w.Write(3);
                break;
            case nameof(NewsSystem):
                var board = new NewsBoard();
                board.Accept("town-fell", "Stack", "Shek Kingdom", "They took Stack.", "Dustlife", 80, 150, 650, 350, out _, "town:stack");
                board.Write(w);
                break;
            case nameof(OutlawCampsSystem):
                w.Write(3); w.Write(1); w.Write(7); w.Write(4200f); w.Write(6000f); w.Write(9L); w.Write(2);
                w.Write(1); w.Write(7); w.Write(138d); w.Write(true);
                w.Write(1); w.Write(7); w.Write(148d); w.Write(4160f); w.Write(4900f);
                w.Write(1); w.Write(8); w.Write(110d);
                if (taggedReprisal) { w.Write(0x4C525054); w.Write(true); }
                break;
            case nameof(OutpostMemories):
                w.Write(3); w.Write(true); w.Write(1); w.Write("outpost:house"); w.Write("Dry Well");
                w.Write(121d); w.Write(false); w.Write(true); w.Write(124d);
                w.Write(true); w.Write("Nomads"); w.Write(138d); w.Write(false);
                w.Write(1); w.Write("wall-2");
                break;
            case nameof(PrisonerCases):
                w.Write(3); w.Write(5); w.Write(1); w.Write(4);
                w.Write("chief"); w.Write("Dust King"); w.Write("Dust Bandits"); w.Write("cell");
                w.Write("outpost:house"); w.Write("Dry Well"); w.Write(144d); w.Write("bounty");
                w.Write(true); w.Write((byte)0); w.Write((byte)0); w.Write("Shek Kingdom");
                w.Write(1); w.Write("other-chief");
                break;
            case nameof(PrisonerEscorts):
                w.Write(4); w.Write(1); w.Write("chief"); w.Write(4); w.Write("Dust Bandits");
                w.Write("town:squin"); w.Write("escort-giver"); w.Write(650f); w.Write(150f); w.Write(350f);
                w.Write(190d); Route(w); w.Write(true); w.Write(true);
                break;
            case nameof(PrisonerRecovery):
                w.Write(5); w.Write(1); w.Write("chief"); w.Write(4);
                w.Write(1); w.Write("chief"); w.Write(4); w.Write("scouts"); w.Write("guard");
                w.Write("cell"); w.Write(150d); w.Write(false);
                w.Write(true); w.Write(4); w.Write("chief"); w.Write("Kite"); w.Write("rescuers");
                w.Write("outpost:house"); w.Write(190d); w.Write((byte)2); w.Write(false); Route(w);
                w.Write(2); w.Write(true); w.Write(650f); w.Write(150f); w.Write(350f);
                w.Write("cell"); w.Write(600f); w.Write(150f); w.Write(460f); w.Write(true); w.Write(false);
                break;
            case nameof(RaidsSystem):
                w.Write(3); w.Write(1); w.Write("Sample raid"); w.Write(true);
                w.Write(0); w.Write(143d); w.Write(850f); w.Write(950f); w.Write(-1d);
                w.Write(false); w.Write(false); w.Write(false); w.Write(true);
                break;
            case nameof(RecoveryClues):
                w.Write(1); w.Write(1); w.Write(4); w.Write("cell"); w.Write("house");
                w.Write((byte)0); w.Write(176d); w.Write(true);
                break;
            case nameof(RivalsSystem):
                using (var stream = W.SavedRival("Hollin", "lost an arm", 1, 850, 950, returns: 2))
                    return stream.ToArray();
            case nameof(TheftsSystem):
                w.Write(2); w.Write(1); w.Write("Squin"); w.Write("Shek Kingdom"); w.Write("iron plates");
                w.Write(800d); w.Write(137d); w.Write("town:squin");
                break;
            default:
                throw new ArgumentException($"No rollback sample for {name}.", nameof(name));
        }
        w.Flush();
        return bytes.ToArray();
    }

    static void Route(BinaryWriter w) => RecoveryGate.Crossing.Write(w,
        [new("east-gate", new Vector3(60, 150, 75), new Vector3(100, 150, 75))]);
}
