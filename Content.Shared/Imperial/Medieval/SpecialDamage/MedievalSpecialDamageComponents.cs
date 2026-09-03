using Content.Shared.Damage;

namespace Content.Shared.Imperial.Medieval.SpecialDamage;

[RegisterComponent]
public sealed partial class MedievalSpecialDamageDealerComponent : Component
{
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public string TargetType = string.Empty;
}

[RegisterComponent]
public sealed partial class MedievalSpecialDamageReceiverComponent : Component
{
    [DataField]
    public string TargetType = string.Empty;
}
