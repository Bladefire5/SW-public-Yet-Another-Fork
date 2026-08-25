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
}
