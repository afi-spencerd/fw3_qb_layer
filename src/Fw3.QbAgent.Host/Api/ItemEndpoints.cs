using System.Text.Json;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.Core.Idempotency;
using Fw3.QbAgent.Core.Queue;

namespace Fw3.QbAgent.Host.Api;

public static class ItemEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/items").WithTags("Items");

        group.MapGet("/", async (DateTimeOffset? updatedSince, int? maxReturned, IQbRequestQueue queue, CancellationToken ct) =>
        {
            var items = await queue.EnqueueAsync("ItemQuery", gw => gw.QueryItems(updatedSince, maxReturned, ct), ct);
            return Results.Ok(items);
        })
        .WithName("ListItems")
        .WithSummary("List items (all types), optionally only those modified at or after 'updatedSince'. 'maxReturned' caps the result; omit it to fetch all.")
        .Produces<IReadOnlyList<ItemDto>>();

        group.MapGet("/{listId}", async (string listId, IQbRequestQueue queue, CancellationToken ct) =>
        {
            var item = await queue.EnqueueAsync("ItemQuery", gw => gw.GetItem(listId, ct), ct)
                ?? throw QbAgentException.NotFound($"No item with ListID '{listId}'.");
            return Results.Ok(item);
        })
        .WithName("GetItem")
        .WithSummary("Fetch a single item by its QuickBooks ListID.")
        .Produces<ItemDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateItemRequest body, HttpRequest request,
            IQbRequestQueue queue, IIdempotencyStore idempotency, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                throw QbAgentException.Validation("'name' is required.");
            }

            var key = request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw QbAgentException.Validation($"A '{IdempotencyHeader}' header is required for writes.");
            }

            var requestHash = RequestHash.Compute(body);

            var outcome = await queue.EnqueueAsync("ItemAdd", gw =>
            {
                if (idempotency.TryGet(key, out var prior))
                {
                    if (prior.Operation != "ItemAdd" || prior.RequestHash != requestHash)
                    {
                        throw QbAgentException.IdempotencyConflict(
                            $"Idempotency-Key '{key}' was already used with a different request body.");
                    }

                    var replayed = JsonSerializer.Deserialize<ItemDto>(prior.ResponseJson, AgentInfo.Json)!;
                    return new AddItemOutcome(replayed, Replayed: true);
                }

                var created = gw.AddItem(body, ct);
                idempotency.Save(new IdempotencyRecord
                {
                    Key = key,
                    Operation = "ItemAdd",
                    RequestHash = requestHash,
                    StatusCode = StatusCodes.Status201Created,
                    ResponseJson = JsonSerializer.Serialize(created, AgentInfo.Json),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                return new AddItemOutcome(created, Replayed: false);
            }, ct);

            return Results.Created($"/items/{outcome.Item.ListId}", outcome.Item);
        })
        .WithName("CreateItem")
        .WithSummary("Create an item in QuickBooks. Requires an Idempotency-Key header; replays are safe.")
        .Produces<ItemDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private sealed record AddItemOutcome(ItemDto Item, bool Replayed);
}
