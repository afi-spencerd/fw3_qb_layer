using System.Threading.Channels;
using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Errors;

namespace Fw3.QbAgent.Core.Queue;

/// <summary>
/// Channel-backed single-consumer queue. Many producers (HTTP requests) can write; exactly one
/// consumer (the STA worker) reads. Registered as a singleton.
/// </summary>
public sealed class QbRequestQueue : IQbRequestQueue
{
    private readonly Channel<QbWorkItem> _channel = Channel.CreateUnbounded<QbWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,   // the one STA worker
            SingleWriter = false,  // many concurrent HTTP requests
        });

    /// <summary>Consumed only by the worker.</summary>
    internal ChannelReader<QbWorkItem> Reader => _channel.Reader;

    public Task<T> EnqueueAsync<T>(string operation, Func<IQuickBooksGateway, T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // RunContinuationsAsynchronously: the awaiting HTTP request resumes on the thread pool, never
        // on the STA worker thread — that thread must stay dedicated to QuickBooks.
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new QbWorkItem(operation, gw => work(gw), completion, cancellationToken);

        if (!_channel.Writer.TryWrite(item))
        {
            // Unbounded channel only refuses writes once completed (i.e. during shutdown).
            throw QbAgentException.Unreachable("The agent is shutting down and is not accepting new work.");
        }

        return AwaitTypedAsync(completion.Task);

        static async Task<T> AwaitTypedAsync(Task<object?> task) => (T)(await task.ConfigureAwait(false))!;
    }

    /// <summary>Stops accepting new work; the worker drains what remains and then exits.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
