using System.Xml.Linq;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.QuickBooks.Mapping;

namespace Fw3.QbAgent.Tests;

public class JournalEntryMapperTests
{
    private const string Version = "16.0";

    private static CreateJournalEntryRequest WipMovement() => new()
    {
        TxnDate = new DateOnly(2026, 6, 19),
        RefNumber = "WO-10042",
        Lines =
        [
            new CreateJournalLine { Type = JournalLineType.Debit, AccountFullName = "Inventory Asset:WIP Inventory", Amount = 1250.00m, Memo = "RM issued" },
            new CreateJournalLine { Type = JournalLineType.Credit, AccountFullName = "Inventory Asset:Raw Materials Inventory", Amount = 1250.00m },
        ],
    };

    [Fact]
    public void BuildAddRequest_emits_debit_then_credit_lines_with_accounts_and_amounts()
    {
        var xml = JournalEntryMapper.BuildAddRequest(Version, WipMovement());
        var add = XDocument.Parse(xml).Descendants("JournalEntryAdd").Single();

        Assert.Equal("2026-06-19", add.Element("TxnDate")!.Value);
        Assert.Equal("WO-10042", add.Element("RefNumber")!.Value);

        var debit = add.Element("JournalDebitLine")!;
        Assert.Equal("Inventory Asset:WIP Inventory", debit.Element("AccountRef")!.Element("FullName")!.Value);
        Assert.Equal("1250.00", debit.Element("Amount")!.Value);

        var credit = add.Element("JournalCreditLine")!;
        Assert.Equal("Inventory Asset:Raw Materials Inventory", credit.Element("AccountRef")!.Element("FullName")!.Value);
    }

    [Fact]
    public void BuildAddRequest_rejects_nonpositive_amounts()
    {
        var req = new CreateJournalEntryRequest
        {
            TxnDate = new DateOnly(2026, 6, 19),
            Lines = [new CreateJournalLine { Type = JournalLineType.Debit, AccountFullName = "Some:Account", Amount = -5m }],
        };

        var ex = Assert.Throws<QbAgentException>(() => JournalEntryMapper.BuildAddRequest(Version, req));
        Assert.Equal(QbErrorCode.Validation, ex.Code);
    }

    [Fact]
    public void BuildAddRequest_rejects_empty_lines()
    {
        var req = new CreateJournalEntryRequest { TxnDate = new DateOnly(2026, 6, 19), Lines = [] };
        Assert.Throws<QbAgentException>(() => JournalEntryMapper.BuildAddRequest(Version, req));
    }

    [Fact]
    public void BuildQueryRequest_includes_line_items()
    {
        var xml = JournalEntryMapper.BuildQueryRequest(Version, updatedSince: null, txnId: "2F1A-1718800000");
        var rq = XDocument.Parse(xml).Descendants("JournalEntryQueryRq").Single();

        Assert.Equal("2F1A-1718800000", rq.Element("TxnID")!.Value);
        Assert.Equal("true", rq.Element("IncludeLineItems")!.Value);
    }

    [Fact]
    public void ParseQueryResponse_reads_entries_and_lines_from_fixture()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "JournalEntryQueryRs.xml"));

        var (status, entries) = JournalEntryMapper.ParseQueryResponse(xml);

        Assert.False(status.IsError);
        Assert.Equal(2, entries.Count);

        var wip = entries.Single(e => e.TxnId == "2F1A-1718800000");
        Assert.Equal("WO-10042", wip.RefNumber);
        Assert.Equal(2, wip.Lines.Count);

        var debit = wip.Lines.Single(l => l.Type == JournalLineType.Debit);
        Assert.Equal("Inventory Asset:WIP Inventory", debit.AccountFullName);
        Assert.Equal(1250.00m, debit.Amount);

        Assert.Contains(wip.Lines, l => l.Type == JournalLineType.Credit && l.AccountFullName == "Inventory Asset:Raw Materials Inventory");
    }
}
