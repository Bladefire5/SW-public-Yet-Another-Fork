using Content.Shared.Administration;
using Robust.Client.Player;
using Robust.Shared.Console;

namespace Content.Client.Imperial.Medieval.Praises;

[AnyCommand] //should be an admin command but server will check if user is an admin anyway so it's easier to implement it on the client
public sealed class AddPraiseCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;

    public string Command => "praises_add";

    public string Description => "";

    public string Help => "praises_add USERNAME WEIGHT / REASON ... Reason may include multiple words but must be separated by '/' from the rest of the command.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteError("Command must have atleast three arguments.");
            return;
        }

        if (!_playerMan.TryGetPlayerDataByUsername(args[0], out var data))
        {
            shell.WriteError("Failed to resolve user ID by name.");
            return;
        }

        if (!int.TryParse(args[1], out var weight))
        {
            shell.WriteError("Weight must be an integer.");
            return;
        }

        string reason = argStr.Split('/')[1].Trim();
        _entMan.EntitySysManager.GetEntitySystem<PraiseSystem>().SendPraise(data.UserId, reason, weight);
        shell.WriteLine("Praise added.");
    }
}
