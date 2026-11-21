using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

namespace Recap
{
    /// <summary>A stand-in for Recap 1.2's public call, shaped as its author describes it.</summary>
    public static class RecapApi
    {
        public static readonly List<(string Caption, string Place, Vector3? Position, int Weight, bool Picture, string Kind)> Calls = [];

        public static bool AddMoment(string caption, string renderer) =>
            throw new InvalidOperationException("The compatibility adapter selected an unrelated overload.");

        public static bool AddMoment(string caption, string place, Vector3? position, int weight, bool picture, string kind)
        {
            Calls.Add((caption, place, position, weight, picture, kind));
            return true;
        }
    }
}

public class RecapLinkTests
{
    [Fact]
    public void TitlesReachRecapWithEveryParameterFilled()
    {
        RecapLink.Source = "LivingWasteland";
        var r = new SquadRecord([new TitleRule("hh", "the Hard Hand", "member", "kills", null, null, 1)]);
        r.Place = "Squin";
        r.OnMoment = (caption, weight) => RecapLink.Moment(caption, r.Place, weight);
        r.Kill("Ana", "Dust Bandits", false, null);

        var call = Assert.Single(Recap.RecapApi.Calls, c => c.Caption.Contains("Hard Hand"));
        Assert.Equal(("Ana is now known as the Hard Hand.", "Squin", (Vector3?)null, 70, false, "LivingWasteland"), call);
    }

    [Fact]
    public void SwitchedOffSendsNothing()
    {
        RecapLink.Enabled = () => false;
        try
        {
            Assert.False(RecapLink.Moment("x", "y", 50));
        }
        finally
        {
            RecapLink.Enabled = () => true;
        }
    }
}
