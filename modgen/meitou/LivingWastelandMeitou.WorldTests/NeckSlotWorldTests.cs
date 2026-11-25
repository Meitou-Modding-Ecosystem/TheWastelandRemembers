using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

// Another mod owns the neck (FCS slot 11): NUE's inventory registers "nue.neck" and hides it. Trophies.AddSlot then adds TWR's neck as a
// second section for slot 11 (Meitou 0.23 ModSlot.Secondary). This checks the engine's side of that: the refusal it matches on, and that
// a secondary neck section is accepted. Older Meitou has no Secondary: nothing to check.
[Collection("settings")]
public sealed class NeckSlotWorldTests
{
    [Fact]
    public void A_neck_slot_already_taken_by_another_mod_still_takes_ours_as_a_second_section()
    {
        var asm = typeof(World).Assembly;
        var slotType = asm.GetType("Meitou.Simulation.Items.ModSlot");
        var slots = asm.GetType("Meitou.Simulation.Items.ModSlots");
        if (slotType?.GetProperty("Secondary") is not { CanWrite: true } secondary || slots is null) return;
        var clear = slots.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var refusal = slots.GetMethod("Refusal")!;
        object Make(string section) => slotType.GetConstructors().Single(c => c.GetParameters().Length == 6)
            .Invoke([section, Trophies.NeckSlot, 2, 2, 336, 224]);
        clear.Invoke(null, null);
        try
        {
            slots.GetMethod("Add")!.Invoke(null, [Make("nue.neck")]);
            var ours = Make(Trophies.Section);
            Assert.Contains("is taken by section", (string)refusal.Invoke(null, [ours])!);
            secondary.SetValue(ours, true);
            Assert.Null(refusal.Invoke(null, [ours]));
        }
        finally { clear.Invoke(null, null); }
    }
}
