using System;
using Content.Server.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Server.Imperial.Medieval.Trading;

[TestFixture]
[TestOf(typeof(TradingSystem))]
public sealed class TradingSystemMarketTest
{
    [Test]
    public void SignedMarketValuesMoveGuildPricesInRequiredDirections()
    {
        var config = new TradingMarketConfigPrototype();

        Assert.That(
            TradingSystem.GetGuildPriceCenterFactor(TradingOfferSide.Sell, 0f, -1f, config),
            Is.LessThan(1f));
        Assert.That(
            TradingSystem.GetGuildPriceCenterFactor(TradingOfferSide.Sell, 0f, 1f, config),
            Is.GreaterThan(1f));
        Assert.That(
            TradingSystem.GetGuildPriceCenterFactor(TradingOfferSide.Buy, -1f, 0f, config),
            Is.GreaterThan(1f));
        Assert.That(
            TradingSystem.GetGuildPriceCenterFactor(TradingOfferSide.Buy, 1f, 0f, config),
            Is.LessThan(1f));
    }

    [Test]
    public void OfferContributionUsesSignedGoldPriceComparison()
    {
        Assert.That(
            TradingSystem.GetOfferContributionFactor(TradingOfferSide.Sell, 100, 50),
            Is.GreaterThan(0f));
        Assert.That(
            TradingSystem.GetOfferContributionFactor(TradingOfferSide.Sell, 100, 200),
            Is.LessThan(0f));
        Assert.That(
            TradingSystem.GetOfferContributionFactor(TradingOfferSide.Buy, 100, 50),
            Is.LessThan(0f));
        Assert.That(
            TradingSystem.GetOfferContributionFactor(TradingOfferSide.Buy, 100, 200),
            Is.GreaterThan(0f));
    }

    [TestCase(30f, 6)]
    [TestCase(31f, 6)]
    [TestCase(60f, 3)]
    [TestCase(200f, 1)]
    public void ReputationScarcityStepCountNeverEndsBeforeConfiguredTime(float stepInterval, int expected)
    {
        var config = new TradingMarketConfigPrototype
        {
            StepInterval = stepInterval,
        };

        Assert.That(TradingSystem.GetReputationScarcityStepsPerPoint(config), Is.EqualTo(expected));
    }

    [TestCase(10, 60, 60, 10f)]
    [TestCase(10, 60, 30, 5.5f)]
    [TestCase(10, 60, 0, 1f)]
    [TestCase(15, 90, 90, 14.5f)]
    public void ReputationScarcityPriceMultiplierUsesRemainingSteps(
        int reputation,
        int initialSteps,
        int remainingSteps,
        float expected)
    {
        var commodity = new TradingCommodity
        {
            MinReputation = reputation,
            InitialScarcitySteps = initialSteps,
            RemainingScarcitySteps = remainingSteps,
        };

        Assert.That(TradingSystem.GetReputationScarcityPriceMultiplier(commodity), Is.EqualTo(expected));
    }

    [Test]
    public void LowestSellOfferMatchesPurchaseOrdering()
    {
        var commodityId = Guid.NewGuid();
        var ownPit = new EntityUid(1);
        var expected = new TradingMarketOffer
        {
            CommodityId = commodityId,
            Side = TradingOfferSide.Sell,
            ParticipantKind = TradingParticipantKind.Guild,
            Price = 2,
            Sequence = 1,
        };
        var offers = new[]
        {
            new TradingMarketOffer
            {
                CommodityId = commodityId,
                Side = TradingOfferSide.Sell,
                Pit = ownPit,
                Price = 1,
                Sequence = 0,
            },
            new TradingMarketOffer
            {
                CommodityId = commodityId,
                Side = TradingOfferSide.Sell,
                Pit = new EntityUid(2),
                Item = new EntityUid(3),
                Price = 2,
                Sequence = 2,
            },
            expected,
            new TradingMarketOffer
            {
                CommodityId = Guid.NewGuid(),
                Side = TradingOfferSide.Sell,
                Price = 1,
            },
            new TradingMarketOffer
            {
                CommodityId = commodityId,
                Side = TradingOfferSide.Buy,
                Price = 3,
            },
        };

        var actual = TradingSystem.GetLowestSellOffer(offers, commodityId, ownPit);

        Assert.That(actual, Is.SameAs(expected));
    }
}
