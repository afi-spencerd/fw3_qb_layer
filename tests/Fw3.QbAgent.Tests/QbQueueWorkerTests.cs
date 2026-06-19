using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Queue;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fw3.QbAgent.Tests;

public class QbQueueWorkerTests
{
    [Fact]
    public async Task Worker_runs_all_work_serialized_on_one_sta_thread()
    {
        var gateway = new ConcurrencyTrackingGateway();
        var queue = new QbRequestQueue();
        var worker = new QbWorkerService(queue, gateway, NullLogger<QbWorkerService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Fire many requests at once; the queue must funnel them through one at a time.
            var calls = Enumerable.Range(0, 25)
                .Select(_ => queue.EnqueueAsync("CustomerQuery", gw => gw.QueryCustomers(null, null, CancellationToken.None), CancellationToken.None))
                .ToArray();

            await Task.WhenAll(calls);

            Assert.Equal(25, gateway.TotalCalls);
            Assert.Equal(1, gateway.MaxObservedConcurrency);     // never more than one QB call in flight
            Assert.Single(gateway.ThreadIds);                    // always the same worker thread
            Assert.True(gateway.WasAlwaysSta);                   // and it is an STA thread
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueAsync_propagates_gateway_exceptions_to_the_caller()
    {
        var queue = new QbRequestQueue();
        var worker = new QbWorkerService(queue, new ThrowingGateway(), NullLogger<QbWorkerService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                queue.EnqueueAsync("CustomerQuery", gw => gw.QueryCustomers(null, null, CancellationToken.None), CancellationToken.None));
            Assert.Equal("boom", ex.Message);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class ConcurrencyTrackingGateway : IQuickBooksGateway
    {
        private int _current;
        public int MaxObservedConcurrency;
        public int TotalCalls;
        public bool WasAlwaysSta = true;
        public readonly HashSet<int> ThreadIds = new();

        public IReadOnlyList<CustomerDto> QueryCustomers(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _current);
            lock (ThreadIds)
            {
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, now);
                TotalCalls++;
                ThreadIds.Add(Environment.CurrentManagedThreadId);
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                {
                    WasAlwaysSta = false;
                }
            }

            Thread.Sleep(5); // hold the "session" briefly so genuine concurrency would be observable
            Interlocked.Decrement(ref _current);
            return [];
        }

        public QbHealth CheckHealth(CancellationToken ct) => new() { QbReachable = true, CompanyFileOpen = true };
        public CustomerDto? GetCustomer(string listId, CancellationToken ct) => null;
        public CustomerDto AddCustomer(CreateCustomerRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<ItemDto> QueryItems(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct) => [];
        public ItemDto? GetItem(string listId, CancellationToken ct) => null;
        public ItemDto AddItem(CreateItemRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<JournalEntryDto> QueryJournalEntries(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct) => [];
        public JournalEntryDto? GetJournalEntry(string txnId, CancellationToken ct) => null;
        public JournalEntryDto AddJournalEntry(CreateJournalEntryRequest request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ThrowingGateway : IQuickBooksGateway
    {
        public IReadOnlyList<CustomerDto> QueryCustomers(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct) => throw new InvalidOperationException("boom");
        public QbHealth CheckHealth(CancellationToken ct) => new() { QbReachable = true, CompanyFileOpen = true };
        public CustomerDto? GetCustomer(string listId, CancellationToken ct) => null;
        public CustomerDto AddCustomer(CreateCustomerRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<ItemDto> QueryItems(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct) => [];
        public ItemDto? GetItem(string listId, CancellationToken ct) => null;
        public ItemDto AddItem(CreateItemRequest request, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<JournalEntryDto> QueryJournalEntries(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct) => [];
        public JournalEntryDto? GetJournalEntry(string txnId, CancellationToken ct) => null;
        public JournalEntryDto AddJournalEntry(CreateJournalEntryRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
