using System.ComponentModel.DataAnnotations;

namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// Request body for <c>POST /items</c>. The ERP states the item type and the accounts; the agent maps
/// it to the matching qbXML add request. It does not infer type or accounts from the name.
/// </summary>
public sealed record CreateItemRequest
{
    [Required]
    public required ItemType Type { get; init; }

    /// <summary>QuickBooks item Name; unique within its level. Max 31 chars (QuickBooks limit).</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(31)]
    public required string Name { get; init; }

    /// <summary>Full name of the parent item to nest under (creates a sub-item), e.g. "Raw Materials:Aroma Chemicals".</summary>
    public string? ParentFullName { get; init; }

    public bool IsActive { get; init; } = true;

    [MaxLength(31)]
    public string? ManufacturerPartNumber { get; init; }

    public string? SalesDescription { get; init; }
    public string? PurchaseDescription { get; init; }

    public decimal? SalesPrice { get; init; }
    public decimal? PurchaseCost { get; init; }

    /// <summary>Income account full name. Required for Inventory; optional for NonInventory (if sold).</summary>
    public string? IncomeAccountFullName { get; init; }

    /// <summary>COGS account (Inventory) or expense account (purchased NonInventory), by full name.</summary>
    public string? CogsOrExpenseAccountFullName { get; init; }

    /// <summary>Inventory-asset account full name. Required for Inventory items.</summary>
    public string? AssetAccountFullName { get; init; }

    public decimal? ReorderPoint { get; init; }

    /// <summary>
    /// Optional opening inventory balance (Inventory items only). Setting this makes QuickBooks record
    /// an inventory adjustment. Prefer leaving it null and letting the ERP post inventory movements as
    /// transactions, so there is one system of record for stock.
    /// </summary>
    public ItemOpeningBalance? OpeningBalance { get; init; }
}

/// <summary>Opening on-hand quantity and total value as of a date, for an inventory item.</summary>
public sealed record ItemOpeningBalance
{
    public required decimal QuantityOnHand { get; init; }
    public required decimal TotalValue { get; init; }
    public required DateTimeOffset AsOf { get; init; }
}
