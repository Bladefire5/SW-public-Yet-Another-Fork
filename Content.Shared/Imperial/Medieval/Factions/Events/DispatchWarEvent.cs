using Content.Shared.Imperial.Medieval.Factions.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Imperial.Medieval.Factions;

[Serializable, NetSerializable]
public sealed partial class DispatchWarEvent : EntityEventArgs
{
    /// <summary>
    /// Hard cap on reason length The client caps input at this length for UX. the server re-clamps
    /// </summary>
    public const int MaxReasonLength = 256;

    public ProtoId<MedievalFactionPrototype> UserFaction;
    public ProtoId<MedievalFactionPrototype> TargetFaction;

    /// <summary>
    /// Free text typed by the declaring faction leader. UNTRUSTED.
    /// goes through SanitizeWarReason() serverside.
    /// </summary>
    public string Reason;

    public DispatchWarEvent(ProtoId<MedievalFactionPrototype> userFaction, ProtoId<MedievalFactionPrototype> targetFaction, string reason)
    {
        UserFaction = userFaction;
        TargetFaction = targetFaction;
        Reason = reason;
    }
}
