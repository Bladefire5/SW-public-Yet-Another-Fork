using System.Linq;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem
{
    private const double PriceWeightBase = 0.5d;

    private void CreateGuildInterventions(
        Entity<TradingMarketComponent> market,
        TradingMarketConfigPrototype config)
    {
        foreach (var commodityId in market.Comp.CommonCommodities.Values)
        {
            if (market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
                TryCreateGuildIntervention(market, commodity, config);
        }
    }

    private void TryCreateGuildIntervention(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var referencePrice = GetGuildReferencePrice(commodity);
        var offers = market.Comp.Offers.Values
            .Where(offer => offer.CommodityId == commodity.Id)
            .ToList();
        var marketPrice = GetMarketPrice(
            offers.Where(offer => offer.Side == TradingOfferSide.Buy).Select(offer => offer.Price),
            offers.Where(offer => offer.Side == TradingOfferSide.Sell).Select(offer => offer.Price),
            referencePrice);
        if (double.IsNaN(marketPrice) || marketPrice == referencePrice)
            return;

        var side = marketPrice > referencePrice
            ? TradingOfferSide.Sell
            : TradingOfferSide.Buy;
        var maximumOffers = side == TradingOfferSide.Sell
            ? config.MaximumGuildSellOfferCount
            : config.MaximumGuildBuyOrderCount;
        if (maximumOffers <= 0)
            return;

        var candidates = GetGuildCandidates(market, commodity);
        if (candidates.Count == 0)
            return;

        var chance = GetInterventionChance(
            marketPrice,
            referencePrice,
            config.InterventionChanceScale);
        if (_random.NextDouble() >= chance)
            return;

        var price = RoundMarketPrice(GetInternalOrderPrice(
            marketPrice,
            referencePrice,
            config.InterventionCorrectionStrength));
        if (GetGuildOfferCount(market, commodity, side) >= maximumOffers)
        {
            var replaceable = GetReplaceableGuildOffer(market, commodity, side);
            if (replaceable == null || !IsMoreCompetitivePrice(price, replaceable.Price, side))
                return;

            RemoveOffer(market, replaceable.Id, false);
        }

        CreateGuildOffer(
            market,
            _random.Pick(candidates),
            commodity,
            side,
            price);
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

    private static TradingMarketOffer? GetReplaceableGuildOffer(
        Entity<TradingMarketComponent> market,
        TradingCommodity commodity,
        TradingOfferSide side)
    {
        var offers = market.Comp.Offers.Values.Where(offer =>
            offer.CommodityId == commodity.Id &&
            offer.ParticipantKind == TradingParticipantKind.Guild &&
            offer.Side == side);
        return side == TradingOfferSide.Sell
            ? offers.OrderByDescending(offer => offer.Price).ThenBy(offer => offer.Sequence).FirstOrDefault()
            : offers.OrderBy(offer => offer.Price).ThenBy(offer => offer.Sequence).FirstOrDefault();
    }

    internal static bool IsMoreCompetitivePrice(
        int candidatePrice,
        int currentPrice,
        TradingOfferSide side)
    {
        return side == TradingOfferSide.Sell
            ? candidatePrice < currentPrice
            : candidatePrice > currentPrice;
    }

    private static double GetGuildReferencePrice(TradingCommodity commodity)
    {
        return Math.Max(1d, commodity.StandardPrice) * GetReputationScarcityPriceMultiplier(commodity);
    }

    internal static double GetDistanceRatio(double price, double referencePrice)
    {
        if (!double.IsFinite(price) ||
            !double.IsFinite(referencePrice) ||
            price <= 0d ||
            referencePrice <= 0d)
        {
            throw new ArgumentOutOfRangeException();
        }

        return Math.Max(price / referencePrice, referencePrice / price);
    }

    internal static double GetPriceWeight(double price, double referencePrice)
    {
        return Math.Pow(PriceWeightBase, GetDistanceRatio(price, referencePrice) - 1d);
    }

    internal static double GetWeightedAveragePrice(
        IEnumerable<int> prices,
        double referencePrice)
    {
        if (!double.IsFinite(referencePrice) || referencePrice <= 0d)
            throw new ArgumentOutOfRangeException(nameof(referencePrice));

        var weightedPrices = new List<(int Price, double LogWeight)>();
        var maximumLogWeight = double.NegativeInfinity;
        foreach (var price in prices)
        {
            if (price <= 0)
                continue;

            var logWeight = (GetDistanceRatio(price, referencePrice) - 1d) * Math.Log(PriceWeightBase);
            weightedPrices.Add((price, logWeight));
            maximumLogWeight = Math.Max(maximumLogWeight, logWeight);
        }

        if (weightedPrices.Count == 0)
            return double.NaN;

        var weightedPriceSum = 0d;
        var weightSum = 0d;
        foreach (var (price, logWeight) in weightedPrices)
        {
            var weight = Math.Exp(logWeight - maximumLogWeight);
            weightedPriceSum += price * weight;
            weightSum += weight;
        }

        return weightedPriceSum / weightSum;
    }

    internal static double GetMarketPrice(
        IEnumerable<int> buyPrices,
        IEnumerable<int> sellPrices,
        double referencePrice)
    {
        var bidPrice = GetWeightedAveragePrice(buyPrices, referencePrice);
        var askPrice = GetWeightedAveragePrice(sellPrices, referencePrice);
        if (double.IsNaN(bidPrice) || double.IsNaN(askPrice))
            return double.NaN;

        return (bidPrice + askPrice) / 2d;
    }

    internal static double GetInterventionChance(
        double marketPrice,
        double referencePrice,
        double chanceScale)
    {
        var ratio = GetDistanceRatio(marketPrice, referencePrice);
        return Math.Clamp(Math.Max(0d, chanceScale) * (ratio - 1d), 0d, 1d);
    }

    internal static double GetInternalOrderPrice(
        double marketPrice,
        double referencePrice,
        double correctionStrength)
    {
        if (!double.IsFinite(marketPrice) ||
            !double.IsFinite(referencePrice) ||
            marketPrice <= 0d ||
            referencePrice <= 0d)
        {
            throw new ArgumentOutOfRangeException();
        }

        var strength = Math.Clamp(correctionStrength, 0d, 1d);
        return marketPrice + strength * (referencePrice - marketPrice);
    }

    internal static double GetInitialGuildOfferPrice(
        double referencePrice,
        TradingOfferSide side,
        double spread,
        double depth)
    {
        if (!double.IsFinite(referencePrice) || referencePrice <= 0d)
            throw new ArgumentOutOfRangeException(nameof(referencePrice));

        var halfSpread = Math.Clamp(spread, 0d, 2d) / 2d;
        var priceDepth = Math.Clamp(depth, 0d, Math.Max(0d, 1d - halfSpread));
        var factor = side == TradingOfferSide.Sell
            ? 1d + halfSpread + priceDepth
            : 1d - halfSpread - priceDepth;
        return referencePrice * factor;
    }

    internal static double GetInitialGuildOfferDepth(
        int index,
        int count,
        double maximumDepth)
    {
        if (index < 0 || count <= 0 || index >= count)
            throw new ArgumentOutOfRangeException();

        if (count == 1)
            return 0d;

        return Math.Max(0d, maximumDepth) * index / (count - 1d);
    }

    internal static int RoundInitialGuildOfferPrice(double price, TradingOfferSide side)
    {
        var rounded = side == TradingOfferSide.Sell
            ? Math.Ceiling(price)
            : Math.Floor(price);
        return RoundMarketPrice(rounded);
    }
}
