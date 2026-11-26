using Meitou.Game.Ui;
using Meitou.Simulation;

namespace LivingWasteland.MeitouMod;

/// <summary>The simulation verifies the portrait actor. The UI gets only a published, generation-safe handle.</summary>
internal static class AtlasLiveFaces
{
    static readonly System.Reflection.PropertyInfo? portrait = typeof(UiNode).GetProperty("PortraitOf");
    static readonly IReadOnlyDictionary<string, CharacterId> empty = new Dictionary<string, CharacterId>();
    static volatile IReadOnlyDictionary<string, CharacterId> visible = empty;

    internal static bool Available => portrait is { CanWrite: true } && portrait.PropertyType == typeof(CharacterId?);
    internal static bool Enabled => Available && LwMod.Settings.LiveAtlasPortraits;

    internal static CharacterId? Living(World world, CharacterId actor, string? record = null)
    {
        var table = world.Characters;
        return table.TryResolveNext(actor, out int slot) && table.Next[slot] is { Alive: true, Dormant: false } &&
            table.Cold(slot) is { Medical.Dead: false } cold && (record is null || cold.RecordId == record) ? actor : null;
    }

    internal static void Publish(Dictionary<string, CharacterId>? next) => visible = next ?? empty;

    internal static CharacterId? Find(string key) =>
        Enabled && visible.TryGetValue(key, out var actor) ? actor : null;

    internal static void Set(UiNode node, CharacterId actor) => portrait!.SetValue(node, actor);
}
