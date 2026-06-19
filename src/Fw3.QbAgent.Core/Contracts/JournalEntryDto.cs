namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// A QuickBooks journal entry as the ERP sees it. Journal entries are <b>transactions</b>, so their
/// identity is a <see cref="TxnId"/> (not a ListID), with <see cref="EditSequence"/> for concurrency.
/// </summary>
public sealed record JournalEntryDto
{
    public required string TxnId { get; init; }
    public required string EditSequence { get; init; }

    /// <summary>QuickBooks' sequential transaction number (distinct from TxnId).</summary>
    public int? TxnNumber { get; init; }

    public required DateOnly TxnDate { get; init; }

    /// <summary>Reference number — typically the ERP's posting/batch identifier.</summary>
    public string? RefNumber { get; init; }

    public bool IsAdjustment { get; init; }

    public required IReadOnlyList<JournalEntryLineDto> Lines { get; init; }

    public DateTimeOffset? TimeCreated { get; init; }
    public DateTimeOffset? TimeModified { get; init; }
}

/// <summary>One debit or credit line of a journal entry.</summary>
public sealed record JournalEntryLineDto
{
    /// <summary>QuickBooks' per-line identifier (assigned on create; needed for line-level edits).</summary>
    public string? TxnLineId { get; init; }

    public required JournalLineType Type { get; init; }

    /// <summary>GL account this line posts to, by QuickBooks full name.</summary>
    public string? AccountFullName { get; init; }

    public required decimal Amount { get; init; }

    public string? Memo { get; init; }

    /// <summary>Optional name (customer/vendor/employee) the line is associated with, by full name.</summary>
    public string? EntityFullName { get; init; }
}
