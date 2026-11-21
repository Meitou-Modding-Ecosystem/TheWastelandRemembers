using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Xunit;

[Collection("settings")]
public sealed class TrophyLooksWorldTests
{
    [Fact]
    public void A_null_neck_fit_does_not_stop_a_valid_fit_from_loading()
    {
        if (!TrophyLooks.Available) return;
        var item = W.Rec("600-LivingWasteland.mod", FcsRecordType.Item, "Kral's Mark");
        item.Ints["slot"] = Trophies.NeckSlot;
        var data = W.Data([item]);
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.Create(Path.Combine(folder.FullName, "mark.mesh")).Dispose();
            var resolve = typeof(TrophyLooks).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic)!;
            var fits = JsonSerializer.Deserialize(
                """[null, {"Item":"Kral's Mark","Mesh":"mark.mesh","Bone":"Bip01 Neck"}]""",
                resolve.GetParameters()[2].ParameterType);
            var resolved = Assert.IsAssignableFrom<IDictionary>(resolve.Invoke(null, [data.Db, folder.FullName, fits]));
            Assert.True(resolved.Contains(item.StringId));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void The_worn_charm_updates_or_clears_only_our_neck_attachment()
    {
        if (!TrophyLooks.Available) return;
        var boneMesh = typeof(CharacterCold).Assembly.GetType("Meitou.Simulation.BoneMesh", true)!;
        object Mesh(string key, string filename) => Activator.CreateInstance(boneMesh, key, filename, "Bip01 Neck", Vector3.Zero, Quaternion.Identity)!;
        var looks = new TrophyLooks(new Dictionary<string, object>
        {
            ["mark"] = Mesh("livingwasteland.neck", "twr_neck_shek.mesh"),
            ["token"] = Mesh("livingwasteland.neck", "twr_neck_holy.mesh")
        });
        bool visible = LwMod.Settings.VisibleNeckCharms, trophies = LwMod.Settings.Trophies;
        try
        {
            LwMod.Settings.VisibleNeckCharms = LwMod.Settings.Trophies = true;
            using var world = W.World(looks);
            var bag = new Inventory();
            var member = W.Spawn(world, "Leaf", W.Player, Vector3.Zero, bag);
            var cold = world.Characters.Cold(member.Slot)!;
            typeof(CharacterCold).GetMethod("SetBoneMesh")!.Invoke(cold, [Mesh("other-mod", "decoration.mesh")]);
            string[] Meshes() => ((IEnumerable)typeof(CharacterCold).GetProperty("BoneMeshes")!.GetValue(cold)!)
                .Cast<object>().Select(m => (string)boneMesh.GetProperty("Mesh")!.GetValue(m)!).Order().ToArray();

            var charm = new ItemInstance { Record = "mark", Name = "Mark", Section = "main" };
            bag.Items.Add(charm);
            world.RunTicks(30);
            Assert.Equal(["decoration.mesh"], Meshes());
            charm.Section = Trophies.Section;
            world.RunTicks(30);
            Assert.Equal(["decoration.mesh", "twr_neck_shek.mesh"], Meshes());
            bag.Items.Clear();
            bag.Items.Add(new ItemInstance { Record = "token", Name = "Token", Section = Trophies.Section });
            world.RunTicks(30);
            Assert.Equal(["decoration.mesh", "twr_neck_holy.mesh"], Meshes());

            LwMod.Settings.VisibleNeckCharms = false;
            world.RunTicks(30);
            Assert.Equal(["decoration.mesh"], Meshes());
        }
        finally { LwMod.Settings.VisibleNeckCharms = visible; LwMod.Settings.Trophies = trophies; }
    }
}
