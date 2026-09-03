using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Ships.BoardingHook;

[Serializable, NetSerializable]
public sealed partial class BoardingHookPullDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class BoardingHookRemoveDoAfterEvent : SimpleDoAfterEvent
{
}

