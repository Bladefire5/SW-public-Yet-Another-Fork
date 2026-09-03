using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Praises;

[Serializable, NetSerializable]
public sealed class PraiseRatingOpenedMessage : EntityEventArgs { }

[Serializable, NetSerializable]
public sealed class PraiseRatingMessage : EntityEventArgs
{
    public List<(string, int)> Rating = default!;
}
