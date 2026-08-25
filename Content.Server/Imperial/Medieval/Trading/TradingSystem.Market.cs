using System.Globalization;
using System.Linq;
using Content.Shared.Imperial.Medieval.ArmorIntegrity;
using Content.Shared.Imperial.Medieval.Chemistry;
using Content.Shared.Imperial.Medieval.SmithingSystem;
using Content.Shared.Imperial.Medieval.SmithingSystem.Behaviours;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.MedievalMeleeResource.Components;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

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
                if (IsTrophy(item.ProductEntity) || IsAlchemyRecipe(item.ProductEntity))
                    continue;

                TradingCommodity commodity;
                if (!market.CommonCommodities.TryGetValue(item.ProductEntity, out var commodityId))
                {
                    var prototype = _prototypeManager.Index(item.ProductEntity);
                    prototype.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory);
                    commodityId = Guid.NewGuid();
                    commodity = new TradingCommodity
                    {
                        Id = commodityId,
                        Product = item.ProductEntity,
                        Sections = TradingMarketSection.Common,
                        StandardPrice = item.Cost,
                        Demand = config.InitialDemand,
                        Supply = config.InitialSupply,
                        BaselineStackCount = stack?.Count ?? 1,
                        HasStack = stack != null,
                        Permanent = true,
                        Signature = $"common:{item.ProductEntity.Id}",
                        DisplayName = FormatStackName(prototype.Name, stack?.Count),
                        Description = prototype.Description,
                    };
                    market.Commodities.Add(commodityId, commodity);
                    market.CommonCommodities.Add(item.ProductEntity, commodityId);
                }
                else
                {
                    commodity = market.Commodities[commodityId];
                }

                commodity.Categories.Add(guildType.ID);
            }
        }

        market.NextStep = _timing.CurTime + TimeSpan.FromSeconds(config.StepInterval);
        for (var index = 0; index < config.InitialSteps; index++)
        {
            RunMarketStep((uid, market), config, false);
        }
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
        TradingMarketConfigPrototype config,
        bool recoverDemand = true)
    {
        if (recoverDemand)
        {
            foreach (var commodity in market.Comp.Commodities.Values)
            {
                var recoveryScale = GetDemandShare(commodity.Demand + config.DemandRecovery, commodity.Supply);
                commodity.Demand += config.DemandRecovery * recoveryScale;
            }
        }

        EnsureGuildOffers(market, config);

        MatchAll(market, config);
    }

    private void EnsureGuildOffers(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodityId in market.Comp.CommonCommodities.Values)
        {
            if (market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
                EnsureGuildOffersForCommodity(market, commodity, config);
        }
    }

    private void EnsureGuildOffersForCommodity(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        if (!commodity.Permanent ||
            !market.Comp.CommonCommodities.TryGetValue(commodity.Product, out var commonId) ||
            commonId != commodity.Id)
        {
            return;
        }

        var candidates = market.Comp.Guilds
            .Where(guild => guild.Items.Any(item => item.ProductEntity == commodity.Product))
            .ToList();
        if (candidates.Count == 0)
            return;

        var current = market.Comp.Offers.Values.Count(offer =>
            offer.CommodityId == commodity.Id &&
            offer.ParticipantKind == TradingParticipantKind.Guild &&
            offer.Side == TradingOfferSide.Sell);
        var target = GetGuildOfferTarget(commodity, config);
        for (var index = current; index < target; index++)
        {
            var guild = _random.Pick(candidates);
            TryCreateGuildOffer(market, guild, commodity, config);
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
        return total > 0f ? demand / total : 1f;
    }

    private void TryCreateGuildOffer(
        Entity<TradingMarketComponent> market,
        Guild guild,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var price = GetGuildSellPrice(commodity, config);
        var highestBid = market.Comp.Offers.Values
            .Where(offer => offer.CommodityId == commodity.Id && offer.Side == TradingOfferSide.Buy)
            .Select(offer => offer.Price)
            .DefaultIfEmpty(0)
            .Max();
        price = Math.Clamp(Math.Max(price, Math.Max(2, highestBid + 1)), 1, config.MaximumPrice);

        AddOffer(market, new TradingMarketOffer
        {
            Id = Guid.NewGuid(),
            CommodityId = commodity.Id,
            Product = commodity.Product,
            Side = TradingOfferSide.Sell,
            ParticipantKind = TradingParticipantKind.Guild,
            ParticipantName = guild.Name,
            Price = price,
            GuildId = guild.Id,
            Sequence = market.Comp.NextSequence++,
        }, config);
    }

    private int GetGuildSellPrice(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var total = commodity.Demand + commodity.Supply;
        var pressure = total > 0f ? (commodity.Demand - commodity.Supply) / total : 0f;
        var center = 1f + pressure * config.PricePressure;
        var spread = config.PriceSpread / 2f;
        var noise = _random.NextFloat(0f, config.PriceNoise);
        var factor = Math.Clamp(center + spread + noise, config.MinimumPriceFactor, config.MaximumPriceFactor);
        return Math.Clamp((int) MathF.Round(commodity.StandardPrice * factor), 1, config.MaximumPrice);
    }

    private void AddOffer(
        Entity<TradingMarketComponent> market,
        TradingMarketOffer offer,
        TradingMarketConfigPrototype config)
    {
        if (offer.Side == TradingOfferSide.Sell && market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity))
        {
            offer.SupplyContribution = config.SupplyPlacementImpact * GetMarketImpactScale(commodity, config) *
                                       Math.Clamp((float) commodity.StandardPrice / offer.Price, 0.25f, 4f);
            commodity.Supply += offer.SupplyContribution;
        }

        market.Comp.Offers.Add(offer.Id, offer);
        if (offer.Pit is { } pit && TryComp<TradingComponent>(pit, out var trading))
            trading.MarketOffers.Add(offer.Id);
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
        commodity.Supply = Math.Max(
            0f,
            commodity.Supply - ask.SupplyContribution - config.SupplyTradeImpact * impactScale);
        var demandImpact = config.DemandTradeImpact * impactScale *
                           Math.Clamp((float) executionPrice / commodity.StandardPrice, 0.25f, 4f);
        commodity.Demand = Math.Max(0f, commodity.Demand - demandImpact);
        TryRemoveCommodity(market, commodity);
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

    internal bool TryResolveCommodityForItem(
        Entity<TradingMarketComponent> market,
        EntityUid item,
        int fallbackPrice,
        bool create,
        out TradingCommodity commodity)
    {
        commodity = default!;
        if (MetaData(item).EntityPrototype?.ID is not { } product || IsTrophy(product))
            return false;

        market.Comp.CommonCommodities.TryGetValue(product, out var commonId);
        TradingCommodity? common = null;
        var hasCommon = commonId != Guid.Empty && market.Comp.Commodities.TryGetValue(commonId, out common);
        var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
        var hasStack = stack != null;
        var isRecipe = HasComp<MedievalRandomChemistryRecipeComponent>(item);
        var isEquipment = HasComp<MedievalMeleeResourceComponent>(item) ||
                          HasComp<MedievalArmorIntegrityComponent>(item);
        var isDamagedEquipment = IsDamagedEquipment(item);
        var matchesCommon = !isRecipe &&
                            hasCommon &&
                            common != null &&
                            common.HasStack == hasStack &&
                            common.BaselineStackCount == stackCount &&
                            (!isEquipment || !isDamagedEquipment);

        if (matchesCommon)
        {
            commodity = common!;
            return true;
        }

        var signature = BuildItemSignature(item, product, stackCount);
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

        var config = _prototypeManager.Index(market.Comp.Config);
        var standardPrice = hasCommon && common != null
            ? common.StandardPrice
            : Math.Clamp(fallbackPrice, 1, config.MaximumPrice);
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

    private string BuildItemSignature(EntityUid item, EntProtoId product, int stackCount)
    {
        var metadata = MetaData(item);
        var values = new List<string>
        {
            product.Id,
            stackCount.ToString(CultureInfo.InvariantCulture),
            metadata.EntityName,
            metadata.EntityDescription,
        };

        if (TryComp<SmithQualityComponent>(item, out var quality))
        {
            values.Add(quality.Applied.ToString());
            values.Add(((int) quality.Quality).ToString(CultureInfo.InvariantCulture));
            values.Add(quality.Modifier.ToString("R", CultureInfo.InvariantCulture));
        }

        if (TryComp<MedievalMeleeResourceComponent>(item, out var weapon))
        {
            values.Add(weapon.Resource.ToString("R", CultureInfo.InvariantCulture));
            values.Add(weapon.MaxResource.ToString("R", CultureInfo.InvariantCulture));
        }

        if (TryComp<MedievalArmorIntegrityComponent>(item, out var armor))
        {
            var currentArmorHp = armor.CurrentArmorHP;
            var maxArmorHp = armor.MaxArmorHP;
            var containerArmorHp = armor.ContainerArmorHP;
            values.Add(currentArmorHp.ToString("R", CultureInfo.InvariantCulture));
            values.Add(maxArmorHp.ToString("R", CultureInfo.InvariantCulture));
            values.Add(containerArmorHp.ToString("R", CultureInfo.InvariantCulture));
        }

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
