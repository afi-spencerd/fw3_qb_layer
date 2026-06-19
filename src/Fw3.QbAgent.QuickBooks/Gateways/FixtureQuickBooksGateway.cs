using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Configuration;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.QuickBooks.Mapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fw3.QbAgent.QuickBooks.Gateways;

/// <summary>
/// A QuickBooks stand-in that requires no SDK and no live company file. It exercises the real qbXML
/// build/parse mapping (so the translation layer is genuinely tested) but reads responses from
/// fixtures and synthesizes write responses. This is what runs in CI and on dev boxes without QB.
/// </summary>
public sealed class FixtureQuickBooksGateway : IQuickBooksGateway
{
    private readonly QbAgentOptions _options;
    private readonly IQbXmlAuditLog _audit;
    private readonly ILogger<FixtureQuickBooksGateway> _logger;

    public FixtureQuickBooksGateway(IOptions<QbAgentOptions> options, IQbXmlAuditLog audit, ILogger<FixtureQuickBooksGateway> logger)
    {
        _options = options.Value;
        _audit = audit;
        _logger = logger;
    }

    public QbHealth CheckHealth(CancellationToken ct) => new()
    {
        QbReachable = true,
        CompanyFileOpen = true,
        CompanyFilePath = string.IsNullOrWhiteSpace(_options.CompanyFilePath) ? "(fixture)" : _options.CompanyFilePath,
        QbVersion = "Fixture",
        SdkVersion = "Fixture",
        Detail = "Fixture mode: responses are simulated; no live QuickBooks is contacted.",
    };

    public IReadOnlyList<CustomerDto> QueryCustomers(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct)
    {
        var request = CustomerMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, listId: null, maxReturned);
        var responseXml = LoadFixture("CustomerQueryRs.xml");
        _audit.Record("CustomerQuery", request, responseXml, null);

        var (status, customers) = CustomerMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        // Simulate QuickBooks' FromModifiedDate filter against the fixture data.
        if (updatedSince is { } since)
        {
            customers = customers.Where(c => c.TimeModified is null || c.TimeModified >= since).ToList();
        }

        return Cap(customers, maxReturned);
    }

    public CustomerDto? GetCustomer(string listId, CancellationToken ct)
    {
        var request = CustomerMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince: null, listId);
        var responseXml = LoadFixture("CustomerQueryRs.xml");
        _audit.Record("CustomerQuery", request, responseXml, null);

        var (status, customers) = CustomerMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        return customers.FirstOrDefault(c => c.ListId == listId);
    }

    public CustomerDto AddCustomer(CreateCustomerRequest request, CancellationToken ct)
    {
        var requestXml = CustomerMapper.BuildAddRequest(_options.QbXmlVersion, request);

        // Synthesize a realistic CustomerAddRs so the response parse path is exercised end-to-end.
        var responseXml = SynthesizeAddResponse(request);
        _audit.Record("CustomerAdd", requestXml, responseXml, null);

        var (status, customer) = CustomerMapper.ParseAddResponse(responseXml);
        ThrowIfError(status);

        return customer ?? throw new QbAgentException(QbErrorCode.Internal, 500,
            "Fixture CustomerAdd produced no CustomerRet.");
    }

    public IReadOnlyList<ItemDto> QueryItems(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct)
    {
        var request = ItemMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, listId: null, maxReturned);
        var responseXml = LoadFixture("ItemQueryRs.xml");
        _audit.Record("ItemQuery", request, responseXml, null);

        var (status, items) = ItemMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        if (updatedSince is { } since)
        {
            items = items.Where(i => i.TimeModified is null || i.TimeModified >= since).ToList();
        }

        return Cap(items, maxReturned);
    }

    public ItemDto? GetItem(string listId, CancellationToken ct)
    {
        var request = ItemMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince: null, listId);
        var responseXml = LoadFixture("ItemQueryRs.xml");
        _audit.Record("ItemQuery", request, responseXml, null);

        var (status, items) = ItemMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        return items.FirstOrDefault(i => i.ListId == listId);
    }

    public ItemDto AddItem(CreateItemRequest request, CancellationToken ct)
    {
        var requestXml = ItemMapper.BuildAddRequest(_options.QbXmlVersion, request);
        var responseXml = SynthesizeItemAddResponse(request);
        _audit.Record("ItemAdd", requestXml, responseXml, null);

        var (status, item) = ItemMapper.ParseAddResponse(responseXml, request.Type);
        ThrowIfError(status);

        return item ?? throw new QbAgentException(QbErrorCode.Internal, 500, "Fixture ItemAdd produced no item.");
    }

    public IReadOnlyList<JournalEntryDto> QueryJournalEntries(DateTimeOffset? updatedSince, int? maxReturned, CancellationToken ct)
    {
        var request = JournalEntryMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, txnId: null, maxReturned);
        var responseXml = LoadFixture("JournalEntryQueryRs.xml");
        _audit.Record("JournalEntryQuery", request, responseXml, null);

        var (status, entries) = JournalEntryMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        if (updatedSince is { } since)
        {
            entries = entries.Where(e => e.TimeModified is null || e.TimeModified >= since).ToList();
        }

        return Cap(entries, maxReturned);
    }

    public JournalEntryDto? GetJournalEntry(string txnId, CancellationToken ct)
    {
        var request = JournalEntryMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince: null, txnId);
        var responseXml = LoadFixture("JournalEntryQueryRs.xml");
        _audit.Record("JournalEntryQuery", request, responseXml, null);

        var (status, entries) = JournalEntryMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        return entries.FirstOrDefault(e => e.TxnId == txnId);
    }

    public JournalEntryDto AddJournalEntry(CreateJournalEntryRequest request, CancellationToken ct)
    {
        var requestXml = JournalEntryMapper.BuildAddRequest(_options.QbXmlVersion, request);
        var responseXml = SynthesizeJournalEntryAddResponse(request);
        _audit.Record("JournalEntryAdd", requestXml, responseXml, null);

        var (status, entry) = JournalEntryMapper.ParseAddResponse(responseXml);
        ThrowIfError(status);

        return entry ?? throw new QbAgentException(QbErrorCode.Internal, 500, "Fixture JournalEntryAdd produced no entry.");
    }

    private static void ThrowIfError(QbStatus status)
    {
        if (status.IsError)
        {
            throw QbAgentException.QbRequestFailed(status);
        }
    }

    private static IReadOnlyList<T> Cap<T>(IReadOnlyList<T> list, int? maxReturned) =>
        maxReturned is { } max && list.Count > max ? list.Take(max).ToList() : list;

    private string LoadFixture(string fileName)
    {
        // Resolve a relative FixturesPath against the app base directory so it works whether the agent
        // runs from its bin folder, as a Windows Service, or under the test host.
        var baseDir = Path.IsPathRooted(_options.FixturesPath)
            ? _options.FixturesPath
            : Path.Combine(AppContext.BaseDirectory, _options.FixturesPath);

        var path = Path.Combine(baseDir, fileName);
        if (!File.Exists(path))
        {
            throw QbAgentException.Unreachable(
                $"Fixture '{fileName}' not found at '{path}'. Set QbAgent:FixturesPath to the fixtures directory.");
        }

        return File.ReadAllText(path);
    }

    private string SynthesizeAddResponse(CreateCustomerRequest request)
    {
        var now = DateTimeOffset.Now;
        var listId = SynthesizeListId(request.Name);

        var ret = new XElement("CustomerRet",
            new XElement("ListID", listId),
            new XElement("TimeCreated", now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
            new XElement("TimeModified", now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
            new XElement("EditSequence", "1"),
            new XElement("Name", request.Name),
            new XElement("FullName", request.Name),
            new XElement("IsActive", "true"));

        AddIfPresent(ret, "CompanyName", request.CompanyName);
        AddIfPresent(ret, "FirstName", request.FirstName);
        AddIfPresent(ret, "LastName", request.LastName);
        AddIfPresent(ret, "Phone", request.Phone);
        AddIfPresent(ret, "Email", request.Email);

        var doc = new XDocument(
            new XProcessingInstruction("qbxml", $"version=\"{_options.QbXmlVersion}\""),
            new XElement("QBXML",
                new XElement("QBXMLMsgsRs",
                    new XElement("CustomerAddRs",
                        new XAttribute("requestID", "1"),
                        new XAttribute("statusCode", "0"),
                        new XAttribute("statusSeverity", "Info"),
                        new XAttribute("statusMessage", "Status OK"),
                        ret))));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + doc;
    }

    // Deterministic pseudo-ListID so repeated fixture runs for the same name are stable in tests.
    private static string SynthesizeListId(string name)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));
        return $"80000000-{hash[..10]}";
    }

    private static void AddIfPresent(XElement parent, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parent.Add(new XElement(name, value));
        }
    }

    private string SynthesizeItemAddResponse(CreateItemRequest request)
    {
        var now = DateTimeOffset.Now;
        var listId = SynthesizeListId(request.Name);
        var fullName = string.IsNullOrWhiteSpace(request.ParentFullName)
            ? request.Name
            : $"{request.ParentFullName}:{request.Name}";

        var (rsElement, ret) = request.Type == ItemType.Inventory
            ? ("ItemInventoryAddRs", BuildInventoryRet(request, listId, fullName, now))
            : ("ItemNonInventoryAddRs", BuildNonInventoryRet(request, listId, fullName, now));

        var doc = new XDocument(
            new XProcessingInstruction("qbxml", $"version=\"{_options.QbXmlVersion}\""),
            new XElement("QBXML",
                new XElement("QBXMLMsgsRs",
                    new XElement(rsElement,
                        new XAttribute("requestID", "1"),
                        new XAttribute("statusCode", "0"),
                        new XAttribute("statusSeverity", "Info"),
                        new XAttribute("statusMessage", "Status OK"),
                        ret))));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + doc;
    }

    private static XElement BuildInventoryRet(CreateItemRequest r, string listId, string fullName, DateTimeOffset now)
    {
        var ret = NewRetHeader("ItemInventoryRet", r, listId, fullName, now);
        AddIfPresent(ret, "SalesDesc", r.SalesDescription);
        AddDecimal(ret, "SalesPrice", r.SalesPrice);
        ret.Add(AccountRefEl("IncomeAccountRef", r.IncomeAccountFullName));
        AddIfPresent(ret, "PurchaseDesc", r.PurchaseDescription);
        AddDecimal(ret, "PurchaseCost", r.PurchaseCost);
        ret.Add(AccountRefEl("COGSAccountRef", r.CogsOrExpenseAccountFullName));
        ret.Add(AccountRefEl("AssetAccountRef", r.AssetAccountFullName));
        ret.Add(new XElement("QuantityOnHand", (r.OpeningBalance?.QuantityOnHand ?? 0m).ToString(CultureInfo.InvariantCulture)));
        return ret;
    }

    private static XElement BuildNonInventoryRet(CreateItemRequest r, string listId, string fullName, DateTimeOffset now)
    {
        var ret = NewRetHeader("ItemNonInventoryRet", r, listId, fullName, now);
        var hasIncome = !string.IsNullOrWhiteSpace(r.IncomeAccountFullName);
        var hasExpense = !string.IsNullOrWhiteSpace(r.CogsOrExpenseAccountFullName);

        if (hasIncome && hasExpense)
        {
            var sp = new XElement("SalesAndPurchase");
            AddIfPresent(sp, "SalesDesc", r.SalesDescription);
            AddDecimal(sp, "SalesPrice", r.SalesPrice);
            sp.Add(AccountRefEl("IncomeAccountRef", r.IncomeAccountFullName));
            AddIfPresent(sp, "PurchaseDesc", r.PurchaseDescription);
            AddDecimal(sp, "PurchaseCost", r.PurchaseCost);
            sp.Add(AccountRefEl("ExpenseAccountRef", r.CogsOrExpenseAccountFullName));
            ret.Add(sp);
        }
        else
        {
            var sop = new XElement("SalesOrPurchase");
            AddIfPresent(sop, "Desc", hasIncome ? r.SalesDescription : r.PurchaseDescription);
            AddDecimal(sop, "Price", hasIncome ? r.SalesPrice : r.PurchaseCost);
            sop.Add(AccountRefEl("AccountRef", hasIncome ? r.IncomeAccountFullName : r.CogsOrExpenseAccountFullName));
            ret.Add(sop);
        }

        return ret;
    }

    private static XElement NewRetHeader(string retName, CreateItemRequest r, string listId, string fullName, DateTimeOffset now)
    {
        var ret = new XElement(retName,
            new XElement("ListID", listId),
            new XElement("TimeCreated", now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
            new XElement("TimeModified", now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
            new XElement("EditSequence", "1"),
            new XElement("Name", r.Name),
            new XElement("FullName", fullName),
            new XElement("IsActive", r.IsActive ? "true" : "false"));
        AddIfPresent(ret, "ManufacturerPartNumber", r.ManufacturerPartNumber);
        return ret;
    }

    private static void AddDecimal(XElement parent, string name, decimal? value)
    {
        if (value is { } v)
        {
            parent.Add(new XElement(name, v.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static XElement AccountRefEl(string name, string? fullName) =>
        new(name, new XElement("FullName", fullName ?? ""));

    private string SynthesizeJournalEntryAddResponse(CreateJournalEntryRequest r)
    {
        var now = DateTimeOffset.Now;
        var ret = new XElement("JournalEntryRet",
            new XElement("TxnID", SynthesizeTxnId(r)),
            new XElement("TimeCreated", now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
            new XElement("TimeModified", now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
            new XElement("EditSequence", "1"),
            new XElement("TxnNumber", "1001"),
            new XElement("TxnDate", r.TxnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        AddIfPresent(ret, "RefNumber", r.RefNumber);
        if (r.IsAdjustment)
        {
            ret.Add(new XElement("IsAdjustment", "true"));
        }

        var lineNo = 0;
        foreach (var line in r.Lines.Where(l => l.Type == JournalLineType.Debit))
        {
            ret.Add(BuildSynthLine("JournalDebitLine", line, ++lineNo));
        }

        foreach (var line in r.Lines.Where(l => l.Type == JournalLineType.Credit))
        {
            ret.Add(BuildSynthLine("JournalCreditLine", line, ++lineNo));
        }

        var doc = new XDocument(
            new XProcessingInstruction("qbxml", $"version=\"{_options.QbXmlVersion}\""),
            new XElement("QBXML",
                new XElement("QBXMLMsgsRs",
                    new XElement("JournalEntryAddRs",
                        new XAttribute("requestID", "1"),
                        new XAttribute("statusCode", "0"),
                        new XAttribute("statusSeverity", "Info"),
                        new XAttribute("statusMessage", "Status OK"),
                        ret))));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + doc;
    }

    private static XElement BuildSynthLine(string elementName, CreateJournalLine line, int lineNo)
    {
        var el = new XElement(elementName,
            new XElement("TxnLineID", lineNo.ToString(CultureInfo.InvariantCulture)),
            new XElement("AccountRef", new XElement("FullName", line.AccountFullName)),
            new XElement("Amount", line.Amount.ToString("0.00", CultureInfo.InvariantCulture)));
        AddIfPresent(el, "Memo", line.Memo);
        if (!string.IsNullOrWhiteSpace(line.EntityFullName))
        {
            el.Add(new XElement("EntityRef", new XElement("FullName", line.EntityFullName)));
        }

        return el;
    }

    private static string SynthesizeTxnId(CreateJournalEntryRequest r)
    {
        var first = r.Lines.Count > 0 ? r.Lines[0] : null;
        var basis = $"{r.RefNumber}|{r.TxnDate:yyyy-MM-dd}|{r.Lines.Count}|{first?.AccountFullName}|{first?.Amount}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(basis)));
        return $"JE-{hash[..12]}";
    }
}
