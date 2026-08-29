using System.Linq;
using Content.Server.Destructible;
using Content.Server.Polymorph.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Imperial.Medieval.Additions;
using Content.Shared.Imperial.Medieval.Magic;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nocturn.Components;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server.Nocturn;

public sealed class AncientNocturneSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly NocturnBloodSpellSystem _bloodSpells = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneBatActionEvent>(OnBatAction);
        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneConversionActionEvent>(OnConversionAction);
        SubscribeLocalEvent<AncientNocturneComponent, AncientNocturneConversionDoAfterEvent>(OnConversionDoAfter);
        SubscribeLocalEvent<PolymorphedEntityComponent, PolymorphedEvent>(OnPolymorphed);
    }

    private void OnBatAction(Entity<AncientNocturneComponent> ent, ref AncientNocturneBatActionEvent args)
    {
        if (args.Handled)
            return;

        var action = args.Action.Owner;
        var beforeCast = new MedievalBeforeCastSpellEvent(ent.Owner, Transform(ent.Owner).Coordinates);
        RaiseLocalEvent(action, ref beforeCast);
        if (beforeCast.Cancelled)
            return;

        foreach (var held in _hands.EnumerateHeld(ent.Owner).ToArray())
        {
            if (!_hands.TryDrop(ent.Owner, held, checkActionBlocker: false))
            {
                _bloodSpells.ClearReservation(ent.Owner, action);
                return;
            }
        }

        if (_polymorph.PolymorphEntity(ent.Owner, ent.Comp.BatPolymorph) is not { } bat)
        {
            _bloodSpells.ClearReservation(ent.Owner, action);
            return;
        }

        RemComp<DestructibleComponent>(bat);
        CopyHealth(ent.Owner, bat);
        RaiseLocalEvent(action, new MedievalAfterCastSpellEvent
        {
            Action = action,
            Performer = ent.Owner
        });
        args.Handled = true;
    }

    private void OnConversionAction(
        Entity<AncientNocturneComponent> ent,
        ref AncientNocturneConversionActionEvent args)
    {
        if (args.Handled)
            return;

        if (!IsValidConversionTarget(args.Target, ent.Comp))
        {
            ShowInvalidConversionTarget(ent.Owner);
            args.Handled = true;
            return;
        }

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            ent.Owner,
            ent.Comp.ConversionDuration,
            new AncientNocturneConversionDoAfterEvent(),
            ent.Owner,
            target: args.Target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DuplicateCondition = DuplicateConditions.SameEvent,
            CancelDuplicate = true,
            BlockDuplicate = false
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
            args.Handled = true;
    }

    private void OnConversionDoAfter(
        Entity<AncientNocturneComponent> ent,
        ref AncientNocturneConversionDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        if (!IsValidConversionTarget(target, ent.Comp))
        {
            ShowInvalidConversionTarget(ent.Owner);
            return;
        }

        var inventory = GetInventorySnapshot(target);
        var hands = GetHandsSnapshot(target);
        var activeHand = _hands.GetActiveHand(target);

        if (_polymorph.PolymorphEntity(target, ent.Comp.ConversionPolymorph) is not { } converted)
            return;

        RestoreInventory(converted, inventory);
        RestoreHands(converted, hands, activeHand);

        RemComp<PolymorphedEntityComponent>(converted);
        _alerts.ClearAlert(converted, "SpawnProtection");
        RemComp<ShieldOnStartupComponent>(converted);
        QueueDel(target);

        var connection = EnsureComp<AncientNocturneMindConnectionComponent>(ent.Owner);
        var trall = EnsureComp<AncientNocturneTrallMindConnectionComponent>(converted);
        EnsureComp<AncientNocturneMindChatComponent>(converted);
        trall.Master = ent.Owner;
        connection.Tralls.Add(converted);

        SendConversionNotification(converted, AncientNocturneConversionNotification.Converted);
        if (!connection.HasConvertedTrall)
        {
            connection.HasConvertedTrall = true;
            SendConversionNotification(ent.Owner, AncientNocturneConversionNotification.FirstTrall);
        }

        _popup.PopupEntity(
            Loc.GetString("medieval-ancient-nocturne-conversion-success-user"),
            converted,
            ent.Owner,
            PopupType.Medium);
        _popup.PopupEntity(
            Loc.GetString("medieval-ancient-nocturne-conversion-success-target"),
            converted,
            converted,
            PopupType.Large);
    }

    private void SendConversionNotification(
        EntityUid recipient,
        AncientNocturneConversionNotification notification)
    {
        if (!TryComp<ActorComponent>(recipient, out var actor))
            return;

        RaiseNetworkEvent(new AncientNocturneConversionNotificationEvent(notification), actor.PlayerSession);
    }

    private void OnPolymorphed(Entity<PolymorphedEntityComponent> ent, ref PolymorphedEvent args)
    {
        if (!args.IsRevert ||
            !TryComp<AncientNocturneComponent>(args.NewEntity, out var ancient) ||
            !TryComp<ActionGrantComponent>(args.NewEntity, out var actionGrant))
            return;

        foreach (var actionUid in actionGrant.ActionEntities)
        {
            if (!TryComp<MetaDataComponent>(actionUid, out var metadata) ||
                metadata.EntityPrototype?.ID != ancient.BatAction.Id)
                continue;

            _actions.SetCooldown(actionUid, ancient.BatActionCooldown);
            break;
        }
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

    private List<(EntityUid Item, string Slot)> GetInventorySnapshot(EntityUid entity)
    {
        var snapshot = new List<(EntityUid Item, string Slot)>();
        if (!TryComp<InventoryComponent>(entity, out var inventory))
            return snapshot;

        var enumerator = _inventory.GetSlotEnumerator((entity, inventory));
        while (enumerator.NextItem(out var item, out var slot))
        {
            snapshot.Add((item, slot.Name));
        }

        return snapshot;
    }

    private List<(string Hand, EntityUid Item)> GetHandsSnapshot(EntityUid entity)
    {
        var snapshot = new List<(string Hand, EntityUid Item)>();
        foreach (var hand in _hands.EnumerateHands(entity))
        {
            if (_hands.TryGetHeldItem(entity, hand, out var item))
                snapshot.Add((hand, item.Value));
        }

        return snapshot;
    }

    private void RestoreInventory(EntityUid entity, List<(EntityUid Item, string Slot)> snapshot)
    {
        foreach (var (item, slot) in snapshot)
        {
            if (TerminatingOrDeleted(item))
                continue;

            if (_inventory.TryGetSlotEntity(entity, slot, out var equipped) && equipped == item)
                continue;

            _inventory.TryEquip(entity, item, slot, true, true, triggerHandContact: true);
        }
    }

    private void RestoreHands(
        EntityUid entity,
        List<(string Hand, EntityUid Item)> snapshot,
        string? activeHand)
    {
        foreach (var (_, item) in snapshot)
        {
            _hands.TryDrop(entity, item, checkActionBlocker: false);
        }

        foreach (var (hand, item) in snapshot)
        {
            if (!TerminatingOrDeleted(item))
                _hands.TryPickup(entity, item, hand, checkActionBlocker: false);
        }

        _hands.TrySetActiveHand(entity, activeHand);
    }

    private bool IsValidConversionTarget(EntityUid target, AncientNocturneComponent component)
    {
        return !TerminatingOrDeleted(target) &&
               TryComp<HumanoidAppearanceComponent>(target, out var appearance) &&
               appearance.Species == component.ConversionTargetSpecies;
    }

    private void ShowInvalidConversionTarget(EntityUid user)
    {
        _popup.PopupEntity(
            Loc.GetString("medieval-ancient-nocturne-conversion-invalid-target"),
            user,
            user,
            PopupType.Medium);
    }
}
