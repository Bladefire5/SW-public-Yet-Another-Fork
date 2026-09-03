using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Content.Shared.Imperial.Medieval.Forged;
using Content.Shared.Body.Events;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using Content.Shared.Movement.Systems;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;
using Content.Shared.Explosion.Components;
using Content.Shared.Explosion;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.Containers;
using Content.Shared.Verbs;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared.Forged;

public sealed class ForgedSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private readonly SharedBodySystem _bodySystem = default!;
    [Dependency] private readonly ForgedAbilitySystem _forgedAbility = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgedComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<ForgedComponent, BeingGibbedEvent>(OnGibbed);

        SubscribeLocalEvent<ForgedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed, after: new[] { typeof(ItemSlotsSystem), typeof(ContainerSystem), typeof(SharedContainerSystem) });
        SubscribeLocalEvent<ForgedComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<ForgedComponent, GetExplosionResistanceEvent>(OnExplosionResistance);

        SubscribeLocalEvent<ForgedComponent, InteractUsingEvent>(OnInteractUsing);

        SubscribeLocalEvent<ForgedComponent, GetVerbsEvent<EquipmentVerb>>(OnGetVerbs);

        SubscribeLocalEvent<ForgedComponent, ForgedAssemblyDoAfterEvent>(OnDoAfter);

    }

    private void OnMapInit(EntityUid uid, ForgedComponent component, MapInitEvent args)
    {
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            UpdateAppearance((uid, component, appearance));

        SetBaseModuleParams(uid);

        InitModules(uid);
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
    }

    private void SetBaseModuleParams(EntityUid uid)
    {
        if (!TryComp<ForgedComponent>(uid, out var comp))
            return;

        foreach (var (_, moduleUid) in comp.FittedModules)
        {
            if (!TryComp<ForgedModuleComponent>(moduleUid, out var module))
                continue;

            module.BaseResistanceModifier = module.ResistanceModifier;
            module.BaseSpeedModifier = module.SpeedModifier;
        }
    }

    private void SetupCore(EntityUid uid, EntityUid moduleId)
    {
        Timer.Spawn(0, () =>
        {
            var test = _bodySystem.GetBodyChildren(uid).ToList();
            EntityUid? torsoId = null;
            foreach (var part in _bodySystem.GetBodyChildren(uid))
            {
                if (part.Component.PartType == BodyPartType.Torso)
                {
                    torsoId = part.Id;
                    break;
                }
            }
            if (torsoId == null) return;

            foreach (var organ in _bodySystem.GetBodyOrgans(uid))
            {
                if (HasComp<StomachComponent>(organ.Id))
                {
                    _bodySystem.RemoveOrgan(organ.Id, organ.Component);
                    QueueDel(organ.Id);
                    break;
                }
            }

            _bodySystem.InsertOrgan(torsoId.Value, moduleId, "stomach");
        });
    }

    private void OnGetVerbs(Entity<ForgedComponent> forgedEntity, ref GetVerbsEvent<EquipmentVerb> args)
    {
        var user = args.User;

        if (forgedEntity.Owner != args.User)
            return;

        var removeCategory = new VerbCategory($"{Loc.GetString("forged-remove-category-verb")}", null);

        foreach (var (_, moduleUid) in forgedEntity.Comp.FittedModules)
        {
            if (!TryComp<ForgedModuleComponent>(moduleUid, out var module))
                continue;

            if (!module.IsReplaceable)
                continue;

            if (!moduleUid.IsValid())
                continue;

            EquipmentVerb verb = new()
            {
                Text = $"{Name(moduleUid)}",
                Category = removeCategory,
                Act = () =>
                {
                    var doAfterArgs = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(15f), new ForgedAssemblyDoAfterEvent { Inserting = false, SlotId = module.ModuleSlot }, forgedEntity, target: forgedEntity)
                    {
                        BreakOnMove = true,
                        BreakOnDamage = true,
                        NeedHand = true
                    };
                    _doAfter.TryStartDoAfter(doAfterArgs);
                }
            };
            args.Verbs.Add(verb);
        }
    }

    private string GetContainerId(string slotId)
    {
        if (slotId == "head") return "forgedhead";
        if (slotId == "eyes") return "forgedeyes";
        return slotId;
    }

    public void InitModules(EntityUid uid)
    {
        if (!TryComp<ForgedComponent>(uid, out var comp))
            return;

        foreach (var (_, moduleUid) in comp.FittedModules)
        {
            if (!TryComp<ForgedModuleComponent>(moduleUid, out var module))
                continue;

            if (module.ModuleSlot == "core")
            {
                SetupCore(uid, moduleUid);
            }
            else
            {
                var containerId = GetContainerId(module.ModuleSlot);
                var container = _containerSystem.EnsureContainer<ContainerSlot>(uid, containerId);
                _containerSystem.Insert(moduleUid, container);
            }

            Timer.Spawn(0, () =>
            {
                if (module.AbilityId != null)
                    _forgedAbility.ExecuteAbility(uid, moduleUid, module.AbilityId);
            });
        }
    }

    private void OnInteractUsing(EntityUid uid, ForgedComponent comp, InteractUsingEvent args)
    {
        if (args.Handled) return;

        if (_hands.TryGetActiveItem(uid, out var activeItem))
        {
            ProtoId<TagPrototype> tag = "ForgedArmCrossbow";
            if (_tagSystem.HasTag(activeItem.Value, tag))
            {
                var weaponInteractArgs = new InteractUsingEvent(args.User, args.Used, activeItem.Value, args.ClickLocation);
                RaiseLocalEvent(activeItem.Value, weaponInteractArgs);

                if (weaponInteractArgs.Handled)
                {
                    args.Handled = true;
                    return;
                }
            }
        }

        if (TryComp<ForgedModuleComponent>(args.Used, out var module))
        {
            var containerId = GetContainerId(module.ModuleSlot);
            var container = _containerSystem.EnsureContainer<ContainerSlot>(uid, containerId);

            if (container.Count > 0)
            {
                if (_net.IsServer)
                    _popup.PopupEntity(Loc.GetString("forged-slot-occupied"), uid, args.User);
                return;
            }

            if (!string.IsNullOrEmpty(module.RequiredModule))
            {
                var reqContainerId = GetContainerId(module.RequiredModule);
                if (!_containerSystem.TryGetContainer(uid, reqContainerId, out var reqContainer) || reqContainer.Count == 0)
                {
                    if (_net.IsServer)
                        _popup.PopupEntity(Loc.GetString("forged-base-module-required"), uid, args.User);
                    return;
                }
            }

            var doAfterArgs = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(15f), new ForgedAssemblyDoAfterEvent { Inserting = true, SlotId = module.ModuleSlot }, uid, target: uid, used: args.Used)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true,
                CancelDuplicate = true
            };

            _doAfter.TryStartDoAfter(doAfterArgs);
            args.Handled = true;
        }
    }

    private void OnDoAfter(EntityUid uid, ForgedComponent component, ForgedAssemblyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (_net.IsClient)
            return;

        if (args.Inserting)
        {
            if (args.Args.Used == null || !TryComp<ForgedModuleComponent>(args.Args.Used.Value, out var module))
                return;

            var containerId = GetContainerId(module.ModuleSlot);
            var container = _containerSystem.EnsureContainer<ContainerSlot>(uid, containerId);

            if (container.Count > 0)
                return;

            if (!string.IsNullOrEmpty(module.RequiredModule))
            {
                var reqContainerId = GetContainerId(module.RequiredModule);
                if (!_containerSystem.TryGetContainer(uid, reqContainerId, out var reqContainer) || reqContainer.Count == 0)
                    return;
            }

            if (_containerSystem.Insert(args.Args.Used.Value, container))
            {
                component.FittedModules[module.ModuleSlot] = args.Args.Used.Value;
                Dirty(uid, component);

                _popup.PopupEntity(Loc.GetString("forged-insert-success", ("item", Name(args.Args.Used.Value))), uid, args.User);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/buckle.ogg"), uid);

                if (module.AbilityId != null)
                    _forgedAbility.ExecuteAbility(uid, args.Args.Used.Value, module.AbilityId);

                _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
            }
        }
        else
        {
            var containerId = GetContainerId(args.SlotId);
            if (!_containerSystem.TryGetContainer(uid, containerId, out var container) || container.Count == 0)
                return;

            var moduleUid = container.ContainedEntities[0];
            if (_containerSystem.TryRemoveFromContainer(moduleUid))
            {
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/unbuckle.ogg"), uid);

                if (TryComp<ForgedModuleComponent>(moduleUid, out var moduleComp))
                {
                    component.FittedModules.Remove(moduleComp.ModuleSlot);
                    Dirty(uid, component);

                    if (moduleComp.AbilityId != null)
                        _forgedAbility.RemoveAbility(uid, moduleUid, moduleComp.AbilityId);
                }

                DropDependentModules(uid, args.SlotId, args.User);
                _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(uid);
            }
        }

        if (TryComp<AppearanceComponent>(uid, out var appearance))
            UpdateAppearance((uid, component, appearance));

        args.Handled = true;
    }

    private void DropDependentModules(EntityUid uid, string parentSlotId, EntityUid user)
    {
        var dependencies = new List<string>();

        foreach (var container in _containerSystem.GetAllContainers(uid))
        {
            if (container.Count == 0) continue;

            var entity = container.ContainedEntities[0];
            if (TryComp<ForgedModuleComponent>(entity, out var module) && module.RequiredModule == parentSlotId)
            {
                dependencies.Add(container.ID);
            }
        }

        foreach (var slotId in dependencies)
        {
            if (_containerSystem.TryGetContainer(uid, slotId, out var container) && container.Count > 0)
            {
                var dependentEntity = container.ContainedEntities[0];
                if (_containerSystem.TryRemoveFromContainer(dependentEntity))
                {
                    if (TryComp<ForgedModuleComponent>(dependentEntity, out var dependentComp))
                    {
                        if (TryComp<ForgedComponent>(uid, out var forgedComp))
                        {
                            forgedComp.FittedModules.Remove(dependentComp.ModuleSlot);
                            Dirty(uid, forgedComp);
                        }

                        if (dependentComp.AbilityId != null)
                            _forgedAbility.RemoveAbility(uid, dependentEntity, dependentComp.AbilityId);
                    }
                    DropDependentModules(uid, slotId, user);
                }
            }
        }
    }

    private void OnGibbed(EntityUid uid, ForgedComponent component, BeingGibbedEvent args)
    {
        foreach (var (slotId, moduleUid) in component.FittedModules)
        {
            if (TerminatingOrDeleted(moduleUid)) continue;
            if (!TryComp<ForgedModuleComponent>(moduleUid, out var module)) continue;
            if (_containerSystem.TryGetContainer(uid, slotId, out var container))
            {
                if (!container.Contains(moduleUid))
                {
                    continue;
                }

                _containerSystem.Remove(moduleUid, container, force: true);

                if (slotId == "torso" || module.AbilityId == "Torso_Explosion")
                {
                    QueueDel(moduleUid);
                    continue;
                }

                if (_random.Prob(0.25f))
                {
                    QueueDel(moduleUid);
                }
            }
        }
    }
    private void UpdateAppearance(Entity<ForgedComponent?, AppearanceComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp1, ref ent.Comp2, logMissing: false)) return;

        foreach (ForgedVisuals visualKey in Enum.GetValues(typeof(ForgedVisuals)))
        {
            string key = visualKey.ToString();
            if (ent.Comp1.FittedModules.TryGetValue(key, out var moduleUid) && moduleUid.IsValid() && TryComp<ForgedModuleComponent>(moduleUid, out var module))
            {
                ForgedVisualsPacket packet = new ForgedVisualsPacket(module.LayerState, module.RsiPath);
                _appearanceSystem.SetData(ent, visualKey, packet, ent.Comp2);
            }
            else
            {
                ForgedVisualsPacket packet = new ForgedVisualsPacket(string.Empty, ResPath.Empty);
                _appearanceSystem.SetData(ent, visualKey, packet, ent.Comp2);
            }
        }
    }

    private float GetModuleSpeedModifier(ForgedComponent component)
    {
        float speedMod = 1f;

        foreach (var (state, moduleUid) in component.FittedModules)
        {
            if (TryComp<ForgedModuleComponent>(moduleUid, out var module))
                speedMod += module.SpeedModifier;
        }

        return Math.Max(0.1f, speedMod);
    }

    private float GetModuleResistanceModifier(ForgedComponent component)
    {
        float damageMod = 1f;

        foreach (var (state, moduleUid) in component.FittedModules)
        {
            if (TryComp<ForgedModuleComponent>(moduleUid, out var module))
                damageMod -= module.ResistanceModifier;
        }

        return Math.Max(0.01f, damageMod);
    }

    private void OnExplosionResistance(EntityUid uid, ForgedComponent component, ref GetExplosionResistanceEvent args)
    {
        float mod = GetModuleResistanceModifier(component);

        args.DamageCoefficient *= mod;
    }

    private void OnRefreshSpeed(EntityUid uid, ForgedComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        float mod = GetModuleSpeedModifier(component);
        args.ModifySpeed(mod, mod);
    }

    private void OnDamageModify(EntityUid uid, ForgedComponent component, DamageModifyEvent args)
    {
        float mod = GetModuleResistanceModifier(component);
        args.Damage *= mod;
    }
}
