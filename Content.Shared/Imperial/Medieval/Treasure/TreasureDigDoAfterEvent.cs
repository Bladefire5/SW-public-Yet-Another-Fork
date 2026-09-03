using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Treasure;

[Serializable, NetSerializable]
public sealed partial class TreasureDigDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates ClickLocation { get; private set; }

    [DataField(required: true)]
    public NetEntity Marker { get; private set; }

    private TreasureDigDoAfterEvent()
    {
    }

    public TreasureDigDoAfterEvent(NetCoordinates clickLocation, NetEntity marker)
    {
        ClickLocation = clickLocation;
        Marker = marker;
    }

    public override DoAfterEvent Clone()
    {
        return new TreasureDigDoAfterEvent(ClickLocation, Marker);
    }
}
