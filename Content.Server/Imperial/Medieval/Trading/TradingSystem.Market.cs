using System.Linq;
using System.Globalization;
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

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem
{
    internal void CreateMarket()
    {
        if (_market is { } previous && Exists(previous))
            QueueDel(previous);

        var uid = Spawn(null, MapCoordinates.Nullspace);
        var market = EnsureComp<TradingMarketComponent>(uid);
        market.Escrow = _containers.EnsureContainer<Robust.Shared.Containers.Container>(uid, TradingMarketComponent.EscrowContainerId);
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
                        GuildEligible = true,
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
        ExpireGuildOffers(market, config);

        if (recoverDemand)
        {
            foreach (var commodity in market.Comp.Commodities.Values)
            {
                commodity.Demand = Math.Min(config.MaximumIndicator, commodity.Demand + config.DemandRecovery);
            }
        }

        foreach (var guild in market.Comp.Guilds)
        {
            if (_random.NextFloat() > config.GuildActionChance)
                continue;

            if (market.Comp.Offers.Values.Count(offer => offer.GuildId == guild.Id) >= config.MaximumGuildOffers)
                continue;

            TryCreateGuildOffer(market, guild, config);
        }

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

        foreach (var id in expired)
        {
            RemoveOffer(market, id, true, config);
        }
    }

    private void TryCreateGuildOffer(
        Entity<TradingMarketComponent> market,
        Guild guild,
        TradingMarketConfigPrototype config)
    {
        var candidates = guild.Items
            .Select(item => item.ProductEntity)
            .Distinct()
            .Where(product => market.Comp.CommonCommodities.ContainsKey(product))
            .Select(product => market.Comp.Commodities[market.Comp.CommonCommodities[product]])
            .ToList();

        if (candidates.Count == 0)
            return;

        var sellWeight = candidates.Sum(commodity => 0.2f + commodity.Demand / config.MaximumIndicator * 1.5f);
        var buyWeight = candidates.Sum(commodity =>
            (0.2f + commodity.Demand / config.MaximumIndicator) *
            Math.Max(0.1f, 1.1f - commodity.Supply / config.MaximumIndicator));
        var side = _random.NextFloat(0f, sellWeight + buyWeight) < sellWeight
            ? TradingOfferSide.Sell
            : TradingOfferSide.Buy;

        var commodity = PickCommodity(candidates, side, config);
        if (side == TradingOfferSide.Buy)
            commodity = PickGuildBuyVariant(market, commodity);
        var price = GetGuildPrice(commodity, side, config);
        EntityUid? item = null;

        if (side == TradingOfferSide.Sell)
        {
            item = Spawn(commodity.Product, MapCoordinates.Nullspace);
            if (!_containers.Insert(item.Value, market.Comp.Escrow, force: true))
            {
                QueueDel(item.Value);
                return;
            }
        }

        AddOffer(market, new TradingMarketOffer
        {
            Id = Guid.NewGuid(),
            CommodityId = commodity.Id,
            Product = commodity.Product,
            Side = side,
            ParticipantKind = TradingParticipantKind.Guild,
            ParticipantName = guild.Name,
            Price = price,
            GuildId = guild.Id,
            Item = item,
            Sequence = market.Comp.NextSequence++,
            ExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(config.GuildOfferLifetime),
        }, config);
    }

    private TradingCommodity PickGuildBuyVariant(
        Entity<TradingMarketComponent> market,
        TradingCommodity baseline)
    {
        var variants = market.Comp.Commodities.Values
            .Where(commodity => commodity.Product == baseline.Product &&
                                commodity.GuildEligible &&
                                !commodity.Permanent &&
                                market.Comp.Offers.Values.Any(offer =>
                                    offer.CommodityId == commodity.Id &&
                                    offer.Side == TradingOfferSide.Sell))
            .ToList();
        if (variants.Count == 0)
            return baseline;

        var baselineWeight = 1f;
        var totalWeight = baselineWeight + variants.Sum(commodity => Math.Max(0.1f, commodity.QualityMultiplier));
        var roll = _random.NextFloat(0f, totalWeight);
        if (roll <= baselineWeight)
            return baseline;

        roll -= baselineWeight;
        foreach (var commodity in variants)
        {
            roll -= Math.Max(0.1f, commodity.QualityMultiplier);
            if (roll <= 0f)
                return commodity;
        }

        return variants[^1];
    }

    private TradingCommodity PickCommodity(
        List<TradingCommodity> candidates,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        var weights = candidates.Select(commodity => side == TradingOfferSide.Sell
                ? 0.2f + commodity.Demand / config.MaximumIndicator * 1.5f
                : (0.2f + commodity.Demand / config.MaximumIndicator) *
                  Math.Max(0.1f, 1.1f - commodity.Supply / config.MaximumIndicator))
            .ToList();
        var roll = _random.NextFloat(0f, weights.Sum());

        for (var index = 0; index < candidates.Count; index++)
        {
            roll -= weights[index];
            if (roll <= 0f)
                return candidates[index];
        }

        return candidates[^1];
    }

    private int GetGuildPrice(
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        var pressure = (commodity.Demand - commodity.Supply) / config.MaximumIndicator;
        var center = 1f + pressure * config.PricePressure;
        var spread = side == TradingOfferSide.Sell ? config.PriceSpread / 2f : -config.PriceSpread / 2f;
        var noise = _random.NextFloat(-config.PriceNoise, config.PriceNoise);
        var factor = Math.Clamp(center + spread + noise, config.MinimumPriceFactor, config.MaximumPriceFactor);
        var quality = side == TradingOfferSide.Buy ? commodity.QualityMultiplier : 1f;
        return Math.Clamp((int) MathF.Round(commodity.StandardPrice * quality * factor), 1, config.MaximumPrice);
    }

    private void AddOffer(
        Entity<TradingMarketComponent> market,
        TradingMarketOffer offer,
        TradingMarketConfigPrototype config)
    {
        if (offer.Side == TradingOfferSide.Sell && market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity))
        {
            offer.SupplyContribution = config.SupplyPlacementImpact *
                                       Math.Clamp((float) commodity.StandardPrice / offer.Price, 0.25f, 4f);
            commodity.Supply = Math.Min(config.MaximumIndicator, commodity.Supply + offer.SupplyContribution);
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
                    value.Price >= candidateAsk.Price && !IsSameParticipant(value, candidateAsk));
                if (candidateBid == null)
                    continue;

                ask = candidateAsk;
                bid = candidateBid;
                break;
            }

            if (ask == null || bid == null)
                return;

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

        RemoveOfferRecord(market, ask);
        RemoveOfferRecord(market, bid);

        commodity.Supply = Math.Max(0f, commodity.Supply - ask.SupplyContribution - config.SupplyTradeImpact);
        var demandImpact = config.DemandTradeImpact *
                           Math.Clamp((float) executionPrice / commodity.StandardPrice, 0.25f, 4f);
        commodity.Demand = Math.Max(0f, commodity.Demand - demandImpact);
        TryRemoveCommodity(market, commodity);
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
        var isEquipment = HasComp<MedievalMeleeResourceComponent>(item) || HasComp<MedievalArmorIntegrityComponent>(item);
        var pristine = isEquipment && IsPristineEquipment(item);

        if (!isRecipe && !isEquipment && hasCommon && common != null &&
            common.HasStack == hasStack && common.BaselineStackCount == stackCount)
        {
            commodity = common;
            return true;
        }

        var signature = BuildItemSignature(item, product, stackCount);
        var existing = market.Comp.Commodities.Values.FirstOrDefault(value =>
            !value.Permanent && value.Signature == signature);
        if (existing != null)
        {
            commodity = existing;
            return true;
        }

        if (!create)
            return false;

        var config = _prototypeManager.Index(market.Comp.Config);
        var qualityMultiplier = GetItemQualityMultiplier(item);
        var sections = TradingMarketSection.Unique;
        var guildEligible = false;
        if (!isRecipe && isEquipment && pristine && hasCommon && common != null)
        {
            sections |= TradingMarketSection.Common;
            guildEligible = true;
        }

        var standardPrice = hasCommon && common != null
            ? common.StandardPrice
            : Math.Clamp(fallbackPrice, 1, config.MaximumPrice);
        var metadata = MetaData(item);
        commodity = new TradingCommodity
        {
            Id = Guid.NewGuid(),
            Product = product,
            Sections = sections,
            StandardPrice = standardPrice,
            Demand = hasCommon && common != null ? common.Demand : config.InitialDemand,
            Supply = 0f,
            BaselineStackCount = stackCount,
            HasStack = hasStack,
            GuildEligible = guildEligible,
            QualityMultiplier = qualityMultiplier,
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

    private bool IsPristineEquipment(EntityUid item)
    {
        if (TryComp<MedievalMeleeResourceComponent>(item, out var weapon) && weapon.Resource <= 80f)
            return false;

        if (TryComp<MedievalArmorIntegrityComponent>(item, out var armor) &&
            (!MathHelper.CloseTo(armor.MaxArmorHP, armor.ContainerArmorHP) ||
             !MathHelper.CloseTo(armor.CurrentArmorHP, armor.ContainerArmorHP)))
        {
            return false;
        }

        return HasComp<MedievalMeleeResourceComponent>(item) || HasComp<MedievalArmorIntegrityComponent>(item);
    }

    private float GetItemQualityMultiplier(EntityUid item)
    {
        return TryComp<SmithQualityComponent>(item, out var quality)
            ? ItemQualityDurabilityMultipliers.Get(quality.Quality)
            : 1f;
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
