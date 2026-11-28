// Who may offer the squad paid work (contracts on outlaws, errands): someone with standing in their town. Seen in play (bv): a lone Starving
// Vagrant offered a 4,400-cat contract. Offers are spread over townsfolk by a common personality tag, and a vagrant's character record has no
// faction of its own, so nothing in the dialogue data kept the destitute out. Judged by the speaker's character record name.
namespace LivingWasteland.MeitouMod;

public static class GiverRules
{
    // Whole words (the census of patch 102: "Slave" inside "Slaver Guard" left the Slave Traders with no givers), and "slave" only as the
    // person's own lot: "Slave Guard", "Slave Master Haga" and "Slave Trader" are slavers, who may well offer work.
    static readonly System.Text.RegularExpressions.Regex Destitute = new(
        @"\b(vagrant|drifter|beggar|homeless|refugee|escapee|starving|hungry)s?\b|\bslaves?\b(?!\s+(guard|hunter|master|mistress|trader|trade|market|patron))",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Whether a character of this record (its name, e.g. "Starving Vagrant") may offer the squad paid work. An unknown record
    /// may (the game's own giver rules still apply).</summary>
    public static bool MayOffer(string? recordName) => recordName is null || !Destitute.IsMatch(recordName);

    static readonly string[] TownRoles = ["Guard", "Sentinel", "Trader", "Merchant", "Shopkeep", "Barman", "Bartender"];
    static readonly string[] RoadRoles = ["Nomad", "Caravan"];

    internal static RumourTopic SpeakerTopic(string? recordName)
    {
        if (recordName is null) return RumourTopic.Any;
        if (RoadRoles.Any(w => recordName.Contains(w, StringComparison.OrdinalIgnoreCase))) return RumourTopic.Road;
        if (TownRoles.Any(w => recordName.Contains(w, StringComparison.OrdinalIgnoreCase))) return RumourTopic.Civic;
        return MayOffer(recordName) ? RumourTopic.Any : RumourTopic.Road;
    }
}
