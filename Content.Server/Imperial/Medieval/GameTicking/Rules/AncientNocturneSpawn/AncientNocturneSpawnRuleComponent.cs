using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

[RegisterComponent, Access(typeof(AncientNocturneSpawnRuleSystem))]
public sealed partial class AncientNocturneSpawnRuleComponent : Component
{
    [DataField]
    public int SpawnCount = 3;

    [DataField]
    public EntProtoId SpawnerPrototype = "MedievalAncientNocturneGhostRoleSpawner";
}
