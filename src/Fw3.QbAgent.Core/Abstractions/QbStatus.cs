namespace Fw3.QbAgent.Core.Abstractions;

/// <summary>
/// The status QuickBooks reports for every qbXML response (statusCode / statusSeverity /
/// statusMessage). We never swallow this — it is the difference between a real success and a
/// silent data problem.
/// </summary>
public sealed record QbStatus(int Code, string Severity, string Message)
{
    public bool IsError => string.Equals(Severity, "Error", StringComparison.OrdinalIgnoreCase);

    public bool IsWarning => string.Equals(Severity, "Warn", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// QuickBooks returns statusCode 1 ("A query request did not find a matching object") as a
    /// warning, not an error. That means "empty result", which is a normal outcome for a query.
    /// </summary>
    public bool IsNoMatchingRecords => Code == 1;

    public static QbStatus Ok { get; } = new(0, "Info", "Status OK");
}
