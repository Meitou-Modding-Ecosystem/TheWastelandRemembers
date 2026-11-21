using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.World;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class NewsHashWorldTests
{
    [Fact]
    public void News_hash_changes_with_report_origin_or_priority_and_is_stable_across_reload()
    {
        ulong Signature(string node, string place, float x, int weight)
        {
            var board = new NewsBoard();
            Assert.NotNull(board.Accept("town-fell", place, "Shek Kingdom", "", "test", weight, 24, x, 0, out _, node));
            using var saved = new MemoryStream();
            using var w = new BinaryWriter(saved);
            board.Write(w);
            saved.Position = 0;
            var news = new NewsSystem(null);
            news.Read(saved);
            var hash = new StateHasher();
            news.Hash(ref hash);
            saved.SetLength(0);
            news.Write(saved);
            saved.Position = 0;
            var restored = new NewsSystem(null);
            restored.Read(saved);
            var restoredHash = new StateHasher();
            restored.Hash(ref restoredHash);
            Assert.Equal(hash.Value, restoredHash.Value);
            return hash.Value;
        }

        ulong original = Signature("town:stack", "Stack", 10, 65);
        Assert.NotEqual(original, Signature("town:bad-teeth", "Bad Teeth", 700, 65));
        Assert.NotEqual(original, Signature("town:second-stack", "Stack", 10, 65));
        Assert.NotEqual(original, Signature("town:stack", "Stack", 10, 70));
    }

    [Fact]
    public void A_saved_news_world_replays_with_one_or_four_workers()
    {
        var board = new NewsBoard();
        Assert.NotNull(board.Accept("town-fell", "Stack", "Shek Kingdom", "", "test", 65, 0, 10, 0, out _));
        using var saved = new MemoryStream();
        using var writer = new BinaryWriter(saved);
        board.Write(writer);

        ulong[] Run(int threads)
        {
            var news = new NewsSystem(null);
            saved.Position = 0;
            news.Read(saved);
            using var world = new World(new WorldSettings { Seed = 3, Threads = threads, PublishSnapshots = false, MinPartitionSize = 8 },
                new OpenGroundWalkability((_, _) => 150), [news]);
            world.Player.Faction = W.Player;
            for (int i = 0; i < 96; i++) W.Spawn(world, $"Scout {i}", W.Player, new Vector3(i * 8, 150, 0));
            var hashes = new ulong[65];
            for (int i = 0; i < hashes.Length; i++)
            {
                world.RunTicks(1);
                hashes[i] = world.StateHash();
            }
            return hashes;
        }

        var first = Run(1);
        Assert.Equal(first, Run(1));
        Assert.Equal(first, Run(4));
    }
}
