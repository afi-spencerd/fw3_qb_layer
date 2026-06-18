using System.Xml.Linq;
using Fw3.QbAgent.Core.Contracts;
using Fw3.QbAgent.QuickBooks.Mapping;

namespace Fw3.QbAgent.Tests;

public class CustomerMapperTests
{
    private const string Version = "16.0";

    [Fact]
    public void BuildAddRequest_emits_qbxml_with_version_and_ordered_fields()
    {
        var request = new CreateCustomerRequest
        {
            Name = "Acme",
            CompanyName = "Acme Inc.",
            FirstName = "Pat",
            LastName = "Lee",
            Phone = "555-0101",
            Email = "pat@acme.example",
        };

        var xml = CustomerMapper.BuildAddRequest(Version, request);

        Assert.Contains("<?qbxml version=\"16.0\"?>", xml);

        var doc = XDocument.Parse(xml);
        var add = doc.Descendants("CustomerAdd").Single();

        Assert.Equal("Acme", add.Element("Name")!.Value);
        Assert.Equal("Acme Inc.", add.Element("CompanyName")!.Value);
        Assert.Equal("pat@acme.example", add.Element("Email")!.Value);

        // qbXML is order-sensitive: CompanyName precedes FirstName, Phone precedes Email.
        var names = add.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(names.IndexOf("CompanyName") < names.IndexOf("FirstName"));
        Assert.True(names.IndexOf("Phone") < names.IndexOf("Email"));
    }

    [Fact]
    public void BuildAddRequest_omits_empty_optional_fields()
    {
        var xml = CustomerMapper.BuildAddRequest(Version, new CreateCustomerRequest { Name = "Solo" });
        var add = XDocument.Parse(xml).Descendants("CustomerAdd").Single();

        Assert.Equal("Solo", add.Element("Name")!.Value);
        Assert.Null(add.Element("CompanyName"));
        Assert.Null(add.Element("Email"));
    }

    [Fact]
    public void BuildQueryRequest_with_updatedSince_uses_modified_date_filter()
    {
        var since = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var xml = CustomerMapper.BuildQueryRequest(Version, since, listId: null);
        var rq = XDocument.Parse(xml).Descendants("CustomerQueryRq").Single();

        Assert.Equal("All", rq.Element("ActiveStatus")!.Value);
        Assert.NotNull(rq.Element("FromModifiedDate"));
        Assert.Null(rq.Element("ListID"));
    }

    [Fact]
    public void BuildQueryRequest_with_listId_does_not_combine_with_date_filter()
    {
        var xml = CustomerMapper.BuildQueryRequest(Version, updatedSince: null, listId: "80000001-1500000001");
        var rq = XDocument.Parse(xml).Descendants("CustomerQueryRq").Single();

        Assert.Equal("80000001-1500000001", rq.Element("ListID")!.Value);
        Assert.Null(rq.Element("FromModifiedDate"));
    }

    [Fact]
    public void ParseQueryResponse_reads_customers_from_fixture()
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "CustomerQueryRs.xml"));

        var (status, customers) = CustomerMapper.ParseQueryResponse(xml);

        Assert.False(status.IsError);
        Assert.Equal(2, customers.Count);

        var acme = customers.Single(c => c.Name == "Acme Manufacturing");
        Assert.Equal("80000001-1500000001", acme.ListId);
        Assert.Equal("1581550000", acme.EditSequence);
        Assert.Equal("ap@acme.example", acme.Email);
        Assert.NotNull(acme.TimeModified);
    }

    [Fact]
    public void ParseAddResponse_reads_new_ids()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <?qbxml version="16.0"?>
            <QBXML><QBXMLMsgsRs>
              <CustomerAddRs requestID="1" statusCode="0" statusSeverity="Info" statusMessage="Status OK">
                <CustomerRet>
                  <ListID>80000010-1600000000</ListID>
                  <EditSequence>1600000000</EditSequence>
                  <Name>New Co</Name>
                  <IsActive>true</IsActive>
                </CustomerRet>
              </CustomerAddRs>
            </QBXMLMsgsRs></QBXML>
            """;

        var (status, customer) = CustomerMapper.ParseAddResponse(xml);

        Assert.False(status.IsError);
        Assert.NotNull(customer);
        Assert.Equal("80000010-1600000000", customer!.ListId);
        Assert.Equal("1600000000", customer.EditSequence);
    }

    [Fact]
    public void ParseStatus_flags_quickbooks_errors()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <QBXML><QBXMLMsgsRs>
              <CustomerAddRs requestID="1" statusCode="3100" statusSeverity="Error"
                             statusMessage="The name &quot;New Co&quot; of the list element is already in use." />
            </QBXMLMsgsRs></QBXML>
            """;

        var (status, customer) = CustomerMapper.ParseAddResponse(xml);

        Assert.True(status.IsError);
        Assert.Equal(3100, status.Code);
        Assert.Null(customer);
    }
}
