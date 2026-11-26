using System.Reflection;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class ChronicleWorldTests
{
    [Fact]
    public void Chronicle_wraps_the_published_story_and_escapes_text_from_the_save()
    {
        var make = typeof(StatsUi).GetMethod("ChronicleHtml", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(make);
        var before = (RecordHost.Panel, RecordHost.HistoryText, RecordHost.FallenText, RecordHost.AtlasPages);
        try
        {
            RecordHost.Panel = "Ruka & Leaf";
            RecordHost.HistoryText = "Day 2\n<script>alert(1)</script>";
            RecordHost.FallenText = "Hollin";
            RecordHost.AtlasPages = new Dictionary<string, string>
            {
                ["r:Vex"] = "Vex\nThe grudge ended.",
                ["town:Stack"] = "A town outside the story."
            };

            var html = Assert.IsType<string>(make.Invoke(null, null));
            Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
            Assert.Contains("Ruka &amp; Leaf", html);
            Assert.Contains("Hollin", html);
            Assert.Contains("The grudge ended.", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.DoesNotContain("<script>", html);
            Assert.DoesNotContain("A town outside the story.", html);
            Assert.Contains("<h2>History</h2>", html);
            Assert.Contains("<h2>The fallen</h2>", html);
            Assert.Contains("<h2>Rivals</h2>", html);
            Assert.Contains("<h2>Titles</h2>", html);
            Assert.Contains("<h2>The squad</h2>", html);
            Assert.Contains("Ruka & Leaf", StatsUi.StoryText());
            Assert.Contains("<script>", StatsUi.StoryText());
        }
        finally
        {
            (RecordHost.Panel, RecordHost.HistoryText, RecordHost.FallenText, RecordHost.AtlasPages) = before;
        }
    }
}
