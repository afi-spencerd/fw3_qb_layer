namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// A QuickBooks customer as the ERP sees it: clean JSON, no qbXML, no COM.
/// <para>
/// <see cref="ListId"/> and <see cref="EditSequence"/> are QuickBooks-owned identity/concurrency
/// tokens. The agent passes them through faithfully; the ERP owns the mapping between its own
/// record IDs and these values.
/// </para>
/// </summary>
public sealed record CustomerDto
{
    /// <summary>Permanent QuickBooks list identifier. Stable across edits.</summary>
    public required string ListId { get; init; }

    /// <summary>
    /// Optimistic-concurrency token. Must be echoed back on a future update so QuickBooks can
    /// detect a stale write.
    /// </summary>
    public required string EditSequence { get; init; }

    /// <summary>The customer's QuickBooks "Name" (unique within the list).</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified name including any parent (job) hierarchy, e.g. "Acme:Project X".</summary>
    public string? FullName { get; init; }

    public bool IsActive { get; init; } = true;

    public string? CompanyName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }

    /// <summary>When QuickBooks created the record (UTC-normalized from the qbXML timestamp).</summary>
    public DateTimeOffset? TimeCreated { get; init; }

    /// <summary>When QuickBooks last modified the record.</summary>
    public DateTimeOffset? TimeModified { get; init; }
}
