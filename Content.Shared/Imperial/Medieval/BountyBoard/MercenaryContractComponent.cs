using Content.Shared.Stacks;
using Content.Shared.Imperial.Medieval.Recipient;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Imperial.Medieval.BountyBoard;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MercenaryContractComponent : Component
{
    [AutoNetworkedField]
    public EntityUid TargetUid { get; set; }

    [DataField]
    public EntProtoId<StackComponent> CurrencyProtoId { get; set; } = "MedievalRevent";

    [DataField]
    public Vector2i PayoutRange { get; set; } = new(125, 200);

    public RecipientData? TargetData;

    public int Payout;
}
