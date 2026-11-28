using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;
using Xunit;

[Collection("settings")]
public sealed class GossipSpeakerWorldTests
{
    [Fact]
    public void A_heard_rumour_uses_the_real_speech_bubble_and_ignores_ordinary_talk()
    {
        const string lineText = "The roads are rough.";
        var voice = W.Rec("r-Traveller", FcsRecordType.Character, "Starving Vagrant");
        var pack = W.Rec("pack", (FcsRecordType)73, "Test dialogue");
        voice.References["dialogue package"] = [new(pack.StringId, 0, 0, 0)];
        var effect = W.Rec("report", (FcsRecordType)31, "Record a rumour");
        const int report = 19107;
        effect.Ints["action name"] = report;
        var records = new List<FcsRecord> { voice, pack, effect };
        foreach (int ev in new[] { 72, 12 })
        {
            var root = W.Rec($"root-{ev}", (FcsRecordType)18, "Road talk");
            var line = W.Rec($"line-{ev}", (FcsRecordType)19, "Spoken line");
            root.Bools["monologue"] = true;
            root.References["lines"] = [new(line.StringId, 0, 0, 0)];
            root.Floats["chance temporary"] = line.Floats["chance temporary"] = 100;
            root.Floats["chance permanent"] = line.Floats["chance permanent"] = 100;
            line.Strings["text0"] = lineText;
            if (ev == 12) line.References["effects"] = [new(effect.StringId, 1, 0, 0)];
            if (!pack.References.TryGetValue("dialogs", out var events)) pack.References["dialogs"] = events = [];
            events.Add(new(root.StringId, ev, 0, 0));
            records.Add(root);
            records.Add(line);
        }
        var data = W.Data(records);
        var dialogue = new DialogueSystem(data, new DialogueSettings { AmbientEvents = true });
        dialogue.Extensions = new SimulationExtensions();
        dialogue.Extensions.DialogueEffects.Add(report, GossipLink.Queue);
        var state = new LwSystem(null, dialogue, data);
        var atlas = new AtlasSystem(new AtlasTable(Lines: [[string.Empty, lineText]]), dialogue, data);
        using var world = W.World(dialogue, state, atlas);
        var npc = W.Spawn(world, "Traveller", W.Bandits, Vector3.Zero);
        var witness = W.Spawn(world, "Traveller", W.Bandits, Vector3.UnitZ * 100);
        var player = W.Spawn(world, "Leaf", W.Player, Vector3.UnitX * 100);
        var old = (LwMod.Settings.RumoursFitSpeaker, GossipLink.DeliveryAvailable);
        try
        {
            LwMod.Settings.RumoursFitSpeaker = GossipLink.DeliveryAvailable = true;
            Assert.Equal(RumourTopic.Road, GiverRules.SpeakerTopic(voice.Name));
            Assert.Equal(RumourTopic.Civic, GiverRules.SpeakerTopic("Town Trader"));
            Assert.Equal(RumourTopic.Civic, GiverRules.SpeakerTopic("Gate Guard"));
            Assert.Equal(RumourTopic.Road, GiverRules.SpeakerTopic("Caravan Trader"));
            world.RunTicks(2);
            Assert.True(dialogue.FireEvent(world, npc, player, 72));
            world.RunTicks(12);
            Assert.Contains(dialogue.PublishSpeech(), b => b.Speaker == npc && b.Text == lineText);
            var memory = GossipLink.For(world);
            Assert.Null(memory.LastBy(GossipLink.Key(world, npc), world.GameHours));
            Assert.True(dialogue.FireEvent(world, witness, player, 12));
            world.RunTicks(3);
            Assert.Equal(lineText, memory.LastBy(GossipLink.Key(world, witness), world.GameHours));
            world.Characters.Place(player.Slot, Vector3.UnitX * 1_000, 0);
            world.Defer(new HearRumour(witness, RumourTopic.Road) { Tick = world.Tick + 1 });
            world.RunTicks(3);
            Assert.True(memory.MayTell(GossipLink.Key(world, witness), world.GameHours));
        }
        finally { (LwMod.Settings.RumoursFitSpeaker, GossipLink.DeliveryAvailable) = old; }
    }

    [Fact]
    public void Only_heard_speech_assigns_an_identity_and_the_old_client_still_remembers()
    {
        using var world = W.World();
        var speaker = W.Spawn(world, "Traveller", W.Bandits, Vector3.Zero);
        var actor = world.Characters.Cold(speaker.Slot)!;
        var memory = GossipLink.For(world);
        Assert.True(memory.MayTell(GossipLink.Key(world, speaker), world.GameHours));
        Assert.Equal(0UL, LastingActors.Of(actor));

        const string rumour = "The road's bad.";
        GossipLink.Remember(world, speaker, actor, "Squin", rumour);
        Assert.Equal(LastingActors.Available, LastingActors.Of(actor) != 0UL);
        Assert.Equal(rumour, memory.LastBy(GossipLink.Key(world, speaker), world.GameHours));
    }

    [Fact]
    public void A_returning_speaker_keeps_their_last_rumour_and_quota()
    {
        if (!LastingActors.Available) return;
        using var world = W.World();
        var speaker = W.Spawn(world, "Traveller", W.Bandits, Vector3.Zero);
        var lasting = LastingActors.Grant(world, speaker);
        Assert.NotEqual(0UL, lasting);
        var memory = GossipLink.For(world);
        var key = GossipLink.Key(world, speaker);
        memory.Told(key, "Squin", "The road's bad.", 10);
        memory.Told(key, "Squin", "It’s rough outside.", 11);
        world.Characters.Remove(speaker);

        var stranger = W.Spawn(world, "Traveller", W.Bandits, Vector3.Zero);
        Assert.True(memory.MayTell(GossipLink.Key(world, stranger), 12));
        Assert.Null(memory.LastBy(GossipLink.Key(world, stranger), 12));

        // Rematerialise the engine's lasting identity on a new handle with a changed display name.
        var cold = new CharacterCold { Name = "Traveller One-Arm", Faction = W.Bandits, Medical = new MedicalState([]) };
        var property = typeof(CharacterCold).GetProperty("LastingId")!;
        property.SetValue(cold, Activator.CreateInstance(property.PropertyType, lasting));
        var returning = world.Characters.Spawn(new CharacterHot { Alive = true, Position = Vector3.Zero }, cold, world.Tick);
        Assert.NotEqual(speaker, returning);
        Assert.Equal(returning, LastingActors.Find(world, lasting));
        Assert.False(memory.MayTell(GossipLink.Key(world, returning), 12));
        Assert.Equal("It’s rough outside.", memory.LastBy(GossipLink.Key(world, returning), 12));
    }
}
