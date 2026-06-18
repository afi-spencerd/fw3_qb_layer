using Fw3.QbAgent.Core.Abstractions;
using Serilog;
using Serilog.Core;

namespace Fw3.QbAgent.Host.Logging;

/// <summary>
/// Writes the qbXML audit trail to its own daily-rolling file, separate from the application log.
/// Every request/response pair is captured verbatim — this is the record we reconcile against when
/// QuickBooks and the ERP disagree.
/// </summary>
public sealed class SerilogQbXmlAuditLog : IQbXmlAuditLog, IDisposable
{
    private readonly Logger _log;

    public SerilogQbXmlAuditLog(string directory)
    {
        Directory.CreateDirectory(directory);
        _log = new LoggerConfiguration()
            .WriteTo.File(
                path: Path.Combine(directory, "qbxml-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                shared: true,
                outputTemplate: "{Message:lj}{NewLine}")
            .CreateLogger();
    }

    public void Record(string operation, string requestXml, string? responseXml, QbStatus? status, Exception? error = null)
    {
        var outcome = error is not null
            ? $"EXCEPTION {error.GetType().Name}: {error.Message}"
            : status is not null
                ? $"{status.Severity} (statusCode {status.Code}): {status.Message}"
                : "(no status)";

        // One self-delimiting block per exchange, timestamped, with the exact wire content.
        _log.Information(
            "===== {Timestamp:o} | {Operation} | {Outcome} =====\n--- REQUEST ---\n{Request}\n--- RESPONSE ---\n{Response}\n",
            DateTimeOffset.Now,
            operation,
            outcome,
            requestXml,
            responseXml ?? "(no response)");
    }

    public void Dispose() => _log.Dispose();
}
