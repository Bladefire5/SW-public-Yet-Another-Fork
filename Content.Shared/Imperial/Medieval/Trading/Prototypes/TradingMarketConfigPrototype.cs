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
    public float StepInterval = 30f;

    [DataField]
    public int InitialSteps = 4;

    [DataField]
    public float InitialDemand = 50f;

    [DataField]
    public float InitialSupply = 25f;

    [DataField]
    public float DemandRecovery = 0.75f;

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

    [DataField]
    public float MinimumPriceFactor = 0.25f;

    [DataField]
    public float MaximumPriceFactor = 3f;

    [DataField]
    public int MaximumPrice = 1000000;
}
