using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Trading.Prototypes;

[DataDefinition, NetSerializable, Serializable]
public sealed partial record GuildTradingItem
{
    [DataField(required: true)]
    public int Cost;

    [DataField(required: true)]
    public EntProtoId ProductEntity;

    [DataField]
    public string? Name;

    [DataField]
    public string? Description;
}
