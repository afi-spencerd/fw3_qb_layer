using System.Net;
using System.Net.Http.Json;
using Fw3.QbAgent.Core.Contracts;

namespace Fw3.QbAgent.Tests;

/// <summary>Full pipe for items against the Fixture gateway: list, get, create (inventory + non-inventory),
/// idempotency, and type-required-account validation.</summary>
public class ItemApiTests : IClassFixture<AgentApiFactory>
{
    private readonly AgentApiFactory _factory;

    public ItemApiTests(AgentApiFactory factory) => _factory = factory;

    private HttpClient Authed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return client;
    }

    [Fact]
    public async Task ListItems_returns_fixture_items_of_mixed_types()
    {
        var items = await Authed().GetFromJsonAsync<List<ItemDto>>("/items");

        Assert.NotNull(items);
        Assert.Equal(4, items!.Count);
        Assert.Contains(items, i => i.Name == "Bergamot Bloom 50ml" && i.Type == "Inventory");
        Assert.Contains(items, i => i.Type == "NonInventory");
    }

    [Fact]
    public async Task ListItems_respects_maxReturned_cap()
    {
        var items = await Authed().GetFromJsonAsync<List<ItemDto>>("/items?maxReturned=2");

        Assert.NotNull(items);
        Assert.True(items!.Count <= 2);
    }

    [Fact]
    public async Task GetItem_returns_inventory_item_with_accounts()
    {
        var item = await Authed().GetFromJsonAsync<ItemDto>("/items/80000102-1500000102");

        Assert.NotNull(item);
        Assert.Equal("Bergamot Bloom 50ml", item!.Name);
        Assert.Equal("Inventory Asset:Finished Goods Inventory", item.AssetAccountFullName);
        Assert.Equal("Sales:Fragrance Sales", item.IncomeAccountFullName);
    }

    [Fact]
    public async Task GetItem_unknown_is_404()
    {
        var response = await Authed().GetAsync("/items/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateInventoryItem_returns_201_with_ids()
    {
        var body = new
        {
            type = "Inventory",
            name = "Vetiver " + Guid.NewGuid().ToString("N")[..6],
            incomeAccountFullName = "Sales:Fragrance Sales",
            cogsOrExpenseAccountFullName = "Cost of Goods Sold:COGS - Finished Goods",
            assetAccountFullName = "Inventory Asset:Finished Goods Inventory",
        };

        var response = await PostItem(body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal("Inventory", dto!.Type);
        Assert.False(string.IsNullOrEmpty(dto.ListId));
        Assert.False(string.IsNullOrEmpty(dto.EditSequence));
    }

    [Fact]
    public async Task CreateInventoryItem_missing_accounts_is_400()
    {
        var body = new { type = "Inventory", name = "No Accounts Co" };
        var response = await PostItem(body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateNonInventoryItem_with_expense_only_succeeds()
    {
        var body = new
        {
            type = "NonInventory",
            name = "Freight " + Guid.NewGuid().ToString("N")[..6],
            cogsOrExpenseAccountFullName = "Cost of Goods Sold:Freight In",
            purchaseDescription = "Inbound freight",
        };

        var response = await PostItem(body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal("NonInventory", dto!.Type);
    }

    [Fact]
    public async Task CreateItem_is_idempotent_on_replay()
    {
        var key = Guid.NewGuid().ToString();
        var body = new
        {
            type = "Inventory",
            name = "Replay Item " + Guid.NewGuid().ToString("N")[..6],
            incomeAccountFullName = "Sales:Fragrance Sales",
            cogsOrExpenseAccountFullName = "Cost of Goods Sold:COGS - Finished Goods",
            assetAccountFullName = "Inventory Asset:Finished Goods Inventory",
        };

        var first = await PostItem(body, key);
        var second = await PostItem(body, key);

        var a = await first.Content.ReadFromJsonAsync<ItemDto>();
        var b = await second.Content.ReadFromJsonAsync<ItemDto>();
        Assert.Equal(a!.ListId, b!.ListId);
    }

    private Task<HttpResponseMessage> PostItem(object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/items")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", key);
        return Authed().SendAsync(request);
    }
}
