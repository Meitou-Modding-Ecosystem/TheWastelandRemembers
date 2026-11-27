// Who may offer the squad paid work (contracts on outlaws, errands): someone with standing in their town. Seen in play (bv): a lone Starving
// Vagrant offered a 4,400-cat contract. Offers are spread over townsfolk by a common personality tag, and a vagrant's character record has no
// faction of its own, so nothing in the dialogue data kept the destitute out. Judged by the speaker's character record name.
namespace LivingWasteland.MeitouMod;

public static class GiverRules
{
    static readonly string[] Destitute = ["Vagrant", "Drifter", "Beggar", "Homeless", "Refugee", "Escapee", "Slave", "Starving", "Hungry"];

    /// <summary>Whether a character of this record (its name, e.g. "Starving Vagrant") may offer the squad paid work. An unknown record
    /// may (the game's own giver rules still apply).</summary>
    public static bool MayOffer(string? recordName) =>
        recordName is null || !Destitute.Any(w => recordName.Contains(w, StringComparison.OrdinalIgnoreCase));
}
