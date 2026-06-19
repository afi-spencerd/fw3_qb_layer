using System.Xml.Linq;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.Core.Errors;
using Fw3.QbAgent.QuickBooks.Mapping;

namespace Fw3.QbAgent.Tests;

public class ItemMapperTests
{
    private const string Version = "16.0";

    [Fact]
    public void BuildAddRequest_inventory_emits_ordered_accounts()
    {
        var req = new CreateItemRequest
        {
            Type = ItemType.Inventory,
            Name = "Linalool",
            ParentFullName = "Raw Materials:Aroma Chemicals",
            PurchaseCost = 42.50m,
            IncomeAccountFullName = "Sales:Material Resale",
            CogsOrExpenseAccountFullName = "Cost of Goods Sold:COGS - Raw Materials",
            AssetAccountFullName = "Inventory Asset:Raw Materials Inventory",
        };

        var xml = ItemMapper.BuildAddRequest(Version, req);
        var add = XDocument.Parse(xml).Descendants("ItemInventoryAdd").Single();

        Assert.Equal("Linalool", add.Element("Name")!.Value);
        Assert.Equal("Raw Materials:Aroma Chemicals", add.Element("ParentRef")!.Element("FullName")!.Value);
        Assert.Equal("Inventory Asset:Raw Materials Inventory", add.Element("AssetAccountRef")!.Element("FullName")!.Value);

        // Schema order: IncomeAccountRef -> COGSAccountRef -> AssetAccountRef.
        var names = add.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(names.IndexOf("IncomeAccountRef") < names.IndexOf("COGSAccountRef"));
        Assert.True(names.IndexOf("COGSAccountRef") < names.IndexOf("AssetAccountRef"));
    }

    [Fact]
    public void BuildAddRequest_inventory_requires_all_three_accounts()
    {
        var req = new CreateItemRequest
        {
            Type = ItemType.Inventory,
            Name = "Missing Accounts",
            IncomeAccountFullName = "Sales:Fragrance Sales",
            // no COGS, no asset
        };

        var ex = Assert.Throws<QbAgentException>(() => ItemMapper.BuildAddRequest(Version, req));
        Assert.Equal(QbErrorCode.Validation, ex.Code);
    }

    [Fact]
    public void BuildAddRequest_noninventory_with_both_accounts_uses_SalesAndPurchase()
    {
        var req = new CreateItemRequest
        {
            Type = ItemType.NonInventory,
            Name = "Resold Supply",
            IncomeAccountFullName = "Sales:Material Resale",
            CogsOrExpenseAccountFullName = "Cost of Goods Sold:Supplies",
        };

        var add = XDocument.Parse(ItemMapper.BuildAddRequest(Version, req)).Descendants("ItemNonInventoryAdd").Single();
        var sap = add.Element("SalesAndPurchase")!;

        Assert.Equal("Sales:Material Resale", sap.Element("IncomeAccountRef")!.Element("FullName")!.Value);
        Assert.Equal("Cost of Goods Sold:Supplies", sap.Element("ExpenseAccountRef")!.Element("FullName")!.Value);
    }

    [Fact]
    public void BuildAddRequest_noninventory_with_expense_only_uses_SalesOrPurchase()
    {
        var req = new CreateItemRequest
        {
            Type = ItemType.NonInventory,
            Name = "Inbound Freight",
            PurchaseDescription = "Freight on raw materials",
            CogsOrExpenseAccountFullName = "Cost of Goods Sold:Freight In",
        };

        var add = XDocument.Parse(ItemMapper.BuildAddRequest(Version, req)).Descendants("ItemNonInventoryAdd").Single();
        var sop = add.Element("SalesOrPurchase")!;

        Assert.Equal("Cost of Goods Sold:Freight In", sop.Element("AccountRef")!.Element("FullName")!.Value);
    }

    [Fact]
    public void BuildQueryRequest_with_iterator_sets_attribute_and_maxReturned_before_filters()
    {
        var xml = ItemMapper.BuildQueryRequest(Version, updatedSince: null, listId: null,
            maxReturned: 500, iterator: IteratorMode.Start, iteratorId: null);
        var rq = XDocument.Parse(xml).Descendants("ItemQueryRq").Single();

        Assert.Equal("Start", rq.Attribute("iterator")!.Value);
        Assert.Equal("500", rq.Element("MaxReturned")!.Value);

        // qbXML schema: MaxReturned must precede ActiveStatus.
        var names = rq.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(names.IndexOf("MaxReturned") < names.IndexOf("ActiveStatus"));
    }

    [Fact]
    public void BuildQueryRequest_continue_carries_iteratorId()
    {
        var xml = ItemMapper.BuildQueryRequest(Version, updatedSince: null, listId: null,
            maxReturned: 500, iterator: IteratorMode.Continue, iteratorId: "{abc-123}");
        var rq = XDocument.Parse(xml).Descendants("ItemQueryRq").Single();

        Assert.Equal("Continue", rq.Attribute("iterator")!.Value);
        Assert.Equal("{abc-123}", rq.Attribute("iteratorID")!.Value);
    }

    [Fact]
    public void ParseQueryResponse_reads_mixed_item_types_from_fixture()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "ItemQueryRs.xml"));

        var (status, items) = ItemMapper.ParseQueryResponse(xml);

        Assert.False(status.IsError);
        Assert.Equal(4, items.Count);

        var rawMaterial = items.Single(i => i.Name == "Linalool");
        Assert.Equal("Inventory", rawMaterial.Type);
        Assert.Equal("Inventory Asset:Raw Materials Inventory", rawMaterial.AssetAccountFullName);
        Assert.Equal(320m, rawMaterial.QuantityOnHand);
        Assert.Equal("Raw Materials:Aroma Chemicals", rawMaterial.ParentFullName);

        var freight = items.Single(i => i.Name == "Inbound Freight");
        Assert.Equal("NonInventory", freight.Type);
        Assert.Equal("Cost of Goods Sold:Freight In", freight.CogsOrExpenseAccountFullName);
        Assert.Null(freight.QuantityOnHand);
    }
}
