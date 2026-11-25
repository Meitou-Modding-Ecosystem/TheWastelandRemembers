// What a returning rival's people can afford to replace a lost limb (Rivals.Mend). A rival's faction decides it: the starving come back on
// a shoddy hiver-made Economy Leg and keep their lost arm lost; the middling (the nations, the ninjas, the hives, the bigger gangs) on a
// standard Economy Leg, with a lost arm restored too; the wealthy (Tech Hunters, traders, nobles, the guilds, slavers) on a high-grade
// Skeleton leg and arm. Any faction not named is poor, as every rival was before. Pure: no engine types, unit tested.
namespace LivingWasteland.MeitouMod;

public static class ProstheticRules
{
    public enum Tier { Poor, Middle, Wealthy }

    static readonly HashSet<string> middle = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shek Kingdom", "Kral's Chosen", "The Holy Nation", "Holy Nation Outlaws", "Inquisition", "United Cities", "Empire Peasants",
        "Western Hive", "Southern Hive", "Hive Diaspora", "Sand Ninjas", "Shinobi Thieves", "Flotsam Ninjas", "Black Dragon Ninjas",
        "Hounds", "Swampers", "Anti-Slavers", "Reavers", "Bloodraiders", "Black Desert Ninjas", "Skin Bandits",
    };

    static readonly HashSet<string> wealthy = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tech Hunters", "Traders Guild", "Trade Ninjas", "Nobles", "Mercenary Guild", "Manhunters", "Slave Traders", "Second Empire",
    };

    /// <summary>What the rival's faction can afford.</summary>
    public static Tier Of(string? faction) =>
        faction is null ? Tier.Poor : wealthy.Contains(faction) ? Tier.Wealthy : middle.Contains(faction) ? Tier.Middle : Tier.Poor;

    // the limb records by limb slot (0 left arm, 1 right arm, 2 left leg, 3 right leg): Newwworld.mod and rebirth.mod
    static readonly string[] economy = ["96134-Newwworld.mod", "96137-Newwworld.mod", "96136-Newwworld.mod", "96135-Newwworld.mod"];
    static readonly string[] skeleton = ["95747-Newwworld.mod", "95748-Newwworld.mod", "95872-rebirth.mod", "96022-rebirth.mod"];

    /// <summary>
    /// The prosthetic a rival of <paramref name="tier"/> is fitted in limb slot <paramref name="slot"/> (0 to 3), with its level (the game's
    /// grades: 20 Shoddy, 40 Standard, 60 High); null where they go without (a poor rival's arm).
    /// </summary>
    public static (string Record, int Level)? Limb(Tier tier, int slot)
    {
        if (slot is < 0 or > 3) return null;
        bool arm = slot <= 1;
        return tier switch
        {
            Tier.Poor => arm ? null : (economy[slot], 20),
            Tier.Middle => (economy[slot], 40),
            _ => (skeleton[slot], 60),
        };
    }

    /// <summary>The words for "Word on the road: X is hunting your squad, ..." once limbs are fitted ("" when none were).</summary>
    public static string Arrival(Tier tier, bool leg, bool arm)
    {
        if (!leg && !arm) return "";
        string kind = tier switch { Tier.Poor => "a shoddy iron", Tier.Middle => "a new iron", _ => "a fine skeleton" };
        return leg && arm ? $", on {kind} leg and with {kind} arm" : leg ? $", on {kind} leg" : $", with {kind} arm";
    }
}
