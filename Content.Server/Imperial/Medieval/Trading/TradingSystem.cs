using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server.Imperial.Medieval.Trading;

public sealed partial class TradingSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    private EntityUid? _market;
    private CancellationTokenSource? _marketUpdateCancellation;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TradingComponent, ActivatableUIOpenAttemptEvent>(OnStoreOpenAttempt);
        SubscribeLocalEvent<TradingComponent, BeforeActivatableUIOpenEvent>(BeforeActivatableUiOpen);
        SubscribeLocalEvent<TradingComponent, EntityTerminatingEvent>(OnTradingPitTerminating);
        SubscribeLocalEvent<MedievalCurrencyComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnd);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        InitializeUi();
    }

    public override void Shutdown()
    {
        StopMarketUpdates();
        base.Shutdown();
    }

    private void OnTradingPitTerminating(
        Entity<TradingComponent> pit,
        ref EntityTerminatingEvent args)
    {
        if (TryGetMarket(out var market))
        {
            var config = _prototypeManager.Index(market.Comp.Config);
            var offers = market.Comp.Offers.Values
                .Where(offer => offer.Pit == pit.Owner)
                .Select(offer => offer.Id)
                .ToList();

            foreach (var offer in offers)
            {
                RemoveOffer(market, offer, false, config);
            }
        }

        pit.Comp.MarketOffers.Clear();
        pit.Comp.StoredMarketItems.Clear();

        if (_containers.TryGetContainer(pit.Owner, TradingComponent.MarketContainerId, out var container))
            _containers.EmptyContainer(container, true, Transform(pit.Owner).Coordinates);
    }

    private void OnRoundStart(RoundStartedEvent args)
    {
        StopMarketUpdates();
        CreateMarket();
        _marketUpdateCancellation = new CancellationTokenSource();
        _ = RunMarketUpdatesAsync(_marketUpdateCancellation.Token);
    }

    private void OnRoundEnd(RoundEndedEvent args)
    {
        StopMarketUpdates();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        StopMarketUpdates();
        _market = null;
    }

    private async Task RunMarketUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TryGetMarket(out var market))
                    return;

                var config = _prototypeManager.Index(market.Comp.Config);
                var interval = TimeSpan.FromSeconds(config.StepInterval);
                await Timer.Delay(interval, cancellationToken).WaitAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetMarket(out market))
                    return;

                config = _prototypeManager.Index(market.Comp.Config);
                RunMarketStep(market, config);
                UpdateAllInterfaces(market);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void StopMarketUpdates()
    {
        if (_marketUpdateCancellation == null)
            return;

        _marketUpdateCancellation.Cancel();
        _marketUpdateCancellation.Dispose();
        _marketUpdateCancellation = null;
    }

    private void OnStoreOpenAttempt(EntityUid uid, TradingComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (!_mind.TryGetMind(args.User, out var mindId, out _))
        {
            args.Cancel();
            return;
        }

        if (component.AccountOwner != null && component.AccountOwner != mindId &&
            (!_mind.TryGetMind(component.AccountOwner.Value, out var previousMind, out _) || previousMind != mindId))
        {
            if (component.OwnerOnly)
            {
                _popup.PopupEntity(Loc.GetString("store-not-account-owner", ("store", uid)), uid, args.User);
                args.Cancel();
                return;
            }
        }

        component.AccountOwner = mindId;
        _containers.EnsureContainer<Robust.Shared.Containers.Container>(uid, TradingComponent.MarketContainerId);
    }

    private void OnAfterInteract(EntityUid uid, MedievalCurrencyComponent component, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<TradingComponent>(args.Target, out var store))
            return;

        if (!TryAddCurrency((uid, component), (args.Target.Value, store)))
            return;

        args.Handled = true;
        _popup.PopupEntity(
            Loc.GetString("store-currency-inserted", ("used", args.Used), ("target", args.Target)),
            args.Target.Value,
            args.User);
    }

    public bool TryAddCurrency(
        Entity<MedievalCurrencyComponent?> currency,
        Entity<TradingComponent?> store)
    {
        if (!Resolve(currency.Owner, ref currency.Comp) || !Resolve(store.Owner, ref store.Comp))
            return false;

        var value = currency.Comp.Price;
        if (TryComp(currency.Owner, out StackComponent? stack) && stack.Count != 1)
        {
            value = currency.Comp.Price.ToDictionary(entry => entry.Key, entry => entry.Value * stack.Count);
        }

        if (!TryAddCurrency(value, store.Owner, store.Comp))
            return false;

        currency.Comp.Price.Clear();
        if (stack != null)
            _stack.SetCount(currency.Owner, 0, stack);

        QueueDel(currency.Owner);
        return true;
    }

    public bool TryAddCurrency(
        Dictionary<string, FixedPoint2> currency,
        EntityUid uid,
        TradingComponent? store = null)
    {
        if (!Resolve(uid, ref store))
            return false;

        foreach (var type in currency.Keys)
        {
            if (store.Currency != type)
                return false;
        }

        foreach (var value in currency.Values)
        {
            store.Balance += value.Int();
        }

        foreach (var user in _ui.GetActors(uid, TradingUiKey.Key))
        {
            UpdateUserInterface(user, uid, store);
        }
        return true;
    }

    private bool TryGetMarket(out Entity<TradingMarketComponent> market)
    {
        if (_market is { } marketUid && TryComp<TradingMarketComponent>(marketUid, out var component))
        {
            market = (marketUid, component);
            return true;
        }

        var query = EntityQueryEnumerator<TradingMarketComponent>();
        if (query.MoveNext(out marketUid, out component))
        {
            _market = marketUid;
            market = (marketUid, component);
            return true;
        }

        market = default;
        return false;
    }
}
