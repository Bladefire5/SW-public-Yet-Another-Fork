using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Factions;

/// <summary>
/// Server -> client. Raised once per player belonging to a faction whose relations actually
/// changed as a result of a war declaration.
/// </summary>
[Serializable, NetSerializable]
public sealed class MedievalWarDeclaredEvent : EntityEventArgs
{
    public ProtoId<MedievalFactionPrototype> Declarer;
    public ProtoId<MedievalFactionPrototype> Target;

    /// <summary>
    /// Already sanitised server-side. May be empty.
    /// </summary>
    public string Reason;

    /// <summary>
    /// How the RECIPIENT'S faction got involved. This is per-recipient, not global.
    /// </summary>
    public WarInvolvement Involvement;

    public MedievalWarDeclaredEvent(
        ProtoId<MedievalFactionPrototype> declarer,
        ProtoId<MedievalFactionPrototype> target,
        string reason,
        WarInvolvement involvement)
    {
        Declarer = declarer;
        Target = target;
        Reason = reason;
        Involvement = involvement;
    }
}

[Serializable, NetSerializable]
public enum WarInvolvement : byte
{
    /// <summary>
    /// The recipient's faction declared the war itself.
    /// </summary>
    Declarer,

    /// <summary>
    /// The war was declared on the recipient's faction.
    /// </summary>
    Target,

    /// <summary>
    /// A Union with the declarer dragged the recipient into the war against the target.
    /// </summary>
    AllyOfDeclarer,

    /// <summary>
    /// A Union with the target dragged the recipient into the war against the declarer.
    /// </summary>
    AllyOfTarget,

    /// <summary>
    /// The recipient held a Union with both belligerents and is now at war with both.
    /// </summary>
    AllyOfBoth
}
