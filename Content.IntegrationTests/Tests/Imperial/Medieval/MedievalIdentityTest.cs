using System.Linq;
using System.Numerics;
using Content.Client.Verbs;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Imperial.Medieval.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests.Imperial.Medieval;

[TestFixture]
public sealed class MedievalIdentityTest
{
    [Test]
    public async Task IntroductionVerbSynchronizesThroughNetworkRequest()
    {
        var settings = new PoolSettings { Connected = true };
        await using var pair = await PoolManager.GetServerClient(settings);
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        var serverEntities = server.ResolveDependency<IEntityManager>();
        var clientEntities = client.ResolveDependency<IEntityManager>();
        var serverPlayerManager = server.ResolveDependency<IPlayerManager>();
        var serverSession = serverPlayerManager.Sessions.Single();
        EntityUid introducer = default;
        EntityUid observer = default;

        await server.WaitPost(() =>
        {
            EntityUid SpawnIdentityHolder()
            {
                var holder = serverEntities.CreateEntityUninitialized(null, map.GridCoords);
                serverEntities.AddComponent<IdentityComponent>(holder);
                serverEntities.AddComponent<IdentityRequiresKnowledgeComponent>(holder);
                serverEntities.InitializeAndStartEntity(holder, map.MapId);
                return holder;
            }

            introducer = SpawnIdentityHolder();
            serverEntities.System<MetaDataSystem>().SetEntityName(introducer, "Introducer Name");
            serverPlayerManager.SetAttachedEntity(serverSession, introducer);

            observer = SpawnIdentityHolder();
        });
        await pair.RunTicksSync(5);

        var clientIntroducer = client.Session!.AttachedEntity!.Value;
        var observerNet = serverEntities.GetNetEntity(observer);
        var clientObserver = clientEntities.GetEntity(observerNet);
        var clientTestSystem = client.System<MedievalIdentityTestSystem>();

        await server.WaitPost(() => serverEntities.EnsureComponent<StunnedComponent>(introducer));
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var verbs = client.System<VerbSystem>()
                .GetLocalVerbs(clientObserver, clientIntroducer, typeof(AlternativeVerb), force: false);
            Assert.That(verbs.Any(verb => verb.Text == Loc.GetString("imperial-hm-identity-intrd")), Is.False);
        });

        await client.WaitAssertion(() =>
        {
            clientTestSystem.PopupCount = 0;
            var verbs = client.System<VerbSystem>()
                .GetLocalVerbs(clientObserver, clientIntroducer, typeof(AlternativeVerb), force: true);
            var introduce = verbs.Single(verb => verb.Text == Loc.GetString("imperial-hm-identity-intrd"));
            client.System<VerbSystem>().ExecuteVerb(introduce, clientIntroducer, clientObserver, forced: true);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var introducerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(introducer);
            var observerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(observer);
            Assert.That(observerComp.KnownIds, Does.Not.Contain(introducerComp.Identifier));
        });
        await client.WaitAssertion(() => Assert.That(clientTestSystem.PopupCount, Is.Zero));

        await server.WaitPost(() => serverEntities.RemoveComponent<StunnedComponent>(introducer));
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
            serverEntities.System<SharedTransformSystem>().SetLocalPosition(observer, new Vector2(20f, 20f)));
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var verbs = client.System<VerbSystem>()
                .GetLocalVerbs(clientObserver, clientIntroducer, typeof(AlternativeVerb), force: true);
            var introduce = verbs.Single(verb => verb.Text == Loc.GetString("imperial-hm-identity-intrd"));
            client.System<VerbSystem>().ExecuteVerb(introduce, clientIntroducer, clientObserver, forced: true);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var introducerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(introducer);
            var observerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(observer);
            Assert.That(observerComp.KnownIds, Does.Not.Contain(introducerComp.Identifier));
        });
        await client.WaitAssertion(() => Assert.That(clientTestSystem.PopupCount, Is.Zero));

        await server.WaitPost(() =>
            serverEntities.System<SharedTransformSystem>().SetLocalPosition(observer, Vector2.Zero));
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var verbs = client.System<VerbSystem>()
                .GetLocalVerbs(clientObserver, clientIntroducer, typeof(AlternativeVerb), force: true);
            var introduce = verbs.Single(verb => verb.Text == Loc.GetString("imperial-hm-identity-intrd"));
            client.System<VerbSystem>().ExecuteVerb(introduce, clientIntroducer, clientObserver, forced: true);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var introducerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(introducer);
            var observerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(observer);
            Assert.That(observerComp.KnownIds, Does.Contain(introducerComp.Identifier));
            Assert.That(Identity.Name(introducer, serverEntities, observer), Is.EqualTo("Introducer Name"));
        });

        await client.WaitAssertion(() =>
        {
            var verbs = client.System<VerbSystem>()
                .GetLocalVerbs(clientObserver, clientIntroducer, typeof(AlternativeVerb), force: true);
            Assert.That(verbs.Any(verb => verb.Text == Loc.GetString("imperial-hm-identity-intrd")), Is.False);
            Assert.That(Identity.Name(clientIntroducer, clientEntities, clientObserver), Is.EqualTo("Introducer Name"));
            Assert.That(clientTestSystem.PopupCount, Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IntroductionSynchronizesDirectionalIdentityKnowledge()
    {
        var settings = new PoolSettings { Connected = true };
        await using var pair = await PoolManager.GetServerClient(settings);
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        var serverEntities = server.ResolveDependency<IEntityManager>();
        var clientEntities = client.ResolveDependency<IEntityManager>();
        var serverIdentity = server.System<Content.Server.Imperial.Medieval.IdentityManagement.MedievalIdentitySystem>();

        EntityUid introducer = default;
        EntityUid observer = default;
        await server.WaitPost(() =>
        {
            EntityUid SpawnIdentityHolder(string name)
            {
                var holder = serverEntities.CreateEntityUninitialized(null, map.GridCoords);
                serverEntities.AddComponent<IdentityComponent>(holder);
                serverEntities.AddComponent<IdentityRequiresKnowledgeComponent>(holder);
                serverEntities.InitializeAndStartEntity(holder, map.MapId);
                serverEntities.System<MetaDataSystem>().SetEntityName(holder, name);
                return holder;
            }

            introducer = SpawnIdentityHolder("Introducer Name");
            observer = SpawnIdentityHolder("Observer Name");
        });
        await pair.RunTicksSync(5);

        var introducerId = 0;
        await server.WaitAssertion(() =>
        {
            var introducerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(introducer);
            var observerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(observer);
            var unknownName = Identity.Name(introducer, serverEntities, observer);
            introducerId = introducerComp.Identifier;

            Assert.That(unknownName, Is.Not.EqualTo("Introducer Name"));
            Assert.That(observerComp.KnownIds, Does.Not.Contain(introducerComp.Identifier));
            Assert.That(introducerComp.KnownIds, Does.Not.Contain(observerComp.Identifier));
            Assert.That(serverIdentity.CanIntroduce(introducer, observer), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            var introducerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(introducer);
            var observerComp = serverEntities.GetComponent<IdentityRequiresKnowledgeComponent>(observer);

            serverEntities.System<SharedTransformSystem>().SetLocalPosition(observer, new Vector2(20f, 20f));
            Assert.That(serverIdentity.Introduce(introducer, observer), Is.False);
            Assert.That(observerComp.KnownIds, Does.Not.Contain(introducerComp.Identifier));

            serverEntities.System<SharedTransformSystem>().SetLocalPosition(observer, Vector2.Zero);

            serverEntities.EnsureComponent<StunnedComponent>(introducer);
            Assert.That(serverIdentity.Introduce(introducer, observer), Is.False);
            Assert.That(observerComp.KnownIds, Does.Not.Contain(introducerComp.Identifier));
            serverEntities.RemoveComponent<StunnedComponent>(introducer);

            Assert.That(serverIdentity.Introduce(introducer, observer), Is.True);
            Assert.That(observerComp.KnownIds, Does.Contain(introducerComp.Identifier));
            Assert.That(Identity.Name(introducer, serverEntities, observer), Is.EqualTo("Introducer Name"));
            Assert.That(introducerComp.KnownIds, Does.Not.Contain(observerComp.Identifier));
            Assert.That(Identity.Name(observer, serverEntities, introducer), Is.Not.EqualTo("Observer Name"));
            Assert.That(serverIdentity.CanIntroduce(introducer, observer), Is.False);
            Assert.That(serverIdentity.Introduce(introducer, observer), Is.False);
            Assert.That(observerComp.KnownIds.Count(id => id == introducerComp.Identifier), Is.EqualTo(1));
        });

        await pair.RunTicksSync(5);

        var introducerNet = serverEntities.GetNetEntity(introducer);
        var observerNet = serverEntities.GetNetEntity(observer);
        await client.WaitAssertion(() =>
        {
            var clientIntroducer = clientEntities.GetEntity(introducerNet);
            var clientObserver = clientEntities.GetEntity(observerNet);
            var clientObserverComp = clientEntities.GetComponent<IdentityRequiresKnowledgeComponent>(clientObserver);

            Assert.That(clientObserverComp.KnownIds, Does.Contain(introducerId));
            Assert.That(Identity.Name(clientIntroducer, clientEntities, clientObserver), Is.EqualTo("Introducer Name"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LearnedIdentityPersistsAcrossVisualDisguises()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entities = server.ResolveDependency<IEntityManager>();
        var identity = server.System<Content.Server.Imperial.Medieval.IdentityManagement.MedievalIdentitySystem>();
        var inventory = server.System<InventorySystem>();

        EntityUid introducer = default;
        EntityUid observer = default;
        EntityUid helmet = default;
        EntityUid apron = default;

        await server.WaitPost(() =>
        {
            introducer = entities.SpawnEntity("MobHuman", map.GridCoords);
            observer = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<IdentityRequiresKnowledgeComponent>(introducer);
            entities.EnsureComponent<IdentityRequiresKnowledgeComponent>(observer);
            entities.System<MetaDataSystem>().SetEntityName(introducer, "Introducer Name");

            helmet = entities.SpawnEntity("ClothingHeadHelmetEVA", map.GridCoords);
            apron = entities.SpawnEntity("ClothingOuterApron", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(identity.Introduce(introducer, observer), Is.True);
            Assert.That(Identity.Name(introducer, entities, observer), Is.EqualTo("Introducer Name"));
            Assert.That(inventory.TryEquip(introducer, helmet, "head"), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var introducerComp = entities.GetComponent<IdentityRequiresKnowledgeComponent>(introducer);
            var observerComp = entities.GetComponent<IdentityRequiresKnowledgeComponent>(observer);

            Assert.That(observerComp.KnownIds, Does.Contain(introducerComp.Identifier));
            Assert.That(Identity.Name(introducer, entities, observer), Is.Not.EqualTo("Introducer Name"));

            Assert.That(inventory.TryUnequip(introducer, "head", true), Is.True);
            Assert.That(inventory.TryEquip(introducer, apron, "outerClothing"), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(Identity.Name(introducer, entities, observer), Is.EqualTo("Introducer Name"));
        });

        await pair.CleanReturnAsync();
    }
}

public sealed class MedievalIdentityTestSystem : EntitySystem
{
    public int PopupCount;

    public override void Initialize()
    {
        SubscribeNetworkEvent<PopupEntityEvent>(_ => PopupCount++);
    }
}
