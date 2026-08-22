using System.Linq;
using Content.Server.Destructible;
using Content.Server.Polymorph.Systems;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nocturn.Components;

namespace Content.Server.Nocturn;

public sealed class AncientNocturneSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneBatActionEvent>(OnBatAction);
    }

    private void OnBatAction(Entity<AncientNocturneComponent> ent, ref AncientNocturneBatActionEvent args)
    {
        if (args.Handled)
            return;

        foreach (var held in _hands.EnumerateHeld(ent.Owner).ToArray())
        {
            if (!_hands.TryDrop(ent.Owner, held, checkActionBlocker: false))
                return;
        }

        if (_polymorph.PolymorphEntity(ent.Owner, ent.Comp.BatPolymorph) is not { } bat)
            return;

        RemComp<DestructibleComponent>(bat);
        CopyHealth(ent.Owner, bat);
        args.Handled = true;
    }

    private void CopyHealth(EntityUid source, EntityUid target)
    {
        if (TryComp<MobThresholdsComponent>(source, out var sourceThresholds) &&
            TryComp<MobThresholdsComponent>(target, out var targetThresholds))
        {
            foreach (var (threshold, state) in sourceThresholds.Thresholds)
            {
                _mobThreshold.SetMobStateThreshold(target, threshold, state, targetThresholds);
            }
        }

        if (TryComp<DamageableComponent>(source, out var sourceDamage) &&
            TryComp<DamageableComponent>(target, out var targetDamage))
        {
            _damageable.SetDamage(target, targetDamage, new DamageSpecifier(sourceDamage.Damage));
        }
    }
}
