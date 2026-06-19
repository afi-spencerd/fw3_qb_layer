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

    /// <summary>
    /// Apply a qbXML iterator to a query request element. Iterators let a large result set be fetched
    /// in chunks; they are valid only within a single QuickBooks session, so the whole drain must happen
    /// inside one open session.
    /// </summary>
    public static void ApplyIterator(XElement requestElement, IteratorMode mode, string? iteratorId)
    {
        switch (mode)
        {
            case IteratorMode.Start:
                requestElement.SetAttributeValue("iterator", "Start");
                break;
            case IteratorMode.Continue:
                requestElement.SetAttributeValue("iterator", "Continue");
                if (!string.IsNullOrEmpty(iteratorId))
                {
                    requestElement.SetAttributeValue("iteratorID", iteratorId);
                }

                break;
            case IteratorMode.None:
            default:
                break;
        }
    }

    /// <summary>Read the iterator id and remaining-count QuickBooks puts on a query response element.</summary>
    public static (string? IteratorId, int RemainingCount) ReadIterator(XElement responseElement)
    {
        var id = responseElement.Attribute("iteratorID")?.Value;
        var remaining = int.TryParse(responseElement.Attribute("iteratorRemainingCount")?.Value, out var r) ? r : 0;
        return (id, remaining);
    }
}

/// <summary>qbXML query iterator state for a single drain.</summary>
public enum IteratorMode
{
    /// <summary>No iterator (a one-shot query, optionally capped by MaxReturned).</summary>
    None,

    /// <summary>Begin a new iterated query.</summary>
    Start,

    /// <summary>Continue an iterated query using a prior iteratorID.</summary>
    Continue,
}
