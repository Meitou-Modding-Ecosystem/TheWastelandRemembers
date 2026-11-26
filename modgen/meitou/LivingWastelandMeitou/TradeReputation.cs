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

    static readonly Type? tradeObserver = typeof(World).Assembly.GetType("Meitou.Simulation.Trading.ITradeObserver");
    static readonly Type? receipt = typeof(World).Assembly.GetType("Meitou.Simulation.Trading.TradeSale");
    static readonly PropertyInfo? saleKind = receipt?.GetProperty("Kind"), buyer = receipt?.GetProperty("Buyer"),
        seller = receipt?.GetProperty("Seller"), cats = receipt?.GetProperty("Price"), units = receipt?.GetProperty("Units"),
        item = receipt?.GetProperty("Record"), buyback = receipt?.GetProperty("Buyback"), delivered = receipt?.GetProperty("Delivered");
    static readonly PropertyInfo? person = buyer?.PropertyType.GetProperty("Character"), buyingFaction = buyer?.PropertyType.GetProperty("Faction"),
        shopParty = seller?.PropertyType.GetProperty("Squad");
    static readonly object? windowBuy = saleKind?.PropertyType.IsEnum == true &&
        Enum.TryParse(saleKind.PropertyType, "WindowBuy", out var value) ? value : null;

    internal static bool ReceiptHookAvailable => tradeObserver is not null && windowBuy is not null;

    readonly Dictionary<(int Site, int Faction), double> remarked = [];
    readonly PopulationData? population = data;
    sealed record TitleIndex(Dictionary<string, Dictionary<string, List<string>>> Views,
        IReadOnlyList<TitleRule> Rules, Dictionary<string, TitleRule[]> ByOwner);
    TitleIndex? titleIndex;
    int journalHour = -1, detailedPurchases;

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
            context.AddDebugCommand(UiLanguage.Text("Wasteland Remembers: reputation prices"), _ => Volatile.Read(ref status), hasField: false);
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
        var filter = typeof(TradeReputation).GetMethod(nameof(Filter))!;
        if (tradeObserver is not null && windowBuy is not null &&
            ObserverEmit.BuildPair(hook, "Price", filter, "ReputationPriceSystem", tradeObserver, "Traded",
                typeof(TradeReputation).GetMethod(nameof(Traded))!) is { } paired)
            return paired;
        return ObserverEmit.Build(hook, "Price", filter, "ReputationPriceSystem");
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

    // A receipt follows money and goods. Window previews, sales to the shop and buybacks do not go in the journal.
    public static void Traded(World world, object sale)
    {
        if (!PlaytestLog.Enabled || !LwMod.Settings.ReputationPrices || !worlds.TryGetValue(world, out var state) ||
            !Equals(windowBuy, saleKind?.GetValue(sale)) || buyback?.GetValue(sale) is not false ||
            delivered?.GetValue(sale) is not true || buyer?.GetValue(sale) is not { } customer ||
            buyingFaction?.GetValue(customer) is not int faction || faction != world.Player.Faction ||
            person?.GetValue(customer) is not CharacterId who ||
            seller?.GetValue(sale) is not { } trader || shopParty?.GetValue(trader) is not int squad ||
            world.Squads.Find(squad) is not { } shop)
            return;
        int terms = state.Terms(world, shop, who);
        if (terms == 100) return;

        LogTally.Add("reputation-priced buys");
        int hour = (int)world.GameHours;
        if (state.journalHour != hour) (state.journalHour, state.detailedPurchases) = (hour, 0);
        const int DetailedPerHour = 4;
        if (state.detailedPurchases >= DetailedPerHour || state.population is not { } data) return;
        state.detailedPurchases++;
        var site = data.Sites[shop.Town];
        PlaytestLog.Event(world, "shop reputation", NewsSystem.NodeOf(site), "purchase completed",
            $"{site.Town.Name}: {item?.GetValue(sale)} ×{units?.GetValue(sale)}, {cats?.GetValue(sale)} cats paid, TWR terms at receipt {terms - 100:+0;-0;0}%");
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
        bool title = LwMod.Settings.TitleReactions && HasWorryingTitle(world, deeds, who, owner);
        int local = heat >= 24 ? 22 : heat >= 14 ? 12 : 0;
        int relations = standing >= 40 ? -8 : standing >= 20 ? -4 : standing <= -50 ? 8 : 0;
        return 100 + Math.Clamp(local + relations + (title ? 8 : 0), -8, 30);
    }

    bool HasWorryingTitle(World world, LwDeeds deeds, CharacterId who, string owner)
    {
        var views = LwMod.TitleViews;
        var rules = deeds.Record.Rules;
        if (titleIndex is { } index && ReferenceEquals(views, index.Views) && ReferenceEquals(rules, index.Rules))
        {
            if (!index.ByOwner.TryGetValue(owner, out var relevant)) return false;
            foreach (var rule in relevant)
                if (deeds.Holds(world, who, rule.Id)) return true;
        }
        else if (views.TryGetValue(owner, out var current))
        {
            foreach (var rule in rules)
                if (Worries(current, rule.Name) && deeds.Holds(world, who, rule.Id)) return true;
        }
        return false;
    }

    static bool Worries(Dictionary<string, List<string>> views, string title) =>
        views.GetValueOrDefault("fears")?.Contains(title) == true || views.GetValueOrDefault("despises")?.Contains(title) == true;

    public void SlowWorld(World world)
    {
        if (!worlds.TryGetValue(world, out _) && LwDeeds.For(world) is { } deeds)
        {
            // The title ladder builds lazily. Set it up here so a quote can only read it.
            var rules = deeds.Record.Rules;
            foreach (var rule in rules) _ = deeds.Record.AnyHoldsTop(rule.Id);
            var views = LwMod.TitleViews;
            var byOwner = new Dictionary<string, TitleRule[]>(StringComparer.Ordinal);
            foreach (var (owner, opinions) in views)
            {
                var relevant = rules.Where(rule => Worries(opinions, rule.Name)).ToArray();
                if (relevant.Length > 0) byOwner.Add(owner, relevant);
            }
            titleIndex = new TitleIndex(views, rules, byOwner);
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
