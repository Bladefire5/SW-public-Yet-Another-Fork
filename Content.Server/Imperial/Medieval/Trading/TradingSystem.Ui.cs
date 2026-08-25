using System.Linq;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Store;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
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
        SubscribeLocalEvent<TradingComponent, TradingCreateSellOfferMessage>(OnCreateSellOffer);
        SubscribeLocalEvent<TradingComponent, TradingCreateBuyOfferMessage>(OnCreateBuyOffer);
        SubscribeLocalEvent<TradingComponent, TradingCreateBuyOfferFromHeldMessage>(OnCreateBuyOfferFromHeld);
        SubscribeLocalEvent<TradingComponent, TradingCancelOfferMessage>(OnCancelOffer);
        SubscribeLocalEvent<TradingComponent, TradingCollectStoredItemMessage>(OnCollectStoredItem);
        SubscribeLocalEvent<TradingComponent, TradingRequestWithdrawMessage>(OnRequestWithdraw);
    }

    public void ToggleUi(EntityUid user, EntityUid storeEnt, TradingComponent? component = null)
    {
        if (!Resolve(storeEnt, ref component) || !TryComp<ActorComponent>(user, out var actor))
            return;

        if (!_ui.TryToggleUi(storeEnt, TradingUiKey.Key, actor.PlayerSession))
            return;

        UpdateUserInterface(user, storeEnt, component);
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

        component.MarketOffers.RemoveWhere(id => !market.Comp.Offers.ContainsKey(id));
        component.StoredMarketItems.RemoveAll(item => !Exists(item));
        RefreshVisibleMarketItems(user, store, component, market);

        var items = market.Comp.Commodities.Values
            .Select(commodity =>
            {
                var commodityOffers = market.Comp.Offers.Values
                    .Where(offer => offer.CommodityId == commodity.Id)
                    .ToList();
                var asks = commodityOffers.Where(offer => offer.Side == TradingOfferSide.Sell).ToList();
                var bids = commodityOffers.Where(offer => offer.Side == TradingOfferSide.Buy).ToList();
                var preview = asks
                    .Where(offer => offer.Item != null && Exists(offer.Item.Value))
                    .OrderBy(offer => offer.Price)
                    .ThenBy(offer => offer.Sequence)
                    .Select(offer => offer.Item)
                    .FirstOrDefault();
                var displayName = commodity.DisplayName;
                var description = commodity.Description;
                int? stackCount = commodity.HasStack ? commodity.BaselineStackCount : null;
                NetEntity? previewEntity = null;
                if (preview is { } previewItem)
                {
                    var metadata = MetaData(previewItem);
                    stackCount = TryComp<StackComponent>(previewItem, out var stack) ? stack.Count : null;
                    displayName = FormatStackName(metadata.EntityName, stackCount);
                    description = metadata.EntityDescription;
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
                    commodity.Demand,
                    commodity.Supply,
                    asks.Count == 0 ? null : asks.Min(offer => offer.Price),
                    bids.Count == 0 ? null : bids.Max(offer => offer.Price),
                    asks.Count,
                    bids.Count,
                    new HashSet<ProtoId<GuildTypePrototype>>(commodity.Categories));
            })
            .ToList();

        var offers = market.Comp.Offers.Values
            .OrderBy(offer => offer.Product.Id)
            .ThenBy(offer => offer.Side)
            .ThenBy(offer => offer.Price)
            .Select(offer => CreateOfferState(market, store, offer))
            .ToList();

        var storedItems = component.StoredMarketItems
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

        _ui.SetUiState(
            store,
            TradingUiKey.Key,
            new TradingUpdateState(items, offers, storedItems, component.Balance, component.Currency));
    }

    private TradingMarketOfferState CreateOfferState(
        Entity<TradingMarketComponent> market,
        EntityUid store,
        TradingMarketOffer offer)
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
            offer.Pit == store,
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

    private void OnUiOpened(EntityUid uid, TradingComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(args.Actor, uid, component);
    }

    private void OnUiClosed(EntityUid uid, TradingComponent component, BoundUIClosedEvent args)
    {
        ClearVisibleMarketItems(args.Actor);
    }

    private void BeforeActivatableUiOpen(EntityUid uid, TradingComponent component, BeforeActivatableUIOpenEvent args)
    {
        UpdateUserInterface(args.User, uid, component);
    }

    private void OnBuyRequest(EntityUid uid, TradingComponent component, TradingBuyMessage msg)
    {
        if (!TryGetMarket(out var market))
            return;

        var ask = market.Comp.Offers.Values
            .Where(offer => offer.CommodityId == msg.CommodityId &&
                            offer.Side == TradingOfferSide.Sell &&
                            offer.Pit != uid)
            .OrderBy(offer => offer.Price)
            .ThenBy(offer => offer.Sequence)
            .FirstOrDefault();
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
        _audio.PlayEntity(component.BuySuccessSound, msg.Actor, uid);
        UpdateAllInterfaces(market);
    }

    private void OnSellRequest(EntityUid uid, TradingComponent component, TradingSellMessage msg)
    {
        if (!TryGetMarket(out var market))
            return;

        var bid = market.Comp.Offers.Values
            .Where(offer => offer.CommodityId == msg.CommodityId &&
                            offer.Side == TradingOfferSide.Buy &&
                            offer.Pit != uid)
            .OrderByDescending(offer => offer.Price)
            .ThenBy(offer => offer.Sequence)
            .FirstOrDefault();
        if (bid == null ||
            !market.Comp.Commodities.TryGetValue(msg.CommodityId, out var commodity) ||
            !TryFindInventoryItem(msg.Actor, market, commodity, out var item))
        {
            return;
        }

        if (!TryCreateTraderSellOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                item,
                bid.Price,
                out var commodityId) ||
            commodityId != msg.CommodityId)
        {
            return;
        }

        MatchCommodity(market, commodity, _prototypeManager.Index(market.Comp.Config));
        _audio.PlayEntity(component.BuySuccessSound, msg.Actor, uid);
        UpdateAllInterfaces(market);
    }

    private void OnCreateSellOffer(EntityUid uid, TradingComponent component, TradingCreateSellOfferMessage msg)
    {
        if (!TryGetMarket(out var market) ||
            !_hands.TryGetActiveItem(msg.Actor, out var held) ||
            held is not { } item ||
            !TryCreateTraderSellOffer(
                market,
                (uid, component),
                MetaData(msg.Actor).EntityName,
                item,
                msg.Price,
                out var commodityId))
        {
            return;
        }

        if (!market.Comp.Commodities.TryGetValue(commodityId, out var commodity))
            return;

        MatchCommodity(market, commodity, _prototypeManager.Index(market.Comp.Config));
        UpdateAllInterfaces(market);
    }

    private void OnCreateBuyOffer(EntityUid uid, TradingComponent component, TradingCreateBuyOfferMessage msg)
    {
        if (!TryGetMarket(out var market) ||
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
        UpdateAllInterfaces(market);
    }

    private void OnCreateBuyOfferFromHeld(
        EntityUid uid,
        TradingComponent component,
        TradingCreateBuyOfferFromHeldMessage msg)
    {
        if (!TryGetMarket(out var market) ||
            !_hands.TryGetActiveItem(msg.Actor, out var held) ||
            held is not { } item ||
            !HasComp<ItemComponent>(item) ||
            msg.Price <= 0 ||
            msg.Price > _prototypeManager.Index(market.Comp.Config).MaximumPrice ||
            component.Balance < msg.Price)
        {
            return;
        }

        if (!TryResolveCommodityForItem(market, item, msg.Price, true, out var commodity) ||
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
        UpdateAllInterfaces(market);
    }

    private void OnCancelOffer(EntityUid uid, TradingComponent component, TradingCancelOfferMessage msg)
    {
        if (!TryGetMarket(out var market) ||
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
        var config = _prototypeManager.Index(market.Comp.Config);
        if (price <= 0 ||
            price > config.MaximumPrice ||
            pit.Comp.Balance < price ||
            !market.Comp.Commodities.ContainsKey(commodity.Id))
        {
            return false;
        }

        pit.Comp.Balance -= price;
        AddOffer(market, new TradingMarketOffer
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
            Sequence = market.Comp.NextSequence++,
        }, config);
        return true;
    }

    internal bool TryCreateTraderSellOffer(
        Entity<TradingMarketComponent> market,
        Entity<TradingComponent> pit,
        string participantName,
        EntityUid sourceItem,
        int price,
        out Guid commodityId)
    {
        commodityId = default;
        var config = _prototypeManager.Index(market.Comp.Config);
        if (price <= 0 || price > config.MaximumPrice ||
            !HasComp<ItemComponent>(sourceItem) ||
            MetaData(sourceItem).EntityPrototype?.ID is not { } product ||
            IsTrophy(product))
        {
            return false;
        }

        if (!TryResolveCommodityForItem(market, sourceItem, price, true, out var commodity))
            return false;

        var destination = _containers.EnsureContainer<Container>(pit.Owner, TradingComponent.MarketContainerId);
        BaseContainer? previousContainer = null;
        if (_containers.TryGetContainingContainer((sourceItem, null, null), out previousContainer) &&
            !_containers.Remove(sourceItem, previousContainer, reparent: false, force: true))
        {
            return false;
        }

        if (!_containers.Insert(sourceItem, destination, force: true))
        {
            if (previousContainer != null)
                _containers.Insert(sourceItem, previousContainer, force: true);
            TryRemoveCommodity(market, commodity);
            return false;
        }

        AddOffer(market, new TradingMarketOffer
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
            Sequence = market.Comp.NextSequence++,
        }, config);
        commodityId = commodity.Id;
        return true;
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
        if (recipient is { } user && Exists(user))
        {
            if (_containers.TryGetContainingContainer((item, null, null), out var current) &&
                !_containers.Remove(item, current, reparent: false, force: true))
            {
                return;
            }

            pit.StoredMarketItems.Remove(item);
            _hands.PickupOrDrop(user, item);
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
        Entity<TradingMarketComponent> market)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        var desired = market.Comp.Commodities.Values
            .Select(commodity => market.Comp.Offers.Values
                .Where(offer => offer.CommodityId == commodity.Id &&
                                offer.Side == TradingOfferSide.Sell &&
                                offer.Item != null &&
                                Exists(offer.Item.Value))
                .OrderBy(offer => offer.Price)
                .ThenBy(offer => offer.Sequence)
                .Select(offer => offer.Item)
                .FirstOrDefault())
            .Where(item => item != null)
            .Select(item => item!.Value)
            .ToHashSet();

        desired.UnionWith(market.Comp.Offers.Values
            .Where(offer => offer.Pit == store && offer.Item is { } item && Exists(item))
            .Select(offer => offer.Item!.Value));
        desired.UnionWith(component.StoredMarketItems.Where(Exists));

        var viewer = EnsureComp<TradingMarketViewerComponent>(user);
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

    private void OnRequestWithdraw(EntityUid uid, TradingComponent component, TradingRequestWithdrawMessage msg)
    {
        if (msg.Amount <= 0 || component.Balance < msg.Amount)
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
}
