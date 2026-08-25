using Content.Shared.Imperial.Medieval.Trading;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Imperial.Medieval.Trading;

[UsedImplicitly]
public sealed class TradingBoundUserInterface : BoundUserInterface
{
    private TradingMenu? _menu;

    public TradingBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<TradingMenu>();
        _menu.OnBuy += commodity => SendMessage(new TradingBuyMessage(commodity));
        _menu.OnSell += commodity => SendMessage(new TradingSellMessage(commodity));
        _menu.OnBuyOffer += offer => SendMessage(new TradingBuyOfferMessage(offer));
        _menu.OnSellOffer += offer => SendMessage(new TradingSellOfferMessage(offer));
        _menu.OnSelectCommodity += commodity => SendMessage(new TradingSelectCommodityMessage(commodity));
        _menu.OnCreateSellOffer += price => SendMessage(new TradingCreateSellOfferMessage(price));
        _menu.OnCreateBuyOffer += (commodity, price) => SendMessage(new TradingCreateBuyOfferMessage(commodity, price));
        _menu.OnCreateBuyOfferFromHeld += price => SendMessage(new TradingCreateBuyOfferFromHeldMessage(price));
        _menu.OnCancelOffer += id => SendMessage(new TradingCancelOfferMessage(id));
        _menu.OnCollectStoredItem += item => SendMessage(new TradingCollectStoredItemMessage(item));
        _menu.OnWithdraw += amount => SendMessage(new TradingRequestWithdrawMessage(amount));
        SendMessage(new TradingRequestUpdateInterfaceMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TradingUpdateState update)
            _menu?.UpdateState(update);
    }
}
