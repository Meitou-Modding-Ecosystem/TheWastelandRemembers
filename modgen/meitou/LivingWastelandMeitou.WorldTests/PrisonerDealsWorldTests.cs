using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class PrisonerDealsWorldTests : IDisposable
{
    internal static readonly Vector3 At = new(14000, 150, 14000);
    readonly bool cases = LwMod.Settings.PrisonerCases, deals = LwMod.Settings.PrisonerDeals;

    public void Dispose()
    {
        LwMod.Settings.PrisonerCases = cases;
        LwMod.Settings.PrisonerDeals = deals;
    }

    internal static Fixture Hold(SimWorld world, CharacterId who, string cageId = "cage", int town = 0, Vector3? position = null)
    {
        var at = position ?? At;
        var site = new BuildSite { Id = cageId + "-building", Record = "cell", Owner = W.Player, Position = at,
            Complete = true, Town = town, Materials = [] };
        world.Buildings.Add(site);
        var cage = new Fixture { Id = cageId, Building = site.Id, Kind = FixtureKind.Cage, Position = at, Owner = W.Player,
            Occupant = who, Lock = new LockState { Level = 20, Locked = true } };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((List<Fixture>)typeof(FixtureRegistry).GetField("_all", flags)!.GetValue(world.Fixtures)!).Add(cage);
        ((Dictionary<string, Fixture>)typeof(FixtureRegistry).GetField("_byId", flags)!.GetValue(world.Fixtures)!).Add(cageId, cage);
        var captive = world.Characters.Cold(who.Slot)!;
        captive.InWhat = cage.Id;
        captive.InSomething = HeldState.Imprisoned;
        return cage;
    }

    internal static MemoryStream Save(Meitou.Modding.IModSaveData system)
    {
        var stream = new MemoryStream();
        system.Write(stream);
        stream.Position = 0;
        return stream;
    }

    internal static void ReplayPreviewSave(Meitou.Modding.IModSaveData previous,
        Meitou.Modding.IModSaveData restored, string section)
    {
        using var generated = Save(previous);
        string? folder = Environment.GetEnvironmentVariable("TWR_TEST_PREVIEW_SAVES");
        if (string.IsNullOrWhiteSpace(folder))
        {
            restored.Read(generated);
            return;
        }

        string path = Path.Combine(folder, section + ".bin");
        if (Environment.GetEnvironmentVariable("TWR_TEST_WRITE_PREVIEW_SAVES") == "1")
            File.WriteAllBytes(path, generated.ToArray());
        using var archived = File.OpenRead(path);
        restored.Read(archived);
        Assert.Equal(archived.Length, archived.Position);
    }

    static PopulationData Data()
    {
        var empire = W.Rec("empire", FcsRecordType.Faction, "United Cities");
        empire.Bools["offers bounties"] = true;
        var town = W.Rec("heft", FcsRecordType.Town, "Heft");
        town.References["faction"] = [new FcsReference(empire.StringId, 0, 0, 0)];
        return W.Data([empire, town], [new TownPlacement("heft-placement", town.StringId, At * 2, null)]);
    }

    internal static (CharacterId Player, CharacterId Prisoner) Actors(SimWorld world, string record, int faction = W.Bandits)
    {
        var player = W.Spawn(world, "Leaf", W.Player, At);
        var prisoner = W.Spawn(world, "Prisoner", faction, At);
        world.Uniques.Bind(record, prisoner);
        world.Uniques.Captured(record, true);
        return (player, prisoner);
    }

    [Fact]
    public void Lawful_reservation_validates_the_live_issuer_and_survives_a_reload_without_paying()
    {
        LwMod.Settings.PrisonerCases = LwMod.Settings.PrisonerDeals = true;
        var data = Data();
        int empire = data.FactionIndex("empire");
        var system = new PrisonerCases(data, []);
        var ui = new RecruitmentRegistration();
        PrisonerMenu.Register(ui);
        using var world = W.World(new LwDeedsSystem([], data), system);
        var (player, captive) = Actors(world, PrisonerCases.WesternQueen);
        Hold(world, captive);
        var cold = world.Characters.Cold(captive.Slot)!;
        cold.Bounties = new BountyLedger();
        var warrant = cold.Bounties.Get(empire, "United Cities");
        warrant.Amount = 4000;
        world.RunTicks(150);
        long wallet = world.Player.Money;
        var row = Assert.Single(ui.Verbs!(new RecruitmentMenu(new(captive, cold.Name), [player], world.Tick,
            input => system.Inputs(world, [input]))));
        Assert.Equal("Reserve delivery to United Cities", row.Caption);
        warrant.Amount = 0;
        row.Choose();
        Assert.Equal(PrisonerCases.Route.Open, system.RouteOf(PrisonerCases.WesternQueen));

        warrant.Amount = 4000;
        row.Choose();
        row.Choose();
        Assert.Equal(PrisonerCases.Route.Lawful, system.RouteOf(PrisonerCases.WesternQueen));
        Assert.Equal(wallet, world.Player.Money);
        Assert.True(cold.IsImprisoned);
        Assert.Single(world.TakeMessages(), line => line.Contains("usual bounty desk"));

        using var save = Save(system);
        var reloaded = new PrisonerCases(data, []);
        reloaded.Read(save);
        Assert.Equal(PrisonerCases.Route.Lawful, reloaded.RouteOf(PrisonerCases.WesternQueen));
        warrant.Claimed = true; // same ledger marker the native bounty action persists
        world.RunTicks(150);
        Assert.Equal(PrisonerCases.Route.LawfulPaid, system.RouteOf(PrisonerCases.WesternQueen));
        Assert.True(cold.IsImprisoned); // a receipt alone does not hand the captive over
        Assert.Equal(wallet, world.Player.Money);
    }

    [Fact]
    public void The_native_desk_pays_once_and_its_live_receipt_supersedes_the_prisoner_route()
    {
        if (!PrisonerHandover.Available) return;
        LwMod.Settings.PrisonerCases = LwMod.Settings.PrisonerDeals = true;
        var data = Data();
        int empire = data.FactionIndex("empire");
        var system = new PrisonerCases(data, []);
        using var world = W.World(new LwDeedsSystem([], data), system);
        var (player, captive) = Actors(world, PrisonerCases.WesternQueen);
        Hold(world, captive);
        var actor = world.Characters.Cold(captive.Slot)!;
        actor.Bounties = new BountyLedger();
        actor.Bounties.Get(empire, "United Cities").Amount = 4000;
        world.RunTicks(150);
        Assert.True(system.TryHeld(world, PrisonerCases.WesternQueen, out var held));
        Assert.True(system.ReserveRansom(held));
        Assert.True(PrisonerHandover.Free(world, held));  // stage the living captive for a normal desk visit
        world.Characters.Cold(player.Slot)!.Carrying = captive;
        actor.CarriedBy = player;
        var guard = W.Spawn(world, "Bounty officer", empire, At);
        var arrest = new ArrestSystem(new CrimeSystem(data.Relations), Meitou.Data.Gameplay.GameConstants.Default);
        long purse = world.Player.Money;
        Assert.Equal(4000, arrest.PayBounty(world, guard, player));
        Assert.Equal(0, arrest.PayBounty(world, guard, player));
        world.RunTicks(150);
        Assert.Equal(PrisonerCases.Route.LawfulPaid, system.RouteOf(PrisonerCases.WesternQueen));
        Assert.Equal(purse + 4000, world.Player.Money);
        Assert.False(system.CanRansom(PrisonerCases.WesternQueen, held.Episode));
    }

    [Fact]
    public void A_version_one_case_reopens_with_an_unreserved_route()
    {
        var data = Data();
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1);
            w.Write(2);
            w.Write(1);
            w.Write(1);
            w.Write(PrisonerCases.WesternQueen);
            w.Write("The Queen");
            w.Write("Western Hive");
            w.Write("cage");
            w.Write("town:heft-placement");
            w.Write("Heft");
            w.Write(12d);
            w.Write("");
            w.Write(true);
            w.Write((byte)0);
        }

        var cases = new PrisonerCases(data, []);
        stream.Position = 0;
        cases.Read(stream);
        Assert.Equal(1, cases.QueenEpisode);
        Assert.Equal(PrisonerCases.Route.Open, cases.RouteOf(PrisonerCases.WesternQueen));
    }
}
