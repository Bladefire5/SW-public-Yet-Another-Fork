using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.Fishing;

[Serializable, NetSerializable]
public sealed partial class FishingWaitDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity? Bobber { get; private set; }

    [DataField]
    public float CurrentChance { get; set; } = 1f;

    private FishingWaitDoAfterEvent()
    {
    }

    public FishingWaitDoAfterEvent(NetEntity? bobber, float currentChance = 1f)
    {
        Bobber = bobber;
        CurrentChance = currentChance;
    }

    public override DoAfterEvent Clone()
    {
        return new FishingWaitDoAfterEvent(Bobber, CurrentChance);
    }
}
