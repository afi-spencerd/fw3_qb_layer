using Fw3.QbAgent.Core.Abstractions;

namespace Fw3.QbAgent.Core.Queue;

/// <summary>
/// One unit of QuickBooks work waiting in the queue. The HTTP request that enqueued it awaits
/// <see cref="Completion"/>; the single STA worker runs <see cref="Work"/> and completes it.
/// </summary>
internal sealed class QbWorkItem
{
    public QbWorkItem(string operation, Func<IQuickBooksGateway, object?> work,
        TaskCompletionSource<object?> completion, CancellationToken cancellationToken)
    {
        Operation = operation;
        Work = work;
        Completion = completion;
        CancellationToken = cancellationToken;
    }

    /// <summary>Short operation label for logging, e.g. "CustomerAdd".</summary>
    public string Operation { get; }

    public Func<IQuickBooksGateway, object?> Work { get; }

    public TaskCompletionSource<object?> Completion { get; }

    /// <summary>Cancellation from the originating HTTP request (RequestAborted).</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Runs on the STA worker thread. Translates the synchronous gateway call into a completed task.
    /// Never throws — failures flow back to the awaiting HTTP request through the completion source.
    /// </summary>
    public void Execute(IQuickBooksGateway gateway)
    {
        if (CancellationToken.IsCancellationRequested)
        {
            Completion.TrySetCanceled(CancellationToken);
            return;
        }

        try
        {
            Completion.TrySetResult(Work(gateway));
        }
        catch (OperationCanceledException)
        {
            Completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            Completion.TrySetException(ex);
        }
    }

    /// <summary>Completes the item as cancelled, e.g. when the agent is shutting down.</summary>
    public void Cancel(string reason) =>
        Completion.TrySetException(new OperationCanceledException(reason));
}
