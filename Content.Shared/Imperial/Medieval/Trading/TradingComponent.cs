using Content.Shared.FixedPoint;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.Store;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.Trading;

[RegisterComponent, NetworkedComponent]
public sealed partial class TradingComponent : Component
{
    public const string MarketContainerId = "trading-pit-market";

    [DataField]
    public int Balance;

    [DataField]
    public ProtoId<CurrencyPrototype> Currency;

    [DataField]
    public EntityUid? AccountOwner = null;

    [DataField]
    public HashSet<ProtoId<GuildTypePrototype>> GuildTypes;

    public HashSet<Guid> MarketOffers = new();

    public List<EntityUid> StoredMarketItems = new();

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool OwnerOnly;

    [DataField]
    public SoundSpecifier BuySuccessSound = new SoundPathSpecifier("/Audio/Effects/kaching.ogg");
}
