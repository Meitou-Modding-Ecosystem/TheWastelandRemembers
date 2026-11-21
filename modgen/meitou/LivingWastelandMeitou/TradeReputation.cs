using System.Reflection;
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace LivingWasteland.MeitouMod;

public sealed class TradeReputation(PopulationData? data) : ITickSystem
{
    const string FilterName = "Meitou.Simulation.Trading.ITradePriceFilter";
    static readonly ConditionalWeakTable<World, TradeReputation> worlds = new();
    static PropertyInfo? customer, shop, sell;
    static string status = "no world yet";

    readonly Dictionary<(int Site, int Faction), double> remarked = [];
    readonly PopulationData? population = data;

    public static void Register(ModContext context)
    {
        try
        {
            if (Build() is not { } adapter)
            {
                context.Log("reputation prices: this Meitou has no supported trade price hook");
                return;
            }
            context.AddSystem("lw-reputation", ctx => new TradeReputation(ctx.Data), SystemAnchor.End);
            context.AddSystem("lw-reputation-price", _ => (ITickSystem)Activator.CreateInstance(adapter)!, SystemAnchor.End);
            context.AddDebugCommand("Wasteland Remembers: reputation prices", _ => Volatile.Read(ref status), hasField: false);
            context.Log("reputation prices: trade price hook found (setting \"Prices by reputation\")");
        }
        catch (Exception e) { context.Log($"reputation prices: hook not added ({e.GetBaseException().Message})"); }
    }

    internal static Type? Build()
    {
        var hook = typeof(World).Assembly.GetType(FilterName);
        var deal = hook?.GetMethod("Price")?.GetParameters().ElementAtOrDefault(1)?.ParameterType.GetElementType();
        if (hook is null || deal is null ||
            (customer = deal.GetProperty("Customer"))?.PropertyType != typeof(CharacterId) ||
            (shop = deal.GetProperty("ShopSquad"))?.PropertyType != typeof(int) ||
            (sell = deal.GetProperty("Sell"))?.PropertyType != typeof(bool)) return null;
        return ObserverEmit.Build(hook, "Price", typeof(TradeReputation).GetMethod(nameof(Filter))!, "ReputationPriceSystem");
    }

    // Meitou calls this both when showing the window and when charging the customer. Nothing here changes the world.
    public static long Filter(World world, object deal, long price)
    {
        if (!LwMod.Settings.ReputationPrices || price <= 0 || !worlds.TryGetValue(world, out var state)) return price;
        try
        {
            if (sell?.GetValue(deal) is not false || customer?.GetValue(deal) is not CharacterId who ||
                shop?.GetValue(deal) is not int squad || world.Squads.Find(squad) is not { } trader) return price;
            int percent = state.Terms(world, trader, who);
            if (percent == 100) return price;
            decimal quote = Math.Round((decimal)price * percent / 100, MidpointRounding.AwayFromZero);
            return (long)Math.Clamp(quote, 1, long.MaxValue);
        }
        catch (Exception) { return price; }
    }

    bool TownShop(Squad shop) => population is { } d && shop.Town >= 0 &&
        shop.Town < d.Sites.Count && shop.Town < d.SiteFactions.Count && d.SiteFactions[shop.Town] == shop.Faction;

    int Terms(World world, Squad trader, CharacterId who)
    {
        if (population is not { } d || !TownShop(trader) || trader.Faction == world.Player.Faction ||
            LwDeeds.For(world) is not { } deeds || !world.Characters.TryResolveNext(who, out int slot) ||
            !world.Characters.Next[slot].Alive || world.Characters.Cold(slot) is not { IsPlayer: true }) return 100;
        string owner = deeds.FactionName(trader.Faction);
        if (owner.Length == 0 || world.Player.Faction < 0 || world.Player.Faction >= d.Factions.Count) return 100;
        float standing = d.Relations.Get(trader.Faction, world.Player.Faction);    // includes existing contract favours and alms
        double heat = LwMod.Settings.Heat ? deeds.Heat.HereOwned(deeds.PriceNode(trader.Town), owner) : 0;
        bool title = LwMod.Settings.TitleReactions && LwMod.TitleViews.TryGetValue(owner, out var views) && deeds.Record.Rules.Any(rule =>
            (views.GetValueOrDefault("fears")?.Contains(rule.Name) == true ||
             views.GetValueOrDefault("despises")?.Contains(rule.Name) == true) && deeds.Holds(world, who, rule.Id));
        int local = heat >= 24 ? 22 : heat >= 14 ? 12 : 0;
        int relations = standing >= 40 ? -8 : standing >= 20 ? -4 : standing <= -50 ? 8 : 0;
        return 100 + Math.Clamp(local + relations + (title ? 8 : 0), -8, 30);
    }

    public void SlowWorld(World world)
    {
        if (!worlds.TryGetValue(world, out _) && LwDeeds.For(world) is { } deeds)
        {
            // The title ladder builds lazily. Set it up here so a quote can only read it.
            foreach (var rule in deeds.Record.Rules) _ = deeds.Record.AnyHoldsTop(rule.Id);
            worlds.Add(world, this);
        }
        if (Published.Due(world.Tick, 14))
            Volatile.Write(ref status, LwMod.Settings.ReputationPrices ? $"{remarked.Count} town owners have remarked on the squad" : "off");
    }

    internal static bool Remark(World world, CharacterId speaker, CharacterId listener)
    {
        if (!LwMod.Settings.ReputationPrices || !worlds.TryGetValue(world, out var state) || state.population is not { } d ||
            !world.Characters.TryResolveNext(speaker, out int slot) || world.Characters.Cold(slot) is not
                { IsPlayer: false, InCombat: false, Animal: null, IsImprisoned: false, Medical: { Dead: false, Incapacitated: false } } cold ||
            !cold.CarriedBy.IsNone || world.Squads.Find(cold.SquadId) is not { } trader || !state.TownShop(trader) ||
            d.TownAt(world.Characters.Next[slot].Position) != trader.Town) return false;
        string faction = LwDeeds.For(world)?.FactionName(trader.Faction) ?? "";
        if (faction is not ("The Holy Nation" or "United Cities" or "Shek Kingdom" or "Tech Hunters" or "Trade Ninjas")) return false;
        var key = (trader.Town, trader.Faction);
        if (state.remarked.TryGetValue(key, out double last) && world.GameHours - last < 24) return false;
        int terms = state.Terms(world, trader, listener);
        if (terms == 100) return false;
        world.Bark(speaker, (faction, terms < 100) switch
        {
            ("The Holy Nation", true) => "The faithful remember those who lend a hand.",
            ("The Holy Nation", false) => "Word of your lot travels.",
            ("Shek Kingdom", true) => "You stood with us. We remember.",
            ("Shek Kingdom", false) => "We remember your kind.",
            (_, true) => "Folk round here remember a favour.",
            _ => "Word gets round fast here.",
        });
        state.remarked[key] = world.GameHours;
        if (PlaytestLog.Enabled)
            PlaytestLog.Event(world, "shop reputation", NewsSystem.NodeOf(d.Sites[trader.Town]), "trader remarked",
                $"{faction} in {d.Sites[trader.Town].Town.Name}; buy modifier {terms - 100:+0;-0;0}% at remark");
        LwMod.Log($"reputation prices: a remark in {d.Sites[trader.Town].Town.Name} ({faction})");
        LogTally.Add("shop remarks");
        return true;
    }
}
