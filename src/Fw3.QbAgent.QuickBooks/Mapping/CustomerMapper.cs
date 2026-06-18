using System.Globalization;
using System.Xml.Linq;
using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Contracts;

namespace Fw3.QbAgent.QuickBooks.Mapping;

/// <summary>
/// The one place that knows the qbXML wire format for customers. It builds CustomerQueryRq /
/// CustomerAddRq request strings and parses CustomerQueryRs / CustomerAddRs responses into DTOs.
/// Both the Fixture gateway and the Live gateway use this, so there is a single mapping codepath.
/// </summary>
public static class CustomerMapper
{
    public const string QueryResponseElement = "CustomerQueryRs";
    public const string AddResponseElement = "CustomerAddRs";

    /// <summary>
    /// Build a CustomerQueryRq. qbXML's schema forbids combining a ListID filter with the
    /// modified-date filter, so we branch: a single-record lookup OR a (possibly filtered) list query.
    /// </summary>
    public static string BuildQueryRequest(string qbXmlVersion, DateTimeOffset? updatedSince, string? listId)
    {
        var rq = new XElement("CustomerQueryRq", new XAttribute("requestID", "1"));

        if (!string.IsNullOrWhiteSpace(listId))
        {
            rq.Add(new XElement("ListID", listId));
        }
        else
        {
            rq.Add(new XElement("ActiveStatus", "All"));
            if (updatedSince is { } since)
            {
                rq.Add(new XElement("FromModifiedDate", FormatQbDateTime(since)));
            }
        }

        return QbXml.BuildRequest(qbXmlVersion, rq);
    }

    /// <summary>Build a CustomerAddRq. Element order follows the qbXML CustomerAdd schema sequence.</summary>
    public static string BuildAddRequest(string qbXmlVersion, CreateCustomerRequest request)
    {
        var add = new XElement("CustomerAdd", new XElement("Name", request.Name));

        AddIfPresent(add, "CompanyName", request.CompanyName);
        AddIfPresent(add, "FirstName", request.FirstName);
        AddIfPresent(add, "LastName", request.LastName);
        AddIfPresent(add, "Phone", request.Phone);
        AddIfPresent(add, "Email", request.Email);

        var rq = new XElement("CustomerAddRq",
            new XAttribute("requestID", "1"),
            add);

        return QbXml.BuildRequest(qbXmlVersion, rq);
    }

    public static (QbStatus Status, IReadOnlyList<CustomerDto> Customers) ParseQueryResponse(string responseXml)
    {
        var rs = QbXml.GetResponseElement(responseXml, QueryResponseElement);
        var status = ParseStatus(rs);

        var customers = rs.Elements("CustomerRet")
            .Select(ParseCustomerRet)
            .ToList();

        return (status, customers);
    }

    public static (QbStatus Status, CustomerDto? Customer) ParseAddResponse(string responseXml)
    {
        var rs = QbXml.GetResponseElement(responseXml, AddResponseElement);
        var status = ParseStatus(rs);

        var ret = rs.Element("CustomerRet");
        return (status, ret is null ? null : ParseCustomerRet(ret));
    }

    /// <summary>Read the statusCode / statusSeverity / statusMessage attributes QuickBooks attaches to every response.</summary>
    public static QbStatus ParseStatus(XElement responseElement)
    {
        var code = int.TryParse(responseElement.Attribute("statusCode")?.Value, out var c) ? c : -1;
        var severity = responseElement.Attribute("statusSeverity")?.Value ?? "Error";
        var message = responseElement.Attribute("statusMessage")?.Value ?? "(no status message)";
        return new QbStatus(code, severity, message);
    }

    public static CustomerDto ParseCustomerRet(XElement ret) => new()
    {
        ListId = ret.ChildOrNull("ListID") ?? throw new FormatException("CustomerRet is missing ListID."),
        EditSequence = ret.ChildOrNull("EditSequence") ?? throw new FormatException("CustomerRet is missing EditSequence."),
        Name = ret.ChildOrNull("Name") ?? "",
        FullName = ret.ChildOrNull("FullName"),
        IsActive = !string.Equals(ret.ChildOrNull("IsActive"), "false", StringComparison.OrdinalIgnoreCase),
        CompanyName = ret.ChildOrNull("CompanyName"),
        FirstName = ret.ChildOrNull("FirstName"),
        LastName = ret.ChildOrNull("LastName"),
        Phone = ret.ChildOrNull("Phone"),
        Email = ret.ChildOrNull("Email"),
        TimeCreated = ParseQbDateTime(ret.ChildOrNull("TimeCreated")),
        TimeModified = ParseQbDateTime(ret.ChildOrNull("TimeModified")),
    };

    private static void AddIfPresent(XElement parent, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parent.Add(new XElement(name, value));
        }
    }

    // qbXML datetimes are ISO 8601, usually with an offset (e.g. 2024-01-15T10:30:00-08:00).
    private static string FormatQbDateTime(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseQbDateTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
            ? dto
            : null;
}
