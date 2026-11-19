// What the F10 rows show, written in the tick. Meitou runs a debug row on the host thread while a tick may be in flight, so it must not
// touch the world (docs/modding.md "Debug rows"); the rows used to build their text from live lists and tables (from a review of the code).
// Each system now writes its line once a second from its own tick, and the row only reads it.
namespace LivingWasteland.MeitouMod;

public sealed class Published
{
    volatile string text = "not ready yet (a moment after loading)";

    public string Text => text;

    /// <summary>Writes the line now (call from the tick); an error is shown instead of the line.</summary>
    public void Set(Func<string> make)
    {
        try { text = make(); }
        catch (Exception e) { text = $"could not be read this time ({e.GetBaseException().Message})"; }
    }

    /// <summary>Once a game second, at a tick offset of the system's own (so the systems don't all write on the same tick).</summary>
    public static bool Due(long tick, int offset) => tick % 30 == offset;
}
