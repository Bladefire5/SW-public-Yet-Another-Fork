using Content.Client.Actions;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared.Mind;
using Content.Shared.Nocturn.Components;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Player;

namespace Content.Client.Nocturn;

public sealed class AncientNocturneActionOrderSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AncientNocturneComponent, LocalPlayerDetachedEvent>(
            OnPlayerDetached,
            before: new[] { typeof(ActionsSystem) });
        SubscribeLocalEvent<AncientNocturneComponent, LocalPlayerAttachedEvent>(
            OnPlayerAttached,
            after: new[] { typeof(ActionsSystem) });
    }

    private void OnPlayerDetached(
        Entity<AncientNocturneComponent> ent,
        ref LocalPlayerDetachedEvent args)
    {
        if (!TryGetLocalMind(out var mindUid))
            return;

        var state = EnsureComp<AncientNocturneActionOrderComponent>(mindUid);
        state.Actions.Clear();
        state.Actions.AddRange(_ui.GetUIController<ActionUIController>().GetActionOrder());
    }

    private void OnPlayerAttached(
        Entity<AncientNocturneComponent> ent,
        ref LocalPlayerAttachedEvent args)
    {
        if (!TryGetLocalMind(out var mindUid) ||
            !TryComp<AncientNocturneActionOrderComponent>(mindUid, out var state))
            return;

        _ui.GetUIController<ActionUIController>().RestoreActionOrder(state.Actions);
    }

    private bool TryGetLocalMind(out EntityUid mindUid)
    {
        mindUid = default;
        if (_player.LocalUser is not { } user ||
            !_mind.TryGetMind(user, out var mind, out _))
            return false;

        mindUid = mind.Value;
        return true;
    }
}
