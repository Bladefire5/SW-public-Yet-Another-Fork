using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Praises;

//used in 'PraisesViewWindow'
//the only reason this exists is because 'Praise' cannot be put in this namespace
[Serializable, NetSerializable]
public sealed class PraiseViewRecord
{
    public Guid? GivenBy; //will be null when sent to anyone who is not an admin, used for deletion and weight changes
    public string GivenByName = "";
    public string Reason = "";
    public DateTime Date;
    public int Weight;
}

[Serializable, NetSerializable]
public sealed class PraiseViewOpenedMessage : EntityEventArgs
{
    public NetUserId Target;
}

[Serializable, NetSerializable]
public sealed class PraiseViewMessage : EntityEventArgs
{
    public NetUserId Target;
    public List<PraiseViewRecord> Records = default!;
    public bool Admin;
    public bool Spam;
}

[Serializable, NetSerializable]
public sealed class PraiseViewEditMessage : EntityEventArgs
{
    public NetUserId Target;
    public PraiseViewRecord Record = default!;
}

[Serializable, NetSerializable]
public sealed class PraiseViewDeleteMessage : EntityEventArgs
{
    public NetUserId Target;
    public PraiseViewRecord Record = default!;
}
