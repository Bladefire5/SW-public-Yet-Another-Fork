using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.Imperial.SpawnOnAction.Components;
using Content.Shared.Imperial.SpawnOnAction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Imperial.Medieval;

[TestFixture]
public sealed class SpawnOnActionTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: SpawnOnActionTestSource
  components:
  - type: SpawnOnAction
    actionId: SpawnOnActionTestAction
    prototype: SpawnOnActionTestTarget

- type: entity
  id: SpawnOnActionTestAction
  components:
  - type: Action
  - type: TargetAction
  - type: WorldTargetAction
    event: !type:SpawnOnActionEvent

- type: entity
  id: SpawnOnActionTestTarget
""";

    [Test]
    public async Task FirstUseSpawnsAtTargetOnOffsetGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entityManager = server.EntMan;
            var transform = entityManager.System<SharedTransformSystem>();
            var actions = entityManager.System<SharedActionsSystem>();
            var gridTransform = entityManager.GetComponent<TransformComponent>(testMap.Grid);
            transform.SetWorldPosition((testMap.Grid, gridTransform), new Vector2(100f, 100f));

            var source = entityManager.SpawnEntity(SpawnOnActionTestSource, testMap.GridCoords);
            var component = entityManager.GetComponent<SpawnOnActionComponent>(source);
            var target = new EntityCoordinates(testMap.Grid, 1f, 1f);
            var action = actions.GetAction(component.Action);

            Assert.That(action, Is.Not.Null);

            actions.PerformAction(source, action!.Value, new SpawnOnActionEvent { Target = target });

            Assert.That(component.Object, Is.Not.Null);
            Assert.That(transform.GetMapCoordinates(component.Object!.Value), Is.EqualTo(transform.ToMapCoordinates(target)));
        });

        await pair.CleanReturnAsync();
    }

    private const string SpawnOnActionTestSource = "SpawnOnActionTestSource";
}
