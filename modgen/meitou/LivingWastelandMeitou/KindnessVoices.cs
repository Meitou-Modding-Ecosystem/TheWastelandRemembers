using System.Text.Json;

namespace LivingWasteland.MeitouMod;

internal sealed record KindnessVoices(Dictionary<string, string> Deeds, Dictionary<string, string> Factions,
    string TwiceAsStrong, string LearntTheBlade, string Word, string Gift, Dictionary<string, string>? TownTalk = null)
{
    internal static KindnessVoices? Current { get; set; }

    internal static void Load(string folder, Action<string> log)
    {
        Current = null;
        try
        {
            var path = Path.Combine(folder, "kindness_lines.json");
            if (!File.Exists(path)) return;
            var voices = JsonSerializer.Deserialize<KindnessVoices>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (voices is not { Deeds: { Count: > 0 }, Factions: not null, Word.Length: > 0, Gift.Length: > 0 } ||
                Enum.GetNames<KindnessKind>().Any(kind => string.IsNullOrWhiteSpace(voices.Deeds.GetValueOrDefault(kind))))
                throw new InvalidDataException("A kindness deed needs a line.");
            Current = voices;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            log($"kindness reunion lines: {error.Message}");
        }
    }

    internal string? Gossip(KindnessDeed deed) =>
        TownTalk?.GetValueOrDefault(deed.Kind.ToString())?.Replace("{name}", deed.Name, StringComparison.Ordinal);

    internal string? Line(KindnessDeed deed, string faction, double hours, KindnessSkills now)
    {
        if (NewsAskRules.NoConversationNews(faction) || !Deeds.TryGetValue(deed.Kind.ToString(), out var line)) return null;
        line = line.Replace("{helper}", deed.Helper, StringComparison.Ordinal)
            .Replace("{place}", deed.Place, StringComparison.Ordinal)
            .Replace("{ago}", KindnessRules.HowLongAgo(hours - deed.Hours), StringComparison.Ordinal);
        if (Factions.TryGetValue(faction, out var voice)) line += " " + voice;
        if (deed.Skills.Strength >= 5 && now.Strength >= deed.Skills.Strength * 2) line += " " + TwiceAsStrong;
        else if (deed.Skills.Attack < 15 && now.Attack >= deed.Skills.Attack + 15) line += " " + LearntTheBlade;
        return line;
    }
}
