using System.ComponentModel.DataAnnotations;

namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// Request body for <c>POST /journal-entries</c>. The ERP supplies the complete, balanced set of
/// debit/credit lines; the agent does not compute, infer, or rebalance them. QuickBooks is the
/// authority on accounting validity (e.g. debits must equal credits) and the agent surfaces its
/// verdict — it does not embed accounting rules of its own.
/// </summary>
public sealed record CreateJournalEntryRequest
{
    [Required]
    public required DateOnly TxnDate { get; init; }

    /// <summary>Reference number — typically the ERP's posting/batch id, useful for reconciliation.</summary>
    [MaxLength(20)]
    public string? RefNumber { get; init; }

    /// <summary>Flag the entry as an adjustment (e.g. for period-end inventory/COGS adjustments).</summary>
    public bool IsAdjustment { get; init; }

    [Required]
    [MinLength(1)]
    public required IReadOnlyList<CreateJournalLine> Lines { get; init; }
}

/// <summary>One line of a journal entry to create.</summary>
public sealed record CreateJournalLine
{
    [Required]
    public required JournalLineType Type { get; init; }

    /// <summary>GL account full name, e.g. "Inventory Asset:WIP Inventory".</summary>
    [Required(AllowEmptyStrings = false)]
    public required string AccountFullName { get; init; }

    /// <summary>Positive amount of the line. Debit vs credit is conveyed by <see cref="Type"/>, not the sign.</summary>
    [Required]
    public required decimal Amount { get; init; }

    public string? Memo { get; init; }

    /// <summary>Optional name (customer/vendor/employee) to associate, by full name.</summary>
    public string? EntityFullName { get; init; }
}
