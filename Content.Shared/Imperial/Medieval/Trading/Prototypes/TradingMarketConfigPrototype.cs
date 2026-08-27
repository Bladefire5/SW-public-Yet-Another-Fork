using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.Trading.Prototypes;

[Prototype]
public sealed partial class TradingMarketConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public HashSet<ProtoId<GuildTypePrototype>> GuildTypes = new();

    [DataField]
    public HashSet<ProtoId<TagPrototype>> BlockedTraderItemTags = new();

    [DataField]
    public float StepInterval = 30f;

    [DataField]
    public float InitialDemand = 50f;

    [DataField]
    public float InitialSupply = 25f;

    [DataField]
    public float DemandRecovery = 0.75f;

    [DataField]
    public float ReputationScarcityMinutesPerPoint = 3f;

    [DataField]
    public float ReputationScarcityDemandMultiplier = 2f;

    [DataField]
    public float ReputationScarcityPriceFactorPerPoint = 1f;

    [DataField]
    public int LiquidityReferencePrice = 5;

    [DataField]
    public int LiquidityReferenceOfferCount = 60;

    [DataField]
    public float LiquidityPriceExponent = 0.4f;

    [DataField]
    public int MinimumGuildOfferCount = 2;

    [DataField]
    public int MaximumGuildOfferCount = 120;

    [DataField]
    public float GuildSellOfferChance = 0.5f;

    [DataField]
    public float GuildBuyOrderChance = 0.25f;

    [DataField]
    public int MaximumGuildSellOfferCount = 200;

    [DataField]
    public int MaximumGuildBuyOrderCount = 100;

    [DataField]
    public float GuildSellOfferRemovalChance = 0.01f;

    [DataField]
    public float GuildBuyOrderRemovalChance = 0.01f;

    [DataField]
    public float GuildOfferMinimumLifetime = 600f;

    [DataField]
    public float GuildOfferMaximumLifetime = 1200f;

    [DataField]
    public float MarketImpactReferenceOfferCount = 6f;

    [DataField]
    public float SupplyPlacementImpact = 3f;

    [DataField]
    public float SupplyTradeImpact = 1f;

    [DataField]
    public float DemandTradeImpact = 2f;

    [DataField]
    public float PricePressure = 0.45f;

    [DataField]
    public float PriceSpread = 0.12f;

    [DataField]
    public float PriceNoise = 0.18f;
}
