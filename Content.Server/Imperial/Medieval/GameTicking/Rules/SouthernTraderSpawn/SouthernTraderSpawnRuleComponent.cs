using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules.SouthernTraderSpawn;

[RegisterComponent]
public sealed partial class SouthernTraderSpawnRuleComponent : Component
{
    [DataField(required: true)]
    public ProtoId<JobPrototype> Job;

    [ViewVariables]
    public EntityUid? Performer;
}
