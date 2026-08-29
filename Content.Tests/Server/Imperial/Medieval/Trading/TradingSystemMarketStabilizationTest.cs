using System;
using Content.Server.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading;
using NUnit.Framework;

namespace Content.Tests.Server.Imperial.Medieval.Trading;

[TestFixture]
public sealed class TradingSystemMarketStabilizationTest
{
    [TestCase(100f, 100f, 1f)]
    [TestCase(200f, 100f, 2f)]
    [TestCase(50f, 100f, 2f)]
    [TestCase(125f, 100f, 1.25f)]
    [TestCase(80f, 100f, 1.25f)]
    public void DistanceRatioIsMultiplicativeAndSymmetric(
        float price,
        float referencePrice,
        float expected)
    {
        Assert.That(
            TradingSystem.GetDistanceRatio(price, referencePrice),
            Is.EqualTo(expected).Within(0.000001f));
    }

    [TestCase(100f, 100f, 1f)]
    [TestCase(200f, 100f, 0.5f)]
    [TestCase(300f, 100f, 0.25f)]
    [TestCase(25f, 100f, 0.125f)]
    [TestCase(1000f, 100f, 1f / 512f)]
    public void PriceWeightFallsExponentially(
        float price,
        float referencePrice,
        float expected)
    {
        Assert.That(
            TradingSystem.GetPriceWeight(price, referencePrice, 0.5f),
            Is.EqualTo(expected).Within(0.000001f));
    }

    [TestCase(0.25f, 0.25f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(0.75f, 0.75f)]
    [TestCase(1f, 1f)]
    public void PriceWeightBaseControlsFalloff(float priceWeightBase, float expected)
    {
        Assert.That(
            TradingSystem.GetPriceWeight(200f, 100f, priceWeightBase),
            Is.EqualTo(expected).Within(0.000001f));
    }

    [TestCase(0f)]
    [TestCase(-0.5f)]
    [TestCase(1.5f)]
    public void PriceWeightBaseRequiresValidRange(float priceWeightBase)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TradingSystem.GetPriceWeight(100f, 100f, priceWeightBase));
    }

    [Test]
    public void WeightedAverageSuppressesDistantOrders()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([100, 1000], 100f, 0.5f),
            Is.EqualTo(101.75438596491227f).Within(0.000001f));
    }

    [Test]
    public void WeightedAveragePreservesExtremeSingleton()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([int.MaxValue], 1f, 0.5f),
            Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void WeightedAverageIgnoresNonPositivePrices()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([-10, 0, 100], 100f, 0.5f),
            Is.EqualTo(100f));
    }

    [Test]
    public void WeightedAverageReflectsOrderCount()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([100, 100, 200], 100f, 0.5f),
            Is.EqualTo(120f).Within(0.000001f));
    }

    [Test]
    public void MarketPriceUsesSeparateBookSides()
    {
        Assert.That(
            TradingSystem.GetMarketPrice([98], [108], 100f, 0.5f),
            Is.EqualTo(103f));
    }

    [Test]
    public void MarketPriceRequiresBothBookSides()
    {
        Assert.That(
            TradingSystem.GetMarketPrice([], [100], 100f, 0.5f),
            Is.NaN);
        Assert.That(
            TradingSystem.GetMarketPrice([100], [], 100f, 0.5f),
            Is.NaN);
    }

    [Test]
    public void BuyingCheapestSellRaisesMarketPrice()
    {
        var before = TradingSystem.GetMarketPrice([98], [102, 108], 100f, 0.5f);
        var after = TradingSystem.GetMarketPrice([98], [108], 100f, 0.5f);

        Assert.That(after, Is.GreaterThan(before));
    }

    [Test]
    public void SellingIntoHighestBuyLowersMarketPrice()
    {
        var before = TradingSystem.GetMarketPrice([92, 98], [102], 100f, 0.5f);
        var after = TradingSystem.GetMarketPrice([92], [102], 100f, 0.5f);

        Assert.That(after, Is.LessThan(before));
    }

    [TestCase(100f, 100f, 0f)]
    [TestCase(125f, 100f, 0.1f)]
    [TestCase(80f, 100f, 0.1f)]
    [TestCase(150f, 100f, 0.2f)]
    [TestCase(200f, 100f, 0.4f)]
    [TestCase(300f, 100f, 0.8f)]
    [TestCase(350f, 100f, 1f)]
    public void InterventionChanceGrowsLinearly(
        float marketPrice,
        float referencePrice,
        float expected)
    {
        Assert.That(
            TradingSystem.GetInterventionChance(marketPrice, referencePrice, 0.4f),
            Is.EqualTo(expected).Within(0.000001f));
    }

    [TestCase(120f, 100f, 115f)]
    [TestCase(80f, 100f, 85f)]
    public void InternalOrderCorrectsOneQuarterOfDeviation(
        float marketPrice,
        float referencePrice,
        float expected)
    {
        Assert.That(
            TradingSystem.GetInternalOrderPrice(marketPrice, referencePrice, 0.25f),
            Is.EqualTo(expected).Within(0.000001f));
    }

    [TestCase(120f, 100f, TradingOfferSide.Sell)]
    [TestCase(80f, 100f, TradingOfferSide.Buy)]
    [TestCase(100f, 100f, null)]
    public void InterventionSideOpposesMarketDeviation(
        float marketPrice,
        float referencePrice,
        TradingOfferSide? expected)
    {
        Assert.That(
            TradingSystem.GetInterventionSide(marketPrice, referencePrice),
            Is.EqualTo(expected));
    }

    [Test]
    public void InitialGuildPricesAreSymmetric()
    {
        var buy = TradingSystem.GetInitialGuildOfferPrice(100f, TradingOfferSide.Buy, 0.12f, 0f);
        var sell = TradingSystem.GetInitialGuildOfferPrice(100f, TradingOfferSide.Sell, 0.12f, 0f);

        Assert.That(buy, Is.EqualTo(94f));
        Assert.That(sell, Is.EqualTo(106f));
        Assert.That((buy + sell) / 2f, Is.EqualTo(100f));
    }

    [Test]
    public void InitialGuildPriceDepthBuildsAnOrderedBook()
    {
        var nearBuy = TradingSystem.GetInitialGuildOfferPrice(100f, TradingOfferSide.Buy, 0.12f, 0f);
        var deepBuy = TradingSystem.GetInitialGuildOfferPrice(100f, TradingOfferSide.Buy, 0.12f, 0.18f);
        var nearSell = TradingSystem.GetInitialGuildOfferPrice(100f, TradingOfferSide.Sell, 0.12f, 0f);
        var deepSell = TradingSystem.GetInitialGuildOfferPrice(100f, TradingOfferSide.Sell, 0.12f, 0.18f);

        Assert.That(deepBuy, Is.LessThan(nearBuy));
        Assert.That(deepSell, Is.GreaterThan(nearSell));
    }

    [Test]
    public void InitialGuildRoundingKeepsCheapBookSidesApart()
    {
        var buy = TradingSystem.RoundInitialGuildOfferPrice(
            TradingSystem.GetInitialGuildOfferPrice(2f, TradingOfferSide.Buy, 0.12f, 0f),
            TradingOfferSide.Buy);
        var sell = TradingSystem.RoundInitialGuildOfferPrice(
            TradingSystem.GetInitialGuildOfferPrice(2f, TradingOfferSide.Sell, 0.12f, 0f),
            TradingOfferSide.Sell);

        Assert.That(buy, Is.EqualTo(1));
        Assert.That(sell, Is.EqualTo(3));
    }

    [TestCase(90, 100, TradingOfferSide.Sell, true)]
    [TestCase(100, 100, TradingOfferSide.Sell, false)]
    [TestCase(110, 100, TradingOfferSide.Sell, false)]
    [TestCase(110, 100, TradingOfferSide.Buy, true)]
    [TestCase(100, 100, TradingOfferSide.Buy, false)]
    [TestCase(90, 100, TradingOfferSide.Buy, false)]
    public void FullBookOnlyAcceptsMoreCompetitiveIntervention(
        int candidatePrice,
        int currentPrice,
        TradingOfferSide side,
        bool expected)
    {
        Assert.That(
            TradingSystem.IsMoreCompetitivePrice(candidatePrice, currentPrice, side),
            Is.EqualTo(expected));
    }

    [TestCase(1, 0, 100, 100, TradingOfferSide.Sell)]
    [TestCase(0, 1, 100, 100, TradingOfferSide.Buy)]
    [TestCase(0, 0, 100, 100, TradingOfferSide.Sell)]
    [TestCase(0, 0, 100, 0, TradingOfferSide.Buy)]
    [TestCase(1, 1, 100, 100, null)]
    public void MissingBookSideIsRecovered(
        int buyCount,
        int sellCount,
        int maximumBuyCount,
        int maximumSellCount,
        TradingOfferSide? expected)
    {
        Assert.That(
            TradingSystem.GetMissingBookSide(
                buyCount,
                sellCount,
                maximumBuyCount,
                maximumSellCount),
            Is.EqualTo(expected));
    }

    [Test]
    public void NormalInterventionMovesMarketTowardReference()
    {
        const float referencePrice = 100f;
        var current = TradingSystem.GetMarketPrice([98], [108], referencePrice, 0.5f);
        var updated = TradingSystem.GetMarketPriceAfterIntervention(
            [98],
            [108],
            referencePrice,
            0.5f,
            TradingOfferSide.Sell,
            102);

        Assert.That(
            TradingSystem.MovesMarketTowardReference(current, updated, referencePrice),
            Is.True);
    }

    [Test]
    public void CrossedBookRejectsPositiveFeedback()
    {
        const float referencePrice = 100f;
        var current = TradingSystem.GetMarketPrice([200], [100], referencePrice, 0.5f);
        var updated = TradingSystem.GetMarketPriceAfterIntervention(
            [200],
            [100],
            referencePrice,
            0.5f,
            TradingOfferSide.Sell,
            138);

        Assert.That(
            TradingSystem.MovesMarketTowardReference(current, updated, referencePrice),
            Is.False);
    }

    [Test]
    public void FullBookReplacementRejectsPositiveFeedback()
    {
        const float referencePrice = 100f;
        var current = TradingSystem.GetMarketPrice([500], [100], referencePrice, 0.5f);
        var updated = TradingSystem.GetMarketPriceAfterIntervention(
            [500],
            [100],
            referencePrice,
            0.5f,
            TradingOfferSide.Sell,
            250,
            100);

        Assert.That(
            TradingSystem.MovesMarketTowardReference(current, updated, referencePrice),
            Is.False);
    }

    [Test]
    public void ScarcityRaisesOnlyGuildSellReference()
    {
        var commodity = new TradingCommodity
        {
            StandardPrice = 100,
            MinReputation = 10,
            InitialScarcitySteps = 10,
            RemainingScarcitySteps = 10,
        };

        Assert.That(TradingSystem.GetGuildReferencePrice(commodity), Is.EqualTo(100f));
        Assert.That(TradingSystem.GetGuildSellReferencePrice(commodity), Is.EqualTo(1000f));
    }

    [Test]
    public void ScarcityFloorDoesNotRaiseGuildBuyPrice()
    {
        Assert.That(
            TradingSystem.GetGuildInterventionPrice(
                80f,
                100f,
                0.25f,
                TradingOfferSide.Buy,
                1000f),
            Is.EqualTo(85));
        Assert.That(
            TradingSystem.GetGuildInterventionPrice(
                120f,
                100f,
                0.25f,
                TradingOfferSide.Sell,
                1000f),
            Is.EqualTo(1000));
    }

    [Test]
    public void EqualGuildOffersFromDifferentGuildsMatch()
    {
        var ask = CreateOffer(TradingOfferSide.Sell, TradingParticipantKind.Guild, 100, Guid.NewGuid());
        var bid = CreateOffer(TradingOfferSide.Buy, TradingParticipantKind.Guild, 100, Guid.NewGuid());

        Assert.That(TradingSystem.CanMatchOffers(ask, bid), Is.True);
    }

    [Test]
    public void OffersFromSameGuildDoNotMatch()
    {
        var guildId = Guid.NewGuid();
        var ask = CreateOffer(TradingOfferSide.Sell, TradingParticipantKind.Guild, 100, guildId);
        var bid = CreateOffer(TradingOfferSide.Buy, TradingParticipantKind.Guild, 200, guildId);

        Assert.That(TradingSystem.CanMatchOffers(ask, bid), Is.False);
    }

    [Test]
    public void TraderSellMatchesGuildBuy()
    {
        var ask = CreateOffer(TradingOfferSide.Sell, TradingParticipantKind.Trader, 100, null);
        var bid = CreateOffer(TradingOfferSide.Buy, TradingParticipantKind.Guild, 100, Guid.NewGuid());

        Assert.That(TradingSystem.CanMatchOffers(ask, bid), Is.True);
    }

    private static TradingMarketOffer CreateOffer(
        TradingOfferSide side,
        TradingParticipantKind participantKind,
        int price,
        Guid? guildId)
    {
        return new TradingMarketOffer
        {
            Side = side,
            ParticipantKind = participantKind,
            Price = price,
            GuildId = guildId,
        };
    }
}
