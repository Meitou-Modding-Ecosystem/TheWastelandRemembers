using System.Collections;
using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class SilentNewsWorldTests
{
    static readonly Vector3 Here = new(10000, 150, 10000);

    [Fact]
    public void Direct_news_questions_leave_silent_factions_silent()
    {
        var data = W.Data([W.Rec("fish", FcsRecordType.Faction, "Fishmen")]);
        using var world = W.World(new NewsSystem(data));
        var local = W.Spawn(world, "Local", W.Bandits, Here);
        var fishman = W.Spawn(world, "Fishman", data.FactionIndex("fish"), Here);
        world.RunTicks(2);

        var news = NewsSystem.For(world)!;
        news.AnswerAsk(world, local);
        news.AnswerAsk(world, fishman);

        var barks = (IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        Assert.True(barks.Contains(local));
        Assert.False(barks.Contains(fishman));
    }

}
