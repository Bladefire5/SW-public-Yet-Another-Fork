using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules.SouthernTraderSpawn;

[AdminCommand(AdminFlags.Fun)]
public sealed class SpawnSouthernTraderRuleCommand : IConsoleCommand
{
    private const string RulePrototype = "MedievalSouthernTraderSpawnRule";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    public string Command => "spawnsoutherntrader";
    public string Description => "Spawns a Southern Lands trader and transfers the command user into it.";
    public string Help => $"Usage: {Command}";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { AttachedEntity: { } performer })
        {
            shell.WriteError("This command must be run through a client console while controlling an entity.");
            return;
        }

        var gameTicker = _entitySystemManager.GetEntitySystem<GameTicker>();
        var ruleUid = gameTicker.AddGameRule(RulePrototype);
        var rule = _entityManager.GetComponent<SouthernTraderSpawnRuleComponent>(ruleUid);
        rule.Performer = performer;
        gameTicker.StartGameRule(ruleUid);
    }
}
