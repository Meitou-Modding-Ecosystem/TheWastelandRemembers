using System.Numerics;
using System.Reflection;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Dialogue;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using Xunit;

public sealed class ExtraExtensionsWorldTests
{
    [Fact]
    public void Native_variables_gate_one_local_thanks_after_verified_help()
    {
        if (typeof(World).Assembly.GetName().Version is not { Major: 0, Minor: >= 24 }) return;
        const string variableId = "twr-prototype-squin-help";
        var variable = W.Rec(variableId, (FcsRecordType)1000, "TWR prototype Squin help");
        variable.Ints["value"] = 0;
        var proof = W.Rec("twr-prototype-help-confirmed", FcsRecordType.DialogueLine, "Help completed");
        proof.References["set variable"] = [new(variableId, 1, 0, 0)];
        var thanks = W.Rec("twr-prototype-help-thanks", FcsRecordType.DialogueLine, "One thanks");
        thanks.Strings["text0"] = "You stood with us. We remember.";
        thanks.References["variable equals"] = [new(variableId, 1, 0, 0)];
        thanks.References["add to variable"] = [new(variableId, 1, 0, 0)];
        var data = W.Data([variable, proof, thanks]);

        var records = new DialogueDatabase(data.Db);
        var proofNode = records.Node(proof.StringId)!;
        var thanksNode = records.Node(thanks.StringId)!;
        Assert.Equal("You stood with us. We remember.", Assert.Single(thanksNode.Texts));
        var dialogue = new DialogueSystem(data);
        using var world = W.World(dialogue);
        var npc = W.Spawn(world, "Resident", W.Bandits, Vector3.Zero);
        var player = W.Spawn(world, "Leaf", W.Player, Vector3.UnitX);
        world.RunTicks(2);

        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var hold = typeof(DialogueSystem).GetMethod("VariablesHold", hidden)!;
        var effects = typeof(DialogueSystem).GetMethod("RunEffects", hidden)!;
        var sceneType = effects.GetParameters()[1].ParameterType;
        var scene = Activator.CreateInstance(sceneType, nonPublic: true)!;
        sceneType.GetField("Npc")!.SetValue(scene, npc);
        sceneType.GetField("Player")!.SetValue(scene, player);
        bool CanThank(World inWorld) => hold.Invoke(dialogue, [inWorld, thanksNode]) is true;
        void Say(DialogueNode line) => effects.Invoke(dialogue, [world, scene, line, npc]);

        Assert.False(CanThank(world));
        Say(proofNode);
        Assert.True(CanThank(world));
        Say(thanksNode);
        Assert.False(CanThank(world));
        using var other = W.World();
        Assert.False(CanThank(other));
    }
}
