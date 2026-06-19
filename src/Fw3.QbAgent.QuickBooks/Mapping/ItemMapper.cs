using System.Globalization;
using System.Xml.Linq;
using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;

namespace Fw3.QbAgent.QuickBooks.Mapping;

/// <summary>
/// The qbXML wire format for items. Builds ItemQueryRq and the type-specific add requests
/// (ItemInventoryAddRq / ItemNonInventoryAddRq), and parses the heterogeneous ItemQueryRs (which can
/// contain ItemInventoryRet, ItemNonInventoryRet, ItemServiceRet, ...) plus add responses.
/// Element order in the add requests follows the qbXML schema sequence — wrong order is rejected.
/// </summary>
public static class ItemMapper
{
    public const string QueryResponseElement = "ItemQueryRs";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string BuildQueryRequest(string qbXmlVersion, DateTimeOffset? updatedSince, string? listId,
        int? maxReturned = null, IteratorMode iterator = IteratorMode.None, string? iteratorId = null)
    {
        var rq = new XElement("ItemQueryRq", new XAttribute("requestID", "1"));

        if (!string.IsNullOrWhiteSpace(listId))
        {
            rq.Add(new XElement("ListID", listId));
        }
        else
        {
            QbXml.ApplyIterator(rq, iterator, iteratorId);
            if (maxReturned is { } max)
            {
                rq.Add(new XElement("MaxReturned", max.ToString(Inv)));
            }

            rq.Add(new XElement("ActiveStatus", "All"));
            if (updatedSince is { } since)
            {
                rq.Add(new XElement("FromModifiedDate", since.ToString("yyyy-MM-ddTHH:mm:ssK", Inv)));
            }
        }

        return QbXml.BuildRequest(qbXmlVersion, rq);
    }

    /// <summary>Build the add request matching the requested item type, validating type-required accounts up front.</summary>
    public static string BuildAddRequest(string qbXmlVersion, CreateItemRequest request) => request.Type switch
    {
        ItemType.Inventory => BuildInventoryAdd(qbXmlVersion, request),
        ItemType.NonInventory => BuildNonInventoryAdd(qbXmlVersion, request),
        _ => throw QbAgentException.Validation($"Unsupported item type '{request.Type}'."),
    };

    /// <summary>The qbXML add-response element name for a given create request type.</summary>
    public static string AddResponseElement(ItemType type) => type switch
    {
        ItemType.Inventory => "ItemInventoryAddRs",
        ItemType.NonInventory => "ItemNonInventoryAddRs",
        _ => throw QbAgentException.Validation($"Unsupported item type '{type}'."),
    };

    private static string BuildInventoryAdd(string qbXmlVersion, CreateItemRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.IncomeAccountFullName)
            || string.IsNullOrWhiteSpace(r.CogsOrExpenseAccountFullName)
            || string.IsNullOrWhiteSpace(r.AssetAccountFullName))
        {
            throw QbAgentException.Validation(
                "Inventory items require incomeAccountFullName, cogsOrExpenseAccountFullName, and assetAccountFullName.");
        }

        // Order matters: Name, IsActive, ParentRef, ManufacturerPartNumber, SalesDesc, SalesPrice,
        // IncomeAccountRef, PurchaseDesc, PurchaseCost, COGSAccountRef, AssetAccountRef, ReorderPoint,
        // then opening balance (QuantityOnHand, TotalValue, InventoryDate).
        var add = new XElement("ItemInventoryAdd", new XElement("Name", r.Name));
        AddBool(add, "IsActive", r.IsActive);
        AddParentRef(add, r.ParentFullName);
        AddText(add, "ManufacturerPartNumber", r.ManufacturerPartNumber);
        AddText(add, "SalesDesc", r.SalesDescription);
        AddDecimal(add, "SalesPrice", r.SalesPrice);
        add.Add(AccountRef("IncomeAccountRef", r.IncomeAccountFullName!));
        AddText(add, "PurchaseDesc", r.PurchaseDescription);
        AddDecimal(add, "PurchaseCost", r.PurchaseCost);
        add.Add(AccountRef("COGSAccountRef", r.CogsOrExpenseAccountFullName!));
        add.Add(AccountRef("AssetAccountRef", r.AssetAccountFullName!));
        AddDecimal(add, "ReorderPoint", r.ReorderPoint);

        if (r.OpeningBalance is { } ob)
        {
            AddDecimal(add, "QuantityOnHand", ob.QuantityOnHand);
            AddDecimal(add, "TotalValue", ob.TotalValue);
            add.Add(new XElement("InventoryDate", ob.AsOf.ToString("yyyy-MM-dd", Inv)));
        }

        return QbXml.BuildRequest(qbXmlVersion, new XElement("ItemInventoryAddRq", new XAttribute("requestID", "1"), add));
    }

    private static string BuildNonInventoryAdd(string qbXmlVersion, CreateItemRequest r)
    {
        var add = new XElement("ItemNonInventoryAdd", new XElement("Name", r.Name));
        AddBool(add, "IsActive", r.IsActive);
        AddParentRef(add, r.ParentFullName);
        AddText(add, "ManufacturerPartNumber", r.ManufacturerPartNumber);

        var hasIncome = !string.IsNullOrWhiteSpace(r.IncomeAccountFullName);
        var hasExpense = !string.IsNullOrWhiteSpace(r.CogsOrExpenseAccountFullName);

        if (hasIncome && hasExpense)
        {
            // Bought and sold: SalesAndPurchase carries both sides.
            var sp = new XElement("SalesAndPurchase");
            AddText(sp, "SalesDesc", r.SalesDescription);
            AddDecimal(sp, "SalesPrice", r.SalesPrice);
            sp.Add(AccountRef("IncomeAccountRef", r.IncomeAccountFullName!));
            AddText(sp, "PurchaseDesc", r.PurchaseDescription);
            AddDecimal(sp, "PurchaseCost", r.PurchaseCost);
            sp.Add(AccountRef("ExpenseAccountRef", r.CogsOrExpenseAccountFullName!));
            add.Add(sp);
        }
        else if (hasIncome || hasExpense)
        {
            // One side only: SalesOrPurchase with a single account.
            var account = hasIncome ? r.IncomeAccountFullName! : r.CogsOrExpenseAccountFullName!;
            var desc = hasIncome ? r.SalesDescription : r.PurchaseDescription;
            var price = hasIncome ? r.SalesPrice : r.PurchaseCost;

            var sop = new XElement("SalesOrPurchase");
            AddText(sop, "Desc", desc);
            AddDecimal(sop, "Price", price);
            sop.Add(AccountRef("AccountRef", account));
            add.Add(sop);
        }
        else
        {
            throw QbAgentException.Validation(
                "Non-inventory items require at least one of incomeAccountFullName or cogsOrExpenseAccountFullName.");
        }

        return QbXml.BuildRequest(qbXmlVersion, new XElement("ItemNonInventoryAddRq", new XAttribute("requestID", "1"), add));
    }

    public static (QbStatus Status, IReadOnlyList<ItemDto> Items) ParseQueryResponse(string responseXml)
    {
        var rs = QbXml.GetResponseElement(responseXml, QueryResponseElement);
        var status = CustomerMapper.ParseStatus(rs);

        var items = rs.Elements()
            .Where(e => e.Name.LocalName.StartsWith("Item", StringComparison.Ordinal)
                        && e.Name.LocalName.EndsWith("Ret", StringComparison.Ordinal))
            .Select(ParseItemRet)
            .ToList();

        return (status, items);
    }

    public static (QbStatus Status, ItemDto? Item) ParseAddResponse(string responseXml, ItemType type)
    {
        var rs = QbXml.GetResponseElement(responseXml, AddResponseElement(type));
        var status = CustomerMapper.ParseStatus(rs);

        var ret = rs.Elements().FirstOrDefault(e =>
            e.Name.LocalName.StartsWith("Item", StringComparison.Ordinal)
            && e.Name.LocalName.EndsWith("Ret", StringComparison.Ordinal));

        return (status, ret is null ? null : ParseItemRet(ret));
    }

    /// <summary>
    /// Map any Item*Ret element to the common DTO. Accounts/prices live directly on inventory items but
    /// inside SalesOrPurchase / SalesAndPurchase on non-inventory and service items, so we read leniently
    /// via descendants.
    /// </summary>
    public static ItemDto ParseItemRet(XElement ret) => new()
    {
        ListId = ret.ChildOrNull("ListID") ?? throw new FormatException($"{ret.Name.LocalName} is missing ListID."),
        EditSequence = ret.ChildOrNull("EditSequence") ?? throw new FormatException($"{ret.Name.LocalName} is missing EditSequence."),
        Type = TypeFromRetName(ret.Name.LocalName),
        Name = ret.ChildOrNull("Name") ?? "",
        FullName = ret.ChildOrNull("FullName"),
        IsActive = !string.Equals(ret.ChildOrNull("IsActive"), "false", StringComparison.OrdinalIgnoreCase),
        ParentFullName = ret.Element("ParentRef")?.ChildOrNull("FullName"),
        ManufacturerPartNumber = ret.ChildOrNull("ManufacturerPartNumber"),
        SalesDescription = ret.ChildOrNull("SalesDesc") ?? DescendantValue(ret, "SalesDesc") ?? DescendantValue(ret, "Desc"),
        PurchaseDescription = ret.ChildOrNull("PurchaseDesc") ?? DescendantValue(ret, "PurchaseDesc"),
        SalesPrice = ParseDecimal(DescendantValue(ret, "SalesPrice") ?? DescendantValue(ret, "Price")),
        PurchaseCost = ParseDecimal(DescendantValue(ret, "PurchaseCost")),
        IncomeAccountFullName = AccountFullName(ret, "IncomeAccountRef"),
        CogsOrExpenseAccountFullName =
            AccountFullName(ret, "COGSAccountRef")
            ?? AccountFullName(ret, "ExpenseAccountRef")
            ?? AccountFullName(ret, "AccountRef"),
        AssetAccountFullName = AccountFullName(ret, "AssetAccountRef"),
        QuantityOnHand = ParseDecimal(ret.ChildOrNull("QuantityOnHand")),
        AverageCost = ParseDecimal(ret.ChildOrNull("AverageCost")),
        ReorderPoint = ParseDecimal(ret.ChildOrNull("ReorderPoint")),
        TimeCreated = ParseDate(ret.ChildOrNull("TimeCreated")),
        TimeModified = ParseDate(ret.ChildOrNull("TimeModified")),
    };

    // ---- helpers ----

    private static string TypeFromRetName(string localName)
    {
        // "ItemInventoryRet" -> "Inventory"; "ItemNonInventoryRet" -> "NonInventory".
        var trimmed = localName;
        if (trimmed.StartsWith("Item", StringComparison.Ordinal)) trimmed = trimmed["Item".Length..];
        if (trimmed.EndsWith("Ret", StringComparison.Ordinal)) trimmed = trimmed[..^"Ret".Length];
        return trimmed;
    }

    private static XElement AccountRef(string elementName, string fullName) =>
        new(elementName, new XElement("FullName", fullName));

    private static string? AccountFullName(XElement ret, string refName) =>
        ret.Descendants(refName).FirstOrDefault()?.ChildOrNull("FullName");

    private static string? DescendantValue(XElement ret, string name)
    {
        var value = ret.Descendants(name).FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void AddText(XElement parent, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parent.Add(new XElement(name, value));
    }

    private static void AddBool(XElement parent, string name, bool value) =>
        parent.Add(new XElement(name, value ? "true" : "false"));

    private static void AddDecimal(XElement parent, string name, decimal? value)
    {
        if (value is { } v) parent.Add(new XElement(name, v.ToString(Inv)));
    }

    private static void AddParentRef(XElement parent, string? parentFullName)
    {
        if (!string.IsNullOrWhiteSpace(parentFullName))
        {
            parent.Add(new XElement("ParentRef", new XElement("FullName", parentFullName)));
        }
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, Inv, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, Inv, DateTimeStyles.None, out var dto) ? dto : null;
}
