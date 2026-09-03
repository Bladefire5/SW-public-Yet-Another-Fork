using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Shared.Imperial.DeimonFly;

/// <summary>
/// Убирает только положительный урон типа Heat, не затрагивая лечение и другие типы урона.
/// </summary>
public sealed class DeimonFlyFireImmunitySystem : EntitySystem
{
    private const string HeatDamageType = "Heat";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeimonFlyFireImmunityComponent, BeforeDamageChangedEvent>(OnBeforeDamageChanged);
        SubscribeLocalEvent<DeimonFlyFireImmunityComponent, DamageModifyEvent>(OnDamageModify);
    }

    /// <summary>
    /// Температурная система наносит чистый Heat с обходом обычных сопротивлений.
    /// Отменяем такой пакет целиком, не изменяя переданный источником DamageSpecifier.
    /// </summary>
    private static void OnBeforeDamageChanged(
        EntityUid uid,
        DeimonFlyFireImmunityComponent component,
        ref BeforeDamageChangedEvent args)
    {
        if (!args.Damage.DamageDict.TryGetValue(HeatDamageType, out var heatDamage) ||
            heatDamage <= FixedPoint2.Zero)
        {
            return;
        }

        foreach (var (damageType, damageAmount) in args.Damage.DamageDict)
        {
            if (damageType != HeatDamageType && damageAmount != FixedPoint2.Zero)
                return;
        }

        args.Cancelled = true;
    }

    private static void OnDamageModify(
        EntityUid uid,
        DeimonFlyFireImmunityComponent component,
        DamageModifyEvent args)
    {
        if (!args.Damage.DamageDict.TryGetValue(HeatDamageType, out var heatDamage) ||
            heatDamage <= FixedPoint2.Zero)
        {
            return;
        }

        // Создаём копию, чтобы не изменить исходный DamageSpecifier оружия или способности.
        var damageWithoutHeat = new DamageSpecifier(args.Damage);
        damageWithoutHeat.DamageDict.Remove(HeatDamageType);
        args.Damage = damageWithoutHeat;
    }
}
