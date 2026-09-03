using Content.Shared.Administration;
using Robust.Client.Player;
using Robust.Shared.Console;

namespace Content.Client.Imperial.Medieval.Praises;

[AnyCommand] //should be an admin command but server will check if user is an admin anyway so it's easier to implement it on the client
public sealed class ViewPraisesCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "praises_view";

    public string Description => "";

    public string Help => "praises_view USERNAME";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Command requires a single argument (target's username).");
            return;
        }

        if (!_playerMan.TryGetPlayerDataByUsername(args[0], out var data))
        {
            shell.WriteError("Failed to resolve user ID by name.");
            return;
        }

        _entMan.EntitySysManager.GetEntitySystem<PraiseSystem>().OpenView(data.UserId);
    }
}
