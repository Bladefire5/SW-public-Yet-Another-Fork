using Content.Server.Popups;
using Content.Shared.Communications;
using Content.Shared.Imperial.Medieval.CCVar;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Server.Imperial.Medieval.Horn;

public sealed class MedievalHornPlaytimeRequirementSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly ISharedPlaytimeManager _playtimeManager = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MedievalHornPlaytimeRequirementComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
    }

    private void OnMessageAttempt(
        Entity<MedievalHornPlaytimeRequirementComponent> ent,
        ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.Cancelled || args.Message is not CommunicationsConsoleAnnounceMessage)
            return;

        if (!_playerManager.TryGetSessionByEntity(args.Actor, out var session))
        {
            args.Cancel();
            return;
        }

        var requiredMinutes = _configuration.GetCVar(MedievalCCVars.MedievalTotalMinutesForHornRequired);
        var playtimes = _playtimeManager.GetPlayTimes(session);
        var totalPlaytime = playtimes.GetValueOrDefault(PlayTimeTrackingShared.TrackerOverall);

        if (totalPlaytime.TotalMinutes >= requiredMinutes)
            return;

        _popup.PopupEntity(
            Loc.GetString("medieval-horn-account-too-new-popup"),
            ent.Owner,
            args.Actor,
            PopupType.MediumCaution);
        args.Cancel();
    }
}
