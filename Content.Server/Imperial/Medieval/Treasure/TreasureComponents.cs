using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.Treasure;

[RegisterComponent]
public sealed partial class TreasureBoardComponent : Component
{
    [DataField]
    public EntProtoId<TreasureMapComponent> MapPrototype = "MedievalTreasureMap";

    [DataField]
    public TimeSpan MinimumSpawnDelay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan MaximumSpawnDelay = TimeSpan.FromMinutes(15);
}

[RegisterComponent]
public sealed partial class TreasureMapComponent : Component
{
    public EntityUid? Marker;

    public bool Completed;
}

[RegisterComponent]
public sealed partial class TreasureMarkerComponent : Component
{
    [DataField]
    public float DigRadius = 5f;

    public EntityUid? Map;

    public Vector2i WorldPosition;

}

[RegisterComponent]
public sealed partial class TreasureDiggerComponent : Component
{
    [DataField]
    public TimeSpan DiggingDuration = TimeSpan.FromSeconds(5);
}
