using Fw3.QbAgent.Core.Abstractions;

namespace Fw3.QbAgent.Core.Queue;

/// <summary>
/// The choke point in front of QuickBooks. Every operation that touches QuickBooks goes through here
/// so that — no matter how many REST calls arrive at once — exactly one request hits QuickBooks at a
/// time, on one dedicated thread. Callers enqueue work and await its result.
/// </summary>
public interface IQbRequestQueue
{
    /// <summary>
    /// Enqueue a unit of QuickBooks work and await its result. <paramref name="work"/> runs on the
    /// single STA worker thread; do not capture the HTTP request's async context inside it.
    /// </summary>
    /// <param name="operation">Short label for logging, e.g. "CustomerQuery".</param>
    /// <param name="work">Synchronous gateway call. Runs serialized with all other QB work.</param>
    /// <param name="cancellationToken">Typically HttpContext.RequestAborted.</param>
    Task<T> EnqueueAsync<T>(string operation, Func<IQuickBooksGateway, T> work, CancellationToken cancellationToken);
}
