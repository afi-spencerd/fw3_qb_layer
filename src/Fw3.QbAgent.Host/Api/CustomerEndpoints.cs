using System.Text.Json;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.Core.Idempotency;
using Fw3.QbAgent.Core.Queue;

namespace Fw3.QbAgent.Host.Api;

public static class CustomerEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers").WithTags("Customers");

        group.MapGet("/", async (DateTimeOffset? updatedSince, IQbRequestQueue queue, CancellationToken ct) =>
        {
            var customers = await queue.EnqueueAsync("CustomerQuery", gw => gw.QueryCustomers(updatedSince, ct), ct);
            return Results.Ok(customers);
        })
        .WithName("ListCustomers")
        .WithSummary("List customers, optionally only those modified at or after 'updatedSince'.")
        .Produces<IReadOnlyList<CustomerDto>>();

        group.MapGet("/{listId}", async (string listId, IQbRequestQueue queue, CancellationToken ct) =>
        {
            var customer = await queue.EnqueueAsync("CustomerQuery", gw => gw.GetCustomer(listId, ct), ct)
                ?? throw QbAgentException.NotFound($"No customer with ListID '{listId}'.");
            return Results.Ok(customer);
        })
        .WithName("GetCustomer")
        .WithSummary("Fetch a single customer by its QuickBooks ListID.")
        .Produces<CustomerDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateCustomerRequest body, HttpRequest request,
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

            // The whole check-create-record sequence runs on the single QB worker thread, so a retry
            // that races the original can never produce two customers.
            var outcome = await queue.EnqueueAsync("CustomerAdd", gw =>
            {
                if (idempotency.TryGet(key, out var prior))
                {
                    if (prior.Operation != "CustomerAdd" || prior.RequestHash != requestHash)
                    {
                        throw QbAgentException.IdempotencyConflict(
                            $"Idempotency-Key '{key}' was already used with a different request body.");
                    }

                    var replayed = JsonSerializer.Deserialize<CustomerDto>(prior.ResponseJson, AgentInfo.Json)!;
                    return new AddOutcome(replayed, Replayed: true);
                }

                var created = gw.AddCustomer(body, ct);
                idempotency.Save(new IdempotencyRecord
                {
                    Key = key,
                    Operation = "CustomerAdd",
                    RequestHash = requestHash,
                    StatusCode = StatusCodes.Status201Created,
                    ResponseJson = JsonSerializer.Serialize(created, AgentInfo.Json),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });
                return new AddOutcome(created, Replayed: false);
            }, ct);

            // A replay returns the original result with the original 201 — same contract either way.
            return Results.Created($"/customers/{outcome.Customer.ListId}", outcome.Customer);
        })
        .WithName("CreateCustomer")
        .WithSummary("Create a customer in QuickBooks. Requires an Idempotency-Key header; replays are safe.")
        .Produces<CustomerDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private sealed record AddOutcome(CustomerDto Customer, bool Replayed);
}
