namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// A QuickBooks item as the ERP sees it. Covers the fields shared across item types; accounts are
/// passed through as QuickBooks <i>full names</i> (e.g. "Inventory Asset:Raw Materials"). The ERP owns
/// the mapping between its records and these ListIDs, and decides which accounts an item uses.
/// </summary>
public sealed record ItemDto
{
    public required string ListId { get; init; }
    public required string EditSequence { get; init; }

    /// <summary>QuickBooks item type as a string ("Inventory", "NonInventory", "Service", "InventoryAssembly", ...).</summary>
    public required string Type { get; init; }

    public required string Name { get; init; }

    /// <summary>Fully qualified name including any parent, e.g. "Raw Materials:Aroma Chemicals:Linalool".</summary>
    public string? FullName { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>Parent item's full name when this is a sub-item; null for top-level items.</summary>
    public string? ParentFullName { get; init; }

    public string? ManufacturerPartNumber { get; init; }
    public string? SalesDescription { get; init; }
    public string? PurchaseDescription { get; init; }

    public decimal? SalesPrice { get; init; }
    public decimal? PurchaseCost { get; init; }

    public string? IncomeAccountFullName { get; init; }

    /// <summary>COGS account for inventory items, or the expense account for purchased non-inventory items.</summary>
    public string? CogsOrExpenseAccountFullName { get; init; }

    /// <summary>Inventory-asset account; inventory items only.</summary>
    public string? AssetAccountFullName { get; init; }

    /// <summary>Quantity on hand; inventory items only (read-only — QuickBooks computes it).</summary>
    public decimal? QuantityOnHand { get; init; }

    /// <summary>Average cost; inventory items only (read-only).</summary>
    public decimal? AverageCost { get; init; }

    public decimal? ReorderPoint { get; init; }

    public DateTimeOffset? TimeCreated { get; init; }
    public DateTimeOffset? TimeModified { get; init; }
}
