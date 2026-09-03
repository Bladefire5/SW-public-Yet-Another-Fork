using System.Collections.Generic;

namespace Content.Server.Imperial.Medieval.Ships;

[RegisterComponent]
public sealed partial class ShipGridComponent : Component
{
    public readonly HashSet<EntityUid> Sails = new();

    public readonly HashSet<EntityUid> Anchors = new();

    public EntityUid? Helm;

    public int TileCount;

    public int FloodContribution;

    public float SteeringPower;

    public float SailsEfficiency;

    public float TotalWeight;

    public bool HasLoweredAnchor;

    public TimeSpan? WavesDisabledAt;
}

[ByRefEvent]
public readonly record struct ShipAnchorStateChangedEvent(bool HasLoweredAnchors);
