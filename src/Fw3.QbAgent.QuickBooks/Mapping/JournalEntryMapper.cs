using System.Globalization;
using System.Xml.Linq;
using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;

namespace Fw3.QbAgent.QuickBooks.Mapping;

/// <summary>
/// The qbXML wire format for journal entries. Builds JournalEntryQueryRq / JournalEntryAddRq and parses
/// the responses. Validation here is purely structural (a line needs an account, a positive amount, and
/// a side); whether debits equal credits is QuickBooks' call, and its verdict is surfaced — the agent
/// embeds no accounting rules.
/// </summary>
public static class JournalEntryMapper
{
    public const string QueryResponseElement = "JournalEntryQueryRs";
    public const string AddResponseElement = "JournalEntryAddRs";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string BuildQueryRequest(string qbXmlVersion, DateTimeOffset? updatedSince, string? txnId,
        int? maxReturned = null, IteratorMode iterator = IteratorMode.None, string? iteratorId = null)
    {
        var rq = new XElement("JournalEntryQueryRq", new XAttribute("requestID", "1"));

        if (!string.IsNullOrWhiteSpace(txnId))
        {
            rq.Add(new XElement("TxnID", txnId));
        }
        else
        {
            QbXml.ApplyIterator(rq, iterator, iteratorId);
            if (maxReturned is { } max)
            {
                rq.Add(new XElement("MaxReturned", max.ToString(Inv)));
            }

            if (updatedSince is { } since)
            {
                rq.Add(new XElement("ModifiedDateRangeFilter",
                    new XElement("FromModifiedDate", since.ToString("yyyy-MM-ddTHH:mm:ssK", Inv))));
            }
        }

        // Without this, QuickBooks returns the entry header but no debit/credit lines.
        rq.Add(new XElement("IncludeLineItems", "true"));

        return QbXml.BuildRequest(qbXmlVersion, rq);
    }

    public static string BuildAddRequest(string qbXmlVersion, CreateJournalEntryRequest request)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw QbAgentException.Validation("A journal entry requires at least one line.");
        }

        var add = new XElement("JournalEntryAdd",
            new XElement("TxnDate", request.TxnDate.ToString("yyyy-MM-dd", Inv)));

        if (!string.IsNullOrWhiteSpace(request.RefNumber))
        {
            add.Add(new XElement("RefNumber", request.RefNumber));
        }

        if (request.IsAdjustment)
        {
            add.Add(new XElement("IsAdjustment", "true"));
        }

        // Emit all debit lines, then all credit lines (a valid, conventional ordering for qbXML).
        foreach (var line in request.Lines.Where(l => l.Type == JournalLineType.Debit))
        {
            add.Add(BuildLine("JournalDebitLine", line));
        }

        foreach (var line in request.Lines.Where(l => l.Type == JournalLineType.Credit))
        {
            add.Add(BuildLine("JournalCreditLine", line));
        }

        return QbXml.BuildRequest(qbXmlVersion, new XElement("JournalEntryAddRq", new XAttribute("requestID", "1"), add));
    }

    public static (QbStatus Status, IReadOnlyList<JournalEntryDto> Entries) ParseQueryResponse(string responseXml)
    {
        var rs = QbXml.GetResponseElement(responseXml, QueryResponseElement);
        var status = CustomerMapper.ParseStatus(rs);

        var entries = rs.Elements("JournalEntryRet").Select(ParseJournalEntryRet).ToList();
        return (status, entries);
    }

    public static (QbStatus Status, JournalEntryDto? Entry) ParseAddResponse(string responseXml)
    {
        var rs = QbXml.GetResponseElement(responseXml, AddResponseElement);
        var status = CustomerMapper.ParseStatus(rs);

        var ret = rs.Element("JournalEntryRet");
        return (status, ret is null ? null : ParseJournalEntryRet(ret));
    }

    public static JournalEntryDto ParseJournalEntryRet(XElement ret)
    {
        var lines = new List<JournalEntryLineDto>();
        lines.AddRange(ret.Elements("JournalDebitLine").Select(l => ParseLine(l, JournalLineType.Debit)));
        lines.AddRange(ret.Elements("JournalCreditLine").Select(l => ParseLine(l, JournalLineType.Credit)));

        return new JournalEntryDto
        {
            TxnId = ret.ChildOrNull("TxnID") ?? throw new FormatException("JournalEntryRet is missing TxnID."),
            EditSequence = ret.ChildOrNull("EditSequence") ?? throw new FormatException("JournalEntryRet is missing EditSequence."),
            TxnNumber = int.TryParse(ret.ChildOrNull("TxnNumber"), NumberStyles.Integer, Inv, out var n) ? n : null,
            TxnDate = ParseDateOnly(ret.ChildOrNull("TxnDate")),
            RefNumber = ret.ChildOrNull("RefNumber"),
            IsAdjustment = string.Equals(ret.ChildOrNull("IsAdjustment"), "true", StringComparison.OrdinalIgnoreCase),
            Lines = lines,
            TimeCreated = ParseDate(ret.ChildOrNull("TimeCreated")),
            TimeModified = ParseDate(ret.ChildOrNull("TimeModified")),
        };
    }

    private static XElement BuildLine(string elementName, CreateJournalLine line)
    {
        if (string.IsNullOrWhiteSpace(line.AccountFullName))
        {
            throw QbAgentException.Validation("Each journal line requires accountFullName.");
        }

        if (line.Amount <= 0)
        {
            throw QbAgentException.Validation(
                "Each journal line amount must be positive; use the line type (Debit/Credit) to convey direction.");
        }

        var el = new XElement(elementName,
            new XElement("AccountRef", new XElement("FullName", line.AccountFullName)),
            new XElement("Amount", line.Amount.ToString("0.00", Inv)));

        if (!string.IsNullOrWhiteSpace(line.Memo))
        {
            el.Add(new XElement("Memo", line.Memo));
        }

        if (!string.IsNullOrWhiteSpace(line.EntityFullName))
        {
            el.Add(new XElement("EntityRef", new XElement("FullName", line.EntityFullName)));
        }

        return el;
    }

    private static JournalEntryLineDto ParseLine(XElement line, JournalLineType type) => new()
    {
        TxnLineId = line.ChildOrNull("TxnLineID"),
        Type = type,
        AccountFullName = line.Element("AccountRef")?.ChildOrNull("FullName"),
        Amount = decimal.TryParse(line.ChildOrNull("Amount"), NumberStyles.Any, Inv, out var a) ? a : 0m,
        Memo = line.ChildOrNull("Memo"),
        EntityFullName = line.Element("EntityRef")?.ChildOrNull("FullName"),
    };

    private static DateOnly ParseDateOnly(string? value) =>
        DateOnly.TryParse(value, Inv, DateTimeStyles.None, out var d) ? d : default;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, Inv, DateTimeStyles.None, out var dto) ? dto : null;
}
