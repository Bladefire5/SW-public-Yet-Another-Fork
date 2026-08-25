using System.Reflection;
using Content.Client.Imperial.Medieval.Trading;
using Content.Shared.Imperial.Medieval.Trading;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Imperial.Medieval;

[TestFixture]
public sealed class TradingMenuTest
{
    [Test]
    public async Task ClosingMenuCancelsPreviewRetries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var menu = new TradingMenu();
            menu.Open();
            menu.UpdateState(new TradingUpdateState(
                [],
                [],
                [new TradingStoredItemState(NetEntity.Invalid, "MissingPrototype", "Test item")],
                0,
                "Revent"));
            menu.Close();
            ((IDisposable) menu).Dispose();
        });

        await client.WaitRunTicks(5);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissingForeignOfferPreviewDoesNotRetry()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var menu = new TradingMenu();
            var state = new TradingUpdateState(
                [],
                [new TradingMarketOfferState(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "MissingPrototype",
                    TradingOfferSide.Sell,
                    TradingParticipantKind.Guild,
                    "Guild",
                    1,
                    false,
                    "Test item",
                    NetEntity.Invalid)],
                [],
                0,
                "Revent");
            var method = typeof(TradingMenu).GetMethod(
                "HasMissingPreviews",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.Invoke(menu, [state]), Is.False);
            ((IDisposable) menu).Dispose();
        });

        await pair.CleanReturnAsync();
    }
}
