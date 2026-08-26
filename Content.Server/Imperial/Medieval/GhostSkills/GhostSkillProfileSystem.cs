using Content.Server.Imperial.Medieval.Skills;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Imperial.Medieval.GhostSkills;
using Content.Shared.Imperial.Medieval.Skills;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Imperial.Medieval.GhostSkills;

public sealed class GhostSkillProfileSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GhostSkillProfileComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GhostSkillProfileComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GhostSkillProfileComponent, OpenGhostSkillsActionEvent>(OnOpenAction);
        SubscribeLocalEvent<GhostSkillProfileComponent, ApplyGhostSkillsToRoleEvent>(OnApplyToRole);
        SubscribeNetworkEvent<SaveGhostSkillsMessage>(OnSave);
    }

    private void OnMapInit(Entity<GhostSkillProfileComponent> ent, ref MapInitEvent args)
    {
        if (!SharedSkillsSystem.TryValidateSkillLevels(_prototypes, ent.Comp.Levels, out var levels))
            levels = SharedSkillsSystem.GetDefaultSkillLevels(_prototypes);

        ent.Comp.Levels = levels;
        _actions.AddAction(ent.Owner, ref ent.Comp.Action, ent.Comp.ActionPrototype);
    }

    private void OnShutdown(Entity<GhostSkillProfileComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.Action);
    }

    private void OnOpenAction(Entity<GhostSkillProfileComponent> ent, ref OpenGhostSkillsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        RaiseNetworkEvent(new OpenGhostSkillsMenuMessage(new(ent.Comp.Levels)), ent.Owner);
    }

    private void OnSave(SaveGhostSkillsMessage message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid ||
            !TryComp<GhostSkillProfileComponent>(uid, out var component))
            return;

        if (!SharedSkillsSystem.TryValidateSkillLevels(_prototypes, message.Levels, out var levels))
        {
            _popup.PopupEntity(Loc.GetString("ghost-skills-invalid-points"), uid, uid, PopupType.MediumCaution);
            return;
        }

        component.Levels = levels;
        RaiseNetworkEvent(new GhostSkillsSavedMessage(), args.SenderSession);
        _popup.PopupEntity(Loc.GetString("ghost-skills-saved"), uid, uid);
    }

    private void OnApplyToRole(Entity<GhostSkillProfileComponent> ent, ref ApplyGhostSkillsToRoleEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Target) &&
            !HasComp<SkillsComponent>(args.Target) &&
            !(HasComp<BodyComponent>(args.Target) &&
              HasComp<HandsComponent>(args.Target) &&
              HasComp<InventoryComponent>(args.Target)))
            return;

        _skills.ApplySkills(args.Target, ent.Comp.Levels);
    }
}
