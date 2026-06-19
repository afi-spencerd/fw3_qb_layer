using Fw3.QbAgent.Core.Contracts;

namespace Fw3.QbAgent.Core.Abstractions;

/// <summary>
/// The single seam between the agent and QuickBooks. Everything above this interface (HTTP, queue,
/// idempotency) is QuickBooks-agnostic; everything below it (qbXML, COM) never leaks past it.
/// <para>
/// All methods are synchronous and are ALWAYS invoked on the single dedicated STA worker thread
/// (see the QB request queue). Implementations therefore do not need to be thread-safe, but they
/// MUST clean up any QuickBooks session even on failure.
/// </para>
/// </summary>
public interface IQuickBooksGateway
{
    /// <summary>Probe QuickBooks reachability and company-file visibility. Never throws for "not reachable" — reports it.</summary>
    QbHealth CheckHealth(CancellationToken ct);

    /// <summary>
    /// Query customers, optionally only those modified at/after <paramref name="updatedSince"/>.
    /// <paramref name="maxReturned"/> caps the result; when null, the full set is fetched in chunks.
    /// </summary>
    IReadOnlyList<CustomerDto> QueryCustomers(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct);

    /// <summary>Fetch a single customer by its QuickBooks ListID, or null if not found.</summary>
    CustomerDto? GetCustomer(string listId, CancellationToken ct);

    /// <summary>Create a customer in QuickBooks and return it including its new ListID and EditSequence.</summary>
    CustomerDto AddCustomer(CreateCustomerRequest request, CancellationToken ct);

    /// <summary>Query items (all types), optionally only those modified at/after <paramref name="updatedSince"/>.
    /// <paramref name="maxReturned"/> caps the result; when null, the full set is fetched in chunks.</summary>
    IReadOnlyList<ItemDto> QueryItems(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct);

    /// <summary>Fetch a single item by its QuickBooks ListID, or null if not found.</summary>
    ItemDto? GetItem(string listId, CancellationToken ct);

    /// <summary>Create an item in QuickBooks and return it including its new ListID and EditSequence.</summary>
    ItemDto AddItem(CreateItemRequest request, CancellationToken ct);

    /// <summary>Query journal entries, optionally only those modified at/after <paramref name="updatedSince"/>.
    /// <paramref name="maxReturned"/> caps the result; when null, the full set is fetched in chunks.</summary>
    IReadOnlyList<JournalEntryDto> QueryJournalEntries(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct);

    /// <summary>Fetch a single journal entry by its QuickBooks TxnID, or null if not found.</summary>
    JournalEntryDto? GetJournalEntry(string txnId, CancellationToken ct);

    /// <summary>Post a journal entry to QuickBooks and return it including its new TxnID and EditSequence.</summary>
    JournalEntryDto AddJournalEntry(CreateJournalEntryRequest request, CancellationToken ct);
}
