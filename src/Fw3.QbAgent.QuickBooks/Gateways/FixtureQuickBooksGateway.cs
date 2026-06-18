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

    public IReadOnlyList<CustomerDto> QueryCustomers(DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var request = CustomerMapper.BuildQueryRequest(_options.QbXmlVersion, updatedSince, listId: null);
        var responseXml = LoadFixture("CustomerQueryRs.xml");
        _audit.Record("CustomerQuery", request, responseXml, null);

        var (status, customers) = CustomerMapper.ParseQueryResponse(responseXml);
        ThrowIfError(status);

        // Simulate QuickBooks' FromModifiedDate filter against the fixture data.
        if (updatedSince is { } since)
        {
            customers = customers.Where(c => c.TimeModified is null || c.TimeModified >= since).ToList();
        }

        return customers;
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

    private static void ThrowIfError(QbStatus status)
    {
        if (status.IsError)
        {
            throw QbAgentException.QbRequestFailed(status);
        }
    }

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
}
