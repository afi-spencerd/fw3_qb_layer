using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Configuration;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.QuickBooks.Connection;
using Fw3.QbAgent.QuickBooks.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fw3.QbAgent.QuickBooks.Gateways;

/// <summary>
/// The real gateway. Opens a QuickBooks session per operation (guaranteeing clean teardown), submits
/// the qbXML produced by <see cref="CustomerMapper"/>, audits the exact request/response exactly once,
/// and never returns success on a QuickBooks error.
/// </summary>
public sealed class LiveQuickBooksGateway : IQuickBooksGateway
{
    private readonly IQbConnectionFactory _connections;
    private readonly IQbXmlAuditLog _audit;
    private readonly QbAgentOptions _options;
    private readonly ILogger<LiveQuickBooksGateway> _logger;

    public LiveQuickBooksGateway(
        IQbConnectionFactory connections,
        IQbXmlAuditLog audit,
        IOptions<QbAgentOptions> options,
        ILogger<LiveQuickBooksGateway> logger)
    {
        _connections = connections;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    public QbHealth CheckHealth(CancellationToken ct)
    {
        try
        {
            using var conn = _connections.Create();
            var info = conn.Open();
            return new QbHealth
            {
                QbReachable = true,
                CompanyFileOpen = true,
                CompanyFilePath = info.CompanyFilePath,
                SdkVersion = info.SdkVersion,
                QbVersion = info.QbVersion,
            };
        }
        catch (QbAgentException ex)
        {
            // Health never throws for "not reachable" — it reports it.
            _logger.LogWarning(ex, "QuickBooks health probe failed.");
            return new QbHealth
            {
                QbReachable = false,
                CompanyFileOpen = false,
                CompanyFilePath = _options.ConnectToOpenFile ? null : _options.CompanyFilePath,
                Detail = ex.Message,
            };
        }
    }

    public IReadOnlyList<CustomerDto> QueryCustomers(DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var requestXml = CustomerMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, listId: null);
        var (status, customers) = Run("CustomerQuery", requestXml, CustomerMapper.ParseQueryResponse);
        ThrowIfError(status);
        return customers;
    }

    public CustomerDto? GetCustomer(string listId, CancellationToken ct)
    {
        var requestXml = CustomerMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince: null, listId);
        var (status, customers) = Run("CustomerQuery", requestXml, CustomerMapper.ParseQueryResponse);

        // statusCode 1 (no matching records) is a warning meaning "not found" — not an error.
        if (status.IsNoMatchingRecords)
        {
            return null;
        }

        ThrowIfError(status);
        return customers.FirstOrDefault(c => c.ListId == listId);
    }

    public CustomerDto AddCustomer(CreateCustomerRequest request, CancellationToken ct)
    {
        var requestXml = CustomerMapper.BuildAddRequest(_options.QbXmlVersion, request);
        var (status, customer) = Run("CustomerAdd", requestXml, CustomerMapper.ParseAddResponse);
        ThrowIfError(status);

        return customer ?? throw new QbAgentException(QbErrorCode.Internal, 500,
            "QuickBooks reported success but returned no CustomerRet.", status);
    }

    public IReadOnlyList<ItemDto> QueryItems(DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var requestXml = ItemMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, listId: null);
        var (status, items) = Run("ItemQuery", requestXml, ItemMapper.ParseQueryResponse);
        ThrowIfError(status);
        return items;
    }

    public ItemDto? GetItem(string listId, CancellationToken ct)
    {
        var requestXml = ItemMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince: null, listId);
        var (status, items) = Run("ItemQuery", requestXml, ItemMapper.ParseQueryResponse);

        if (status.IsNoMatchingRecords)
        {
            return null;
        }

        ThrowIfError(status);
        return items.FirstOrDefault(i => i.ListId == listId);
    }

    public ItemDto AddItem(CreateItemRequest request, CancellationToken ct)
    {
        var requestXml = ItemMapper.BuildAddRequest(_options.QbXmlVersion, request);
        var (status, item) = Run("ItemAdd", requestXml, xml => ItemMapper.ParseAddResponse(xml, request.Type));
        ThrowIfError(status);

        return item ?? throw new QbAgentException(QbErrorCode.Internal, 500,
            "QuickBooks reported success but returned no item.", status);
    }

    public IReadOnlyList<JournalEntryDto> QueryJournalEntries(DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var requestXml = JournalEntryMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, txnId: null);
        var (status, entries) = Run("JournalEntryQuery", requestXml, JournalEntryMapper.ParseQueryResponse);
        ThrowIfError(status);
        return entries;
    }

    public JournalEntryDto? GetJournalEntry(string txnId, CancellationToken ct)
    {
        var requestXml = JournalEntryMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince: null, txnId);
        var (status, entries) = Run("JournalEntryQuery", requestXml, JournalEntryMapper.ParseQueryResponse);

        if (status.IsNoMatchingRecords)
        {
            return null;
        }

        ThrowIfError(status);
        return entries.FirstOrDefault(e => e.TxnId == txnId);
    }

    public JournalEntryDto AddJournalEntry(CreateJournalEntryRequest request, CancellationToken ct)
    {
        var requestXml = JournalEntryMapper.BuildAddRequest(_options.QbXmlVersion, request);
        var (status, entry) = Run("JournalEntryAdd", requestXml, JournalEntryMapper.ParseAddResponse);
        ThrowIfError(status);

        return entry ?? throw new QbAgentException(QbErrorCode.Internal, 500,
            "QuickBooks reported success but returned no journal entry.", status);
    }

    /// <summary>
    /// Open a session, submit the request, parse the response — auditing the exact qbXML exchange
    /// exactly once (in <c>finally</c>) whether it succeeds, fails, or throws. The caller decides how
    /// to act on the returned <see cref="QbStatus"/>.
    /// </summary>
    private (QbStatus Status, TResult Result) Run<TResult>(
        string operation, string requestXml, Func<string, (QbStatus, TResult)> parse)
    {
        string? responseXml = null;
        QbStatus? status = null;
        Exception? error = null;
        try
        {
            using var conn = _connections.Create();
            conn.Open();
            responseXml = conn.ProcessRequest(requestXml);
            var (parsedStatus, result) = parse(responseXml);
            status = parsedStatus;
            return (parsedStatus, result);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            _audit.Record(operation, requestXml, responseXml, status, error);
        }
    }

    private static void ThrowIfError(QbStatus status)
    {
        if (status.IsError)
        {
            throw QbAgentException.QbRequestFailed(status);
        }
    }
}
