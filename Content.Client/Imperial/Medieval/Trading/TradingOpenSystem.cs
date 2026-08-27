using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.Imperial.Medieval.Trading;

public sealed class TradingOpenSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TradingComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
    }

    private void OnOpenAttempt(EntityUid uid, TradingComponent component, ActivatableUIOpenAttemptEvent args)
    {
        args.Cancel();
        if (!_timing.IsFirstTimePredicted)
            return;

        RaiseNetworkEvent(new TradingRequestOpenUiMessage(GetNetEntity(uid)));
    }
}
