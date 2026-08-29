using Content.Server.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading;
using NUnit.Framework;

namespace Content.Tests.Server.Imperial.Medieval.Trading;

[TestFixture]
public sealed class TradingSystemMarketStabilizationTest
{
    [TestCase(100d, 100d, 1d)]
    [TestCase(200d, 100d, 2d)]
    [TestCase(50d, 100d, 2d)]
    [TestCase(125d, 100d, 1.25d)]
    [TestCase(80d, 100d, 1.25d)]
    public void DistanceRatioIsMultiplicativeAndSymmetric(
        double price,
        double referencePrice,
        double expected)
    {
        Assert.That(
            TradingSystem.GetDistanceRatio(price, referencePrice),
            Is.EqualTo(expected).Within(0.000001d));
    }

    [TestCase(100d, 100d, 1d)]
    [TestCase(200d, 100d, 0.5d)]
    [TestCase(300d, 100d, 0.25d)]
    [TestCase(25d, 100d, 0.125d)]
    [TestCase(1000d, 100d, 1d / 512d)]
    public void PriceWeightFallsExponentially(
        double price,
        double referencePrice,
        double expected)
    {
        Assert.That(
            TradingSystem.GetPriceWeight(price, referencePrice),
            Is.EqualTo(expected).Within(0.000001d));
    }

    [Test]
    public void WeightedAverageSuppressesDistantOrders()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([100, 1000], 100d),
            Is.EqualTo(101.75438596491227d).Within(0.000001d));
    }

    [Test]
    public void WeightedAveragePreservesExtremeSingleton()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([int.MaxValue], 1d),
            Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void WeightedAverageIgnoresNonPositivePrices()
    {
        Assert.That(
            TradingSystem.GetWeightedAveragePrice([-10, 0, 100], 100d),
            Is.EqualTo(100d));
    }

    [Test]
    public void MarketPriceUsesSeparateBookSides()
    {
        Assert.That(
            TradingSystem.GetMarketPrice([98], [108], 100d),
            Is.EqualTo(103d));
    }

    [Test]
    public void MarketPriceRequiresBothBookSides()
    {
        Assert.That(
            TradingSystem.GetMarketPrice([], [100], 100d),
            Is.NaN);
        Assert.That(
            TradingSystem.GetMarketPrice([100], [], 100d),
            Is.NaN);
    }

    [TestCase(100d, 100d, 0d)]
    [TestCase(125d, 100d, 0.1d)]
    [TestCase(80d, 100d, 0.1d)]
    [TestCase(150d, 100d, 0.2d)]
    [TestCase(200d, 100d, 0.4d)]
    [TestCase(300d, 100d, 0.8d)]
    [TestCase(350d, 100d, 1d)]
    public void InterventionChanceGrowsLinearly(
        double marketPrice,
        double referencePrice,
        double expected)
    {
        Assert.That(
            TradingSystem.GetInterventionChance(marketPrice, referencePrice, 0.4d),
            Is.EqualTo(expected).Within(0.000001d));
    }

    [TestCase(120d, 100d, 115d)]
    [TestCase(80d, 100d, 85d)]
    public void InternalOrderCorrectsOneQuarterOfDeviation(
        double marketPrice,
        double referencePrice,
        double expected)
    {
        Assert.That(
            TradingSystem.GetInternalOrderPrice(marketPrice, referencePrice, 0.25d),
            Is.EqualTo(expected).Within(0.000001d));
    }

    [Test]
    public void InitialGuildPricesAreSymmetric()
    {
        var buy = TradingSystem.GetInitialGuildOfferPrice(100d, TradingOfferSide.Buy, 0.12d, 0d);
        var sell = TradingSystem.GetInitialGuildOfferPrice(100d, TradingOfferSide.Sell, 0.12d, 0d);

        Assert.That(buy, Is.EqualTo(94d));
        Assert.That(sell, Is.EqualTo(106d));
        Assert.That((buy + sell) / 2d, Is.EqualTo(100d));
    }

    [Test]
    public void InitialGuildPriceDepthBuildsAnOrderedBook()
    {
        var nearBuy = TradingSystem.GetInitialGuildOfferPrice(100d, TradingOfferSide.Buy, 0.12d, 0d);
        var deepBuy = TradingSystem.GetInitialGuildOfferPrice(100d, TradingOfferSide.Buy, 0.12d, 0.18d);
        var nearSell = TradingSystem.GetInitialGuildOfferPrice(100d, TradingOfferSide.Sell, 0.12d, 0d);
        var deepSell = TradingSystem.GetInitialGuildOfferPrice(100d, TradingOfferSide.Sell, 0.12d, 0.18d);

        Assert.That(deepBuy, Is.LessThan(nearBuy));
        Assert.That(deepSell, Is.GreaterThan(nearSell));
    }

    [Test]
    public void InitialGuildRoundingKeepsCheapBookSidesApart()
    {
        var buy = TradingSystem.RoundInitialGuildOfferPrice(
            TradingSystem.GetInitialGuildOfferPrice(2d, TradingOfferSide.Buy, 0.12d, 0d),
            TradingOfferSide.Buy);
        var sell = TradingSystem.RoundInitialGuildOfferPrice(
            TradingSystem.GetInitialGuildOfferPrice(2d, TradingOfferSide.Sell, 0.12d, 0d),
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
}
