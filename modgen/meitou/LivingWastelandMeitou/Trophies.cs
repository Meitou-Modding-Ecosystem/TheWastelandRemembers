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
            method.Invoke(c, [slot]);
            c.Log($"trophies: neck slot added at {x},{y}");
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
    public static bool DropWarrant(World world, int squadId)
    {
        if (Warrant is null || !LwMod.Settings.Trophies || LwState.For(world).Data is not { } data || world.Squads.Find(squadId) is not { } squad) return false;
        var table = world.Characters;
        foreach (var m in squad.Members)
        {
            if (!table.TryResolveNext(m, out int s)) continue;
            if (data.Items.Create(Warrant) is not { } item) { LwMod.Log($"trophies: no item {Warrant}"); return false; }
            world.GroundItems.Drop(item, table.Next[s].Position, 0);
            return true;
        }
        return false;
    }

    /// <summary>Hands the keepsake of <paramref name="fallen"/> to <paramref name="mourner"/>; dropped at their feet when the bag is full.</summary>
    public static void GiveKeepsake(World world, CharacterId mourner, string mournerName, string fallen, string fallenKey)
    {
        if (Keepsake is null || !LwMod.Settings.Trophies || LwState.For(world).Data is not { } data) return;
        var table = world.Characters;
        if (!table.TryResolveNext(mourner, out int s) || table.Cold(s) is not { Inventory: { } bag }) return;
        // written for the one who fell (Keepsakes.cs): their name and their life with the squad on a keepsake record of its own
        string record = KeepsakeBook.For(world)?.Take(world, fallenKey, mournerName) ?? Keepsake;
        if (data.Items.Create(record) is not { } item) { LwMod.Log($"trophies: no item {record}"); return; }
        if (!bag.Add(item)) world.GroundItems.Drop(item, table.Next[s].Position, 0);
        world.Post($"{mournerName} keeps something of {fallen}'s.");
        if (LwMod.Settings.LogChanges) LwMod.Log($"trophies: {mournerName} keeps a keepsake of {fallen}");
    }
}
