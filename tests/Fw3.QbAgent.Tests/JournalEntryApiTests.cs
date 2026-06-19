using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fw3.QbAgent.Core.Contracts;

namespace Fw3.QbAgent.Tests;

/// <summary>Full pipe for journal entries against the Fixture gateway: list, get, post, idempotency,
/// and structural validation.</summary>
public class JournalEntryApiTests : IClassFixture<AgentApiFactory>
{
    private readonly AgentApiFactory _factory;

    // The contract serializes enums as strings (e.g. "Debit"); match that when reading responses.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public JournalEntryApiTests(AgentApiFactory factory) => _factory = factory;

    private HttpClient Authed()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return client;
    }

    private static object WipMovement() => new
    {
        txnDate = "2026-06-19",
        refNumber = "WO-99001",
        lines = new object[]
        {
            new { type = "Debit", accountFullName = "Inventory Asset:WIP Inventory", amount = 1250.00m, memo = "RM issued" },
            new { type = "Credit", accountFullName = "Inventory Asset:Raw Materials Inventory", amount = 1250.00m },
        },
    };

    [Fact]
    public async Task ListJournalEntries_returns_fixture_entries_with_lines()
    {
        var entries = await Authed().GetFromJsonAsync<List<JournalEntryDto>>("/journal-entries", Json);

        Assert.NotNull(entries);
        Assert.Equal(2, entries!.Count);
        var wip = entries.Single(e => e.TxnId == "2F1A-1718800000");
        Assert.Equal(2, wip.Lines.Count);
    }

    [Fact]
    public async Task GetJournalEntry_unknown_is_404()
    {
        var response = await Authed().GetAsync("/journal-entries/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostJournalEntry_returns_201_with_txnId_and_lines()
    {
        var response = await Post(WipMovement(), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<JournalEntryDto>(Json);
        Assert.False(string.IsNullOrEmpty(dto!.TxnId));
        Assert.False(string.IsNullOrEmpty(dto.EditSequence));
        Assert.Equal(2, dto.Lines.Count);
        Assert.Equal($"/journal-entries/{dto.TxnId}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostJournalEntry_requires_idempotency_key()
    {
        var response = await Authed().PostAsJsonAsync("/journal-entries", WipMovement());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJournalEntry_rejects_empty_lines()
    {
        var body = new { txnDate = "2026-06-19", lines = Array.Empty<object>() };
        var response = await Post(body, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJournalEntry_is_idempotent_on_replay()
    {
        var key = Guid.NewGuid().ToString();
        var first = await Post(WipMovement(), key);
        var second = await Post(WipMovement(), key);

        var a = await first.Content.ReadFromJsonAsync<JournalEntryDto>(Json);
        var b = await second.Content.ReadFromJsonAsync<JournalEntryDto>(Json);
        Assert.Equal(a!.TxnId, b!.TxnId);
    }

    private Task<HttpResponseMessage> Post(object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/journal-entries")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", key);
        return Authed().SendAsync(request);
    }
}
