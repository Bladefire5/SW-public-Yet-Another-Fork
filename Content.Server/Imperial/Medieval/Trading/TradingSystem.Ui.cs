using System.Linq;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;

    private void InitializeUi()
    {
        Subs.BuiEvents<TradingComponent>(TradingUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<BoundUIClosedEvent>(OnUiClosed);
        });
        SubscribeLocalEvent<TradingComponent, TradingRequestUpdateInterfaceMessage>(OnRequestUpdate);
        SubscribeLocalEvent<TradingComponent, TradingBuyMessage>(OnBuyRequest);
        SubscribeLocalEvent<TradingComponent, TradingSellMessage>(OnSellRequest);
        SubscribeLocalEvent<TradingComponent, TradingBuyOfferMessage>(OnBuyOfferRequest);
        SubscribeLocalEvent<TradingComponent, TradingSellOfferMessage>(OnSellOfferRequest);
        SubscribeLocalEvent<TradingComponent, TradingSelectCommodityMessage>(OnSelectCommodity);
        SubscribeLocalEvent<TradingComponent, TradingSelectOfferMessage>(OnSelectOffer);
        SubscribeLocalEvent<TradingComponent, TradingCreateSellOfferMessage>(OnCreateSellOffer);
        SubscribeLocalEvent<TradingComponent, TradingCreateBuyOfferMessage>(OnCreateBuyOffer);
        SubscribeLocalEvent<TradingComponent, TradingCreateBuyOfferFromHeldMessage>(OnCreateBuyOfferFromHeld);
        SubscribeLocalEvent<TradingComponent, TradingCancelOfferMessage>(OnCancelOffer);
        SubscribeLocalEvent<TradingComponent, TradingCollectStoredItemMessage>(OnCollectStoredItem);
        SubscribeLocalEvent<TradingComponent, TradingRequestWithdrawMessage>(OnRequestWithdraw);
        SubscribeLocalEvent<TradingComponent, BoundUserInterfaceMessageAttempt>(OnUiMessageAttempt);
    }

    public void CloseUi(EntityUid uid, TradingComponent? component = null)
    {
        if (Resolve(uid, ref component))
            _ui.CloseUi(uid, TradingUiKey.Key);
    }

    public void UpdateUserInterface(EntityUid user, EntityUid store, TradingComponent? component = null)
    {
        if (!Resolve(store, ref component) || !TryGetMarket(out var market))
            return;

        var isOwner = IsTradingPitOwner(user, component);
        component.MarketOffers.RemoveWhere(id => !market.Comp.Offers.ContainsKey(id));
        component.StoredMarketItems.RemoveAll(item => !Exists(item));
        var viewer = EnsureComp<TradingMarketViewerComponent>(user);
        if (!isOwner &&
            (viewer.SelectedCommodity is not { } selected ||
             !market.Comp.Commodities.TryGetValue(selected, out var selectedItem) ||
             (selectedItem.Sections & TradingMarketSection.Unique) == 0))
        {
            viewer.SelectedCommodity = market.Comp.Commodities.Values
                .FirstOrDefault(commodity => (commodity.Sections & TradingMarketSection.Unique) != 0)?.Id;
            viewer.SelectedOffer = null;
        }

        RefreshVisibleMarketItems(user, store, component, market, isOwner);
        var selectedCommodity = viewer.SelectedCommodity;
        var visibleOffers = market.Comp.Offers.Values.ToList();
        var offersByCommodity = visibleOffers.ToLookup(offer => offer.CommodityId);

        var items = market.Comp.Commodities.Values
            .Where(commodity => isOwner || (commodity.Sections & TradingMarketSection.Unique) != 0)
            .Select(commodity =>
            {
                var commodityOffers = offersByCommodity[commodity.Id].ToList();
                var asks = commodityOffers.Where(offer => offer.Side == TradingOfferSide.Sell).ToList();
                var bids = commodityOffers
                    .Where(offer => offer.Side == TradingOfferSide.Buy && (!isOwner || offer.Pit != store))
                    .ToList();
                var traderBids = bids
                    .Where(offer => offer.ParticipantKind == TradingParticipantKind.Trader)
                    .ToList();
                var lowestSellOffer = GetLowestSellOffer(
                    commodityOffers,
                    commodity.Id,
                    isOwner ? store : null);
                var preview = lowestSellOffer?.Item;
                var displayName = commodity.DisplayName;
                var description = commodity.Description;
                int? stackCount = commodity.HasStack ? commodity.BaselineStackCount : null;
                var damagedEquipment = commodity.IsDamagedEquipment;
                NetEntity? previewEntity = null;
                if (preview is { } previewItem && Exists(previewItem))
                {
                    var metadata = MetaData(previewItem);
                    stackCount = TryComp<StackComponent>(previewItem, out var stack) ? stack.Count : null;
                    displayName = FormatStackName(metadata.EntityName, stackCount);
                    description = metadata.EntityDescription;
                    damagedEquipment = IsDamagedEquipment(previewItem);
                    previewEntity = GetNetEntity(previewItem);
                }

                return new TradingMarketItemState(
                    commodity.Id,
                    commodity.Product,
                    commodity.Sections,
                    displayName,
                    description,
                    stackCount,
                    previewEntity,
                    commodity.Permanent,
                    commodity.HasStack,
                    commodity.BaselineStackCount,
                    damagedEquipment,
                    commodity.Demand,
                    commodity.Supply,
                    lowestSellOffer?.Price,
                    traderBids.Count == 0 ? null : traderBids.Max(offer => offer.Price),
                    asks.Count,
                    bids.Count,
                    new HashSet<ProtoId<GuildTypePrototype>>(commodity.Categories));
            })
            .ToList();

        var offers = visibleOffers
            .Where(offer => offer.CommodityId == selectedCommodity || isOwner && offer.Pit == store)
            .OrderBy(offer => offer.Product.Id)
            .ThenBy(offer => offer.Side)
            .ThenBy(offer => offer.Price)
            .Select(offer => CreateOfferState(market, store, offer, isOwner))
            .ToList();

        var storedItems = (isOwner ? component.StoredMarketItems : [])
            .Where(item => Exists(item) && MetaData(item).EntityPrototype != null)
            .Select(item =>
            {
                var metadata = MetaData(item);
                var product = metadata.EntityPrototype!.ID;
                var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : (int?) null;
                return new TradingStoredItemState(
                    GetNetEntity(item),
                    product,
                    FormatStackName(metadata.EntityName, stackCount));
            })
            .ToList();

        _ui.ServerSendUiMessage(
            store,
            TradingUiKey.Key,
            new TradingUpdateInterfaceMessage(
                new TradingUpdateState(
                    items,
                    offers,
                    storedItems,
                    isOwner ? new List<string>(component.MarketArchive) : [],
                    isOwner ? component.Balance : 0,
                    component.Currency,
                    isOwner)),
            user);
    }

    private void OnUiMessageAttempt(
        Entity<TradingComponent> pit,
        ref BoundUserInterfaceMessageAttempt args)
    {
        if (IsTradingPitOwner(args.Actor, pit.Comp) ||
            args.Message is OpenBoundInterfaceMessage or
                TradingRequestUpdateInterfaceMessage or
                TradingSelectCommodityMessage or
                TradingSelectOfferMessage)
        {
            return;
        }

        args.Cancel();
    }

    private TradingMarketOfferState CreateOfferState(
        Entity<TradingMarketComponent> market,
        EntityUid store,
        TradingMarketOffer offer,
        bool isOwner)
    {
        var displayName = market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity)
            ? commodity.DisplayName
            : offer.Product.Id;
        NetEntity? preview = null;
        if (offer.Item is { } item && Exists(item))
        {
            var metadata = MetaData(item);
            var stackCount = TryComp<StackComponent>(item, out var stack) ? stack.Count : (int?) null;
            displayName = FormatStackName(metadata.EntityName, stackCount);
            preview = GetNetEntity(item);
        }

        return new TradingMarketOfferState(
            offer.Id,
            offer.CommodityId,
            offer.Product,
            offer.Side,
            offer.ParticipantKind,
            offer.ParticipantName,
            offer.Price,
            isOwner && offer.Pit == store,
            displayName,
            preview);
    }

    private void UpdateAllInterfaces(Entity<TradingMarketComponent> market)
    {
        var query = EntityQueryEnumerator<TradingComponent>();
        while (query.MoveNext(out var pit, out var component))
        {
            foreach (var user in _ui.GetActors(pit, TradingUiKey.Key))
            {
                UpdateUserInterface(user, pit, component);
            }
        }
    }

    private void OnRequestUpdate(EntityUid uid, TradingComponent component, TradingRequestUpdateInterfaceMessage args)
    {
        UpdateUserInterface(args.Actor, uid, component);
    }

    private void OnSelectCommodity(EntityUid uid, TradingComponent component, TradingSelectCommodityMessage args)
    {
        if (!TryGetMarket(out var market) ||
            !market.Comp.Commodities.TryGetValue(args.CommodityId, out var commodity) ||
            !IsTradingPitOwner(args.Actor, component) &&
            (commodity.Sections & TradingMarketSection.Unique) == 0)
        {
            UpdateUserInterface(args.Actor, uid, component);
            return;
        }

        var viewer = EnsureComp<TradingMarketViewerComponent>(args.Actor);
        viewer.SelectedCommodity = args.CommodityId;
        viewer.SelectedOffer = null;
        UpdateUserInterface(args.Actor, uid, component);
    }

    private void OnSelectOffer(EntityUid uid, TradingComponent component, TradingSelectOfferMessage args)
    {
        if (!TryGetMarket(out var market))
            return;

        var isOwner = IsTradingPitOwner(args.Actor, component);
        var viewer = EnsureComp<TradingMarketViewerComponent>(args.Actor);
        if (!market.Comp.Offers.TryGetValue(args.OfferId, out var offer) ||
            !market.Comp.Commodities.TryGetValue(offer.CommodityId, out var commodity) ||
            !isOwner && (commodity.Sections & TradingMarketSection.Unique) == 0 ||
            !CanSelectOffer(offer, viewer.SelectedCommodity))
        {
            viewer.SelectedOffer = null;
            UpdateUserInterface(args.Actor, uid, component);
            return;
        }

        viewer.SelectedOffer = offer.Id;
        UpdateUserInterface(args.Actor, uid, component);
    }

    private bool CanSelectOffer(
        TradingMarketOffer offer,
        Guid? selectedCommodity)
    {
        if (offer.CommodityId != selectedCommodity)
            return false;

        if (offer.Side == TradingOfferSide.Buy ||
            offer.ParticipantKind == TradingParticipantKind.Guild)
            return true;

        return offer.Item is { } item &&
               Exists(item);
    }

    private void OnUiOpened(EntityUid uid, TradingComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(args.Actor, uid, component);
    }

    private void OnUiClosed(EntityUid uid, TradingComponent component, BoundUIClosedEvent args)
    {
        ClearVisibleMarketItems(args.Actor);
    }

    private void OnBuyRequest(EntityUid uid, TradingComponent component, TradingBuyMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) || !TryGetMarket(out var market))
            return;

        var ask = GetLowestSellOffer(market.Comp.Offers.Values, msg.CommodityId, uid);
        if (ask == null || component.Balance < ask.Price)
            return;

        if (!market.Comp.Commodities.TryGetValue(msg.CommodityId, out var commodity) ||
            !CreateTraderBuyOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                commodity,
                ask.Price,
                msg.Actor))
        {
            return;
        }

        MatchCommodity(market, commodity, _prototypeManager.Index(market.Comp.Config));
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-purchase-success");
        UpdateAllInterfaces(market);
    }

    private void OnSellRequest(EntityUid uid, TradingComponent component, TradingSellMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) || !TryGetMarket(out var market))
            return;

        var bid = market.Comp.Offers.Values
            .Where(offer => offer.CommodityId == msg.CommodityId &&
                            offer.Side == TradingOfferSide.Buy &&
                            offer.ParticipantKind == TradingParticipantKind.Trader &&
                            offer.Pit != uid)
            .OrderByDescending(offer => offer.Price)
            .ThenBy(offer => offer.Sequence)
            .FirstOrDefault();
        if (bid == null ||
            !market.Comp.Commodities.TryGetValue(msg.CommodityId, out var commodity) ||
            (commodity.Sections & TradingMarketSection.Unique) == 0 ||
            !TryFindInventoryItem(msg.Actor, market, commodity, out var item))
        {
            return;
        }

        if (!TryCreateTraderSellOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                item,
                msg.Actor,
                bid.Price,
                out var commodityId,
                true) ||
            commodityId != msg.CommodityId)
        {
            return;
        }

        MatchCommodity(market, commodity, _prototypeManager.Index(market.Comp.Config));
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-sale-success");
        UpdateAllInterfaces(market);
    }

    private void OnBuyOfferRequest(EntityUid uid, TradingComponent component, TradingBuyOfferMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) ||
            !TryGetMarket(out var market) ||
            !market.Comp.Offers.TryGetValue(msg.OfferId, out var ask) ||
            ask.Side != TradingOfferSide.Sell ||
            ask.Pit == uid ||
            component.Balance < ask.Price ||
            !market.Comp.Commodities.TryGetValue(ask.CommodityId, out var commodity) ||
            !CreateTraderBuyOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                commodity,
                ask.Price,
                msg.Actor,
                out var bid))
        {
            return;
        }

        CompleteTrade(market, commodity, ask, bid, _prototypeManager.Index(market.Comp.Config));
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-purchase-success");
        UpdateAllInterfaces(market);
    }

    private void OnSellOfferRequest(EntityUid uid, TradingComponent component, TradingSellOfferMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) ||
            !TryGetMarket(out var market) ||
            !market.Comp.Offers.TryGetValue(msg.OfferId, out var bid) ||
            bid.Side != TradingOfferSide.Buy ||
            bid.ParticipantKind != TradingParticipantKind.Trader ||
            bid.Pit == uid ||
            !market.Comp.Commodities.TryGetValue(bid.CommodityId, out var commodity) ||
            !_hands.TryGetActiveItem(msg.Actor, out var held) ||
            held is not { } item)
        {
            return;
        }

        var requiredStackCount = commodity.HasStack ? commodity.BaselineStackCount : (int?) null;
        StackComponent? heldStack = null;
        if (requiredStackCount is { } required &&
            (!TryComp(item, out heldStack) || heldStack.Count < required))
        {
            return;
        }

        if (!TryResolveCommodityForItem(
                market,
                item,
                bid.Price,
                false,
                out var heldCommodity,
                requiredStackCount) ||
            heldCommodity.Id != commodity.Id)
        {
            return;
        }

        var tradeItem = item;
        EntityUid? splitItem = null;
        var originalStackCount = heldStack?.Count;
        if (requiredStackCount is { } splitCount && heldStack != null && heldStack.Count > splitCount)
        {
            splitItem = _stack.Split(item, splitCount, Transform(uid).Coordinates, heldStack);
            if (splitItem == null ||
                !TryResolveCommodityForItem(market, splitItem.Value, bid.Price, false, out var splitCommodity) ||
                splitCommodity.Id != commodity.Id)
            {
                RestoreSplitStack(item, heldStack, originalStackCount, splitItem);
                return;
            }

            tradeItem = splitItem.Value;
        }

        if (!TryCreateTraderSellOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                tradeItem,
                msg.Actor,
                bid.Price,
                out var commodityId,
                out var ask,
                true))
        {
            RestoreSplitStack(item, heldStack, originalStackCount, splitItem);
            return;
        }

        if (commodityId != bid.CommodityId)
        {
            RemoveOffer(
                market,
                ask.Id,
                true,
                _prototypeManager.Index(market.Comp.Config),
                msg.Actor);
            RestoreSplitStack(item, heldStack, originalStackCount, splitItem);
            return;
        }

        CompleteTrade(market, commodity, ask, bid, _prototypeManager.Index(market.Comp.Config));
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-sale-success");
        UpdateAllInterfaces(market);
    }

    private void RestoreSplitStack(
        EntityUid originalItem,
        StackComponent? originalStack,
        int? originalCount,
        EntityUid? splitItem)
    {
        if (originalStack != null && originalCount != null && Exists(originalItem))
            _stack.SetCount(originalItem, originalCount.Value, originalStack);

        if (splitItem is { } split && Exists(split))
            QueueDel(split);
    }

    private void OnCreateSellOffer(EntityUid uid, TradingComponent component, TradingCreateSellOfferMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) || !TryGetMarket(out var market))
            return;

        if (!_hands.TryGetActiveItem(msg.Actor, out var held) ||
            held is not { } item ||
            !TryCreateTraderSellOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                item,
                msg.Actor,
                msg.Price,
                out var commodityId))
        {
            _popup.PopupCursor(
                Loc.GetString("trading-ui-invalid-sell-offer"),
                msg.Actor,
                PopupType.SmallCaution);
            return;
        }

        if (!market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
            return;

        MatchCommodity(market, commodity, _prototypeManager.Index(market.Comp.Config));
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-sell-offer-created");
        UpdateAllInterfaces(market);
    }

    private void OnCreateBuyOffer(EntityUid uid, TradingComponent component, TradingCreateBuyOfferMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) ||
            !TryGetMarket(out var market) ||
            !market.Comp.Commodities.TryGetValue(msg.CommodityId, out var commodity) ||
            !CreateTraderBuyOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                commodity,
                msg.Price))
        {
            return;
        }

        MatchCommodity(market, commodity, _prototypeManager.Index(market.Comp.Config));
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-buy-order-created");
        UpdateAllInterfaces(market);
    }

    private void OnCreateBuyOfferFromHeld(
        EntityUid uid,
        TradingComponent component,
        TradingCreateBuyOfferFromHeldMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) || !TryGetMarket(out var market))
            return;

        var config = _prototypeManager.Index(market.Comp.Config);
        if (!_hands.TryGetActiveItem(msg.Actor, out var held) ||
            held is not { } item ||
            msg.Price <= 0 ||
            component.Balance < msg.Price)
        {
            return;
        }

        if (!TryResolveCommodityForItem(
                market,
                item,
                msg.Price,
                true,
                out var commodity,
                forceIntactEquipment: true) ||
            !CreateTraderBuyOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                commodity,
                msg.Price))
        {
            return;
        }

        MatchCommodity(market, commodity, config);
        ShowTradingSuccess(msg.Actor, uid, component, "trading-ui-buy-order-created");
        UpdateAllInterfaces(market);
    }

    private void OnCancelOffer(EntityUid uid, TradingComponent component, TradingCancelOfferMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) ||
            !TryGetMarket(out var market) ||
            !market.Comp.Offers.TryGetValue(msg.OfferId, out var offer) ||
            offer.Pit != uid)
        {
            return;
        }

        RemoveOffer(
            market,
            msg.OfferId,
            true,
            _prototypeManager.Index(market.Comp.Config),
            msg.Actor);
        UpdateAllInterfaces(market);
    }

    private void OnCollectStoredItem(EntityUid uid, TradingComponent component, TradingCollectStoredItemMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component))
            return;

        var item = GetEntity(msg.Item);
        if (!Exists(item) || !component.StoredMarketItems.Contains(item))
            return;

        DeliverItem(uid, component, item, msg.Actor);
        UpdateUserInterface(msg.Actor, uid, component);
    }

    internal bool CreateTraderBuyOffer(
        Entity<TradingMarketComponent> market,
        Entity<TradingComponent> pit,
        string participantName,
        TradingCommodity commodity,
        int price,
        EntityUid? immediateRecipient = null)
    {
        return CreateTraderBuyOffer(
            market,
            pit,
            participantName,
            commodity,
            price,
            immediateRecipient,
            out _);
    }

    private bool CreateTraderBuyOffer(
        Entity<TradingMarketComponent> market,
        Entity<TradingComponent> pit,
        string participantName,
        TradingCommodity commodity,
        int price,
        EntityUid? immediateRecipient,
        out TradingMarketOffer offer)
    {
        offer = default!;
        var config = _prototypeManager.Index(market.Comp.Config);
        if (price < 0 ||
            (price == 0 && immediateRecipient == null) ||
            !CanTradeProduct(commodity.Product, config) ||
            pit.Comp.Balance < price ||
            !market.Comp.Commodities.ContainsKey(commodity.Id))
        {
            return false;
        }

        pit.Comp.Balance -= price;
        offer = new TradingMarketOffer
        {
            Id = Guid.NewGuid(),
            CommodityId = commodity.Id,
            Product = commodity.Product,
            Side = TradingOfferSide.Buy,
            ParticipantKind = TradingParticipantKind.Trader,
            ParticipantName = participantName,
            Price = price,
            Pit = pit.Owner,
            ImmediateRecipient = immediateRecipient,
            IsImmediate = immediateRecipient != null,
            Sequence = market.Comp.NextSequence++,
        };
        AddOffer(market, offer, config);
        return true;
    }

    internal bool TryCreateTraderSellOffer(
        Entity<TradingMarketComponent> market,
        Entity<TradingComponent> pit,
        string participantName,
        EntityUid sourceItem,
        EntityUid seller,
        int price,
        out Guid commodityId,
        bool immediate = false)
    {
        return TryCreateTraderSellOffer(
            market,
            pit,
            participantName,
            sourceItem,
            seller,
            price,
            out commodityId,
            out _,
            immediate);
    }

    private bool TryCreateTraderSellOffer(
        Entity<TradingMarketComponent> market,
        Entity<TradingComponent> pit,
        string participantName,
        EntityUid sourceItem,
        EntityUid seller,
        int price,
        out Guid commodityId,
        out TradingMarketOffer offer,
        bool immediate = false)
    {
        commodityId = default;
        offer = default!;
        var config = _prototypeManager.Index(market.Comp.Config);
        if (price < 0 ||
            !TryResolveCommodityForItem(market, sourceItem, price, true, out var commodity) ||
            MetaData(sourceItem).EntityPrototype?.ID is not { } product)
        {
            return false;
        }

        var destination = _containers.EnsureContainer<Container>(pit.Owner, TradingComponent.MarketContainerId);
        BaseContainer? previousContainer = null;
        if (_containers.TryGetContainingContainer((sourceItem, null, null), out previousContainer) &&
            !_containers.Remove(sourceItem, previousContainer, reparent: false, force: true))
        {
            return false;
        }

        EmptyItemContainers(sourceItem, seller);

        if (!_containers.Insert(sourceItem, destination, force: true))
        {
            if (previousContainer != null)
                _containers.Insert(sourceItem, previousContainer, force: true);
            TryRemoveCommodity(market, commodity);
            return false;
        }

        offer = new TradingMarketOffer
        {
            Id = Guid.NewGuid(),
            CommodityId = commodity.Id,
            Product = product,
            Side = TradingOfferSide.Sell,
            ParticipantKind = TradingParticipantKind.Trader,
            ParticipantName = participantName,
            Price = price,
            Pit = pit.Owner,
            Item = sourceItem,
            ListedItemName = MetaData(sourceItem).EntityName,
            IsImmediate = immediate,
            Sequence = market.Comp.NextSequence++,
        };
        AddOffer(market, offer, config);
        commodityId = commodity.Id;
        return true;
    }

    private void EmptyItemContainers(EntityUid item, EntityUid seller)
    {
        if (!TryComp<ContainerManagerComponent>(item, out var containerManager))
            return;

        var coordinates = Transform(seller).Coordinates;
        foreach (var container in _containers.GetAllContainers(item, containerManager))
        {
            _containers.EmptyContainer(container, true, coordinates);
        }
    }

    private bool TryFindInventoryItem(
        EntityUid user,
        Entity<TradingMarketComponent> market,
        TradingCommodity selected,
        out EntityUid item)
    {
        var pending = new Queue<EntityUid>(_inventory.GetHandOrInventoryEntities(user));
        var visited = new HashSet<EntityUid>();
        while (pending.TryDequeue(out var candidate))
        {
            if (!visited.Add(candidate))
                continue;

            if (TryResolveCommodityForItem(market, candidate, selected.StandardPrice, false, out var commodity) &&
                commodity.Id == selected.Id)
            {
                item = candidate;
                return true;
            }

            if (TryComp<StorageComponent>(candidate, out var storage))
            {
                foreach (var contained in storage.Container.ContainedEntities)
                {
                    pending.Enqueue(contained);
                }
            }
        }

        item = default;
        return false;
    }

    private void DeliverItem(
        EntityUid pitUid,
        TradingComponent pit,
        EntityUid item,
        EntityUid? recipient)
    {
        if (!Exists(item) || TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item))
        {
            pit.StoredMarketItems.Remove(item);
            return;
        }

        if (recipient is { } user && Exists(user))
        {
            pit.StoredMarketItems.Remove(item);
            _hands.PickupOrDrop(user, item, dropNear: true);
            return;
        }

        StoreItemInPit(pitUid, pit, item);
    }

    private bool StoreItemInPit(EntityUid pitUid, TradingComponent pit, EntityUid item)
    {
        var destination = _containers.EnsureContainer<Container>(pitUid, TradingComponent.MarketContainerId);
        BaseContainer? previousContainer = null;
        if (_containers.TryGetContainingContainer((item, null, null), out previousContainer) &&
            (previousContainer.Owner != pitUid || previousContainer.ID != TradingComponent.MarketContainerId))
        {
            if (!_containers.Remove(item, previousContainer, reparent: false, force: true))
                return false;

            if (!_containers.Insert(item, destination, force: true))
            {
                _containers.Insert(item, previousContainer, force: true);
                return false;
            }
        }
        else if (previousContainer == null && !_containers.Insert(item, destination, force: true))
        {
            return false;
        }

        if (!pit.StoredMarketItems.Contains(item))
            pit.StoredMarketItems.Add(item);
        return true;
    }

    private void RefreshVisibleMarketItems(
        EntityUid user,
        EntityUid store,
        TradingComponent component,
        Entity<TradingMarketComponent> market,
        bool isOwner)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var viewer = EnsureComp<TradingMarketViewerComponent>(user);
        var desired = market.Comp.Commodities.Values
            .Where(commodity => isOwner || (commodity.Sections & TradingMarketSection.Unique) != 0)
            .Select(commodity => GetLowestSellOffer(
                market.Comp.Offers.Values,
                commodity.Id,
                isOwner ? store : null)?.Item)
            .Where(item => item != null && Exists(item.Value))
            .Select(item => item!.Value)
            .ToHashSet();

        if (isOwner)
        {
            desired.UnionWith(market.Comp.Offers.Values
                .Where(offer => offer.Pit == store && offer.Item is { } item && Exists(item))
                .Select(offer => offer.Item!.Value));
        }

        if (viewer.SelectedOffer is { } selectedOffer &&
            market.Comp.Offers.TryGetValue(selectedOffer, out var offer) &&
            CanSelectOffer(offer, viewer.SelectedCommodity))
        {
            if (offer.Item is { } selectedItem && Exists(selectedItem))
                desired.Add(selectedItem);
        }
        else
        {
            viewer.SelectedOffer = null;
        }

        if (isOwner)
            desired.UnionWith(component.StoredMarketItems.Where(Exists));

        foreach (var item in viewer.VisibleItems.Except(desired).ToList())
        {
            if (Exists(item))
                _pvs.RemoveForceSend(item, actor.PlayerSession);
            viewer.VisibleItems.Remove(item);
        }

        foreach (var item in desired.Except(viewer.VisibleItems))
        {
            _pvs.AddForceSend(item, actor.PlayerSession);
            viewer.VisibleItems.Add(item);
        }
    }

    private void ClearVisibleMarketItems(EntityUid user)
    {
        if (!TryComp<ActorComponent>(user, out var actor) ||
            !TryComp<TradingMarketViewerComponent>(user, out var viewer))
        {
            return;
        }

        foreach (var item in viewer.VisibleItems)
        {
            if (Exists(item))
                _pvs.RemoveForceSend(item, actor.PlayerSession);
        }

        RemCompDeferred<TradingMarketViewerComponent>(user);
    }

    internal static TradingMarketOffer? GetLowestSellOffer(
        IEnumerable<TradingMarketOffer> offers,
        Guid commodityId,
        EntityUid? excludedPit = null)
    {
        return offers
            .Where(offer => offer.CommodityId == commodityId &&
                            offer.Side == TradingOfferSide.Sell &&
                            (excludedPit == null || offer.Pit != excludedPit))
            .OrderBy(offer => offer.Price)
            .ThenBy(offer => offer.Sequence)
            .FirstOrDefault();
    }

    private void OnRequestWithdraw(EntityUid uid, TradingComponent component, TradingRequestWithdrawMessage msg)
    {
        if (!IsTradingPitOwner(msg.Actor, component) || msg.Amount <= 0 || component.Balance < msg.Amount)
            return;

        if (!_prototypeManager.TryIndex(component.Currency, out var prototype) ||
            prototype.Cash == null ||
            !prototype.CanWithdraw)
        {
            return;
        }

        FixedPoint2 amountRemaining = msg.Amount;
        var coordinates = Transform(msg.Actor).Coordinates;
        foreach (var value in prototype.Cash.Keys.OrderByDescending(value => value))
        {
            var amountToSpawn = (int) MathF.Floor((float) (amountRemaining / value));
            var entities = _stack.SpawnMultiple(prototype.Cash[value], amountToSpawn, coordinates);
            if (entities.FirstOrDefault() is { } entity)
                _hands.PickupOrDrop(msg.Actor, entity);
            amountRemaining -= value * amountToSpawn;
        }

        component.Balance -= msg.Amount;
        UpdateUserInterface(msg.Actor, uid, component);
    }

    private void ShowTradingSuccess(
        EntityUid actor,
        EntityUid pit,
        TradingComponent component,
        string message)
    {
        _popup.PopupCursor(Loc.GetString(message), actor);
        _audio.PlayEntity(component.BuySuccessSound, actor, pit);
    }
}
