using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fw3.QbAgent.Core.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Fw3.QbAgent.Tests;

/// <summary>
/// Exercises the full pipe — HTTP -> API key -> queue -> STA worker -> qbXML map -> JSON — against the
/// Fixture gateway, so it proves the round-trip without a live QuickBooks.
/// </summary>
public class CustomerApiTests : IClassFixture<AgentApiFactory>
{
    private const string ApiKey = "test-key";
    private readonly AgentApiFactory _factory;

    public CustomerApiTests(AgentApiFactory factory) => _factory = factory;

    private HttpClient Authed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    [Fact]
    public async Task Health_reports_fixture_mode_and_reachable()
    {
        var client = _factory.CreateClient(); // health is exempt from auth
        var health = await client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.NotNull(health);
        Assert.True(health!.QbReachable);
        Assert.Equal("Fixture", health.Mode);
        Assert.False(string.IsNullOrEmpty(health.AgentVersion));
    }

    [Fact]
    public async Task Customers_require_api_key()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListCustomers_returns_fixture_customers()
    {
        var customers = await Authed().GetFromJsonAsync<List<CustomerDto>>("/customers");

        Assert.NotNull(customers);
        Assert.Equal(2, customers!.Count);
        Assert.Contains(customers, c => c.Name == "Acme Manufacturing");
    }

    [Fact]
    public async Task GetCustomer_returns_404_for_unknown_listId()
    {
        var response = await Authed().GetAsync("/customers/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCustomer_requires_idempotency_key()
    {
        var response = await Authed().PostAsJsonAsync("/customers", new CreateCustomerRequest { Name = "No Key Co" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCustomer_returns_201_with_quickbooks_ids()
    {
        var created = await PostCustomer(new CreateCustomerRequest { Name = "Vertical Slice Co" }, key: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(dto);
        Assert.False(string.IsNullOrEmpty(dto!.ListId));
        Assert.False(string.IsNullOrEmpty(dto.EditSequence));
        Assert.Equal($"/customers/{dto.ListId}", created.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CreateCustomer_is_idempotent_on_replay()
    {
        var key = Guid.NewGuid().ToString();
        var body = new CreateCustomerRequest { Name = "Replay Co", Email = "ap@replay.example" };

        var first = await PostCustomer(body, key);
        var second = await PostCustomer(body, key); // same key, same body

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var a = await first.Content.ReadFromJsonAsync<CustomerDto>();
        var b = await second.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.Equal(a!.ListId, b!.ListId); // replay returns the original result, no duplicate create
    }

    [Fact]
    public async Task CreateCustomer_reusing_key_with_different_body_is_conflict()
    {
        var key = Guid.NewGuid().ToString();

        var first = await PostCustomer(new CreateCustomerRequest { Name = "Conflict Co A" }, key);
        var second = await PostCustomer(new CreateCustomerRequest { Name = "Conflict Co B" }, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private Task<HttpResponseMessage> PostCustomer(CreateCustomerRequest body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/customers")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", key);
        return Authed().SendAsync(request);
    }
}

/// <summary>Test host: Testing environment, Fixture mode, isolated temp dirs, a known API key.</summary>
public sealed class AgentApiFactory : WebApplicationFactory<Program>
{
    public string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "fw3qbagent-api-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QbAgent:Mode"] = "Fixture",
                ["QbAgent:FixturesPath"] = "fixtures",
                ["QbAgent:IdempotencyPath"] = Path.Combine(TempRoot, "idem"),
                ["QbAgent:QbXmlAuditPath"] = Path.Combine(TempRoot, "audit"),
                ["Auth:Enabled"] = "true",
                ["Auth:HeaderName"] = "X-Api-Key",
                ["Auth:ApiKeys:0"] = ApiKeyConst,
            });
        });
    }

    private const string ApiKeyConst = "test-key";

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
