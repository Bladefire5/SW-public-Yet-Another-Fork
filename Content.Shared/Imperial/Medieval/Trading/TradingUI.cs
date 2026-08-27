using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.Store;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Trading;

[Serializable, NetSerializable]
public enum TradingUiKey : byte
{
    Key
}

[Flags, Serializable, NetSerializable]
public enum TradingMarketSection : byte
{
    Common = 1,
    Unique = 2
}

[Serializable, NetSerializable]
public enum TradingOfferSide : byte
{
    Buy,
    Sell
}

[Serializable, NetSerializable]
public enum TradingParticipantKind : byte
{
    Guild,
    Trader
}

[Serializable, NetSerializable]
public sealed class TradingMarketItemState
{
    public Guid CommodityId;
    public EntProtoId ProductEntity;
    public TradingMarketSection Sections;
    public string DisplayName;
    public string Description;
    public int? StackCount;
    public NetEntity? PreviewEntity;
    public bool IsCommonBaseline;
    public bool HasStack;
    public int BaselineStackCount;
    public bool IsDamagedEquipment;
    public float Demand;
    public float Supply;
    public int? LowestSellPrice;
    public int? HighestBuyPrice;
    public int SellOfferCount;
    public int BuyOfferCount;
    public HashSet<ProtoId<GuildTypePrototype>> Categories;

    public TradingMarketItemState(
        Guid commodityId,
        EntProtoId productEntity,
        TradingMarketSection sections,
        string displayName,
        string description,
        int? stackCount,
        NetEntity? previewEntity,
        bool isCommonBaseline,
        bool hasStack,
        int baselineStackCount,
        bool damagedEquipment,
        float demand,
        float supply,
        int? lowestSellPrice,
        int? highestBuyPrice,
        int sellOfferCount,
        int buyOfferCount,
        HashSet<ProtoId<GuildTypePrototype>> categories)
    {
        CommodityId = commodityId;
        ProductEntity = productEntity;
        Sections = sections;
        DisplayName = displayName;
        Description = description;
        StackCount = stackCount;
        PreviewEntity = previewEntity;
        IsCommonBaseline = isCommonBaseline;
        HasStack = hasStack;
        BaselineStackCount = baselineStackCount;
        IsDamagedEquipment = damagedEquipment;
        Demand = demand;
        Supply = supply;
        LowestSellPrice = lowestSellPrice;
        HighestBuyPrice = highestBuyPrice;
        SellOfferCount = sellOfferCount;
        BuyOfferCount = buyOfferCount;
        Categories = categories;
    }
}

[Serializable, NetSerializable]
public sealed class TradingMarketOfferState
{
    public Guid Id;
    public Guid CommodityId;
    public EntProtoId ProductEntity;
    public TradingOfferSide Side;
    public TradingParticipantKind ParticipantKind;
    public string ParticipantName;
    public int Price;
    public bool IsOwn;
    public string DisplayName;
    public NetEntity? PreviewEntity;

    public TradingMarketOfferState(
        Guid id,
        Guid commodityId,
        EntProtoId productEntity,
        TradingOfferSide side,
        TradingParticipantKind participantKind,
        string participantName,
        int price,
        bool isOwn,
        string displayName,
        NetEntity? previewEntity)
    {
        Id = id;
        CommodityId = commodityId;
        ProductEntity = productEntity;
        Side = side;
        ParticipantKind = participantKind;
        ParticipantName = participantName;
        Price = price;
        IsOwn = isOwn;
        DisplayName = displayName;
        PreviewEntity = previewEntity;
    }
}

[Serializable, NetSerializable]
public sealed class TradingStoredItemState
{
    public NetEntity Item;
    public EntProtoId ProductEntity;
    public string DisplayName;

    public TradingStoredItemState(NetEntity item, EntProtoId productEntity, string displayName)
    {
        Item = item;
        ProductEntity = productEntity;
        DisplayName = displayName;
    }
}

[Serializable, NetSerializable]
public sealed class TradingUpdateState : BoundUserInterfaceState
{
    public List<TradingMarketItemState> Items;
    public List<TradingMarketOfferState> Offers;
    public List<TradingStoredItemState> StoredItems;
    public List<string> Archive;
    public int Balance;
    public ProtoId<CurrencyPrototype> Currency;

    public TradingUpdateState(
        List<TradingMarketItemState> items,
        List<TradingMarketOfferState> offers,
        List<TradingStoredItemState> storedItems,
        List<string> archive,
        int balance,
        ProtoId<CurrencyPrototype> currency)
    {
        Items = items;
        Offers = offers;
        StoredItems = storedItems;
        Archive = archive;
        Balance = balance;
        Currency = currency;
    }
}

[Serializable, NetSerializable]
public sealed class TradingRequestUpdateInterfaceMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class TradingBuyMessage(Guid commodityId) : BoundUserInterfaceMessage
{
    public Guid CommodityId = commodityId;
}

[Serializable, NetSerializable]
public sealed class TradingSellMessage(Guid commodityId) : BoundUserInterfaceMessage
{
    public Guid CommodityId = commodityId;
}

[Serializable, NetSerializable]
public sealed class TradingBuyOfferMessage(Guid offerId) : BoundUserInterfaceMessage
{
    public Guid OfferId = offerId;
}

[Serializable, NetSerializable]
public sealed class TradingSellOfferMessage(Guid offerId) : BoundUserInterfaceMessage
{
    public Guid OfferId = offerId;
}

[Serializable, NetSerializable]
public sealed class TradingSelectCommodityMessage(Guid commodityId) : BoundUserInterfaceMessage
{
    public Guid CommodityId = commodityId;
}

[Serializable, NetSerializable]
public sealed class TradingSelectOfferMessage(Guid offerId) : BoundUserInterfaceMessage
{
    public Guid OfferId = offerId;
}

[Serializable, NetSerializable]
public sealed class TradingCreateSellOfferMessage(int price) : BoundUserInterfaceMessage
{
    public int Price = price;
}

[Serializable, NetSerializable]
public sealed class TradingCreateBuyOfferMessage(Guid commodityId, int price) : BoundUserInterfaceMessage
{
    public Guid CommodityId = commodityId;
    public int Price = price;
}

[Serializable, NetSerializable]
public sealed class TradingCreateBuyOfferFromHeldMessage(int price) : BoundUserInterfaceMessage
{
    public int Price = price;
}

[Serializable, NetSerializable]
public sealed class TradingCancelOfferMessage(Guid offerId) : BoundUserInterfaceMessage
{
    public Guid OfferId = offerId;
}

[Serializable, NetSerializable]
public sealed class TradingCollectStoredItemMessage(NetEntity item) : BoundUserInterfaceMessage
{
    public NetEntity Item = item;
}

[Serializable, NetSerializable]
public sealed class TradingRequestWithdrawMessage(int amount) : BoundUserInterfaceMessage
{
    public int Amount = amount;
}
