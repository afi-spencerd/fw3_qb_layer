using System.Diagnostics.CodeAnalysis;

namespace Fw3.QbAgent.Core.Idempotency;

/// <summary>
/// Durable store of completed writes, keyed by the ERP-supplied idempotency key. Accessed only from
/// the single STA worker thread (inside enqueued work), so the check-then-write is naturally
/// race-free without distributed locking.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns true and the stored record if this key has already been processed.</summary>
    bool TryGet(string key, [NotNullWhen(true)] out IdempotencyRecord? record);

    /// <summary>Persist the outcome of a write so future replays of the same key are safe.</summary>
    void Save(IdempotencyRecord record);
}
