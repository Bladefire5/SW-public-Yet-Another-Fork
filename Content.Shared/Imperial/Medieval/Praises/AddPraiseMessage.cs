using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Praises;

[Serializable, NetSerializable]
public sealed class AddPraiseMessage : EntityEventArgs
{
    public NetUserId Target;
    public string Reason = default!;
    public int Weight;
}
