using System.Collections.Generic;
using Content.Shared.DoAfter;
using Content.Shared.Imperial.Medieval.Skills;
using Content.Shared.Interaction;
using Robust.Shared.Network;

namespace Content.Shared.Imperial.Medieval.Ships.Anchor;

public sealed class MedievalAnchorSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedSkillsSystem _skills = default!;
    [Dependency] private readonly INetManager _net = default!;

    private readonly Dictionary<EntityUid, EntityUid> _activeUsers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<MedievalAnchorComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(EntityUid uid, MedievalAnchorComponent component, ActivateInWorldEvent args)
    {
        if (args.Handled || !TryStartUse(uid, component, args.User))
            return;

        args.Handled = true;
    }

    private bool TryStartUse(EntityUid uid, MedievalAnchorComponent component, EntityUid user)
    {
        if (_activeUsers.ContainsKey(uid) || !_skills.HasSkill(user, SharedSkillsSystem.StrengthId))
            return false;

        if (_net.IsServer)
            _activeUsers[uid] = user;

        var doAfter = new DoAfterArgs(EntityManager,
            user,
            GetUseTime(user, component),
            new ToggleAnchorEvent(),
            uid,
            target: uid)
        {
            MovementThreshold = 0.5f,
            BreakOnMove = true,
            CancelDuplicate = true,
            DistanceThreshold = 2,
            BreakOnDamage = true,
            RequireCanInteract = false,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            return true;

        if (_net.IsServer)
            _activeUsers.Remove(uid);

        return false;
    }

    private float GetUseTime(EntityUid user, MedievalAnchorComponent component)
    {
        var strength = _skills.GetSkillLevel(user, SharedSkillsSystem.StrengthId);
        var baseUseTime = float.IsFinite(component.BaseUseTime) ? component.BaseUseTime : 1f;
        var strengthModifier = float.IsFinite(component.StrengthUseTimeModifier)
            ? component.StrengthUseTimeModifier
            : 0f;
        var loweringMultiplier = float.IsFinite(component.LoweringTimeMultiplier)
            ? MathF.Max(0f, component.LoweringTimeMultiplier)
            : 1f;
        var useTime = MathF.Max(1f, baseUseTime - strength * strengthModifier);
        return component.Lowered ? useTime : MathF.Max(0.1f, useTime * loweringMultiplier);
    }

    public EntityUid? GetActiveUser(EntityUid uid)
    {
        return _activeUsers.GetValueOrDefault(uid);
    }

    public void ClearActiveUser(EntityUid uid)
    {
        _activeUsers.Remove(uid);
    }

}
