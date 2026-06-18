namespace Fw3.QbAgent.Core.Idempotency;

/// <summary>
/// The durable record of a completed write. On replay of the same idempotency key we return the
/// stored response verbatim instead of hitting QuickBooks again — so a network retry can never
/// create a duplicate customer/invoice.
/// </summary>
public sealed record IdempotencyRecord
{
    public required string Key { get; init; }

    /// <summary>Operation label, e.g. "CustomerAdd" — guards against reusing a key across operations.</summary>
    public required string Operation { get; init; }

    /// <summary>Hash of the original request body. A replay with a different body is a conflict, not a replay.</summary>
    public required string RequestHash { get; init; }

    /// <summary>HTTP status that was returned originally (e.g. 201).</summary>
    public required int StatusCode { get; init; }

    /// <summary>The exact JSON response returned originally.</summary>
    public required string ResponseJson { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
