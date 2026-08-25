using System.Linq;
using Content.Server.Stack;
using Content.Server.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.SmithingSystem;
using Content.Shared.Imperial.Medieval.SmithingSystem.Behaviours;
using Content.Shared.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading.Prototypes;
using Content.Shared.Imperial.Medieval.Chemistry;
using Content.Shared.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.MedievalMeleeResource.Components;
using Content.Shared.Store;
using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Imperial.Medieval;

[TestFixture]
public sealed class TradingMarketTest
{
    private static readonly EntProtoId WeakReliefStone = "MedievalReliefStoneWeak";
    private static readonly EntProtoId StrongReliefStone = "MedievalReliefStoneStrong";
    private static readonly EntProtoId TrophyBowl = "FoodBowlBig";
    private static readonly ProtoId<CurrencyPrototype> Revent = "Revent";

    [Test]
    public void ClientMarketStateDoesNotExposeStandardPrice()
    {
        Assert.That(typeof(TradingMarketItemState).GetField("StandardPrice"), Is.Null);
        Assert.That(typeof(TradingMarketItemState).GetProperty("StandardPrice"), Is.Null);
    }

    [Test]
    public async Task TrophySaleCreditsTheUsedPit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var system = server.System<TradingSystem>();
            var pitUid = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var pit = server.EntMan.EnsureComponent<TradingComponent>(pitUid);
            pit.Currency = Revent;
            var otherPitUid = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var otherPit = server.EntMan.EnsureComponent<TradingComponent>(otherPitUid);
            otherPit.Currency = Revent;
            var trophy = server.EntMan.SpawnEntity(TrophyBowl, MapCoordinates.Nullspace);
            var currency = server.EntMan.GetComponent<MedievalCurrencyComponent>(trophy);

            Assert.That(system.TryAddCurrency((trophy, currency), (pitUid, pit)), Is.True);
            Assert.That(pit.Balance, Is.EqualTo(5));
            Assert.That(otherPit.Balance, Is.Zero);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CatalogUsesPrototypePricesAndExcludesTrophies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitPost(() => server.System<TradingSystem>().CreateMarket());

        await server.WaitAssertion(() =>
        {
            var query = server.EntMan.EntityQueryEnumerator<TradingMarketComponent>();
            Assert.That(query.MoveNext(out _, out var market), Is.True);
            Assert.That(query.MoveNext(out _, out _), Is.False);

            var config = server.ProtoMan.Index(market.Config);
            foreach (var guildTypeId in config.GuildTypes)
            {
                var guildType = server.ProtoMan.Index(guildTypeId);
                foreach (var item in guildType.Items)
                {
                    var prototype = server.ProtoMan.Index(item.ProductEntity);
                    if (prototype.HasComponent<MedievalCurrencyComponent>(server.EntMan.ComponentFactory) ||
                        prototype.HasComponent<MedievalRandomChemistryRecipeComponent>(server.EntMan.ComponentFactory))
                    {
                        Assert.That(market.CommonCommodities.ContainsKey(item.ProductEntity), Is.False);
                        continue;
                    }

                    Assert.That(market.CommonCommodities.TryGetValue(item.ProductEntity, out var commodityId), Is.True);
                    Assert.That(market.Commodities.TryGetValue(commodityId, out var commodity), Is.True);
                    Assert.That(commodity!.Sections, Is.EqualTo(TradingMarketSection.Common));
                    Assert.That(commodity.StandardPrice, Is.EqualTo(item.Cost));
                    prototype.TryGetComponent<StackComponent>(out var stack, server.EntMan.ComponentFactory);
                    Assert.That(commodity.HasStack, Is.EqualTo(stack != null));
                    Assert.That(commodity.BaselineStackCount, Is.EqualTo(stack?.Count ?? 1));
                }
            }

            foreach (var offer in market.Offers.Values.Where(offer => offer.Side == TradingOfferSide.Sell))
            {
                Assert.That(offer.Item, Is.Not.Null);
                Assert.That(server.EntMan.EntityExists(offer.Item));
                Assert.That(market.Escrow.Contains(offer.Item!.Value), Is.True);
            }

            foreach (var guild in market.Guilds)
            {
                foreach (var product in guild.Items.Select(item => item.ProductEntity).Distinct())
                {
                    if (!market.CommonCommodities.TryGetValue(product, out var commodityId))
                        continue;

                    var guildOffers = market.Offers.Values.Count(offer =>
                        offer.ParticipantKind == TradingParticipantKind.Guild &&
                        offer.GuildId == guild.Id &&
                        offer.CommodityId == commodityId);
                    Assert.That(guildOffers, Is.EqualTo(1),
                        $"Guild {guild.Name} must have one offer for {product}");
                }
            }

            Assert.That(market.CommonCommodities.ContainsKey(WeakReliefStone), Is.True);
            Assert.That(market.CommonCommodities.ContainsKey(StrongReliefStone), Is.True);

            var weakStone = server.ProtoMan.Index<EntityPrototype>(WeakReliefStone);
            var strongStone = server.ProtoMan.Index<EntityPrototype>(StrongReliefStone);
            Assert.That(weakStone.HasComponent<MedievalCurrencyComponent>(server.EntMan.ComponentFactory), Is.False);
            Assert.That(strongStone.HasComponent<MedievalCurrencyComponent>(server.EntMan.ComponentFactory), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayerLotsKeepWholeStacksAndUseStackVariant()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var system = server.System<TradingSystem>();
            system.CreateMarket();

            var query = server.EntMan.EntityQueryEnumerator<TradingMarketComponent>();
            Assert.That(query.MoveNext(out var marketUid, out var market), Is.True);
            var common = market.Commodities.Values.First(commodity =>
                commodity.Permanent && commodity.HasStack && commodity.BaselineStackCount > 1);

            var fullStack = server.EntMan.SpawnEntity(common.Product, MapCoordinates.Nullspace);
            var fullStackComp = server.EntMan.GetComponent<StackComponent>(fullStack);
            Assert.That(fullStackComp.Count, Is.EqualTo(common.BaselineStackCount));

            var pitUid = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var pit = server.EntMan.EnsureComponent<TradingComponent>(pitUid);
            Assert.That(system.TryCreateTraderSellOffer(
                (marketUid, market),
                (pitUid, pit),
                "Trader",
                fullStack,
                common.StandardPrice,
                out var commodityId), Is.True);
            Assert.That(commodityId, Is.EqualTo(common.Id));
            Assert.That(fullStackComp.Count, Is.EqualTo(common.BaselineStackCount));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(fullStack).ParentUid, Is.EqualTo(pitUid));
            Assert.That(market.Offers.Values.Any(offer => offer.Item == fullStack), Is.True);
            Assert.That(market.Offers.Values.Any(offer => offer.Pit == pitUid), Is.True);

            var unusualStack = server.EntMan.SpawnEntity(common.Product, MapCoordinates.Nullspace);
            var unusualStackComp = server.EntMan.GetComponent<StackComponent>(unusualStack);
            server.System<StackSystem>().SetCount(unusualStack, common.BaselineStackCount - 1, unusualStackComp);
            Assert.That(system.TryResolveCommodityForItem(
                (marketUid, market),
                unusualStack,
                common.StandardPrice,
                true,
                out var unusual), Is.True);
            Assert.That(unusual.Id, Is.Not.EqualTo(common.Id));
            Assert.That(unusual.Sections, Is.EqualTo(TradingMarketSection.Unique));
            Assert.That(unusual.BaselineStackCount, Is.EqualTo(common.BaselineStackCount - 1));

            var buyerPitUid = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var buyerPit = server.EntMan.EnsureComponent<TradingComponent>(buyerPitUid);
            buyerPit.Balance = unusual.StandardPrice * 2;
            Assert.That(system.TryCreateTraderSellOffer(
                (marketUid, market),
                (pitUid, pit),
                "Seller",
                unusualStack,
                unusual.StandardPrice,
                out var soldCommodityId), Is.True);
            Assert.That(soldCommodityId, Is.EqualTo(unusual.Id));
            Assert.That(system.CreateTraderBuyOffer(
                (marketUid, market),
                (buyerPitUid, buyerPit),
                "Buyer",
                unusual,
                unusual.StandardPrice), Is.True);
            Assert.That(buyerPit.Balance, Is.EqualTo(unusual.StandardPrice));

            var config = server.ProtoMan.Index(market.Config);
            system.MatchCommodity((marketUid, market), unusual, config);
            Assert.That(buyerPit.StoredMarketItems, Does.Contain(unusualStack));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(unusualStack).ParentUid, Is.EqualTo(buyerPitUid));
            Assert.That(pit.Balance, Is.EqualTo(unusual.StandardPrice));

            var reserved = unusual.StandardPrice;
            var balanceBeforeReservation = buyerPit.Balance;
            Assert.That(system.CreateTraderBuyOffer(
                (marketUid, market),
                (buyerPitUid, buyerPit),
                "Buyer",
                common,
                reserved), Is.True);
            var orderId = buyerPit.MarketOffers.Single();
            Assert.That(buyerPit.Balance, Is.EqualTo(balanceBeforeReservation - reserved));
            system.RemoveOffer((marketUid, market), orderId, true, config);
            Assert.That(buyerPit.Balance, Is.EqualTo(balanceBeforeReservation));
            Assert.That(buyerPit.MarketOffers, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EquipmentQualityAndRecipeSectionsAreExact()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var system = server.System<TradingSystem>();
            system.CreateMarket();

            var query = server.EntMan.EntityQueryEnumerator<TradingMarketComponent>();
            Assert.That(query.MoveNext(out var marketUid, out var market), Is.True);
            var common = market.Commodities.Values.First(commodity =>
                commodity.Permanent &&
                server.ProtoMan.Index(commodity.Product).HasComponent<MedievalMeleeResourceComponent>(server.EntMan.ComponentFactory));

            var pristine = server.EntMan.SpawnEntity(common.Product, MapCoordinates.Nullspace);
            var quality = server.EntMan.EnsureComponent<SmithQualityComponent>(pristine);
            quality.Applied = true;
            quality.Quality = ItemQuality.Excellent;
            Assert.That(system.TryResolveCommodityForItem(
                (marketUid, market),
                pristine,
                common.StandardPrice,
                true,
                out var pristineCommodity), Is.True);
            Assert.That(pristineCommodity.Sections,
                Is.EqualTo(TradingMarketSection.Common | TradingMarketSection.Unique));
            Assert.That(pristineCommodity.GuildEligible, Is.True);
            Assert.That(pristineCommodity.StandardPrice, Is.EqualTo(common.StandardPrice));
            Assert.That(pristineCommodity.QualityMultiplier,
                Is.EqualTo(ItemQualityDurabilityMultipliers.Get(ItemQuality.Excellent)));

            var damaged = server.EntMan.SpawnEntity(common.Product, MapCoordinates.Nullspace);
            server.EntMan.GetComponent<MedievalMeleeResourceComponent>(damaged).Resource = 60f;
            Assert.That(system.TryResolveCommodityForItem(
                (marketUid, market),
                damaged,
                common.StandardPrice,
                true,
                out var damagedCommodity), Is.True);
            Assert.That(damagedCommodity.Sections, Is.EqualTo(TradingMarketSection.Unique));
            Assert.That(damagedCommodity.GuildEligible, Is.False);

            var recipe = server.EntMan.CreateEntityUninitialized("MedievalChemistryRecipe", MapCoordinates.Nullspace);
            server.EntMan.InitializeAndStartEntity(recipe, false);
            Assert.That(system.TryResolveCommodityForItem(
                (marketUid, market),
                recipe,
                25,
                true,
                out var recipeCommodity), Is.True);
            Assert.That(recipeCommodity.Sections, Is.EqualTo(TradingMarketSection.Unique));
            Assert.That(recipeCommodity.GuildEligible, Is.False);
            Assert.That(market.CommonCommodities.ContainsKey("MedievalChemistryRecipe"), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeletingPitRemovesOffersAndDropsStoredItems()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var system = server.System<TradingSystem>();
            system.CreateMarket();

            var query = server.EntMan.EntityQueryEnumerator<TradingMarketComponent>();
            Assert.That(query.MoveNext(out var marketUid, out var market), Is.True);
            var commodity = market.Commodities.Values.First(value => value.Permanent && !value.HasStack);
            var pitUid = server.EntMan.SpawnEntity(null, testMap.GridCoords);
            var pit = server.EntMan.EnsureComponent<TradingComponent>(pitUid);
            pit.Balance = commodity.StandardPrice;

            var sellItem = server.EntMan.SpawnEntity(commodity.Product, testMap.GridCoords);
            Assert.That(system.TryCreateTraderSellOffer(
                (marketUid, market),
                (pitUid, pit),
                "Trader",
                sellItem,
                commodity.StandardPrice,
                out _), Is.True);
            Assert.That(system.CreateTraderBuyOffer(
                (marketUid, market),
                (pitUid, pit),
                "Trader",
                commodity,
                commodity.StandardPrice), Is.True);

            var storedItem = server.EntMan.SpawnEntity(commodity.Product, testMap.GridCoords);
            var containers = server.EntMan.System<SharedContainerSystem>();
            var container = containers.EnsureContainer<Container>(pitUid, TradingComponent.MarketContainerId);
            Assert.That(containers.Insert(storedItem, container, force: true), Is.True);
            pit.StoredMarketItems.Add(storedItem);
            var dropCoordinates = server.EntMan.System<SharedTransformSystem>().GetMapCoordinates(pitUid);

            server.EntMan.DeleteEntity(pitUid);

            Assert.That(market.Offers.Values.Any(offer => offer.Pit == pitUid), Is.False);
            Assert.That(server.EntMan.EntityExists(sellItem), Is.True);
            Assert.That(server.EntMan.EntityExists(storedItem), Is.True);
            Assert.That(server.EntMan.System<SharedTransformSystem>().GetMapCoordinates(sellItem), Is.EqualTo(dropCoordinates));
            Assert.That(server.EntMan.System<SharedTransformSystem>().GetMapCoordinates(storedItem), Is.EqualTo(dropCoordinates));
        });

        await pair.CleanReturnAsync();
    }
}
