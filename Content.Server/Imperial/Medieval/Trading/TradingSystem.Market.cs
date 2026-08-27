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
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Spawners;

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem
{
    internal void CreateMarket()
    {
        if (_market is { } previous && Exists(previous))
            QueueDel(previous);

        var uid = Spawn(null, MapCoordinates.Nullspace);
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
                if (item.ProductEntity is not { } product || IsTrophy(product) || IsAlchemyRecipe(product))
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
        SeedGuildSellOffers((uid, market), config);
    }

    private static void InitializeReputationScarcity(
        TradingMarketComponent market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodity in market.Commodities.Values)
        {
            if (commodity.MinReputation <= 0)
                continue;

            var demand = Math.Max(float.Epsilon,
                config.InitialDemand * config.ReputationScarcityDemandMultiplier);
            var startPressure = GetScarcityStartPressure(commodity, config);
            var startSupply = GetSupplyForPressure(demand, startPressure);
            var targetSupply = GetSupplyForPressure(
                demand,
                GetGoldenPricePressure(commodity, config));
            var duration = commodity.MinReputation * config.ReputationScarcityMinutesPerPoint * 60f;
            var steps = Math.Max(1, (int) MathF.Ceiling(duration / Math.Max(float.Epsilon, config.StepInterval)));
            var seedSupplyContribution = GetGuildOfferTarget(commodity, config) *
                                         config.SupplyPlacementImpact *
                                         GetMarketImpactScale(commodity, config) *
                                         GetPriceImpactRatio(
                                             1f / (1f + commodity.MinReputation *
                                                   config.ReputationScarcityPriceFactorPerPoint));

            commodity.Demand = demand;
            commodity.Supply = startSupply - seedSupplyContribution;
            commodity.ScarcityStepsRemaining = steps;
            commodity.ScarcitySupplyStep = (targetSupply - startSupply) / steps;
        }
    }

    private static void RecoverReputationScarcity(TradingCommodity commodity)
    {
        if (commodity.ScarcityStepsRemaining <= 0)
            return;

        commodity.Supply += commodity.ScarcitySupplyStep;
        commodity.ScarcityStepsRemaining--;
    }

    private static float GetScarcityStartPressure(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var initialFactor = 1f + commodity.MinReputation * config.ReputationScarcityPriceFactorPerPoint;
        return (initialFactor - 1f - config.PriceSpread / 2f - GetExpectedLowestPriceNoise(commodity, config)) /
               Math.Max(float.Epsilon, config.PricePressure);
    }

    private static float GetGoldenPricePressure(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        return -(config.PriceSpread / 2f + GetExpectedLowestPriceNoise(commodity, config)) /
               Math.Max(float.Epsilon, config.PricePressure);
    }

    private static float GetExpectedLowestPriceNoise(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var offerCount = Math.Max(1, GetGuildOfferTarget(commodity, config));
        return -config.PriceNoise * (offerCount - 1f) / (offerCount + 1f);
    }

    private static float GetSupplyForPressure(float demand, float pressure)
    {
        var denominator = 1f + pressure;
        if (MathHelper.CloseTo(denominator, 0f))
            denominator = denominator < 0f ? -float.Epsilon : float.Epsilon;

        return demand * (1f - pressure) / denominator;
    }

    private bool IsTrophy(EntProtoId product)
    {
        return _prototypeManager.TryIndex(product, out var prototype) &&
               prototype.HasComponent<MedievalCurrencyComponent>();
    }

    private bool IsAlchemyRecipe(EntProtoId product)
    {
        return _prototypeManager.TryIndex(product, out var prototype) &&
               prototype.HasComponent<MedievalRandomChemistryRecipeComponent>();
    }

    private void RunMarketStep(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        ExpireGuildOffers(market, config);

        foreach (var commodity in market.Comp.Commodities.Values)
        {
            RecoverReputationScarcity(commodity);

            var recoveryScale = GetDemandShare(commodity.Demand + config.DemandRecovery, commodity.Supply);
            commodity.Demand += config.DemandRecovery * recoveryScale;
        }

        CreateGuildActivity(market, config);
        MatchAll(market, config);
    }

    private void ExpireGuildOffers(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        var expired = market.Comp.Offers.Values
            .Where(offer => offer.ParticipantKind == TradingParticipantKind.Guild &&
                            offer.ExpiresAt <= _timing.CurTime)
            .Select(offer => offer.Id)
            .ToList();

        foreach (var offerId in expired)
        {
            RemoveOffer(market, offerId, true, config);
        }
    }

    private void SeedGuildSellOffers(
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

            for (var index = 0; index < GetGuildOfferTarget(commodity, config); index++)
            {
                TryCreateGuildOffer(
                    market,
                    _random.Pick(candidates),
                    commodity,
                    TradingOfferSide.Sell,
                    config);
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
            var expectedSellCount = GetGuildOfferTarget(commodity, config);
            var sellTarget = Math.Max(
                expectedSellCount,
                (int) MathF.Ceiling(expectedSellCount * config.GuildSellOfferCapacityMultiplier));
            var currentSells = GetGuildOfferCount(market, commodity, TradingOfferSide.Sell);
            CreateGuildOffers(
                market,
                commodity,
                candidates,
                TradingOfferSide.Sell,
                config.GuildSellOfferChance,
                Math.Max(0, sellTarget - currentSells),
                config);

            var buyTarget = Math.Max(1, (int) MathF.Ceiling(expectedSellCount * config.GuildBuyOrderShare));
            var currentBuys = GetGuildOfferCount(market, commodity, TradingOfferSide.Buy);
            CreateGuildOffers(
                market,
                commodity,
                candidates,
                TradingOfferSide.Buy,
                config.GuildBuyOrderChance,
                Math.Max(0, buyTarget - currentBuys),
                config);
        }
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
        float chance,
        int availableSlots,
        TradingMarketConfigPrototype config)
    {
        if (availableSlots <= 0 || candidates.Count == 0)
            return;

        var demandScale = Math.Max(0f, commodity.Demand) /
                          Math.Max(float.Epsilon, config.InitialDemand);
        var creationChance = Math.Clamp(chance * demandScale, 0f, 1f);

        for (var index = 0; index < availableSlots; index++)
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

    internal static float GetDemandShare(float demand, float supply)
    {
        var total = demand + supply;
        return total > 0f ? Math.Clamp(demand / total, 0f, 1f) : 1f;
    }

    private void TryCreateGuildOffer(
        Entity<TradingMarketComponent> market,
        Guild guild,
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
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
            ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(
                config.GuildOfferMinimumLifetime,
                Math.Max(config.GuildOfferMinimumLifetime, config.GuildOfferMaximumLifetime))),
        }, config);
    }

    private int GetGuildPrice(
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        var total = Math.Max(commodity.Demand + commodity.Supply, float.Epsilon);
        var pressure = (commodity.Demand - commodity.Supply) / total;
        var center = 1f + pressure * config.PricePressure;
        var spread = side == TradingOfferSide.Sell ? config.PriceSpread / 2f : -config.PriceSpread / 2f;
        var noise = _random.NextFloat(-config.PriceNoise, config.PriceNoise);
        var factor = Math.Max(config.MinimumPriceFactor, center + spread + noise);
        return RoundMarketPrice(commodity.StandardPrice * (double) factor);
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
        TradingMarketConfigPrototype config)
    {
        if (offer.Side == TradingOfferSide.Sell && market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity))
        {
            offer.SupplyContribution = config.SupplyPlacementImpact * GetMarketImpactScale(commodity, config) *
                                       GetPriceImpactRatio((float) commodity.StandardPrice / offer.Price);
            commodity.Supply += offer.SupplyContribution;
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
        ArchiveTrade(commodity, ask, bid);

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
        commodity.Supply -= ask.SupplyContribution + config.SupplyTradeImpact * impactScale;
        var demandImpact = config.DemandTradeImpact * impactScale *
                           Math.Clamp((float) executionPrice / commodity.StandardPrice, 0.25f, 4f);
        commodity.Demand = Math.Max(0f, commodity.Demand - demandImpact);
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
        TradingMarketOffer bid)
    {
        if (ask.ParticipantKind != TradingParticipantKind.Trader ||
            bid.ParticipantKind != TradingParticipantKind.Trader)
        {
            return;
        }

        var displayName = commodity.DisplayName;
        if (ask.Item is { } item && Exists(item))
        {
            var metadata = MetaData(item);
            var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : (int?) null;
            displayName = FormatStackName(metadata.EntityName, stackCount);
        }

        if (!ask.IsImmediate &&
            ask.Pit is { } sellerPit &&
            TryComp<TradingComponent>(sellerPit, out var seller))
        {
            seller.MarketArchive.Add(
                $"ваш лот {displayName} был куплен торговцем {bid.ParticipantName} за {ask.Price} ревентов");
        }

        if (!bid.IsImmediate &&
            bid.Pit is { } buyerPit &&
            TryComp<TradingComponent>(buyerPit, out var buyer))
        {
            buyer.MarketArchive.Add(
                $"Ваш заказ {displayName} был выполнен торговцем {ask.ParticipantName} за {bid.Price} ревентов");
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

        if (offer.Side == TradingOfferSide.Buy && offer.Pit is { } buyerId &&
            TryComp<TradingComponent>(buyerId, out var buyer))
        {
            buyer.Balance += offer.Price;
        }

        if (offer.Side == TradingOfferSide.Sell &&
            market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity))
        {
            commodity.Supply -= offer.SupplyContribution;
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
        var hasAppliedQuality = TryComp<SmithQualityComponent>(item, out var quality) && quality.Applied;
        var isEquipment = hasAppliedQuality ||
                          HasComp<MedievalMeleeResourceComponent>(item) ||
                          HasComp<MedievalArmorIntegrityComponent>(item);
        var isDamagedEquipment = !forceIntactEquipment && IsDamagedEquipment(item);
        var matchesCommon = !isRecipe &&
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
