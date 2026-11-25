// Trophies and keepsakes (Meitou only; data: content/trophies.json, the outlaw tokens made with each outlaw in content.py, the faction
// charms handed over with a barman's gift in gen_title_temperaments.py). All of them are ITEM records with FCS slot 11 (ATTACH_NECK).
//
// The neck slot: Meitou 0.18 lets a code mod add an equipment slot (ModContext.AddEquipmentSlot(new ModSlot(section, fcsSlot, w, h, x, y)),
// docs/modding.md "Equipment slots"). We build against 0.17.3, which has neither, so the call is made by reflection: on 0.18 the slot is
// there, on 0.17 the items simply stay in the bag (an ITEM with an unknown slot is not wearable there). Its place in the character window is
// a setting (layout pixels from the window's top-left corner), read once at start: the game must restart for a change to show.
//
// The keepsake: when a squadmate dies and the fight is over, the nearest standing squadmate (the one who mourns them, Notoriety.cs) is
// handed a Keepsake of the Fallen, once per death.
using System.Reflection;
using Meitou.Modding;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

public static class Trophies
{
    public const int NeckSlot = 11;           // AttachSlot.ATTACH_NECK
    public const string Section = "lw_neck";

    /// <summary>Adds the neck slot where the engine can (0.18+); logs what happened. Never throws.</summary>
    public static void AddSlot(ModContext c, int x, int y)
    {
        try
        {
            var method = c.GetType().GetMethod("AddEquipmentSlot", BindingFlags.Public | BindingFlags.Instance);
            var slotType = method?.GetParameters() is [{ ParameterType: var t }] ? t : null;
            if (method is null || slotType is null)
            {
                c.Log("trophies: this Meitou has no mod equipment slots (0.18 and later do); trophies stay in the bag");
                return;
            }
            object? slot = null;
            foreach (var ctor in slotType.GetConstructors())
            {
                var ps = ctor.GetParameters();
                if (ps.Length == 6 && ps[0].ParameterType == typeof(string) && ps.Skip(1).All(p => p.ParameterType == typeof(int)))
                    slot = ctor.Invoke([Section, NeckSlot, 2, 2, x, y]);
            }
            if (slot is null) { c.Log("trophies: the engine's slot type changed; no neck slot"); return; }
            try
            {
                method.Invoke(c, [slot]);
                c.Log($"trophies: neck slot added at {x},{y}");
            }
            catch (TargetInvocationException e) when (e.InnerException is ArgumentException taken
                && taken.Message.Contains("is taken by section", StringComparison.Ordinal)
                && slotType.GetProperty("Secondary") is { CanWrite: true } secondary)
            {
                // another mod already owns the neck slot (NUE's inventory registers "nue.neck" and hides it): ours becomes a second neck
                // section (Meitou 0.23 ModSlot.Secondary), still shown at our place, that takes trophies when they're dragged into it
                secondary.SetValue(slot, true);
                method.Invoke(c, [slot]);
                c.Log($"trophies: another mod has the neck slot ({taken.Message}); ours added as a second neck section at {x},{y}");
            }
        }
        catch (Exception e)
        {
            c.Log($"trophies: no neck slot ({e.GetBaseException().Message})");
        }
    }

    /// <summary>The keepsake item's record id (lw_codes.json "trophies"), null when the build has none.</summary>
    public static string? Keepsake;

    /// <summary>The warrant item a beaten bounty hunters' crew drops (lw_codes.json "trophies"), null when the build has none.</summary>
    public static string? Warrant;

    /// <summary>Drops the warrant where one of a beaten crew lies, for the squad to pick up (a trophy of the fight).</summary>
    public static bool DropWarrant(World world, int squadId, string town)
    {
        if (Warrant is null || !LwMod.Settings.Trophies || LwState.For(world).Data is not { } data || world.Squads.Find(squadId) is not { } squad) return false;
        var table = world.Characters;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s)) continue;
            if (data.Items.Create(Warrant) is not { } item) { LwMod.Log($"trophies: no item {Warrant}"); return false; }
            ItemUses.SetTag(item, town);                     // which town sent them, read on a right click (0.20.2+)
            world.GroundItems.Drop(item, table.Next[s].Position, 0);
            return true;
        }
        return false;
    }

    /// <summary>Hands the keepsake of <paramref name="fallen"/> to <paramref name="mourner"/>; dropped at their feet when the bag is full.</summary>
    public static void GiveKeepsake(World world, CharacterId mourner, string mournerName, string fallen, string fallenKey)
    {
        // its own errors stay here: one used to stop the whole squad record for the session (the record's look calls this)
        try { Give(world, mourner, mournerName, fallen, fallenKey); }
        catch (Exception e) { LwMod.Log($"trophies: no keepsake of {fallen} ({e.GetBaseException().Message})"); }
    }

    static void Give(World world, CharacterId mourner, string mournerName, string fallen, string fallenKey)
    {
        if (Keepsake is null || !LwMod.Settings.Trophies || LwState.For(world).Data is not { } data) return;
        var table = world.Characters;
        if (!table.TryResolveNext(mourner, out int s) || table.Cold(s) is not { Inventory: { } bag }) return;
        // written for the one who fell (Keepsakes.cs): their name and their life with the squad on a keepsake record of its own
        string? tag = null;
        string record = KeepsakeBook.For(world)?.Take(world, fallenKey, mournerName, out tag) ?? Keepsake;
        if (data.Items.Create(record) is not { } item) { LwMod.Log($"trophies: no item {record}"); return; }
        if (tag is not null) ItemUses.SetTag(item, tag);
        if (!InventoryCompat.Add(bag, item)) world.GroundItems.Drop(item, table.Next[s].Position, 0);
        world.Post($"{mournerName} keeps something of {fallen}'s.");
        if (LwMod.Settings.LogChanges) LwMod.Log($"trophies: {mournerName} keeps a keepsake of {fallen}");
    }
}

/// <summary>
/// Inventory.Add across Meitou versions. 0.21 gave it a second, optional parameter (<c>Add(ItemInstance, bool mainShut = false)</c>); an
/// optional parameter is filled in at build time, so this DLL (built against 0.17.3) asked 0.21 for the old one-parameter method and got a
/// MissingMethodException (seen in play: a squadmate died, no keepsake, and the squad record stopped for the session). Found by
/// reflection once, with every parameter after the item given its default.
/// </summary>
public static class InventoryCompat
{
    static readonly System.Reflection.MethodInfo? add = typeof(Meitou.Simulation.Items.Inventory).GetMethods()
        .Where(m => m.Name == "Add" && m.ReturnType == typeof(bool) && m.GetParameters() is [var first, ..] rest
            && first.ParameterType == typeof(Meitou.Simulation.Items.ItemInstance) && rest.Skip(1).All(p => p.HasDefaultValue))
        .OrderBy(m => m.GetParameters().Length).FirstOrDefault();

    public static bool Add(Meitou.Simulation.Items.Inventory bag, Meitou.Simulation.Items.ItemInstance item)
    {
        if (add is null) return false;
        var ps = add.GetParameters();
        var args = new object?[ps.Length];
        args[0] = item;
        for (int i = 1; i < ps.Length; i++) args[i] = ps[i].DefaultValue;
        return add.Invoke(bag, args) is true;
    }
}
