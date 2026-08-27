using Content.Server.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using NUnit.Framework;

namespace Content.Tests.Server.Imperial.Medieval.Trading;

[TestFixture]
[TestOf(typeof(TradingSystem))]
public sealed class TradingSystemMarketTest
{
    [Test]
    public void GuildSellOfferCreationChanceIncreasesWhenSupplyFalls()
    {
        Assert.That(TradingSystem.GetGuildSellOfferCreationChance(0.1f, 50f, 25f), Is.EqualTo(0.05f));
        Assert.That(TradingSystem.GetGuildSellOfferCreationChance(0.1f, 25f, 25f), Is.EqualTo(0.1f));
        Assert.That(TradingSystem.GetGuildSellOfferCreationChance(0.1f, 12.5f, 25f), Is.EqualTo(0.2f));
        Assert.That(TradingSystem.GetGuildSellOfferCreationChance(0.1f, 0f, 25f), Is.EqualTo(0.2f));
    }

    [Test]
    public void GuildBuyOrderCreationChanceIncreasesWhenDemandFalls()
    {
        Assert.That(TradingSystem.GetGuildBuyOrderCreationChance(0.05f, 100f, 50f), Is.EqualTo(0.025f));
        Assert.That(TradingSystem.GetGuildBuyOrderCreationChance(0.05f, 50f, 50f), Is.EqualTo(0.05f));
        Assert.That(TradingSystem.GetGuildBuyOrderCreationChance(0.05f, 25f, 50f), Is.EqualTo(0.1f));
        Assert.That(TradingSystem.GetGuildBuyOrderCreationChance(0.05f, 0f, 50f), Is.EqualTo(0.1f));
    }

    [TestCase(12, 10)]
    [TestCase(220, 10)]
    [TestCase(1600, 10)]
    [TestCase(100, 15)]
    public void ReputationScarcityRecoversAtConfiguredExpectedTime(int price, int reputation)
    {
        var config = new TradingMarketConfigPrototype();
        var commodity = new TradingCommodity
        {
            StandardPrice = price,
            MinReputation = reputation,
        };
        var target = TradingSystem.GetReputationScarcityRecoveryStepTarget(commodity, config);
        var state = TradingSystem.GetReputationScarcityInitialState(commodity, config);
        var initialFactor = TradingSystem.GetExpectedReputationScarcityPriceFactor(
            commodity,
            config,
            state.Demand,
            state.Supply,
            0);
        var factor = TradingSystem.GetExpectedReputationScarcityPriceFactor(
            commodity,
            config,
            state.Demand,
            state.Supply,
            target);

        Assert.That(state.Supply, Is.Zero);
        Assert.That(initialFactor, Is.GreaterThan(1f));
        Assert.That(factor, Is.EqualTo(1f).Within(0.01f));
    }
}
