using System.Xml.Linq;

namespace Fw3.QbAgent.QuickBooks.Mapping;

/// <summary>
/// qbXML envelope helpers. qbXML is plain (no XML namespace) and is wrapped in a
/// <c>&lt;?qbxml version="X"?&gt;</c> processing instruction that tells QuickBooks which schema to use.
/// </summary>
public static class QbXml
{
    /// <summary>
    /// Wrap a request element (e.g. CustomerAddRq) in the full QBXML envelope and return it as a
    /// string ready for the request processor. This exact string is what we log for audit.
    /// </summary>
    public static string BuildRequest(string qbXmlVersion, XElement requestElement, string onError = "stopOnError")
    {
        var doc = new XDocument(
            new XProcessingInstruction("qbxml", $"version=\"{qbXmlVersion}\""),
            new XElement("QBXML",
                new XElement("QBXMLMsgsRq",
                    new XAttribute("onError", onError),
                    requestElement)));

        // XDocument.ToString() emits the processing instruction and body but not the XML declaration,
        // so we prepend it. (StringWriter would force a utf-16 declaration, which is misleading here.)
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + doc;
    }

    /// <summary>Parse a qbXML response and return the single response element (e.g. CustomerQueryRs).</summary>
    public static XElement GetResponseElement(string responseXml, string responseElementName)
    {
        var doc = XDocument.Parse(responseXml);
        var element = doc.Root?
            .Element("QBXMLMsgsRs")?
            .Element(responseElementName);

        return element
            ?? throw new FormatException($"qbXML response did not contain a <{responseElementName}> element.");
    }

    /// <summary>Optional child element text, or null if absent/empty.</summary>
    public static string? ChildOrNull(this XElement element, string name)
    {
        var value = element.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
