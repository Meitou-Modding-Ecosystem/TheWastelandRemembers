// Builds each settings group the way the game's AddSettings does (internal ModSettingsGroup(modId, name, target)); a throw there would
// make Meitou refuse the whole mod (MeitouClient docs/modding-practice.md "Settings and saves").
using System.Reflection;
var ctor = typeof(Meitou.Modding.ModSettingsGroup).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(string), typeof(string), typeof(object)])
    ?? throw new Exception("ModSettingsGroup(string, string, object) not found");
int bad = 0;
foreach (var (id, type) in new[] { ("LivingWastelandMeitou", typeof(LivingWasteland.MeitouMod.LwSettings)),
                                   ("SquadRecord", Type.GetType("LivingWasteland.SquadRecordOnly.SquadRecordSettings, SquadRecord") ?? AppDomain.CurrentDomain.Load("SquadRecord").GetTypes().First(t => t.Name == "SquadRecordSettings")) })
{
    try
    {
        var group = ctor.Invoke([id, id, Activator.CreateInstance(type)!]);
        int n = type.GetProperties().Count(p => p.GetCustomAttributes().Any(a => a.GetType().Name == "SettingAttribute"));
        Console.WriteLine($"{id}: OK ({n} settings)");
    }
    catch (TargetInvocationException e) { bad++; Console.WriteLine($"{id}: THROWS {e.InnerException?.GetType().Name}: {e.InnerException?.Message}"); }
}
return bad;
