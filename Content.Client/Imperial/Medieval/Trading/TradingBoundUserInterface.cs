using Content.Shared.Imperial.Medieval.Trading;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Imperial.Medieval.Trading;

[UsedImplicitly]
public sealed class TradingBoundUserInterface : BoundUserInterface
{
    private TradingMenu? _menu;
    private bool _isOwner;

    public TradingBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<TradingMenu>();
        _menu.OnBuy += commodity => SendOwnerMessage(new TradingBuyMessage(commodity));
        _menu.OnSell += commodity => SendOwnerMessage(new TradingSellMessage(commodity));
        _menu.OnBuyOffer += offer => SendOwnerMessage(new TradingBuyOfferMessage(offer));
        _menu.OnSellOffer += offer => SendOwnerMessage(new TradingSellOfferMessage(offer));
        _menu.OnSelectCommodity += commodity => SendMessage(new TradingSelectCommodityMessage(commodity));
        _menu.OnSelectOffer += offer => SendMessage(new TradingSelectOfferMessage(offer));
        _menu.OnCreateSellOffer += price => SendOwnerMessage(new TradingCreateSellOfferMessage(price));
        _menu.OnCreateBuyOffer += (commodity, price) => SendOwnerMessage(new TradingCreateBuyOfferMessage(commodity, price));
        _menu.OnCreateBuyOfferFromHeld += price => SendOwnerMessage(new TradingCreateBuyOfferFromHeldMessage(price));
        _menu.OnCancelOffer += id => SendOwnerMessage(new TradingCancelOfferMessage(id));
        _menu.OnCollectStoredItem += item => SendOwnerMessage(new TradingCollectStoredItemMessage(item));
        _menu.OnWithdraw += amount => SendOwnerMessage(new TradingRequestWithdrawMessage(amount));
        SendMessage(new TradingRequestUpdateInterfaceMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TradingUpdateState update)
        {
            _isOwner = update.IsOwner;
            _menu?.UpdateState(update);
        }
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is TradingUpdateInterfaceMessage update)
        {
            _isOwner = update.State.IsOwner;
            _menu?.UpdateState(update.State);
        }
    }

    private void SendOwnerMessage(BoundUserInterfaceMessage message)
    {
        if (_isOwner)
            SendMessage(message);
    }
}
