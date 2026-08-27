using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Trading;

[RegisterComponent]
public sealed partial class TradingMarketComponent : Component
{
    public Dictionary<Guid, TradingCommodity> Commodities = new();
    public Dictionary<EntProtoId, Guid> CommonCommodities = new();
    public List<Guild> Guilds = new();
    public Dictionary<Guid, TradingMarketOffer> Offers = new();
    public long NextSequence;
    public ProtoId<TradingMarketConfigPrototype> Config = "MedievalMarket";
}

public sealed class TradingCommodity
{
    public Guid Id;
    public EntProtoId Product;
    public TradingMarketSection Sections;
    public int StandardPrice;
    public float Demand;
    public float Supply;
    public int MinReputation;
    public int BaselineStackCount = 1;
    public bool HasStack;
    public bool Permanent;
    public bool IsDamagedEquipment;
    public string Signature = string.Empty;
    public string DisplayName = string.Empty;
    public string Description = string.Empty;
    public HashSet<ProtoId<GuildTypePrototype>> Categories = new();
}

public sealed class TradingMarketOffer
{
    public Guid Id;
    public Guid CommodityId;
    public EntProtoId Product;
    public TradingOfferSide Side;
    public TradingParticipantKind ParticipantKind;
    public string ParticipantName = string.Empty;
    public int Price;
    public Guid? GuildId;
    public EntityUid? Pit;
    public EntityUid? ImmediateRecipient;
    public EntityUid? Item;
    public string ListedItemName = string.Empty;
    public bool IsImmediate;
    public long Sequence;
    public float SupplyContribution;
    public float DemandContribution;
}

[RegisterComponent]
public sealed partial class TradingMarketViewerComponent : Component
{
    public HashSet<EntityUid> VisibleItems = new();
    public Guid? SelectedCommodity;
    public Guid? SelectedOffer;
}
