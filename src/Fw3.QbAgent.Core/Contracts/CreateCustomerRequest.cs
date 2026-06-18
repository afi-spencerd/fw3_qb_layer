using System.ComponentModel.DataAnnotations;

namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// Request body for <c>POST /customers</c>. Intentionally a thin, flat shape — the agent does not
/// validate business meaning, only that the request is structurally usable as a qbXML CustomerAdd.
/// </summary>
public sealed record CreateCustomerRequest
{
    /// <summary>
    /// QuickBooks customer Name. Required by QuickBooks and must be unique within the customer list.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(41)] // QuickBooks Name field limit
    public required string Name { get; init; }

    [MaxLength(41)]
    public string? CompanyName { get; init; }

    [MaxLength(25)]
    public string? FirstName { get; init; }

    [MaxLength(25)]
    public string? LastName { get; init; }

    [MaxLength(21)]
    public string? Phone { get; init; }

    [MaxLength(1023)]
    public string? Email { get; init; }
}
