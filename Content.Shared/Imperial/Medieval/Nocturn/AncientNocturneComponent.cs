using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Nocturn.Components;

public sealed partial class AncientNocturneBatActionEvent : InstantActionEvent;

public sealed partial class AncientNocturneConversionActionEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class AncientNocturneConversionDoAfterEvent : SimpleDoAfterEvent;

[RegisterComponent]
public sealed partial class AncientNocturneComponent : Component
{
    [DataField]
    public ProtoId<PolymorphPrototype> BatPolymorph = "MedievalAncientNocturneBatPolymorph";

    [DataField]
    public EntProtoId BatAction = "MedievalAncientNocturneBatAction";

    [DataField]
    public TimeSpan BatActionCooldown = TimeSpan.FromMinutes(1);

    [DataField]
    public ProtoId<PolymorphPrototype> ConversionPolymorph = "MedievalAncientNocturneConversionPolymorph";

    [DataField]
    public TimeSpan ConversionDuration = TimeSpan.FromSeconds(6);

    [DataField]
    public ProtoId<SpeciesPrototype> ConversionTargetSpecies = "Human";
}
