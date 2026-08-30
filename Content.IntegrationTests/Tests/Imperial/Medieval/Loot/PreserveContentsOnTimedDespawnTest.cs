using Content.Server.Imperial.Medieval.Ships.PlayerDrowning;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests.Imperial.Medieval.Loot;

[TestFixture]
[NonParallelizable]
public sealed class PreserveContentsOnTimedDespawnTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestTimedDespawnUndrowableItem
  parent: Crowbar
  components:
  - type: Undrowable
";

    [Test]
    public async Task MobsAndUndrowableItemsSurviveChestDespawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        var entMan = server.ResolveDependency<IEntityManager>();
        var containerSystem = server.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var chest = entMan.SpawnEntity("MedievalCrateGeneric", map.GridCoords);
            var mob = entMan.SpawnEntity("MobMonkey", map.GridCoords);
            var protectedItem = entMan.SpawnEntity("TestTimedDespawnUndrowableItem", map.GridCoords);
            var regularItem = entMan.SpawnEntity("Crowbar", map.GridCoords);

            Assert.That(containerSystem.TryGetContainer(chest, "entity_storage", out var container), Is.True);
            Assert.That(containerSystem.Insert(mob, container), Is.True);
            Assert.That(containerSystem.Insert(protectedItem, container), Is.True);
            Assert.That(containerSystem.Insert(regularItem, container), Is.True);

            var despawn = new TimedDespawnEvent();
            entMan.EventBus.RaiseLocalEvent(chest, ref despawn);

            Assert.That(container.Contains(mob), Is.False);
            Assert.That(container.Contains(protectedItem), Is.False);
            Assert.That(container.Contains(regularItem), Is.True);

            entMan.DeleteEntity(chest);

            Assert.That(entMan.EntityExists(mob), Is.True);
            Assert.That(entMan.EntityExists(protectedItem), Is.True);
            Assert.That(entMan.EntityExists(regularItem), Is.False);
            Assert.That(entMan.HasComponent<UndrowableComponent>(protectedItem), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
