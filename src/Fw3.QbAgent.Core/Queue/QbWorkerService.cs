using System.Threading.Channels;
using Fw3.QbAgent.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fw3.QbAgent.Core.Queue;

/// <summary>
/// Owns the one thread that is allowed to talk to QuickBooks. The thread runs in a Single-Threaded
/// Apartment (STA) because the QuickBooks SDK is apartment-threaded COM: the session and all COM
/// calls must happen on the same STA thread. Because this is the only consumer of the queue, the
/// gateway implementation does not need to be thread-safe.
/// </summary>
public sealed class QbWorkerService : BackgroundService
{
    private readonly QbRequestQueue _queue;
    private readonly IQuickBooksGateway _gateway;
    private readonly ILogger<QbWorkerService> _logger;
    private Thread? _worker;

    public QbWorkerService(QbRequestQueue queue, IQuickBooksGateway gateway, ILogger<QbWorkerService> logger)
    {
        _queue = queue;
        _gateway = gateway;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A dedicated STA thread, not a thread-pool thread — thread-pool threads are MTA and would
        // marshal QuickBooks COM calls across apartments, which the SDK does not tolerate well.
        _worker = new Thread(() => RunLoop(stoppingToken))
        {
            IsBackground = true,
            Name = "qb-sta-worker",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Complete();                          // refuse new work; let the worker drain the rest
        await base.StopAsync(cancellationToken);    // cancels the stoppingToken handed to RunLoop
        _worker?.Join(TimeSpan.FromSeconds(30));     // wait for in-flight QuickBooks work to finish
    }

    private void RunLoop(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "QuickBooks STA worker started (apartment={Apartment}).",
            Thread.CurrentThread.GetApartmentState());

        var reader = _queue.Reader;
        try
        {
            while (true)
            {
                QbWorkItem item;
                try
                {
                    // Block this dedicated thread until work arrives, shutdown is requested, or the
                    // channel is completed and drained.
                    item = reader.ReadAsync(stoppingToken).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                using (_logger.BeginScope("QbOperation:{Operation}", item.Operation))
                {
                    item.Execute(_gateway);
                }
            }
        }
        finally
        {
            // Anything still queued at shutdown is failed deterministically — never silently dropped.
            while (reader.TryRead(out var pending))
            {
                pending.Cancel("Agent shut down before this request was processed.");
            }

            (_gateway as IDisposable)?.Dispose();
            _logger.LogInformation("QuickBooks STA worker stopped.");
        }
    }
}
