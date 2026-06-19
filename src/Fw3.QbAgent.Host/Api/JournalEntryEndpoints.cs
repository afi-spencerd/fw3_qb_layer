using System.Text.Json;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.Core.Idempotency;
using Fw3.QbAgent.Core.Queue;

namespace Fw3.QbAgent.Host.Api;

public static class JournalEntryEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapJournalEntryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/journal-entries").WithTags("JournalEntries");

        group.MapGet("/", async (DateTimeOffset? updatedSince, int? maxReturned, IQbRequestQueue queue, CancellationToken ct) =>
        {
            var entries = await queue.EnqueueAsync("JournalEntryQuery", gw => gw.QueryJournalEntries(updatedSince, maxReturned, ct), ct);
            return Results.Ok(entries);
        })
        .WithName("ListJournalEntries")
        .WithSummary("List journal entries, optionally only those modified at or after 'updatedSince'. 'maxReturned' caps the result; omit it to fetch all.")
        .Produces<IReadOnlyList<JournalEntryDto>>();

        group.MapGet("/{txnId}", async (string txnId, IQbRequestQueue queue, CancellationToken ct) =>
        {
            var entry = await queue.EnqueueAsync("JournalEntryQuery", gw => gw.GetJournalEntry(txnId, ct), ct)
                ?? throw QbAgentException.NotFound($"No journal entry with TxnID '{txnId}'.");
            return Results.Ok(entry);
        })
        .WithName("GetJournalEntry")
        .WithSummary("Fetch a single journal entry by its QuickBooks TxnID.")
        .Produces<JournalEntryDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateJournalEntryRequest body, HttpRequest request,
            IQbRequestQueue queue, IIdempotencyStore idempotency, CancellationToken ct) =>
        {
            if (body.Lines is null || body.Lines.Count == 0)
            {
                throw QbAgentException.Validation("A journal entry requires at least one line.");
            }

            var key = request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw QbAgentException.Validation($"A '{IdempotencyHeader}' header is required for writes.");
            }

            var requestHash = RequestHash.Compute(body);

            var outcome = await queue.EnqueueAsync("JournalEntryAdd", gw =>
            {
                if (idempotency.TryGet(key, out var prior))
                {
                    if (prior.Operation != "JournalEntryAdd" || prior.RequestHash != requestHash)
                    {
                        throw QbAgentException.IdempotencyConflict(
                            $"Idempotency-Key '{key}' was already used with a different request body.");
                    }

                    var replayed = JsonSerializer.Deserialize<JournalEntryDto>(prior.ResponseJson, AgentInfo.Json)!;
                    return new AddJournalEntryOutcome(replayed, Replayed: true);
                }

                var created = gw.AddJournalEntry(body, ct);
                idempotency.Save(new IdempotencyRecord
                {
                    Key = key,
                    Operation = "JournalEntryAdd",
                    RequestHash = requestHash,
                    StatusCode = StatusCodes.Status201Created,
                    ResponseJson = JsonSerializer.Serialize(created, AgentInfo.Json),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                return new AddJournalEntryOutcome(created, Replayed: false);
            }, ct);

            return Results.Created($"/journal-entries/{outcome.Entry.TxnId}", outcome.Entry);
        })
        .WithName("CreateJournalEntry")
        .WithSummary("Post a journal entry to QuickBooks. Requires an Idempotency-Key header; replays are safe. QuickBooks enforces that debits equal credits.")
        .Produces<JournalEntryDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private sealed record AddJournalEntryOutcome(JournalEntryDto Entry, bool Replayed);
}
