using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

internal static class StorySounds
{
    static readonly Action<World, string, string?, string?>? post = Bind();

    internal static bool Available => post is not null;

    static Action<World, string, string?, string?>? Bind()
    {
        var method = typeof(World).GetMethod("PostInterfaceSound", [typeof(string), typeof(string), typeof(string)]);
        return method is { IsStatic: false } && method.ReturnType == typeof(void)
            ? method.CreateDelegate<Action<World, string, string?, string?>>()
            : null;
    }

    internal static void Lookout(World world) => Cue(world, "Under_Attack");

    internal static void Rival(World world) => Cue(world, "Raid");

    static void Cue(World world, string notification)
    {
        if (LwMod.Settings.StoryAlertSounds)
            post?.Invoke(world, "Notifications", "Notifications", notification);
    }
}
