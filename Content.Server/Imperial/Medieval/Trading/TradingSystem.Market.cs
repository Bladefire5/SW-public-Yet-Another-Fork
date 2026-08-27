using System.Globalization;
using System.Linq;
using Content.Server.Imperial.Medieval.Courier;
using Content.Server.Light.Components;
using Content.Shared.Imperial.Medieval.Additions;
using Content.Shared.Imperial.Medieval.ArmorIntegrity;
using Content.Shared.Imperial.Medieval.Chemistry;
using Content.Shared.Imperial.Medieval.SmithingSystem;
using Content.Shared.Imperial.Medieval.SmithingSystem.Behaviours;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.MedievalMeleeResource.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Trigger.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem
{
    internal bool CreateMarket()
    {
        DeleteMarket();
        if (!TryFindMarketMap(out var map))
        {
            Log.Error("Trading market could not be created because no active map exists.");
            return false;
        }

        var uid = Spawn(null, new EntityCoordinates(map, 0f, 0f));
        var market = EnsureComp<TradingMarketComponent>(uid);
        _market = uid;

        var config = _prototypeManager.Index(market.Config);
        foreach (var guildType in _prototypeManager.EnumeratePrototypes<GuildTypePrototype>())
        {
            if (config.GuildTypes.Count > 0 && !config.GuildTypes.Contains(guildType.ID))
                continue;

            for (var index = 0; index < guildType.MaximumGuilds; index++)
            {
                market.Guilds.Add(new Guild(guildType, _random, _prototypeManager));
            }

            foreach (var item in guildType.Items)
            {
                if (item.ProductEntity is not { } product)
                    continue;

                var prototype = _prototypeManager.Index(product);
                if (!CanTradeProduct(prototype, config))
                    continue;

                TradingCommodity commodity;
                if (!market.CommonCommodities.TryGetValue(product, out var commodityId))
                {
                    prototype.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory);
                    commodityId = Guid.NewGuid();
                    commodity = new TradingCommodity
                    {
                        Id = commodityId,
                        Product = product,
                        Sections = TradingMarketSection.Common,
                        StandardPrice = item.Cost,
                        Demand = config.InitialDemand,
                        Supply = config.InitialSupply,
                        BaselineStackCount = stack?.Count ?? 1,
                        HasStack = stack != null,
                        Permanent = true,
                        Signature = $"common:{product.Id}",
                        DisplayName = FormatStackName(prototype.Name, stack?.Count),
                        Description = prototype.Description,
                    };
                    market.Commodities.Add(commodityId, commodity);
                    market.CommonCommodities.Add(product, commodityId);
                }
                else
                {
                    commodity = market.Commodities[commodityId];
                }

                commodity.Categories.Add(guildType.ID);
                commodity.MinReputation = Math.Max(commodity.MinReputation, item.MinReputation);
            }
        }

        InitializeReputationScarcity(market, config);
        SeedGuildOffers((uid, market), config);
        return true;
    }

    private bool TryFindMarketMap(out EntityUid map)
    {
        var maps = EntityQueryEnumerator<MapComponent>();
        while (maps.MoveNext(out var mapUid, out _))
        {
            if (TerminatingOrDeleted(mapUid) || EntityManager.IsQueuedForDeletion(mapUid))
                continue;

            map = mapUid;
            return true;
        }

        map = default;
        return false;
    }

    private void DeleteMarket()
    {
        var query = EntityQueryEnumerator<TradingMarketComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                QueueDel(uid);
        }

        _market = null;
    }

    private static void InitializeReputationScarcity(
        TradingMarketComponent market,
        TradingMarketConfigPrototype config)
    {
        var states = new Dictionary<(int Price, int Reputation), (float Demand, float Supply)>();
        foreach (var commodity in market.Commodities.Values)
        {
            if (commodity.MinReputation <= 0)
                continue;

            var key = (commodity.StandardPrice, commodity.MinReputation);
            if (!states.TryGetValue(key, out var scarcity))
            {
                scarcity = GetReputationScarcityInitialState(commodity, config);
                states.Add(key, scarcity);
            }

            commodity.Demand = scarcity.Demand;
            commodity.Supply = scarcity.Supply;
        }
    }

    private static float GetExpectedLowestPriceNoise(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var offerCount = Math.Max(1, GetGuildOfferTarget(commodity, config));
        return -config.PriceNoise * (offerCount - 1f) / (offerCount + 1f);
    }

    private void RunMarketStep(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        CreateGuildActivity(market, config);
        RemoveUncompetitiveGuildOffers(market, config);
        MatchAll(market, config);
    }

    private void SeedGuildOffers(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodityId in market.Comp.CommonCommodities.Values)
        {
            if (!market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
                continue;

            var candidates = GetGuildCandidates(market, commodity);
            if (candidates.Count == 0)
                continue;

            var offerCount = GetGuildOfferTarget(commodity, config);
            for (var index = 0; index < offerCount; index++)
            {
                TryCreateGuildOffer(
                    market,
                    _random.Pick(candidates),
                    commodity,
                    TradingOfferSide.Sell,
                    config,
                    false);
                TryCreateGuildOffer(
                    market,
                    _random.Pick(candidates),
                    commodity,
                    TradingOfferSide.Buy,
                    config,
                    false);
            }
        }
    }

    private void CreateGuildActivity(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodityId in market.Comp.CommonCommodities.Values)
        {
            if (!market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
                continue;

            var candidates = GetGuildCandidates(market, commodity);
            var currentSells = GetGuildOfferCount(market, commodity, TradingOfferSide.Sell);
            CreateGuildOffers(
                market,
                commodity,
                candidates,
                TradingOfferSide.Sell,
                Math.Max(0, config.MaximumGuildSellOfferCount - currentSells),
                config);

            var currentBuys = GetGuildOfferCount(market, commodity, TradingOfferSide.Buy);
            CreateGuildOffers(
                market,
                commodity,
                candidates,
                TradingOfferSide.Buy,
                Math.Max(0, config.MaximumGuildBuyOrderCount - currentBuys),
                config);
        }
    }

    private void RemoveUncompetitiveGuildOffers(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodityId in market.Comp.CommonCommodities.Values)
        {
            if (!market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
                continue;

            var sellOffer = market.Comp.Offers.Values
                .Where(offer => offer.CommodityId == commodity.Id &&
                                offer.ParticipantKind == TradingParticipantKind.Guild &&
                                offer.Side == TradingOfferSide.Sell)
                .OrderByDescending(offer => offer.Price)
                .ThenBy(offer => offer.Sequence)
                .FirstOrDefault();
            TryRemoveUncompetitiveGuildOffer(market, commodity, sellOffer, config);

            var buyOrder = market.Comp.Offers.Values
                .Where(offer => offer.CommodityId == commodity.Id &&
                                offer.ParticipantKind == TradingParticipantKind.Guild &&
                                offer.Side == TradingOfferSide.Buy)
                .OrderBy(offer => offer.Price)
                .ThenBy(offer => offer.Sequence)
                .FirstOrDefault();
            TryRemoveUncompetitiveGuildOffer(market, commodity, buyOrder, config);
        }
    }

    private void TryRemoveUncompetitiveGuildOffer(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingMarketOffer? offer,
        TradingMarketConfigPrototype config)
    {
        if (offer == null || _random.NextFloat() >= GetGuildOfferRemovalChance(commodity, offer, config))
            return;

        RemoveOffer(market, offer.Id, true, config);
    }

    private List<Guild> GetGuildCandidates(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity)
    {
        return market.Comp.Guilds
            .Where(guild => guild.Items.Any(item => item.ProductEntity is { } product &&
                                                   product == commodity.Product))
            .ToList();
    }

    private static int GetGuildOfferCount(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingOfferSide side)
    {
        return market.Comp.Offers.Values.Count(offer =>
            offer.CommodityId == commodity.Id &&
            offer.ParticipantKind == TradingParticipantKind.Guild &&
            offer.Side == side);
    }

    private void CreateGuildOffers(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        List<Guild> candidates,
        TradingOfferSide side,
        int availableSlots,
        TradingMarketConfigPrototype config)
    {
        if (availableSlots <= 0 || candidates.Count == 0)
            return;

        var creationChance = GetGuildOfferCreationChance(commodity, side, config);
        var attempts = Math.Min(availableSlots, GetGuildOfferTarget(commodity, config));

        for (var index = 0; index < attempts; index++)
        {
            if (_random.NextFloat() >= creationChance)
                continue;

            TryCreateGuildOffer(
                market,
                _random.Pick(candidates),
                commodity,
                side,
                config);
        }
    }

    internal static float GetGuildOfferCreationChance(
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        return side == TradingOfferSide.Sell
            ? GetGuildSellOfferCreationChance(
                config.GuildSellOfferChance,
                commodity.Supply,
                config.InitialSupply)
            : GetGuildBuyOrderCreationChance(
                config.GuildBuyOrderChance,
                commodity.Demand,
                config.InitialDemand);
    }

    internal static float GetGuildSellOfferCreationChance(
        float baseChance,
        float supply,
        float initialSupply)
    {
        return GetSaturationAdjustedCreationChance(baseChance, supply, initialSupply);
    }

    internal static float GetGuildBuyOrderCreationChance(
        float baseChance,
        float demand,
        float initialDemand)
    {
        return GetSaturationAdjustedCreationChance(baseChance, demand, initialDemand);
    }

    private static float GetSaturationAdjustedCreationChance(
        float baseChance,
        float saturation,
        float initialSaturation)
    {
        var chance = baseChance * Math.Max(float.Epsilon, initialSaturation) /
                     Math.Max(float.Epsilon, saturation);
        return Math.Min(chance, baseChance * 2f);
    }

    internal static float GetExpectedGuildOfferCount(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var price = Math.Max(1, commodity.StandardPrice);
        var referencePrice = Math.Max(1, config.LiquidityReferencePrice);
        var expected = config.LiquidityReferenceOfferCount *
                       MathF.Pow((float) referencePrice / price, config.LiquidityPriceExponent);
        return Math.Clamp(expected, config.MinimumGuildOfferCount, config.MaximumGuildOfferCount);
    }

    internal static int GetGuildOfferTarget(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        return (int) MathF.Round(GetExpectedGuildOfferCount(commodity, config));
    }

    internal static float GetMarketImpactScale(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        return config.MarketImpactReferenceOfferCount / GetExpectedGuildOfferCount(commodity, config);
    }

    internal static float GetGuildOfferRemovalChance(
        TradingCommodity commodity,
        TradingMarketOffer offer,
        TradingMarketConfigPrototype config)
    {
        var standardPrice = Math.Max(1, commodity.StandardPrice);
        float baseChance;
        float priceScale;
        float marketScale;

        if (offer.Side == TradingOfferSide.Sell)
        {
            baseChance = config.GuildSellOfferRemovalChance;
            priceScale = Math.Max(1f, (float) offer.Price / standardPrice);
            marketScale = Math.Max(1f,
                Math.Max(0f, commodity.Supply) / Math.Max(float.Epsilon, config.InitialSupply));
        }
        else
        {
            baseChance = config.GuildBuyOrderRemovalChance;
            priceScale = Math.Max(1f, (float) standardPrice / Math.Max(1, offer.Price));
            marketScale = Math.Max(1f,
                Math.Max(0f, commodity.Demand) / Math.Max(float.Epsilon, config.InitialDemand));
        }

        return Math.Clamp(baseChance * priceScale * marketScale, 0f, 1f);
    }

    private void TryCreateGuildOffer(
        Entity<TradingMarketComponent> market,
        Guild guild,
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config,
        bool applyPlacementImpact = true)
    {
        AddOffer(market, new TradingMarketOffer
        {
            Id = Guid.NewGuid(),
            CommodityId = commodity.Id,
            Product = commodity.Product,
            Side = side,
            ParticipantKind = TradingParticipantKind.Guild,
            ParticipantName = guild.Name,
            Price = GetGuildPrice(commodity, side, config),
            GuildId = guild.Id,
            Sequence = market.Comp.NextSequence++,
        }, config, applyPlacementImpact);
    }

    private int GetGuildPrice(
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        var center = GetMarketPriceCenterFactor(commodity.Demand, commodity.Supply, config);
        var spread = side == TradingOfferSide.Sell ? config.PriceSpread / 2f : -config.PriceSpread / 2f;
        var noise = _random.NextFloat(-config.PriceNoise, config.PriceNoise);
        var factor = center + spread + noise;
        return RoundMarketPrice(commodity.StandardPrice * (double) factor);
    }

    internal static float GetMarketPriceCenterFactor(
        float demand,
        float supply,
        TradingMarketConfigPrototype config)
    {
        var floor = Math.Max(float.Epsilon, config.PriceSaturationFloor);
        var baselineRatio = (Math.Max(0f, config.InitialDemand) + floor) /
                            (Math.Max(0f, config.InitialSupply) + floor);
        var marketRatio = (Math.Max(0f, demand) + floor) /
                          (Math.Max(0f, supply) + floor);
        var relativeRatio = Math.Max(float.Epsilon, marketRatio / baselineRatio);
        var factor = 1f + (relativeRatio - 1f) * Math.Max(0f, config.PriceRatioSlope);
        return Math.Max(float.Epsilon, factor);
    }

    private static int RoundMarketPrice(double price)
    {
        if (double.IsNaN(price) || price <= 1d)
            return 1;

        return price >= int.MaxValue ? int.MaxValue : (int) Math.Round(price);
    }

    private void AddOffer(
        Entity<TradingMarketComponent> market,
        TradingMarketOffer offer,
        TradingMarketConfigPrototype config,
        bool applyPlacementImpact = true)
    {
        if (market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity))
        {
            var impactScale = GetMarketImpactScale(commodity, config);
            if (offer.Side == TradingOfferSide.Sell)
            {
                offer.SupplyContribution = config.SupplyPlacementImpact * impactScale *
                                           GetPriceImpactRatio((float) commodity.StandardPrice / offer.Price);
                if (applyPlacementImpact)
                    commodity.Supply += offer.SupplyContribution;
            }
            else
            {
                offer.DemandContribution = config.DemandPlacementImpact * impactScale *
                                           GetPriceImpactRatio((float) offer.Price /
                                                               Math.Max(1, commodity.StandardPrice));
                if (applyPlacementImpact)
                    commodity.Demand += offer.DemandContribution;
            }
        }

        market.Comp.Offers.Add(offer.Id, offer);
        if (offer.Pit is { } pit && TryComp<TradingComponent>(pit, out var trading))
            trading.MarketOffers.Add(offer.Id);
    }

    private static float GetPriceImpactRatio(float priceFactor)
    {
        return Math.Clamp(priceFactor, 0.25f, 4f);
    }

    private void MatchAll(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodity in market.Comp.Commodities.Values.ToList())
        {
            MatchCommodity(market, commodity, config);
        }
    }

    internal void MatchCommodity(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        while (true)
        {
            var asks = market.Comp.Offers.Values
                .Where(offer => offer.CommodityId == commodity.Id && offer.Side == TradingOfferSide.Sell)
                .OrderBy(offer => offer.Price)
                .ThenBy(offer => offer.Sequence)
                .ToList();
            var bids = market.Comp.Offers.Values
                .Where(offer => offer.CommodityId == commodity.Id && offer.Side == TradingOfferSide.Buy)
                .OrderByDescending(offer => offer.Price)
                .ThenBy(offer => offer.Sequence)
                .ToList();

            TradingMarketOffer? ask = null;
            TradingMarketOffer? bid = null;
            foreach (var candidateAsk in asks)
            {
                var candidateBid = bids.FirstOrDefault(value =>
                    value.Price >= candidateAsk.Price &&
                    !IsSameParticipant(value, candidateAsk) &&
                    !(candidateAsk.ParticipantKind == TradingParticipantKind.Trader &&
                      value.ParticipantKind == TradingParticipantKind.Guild));
                if (candidateBid == null)
                    continue;

                ask = candidateAsk;
                bid = candidateBid;
                break;
            }

            if (ask == null || bid == null)
                break;

            CompleteTrade(market, commodity, ask, bid, config);
        }
    }

    private static bool IsSameParticipant(TradingMarketOffer first, TradingMarketOffer second)
    {
        if (first.ParticipantKind != second.ParticipantKind)
            return false;

        return first.ParticipantKind == TradingParticipantKind.Trader
            ? first.Pit == second.Pit
            : first.GuildId == second.GuildId;
    }

    private void CompleteTrade(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingMarketOffer ask,
        TradingMarketOffer bid,
        TradingMarketConfigPrototype config)
    {
        if (ask.Item is { } escrowItem)
        {
            if (!CanTradeItem(escrowItem, config))
            {
                Log.Error(
                    $"Trading market could not complete trade for sell offer {ask.Id}: escrow item {escrowItem} " +
                    $"for product {ask.Product} is no longer eligible for trading.");
                RemoveOffer(market, ask.Id, true, config);
                if (bid.IsImmediate)
                    RemoveOffer(market, bid.Id, false, config);
                return;
            }

            if (!CanTransferEscrowItem(ask, escrowItem))
            {
                Log.Error(
                    $"Trading market could not complete trade for sell offer {ask.Id}: escrow item {escrowItem} " +
                    $"for product {ask.Product} no longer exists or is no longer held by trading pit {ask.Pit}.");
                RemoveOffer(market, ask.Id, false, config);
                if (bid.IsImmediate)
                    RemoveOffer(market, bid.Id, false, config);
                return;
            }

            var currentName = MetaData(escrowItem).EntityName;
            if (!string.Equals(currentName, ask.ListedItemName, StringComparison.Ordinal))
            {
                Log.Error(
                    $"Trading market sell offer {ask.Id} changed item name while in escrow: " +
                    $"'{ask.ListedItemName}' -> '{currentName}' for item {escrowItem} and product {ask.Product}.");
            }
        }

        var executionPrice = ask.Sequence < bid.Sequence ? ask.Price : bid.Price;
        ArchiveTrade(commodity, ask, bid, executionPrice);

        if (bid.Pit is { } buyerPit && TryComp<TradingComponent>(buyerPit, out var buyer))
            buyer.Balance += bid.Price - executionPrice;

        if (ask.Pit is { } sellerPit && TryComp<TradingComponent>(sellerPit, out var seller))
            seller.Balance += executionPrice;

        if (ask.Item is { } item)
        {
            if (bid.Pit is { } destination && TryComp<TradingComponent>(destination, out var destinationPit))
                DeliverItem(destination, destinationPit, item, bid.ImmediateRecipient);
            else
                QueueDel(item);
        }
        else if (ask.ParticipantKind == TradingParticipantKind.Guild &&
                 bid.Pit is { } destination &&
                 TryComp<TradingComponent>(destination, out var destinationPit))
        {
            var spawnedItem = Spawn(ask.Product, MapCoordinates.Nullspace);
            DeliverItem(destination, destinationPit, spawnedItem, bid.ImmediateRecipient);
        }

        RemoveOfferRecord(market, ask);
        RemoveOfferRecord(market, bid);

        var impactScale = GetMarketImpactScale(commodity, config);
        commodity.Supply = Math.Max(0f,
            commodity.Supply - ask.SupplyContribution - config.SupplyTradeImpact * impactScale);
        commodity.Demand = Math.Max(0f,
            commodity.Demand - bid.DemandContribution - config.DemandTradeImpact * impactScale);
        TryRemoveCommodity(market, commodity);
    }

    private bool CanTransferEscrowItem(TradingMarketOffer offer, EntityUid item)
    {
        if (!Exists(item) ||
            TerminatingOrDeleted(item) ||
            EntityManager.IsQueuedForDeletion(item) ||
            offer.Pit is not { } sellerPit ||
            !TryComp<TradingComponent>(sellerPit, out _) ||
            !_containers.TryGetContainingContainer((item, null, null), out var container))
        {
            return false;
        }

        return container.Owner == sellerPit && container.ID == TradingComponent.MarketContainerId;
    }

    private void ArchiveTrade(
        TradingCommodity commodity,
        TradingMarketOffer ask,
        TradingMarketOffer bid,
        int executionPrice)
    {
        var displayName = commodity.DisplayName;
        if (ask.Item is { } item && Exists(item))
        {
            var metadata = MetaData(item);
            var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : (int?) null;
            displayName = FormatStackName(metadata.EntityName, stackCount);
        }

        if (ask.ParticipantKind == TradingParticipantKind.Trader &&
            !ask.IsImmediate &&
            ask.Pit is { } sellerPit &&
            TryComp<TradingComponent>(sellerPit, out var seller))
        {
            seller.MarketArchive.Add(
                $"ваш лот {displayName} был куплен торговцем {bid.ParticipantName} за {executionPrice} ревентов");
        }

        if (bid.ParticipantKind == TradingParticipantKind.Trader &&
            !bid.IsImmediate &&
            bid.Pit is { } buyerPit &&
            TryComp<TradingComponent>(buyerPit, out var buyer))
        {
            buyer.MarketArchive.Add(
                $"Ваш заказ {displayName} был выполнен торговцем {ask.ParticipantName} за {executionPrice} ревентов");
        }
    }

    internal void RemoveOffer(
        Entity<TradingMarketComponent> market,
        Guid id,
        bool returnEscrow,
        TradingMarketConfigPrototype config,
        EntityUid? recipient = null)
    {
        if (!market.Comp.Offers.TryGetValue(id, out var offer))
            return;

        if (offer.Side == TradingOfferSide.Buy)
        {
            if (offer.Pit is { } buyerId && TryComp<TradingComponent>(buyerId, out var buyer))
                buyer.Balance += offer.Price;

            if (market.Comp.Commodities.TryGetValue(offer.CommodityId, out var demandCommodity))
                demandCommodity.Demand = Math.Max(0f, demandCommodity.Demand - offer.DemandContribution);
        }

        if (offer.Side == TradingOfferSide.Sell &&
            market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity))
        {
            commodity.Supply = Math.Max(0f, commodity.Supply - offer.SupplyContribution);
        }

        if (offer.Item is { } item && returnEscrow)
        {
            if (offer.Pit is { } sellerId && TryComp<TradingComponent>(sellerId, out var seller))
                DeliverItem(sellerId, seller, item, recipient);
            else
                QueueDel(item);
        }

        RemoveOfferRecord(market, offer);
        if (market.Comp.Commodities.TryGetValue(offer.CommodityId, out var removedCommodity))
            TryRemoveCommodity(market, removedCommodity);
    }

    private void RemoveOfferRecord(
        Entity<TradingMarketComponent> market,
        TradingMarketOffer offer)
    {
        market.Comp.Offers.Remove(offer.Id);
        if (offer.Pit is { } pit && TryComp<TradingComponent>(pit, out var trading))
            trading.MarketOffers.Remove(offer.Id);
    }

    private void TryRemoveCommodity(Entity<TradingMarketComponent> market, TradingCommodity commodity)
    {
        if (commodity.Permanent || market.Comp.Offers.Values.Any(offer => offer.CommodityId == commodity.Id))
            return;

        market.Comp.Commodities.Remove(commodity.Id);
    }

    private bool CanTradeItem(EntityUid item, TradingMarketConfigPrototype config)
    {
        if (!Exists(item) ||
            TerminatingOrDeleted(item) ||
            EntityManager.IsQueuedForDeletion(item) ||
            MetaData(item).EntityPrototype is not { } prototype ||
            !HasComp<ItemComponent>(item) ||
            !CanTradeProduct(prototype, config) ||
            HasComp<VirtualItemComponent>(item) ||
            HasComp<MobStateComponent>(item) ||
            HasBlockedTraderItemTag(item, config) ||
            ContainsPlayerMind(item) ||
            HasComp<TimedDespawnComponent>(item) ||
            HasComp<MedievalTimedDespawnComponent>(item) ||
            HasComp<ActiveTimerTriggerComponent>(item) ||
            HasComp<ActiveTwoStageTriggerComponent>(item))
        {
            return false;
        }

        if (TryComp<ExpendableLightComponent>(item, out var light) &&
            light.CurrentState != ExpendableLightState.BrandNew)
        {
            return false;
        }

        return !HasComp<LetterComponent>(item);
    }

    private bool CanTradeProduct(EntProtoId product, TradingMarketConfigPrototype config)
    {
        return _prototypeManager.TryIndex(product, out var prototype) &&
               CanTradeProduct(prototype, config);
    }

    private bool CanTradeProduct(EntityPrototype prototype, TradingMarketConfigPrototype config)
    {
        if (!prototype.HasComponent<ItemComponent>() ||
            prototype.HasComponent<VirtualItemComponent>() ||
            prototype.HasComponent<MobStateComponent>() ||
            prototype.HasComponent<TimedDespawnComponent>() ||
            prototype.HasComponent<MedievalTimedDespawnComponent>() ||
            prototype.HasComponent<ActiveTimerTriggerComponent>() ||
            prototype.HasComponent<ActiveTwoStageTriggerComponent>() ||
            prototype.HasComponent<LetterComponent>() ||
            HasBlockedTraderProductTag(prototype, config))
        {
            return false;
        }

        return !prototype.TryGetComponent<ExpendableLightComponent>(
                   out var light,
                   EntityManager.ComponentFactory) ||
               light.CurrentState == ExpendableLightState.BrandNew;
    }

    private bool HasBlockedTraderItemTag(EntityUid item, TradingMarketConfigPrototype config)
    {
        return config.BlockedTraderItemTags.Count > 0 &&
               _tags.HasAnyTag(item, config.BlockedTraderItemTags);
    }

    private bool HasBlockedTraderProductTag(EntityPrototype prototype, TradingMarketConfigPrototype config)
    {
        return config.BlockedTraderItemTags.Count > 0 &&
               prototype.TryGetComponent<TagComponent>(out var tags, EntityManager.ComponentFactory) &&
               _tags.HasAnyTag(tags, config.BlockedTraderItemTags);
    }

    private bool ContainsPlayerMind(EntityUid root)
    {
        var pending = new Queue<EntityUid>();
        var visited = new HashSet<EntityUid>();
        pending.Enqueue(root);

        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current))
                continue;

            if (TryComp<MindContainerComponent>(current, out var mind) && mind.HasMind)
                return true;

            if (!TryComp<ContainerManagerComponent>(current, out var containerManager))
                continue;

            foreach (var container in _containers.GetAllContainers(current, containerManager))
            {
                foreach (var contained in container.ContainedEntities)
                {
                    pending.Enqueue(contained);
                }
            }
        }

        return false;
    }

    internal bool TryResolveCommodityForItem(
        Entity<TradingMarketComponent> market,
        EntityUid item,
        int fallbackPrice,
        bool create,
        out TradingCommodity commodity,
        int? stackCountOverride = null,
        bool forceIntactEquipment = false)
    {
        commodity = default!;
        var config = _prototypeManager.Index(market.Comp.Config);
        if (!CanTradeItem(item, config))
            return false;

        if (HasComp<VirtualItemComponent>(item) ||
            MetaData(item).EntityPrototype?.ID is not { } product)
        {
            return false;
        }

        market.Comp.CommonCommodities.TryGetValue(product, out var commonId);
        TradingCommodity? common = null;
        var hasCommon = commonId != Guid.Empty && market.Comp.Commodities.TryGetValue(commonId, out common);
        var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
        if (stackCountOverride is { } overrideCount)
        {
            if (stack == null || overrideCount <= 0 || overrideCount > stack.Count)
                return false;

            stackCount = overrideCount;
        }

        var hasStack = stack != null;
        var isRecipe = HasComp<MedievalRandomChemistryRecipeComponent>(item);
        var hasCurrencyValue = HasComp<MedievalCurrencyComponent>(item);
        var hasAppliedQuality = TryComp<SmithQualityComponent>(item, out var quality) && quality.Applied;
        var isEquipment = hasAppliedQuality ||
                          HasComp<MedievalMeleeResourceComponent>(item) ||
                          HasComp<MedievalArmorIntegrityComponent>(item);
        var isDamagedEquipment = !forceIntactEquipment && IsDamagedEquipment(item);
        var matchesCommon = !isRecipe &&
                            !hasCurrencyValue &&
                            hasCommon &&
                            common != null &&
                            common.HasStack == hasStack &&
                            common.BaselineStackCount == stackCount &&
                            (!isEquipment || (!hasAppliedQuality && !isDamagedEquipment));

        if (matchesCommon)
        {
            commodity = common!;
            return true;
        }

        var signature = BuildItemSignature(item, product, stackCount, isEquipment, isDamagedEquipment);
        var existing = market.Comp.Commodities.Values.FirstOrDefault(value =>
            !value.Permanent && value.Signature == signature);
        if (existing != null)
        {
            existing.IsDamagedEquipment = isDamagedEquipment;
            commodity = existing;
            return true;
        }

        if (!create)
            return false;

        var standardPrice = hasCommon && common != null
            ? common.StandardPrice
            : Math.Max(fallbackPrice, 1);
        var metadata = MetaData(item);
        commodity = new TradingCommodity
        {
            Id = Guid.NewGuid(),
            Product = product,
            Sections = TradingMarketSection.Unique,
            StandardPrice = standardPrice,
            Demand = hasCommon && common != null ? common.Demand : config.InitialDemand,
            Supply = 0f,
            BaselineStackCount = stackCount,
            HasStack = hasStack,
            IsDamagedEquipment = isDamagedEquipment,
            Signature = signature,
            DisplayName = FormatStackName(metadata.EntityName, hasStack ? stackCount : null),
            Description = metadata.EntityDescription,
            Categories = hasCommon && common != null
                ? new HashSet<ProtoId<GuildTypePrototype>>(common.Categories)
                : new HashSet<ProtoId<GuildTypePrototype>>(),
        };
        market.Comp.Commodities.Add(commodity.Id, commodity);
        return true;
    }

    private bool IsDamagedEquipment(EntityUid item)
    {
        var equipment = false;
        var damaged = false;

        if (TryComp<MedievalMeleeResourceComponent>(item, out var weapon))
        {
            equipment = true;
            damaged |= weapon.Resource <= 80f;
        }

        if (TryComp<MedievalArmorIntegrityComponent>(item, out var armor))
        {
            equipment = true;
            damaged |= !MathHelper.CloseTo(armor.MaxArmorHP, armor.ContainerArmorHP) ||
                       !MathHelper.CloseTo(armor.CurrentArmorHP, armor.ContainerArmorHP);
        }

        return equipment && damaged;
    }

    private string BuildItemSignature(
        EntityUid item,
        EntProtoId product,
        int stackCount,
        bool isEquipment,
        bool isDamagedEquipment)
    {
        var values = new List<string>
        {
            product.Id,
            stackCount.ToString(CultureInfo.InvariantCulture),
        };

        if (isEquipment)
        {
            values.Add(isDamagedEquipment.ToString());
            values.Add(TryComp<SmithQualityComponent>(item, out var quality) && quality.Applied
                ? ((int) quality.Quality).ToString(CultureInfo.InvariantCulture)
                : "none");
            return string.Join('\u001f', values);
        }

        var metadata = MetaData(item);
        values.Add(metadata.EntityName);
        values.Add(metadata.EntityDescription);

        return string.Join('\u001f', values);
    }

    private static string FormatStackName(string name, int? count)
    {
        if (count == null)
            return name;

        var value = count.Value;
        var word = value % 10 == 1 && value % 100 != 11
            ? "штука"
            : value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14)
                ? "штуки"
                : "штук";
        return $"{name} {value} {word}";
    }
}
