using Content.Shared.Damage;
using Content.Shared.Imperial.Medieval.SpecialDamage;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server.Imperial.Medieval.SpecialDamage;

public sealed class MedievalSpecialDamageSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedievalSpecialDamageDealerComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, MedievalSpecialDamageDealerComponent component, MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var target in args.HitEntities)
        {
            if (!TryComp<MedievalSpecialDamageReceiverComponent>(target, out var receiver) ||
                receiver.TargetType != component.TargetType)
                continue;

            _damageable.TryChangeDamage(target, component.Damage, origin: args.User);
        }
    }
}
