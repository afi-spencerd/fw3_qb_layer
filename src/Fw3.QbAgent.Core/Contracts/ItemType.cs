namespace Fw3.QbAgent.Core.Contracts;

/// <summary>
/// The QuickBooks item types the agent can <b>create</b> in this slice. Reads can return any type
/// QuickBooks holds (surfaced as a string on <see cref="ItemDto.Type"/>); writes are scoped to these.
/// </summary>
public enum ItemType
{
    /// <summary>Inventory Part: QuickBooks tracks quantity on hand and value. Requires income, COGS, and asset accounts.</summary>
    Inventory,

    /// <summary>Non-inventory Part: not quantity-tracked. Carries an income and/or expense account.</summary>
    NonInventory,
}
