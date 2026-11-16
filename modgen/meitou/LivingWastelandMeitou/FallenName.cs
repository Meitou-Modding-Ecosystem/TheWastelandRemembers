// The /LWFALLEN/ token (content/squad_mood.json): a word swap of ours with one line, "them"; this writes the name of the squadmate who died
// last into that line, so the grief and anger lines say who they mean ("That one was for Beep."). Meitou expands a word swap's line text
// each time a line is said (DialogueSystem.Token), so changing the text is enough. The text's setter is internal to Meitou.Data, so it is set
// by reflection; anything not where we expect it leaves "them" and logs one line.
using System.Reflection;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public static class FallenName
{
    public const string Token = "LWFALLEN";
    const string None = "them";
    static string? written;
    static bool off;

    /// <summary>Writes <paramref name="name"/> (null: nobody has died) into the token's line when it changed.</summary>
    public static void Set(PopulationData data, string? name)
    {
        string text = string.IsNullOrWhiteSpace(name) ? None : name.Trim();
        if (off || text == written) return;
        try
        {
            if (data.Dialogue.WordSwap(Token) is not { } swap || swap.Children.Count == 0)
                throw new InvalidOperationException($"no {Token} word swap");
            var line = swap.Children[0];
            var texts = line.GetType().GetProperty("Texts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMemberException("DialogueNode", "Texts");
            texts.SetValue(line, (IReadOnlyList<string>)[text]);
            written = text;
        }
        catch (Exception e)
        {
            off = true;
            LwMod.Log($"squad mood: could not put the fallen's name in ({e.GetBaseException().Message}); the lines say \"{None}\"");
        }
    }
}
