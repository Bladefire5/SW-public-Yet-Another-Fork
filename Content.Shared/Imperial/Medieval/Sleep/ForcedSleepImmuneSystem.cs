using Content.Shared.Bed.Sleep;
using Content.Shared.StatusEffectNew;

namespace Content.Shared.Imperial.Medieval.Sleep;

public sealed class ForcedSleepImmuneSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForcedSleepImmuneComponent, BeforeStatusEffectAddedEvent>(OnBeforeStatusEffectAdded);
    }

    private void OnBeforeStatusEffectAdded(Entity<ForcedSleepImmuneComponent> ent, ref BeforeStatusEffectAddedEvent args)
    {
        if (args.Effect == SleepingSystem.StatusEffectForcedSleeping)
            args.Cancelled = true;
    }
}
