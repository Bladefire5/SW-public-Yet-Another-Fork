using Content.Shared.Actions;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;

namespace Content.Shared.Nocturn.Components;

public sealed partial class AncientNocturneBatActionEvent : InstantActionEvent;

[RegisterComponent]
public sealed partial class AncientNocturneComponent : Component
{
    [DataField]
    public ProtoId<PolymorphPrototype> BatPolymorph = "MedievalAncientNocturneBatPolymorph";
}
