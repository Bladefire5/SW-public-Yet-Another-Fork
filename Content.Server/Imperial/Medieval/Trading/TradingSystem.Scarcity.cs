using System.Linq;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem
{
    private const int ScarcityCalibrationIterations = 12;
    private const int ScarcityPriceSamples = 5;

    private sealed class ExpectedMarketOffer
    {
        public TradingOfferSide Side;
        public float Count;
        public int Price;
        public float Contribution;
    }

    private sealed class ExpectedMarketState
    {
        public float Demand;
        public float Supply;
        public List<ExpectedMarketOffer> Offers = new();
    }

    internal static (float Demand, float Supply) GetReputationScarcityInitialState(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var recoverySteps = GetReputationScarcityRecoveryStepTarget(commodity, config);
        var targetPriceFactor = Math.Max(1f, commodity.MinReputation);
        var targetMarketRatio = GetMarketRatioForLowestSellPriceFactor(commodity, config, targetPriceFactor);
        var lowerSupply = 0f;
        var upperSupply = Math.Max(config.InitialSupply, config.PriceSaturationFloor);

        for (var iteration = 0;
             iteration < ScarcityCalibrationIterations &&
             GetExpectedLowestSellPriceFactor(
                 commodity,
                 config,
                 GetReputationScarcityState(targetMarketRatio, upperSupply, config),
                 recoverySteps) < 1f;
             iteration++)
        {
            upperSupply *= 2f;
        }

        for (var iteration = 0; iteration < ScarcityCalibrationIterations; iteration++)
        {
            var supply = (lowerSupply + upperSupply) / 2f;
            var state = GetReputationScarcityState(targetMarketRatio, supply, config);
            if (GetExpectedLowestSellPriceFactor(commodity, config, state, recoverySteps) > 1f)
                upperSupply = supply;
            else
                lowerSupply = supply;
        }

        return GetReputationScarcityState(targetMarketRatio, (lowerSupply + upperSupply) / 2f, config);
    }

    private static float GetMarketRatioForLowestSellPriceFactor(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config,
        float targetPriceFactor)
    {
        var floor = Math.Max(float.Epsilon, config.PriceSaturationFloor);
        var baselineRatio = (Math.Max(0f, config.InitialDemand) + floor) /
                            (Math.Max(0f, config.InitialSupply) + floor);
        var slope = Math.Max(float.Epsilon, config.PriceRatioSlope);
        var targetCenter = targetPriceFactor - config.PriceSpread / 2f -
                           GetExpectedLowestPriceNoise(commodity, config);
        var relativeRatio = 1f + (targetCenter - 1f) / slope;
        return baselineRatio * Math.Max(float.Epsilon, relativeRatio);
    }

    private static (float Demand, float Supply) GetReputationScarcityState(
        float marketRatio,
        float supply,
        TradingMarketConfigPrototype config)
    {
        var floor = Math.Max(float.Epsilon, config.PriceSaturationFloor);
        var demand = marketRatio * (supply + floor) - floor;
        return (Math.Max(0f, demand), Math.Max(0f, supply));
    }

    internal static int GetReputationScarcityRecoveryStepTarget(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var duration = commodity.MinReputation * config.ReputationScarcityMinutesPerPoint * 60f;
        return Math.Max(1, (int) MathF.Round(duration / Math.Max(float.Epsilon, config.StepInterval)));
    }

    internal static float GetExpectedReputationScarcityPriceFactor(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config,
        float demand,
        float supply,
        int steps)
    {
        var state = CreateExpectedScarcityState(commodity, config, demand, supply);
        for (var step = 0; step < steps; step++)
        {
            RunExpectedMarketStep(state, commodity, config);
        }

        return GetExpectedLowestSellPriceFactor(state, commodity, config);
    }

    private static float GetExpectedLowestSellPriceFactor(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config,
        (float Demand, float Supply) state,
        int steps)
    {
        return GetExpectedReputationScarcityPriceFactor(
            commodity,
            config,
            state.Demand,
            state.Supply,
            steps);
    }

    private static ExpectedMarketState CreateExpectedScarcityState(
        TradingCommodity commodity,
        TradingMarketConfigPrototype config,
        float demand,
        float supply)
    {
        var state = new ExpectedMarketState
        {
            Demand = demand,
            Supply = supply,
        };
        var offerCount = GetGuildOfferTarget(commodity, config);
        AddExpectedOffers(
            state,
            commodity,
            TradingOfferSide.Sell,
            offerCount,
            config,
            false);
        AddExpectedOffers(
            state,
            commodity,
            TradingOfferSide.Buy,
            offerCount,
            config,
            false);
        return state;
    }

    private static void RunExpectedMarketStep(
        ExpectedMarketState state,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        CreateExpectedGuildOffers(state, commodity, TradingOfferSide.Sell, config);
        CreateExpectedGuildOffers(state, commodity, TradingOfferSide.Buy, config);
        RemoveExpectedUncompetitiveOffer(state, commodity, TradingOfferSide.Sell, config);
        RemoveExpectedUncompetitiveOffer(state, commodity, TradingOfferSide.Buy, config);
        MatchExpectedOffers(state, commodity, config);
        state.Offers.RemoveAll(offer => offer.Count <= float.Epsilon);
    }

    private static void CreateExpectedGuildOffers(
        ExpectedMarketState state,
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        var maximum = side == TradingOfferSide.Sell
            ? config.MaximumGuildSellOfferCount
            : config.MaximumGuildBuyOrderCount;
        var current = state.Offers.Where(offer => offer.Side == side).Sum(offer => offer.Count);
        var available = Math.Max(0f, maximum - current);
        var attempts = Math.Min(available, GetGuildOfferTarget(commodity, config));
        if (attempts <= 0f)
            return;

        var saturation = side == TradingOfferSide.Sell ? state.Supply : state.Demand;
        var initialSaturation = side == TradingOfferSide.Sell ? config.InitialSupply : config.InitialDemand;
        var baseChance = side == TradingOfferSide.Sell
            ? config.GuildSellOfferChance
            : config.GuildBuyOrderChance;
        var count = attempts * GetSaturationAdjustedCreationChance(baseChance, saturation, initialSaturation);
        AddExpectedOffers(state, commodity, side, count, config);
    }

    private static void AddExpectedOffers(
        ExpectedMarketState state,
        TradingCommodity commodity,
        TradingOfferSide side,
        float count,
        TradingMarketConfigPrototype config,
        bool applyPlacementImpact = true)
    {
        if (count <= float.Epsilon)
            return;

        var center = GetMarketPriceCenterFactor(state.Demand, state.Supply, config);
        var spread = side == TradingOfferSide.Sell ? config.PriceSpread / 2f : -config.PriceSpread / 2f;
        var impactScale = GetMarketImpactScale(commodity, config);
        var sampleCount = Math.Min(ScarcityPriceSamples, Math.Max(1, (int) MathF.Ceiling(count)));
        var countPerSample = count / sampleCount;

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var noise = config.PriceNoise * (2f * (sample + 0.5f) / sampleCount - 1f);
            var price = RoundMarketPrice(commodity.StandardPrice * (double) (center + spread + noise));
            var contribution = side == TradingOfferSide.Sell
                ? config.SupplyPlacementImpact * impactScale *
                  GetPriceImpactRatio((float) commodity.StandardPrice / price)
                : config.DemandPlacementImpact * impactScale *
                  GetPriceImpactRatio((float) price / Math.Max(1, commodity.StandardPrice));
            state.Offers.Add(new ExpectedMarketOffer
            {
                Side = side,
                Count = countPerSample,
                Price = price,
                Contribution = contribution,
            });

            if (applyPlacementImpact)
            {
                if (side == TradingOfferSide.Sell)
                    state.Supply += contribution * countPerSample;
                else
                    state.Demand += contribution * countPerSample;
            }
        }
    }

    private static void RemoveExpectedUncompetitiveOffer(
        ExpectedMarketState state,
        TradingCommodity commodity,
        TradingOfferSide side,
        TradingMarketConfigPrototype config)
    {
        var offers = state.Offers
            .Where(offer => offer.Side == side && offer.Count > float.Epsilon)
            .OrderBy(offer => side == TradingOfferSide.Sell ? -offer.Price : offer.Price)
            .ToList();
        if (offers.Count == 0)
            return;

        var standardPrice = Math.Max(1, commodity.StandardPrice);
        var priceScale = side == TradingOfferSide.Sell
            ? Math.Max(1f, (float) offers[0].Price / standardPrice)
            : Math.Max(1f, (float) standardPrice / Math.Max(1, offers[0].Price));
        var saturation = side == TradingOfferSide.Sell ? state.Supply : state.Demand;
        var initialSaturation = side == TradingOfferSide.Sell ? config.InitialSupply : config.InitialDemand;
        var baseChance = side == TradingOfferSide.Sell
            ? config.GuildSellOfferRemovalChance
            : config.GuildBuyOrderRemovalChance;
        var remaining = Math.Clamp(
            baseChance * priceScale *
            Math.Max(1f, Math.Max(0f, saturation) / Math.Max(float.Epsilon, initialSaturation)),
            0f,
            1f);

        foreach (var offer in offers)
        {
            var removed = Math.Min(remaining, offer.Count);
            RemoveExpectedOfferContribution(state, offer, removed);
            offer.Count -= removed;
            remaining -= removed;
            if (remaining <= float.Epsilon)
                break;
        }
    }

    private static void MatchExpectedOffers(
        ExpectedMarketState state,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        var asks = state.Offers
            .Where(offer => offer.Side == TradingOfferSide.Sell && offer.Count > float.Epsilon)
            .OrderBy(offer => offer.Price)
            .ToList();
        var bids = state.Offers
            .Where(offer => offer.Side == TradingOfferSide.Buy && offer.Count > float.Epsilon)
            .OrderByDescending(offer => offer.Price)
            .ToList();
        var askIndex = 0;
        var bidIndex = 0;
        var impactScale = GetMarketImpactScale(commodity, config);

        while (askIndex < asks.Count && bidIndex < bids.Count)
        {
            var ask = asks[askIndex];
            var bid = bids[bidIndex];
            if (bid.Price < ask.Price)
                break;

            var count = Math.Min(ask.Count, bid.Count);
            state.Supply = Math.Max(0f,
                state.Supply - (ask.Contribution + config.SupplyTradeImpact * impactScale) * count);
            state.Demand = Math.Max(0f,
                state.Demand - (bid.Contribution + config.DemandTradeImpact * impactScale) * count);
            ask.Count -= count;
            bid.Count -= count;
            if (ask.Count <= float.Epsilon)
                askIndex++;
            if (bid.Count <= float.Epsilon)
                bidIndex++;
        }
    }

    private static void RemoveExpectedOfferContribution(
        ExpectedMarketState state,
        ExpectedMarketOffer offer,
        float count)
    {
        if (offer.Side == TradingOfferSide.Sell)
            state.Supply = Math.Max(0f, state.Supply - offer.Contribution * count);
        else
            state.Demand = Math.Max(0f, state.Demand - offer.Contribution * count);
    }

    private static float GetExpectedLowestSellPriceFactor(
        ExpectedMarketState state,
        TradingCommodity commodity,
        TradingMarketConfigPrototype config)
    {
        return GetMarketPriceCenterFactor(state.Demand, state.Supply, config) + config.PriceSpread / 2f +
               GetExpectedLowestPriceNoise(commodity, config);
    }
}
