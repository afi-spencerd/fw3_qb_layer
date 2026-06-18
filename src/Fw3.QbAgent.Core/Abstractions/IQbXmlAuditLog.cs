namespace Fw3.QbAgent.Core.Abstractions;

/// <summary>
/// Records every qbXML request/response pair to a durable, rotating log. This is the audit trail we
/// reconcile against when QuickBooks and the ERP disagree, so it must capture the exact wire content.
/// </summary>
public interface IQbXmlAuditLog
{
    void Record(string operation, string requestXml, string? responseXml, QbStatus? status, Exception? error = null);
}

/// <summary>Default no-op sink (used by tests and when auditing is intentionally disabled).</summary>
public sealed class NullQbXmlAuditLog : IQbXmlAuditLog
{
    public static NullQbXmlAuditLog Instance { get; } = new();

    public void Record(string operation, string requestXml, string? responseXml, QbStatus? status, Exception? error = null)
    {
    }
}
